using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CombatV051.Tests
{
    // Engine contracts, not natural play or balance evidence. This fixture places
    // actors and holds background attacks, and deliberately sets zero charges in
    // the free-action case. It never selects, retries or replaces a d20 outcome.
    public sealed class V12HeroPlayTests
    {
        const string Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn";
        BattleWorld world;Keyboard keyboard;Mouse mouse;GameObject wall;string directory,path;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        ActorState Actor(string id)=>world.State.Actor(id);
        ActorState Target=>world.State.enemies[0];
        static void Place(ActorState a,float x,float z){a.x=x;a.z=z;a.heightY=0;}
        static void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);

        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;updateMode=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Hero-Engine",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            world=new GameObject("V12 isolated engine fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(path);
            world.ChooseFrontend("characters");yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Hero-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        }
        Button VisibleButton(string id)=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            var button=VisibleButton(id);Assert.That(button.interactable,Is.True,id+"; "+world.Message);
            var rect=(RectTransform)button.transform;var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Assert.That(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),Is.True,id+" must be the top actual pointer target.");
            int clicks=0;UnityEngine.Events.UnityAction observer=()=>clicks++;button.onClick.AddListener(observer);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
            button.onClick.RemoveListener(observer);Assert.That(clicks,Is.EqualTo(1),"Use real pointer dispatch; never Invoke the button callback.");
        }
        IEnumerator Until(Func<bool> predicate,float timeout,string reason)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();while(!predicate()&&timer.Elapsed.TotalSeconds<timeout)yield return null;
            Assert.That(predicate(),Is.True,reason+"; "+world.Message);
        }
        IEnumerator Arena(string active)
        {
            var purchase=world.Profile.UnlockV11Node(active,active+"/passive","fixture-passive/"+active);
            Assert.That(purchase.success,Is.True,purchase.reason);
            world.StartMainCharacterTraining();Assert.That(world.V12Enabled,Is.True);Assert.That(world.MainCharacterTraining,Is.True);
            world.enabled=false;
            if(world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            for(int i=0;i<world.State.actors.Count;i++)
            {
                var actor=world.State.actors[i];Place(actor,-9+i,-8);actor.majorRemaining=100;
                actor.guardRemaining=actor.dodgeRemaining=actor.windupRemaining=actor.staggerRemaining=0;actor.aimPreparing=false;
                actor.basicAttackIntent=null;
            }
            Place(Actor(active),-4,-4);Actor(active).majorRemaining=0;
            for(int i=0;i<world.State.enemies.Count;i++)
            {
                var enemy=world.State.enemies[i];Place(enemy,-1+i*3,-4);enemy.aiState=2;enemy.aiRemaining=100;
                enemy.lockedX=enemy.x+1;enemy.lockedZ=enemy.z;
            }
            Good(world.SwitchActive(active));world.SelectedActor=active;world.SelectedTarget=Target.id;
            world.SelectedAction=BattleWorld.V12BasicAction(active);world.ConfigureDiceAnimationSpeed(DiceAnimationSpeed.Standard);
            world.Rig.Step(10);Physics.SyncTransforms();world.enabled=true;yield return null;
        }
        IEnumerator Aim(Vector3 ground)
        {
            world.Rig.Step(10);Vector3 screen=world.BattleCamera.WorldToScreenPoint(ground);
            Assert.That(screen.z,Is.GreaterThan(0));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)});yield return null;yield return null;
            Assert.That(BattleGeometry.DistanceXZ(world.WorldAimPoint,ground),Is.LessThan(.15f),"Real pointer must establish the intended ground coordinate.");
        }

        [UnityTest] public IEnumerator EvanActualFFreeAtZeroChargesAndQSelectsCurrentHeroKey()
        {
            yield return Arena(Evan);world.State.charges=0;int rolls=world.State.rolls.Count;
            yield return Press(Key.F);
            Assert.That(Actor(Evan).v12.intent,Is.Not.Null,world.Message);
            Assert.That(Actor(Evan).v12.intent.actionId,Is.EqualTo("v12_evan_mark"));
            yield return Until(()=>Actor(Evan).v12.intent.stage=="Resolved",3,"Mark must complete through the real Update bridge");
            Assert.That(Target.statuses.Any(s=>s.v12?.family=="opening"&&s.sourceId==Evan),Is.True);
            Assert.That(world.State.charges,Is.Zero);Assert.That(world.State.rolls.Count,Is.EqualTo(rolls));
            yield return Press(Key.Q);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus),world.Message);
            Assert.That(world.SelectedActor,Is.EqualTo(Evan));Assert.That(world.SelectedAction,Is.EqualTo("v12_evan_final"));
            Assert.That(world.State.pendingRoll,Is.Null);Assert.That(world.State.charges,Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator AllThreeFreeActionScreenButtonsKeepHeroLabelsAndActuallyClickAtZeroCharges()
        {
            foreach(string actorId in new[]{Evan,Mira,Finn})
            {
                yield return Arena(actorId);world.State.charges=0;world.Hud.RefreshView();yield return null;
                var button=VisibleButton("p6-bind");string title=button.GetComponentInChildren<Text>().text;
                Assert.That(title,Does.Contain(V11AbilityCatalog.Name(BattleWorld.V12BasicAction(actorId))),actorId);
                Assert.That(title,Does.Contain("免费").And.Not.Contain("裂光"),actorId);
                Assert.That(button.interactable,Is.True,"Missing legacy RadianceCaster cannot disable a protagonist's free skill.");
                int rolls=world.State.rolls.Count;yield return Click("p6-bind");
                Assert.That(Actor(actorId).v12.intent,Is.Not.Null,actorId+"; "+world.Message);
                Assert.That(Actor(actorId).v12.intent.actionId,Is.EqualTo(BattleWorld.V12BasicAction(actorId)));
                Assert.That(world.State.charges,Is.Zero);Assert.That(world.State.rolls.Count,Is.EqualTo(rolls));
                yield return Until(()=>Actor(actorId).v12.intent.stage=="Resolved",3,actorId+" clicked skill completes through Update");
                world.Hud.RefreshView();Assert.That(VisibleButton("p6-bind").interactable,Is.False,"Completed free skill reports its real cooldown.");
                world.EndTraining();yield return null;
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator CommittedHeroRollCancelsOnFirstExecutionFrameWhenTargetLeavesRangeWithoutChasingOrRefund()
        {
            yield return Arena(Evan);int originalCharges=world.State.charges;
            yield return Press(Key.Q);Good(world.ConfirmCurrentDecision());
            int naturalFace=world.State.pendingRoll.selectedFace;string ticketId=world.State.pendingRoll.id;
            yield return Until(()=>world.State.mode==CombatMode.PostRollDecision,5,"Unmodified natural roll animation completes");
            world.enabled=false;Good(world.ConfirmCurrentDecision());
            string queueId=world.State.queue.Last().id;Assert.That(world.State.queue.Last().stage,Is.EqualTo("Queued"));
            Vector3 originalPosition=BattleWorld.Position(Actor(Evan));Place(Target,7,-4);Physics.SyncTransforms();
            Assert.That(world.HasLineOfEffect(originalPosition,BattleWorld.Position(Target)),Is.True,"This tests out-of-range cancellation, not a wall shortcut.");
            Assert.That(BattleGeometry.DistanceXZ(originalPosition,BattleWorld.Position(Target)),Is.GreaterThan(world.Session.Config.v12.finalRange));
            Good(world.Command(CommandKind.ExitFocus));int frames=world.SimulatedFrames;CombatCommand published=null;int starts=0;
            Action<CombatCommand,CombatResult> observe=(c,r)=>{if(c.kind==CommandKind.StartQueued&&c.queueId==queueId){starts++;published=CombatJson.Clone(c);}};
            world.CommandPublished+=observe;world.enabled=true;yield return null;
            world.CommandPublished-=observe;
            Assert.That(world.SimulatedFrames-frames,Is.EqualTo(1));
            var queue=world.State.queue.Single(q=>q.id==queueId);Assert.That(queue.stage,Is.EqualTo("Canceled"));
            Assert.That(queue.reason,Is.EqualTo("execution_position_illegal"));Assert.That(starts,Is.EqualTo(1));Assert.That(published,Is.Not.Null);
            Assert.That(BattleWorld.Position(Actor(Evan)),Is.EqualTo(originalPosition),"A paid V12 roll must not carry its result while automatically chasing.");
            Assert.That(world.State.charges,Is.EqualTo(originalCharges-1));Assert.That(Actor(Evan).heldBy,Is.Null.Or.Empty);
            var replay=world.ExecuteWorldCommand(published);Assert.That(replay.success&&replay.duplicate,Is.True);
            Assert.That(world.State.charges,Is.EqualTo(originalCharges-1));Assert.That(world.State.rolls.Single(t=>t.id==ticketId).selectedFace,Is.EqualTo(naturalFace));
            yield return null;Assert.That(world.State.queue.Single(q=>q.id==queueId).stage,Is.EqualTo("Canceled"));
            Assert.That(BattleWorld.Position(Actor(Evan)),Is.EqualTo(originalPosition));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator MiraChaseRejectsRealWallThenMovesBeforeBackgroundRollAndRetainsLandingForNaturalOutcome()
        {
            yield return Arena(Mira);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="V12 chase blocking physics fixture";wall.layer=8;
            wall.transform.position=new Vector3(-2.55f,1,-4);wall.transform.localScale=new Vector3(.4f,2,3);Physics.SyncTransforms();
            Vector3 start=BattleWorld.Position(Actor(Mira));int charges=world.State.charges;
            ulong draws=world.State.random.backgroundCombat.drawCount;
            var rejected=world.RequestCurrentBasicAction();Assert.That(rejected.success,Is.False);Assert.That(rejected.reason,Is.EqualTo("v12_chase_path_blocked"));
            Assert.That(BattleWorld.Position(Actor(Mira)),Is.EqualTo(start));Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            Assert.That(Actor(Mira).v12.basicCooldown,Is.Zero);
            UnityEngine.Object.Destroy(wall);yield return null;wall=null;Physics.SyncTransforms();
            Good(world.RequestCurrentBasicAction());Assert.That(Actor(Mira).v12.intent.stage,Is.EqualTo("Moving"));
            Assert.That(BattleWorld.Position(Actor(Mira)),Is.EqualTo(start),"Request does not teleport the actor.");
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws),"Background hit check is deferred until movement completes.");
            yield return Until(()=>Actor(Mira).v12.intent.stage=="Resolved",3,"Real collision-constrained chase movement and contact");
            var moved=Actor(Mira);Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(moved),start),Is.GreaterThan(world.Session.Config.v12.chaseMinimumTravel));
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(moved),new Vector3(moved.v12.intent.destinationX,0,moved.v12.intent.destinationZ)),Is.LessThanOrEqualTo(world.Session.Config.orderArrivalDistance+.02f));
            Assert.That(moved.v12.readinessRemaining,Is.GreaterThan(0));Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.GreaterThan(draws));
            yield return Press(Key.Q);Assert.That(world.SelectedActor,Is.EqualTo(Mira));Assert.That(world.SelectedAction,Is.EqualTo("v12_mira_break"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator FinnGroveUsesActualGroundPreviewRejectsRangeAndResolvesOnePaidAreaWithoutVisibleD20()
        {
            yield return Arena(Finn);yield return Aim(new Vector3(-.5f,0,-4));yield return Press(Key.Q);
            Assert.That(world.SelectedActor,Is.EqualTo(Finn));Assert.That(world.SelectedAction,Is.EqualTo("v12_finn_grove"));
            world.RefreshTacticalPreview();var root=world.transform.Find("Tactical spatial preview");Assert.That(root,Is.Not.Null);Assert.That(root.gameObject.activeInHierarchy,Is.True);
            var preview=root.Find("Current target").GetComponent<AreaPreview>();
            Assert.That(preview.Shape.Radius,Is.EqualTo(world.Session.Config.v12.groveRadius));
            Assert.That(BattleGeometry.DistanceXZ(preview.Shape.Origin,world.WorldAimPoint),Is.LessThan(.001f));
            int charges=world.State.charges,rolls=world.State.rolls.Count;Vector3 legal=BattleWorld.Position(Actor(Finn));
            // Explicit out-of-range actor placement leaves the real selected point unchanged.
            Place(Actor(Finn),-10,-8);Assert.That(world.SelectedActionQuote().legal,Is.False);
            var refused=world.ConfirmCurrentDecision();Assert.That(refused.success,Is.False);Assert.That(world.State.charges,Is.EqualTo(charges));
            Place(Actor(Finn),legal.x,legal.z);Physics.SyncTransforms();Good(world.ConfirmCurrentDecision());
            Assert.That(world.State.charges,Is.EqualTo(charges-1));Assert.That(world.State.rolls.Count,Is.EqualTo(rolls));
            Assert.That(world.State.mode,Is.Not.EqualTo(CombatMode.RollFreeze));Good(world.Command(CommandKind.ExitFocus));
            yield return Until(()=>world.State.v12.groves.Count==1,3,"Grove cast must create one authoritative area");
            var grove=world.State.v12.groves.Single();Assert.That(grove.radius,Is.EqualTo(preview.Shape.Radius));
            Assert.That(Target.statuses.Any(s=>s.v12?.family=="parasite"),Is.True);Assert.That(Target.statuses.Any(s=>s.v12?.family=="entangle"),Is.True);
            Assert.That(Actor(Evan).statuses.Any(s=>s.v12?.family=="entangle"),Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator TwoSecondNaturalRollFreezesHeroTimersStatusesAndFocusThenReturnsToSlowDecision()
        {
            yield return Arena(Evan);Good(world.RequestCurrentBasicAction());
            yield return Until(()=>Actor(Evan).v12.intent.stage=="Resolved"&&Actor(Evan).majorRemaining<=0,4,"Mark and normal Action recovery complete");
            yield return Press(Key.Q);Good(world.ConfirmCurrentDecision());
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(world.CurrentRollDuration,Is.EqualTo(2).Within(.001f));
            string ticket=world.State.pendingRoll.id;int face=world.State.pendingRoll.selectedFace;
            string timers=CombatJson.Write(new{world.State.v12,hero=Actor(Evan).v12,Target.statuses,world.State.focusBudget,world.State.simulationSeconds});
            int frames=0;var clock=System.Diagnostics.Stopwatch.StartNew();
            while(world.State.mode==CombatMode.RollFreeze&&clock.Elapsed.TotalSeconds<5)
            {
                Assert.That(Time.timeScale,Is.Zero);
                Assert.That(CombatJson.Write(new{world.State.v12,hero=Actor(Evan).v12,Target.statuses,world.State.focusBudget,world.State.simulationSeconds}),Is.EqualTo(timers));
                Assert.That(world.State.pendingRoll.selectedFace,Is.EqualTo(face));frames++;yield return null;
            }
            Assert.That(frames,Is.GreaterThan(1));Assert.That(clock.Elapsed.TotalSeconds,Is.GreaterThan(1.7));
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));
            Assert.That(Time.timeScale,Is.GreaterThan(0).And.LessThan(1));
            Assert.That(world.State.pendingRoll.selectedFace,Is.EqualTo(face),"Natural original face survives reveal without replacement.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator EndingV12ArenaRestoresOriginalProfileWithoutExportingAbilitiesOrStatusState()
        {
            var profile=world.Profile;string id=profile.ActiveMode.id;
            string actors=CombatJson.Write(profile.ActiveCombat.State.actors),random=CombatJson.Write(profile.ActiveCombat.State.random);
            string meta=CombatJson.Write(profile.State.meta),config=CombatJson.Write(profile.ActiveCombat.Config);
            world.StartMainCharacterTraining();Assert.That(world.V12Enabled,Is.True);Assert.That(world.Profile,Is.Null);
            world.enabled=false;world.State.charges=0;Actor(Finn).hp=1;Actor(Finn).v12.basicCooldown=7;
            world.FinishP6Training();Assert.That(world.Training,Is.False);Assert.That(world.Profile,Is.SameAs(profile));Assert.That(world.Profile.ActiveMode.id,Is.EqualTo(id));
            Assert.That(CombatJson.Write(world.State.actors),Is.EqualTo(actors));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(random));
            Assert.That(CombatJson.Write(world.Profile.State.meta),Is.EqualTo(meta));Assert.That(CombatJson.Write(world.Session.Config),Is.EqualTo(config));
            Assert.That(File.Exists(Path.Combine(directory,"presentation","p6-smite-tutorial.txt")),Is.False);
            yield return null;LogAssert.NoUnexpectedReceived();
        }
    }
}
