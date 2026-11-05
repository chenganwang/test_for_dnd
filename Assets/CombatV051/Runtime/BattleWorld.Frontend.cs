using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public string FrontPage { get; private set; } = "main";
        public bool FrontSettings { get; private set; }
        public bool Training { get; private set; }
        string growthRole = "fighter", frontReturn = "main";
        ProfileSession suspendedTrainingProfile;
        public string FrontSelectedRole => growthRole;
        public bool HasExplorationRun => Profile?.State.challenge != null && Profile.State.challenge.stage != "Settled";
        bool HasHistoricalStory => Profile!=null&&Profile.State.activeMode==ProfileModeKind.Story&&
            (Profile.State.story.encounters.Count>0||Profile.State.story.elapsedRealSeconds>0||Profile.State.story.rooms.Any(r=>r.templateId!="R0"&&r.visited));
        public string LegacyProfilePath => Path.Combine(Application.persistentDataPath, "CombatV051", "P2", "profile-1", "profile.json");
        bool HasP4Save => File.Exists(SavePath) || File.Exists(SavePath + ".bak");
        bool HasLegacySave => File.Exists(LegacyProfilePath) && !HasP4Save;
        bool CanStartExploration => Profile != null && !HasExplorationRun && Profile.IsSafe && Profile.ActiveSafeEvent == null && Profile.PendingReward == null;

        public void InitializeFrontendForTests(string path, bool useP5=false)
        {
            legacyTestRules=!useP5; p6RuntimeRules=false;
            Profile = null; SavePath = path; IgnoreApplicationFocus = true;
            expectDungeonProfile = true; expectPartyProfile = true;
            IsolateSyntheticInput(); InitializeFrontend(); Hud.RefreshView();
        }
        void InitializeFrontend()
        {
            LoadP4Preferences();
            Audio?.Initialize(this,Path.Combine(Path.GetDirectoryName(SavePath),"p3-audio-settings.json"));
            if (HasP4Save) LoadFrontendProfile();
            Menu = true; FrontPage = "main"; FrontSettings = false; ApplyTime();
            if (Profile != null) Message = "冒险记录已找到。";
            else if (!HasP4Save) Message = "";
        }
        bool LoadFrontendProfile()
        {
            if (Profile != null) { EnsureV11FrontendProgress(); return true; }
            if (!HasP4Save) return false;
            ContinueDungeonSaved();
            Menu = true; dungeonDialog = ""; ApplyTime();
            EnsureV11FrontendProgress();
            return Profile != null;
        }
        bool EnsureFrontendProfile()
        {
            if (Profile != null || LoadFrontendProfile()) { EnsureV11FrontendProgress(); return true; }
            if (HasP4Save) return false;
            StartDungeonNew();
            Menu = true; dungeonDialog = ""; ApplyTime();
            EnsureV11FrontendProgress();
            return Profile != null;
        }
        public void FrontendCue(string action)
        {
            PresentationUICue(action=="hover"?"MenuHover":action=="select"?"MenuSelect":action=="back"?"MenuBack":action=="map"?"MapSelect":action=="character"?"CharacterSelect":"MenuConfirm");
        }
        public bool HandleFrontendBack()
        {
            if (!Menu || !DungeonPresentation) return false;
            if (FrontSettings) { CloseFrontendSettings(); return true; }
            if (V11FrontendEnabled && FrontPage == "character-tree") { FrontPage = "characters"; FrontendCue("back"); Hud.RefreshView(); return true; }
            if (FrontPage == "main") FrontPage = "exit";
            else if (FrontPage == "prepare" || FrontPage == "explore-run") FrontPage = "explore";
            else if (FrontPage == "characters" || FrontPage == "growth") FrontPage = frontReturn;
            else if (FrontPage == "legacy" || FrontPage == "credits") FrontPage = "help";
            else FrontPage = "main";
            FrontendCue("back"); Hud.RefreshView(); return true;
        }
        public string FrontTitle => V11FrontTitle ?? (FrontPage == "campaign" ? "战役" : FrontPage == "explore" || FrontPage == "explore-run" ? "探索 · 灰烬遗迹" :
            FrontPage == "prepare" ? "出发准备" : FrontPage == "characters" || FrontPage == "growth" ? "同行者" :
            FrontPage == "exit" ? "离开旅途？" : FrontPage == "legacy" ? "保留原档 · 导入进度" : FrontPage == "credits" ? "制作与许可" : FrontPage == "help" ? "旅者手册" : "遗迹之旅");
        public string FrontBody
        {
            get
            {
                if (V11FrontBody != null) return V11FrontBody;
                if (FrontPage == "main") return "ASHEN RUINS";
                if (FrontPage == "campaign") return "正式剧情开发中\n\n故事与人物的正式设定将由作者填写。";
                if (FrontPage == "exit") return "当前记录仍会保留。";
                if (FrontPage == "credits") return "原创程序化纸纹、地图与角色剪影。\n音乐来源与许可见随附 ASSET_LICENSES.md / Legal。\n角色正式剧情与设定由作者填写。";
                if (FrontPage == "legacy") return "保留原文件，导入到独立档案。\n安全态保留同一结果、奖励与资源。\n旧战斗将恢复整个遭遇开始状态；不能挑选有利结果。";
                if (FrontPage == "help") return "操作与准备可在无收益训练场练习。\n旧档导入会保留原文件。";
                if (FrontPage == "explore") return "一支三人小队，一次深入遗迹的旅途。";
                if (FrontPage == "explore-run") return HasExplorationRun ? "未完成的探索仍在等待。\n继续将保留原队伍、资源与已付结果；不会应用新的准备。" : "当前没有进行中的探索。";
                if (Profile == null) return "记录暂不可读取，原文件已保留。";
                if (FrontPage == "prepare" && p6RuntimeRules) return "誓光大剑 · 圣武士 / 法师 / 游荡者\nQ 至圣斩，F 裂光协助。每次新遭遇至少 2 次，交战自动回充。\n新出发使用此预设；继续已有探索保留其原队伍和资源。";
                if (FrontPage == "prepare")
                    return "三人同行 · 每位选择一项已解锁专精\n初始装备固定。新局采用当前准备，继续旧局保持原配置。\n主控：" + ActorName(Profile.State.meta.lastPreparation.defaultActiveActorId) + "  ·  骰律：" + RuleName(Profile.State.meta.lastPreparation.primaryLawId);
                if (FrontPage == "characters" || FrontPage == "growth")
                    return FrontActorInfo(growthRole) + "\n研究记录  " + Profile.State.meta.research + "  ·  解锁不会自动装备或改变进行中的探索。";
                return "";
            }
        }
        public string FrontActorInfo(string id)
        {
            if(V11FrontendEnabled && V11Catalog.FindCharacter(id)!=null) return V11CharacterInfo(id);
            if(id==PaladinId)return "圣武士 · 誓光大剑\n前线近战 · 大剑\n左键大剑 · 右键架防 · Q至圣斩\n基础1次；骰后命中可再用1次全力。";
            string role = id == "fighter" ? "战士 · 前线守护" : id == "rogue" ? "游荡者 · 侧翼突袭" : "法师 · 奥术控制";
            string equipment = id == "fighter" ? "剑与盾" : id == "rogue" ? "双刃" : "法杖";
            string abilities = p6RuntimeRules&&id=="mage"?"F 裂光协助（免费） / 护盾":id == "fighter" ? "投枪、推击、格挡" : id == "rogue" ? "偷袭、侧翼准备" : "定身、观察、护盾";
            var prepared = (p6RuntimeRules?Profile?.P6PreparationDraft:Profile?.State.meta.lastPreparation)?.actors.FirstOrDefault(a => a.actorId == id);
            var spec = prepared == null ? null : ProfilePreparationCatalog.Find(prepared.specializationId);
            return role + "\n当前专精：" + (spec?.name ?? "未读取") + "\n初始装备：" + equipment + "\n能力：" + abilities + (spec == null ? "" : "\n" + spec.description);
        }
        public string FrontActorPreparation(string id)
        {
            if(id==PaladinId)return "圣武士\n誓光大剑 · 基础 / 全力斩";
            var actor = (p6RuntimeRules?Profile?.P6PreparationDraft:Profile?.State.meta.lastPreparation)?.actors.FirstOrDefault(a => a.actorId == id);
            var spec = actor == null ? null : ProfilePreparationCatalog.Find(actor.specializationId);
            return ActorName(id) + "\n" + (spec?.name ?? "未读取") + "\n" + (id == "fighter" ? "剑与盾" : id == "rogue" ? "双刃" : "法杖");
        }
        public List<DungeonChoice> FrontChoices
        {
            get
            {
                if(TryV11FrontChoices(out var v11Choices))return v11Choices;
                var c = new List<DungeonChoice>();
                if (FrontPage == "main") { c.Add(new DungeonChoice("campaign","战役")); c.Add(new DungeonChoice("explore","探索")); c.Add(new DungeonChoice("characters","角色")); return c; }
                if (FrontPage == "campaign") { c.Add(new DungeonChoice("main","返回")); return c; }
                if (FrontPage == "exit") { c.Add(new DungeonChoice("exit-confirm","退出游戏")); c.Add(new DungeonChoice("main","继续停留")); return c; }
                if (FrontPage == "help")
                {
                    c.Add(new DungeonChoice("training","无收益训练"));
                    if(HasHistoricalStory)c.Add(new DungeonChoice("continue-historical","继续历史演示进度"));
                    if(HasLegacySave)c.Add(new DungeonChoice("legacy","导入历史存档"));
                    c.Add(new DungeonChoice("credits","制作与素材许可")); c.Add(new DungeonChoice("main","返回")); return c;
                }
                if (FrontPage == "legacy") { c.Add(new DungeonChoice("import","保留原档并导入")); c.Add(new DungeonChoice("help","返回")); return c; }
                if (FrontPage == "credits") { c.Add(new DungeonChoice("help","返回")); return c; }
                if (FrontPage == "explore")
                {
                    c.Add(new DungeonChoice("map-ashen","灰烬遗迹\nASHEN RUINS\n被遗忘的地下遗迹\n已解锁 · 三人小队"));
                    if(HasExplorationRun)c.Add(new DungeonChoice("continue-exploration","继续探索"));
                    c.Add(new DungeonChoice("main","返回")); return c;
                }
                if (FrontPage == "explore-run")
                {
                    c.Add(new DungeonChoice("continue-exploration","继续探索",HasExplorationRun));
                    c.Add(new DungeonChoice("new-exploration","新开始",!HasExplorationRun)); c.Add(new DungeonChoice("explore","返回地图")); return c;
                }
                if (FrontPage == "prepare" && Profile != null)
                {
                    foreach(string id in p6RuntimeRules?new[]{PaladinId,"mage","rogue"}:new[]{"fighter","rogue","mage"})c.Add(new DungeonChoice("cycle:"+id,FrontActorPreparation(id),!p6RuntimeRules||id!=PaladinId));
                    c.Add(new DungeonChoice("active","切换默认主控")); c.Add(new DungeonChoice("law",p6RuntimeRules?"基础骰律 · 初次预设":"切换骰律",!p6RuntimeRules));
                    c.Add(new DungeonChoice("depart","出发",CanStartExploration)); c.Add(new DungeonChoice("characters","查看角色与成长")); c.Add(new DungeonChoice("explore","返回地图")); return c;
                }
                if ((FrontPage == "characters" || FrontPage == "growth") && Profile != null)
                {
                    foreach(string id in p6RuntimeRules?new[]{PaladinId,"mage","rogue"}:new[]{"fighter","rogue","mage"})c.Add(new DungeonChoice("role:"+id,ActorName(id)));
                    var role = growthRole == PaladinId ? ActorRole.Paladin : growthRole == "fighter" ? ActorRole.Fighter : growthRole == "rogue" ? ActorRole.Rogue : ActorRole.Mage;
                    foreach(var s in ProfilePreparationCatalog.All.Where(s=>s.role==role))
                        c.Add(new DungeonChoice("unlock:"+s.id,s.name+" · "+(SpecializationUnlocked(s.id)?"已解锁":s.costResearch+" 研究"),
                            !SpecializationUnlocked(s.id)&&Profile.State.meta.research>=s.costResearch&&Profile.IsSafe));
                    c.Add(new DungeonChoice("explore","选择探索")); c.Add(new DungeonChoice("main","返回主界面")); return c;
                }
                c.Add(new DungeonChoice("main","返回")); return c;
            }
        }
        bool SpecializationUnlocked(string id)
        {
            var definition=ProfilePreparationCatalog.Find(id);
            return definition!=null&&(definition.costResearch==0||Profile.State.meta.unlockedSpecializations.Contains(id));
        }
        public void ChooseFrontend(string id)
        {
            bool auxiliary=id=="settings"||id=="exit-app"||id=="help";
            if(!Menu||(!auxiliary&&!FrontChoices.Any(c=>c.id==id&&c.enabled)))return;
            if(TryChooseV11Frontend(id))return;
            if(id=="settings"){FrontSettings=true;FrontendCue("confirm");Hud.RefreshView();return;}
            if(id=="exit-app"){FrontPage="exit";Hud.RefreshView();return;}
            if(id=="exit-confirm"){Application.Quit();return;}
            if(id=="campaign"||id=="main"||id=="explore"||id=="help"||id=="credits"||id=="legacy")
            {if(id=="main")FrontendCue("back");FrontPage=id;Message="";}
            else if(id=="training"){StartTraining();return;}
            else if(id=="continue-historical"){ContinueDungeonSaved();return;}
            else if(id=="import")
            {
                try
                {
                    var config=LoadDungeonConfig();var legacy=LoadLegacyConfig();var rules=LoadConfig();
                    var imported=ProfileSession.LoadLegacyIntoP4(LegacyProfilePath,SavePath,legacy,config,true);
                    Profile=rules.p6!=null?ProfileSession.LoadCompatible(SavePath,rules,LoadP5Config(),legacy,config):
                        rules.p5!=null?ProfileSession.LoadCompatible(SavePath,rules,legacy,config):imported;
                    DungeonConfig=config;FrontPage="explore";ReconcileDungeon();Message="历史进度已导入独立档案，原文件保留。";
                }
                catch(Exception e){Message="导入失败，原文件保留："+e.Message;}
            }
            else if(id=="continue-exploration"){ContinueFrontendExploration();return;}
            else
            {
                if(!EnsureFrontendProfile())return;
                if(id=="map-ashen"||id=="new-exploration")
                {
                    FrontendCue("map");FrontPage=HasExplorationRun?"explore-run":"prepare";
                    if(!CanStartExploration&&!HasExplorationRun)Message="历史冒险有未完成的战斗或选择。请在手册中继续历史演示进度；原状态已保留。";
                }
                else if(id=="characters"){if(p6RuntimeRules&&growthRole=="fighter")growthRole=PaladinId;frontReturn=FrontPage;FrontPage="characters";FrontendCue("character");}
                else if(id.StartsWith("role:",StringComparison.Ordinal)){growthRole=id.Substring(5);FrontendCue("character");}
                else if(id.StartsWith("unlock:",StringComparison.Ordinal))
                    ProfileAction(new ProfileCommand{kind=ProfileCommandKind.UnlockSpecialization,selectionId=id.Substring(7)});
                else if(id=="depart"){DepartFrontend();return;}
                else
                {
                    var draft=p6RuntimeRules?Profile.P6PreparationDraft:CombatJson.Clone(Profile.State.meta.lastPreparation);
                    if(id=="active"){string[] ids=p6RuntimeRules?new[]{PaladinId,"mage","rogue"}:new[]{"fighter","rogue","mage"};draft.defaultActiveActorId=ids[(Array.IndexOf(ids,draft.defaultActiveActorId)+1)%3];}
                    else if(id=="law")
                    {
                        var laws=Profile.State.meta.unlockedLaws.Where(x=>DungeonConfig.Rule(x)?.kind=="Law").ToArray();
                        if(laws.Length>0)draft.primaryLawId=laws[(Array.IndexOf(laws,draft.primaryLawId)+1)%laws.Length];
                    }
                    else if(id.StartsWith("cycle:",StringComparison.Ordinal))
                    {
                        var actor=draft.actors.First(a=>a.actorId==id.Substring(6));var current=ProfilePreparationCatalog.Find(actor.specializationId);
                        var options=ProfilePreparationCatalog.All.Where(s=>s.role==current.role&&SpecializationUnlocked(s.id)).ToArray();
                        actor.specializationId=options[(Array.FindIndex(options,s=>s.id==actor.specializationId)+1)%options.Length].id;
                    }
                    if(p6RuntimeRules){var saved=Profile.SaveP6Preparation(draft,Guid.NewGuid().ToString("N"));if(!saved.success)Message=saved.reason;}
                    else ProfileAction(new ProfileCommand{kind=ProfileCommandKind.SavePreparation,preparation=draft});
                }
            }
            ApplyTime();Hud.RefreshView();
        }
        void ContinueFrontendExploration()
        {
            if(!EnsureFrontendProfile()||!HasExplorationRun)return;
            if(Profile.State.activeMode==ProfileModeKind.Challenge){ContinueDungeonSaved();return;}
            var result=ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ContinueChallenge});
            if(result.success)EnterFrontendDungeon();
            else { Message="历史冒险仍有待处理的战斗或选择。请从手册继续历史演示进度，完成后再切换探索。";Hud.RefreshView(); }
        }
        void DepartFrontend()
        {
            if(!CanStartExploration){Message="进行中的冒险不会被覆盖。请选择继续探索。";Hud.RefreshView();return;}
            if(V11FrontendEnabled){EnsureV11FrontendProgress();var quote=Profile.QuoteV11Departure();if(!quote.legal){Message=V11Reason(quote.reason);Hud.RefreshView();return;}}
            if(p6RuntimeRules&&!p6TutorialSeenInProcess&&!File.Exists(P6TutorialPath)){p6DepartAfterTraining=true;StartTraining();return;}
            var result=ProfileAction(new ProfileCommand{kind=ProfileCommandKind.StartDemoExploration,selectionId=p6RuntimeRules?"BASE":Profile.State.meta.lastPreparation.primaryLawId});
            if(result.success){P6LastDamage="";EnterFrontendDungeon();}
            else { Message="当前探索尚不能出发；已有进度与准备保持不变。";Hud.RefreshView(); }
        }
        void EnterFrontendDungeon()
        {
            Menu=false;FrontPage="main";dungeonDialog="";
            SelectedActor=State.pendingRoll?.actorId??State.activeActorId;
            SelectedAction=State.pendingRoll?.actionId??Session.Config.Template(ActiveActor.role).actionIds[0];
            SelectedTarget=State.pendingRoll?.targetId;
            rollingTicket=null;RollElapsed=0;
            ReconcileDungeon();SynchronizeViews();RestorePresentationSnapshot();RestoreDungeonDialog();RebindPresentation();
            Message="小队已出发。";ApplyTime();Hud.RefreshView();
        }
        public void CloseFrontendSettings(){FrontSettings=false;FrontendCue("back");ApplyTime();Hud.RefreshView();}
        public void OpenGrowthFromSettlement()
        {
            var saved=Profile?.SaveCurrent(Guid.NewGuid().ToString("N"));
            if(saved!=null&&!saved.success){Message="保存失败："+saved.reason;return;}
            Menu=true;FrontPage="characters";frontReturn="explore";dungeonDialog="";ApplyTime();Hud.RefreshView();
        }
        public void StartTraining()
        {
            if (Training) return;
            if (Profile != null) { var saved = Profile.SaveCurrent(Guid.NewGuid().ToString("N")); if (!saved.success) { Message = saved.reason; return; } }
            suspendedTrainingProfile = Profile; Profile = null;
            Training = true; expectDungeonProfile = false; expectPartyProfile = true; Menu = false; FrontSettings = false;
            var config = LoadConfig(); var state = config.p6!=null?CombatState.CreatePartyForPreset("training", "training/"+Guid.NewGuid().ToString("N"),unchecked((ulong)DateTime.UtcNow.Ticks),config):CombatState.CreatePartyP1("training", "training/" + Guid.NewGuid().ToString("N"), 520052UL, config);
            CombatSession.InitializeP5State(state,config);
            state.world.strings["context"] = "TRAINING";
            if(state.p6!=null){state.p6.training=true;state.enemies[0].hp=state.enemies[0].maxHp=250;CombatSession.InitializeP6Focus(state,config.p6.focus,true,true);p6Lesson=1;p6Moved=0;p6TrainingDefended=false;p6TrainingStart=Time.realtimeSinceStartup;P6FirstSmiteSeconds=-1;}
            Session = new CombatSession(state, config); dungeonDialog = ""; SelectedActor = state.activeActorId; SelectedAction = config.p6!=null?SmiteId:"attack"; SelectedTarget = state.enemies[0].id;
            SetDungeonTerrainVisible(false); rollingTicket = null; RollElapsed = 0;
            SynchronizeViews(); Rig.Initialize(BattleCamera, () => Position(ActiveActor), PartyCenter, () => IsFrozen, () => IsFocus); Rig.Step(10);
            ResetCombatPresentationObservation(); ApplyTime(); Hud.RefreshView();
            ApplyP4Preferences();
            Message = "训练场：不改变故事、挑战、研究或解锁。Esc 后返回标题退出训练。";
        }
        public void EndTraining()
        {
            if (!Training) return;
            Profile = suspendedTrainingProfile; suspendedTrainingProfile = null; Training = false;
            expectDungeonProfile = true; Menu = true; FrontPage = "main";
            if (Profile != null) { ReconcileDungeon(); SynchronizeViews(); RestorePresentationSnapshot(); RebindPresentation(); }
            ApplyTime(); Hud.RefreshView(); Message = "已离开训练；原冒险资源与进度保持。";
        }
    }
}
