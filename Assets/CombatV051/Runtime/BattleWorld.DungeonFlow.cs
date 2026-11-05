using System;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        float nextDungeonTransition;
        Transform dungeonLandmarks;
        string replacementCandidate;
        string dungeonMapKey;

        public ProfileResult ProfileAction(ProfileCommand command)
        {
            if (Profile == null) return new ProfileResult { reason = "profile_missing" };
            CapturePresentationSnapshot();
            string previousMode = Profile.ActiveMode.id;
            command.id = command.id ?? Guid.NewGuid().ToString("N");
            var result = Profile.Execute(command);
            Message = result.success ? "进度已保存。" : "未执行：" + result.reason;
            if (result.success)
            {
                ReconcileDungeon(); SynchronizeViews();
                if(Profile.UsesV12Dungeon&&command.kind==ProfileCommandKind.EnterRoom&&Profile.PendingReward!=null)RestoreDungeonDialog();
                SelectedActor = State.pendingRoll?.actorId ?? State.commandActorId ?? State.activeActorId;
                if (State.Actor(SelectedTarget) == null) SelectedTarget = State.enemies.FirstOrDefault(e => e.Alive)?.id;
                if (previousMode != Profile.ActiveMode.id || command.kind == ProfileCommandKind.RetryStoryEncounter || command.kind == ProfileCommandKind.ContinueStory || command.kind == ProfileCommandKind.ContinueChallenge)
                {
                    RebindPresentation();
                    RestoreDiceAnimationPreference();
                    RestorePresentationSnapshot();
                }
            }
            ObservePresentationProfile(command,result);
            if (result.success) ApplyP4Preferences();
            ApplyTime(); Hud.RefreshView(); return result;
        }

        void ReconcileDungeon()
        {
            if (Profile == null) return;
            DungeonConfig=Profile.ActiveDungeonConfig;
            if (DungeonMap == null||dungeonMapKey!=Profile.ActiveDungeonKey)
            {
                if(DungeonMap==null){var obj = new GameObject("Connected adventure map"); obj.transform.SetParent(transform, false);DungeonMap = obj.AddComponent<DungeonMap>();}
                if(dungeonLandmarks){dungeonLandmarks.gameObject.SetActive(false);Destroy(dungeonLandmarks.gameObject);dungeonLandmarks=null;}
                ClearV12PatrolPresentation();ResetP5EnvironmentForMap();artEnvironment?.ResetMapPresentation();artGeometry=null;
                DungeonMap.Initialize(BattleCamera,
                    DungeonConfig.rooms.Select(r => new DungeonRoomGeometry { id = r.id, center = new Vector3(r.x,0,r.z), width = r.width, depth = r.depth }).ToArray(),
                    DungeonConfig.passages.Select(p => new DungeonPassageGeometry { id = p.id, roomA = p.roomA, roomB = p.roomB, center = new Vector3(p.x,0,p.z), width = p.width, open = Profile.IsPassageOpen(p.id) }).ToArray(),Profile.ActiveMode.v12Run?.obstacles.ToArray());
                dungeonMapKey=Profile.ActiveDungeonKey;
                CreateDungeonLandmarks();
            }
            SetDungeonTerrainVisible(true);
            foreach (var passage in DungeonConfig.passages) DungeonMap.SetPassage(passage.id, Profile.IsPassageOpen(passage.id));
            if(Profile.UsesV12Dungeon)DungeonMap.SetV12WorldFlags(Profile.ActiveMode.flags);
            else {DungeonMap.SetCover("R4_CENTER", Profile.ActiveMode.HasFlag("ROUTE_OPEN"));DungeonMap.SetCover("R5_SHIFT", Profile.ActiveMode.HasFlag("COVER_SHIFTED"));}
            string encounter = Profile.ActiveEncounter?.id ?? "";
            if (encounter != lastDungeonEncounter)
            {
                lastDungeonEncounter = encounter; SelectedTarget = State.enemies.FirstOrDefault(e => e.Alive)?.id;
                rollingTicket = null; RollElapsed = 0;
            }
            lastDungeonRoom = Profile.ActiveRoom.templateId;
            ReconcileP5Environment();
            ReconcileV12PatrolPresentation();
        }

        void CreateDungeonLandmarks()
        {
            dungeonLandmarks = new GameObject("Interaction landmarks").transform; dungeonLandmarks.SetParent(DungeonMap.transform, false);
            foreach (var room in DungeonConfig.rooms)
                Landmark(room.id + " / " + RoomName(room.id), new Vector3(room.x,0,room.z-5.5f), new Color(.5f,.72f,.77f), false);
            if(Profile.UsesV12Dungeon)
            {
                foreach(var point in Profile.ActiveMode.v12Run.points)Landmark("E · "+point.label,new Vector3(point.x,0,point.z),Teal,true);
                foreach(var ev in DungeonConfig.events)Landmark("E · "+(ev.id=="V12_READ"?"阅读书页 / 免检定":"调查旧书箱 / 可选检定"),new Vector3(ev.x,0,ev.z),Amber,true);
                return;
            }
            Landmark("E · 入口 / 模式", Vector3.zero, Teal, true);
            foreach (var ev in DungeonConfig.events) Landmark("E · " + (ev.id=="EVENT_A" ? "检修装置" : "侦察记号"), new Vector3(ev.x,0,ev.z), Amber, true);
            Landmark("E · 短休", new Vector3(0,0,22), Teal, true);
            Landmark("E · 捷径", new Vector3(0,0,14), Teal, true);
            Landmark("E · 破障门", new Vector3(48,0,8), Amber, true);
            Landmark("E · 推动掩体", new Vector3(26,0,24), Amber, true);
        }

        void Landmark(string title, Vector3 position, Color color, bool pad)
        {
            if (pad) BattleVisual.Box(title + " pad", position + Vector3.up*.025f, new Vector3(1.4f,.04f,1.4f), color, dungeonLandmarks,0,false);
            var obj = new GameObject(title); obj.transform.SetParent(dungeonLandmarks,false);
            obj.transform.position = position + Vector3.up * (pad ? 1.4f : .1f);
            obj.transform.rotation = BattleCamera.transform.rotation;
            var label = obj.AddComponent<TextMesh>(); label.text=title; label.characterSize=.13f; label.fontSize=40;
            label.anchor=TextAnchor.MiddleCenter; label.color=color;
            // Match the HUD's bundled font, including CJK glyphs when available.
            var hudText = Hud.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (hudText != null && hudText.font != null) { label.font=hudText.font; obj.GetComponent<MeshRenderer>().sharedMaterial=label.font.material; }
            // Player HUD supplies nearby labels. Internal room IDs remain in F1 diagnostics.
            obj.GetComponent<MeshRenderer>().enabled=false;
        }

        public void AdvanceDungeonExploration()
        {
            AdvanceV12PatrolPresentation();
            if (Profile == null || Menu || DungeonModal || IsFrozen || Profile.IsInEncounter) return;
            if (Time.unscaledTime < nextDungeonTransition) return;
            nextDungeonTransition = Time.unscaledTime + .35f;
            string physicalRoom = DungeonMap.RoomAt(Position(ActiveActor), .8f);
            if (physicalRoom == null) return;
            if (physicalRoom != Profile.ActiveRoom.templateId)
            {
                if (!State.actors.Where(a=>a.Alive&&IsPhysical(a)).All(a=>DungeonMap.RoomAt(Position(a), .45f)==physicalRoom))
                { Message = "等队友进入房间后，入口才会封闭开始遭遇。"; return; }
                var entered = ProfileAction(new ProfileCommand { kind=ProfileCommandKind.EnterRoom, roomId=physicalRoom, actorId=State.activeActorId, qualified=true });
                if (!entered.success) return;
            }
            if (!Profile.ActiveRoom.resolved && DungeonConfig.Room(physicalRoom).enemies.Count > 0 && (!Profile.UsesV12Dungeon||Profile.V12EncounterTriggerReached()))
            {
                var started = ProfileAction(new ProfileCommand { kind=ProfileCommandKind.ActivateEncounter, roomId=physicalRoom, actorId=State.activeActorId, qualified=true });
                if (started.success) Message = Profile.UsesV12Dungeon?"遭遇开始 · 免费基础行动随时可用，Q关键行动；注意红色攻击预告。":physicalRoom=="R1" ? "实战教学：基础攻击/格挡/闪避积累动量。Tab → 行动 → 目标 → 确认掷骰 → Commit → Tab执行。" : "遭遇开始；出入口锁定。关注红色地面预警与队友生命。";
            }
        }

        public void ResolveDungeonEncounter()
        {
            if (!CombatSession.V13PartyAlive(State)) { dungeonDialog="defeat"; ApplyTime(); Hud.RefreshView(); return; }
            var result=ProfileAction(new ProfileCommand { kind=ProfileCommandKind.ResolveEncounter, encounterId=Profile.ActiveEncounter?.id });
            if (result.success) RestoreDungeonDialog();
            ApplyTime(); Hud.RefreshView();
        }

        void RestoreDungeonDialog()
        {
            dungeonDialog=""; replacementCandidate=null;
            if (Profile == null) return;
            if (Profile.ActiveMode.stage=="Defeated") dungeonDialog="defeat";
            else if (Profile.PendingReward!=null) dungeonDialog="reward";
            else if (Profile.ActiveSafeEvent!=null) { interactingEvent=Profile.ActiveSafeEvent.templateId; dungeonDialog="event"; }
            else if (Profile.ActiveMode.stage=="Settled") dungeonDialog="settled";
            else if (Profile.ActiveMode.Room(Profile.UsesV12Dungeon?"V12_R6":"R7").resolved && !Profile.ActiveMode.HasFlag("STORY_COMPLETE")) dungeonDialog="victory";
        }

        bool Near(Vector3 point, float distance=5) => ActiveActor!=null && ActiveActor.Alive && BattleGeometry.DistanceXZ(Position(ActiveActor),point)<=distance;
        string NearbyDungeonInteraction()
        {
            if (Profile == null) return null;
            if(Profile.UsesV12Dungeon)return NearbyV12Interaction();
            if (Profile.ActiveRoom.templateId=="R5" && !Profile.ActiveMode.HasFlag("COVER_SHIFTED") && Near(new Vector3(26,0,24),4.5f)) return "cover";
            if (Profile.IsInEncounter || !Profile.IsSafe) return null;
            if (Profile.PendingReward!=null) return "reward";
            if (Profile.ActiveRoom.templateId=="R6" && !Profile.IsPassageOpen("P60_SHORTCUT") && Near(new Vector3(0,0,11))) return "shortcut";
            if (Profile.ActiveRoom.templateId=="R2" && Profile.ActiveMode.HasAbility("SMASH") && !Profile.IsPassageOpen("P24_SMASH") && Near(new Vector3(48,0,11))) return "gate";
            foreach (var ev in DungeonConfig.events)
                if (ev.roomId==Profile.ActiveRoom.templateId && Near(new Vector3(ev.x,0,ev.z),ev.range)) return "event:"+ev.id;
            if (Profile.ActiveRoom.templateId=="R0" && Near(Vector3.zero,6)) return "hub";
            if (Profile.ActiveRoom.templateId=="R6" && Near(new Vector3(0,0,22),6)) return "rest";
            return null;
        }

        bool ProcessDungeonInput()
        {
            if (Profile == null || Menu) return false;
            if (DungeonModal)
            {
                if (Controls.Pause.WasPressedThisFrame()) ChooseDungeonOption("close");
                return true;
            }
            if (!Controls.Interact.WasPressedThisFrame() || IsFrozen || IsFocus) return false;
            string choice=NearbyDungeonInteraction();
            if (choice==null) { Message="靠近发光装置、入口或短休标记后按 E。"; return true; }
            bool interactionAccepted=true;
            if(choice=="v12-beam")interactionAccepted=TryV12BeamInteraction();
            else if(choice.StartsWith("supply:",StringComparison.Ordinal)){v12SupplyId=choice.Substring(7);dungeonDialog="supply";}
            else if (choice.StartsWith("event:",StringComparison.Ordinal))
            {
                interactingEvent=choice.Substring(6);
                var existing=Profile.ActiveMode.events.Find(e=>e.templateId==interactingEvent);
                if (existing?.stage=="Committed") { Message="此事件已完成："+(existing.success ? "成功，世界效果保留。" : "失败，替代道路仍可走。"); return true; }
                dungeonDialog="event-select";
            }
            else if (choice=="cover")
                interactionAccepted=ProfileAction(new ProfileCommand { kind=ProfileCommandKind.UseEnvironment, selectionId="R5_SHIFT", sourceFactId=Profile.ActiveMode.id+"/environment/R5_SHIFT", actorId=State.activeActorId, qualified=true }).success;
            else if (choice=="shortcut" || choice=="gate")
                interactionAccepted=ProfileAction(new ProfileCommand { kind=choice=="gate" ? ProfileCommandKind.OpenGate : ProfileCommandKind.OpenShortcut, gateId=choice=="gate" ? "P24_SMASH":"P60_SHORTCUT", actorId=State.activeActorId, qualified=true }).success;
            else dungeonDialog=choice;
            if(!interactionAccepted)
            {
                Message=choice=="gate"&&!Profile.ActiveMode.HasAbility("SMASH")?"需要破障能力。先探索东侧房间。":"当前无法使用该装置。";
                PresentationUICue("UiCancel");ApplyTime();Hud.RefreshView();return true;
            }
            if(choice=="hub")TutorialFact("entry.interacted",Guid.NewGuid().ToString("N"));
            Audio?.PlayCue("Interact",Position(ActiveActor));NotifyArtCue("interact",State.activeActorId,null,Guid.NewGuid().ToString("N"));PollPresentation();
            ApplyTime(); Hud.RefreshView(); return true;
        }
    }
}
