using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    // Opt-in Player evidence. Only the save location and virtual input devices are isolated.
    // All commands below are delivered through keyboard/mouse and the shipping UI.
    public sealed partial class BattlePresentationSmokeFlow
    {
        const string P6Paladin="char_demo_paladin", P6Smite="ability_smite_recharge";
        bool p6Mode,p6BaseDamage,p6FullDamage,p6MarkedFullDamage,p6FormalBind,p6RoomCleared,p6PracticeComplete;
        string p6PracticeStatus="NOT_RUN",p6Encounter,p6FrozenId,p6FrozenState;
        Vector3 p6FrozenPosition;
        Quaternion p6FrozenRotation;
        float p6FormalStarted=-1,p6PracticeFirstSmite=-1;
        int p6Attempt,p6CaptureSequence;
        readonly List<object> p6Ledger=new List<object>(),p6Inputs=new List<object>(),p6FrozenFrames=new List<object>();
        readonly HashSet<string> p6Seen=new HashSet<string>();
        readonly HashSet<string> p6ActualBindInputs=new HashSet<string>();
        string P6Route {get{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-p6-route");return i>=0&&i+1<args.Length?args[i+1]:"both";}}
        bool P6PracticeRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-p6-practice")>=0;
        bool P6FormalGoals=>P6Route=="base"?p6BaseDamage:P6Route=="combo"?p6MarkedFullDamage&&p6FormalBind:p6BaseDamage&&p6MarkedFullDamage&&p6FormalBind;
        public void BeginP6(BattleWorld owner,string path){p6Mode=true;p4Mode=true;Begin(owner,path);}

        void ObserveP6Actions()
        {
            if(!p6Mode||world?.State?.p6==null)return;
            P6ObserveState(world.State,"live");
            // The last contact may settle the encounter before LateUpdate. Read the real
            // archived encounter DTO too; do not manufacture a result from HP deltas.
            if(!world.Training&&world.Profile?.ActiveMode!=null)
                foreach(var encounter in world.Profile.ActiveMode.encounters)
                    if(encounter.currentCombat?.p6!=null)P6ObserveState(encounter.currentCombat,"encounter-record");
            if(world.State.mode!=CombatMode.RollFreeze||world.State.pendingRoll?.p6==null)return;
            var s=world.State;string ticket=s.pendingRoll.id;
            string frozen=CombatJson.Write(new{s.actors,s.enemies,s.world,s.p6,s.focusBudget,s.random,s.charges,s.momentum,s.simulationSeconds});
            if(p6FrozenId!=ticket)
            {
                p6FrozenId=ticket;p6FrozenState=frozen;p6FrozenPosition=world.BattleCamera.transform.position;p6FrozenRotation=world.BattleCamera.transform.rotation;
                Write("p6-freeze-"+(++p6CaptureSequence).ToString("00")+".json",new{ticket,world=CombatJson.Clone(s),world.RollElapsed,training=world.Training,at=Time.realtimeSinceStartup-started});
            }
            Require(Time.timeScale==0,"P6 RollFreeze has nonzero world scale");
            Require(frozen==p6FrozenState,"P6 actors/resources/RNG/Focus changed during RollFreeze");
            Require(world.BattleCamera.transform.position==p6FrozenPosition&&world.BattleCamera.transform.rotation==p6FrozenRotation,"P6 camera changed during RollFreeze");
            p6FrozenFrames.Add(new{ticket,frame=Time.frameCount,rollElapsed=world.RollElapsed,worldScale=Time.timeScale,worldUnchanged=true,cameraUnchanged=true,
                remaining=s.focusBudget.secondsRemaining,visitRemaining=CombatSession.P6VisitRemaining(s),training=world.Training});
        }
        void P6ObserveState(CombatState state,string source)
        {
            if(state?.p6==null)return;
            bool training=state.p6.training;string scope=(training?"practice/":"formal/")+state.encounterId+"/";
            foreach(var bind in state.p6.bindResults)
            {
                if(!training&&bind.markApplied&&p6ActualBindInputs.Contains(bind.id))p6FormalBind=true;
                if(p6Seen.Add(scope+"bind/"+bind.id))
                {
                    p6Ledger.Add(new{kind="bind",source,training,state.encounterId,record=CombatJson.Clone(bind),actualFInput=p6ActualBindInputs.Contains(bind.id),at=Time.realtimeSinceStartup-started});
                }
            }
            foreach(var damage in state.p6.damageResults)
            {
                if(p6Seen.Add(scope+"damage/"+damage.id))
                    p6Ledger.Add(new{kind="damage",source,training,state.encounterId,record=CombatJson.Clone(damage),state.charges,state.simulationSeconds,activeRealSeconds=world.Profile?.ActiveMode.elapsedRealSeconds,at=Time.realtimeSinceStartup-started});
                if(training||damage.actionId!=P6Smite||!damage.hit||damage.totalDamage<=0)continue;
                p6BaseDamage|=!damage.overcharged;p6FullDamage|=damage.overcharged;
                p6MarkedFullDamage|=damage.overcharged&&damage.markConsumed&&state.p6.bindResults.Any(b=>b.markId==damage.markId&&b.markApplied&&p6ActualBindInputs.Contains(b.id));
            }
            foreach(var receipt in state.receipts)
                if((receipt.result?.p6!=null||receipt.result?.chargeSpent>0)&&p6Seen.Add(scope+"receipt/"+receipt.id))
                    p6Ledger.Add(new{kind="receipt",source,training,state.encounterId,receipt=CombatJson.Clone(receipt)});
        }
        void WriteP6Observations()
        {
            // Final failure reporting must not rerun the failing Freeze assertion.
            P6ObserveState(world.State,"final");
            if(!world.Training&&world.Profile?.ActiveMode!=null)
                foreach(var encounter in world.Profile.ActiveMode.encounters)P6ObserveState(encounter.currentCombat,"final-encounter-record");
            Write("p6-observations.json",new{phase="P6.1",route=P6Route,goalsReached=P6FormalGoals,p6BaseDamage,p6FullDamage,p6MarkedFullDamage,p6FormalBind,p6RoomCleared,
                practiceRequested=P6PracticeRequested,practiceStatus=p6PracticeStatus,p6PracticeComplete,practiceFirstSmiteSeconds=p6PracticeFirstSmite,
                formalElapsedRealSeconds=p6FormalStarted<0?0:Time.realtimeSinceStartup-p6FormalStarted,activeRealSeconds=world.Profile?.ActiveMode.elapsedRealSeconds,
                attempts=p6Attempt,actions=p6Ledger,inputs=p6Inputs,freezeFrames=p6FrozenFrames,audioEvidenceFile="summary.json# audioOutput",
                usesGameplayStateFixtures=false,usesInjectedResources=false,usesFixedSuccessDice=false,usesTrainingPublicRefill=P6PracticeRequested,
                trainingScope="Optional training uses its shipping visible refill/blessing UI. All such receipts have training=true and are excluded from formal damage/clear checks.",
                formalScope="Original R1; only real InputSystem WASD/Q/F/mouse and shipping UI. Base/combo route flags are distinct bounded natural trials, not a claim that one route demonstrated the other.",
                failureScope="Missing natural hit, marked overcharge, or clear remains a failed trial. No retry restores HP, resources, positions, RNG, timers or rule configuration.",
                humanPlayers=0,humanRuns=0,normalWindowPresentationVerified=false,configuration=world.Session.Config.p6});
        }

        // Re-observe reaction/settlement after each yield. A disappearing control is logged
        // as interrupted, never reported as a successful pointer click.
        IEnumerator P6Click(string id,int retry=0)
        {
            if(id=="v13-fate-accept"||id=="v13-fate-replace")
            {yield return V13ClickFate(id,world.State.v13?.fate?.id);yield break;}
            Require(retry<5,"Repeated P6 UI interruption: "+id);
            bool reaction=id.StartsWith("reaction-");
            if(!reaction&&world.State.mode==CombatMode.ReactionPrompt)
            {yield return P6Click("reaction-decline");yield return P6Click(id,retry+1);yield break;}
            Keys();yield return null;world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            if(!reaction&&world.State.mode==CombatMode.ReactionPrompt)
            {p6Inputs.Add(new{kind="refresh_interrupted",id});yield return P6Click("reaction-decline");yield return P6Click(id,retry+1);yield break;}
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).SingleOrDefault(b=>b.name==id&&b.gameObject.activeInHierarchy);
            if(button==null||!button.interactable)
            {
                if((reaction&&world.State.activePendingEvent==null)||(!world.State.inCombat&&!id.StartsWith("front-")))
                {p6Inputs.Add(new{kind="control_expired_without_click",id,mode=world.State.mode.ToString()});yield break;}
                Require(false,"P6 UI unavailable: "+id+"; "+world.Message);
            }
            var rect=(RectTransform)button.transform;pointer=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=pointer},rays);
            Require(rays.Count>0&&(rays[0].gameObject==button.gameObject||rays[0].gameObject.transform.IsChildOf(button.transform)),"P6 UI raycast blocked: "+id+" at "+pointer+"; top="+rays.FirstOrDefault().gameObject);
            int clicked=0;UnityEngine.Events.UnityAction observed=()=>clicked++;button.onClick.AddListener(observed);
            MouseState(0);yield return null;yield return null;MouseState(1);yield return null;yield return null;MouseState(0);yield return null;yield return null;
            button.onClick.RemoveListener(observed);
            if(clicked==0&&!reaction&&world.State.mode==CombatMode.ReactionPrompt)
            {p6Inputs.Add(new{kind="pointer_interrupted",id});yield return P6Click("reaction-decline");yield return P6Click(id,retry+1);yield break;}
            if(clicked==0&&(!world.State.inCombat||reaction&&world.State.activePendingEvent==null))
            {p6Inputs.Add(new{kind="control_expired_during_pointer",id});yield break;}
            Require(clicked==1,"P6 pointer dispatched "+clicked+" clicks for "+id);
            p6Inputs.Add(new{kind="actual_pointer",id,x=pointer.x,y=pointer.y,training=world.Training,at=Time.realtimeSinceStartup-started});
        }
        IEnumerator P6RequestBind()
        {
            var mage=world.State.Actor("mage");string old=mage?.p6?.bindIntent?.id,active=world.State.activeActorId;
            int charge=world.State.charges;string slots=CombatJson.Write(mage?.spellSlots);
            yield return Press(Key.F);
            mage=world.State.Actor("mage");var intent=mage?.p6?.bindIntent;
            bool accepted=intent!=null&&intent.id!=old;
            if(accepted)p6ActualBindInputs.Add(intent.id);
            p6Inputs.Add(new{kind="actual_F",accepted,bindId=intent?.id,activeBefore=active,activeAfter=world.State.activeActorId,chargeBefore=charge,chargeAfter=world.State.charges,
                slotsBefore=slots,slotsAfter=CombatJson.Write(mage?.spellSlots),training=world.Training,at=Time.realtimeSinceStartup-started});
            Require(world.State.activeActorId==active,"F unexpectedly switched the active controller");
        }
        IEnumerator P6TrySmite(string targetId,bool full,bool bind)
        {
            var target=world.State.Actor(targetId);if(target?.Alive!=true)yield break;
            Keys();AimAt(BattleWorld.Position(target));yield return Press(Key.Q);
            if(world.State.mode!=CombatMode.TacticalFocus){p6Inputs.Add(new{kind="Q_not_interactive",world.Message});yield break;}
            int choices=world.State.enemies.Count+1;
            while(world.State.mode==CombatMode.TacticalFocus&&world.SelectedTarget!=targetId&&choices-->0)yield return P6Click("target");
            if(world.SelectedTarget!=targetId||world.State.Actor(targetId)?.Alive!=true){if(world.State.mode==CombatMode.TacticalFocus)yield return P6Click("exit");yield break;}
            if(bind&&world.Session.QuoteSmite(P6Paladin,targetId).markRemaining<=0)
            {
                var q=world.Session.QuoteBind("mage",targetId);
                if(!q.legal){p6Inputs.Add(new{kind="bind_not_legal",q.reason});yield return P6Click("exit");yield break;}
                yield return P6RequestBind();float until=Time.realtimeSinceStartup+11;
                while(world.State.mode==CombatMode.TacticalFocus&&world.State.Actor(targetId)?.Alive==true&&world.Session.QuoteSmite(P6Paladin,targetId).markRemaining<=0&&Time.realtimeSinceStartup<until)yield return null;
                if(world.State.mode!=CombatMode.TacticalFocus)yield break;
                if(world.Session.QuoteSmite(P6Paladin,targetId).markRemaining<=0){yield return P6Click("exit");yield break;}
                yield return CaptureP47("p6-"+(p6Attempt+1).ToString("00")+"-radiant-mark.png");
            }
            var quote=world.Session.QuoteSmite(P6Paladin,targetId);
            if(quote.attack?.legal!=true||!quote.interactiveAllowed||full&&quote.availableCharges<2)
            {p6Inputs.Add(new{kind="smite_not_legal",quote.reason,attackReason=quote.attack?.reason,full});yield return P6Click("exit");yield break;}
            int number=++p6Attempt;bool training=world.Training;string prefix="p6-"+number.ToString("00");
            stage=training?"p6-training-smite":"p6-formal-smite";
            yield return CaptureP47(prefix+"-focus.png");
            int before=world.State.charges;yield return P6Click("confirm");
            var ticket=world.State.pendingRoll;
            if(ticket?.p6==null){p6Inputs.Add(new{kind="confirm_interrupted",number,world.Message});yield break;}
            p6Inputs.Add(new{kind="paid_confirm",number,training,ticket=ticket.id,targetId,fullRequested=full,chargeBefore=before,chargeAfter=world.State.charges,rawFaces=ticket.rawFaces.ToArray(),at=Time.realtimeSinceStartup-started});
            Require(ticket.chargeSpent==1,"P6 accepted ticket did not spend exactly one base charge");
            yield return CaptureP47(prefix+"-freeze.png");
            yield return Wait(()=>world.State.mode!=CombatMode.RollFreeze,8,"P6 dice animation did not naturally reveal");
            if(world.State.mode==CombatMode.PostRollDecision&&world.State.pendingRoll?.id==ticket.id)
            {
                yield return CaptureP47(prefix+"-decision.png");
                bool canFull=full&&world.State.pendingRoll.hit&&world.Session.AvailableSharedCharges>0;
                yield return P6Click(canFull?"p6-full":"commit");
                p6Inputs.Add(new{kind="actual_commit_choice",number,ticket=ticket.id,overchargeRequested=canFull,training});
            }
            // Observe committed contact without adding time or commanding a hit.
            float end=Time.realtimeSinceStartup+5;
            while(Time.realtimeSinceStartup<end&&world.State.rolls.Any(t=>t.id==ticket.id&&(t.stage=="Queued"||t.stage=="Executing")))
            {if(world.State.mode==CombatMode.ReactionPrompt)yield return P6Click("reaction-decline");else yield return null;}
            ObserveP6Actions();yield return CaptureP47(prefix+"-contact.png");
        }

        IEnumerator P6Practice()
        {
            p6PracticeStatus="RUNNING";float deadline=Time.realtimeSinceStartup+210;bool blessed=false;
            while(world.Training&&world.P6Lesson<9&&Time.realtimeSinceStartup<deadline)
            {
                Require(observerFailure==null,observerFailure);ObserveP6Actions();
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return P6Click("commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                if(world.P6Lesson==8){yield return CaptureP47("training-upgrade.png");yield return P6Click("p6-up-radiant");continue;}
                if(world.State.charges==0||!world.State.enemies.Any(e=>e.Alive)||world.P6Lesson==7&&!blessed)
                {
                    if(world.State.pendingRoll!=null||world.State.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing")){yield return null;continue;}
                    bool blessing=world.P6Lesson==7;yield return P6Click("p6-refill");blessed|=blessing;
                    p6Inputs.Add(new{kind="training_public_refill",blessing,formalEvidence=false});continue;
                }
                var enemy=Enemy(false);Require(enemy!=null,"Practice target missing after public refill");
                if(world.P6Lesson==1||BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(enemy))>2.05f)
                {DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                Keys();
                if(world.P6Lesson==2){if(world.Session.P6MajorAvailable(world.ActiveActor))yield return ClickWorld(BattleWorld.Position(enemy));else yield return null;continue;}
                if(world.P6Lesson==5)
                {AimAt(BattleWorld.Position(enemy));MouseState(2);yield return new WaitForSecondsRealtime(.3f);MouseState(0);yield return null;continue;}
                if(world.P6Lesson==6)
                {if(world.Session.QuoteBind("mage",enemy.id).legal)yield return P6RequestBind();yield return new WaitForSecondsRealtime(.1f);continue;}
                if(world.Session.P6MajorAvailable(world.ActiveActor))yield return P6TrySmite(enemy.id,world.P6Lesson==7,false);else yield return null;
            }
            Keys();MouseState(0);p6PracticeFirstSmite=world.P6FirstSmiteSeconds;p6PracticeComplete=world.P6Lesson>=9;
            p6PracticeStatus=p6PracticeComplete?"COMPLETED_ACTUAL_INPUT":"FAILED_INCOMPLETE";
            Require(p6PracticeComplete,"P6 real training did not complete: lesson="+world.P6Lesson+" / "+world.P6TrainingPrompt);
            Record("p6_training_actual_input","T1–T9 through actual inputs; visible practice refill/blessing and upgrade remain isolated from formal exploration.");
            yield return CaptureP47("training-completed.png");
        }
        IEnumerator FlowP6()
        {
            Require(P6Route=="base"||P6Route=="combo"||P6Route=="both","Unknown -p6-route; use base, combo, or both");
            yield return null;yield return null;
            world.InitializeP6FrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            stage="p6-main";Require(world.FrontChoices.Select(c=>c.id).SequenceEqual(new[]{"campaign","explore","characters"}),"P6 main entries changed");
            yield return CaptureP47("00-p6-main.png");yield return FrontP47("explore");yield return FrontP47("map-ashen");
            yield return CaptureP47("01-p6-prepare.png");yield return FrontP47("depart");
            Require(world.Training&&world.P6Enabled,"Fresh P6 departure did not show isolated first training");
            yield return CaptureP47("02-p6-training.png");
            if(P6PracticeRequested)yield return P6Practice();else p6PracticeStatus="SKIPPED_VIA_SHIPPING_UI_NOT_TESTED";
            yield return P6Click("p6-finish");
            Require(!world.Training&&!world.Menu&&world.Profile?.State.activeMode==ProfileModeKind.Challenge,"Training exit did not start real exploration");
            Require(world.State.p6?.training==false&&!world.State.p6.trainingBlessingEnabled&&string.IsNullOrEmpty(world.State.p6.trainingUpgradeId),"Practice state leaked into formal exploration");
            Require(world.State.activeActorId==P6Paladin,"P6 departure active actor is not Paladin");
            p6FormalStarted=Time.realtimeSinceStartup;stage="p6-walk-original-r1";
            yield return WalkTo(()=>new Vector3(26,0,0),.6f,55,()=>world.Profile.IsInEncounter);
            Require(world.Profile.IsInEncounter&&world.Profile.ActiveRoom.templateId=="R1","Actual walking did not activate original R1");
            Require(world.Profile.ActiveEncounter.initialCharge==2,"P6 formal encounter starting grant is not 2");
            p6Encounter=world.Profile.ActiveEncounter.id;yield return CaptureP47("03-p6-original-r1.png");
            float deadline=Time.realtimeSinceStartup+180,nextAttempt=0;
            while(world.Profile.ActiveEncounter?.id==p6Encounter&&Time.realtimeSinceStartup<deadline)
            {
                Require(observerFailure==null,observerFailure);ObserveP6Actions();Require(world.State.Actor(P6Paladin).Alive,"Paladin defeated in natural P6 trial");
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return P6Click("commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                var enemy=Enemy(false);if(enemy==null){Keys();yield return null;continue;}
                bool wantBase=P6Route!="combo"&&!p6BaseDamage,wantCombo=!wantBase&&P6Route!="base"&&!p6MarkedFullDamage;
                var paladin=world.State.Actor(P6Paladin);float distance=BattleGeometry.DistanceXZ(BattleWorld.Position(paladin),BattleWorld.Position(enemy));
                if(distance>2.05f||!world.HasLineOfEffect(BattleWorld.Position(paladin),BattleWorld.Position(enemy)))
                {DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                Keys();
                bool enough=world.Session.AvailableSharedCharges>=(wantCombo?2:1);
                bool mageReady=!wantCombo||world.Session.QuoteBind("mage",enemy.id).legal||world.Session.QuoteSmite(P6Paladin,enemy.id).markRemaining>0;
                if(enough&&mageReady&&world.Session.P6MajorAvailable(paladin)&&Time.realtimeSinceStartup>=nextAttempt)
                {yield return P6TrySmite(enemy.id,wantCombo,wantCombo);nextAttempt=Time.realtimeSinceStartup+.4f;continue;}
                if(P6FormalGoals&&world.Session.P6MajorAvailable(paladin))
                {stage="p6-clear-original-r1";yield return ClickWorld(BattleWorld.Position(enemy));continue;}
                // Waiting for real charge/cooldown is ordinary defence, not clock injection.
                // Ally AI continues its unchanged shipping policy throughout this interval.
                AimAt(BattleWorld.Position(enemy));MouseState(2);yield return new WaitForSecondsRealtime(.18f);MouseState(0);yield return null;
            }
            Keys();MouseState(0);ObserveP6Actions();
            if(!world.State.enemies.Any(e=>e.Alive))yield return Wait(()=>world.Profile.ActiveMode.Room("R1").resolved,6,"P6 last contact did not settle R1");
            p6RoomCleared=world.Profile.ActiveMode.Room("R1").resolved;WriteP6Observations();
            Require(P6FormalGoals,"Natural P6 "+P6Route+" route incomplete: base="+p6BaseDamage+", markedFull="+p6MarkedFullDamage+", actualF="+p6FormalBind+"; preserve random/clear failure");
            Require(p6RoomCleared,"Natural P6 R1 did not clear within the bounded real-input trial");
            Require(p6FrozenFrames.Count>0,"No actual P6 RollFreeze frame observed");
            yield return CaptureP47("04-p6-room-complete.png");
            Record("p6_formal_natural_input",P6Route+" route: real Q confirmation, observed committed damage and original R1 clear; no injected gameplay state.");
            completed=true;
        }
    }
}
