using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool p5Mode;
        readonly List<object> p5Actions=new List<object>();
        readonly HashSet<string> p5Observed=new HashSet<string>();
        readonly Dictionary<string,int> p5FollowupStartingCharges=new Dictionary<string,int>();
        bool p5BraceRoute,p5FrostRoute,p5ZeroPreparation,p5ZeroFollowup;
        string P5Route {get {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-p5-route");return i>=0&&i+1<args.Length?args[i+1]:"frost";}}
        public void BeginP5(BattleWorld owner,string path) { p5Mode=true;p4Mode=true;Begin(owner,path); }
        void ObserveP5Actions()
        {
            if(!p5Mode||!world.P5Enabled||world.State.p5==null)return;
            foreach(var t in world.State.rolls.Where(t=>t.p5!=null&&t.stage=="Resolved"))
            {
                if(!p5Observed.Add(t.id))continue;
                p5Actions.Add(new{kind="paid_result",t.id,t.actorId,t.targetId,t.actionId,t.rawFaces,t.hit,t.p5,
                    world.State.charges,world.State.simulationSeconds,activeRealSeconds=world.Profile?.ActiveMode.elapsedRealSeconds});
                p5BraceRoute|=t.p5.ownerId=="fighter"&&t.actorId=="rogue"&&t.p5.opportunityKind=="off_balance"&&t.p5.effective;
                p5FrostRoute|=t.p5.ownerId=="mage"&&t.actorId=="fighter"&&t.p5.opportunityKind=="bound"&&t.p5.effective;
            }
            foreach(var a in world.State.actors)
            {
                var i=a.basicAttackIntent;if(i?.p5==null||i.actionId!="basic_followup")continue;
                if(!i.resolved&&!p5FollowupStartingCharges.ContainsKey(i.id))
                {p5FollowupStartingCharges[i.id]=world.State.charges;p5Actions.Add(new{kind="free_started",i.id,actorId=a.id,startingCharges=world.State.charges,i.targetId,i.p5.opportunityId});}
                if(!i.resolved||!p5Observed.Add(i.id))continue;
                int startingCharges=p5FollowupStartingCharges.TryGetValue(i.id,out var amount)?amount:-1;
                p5Actions.Add(new{kind="free_result",i.id,actorId=a.id,startingCharges,i.targetId,i.p5,world.State.charges,world.State.simulationSeconds});
                p5ZeroFollowup|=startingCharges==0&&i.p5.effective;
            }
        }
        IEnumerator P5FreeActor(string id)
        {
            if(!world.FreeOrdersVisible)yield return ClickUi("free-orders");
            yield return ClickUi("active-"+id);
        }
        IEnumerator P5Preparation(string actor)
        {
            yield return P5FreeActor(actor);
            if(world.SelectedTarget==null||world.State.Actor(world.SelectedTarget)?.Alive!=true)
                yield return ClickWorld(BattleWorld.Position(Enemy()));
            bool zero=world.State.charges==0;string before=world.State.Actor(actor).p5?.preparation?.sourceRoot;
            yield return ClickUi("order-prepare");
            if(world.PendingOrderLabel!=null)yield return ClickUi("order-replace");
            var prep=world.State.Actor(actor).p5?.preparation;
            var accepted=prep==null?null:world.State.receipts.FirstOrDefault(r=>r.id==prep.sourceRoot&&r.result.success);
            if(prep!=null&&prep.sourceRoot!=before&&accepted!=null)
            {p5ZeroPreparation|=zero&&world.State.charges==0;p5Actions.Add(new{kind="actual_preparation_input",actor,zeroCharges=zero&&world.State.charges==0,commandId=accepted.id,world.State.simulationSeconds});}
        }
        IEnumerator P5PaidOpportunity(SetupToken token,string actor,string action)
        {
            // Approach BEFORE rolling. No illegal-position roll is carried into range.
            if(world.FreeOrdersVisible)yield return ClickUi("free-orders");
            yield return Press(actor=="rogue"?Key.Digit2:actor=="mage"?Key.Digit3:Key.Digit1);
            float approachUntil=Time.realtimeSinceStartup+6;
            while(world.State.Actor(token.targetId)?.Alive==true&&token.remaining>0&&Time.realtimeSinceStartup<approachUntil&&
                (BattleGeometry.DistanceXZ(BattleWorld.Position(world.State.Actor(actor)),BattleWorld.Position(world.State.Actor(token.targetId)))>2.2f||!world.HasLineOfEffect(BattleWorld.Position(world.State.Actor(actor)),BattleWorld.Position(world.State.Actor(token.targetId)))))
            {
                if(world.State.mode==CombatMode.ReactionPrompt){Keys();yield return ClickUi("reaction-decline");continue;}
                DriveP4Combat(BattleWorld.Position(world.State.Actor(token.targetId)));yield return null;
                token=world.State.p5.opportunities.FirstOrDefault(t=>t.id==token.id);if(token==null)break;
            }
            Keys();if(token==null||token.remaining<=0||world.State.Actor(token.targetId)?.Alive!=true)yield break;
            yield return Press(Key.Tab);
            if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            yield return ClickUi(actor);yield return ClickUi(action);
            int cycles=world.State.enemies.Count;
            while(world.SelectedTarget!=token.targetId&&cycles-->0)yield return ClickUi("target");
            var choices=world.OpportunityChoices();int slot=choices.FindIndex(c=>c.id==token.id);
            if(slot<0){yield return ClickUi("exit");yield break;}
            yield return ClickUi("p5-opportunity-"+slot);
            if(!world.SelectedActionQuote().legal){p5Actions.Add(new{kind="illegal_preview",reason=world.SelectedActionQuote().reason,actor,action,distance=BattleGeometry.DistanceXZ(BattleWorld.Position(world.State.Actor(actor)),BattleWorld.Position(world.State.Actor(token.targetId))),lineOfSight=world.HasLineOfEffect(BattleWorld.Position(world.State.Actor(actor)),BattleWorld.Position(world.State.Actor(token.targetId)))});yield return ClickUi("exit");yield break;}
            yield return CaptureP47("p5-opportunity-"+token.p5.kind+".png");
            freezeArmed=true;yield return ClickUi("confirm");
            yield return Wait(()=>world.State.mode!=CombatMode.RollFreeze,8,"P5 RollFreeze did not reveal");
            if(world.State.mode==CombatMode.PostRollDecision)
            {yield return CaptureP47("p5-result-"+token.p5.kind+".png");yield return ClickUi("commit");}
            freezeArmed=false;
            if(world.State.mode==CombatMode.TacticalFocus)yield return ClickUi("exit");
        }
        IEnumerator FlowP5()
        {
            yield return null;yield return null;
            world.InitializeFrontendForTests(Path.Combine(directory,"Profiles","profile.json"),true);
            stage="p5-ordinary-main";yield return CaptureP47("00-main.png");
            Require(world.FrontChoices.Select(c=>c.id).SequenceEqual(new[]{"campaign","explore","characters"}),"P5 lost three ordinary entries");
            yield return FrontP47("explore");yield return FrontP47("map-ashen");yield return FrontP47("depart");
            Require(world.P5Enabled&&world.State.actors.Count==3,"New departure did not use actual P5 party rules");
            stage="p5-walk-original-r1";
            yield return WalkTo(()=>new Vector3(26,0,0),.6f,55,()=>world.Profile.IsInEncounter);
            yield return Wait(()=>world.Profile.IsInEncounter,10,"No ordinary R1 encounter");
            Require(world.Profile.ActiveRoom.templateId=="R1"&&world.Profile.ActiveEncounter.initialCharge==2,"Wrong room or starting grant");
            yield return CaptureP47("01-existing-r1.png");
            string id=world.Profile.ActiveEncounter.id;
            bool zeroMode=Array.IndexOf(Environment.GetCommandLineArgs(),"-p5-zero-route")>=0;
            float deadline=Time.realtimeSinceStartup+150;
            float nextPrepare=0;
            while(world.Profile.ActiveEncounter?.id==id&&Time.realtimeSinceStartup<deadline)
            {
                ObserveP5Actions();Require(world.State.actors.Any(a=>a.Alive),"Party defeated in natural P5 trial");
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.ReactionPrompt){yield return ClickUi("reaction-decline");continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return ClickUi(world.State.pendingRoll?.purpose=="Reaction"?"reaction-commit":"commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return ClickUi("exit");continue;}
                if(world.FreeOrdersVisible)yield return ClickUi("free-orders");
                if(world.State.activeActorId!="fighter"&&world.State.Actor("fighter").Alive){yield return Press(Key.Digit1);continue;}
                var enemy=Enemy(false);if(enemy==null)break;
                var fighter=world.State.Actor("fighter");
                var tokens=world.State.p5.opportunities.Where(t=>!t.consumed&&t.remaining>0&&t.p5.status=="Available").ToArray();
                var brace=tokens.FirstOrDefault(t=>t.ownerId=="fighter"&&t.p5.kind=="off_balance");
                var frost=tokens.FirstOrDefault(t=>t.ownerId=="mage"&&t.p5.kind=="bound");
                if(!zeroMode&&P5Route=="brace"&&!p5BraceRoute&&brace!=null&&world.State.charges>0&&world.State.Actor("rogue").Alive)
                {stage="p5-brace-to-rogue";yield return P5PaidOpportunity(brace,"rogue","sneak");continue;}
                if(!zeroMode&&P5Route=="frost"&&!p5FrostRoute&&frost!=null&&world.State.charges>0&&fighter.Alive)
                {stage="p5-frost-to-fighter";yield return P5PaidOpportunity(frost,"fighter","shove");continue;}
                if(zeroMode&&world.State.charges==0&&frost!=null)
                {
                    // F explicitly commands the current fighter with the panel closed. Avoid
                    // spending the opportunity lifetime opening a panel we do not need.
                    stage="p5-zero-charge-followup";
                    if(world.SelectedTarget!=frost.targetId)yield return ClickWorld(BattleWorld.Position(world.State.Actor(frost.targetId)));
                    yield return Press(Key.F);if(world.PendingOrderLabel!=null)yield return ClickUi("order-replace");
                    yield return CaptureP47("04-zero-charge-followup.png");
                    yield return new WaitForSecondsRealtime(.4f);continue;
                }
                bool routeDone=P5Route=="brace"?p5BraceRoute:p5FrostRoute;
                bool needPrep=zeroMode?world.State.charges==0:!routeDone;
                string producer=!zeroMode&&P5Route=="brace"?"fighter":"mage";
                var preparer=world.State.Actor(producer);
                if(needPrep&&Time.realtimeSinceStartup>=nextPrepare&&preparer.Alive&&preparer.preparationCooldown<=0&&preparer.windupRemaining<=0&&preparer.guardRemaining<=0&&string.IsNullOrEmpty(preparer.heldBy))
                {
                    stage="p5-prepare-"+producer;
                    if(producer=="fighter"&&BattleGeometry.DistanceXZ(BattleWorld.Position(fighter),BattleWorld.Position(enemy))>2.5f)
                    {DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                    Keys();yield return P5Preparation(producer);nextPrepare=Time.realtimeSinceStartup+.6f;continue;
                }
                if(zeroMode&&world.State.charges>0&&fighter.Alive&&fighter.p5.shoveCooldown<=0&&world.State.focusBudget.secondsRemaining>1)
                {
                    if(BattleGeometry.DistanceXZ(BattleWorld.Position(fighter),BattleWorld.Position(enemy))>2.3f)
                    {DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                    Keys();yield return Press(Key.Tab);if(world.State.mode!=CombatMode.TacticalFocus)continue;
                    yield return ClickUi("fighter");yield return ClickUi("shove");
                    int cycles=world.State.enemies.Count;while(!world.SelectedActionQuote().legal&&cycles-->0)yield return ClickUi("target");
                    if(world.SelectedActionQuote().legal){freezeArmed=true;yield return ClickUi("confirm");}
                    else yield return ClickUi("exit");continue;
                }
                if((!zeroMode&&routeDone)||(zeroMode&&p5ZeroPreparation&&p5ZeroFollowup))
                {stage="p5-clear-existing-room";yield return P4Combat();break;}
                Keys();yield return null;
            }
            ObserveP5Actions();
            if(!world.State.enemies.Any(e=>e.Alive))yield return Wait(()=>world.Profile.ActiveMode.Room("R1").resolved,6,"Actual final-hit impact/encounter settlement did not finish");
            Write("p5-routes.json",new{route=P5Route,zeroMode,p5BraceRoute,p5FrostRoute,p5ZeroPreparation,p5ZeroFollowup,actions=p5Actions,
                runtimeConfig=world.Session.Config.p5,activeRealSeconds=world.Profile.ActiveMode.elapsedRealSeconds,
                world.State.simulationSeconds,usesInjectedResources=false,usesFixedSuccessDice=false,humanPlaytest=false});
            Require(zeroMode?p5ZeroPreparation&&p5ZeroFollowup:P5Route=="brace"?p5BraceRoute:p5FrostRoute,"Natural P5 routes not demonstrated; preserve failed attempt without declaring acceptance");
            Require(world.Profile.ActiveMode.Room("R1").resolved,"Collaboration room was not cleared");
            yield return CaptureP47("05-room-complete.png");
            Record("p5_natural_input_room",zeroMode?"Zero-charge preparation and effective followup through actual inputs":"One effective "+P5Route+" ally route and existing R1 clear through actual inputs; combine distinct recorded runs for two-route acceptance");
            completed=true;
        }
    }
}
