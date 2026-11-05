using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed class DungeonChoice
    {
        public string id, label;
        public bool enabled = true;
        public DungeonChoice(string key, string text, bool legal = true) { id = key; label = text; enabled = legal; }
    }

    public sealed partial class BattleWorld
    {
        public ProfileSession Profile { get; private set; }
        public DungeonConfig DungeonConfig { get; private set; }
        public DungeonMap DungeonMap { get; private set; }
        bool expectDungeonProfile = true;
        string dungeonDialog = "", interactingEvent = "", dungeonProfileId = "profile-1";
        string lastDungeonRoom = "", lastDungeonEncounter = "";
        public bool DungeonPresentation => expectDungeonProfile;
        public bool DungeonModal => Profile != null && !Menu && !string.IsNullOrEmpty(dungeonDialog);
        public string DungeonMenuText => StoryText.Load().title + "\n\n三人小队 · 实时行动 · 战术决策\n\n开始后跟随短提示探索。";

        DungeonConfig LoadDungeonConfig()
        {
            var resource = Resources.Load<TextAsset>("DungeonConfig");
            if (resource == null) throw new InvalidOperationException("Missing actual DungeonConfig resource.");
            return CombatJson.Read<DungeonConfig>(resource.text);
        }

        public void InitializeDungeonForTests(string path, string profileId = "test-profile", bool startNew = true, bool useP5 = false)
        {
            legacyTestRules=!useP5; p6RuntimeRules=false;
            IgnoreApplicationFocus = true; SavePath = path; Profile = null; LoadP4Preferences(); IsolateSyntheticInput(); dungeonProfileId = profileId;
            expectDungeonProfile = true; expectPartyProfile = true;
            if (startNew) StartDungeonNew();
        }

        public void StartDungeonNew()
        {
            var previous = Profile;
            var archived = new List<KeyValuePair<string,string>>();
            try
            {
                var config = LoadDungeonConfig();
                string suffix = ".previous-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                foreach (string path in new[] {SavePath, SavePath + ".bak", SavePath + ".tmp"})
                {
                    if (!File.Exists(path)) continue;
                    File.Move(path, path + suffix); archived.Add(new KeyValuePair<string,string>(path, path + suffix));
                }
                var rules=LoadConfig();ulong seed=unchecked((ulong)DateTime.UtcNow.Ticks);
                var candidate = rules.p6!=null?ProfileSession.CreateNewCompatible(dungeonProfileId,seed,rules,LoadP5Config(),LoadLegacyConfig(),config,SavePath):
                    rules.p5==null?ProfileSession.CreateNew(dungeonProfileId,seed,rules,config,SavePath):
                    ProfileSession.CreateNewCompatible(dungeonProfileId,seed,rules,LoadLegacyConfig(),config,SavePath);
                Profile = candidate; DungeonConfig = config;
            }
            catch (Exception error)
            {
                foreach (var pair in archived) if (!File.Exists(pair.Key) && File.Exists(pair.Value)) File.Move(pair.Value, pair.Key);
                Profile = previous; Message = "新档未创建，旧进度保留：" + error.Message; return;
            }
            expectDungeonProfile = true; expectPartyProfile = true; Menu = false; QuickDice = false;
            rollingTicket = null; RollElapsed = 0; SelectedActor = State.activeActorId; SelectedAction = "attack";
            SelectedTarget = null; dungeonDialog = "intro"; interactingEvent = "";
            lastDungeonRoom = ""; lastDungeonEncounter = "";
            ReconcileDungeon(); SynchronizeViews(); Rig.Step(10); ApplyTime(); Hud.RefreshView();
            RebindPresentation();
        }

        void ContinueDungeonSaved()
        {
            try
            {
                var config = LoadDungeonConfig();
                var rules=LoadConfig();
                var restored = rules.p6!=null?ProfileSession.LoadCompatible(SavePath,rules,LoadP5Config(),LoadLegacyConfig(),config):rules.p5==null?ProfileSession.Load(SavePath,rules,config):ProfileSession.LoadCompatible(SavePath,rules,LoadLegacyConfig(),config);
                Profile = restored; DungeonConfig = config;
                expectDungeonProfile = true; expectPartyProfile = true; Menu = false;
                rollingTicket = null; RollElapsed = 0; dungeonDialog = "";
                SelectedActor = State.pendingRoll?.actorId ?? State.commandActorId ?? State.activeActorId;
                SelectedTarget = State.pendingRoll?.targetId ?? State.enemies.FirstOrDefault(e => e.Alive)?.id;
                RestoreDiceAnimationPreference();
                lastDungeonRoom = ""; lastDungeonEncounter = "";
                ReconcileDungeon(); SynchronizeViews(); RestorePresentationSnapshot(); RestoreDungeonDialog();
                RebindPresentation();
                Message = "已恢复同一档案、世界与完整已付结果。"; ApplyTime(); Hud.RefreshView();
            }
            catch (Exception error) { Message = "档案读取失败，原文件保留：" + error.Message; }
        }

        void SetDungeonTerrainVisible(bool enabled)
        {
            if (map != null)
            {
                foreach (Transform child in map)
                    if (child.name == "Ground" || child.name.StartsWith("Grid ", StringComparison.Ordinal) || child.name.EndsWith(" wall", StringComparison.Ordinal) || child.name == "Low cover")
                        child.gameObject.SetActive(!enabled);
                var cutaway = map.GetComponent<FrontWallCutaway>();
                if (cutaway != null) cutaway.enabled = !enabled;
            }
            if (DungeonMap != null) DungeonMap.gameObject.SetActive(enabled);
            PartyNavigation = enabled && DungeonMap != null ? new Func<Vector3,Vector3,Vector3>(DungeonMap.NextWaypoint) : null;
            Physics.SyncTransforms();
        }
    }
}
