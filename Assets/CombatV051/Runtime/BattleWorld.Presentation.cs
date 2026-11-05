using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Observes accepted gameplay facts; never supplies rolls, currency, health or world progression.</summary>
    public sealed partial class BattleWorld
    {
        public BattleAudio Audio { get; private set; }
        public TutorialService Tutorial { get; private set; }
        public bool DebugPresentation { get; private set; }
        public string PresentationLastReason { get; private set; }="";
        TutorialView tutorialView;
        string tutorialInstance;
        string tutorialModeId;
        bool publishingTutorialUnlock;
        bool DemoTutorialMode => Profile?.ActiveMode.kind==ProfileModeKind.Challenge && Profile.ActiveMode.HasFlag("DEMO_EXPLORATION");
        string ActiveTutorialModeId => Profile==null?null:DemoTutorialMode?Profile.ActiveMode.id:Profile.State.story.id;
        string PresentationBindingModeId => P6Enabled ? Profile?.ActiveMode.id : ActiveTutorialModeId;
        bool TutorialForActiveMode => Profile!=null && (Profile.ActiveMode.kind==ProfileModeKind.Story || DemoTutorialMode&&!Profile.State.meta.challengeUnlocked);
        float tutorialMoveDistance, nextTutorialPoll, nextMoveFactRetry;
        string lastGuidance;
        float guidanceUntil;
        Transform guidanceRoot;
        string observedCombat;
        int observedCharges, observedSpent;
        float observedMomentum;
        readonly Dictionary<string,int> observedHp=new Dictionary<string,int>();
        readonly Dictionary<string,float> observedStagger=new Dictionary<string,float>();
        readonly HashSet<string> observedIncoming=new HashSet<string>();
        readonly HashSet<string> observedTelegraph=new HashSet<string>();
        public string TutorialPromptText
        {
            get
            {
                if(P6Enabled)return P6TrainingPrompt;
                if(tutorialView==null||!tutorialView.visible)return "";
                if(State.mode==CombatMode.TacticalFocus&&(tutorialView.highlightKey=="confirm"||tutorialView.highlightKey=="target"))
                {
                    var quote=SelectedActionQuote();
                    if(!quote.legal)return BattleHud.HumanReason(quote.reason);
                    var actor=State.Actor(SelectedActor);var target=State.Actor(SelectedTarget);
                    if(actor!=null&&target!=null&&!HasLineOfEffect(Position(actor),Position(target)))return "目标被墙体遮挡，选择其他目标或返回实时调整位置";
                }
                return tutorialView.prompt??"";
            }
        }
        public bool IsFirstMovementLesson => tutorialView!=null&&tutorialView.visible&&tutorialView.stepId=="move";
        public string TutorialHighlightKey
        {
            get
            {
                if(P6Enabled)return P6TrainingPrompt;
                if(tutorialView==null||!tutorialView.visible)return "";
                switch(tutorialView.highlightKey)
                {
                    case "actor":return SelectedActor;
                    case "action":return SelectedAction;
                    case "exit_focus":return "exit";
                    case "party":return "active-"+(State.activeActorId=="fighter"?"rogue":"fighter");
                    default:return tutorialView.highlightKey??"";
                }
            }
        }
        void InitializePresentation()
        {
            Audio=gameObject.AddComponent<BattleAudio>();
            Audio.Initialize(this,Path.Combine(Path.GetDirectoryName(SavePath),"p3-audio-settings.json"));
            InitializeArtPresentation();
        }
        public void PresentationUICue(string cue) { if(Audio!=null)Audio.PlayCue(cue); }
        public void SetPresentationVolumes(float master,float music,float sfx) { Audio?.SetVolumes(master,music,sfx); }
        void RebindPresentation()
        {
            ApplyP4Preferences();
            Audio?.Initialize(this,Path.Combine(Path.GetDirectoryName(SavePath),"p3-audio-settings.json")); ResetArtObservation(); ResetCombatPresentationObservation();ImpactFeedback?.ResetObservation();
            if(Profile==null)return;
            string modeId=PresentationBindingModeId;
            if(P6Enabled)
            {
                // P6 has its own tutorial, but still needs a stable presentation binding.
                // Otherwise every LateUpdate reinitializes observers and applies settings.
                tutorialInstance=Profile.State.profileInstanceId; tutorialModeId=modeId;
                return;
            }
            if(tutorialInstance==Profile.State.profileInstanceId&&tutorialModeId==modeId)
            {
                // Identity can stay the same while a menu closes. Recompute suppression
                // now; a cached hidden view must not wait for the periodic tutorial poll.
                PollPresentation();return;
            }
            tutorialInstance=Profile.State.profileInstanceId;
            tutorialModeId=modeId;
            var resource=Resources.Load<TextAsset>("TutorialConfig");
            var definition=resource==null?new TutorialDefinition():CombatJson.Read<TutorialDefinition>(resource.text);
            var safeId=new string(tutorialInstance.Where(c=>char.IsLetterOrDigit(c)||c=='-').ToArray());
            string suffix=DemoTutorialMode?"-exploration-"+CombatStore.Digest(modeId).Substring(0,12):"";
            string path=Path.Combine(Path.GetDirectoryName(SavePath),"presentation","tutorial-"+safeId+suffix+".json");
            try { Tutorial=TutorialService.LoadOrCreate(path,definition,tutorialInstance,modeId,BuildTutorialContext()); }
            catch(Exception error) { PresentationLastReason="Tutorial sidecar unavailable: "+error.Message; Tutorial=TutorialService.Create(definition,tutorialInstance,modeId); }
            tutorialMoveDistance=Tutorial.State.facts.FirstOrDefault(f=>f.key=="move.distance")?.value??0;
            PollPresentation();
        }
        TutorialContext BuildTutorialContext()
        {
            var context=new TutorialContext { storyActive=TutorialForActiveMode,
                suppress=Menu||DungeonModal||State.mode==CombatMode.CombatPaused||State.mode==CombatMode.ReactionPrompt||State.pendingRoll?.purpose=="Reaction" };
            if(Profile==null)return context;
            var story=DemoTutorialMode?Profile.ActiveMode:Profile.State.story;
            string first=story.v12Run==null?"R1":"V12_R1",second=story.v12Run==null?"R2":"V12_R2";
            if(!string.IsNullOrEmpty(story.Room(first)?.encounterId))context.worldFacts.Add("encounter.r1_started");
            if(story.Room(first)?.resolved==true)context.worldFacts.Add("encounter.r1_clear");
            if(story.Room(second)?.visited==true)context.worldFacts.Add("room.r2_entered");
            if(NearbyDungeonInteraction()=="hub")context.availableFacts.Add("entry.near");
            if(State.inCombat)context.availableFacts.Add("combat.active");
            if(State.mode==CombatMode.RealtimeCombat||State.mode==CombatMode.QueuedExecution)context.availableFacts.Add("realtime.active");
            if(State.enemies.Any(e=>e.Alive))context.availableFacts.Add("enemy.available");
            if(State.enemies.Any(e=>e.Alive&&e.aiState==1))context.availableFacts.Add("telegraph.active");
            if(State.charges>0)context.availableFacts.Add("dice.ready");
            if(State.mode==CombatMode.TacticalFocus)context.availableFacts.Add("focus.active");
            if(State.mode==CombatMode.RollFreeze)context.availableFacts.Add("roll.active");
            if(State.mode==CombatMode.PostRollDecision)context.availableFacts.Add("decision.active");
            if(State.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing"))context.availableFacts.Add("queue.active");
            return context;
        }
        bool TutorialFact(string key,string source=null,float value=1,bool system=false)
        {
            if(Tutorial==null||!TutorialForActiveMode||Tutorial.State.storyModeId!=ActiveTutorialModeId)return false;
            var result=Tutorial.Observe(key,value,source,playerInitiated:!system);
            if(!result.success)PresentationLastReason="Tutorial: "+result.reason;
            return result.success;
        }
        public void SkipPresentationTutorial()
        {
            var result=Tutorial?.Skip(Guid.NewGuid().ToString("N")); PollPresentation(); PresentationUICue("UiCancel");
            if(result!=null&&!result.success)Message="教程设置暂未保存，请稍后重试。";
        }
        public void ResetPresentationTutorial()
        {
            var result=Tutorial?.Reset(Guid.NewGuid().ToString("N"),BuildTutorialContext());
            if(result!=null&&result.success){tutorialMoveDistance=0;PollPresentation();Message="教程提示已重置。";}
            else Message="教程设置暂未保存，请稍后重试。";
        }
        public void SelectPresentationAction(string id)
        {
            if(!AvailableActions(SelectedActor).Contains(id))return;
            SelectedAction=id; NormalizeV11Target();
            if(State.mode==CombatMode.TacticalFocus)TutorialFact("selection.action",Guid.NewGuid().ToString("N"));
            PollPresentation();
        }
        void ObservePresentationCommand(CombatCommand command,CombatResult result,string priorActive,string priorPurpose,int previousCharges)
        {
            PresentationLastReason=command.kind+": "+result.reason;
            Audio?.ObserveCommand(command,result);
            if(!result.success||result.duplicate)return;
            bool player=command.authority==CommandAuthority.Player&&!command.flag;
            string id=command.id;
            if(command.kind==CommandKind.StartBasicAttack)NotifyArtCue("attack",command.actorId,command.targetId,id);
            if(command.kind==CommandKind.ResolveBasicAttack)
            {
                if(result.hit&&result.damage>0&&command.actorId==State.activeActorId)TutorialFact("basic.hit",id);
                if(result.reason=="basic_swing_missed")NotifyArtCue("miss",command.targetId,null,id);
            }
            if(player)
            {
                switch(command.kind)
                {
                    case CommandKind.BasicAttack: if(result.hit&&result.damage>0)TutorialFact("basic.hit",id);NotifyArtCue("attack",command.actorId,command.targetId,id);break;
                    case CommandKind.StartGuard:TutorialFact("guard.started",id);break;
                    case CommandKind.StartDodge:TutorialFact("dodge.started",id);NotifyArtCue("dodge",command.actorId,null,id);break;
                    case CommandKind.SwitchActive:if(priorActive!=State.activeActorId)TutorialFact("active.switched",id);break;
                    case CommandKind.EnterFocus:TutorialFact("focus.entered",id);break;
                    case CommandKind.SelectCommandActor:TutorialFact("selection.actor",id);break;
                    case CommandKind.ConfirmRoll:if(State.pendingRoll?.purpose=="Tactical")TutorialFact("roll.confirmed",id);break;
                    case CommandKind.RevealRoll:if(State.pendingRoll?.purpose=="Tactical")TutorialFact("roll.revealed",id,system:true);break;
                    case CommandKind.Commit:if(priorPurpose=="Tactical")TutorialFact("roll.committed",id);break;
                }
            }
            if(command.kind==CommandKind.ResolveQueued)
            {
                var q=State.queue.FirstOrDefault(x=>x.id==command.queueId);
                if(q!=null&&q.stage=="Resolved")TutorialFact("action.executed",id,system:true);
            }
            ObserveCombatPresentation();
            PollPresentation();
        }
        void ResetCombatPresentationObservation()
        {
            observedCombat=State.encounterId; observedCharges=State.charges;
            observedSpent=State.receipts.Sum(r=>r.result?.chargeSpent??0); observedMomentum=State.momentumLedger.totalEarned;
            observedHp.Clear();observedStagger.Clear();observedIncoming.Clear();observedTelegraph.Clear();
            foreach(var a in State.actors.Concat(State.enemies)){observedHp[a.id]=a.hp;observedStagger[a.id]=a.staggerRemaining;}
            foreach(var e in State.incomingEvents)if(e.damageApplied)observedIncoming.Add(e.id);
            foreach(var e in State.enemies)if(e.aiState==1&&e.attackGroupId!=null)observedTelegraph.Add(e.attackGroupId);
        }
        void ObserveCombatPresentation()
        {
            if(observedCombat!=State.encounterId){ResetCombatPresentationObservation();return;}
            int spent=State.receipts.Sum(r=>r.result?.chargeSpent??0);
            if(State.inCombat&&State.charges+spent>observedCharges+observedSpent)
            {
                string id=State.encounterId+"/earned/"+State.revision;
                TutorialFact("dice.earned",id,system:true);NotifyArtCue("dice-earned",State.activeActorId,null,id);Message="获得战术行动";
            }
            observedCharges=State.charges;observedSpent=spent;
            if(State.momentumLedger.totalEarned-observedMomentum>=18)
            {
                observedMomentum=State.momentumLedger.totalEarned;
                NotifyArtCue("momentum",State.activeActorId,null,State.encounterId+"/momentum/"+observedMomentum);
            }
            foreach(var a in State.actors.Concat(State.enemies))
            {
                string id=State.encounterId+"/"+State.revision+"/"+a.id;

                observedHp[a.id]=a.hp;observedStagger[a.id]=a.staggerRemaining;
                if(a.isEnemy&&a.aiState==1&&a.attackGroupId!=null&&observedTelegraph.Add(a.attackGroupId))NotifyArtCue("telegraph",a.id,null,a.attackGroupId);
            }

        }
        void ObservePresentationProfile(ProfileCommand command,ProfileResult result)
        {
            if(!result.success||result.duplicate)return;
            if(command.kind==ProfileCommandKind.ActivateEncounter&&Profile.ActiveRoom.templateId=="R1")TutorialFact("encounter.r1_started",command.id,system:true);
            if(command.kind==ProfileCommandKind.ResolveEncounter&&Profile.ActiveRoom.templateId=="R1")TutorialFact("encounter.r1_clear",command.id,system:true);
            if(command.kind==ProfileCommandKind.EnterRoom&&Profile.ActiveRoom.templateId=="R2")TutorialFact("room.r2_entered",command.id,system:true);
            string cue=command.kind==ProfileCommandKind.OpenGate?"BreakableDoor":command.kind==ProfileCommandKind.OpenShortcut?"Door":
                command.kind==ProfileCommandKind.ClaimReward?"ObjectiveUpdate":command.kind==ProfileCommandKind.UseEnvironment?"Interact":null;
            if(cue!=null){Audio?.PlayCue(cue,Position(ActiveActor),command.id);NotifyArtCue(cue=="BreakableDoor"?"break":"door",State.activeActorId,null,command.id);}
            PollPresentation();
        }
        void ObservePlayerMovement(float moved)
        {
            if(moved<=0||Tutorial==null||!TutorialForActiveMode||Tutorial.State.facts.Any(f=>f.key=="move.distance"&&f.value>=2))return;
            tutorialMoveDistance=Mathf.Min(2,tutorialMoveDistance+moved);
            if(tutorialMoveDistance>=2&&Time.unscaledTime>=nextMoveFactRetry)
            {nextMoveFactRetry=Time.unscaledTime+.5f;TutorialFact("move.distance","move/"+Tutorial.State.resetEpoch,2);}
        }
        void PollPresentation()
        {
            if(Tutorial==null||Profile==null||Tutorial.State.storyModeId!=ActiveTutorialModeId)return;
            var context=BuildTutorialContext();
            if(context.storyActive)
            {
                if(NearbyDungeonInteraction()=="hub")TutorialFact("entry.approached","near-entry");
                if(State.inCombat&&State.charges>0)TutorialFact("dice.available","available/"+State.encounterId,system:true);
                Tutorial.Reconcile(context);
            }
            tutorialView=Tutorial.GetView(context);
            if(!publishingTutorialUnlock&&DemoTutorialMode&&!Profile.State.meta.challengeUnlocked&&Tutorial.State.status=="Completed"&&
                Profile.ActiveMode.Room(Profile.UsesV12Dungeon?"V12_R1":"R1")?.resolved==true&&Profile.ActiveMode.Room(Profile.UsesV12Dungeon?"V12_R2":"R2")?.visited==true)
            {
                publishingTutorialUnlock=true;
                try { ProfileAction(new ProfileCommand{id="demo-tutorial/"+Profile.ActiveMode.id,kind=ProfileCommandKind.UnlockChallenge,qualified=true}); }
                finally { publishingTutorialUnlock=false; }
            }
        }
        void LateUpdate() { using (P46Performance.Measure(8)) LateUpdateMeasured(); }
        void LateUpdateMeasured()
        {
            if(Session==null)return;
            if(Controls.Debug.WasPressedThisFrame())DebugPresentation=!DebugPresentation;
            if(Profile!=null&&(tutorialInstance!=Profile.State.profileInstanceId||tutorialModeId!=PresentationBindingModeId))RebindPresentation();
            ImpactFeedback?.Observe();
            ObserveCombatPresentation();
            if(Time.unscaledTime>=nextTutorialPoll){nextTutorialPoll=Time.unscaledTime+.15f;PollPresentation();}
            RefreshArtPresentation(); RefreshGuidance();
        }
        public string NearbyPresentationPrompt
        {
            get
            {
                if(Profile==null||IsFocus||IsFrozen||Menu||IsFirstMovementLesson)return null;
                string near=NearbyDungeonInteraction();if(near==null)return null;
                if(Profile.UsesV12Dungeon)return "[E] "+InteractionName(near);
                return "[E] "+(near=="hub"?"调查入口装置":near=="gate"?"破障开门":near=="shortcut"?"开启捷径":near=="cover"?"推动掩体":near=="rest"?"短休":near=="reward"?"查看奖励":"调查");
            }
        }
        public bool TryNearbyPresentationPoint(out Vector3 point)
        {
            point=Vector3.zero;if(Profile==null)return false;
            string near=NearbyDungeonInteraction();if(near==null)return false;
            if(Profile.UsesV12Dungeon)
            {
                var v12Point=Profile.ActiveMode.v12Run.points.Find(p=>near=="hub"?p.kind=="hub":near=="supply:"+p.id);
                if(v12Point!=null){point=new Vector3(v12Point.x,0,v12Point.z);return true;}
                if(near=="v12-beam"){var beam=Session.Config.v12.beam;point=new Vector3(beam.triggerX,0,beam.triggerZ);return true;}
            }
            if(near.StartsWith("event:")){var ev=DungeonConfig.events.First(e=>e.id==near.Substring(6));point=new Vector3(ev.x,0,ev.z);}
            else if(near=="gate")point=new Vector3(48,0,11);
            else if(near=="shortcut")point=new Vector3(0,0,11);
            else if(near=="rest")point=new Vector3(0,0,22);
            else if(near=="cover")point=new Vector3(26,0,24);
            return true;
        }
        public string PresentationObjective
        {
            get
            {
                if(Profile==null)return "探索";
                if(Profile.UsesV12Dungeon)return Profile.PendingReward!=null?"选择或跳过本局成长":Profile.ActiveV12Purpose.objective;
                if(tutorialView!=null&&tutorialView.visible&&tutorialView.targetKey=="entry")return "调查入口装置";
                if(State.inCombat)return "击退守卫，留意地面预警";
                if(Profile.PendingReward!=null)return "选择一件遗物，或放弃奖励";
                switch(Profile.ActiveRoom.templateId)
                {
                    case "R0":return "前往东侧通道";
                    case "R1":return "穿过东侧出口";
                    case "R2":return !Profile.ActiveMode.Room("R3").resolved?"调查装置，前往东侧房间":!Profile.IsPassageOpen("P24_SMASH")&&Profile.ActiveMode.HasAbility("SMASH")?"使用破障能力打开北门":"沿北侧通路前行";
                    case "R3":return "调查记号，返回岔路";
                    case "R4":return "沿西侧通道前行";
                    case "R5":return "前往休息区";
                    case "R6":return "可短休或开捷径；西侧通往终点";
                    default:return Profile.ActiveRoom.resolved?"探索完成，可返回入口":"抵达终点";
                }
            }
        }
        public bool TryPresentationGoal(out Vector3 goal)
        {
            goal=Vector3.zero;if(Profile==null||Menu||DungeonModal||IsFocus)return false;
            if(Profile.UsesV12Dungeon)
            {
                if(State.inCombat||Profile.PendingReward!=null)return false;
                int number=int.Parse(Profile.ActiveRoom.templateId.Substring(5));if(number>=6)return false;
                var v12Target=DungeonConfig.Room("V12_R"+(number+1));goal=new Vector3(v12Target.x,0,v12Target.z);return true;
            }
            string key=tutorialView!=null&&tutorialView.visible?tutorialView.targetKey:null;
            if(key=="entry")return true;
            if(key=="enemy"&&State.enemies.Any(e=>e.Alive)){goal=Position(State.enemies.First(e=>e.Alive));return true;}
            if(key=="R1"||key=="R2"){var r=DungeonConfig.Room(key);goal=new Vector3(r.x,0,r.z);return true;}
            if(State.inCombat)return false;
            string room=Profile.ActiveRoom.templateId;
            string next=room=="R0"?"R1":room=="R1"?"R2":room=="R2"?(Profile.ActiveMode.Room("R3").resolved?"R4":"R3"):
                room=="R3"?"R2":room=="R4"?"R5":room=="R5"?"R6":room=="R6"?"R7":null;
            if(next==null)return false;
            if(room=="R2"&&next=="R4"&&Profile.ActiveMode.HasAbility("SMASH")&&!Profile.IsPassageOpen("P24_SMASH")){goal=new Vector3(48,0,11);return true;}
            var target=DungeonConfig.Room(next);goal=new Vector3(target.x,0,target.z);return true;
        }
        void RefreshGuidance()
        {
            if(!DungeonPresentation)return;
            if(guidanceRoot==null)
            {
                guidanceRoot=new GameObject("Short objective path").transform;guidanceRoot.SetParent(transform,false);
                for(int i=0;i<7;i++)BattleVisual.Disc("Guide mote",Vector3.zero,.085f,Amber,guidanceRoot);
            }
            string key=PresentationObjective+"/"+tutorialView?.stepId;
            if(key!=lastGuidance){lastGuidance=key;guidanceUntil=Time.unscaledTime+5;Audio?.PlayCue("ObjectiveUpdate");}
            bool visible=!IsFrozen&&!State.inCombat&&Time.unscaledTime<guidanceUntil&&TryPresentationGoal(out _);
            guidanceRoot.gameObject.SetActive(visible);if(!visible||DungeonMap==null)return;
            TryPresentationGoal(out var goal);Vector3 start=Position(ActiveActor);Vector3 waypoint=DungeonMap.NextWaypoint(start,goal);
            float distance=BattleGeometry.DistanceXZ(start,waypoint);Vector3 direction=(waypoint-start).normalized;
            int index=0;foreach(Transform dot in guidanceRoot){float offset=.8f+index++*.5f;dot.gameObject.SetActive(offset<distance);dot.position=start+direction*offset+Vector3.up*.04f;}
        }
        public string PresentationToast
        {
            get
            {
                if(string.IsNullOrEmpty(Message)||Message=="已执行"||Message.Contains("存档")||Message.Contains("快照")||Message.Contains("Tab →")||Message.Contains("_")||System.Text.RegularExpressions.Regex.IsMatch(Message,@"\bR[0-7]\b"))return "";
                if(Message.Length>70)return "";
                if(Message.StartsWith("主控已切换"))return "当前主控："+ActorName(State.activeActorId);
                return Message;
            }
        }
    }
}
