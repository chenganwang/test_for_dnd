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
    /// <summary>
    /// Engine integration with Resources configs, actual Update, Input System and UI raycasts.
    /// Fixtures place the original party in the existing arena, suppress unrelated enemy attacks,
    /// and inject qualified contact batches / known background RNG. They are not natural Player playthroughs.
    /// </summary>
    public sealed class P4ExperiencePlayTests
    {
        BattleWorld world;
        Keyboard keyboard;Mouse mouse;
        string directory;
        InputSettings.BackgroundBehavior background;
        InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        ActorState Fighter=>world.State.Actor("fighter");
        ActorState Rogue=>world.State.Actor("rogue");
        ActorState Mage=>world.State.Actor("mage");
        ActorState Enemy=>world.State.enemies[0];
        static void Place(ActorState actor,Vector3 p){actor.x=p.x;actor.z=p.z;actor.heightY=p.y;}

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
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P4-Experience",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            Assert.That(Resources.Load<TextAsset>("CombatConfig"),Is.Not.Null,"These tests must read the actual shipped resource config.");
            world=new GameObject("P4 actual resource experience fixture").AddComponent<BattleWorld>();
            world.InitializePartyForTests(Path.Combine(directory,"party.json"));world.QuickDice=false;
            Place(Fighter,Vector3.zero);Place(Rogue,new Vector3(1,0,-1));Place(Mage,new Vector3(-1,0,-1));Place(Enemy,new Vector3(3,0,0));
            Enemy.aiState=2;Enemy.aiRemaining=1000;Enemy.lockedX=5;Enemy.lockedZ=0;
            foreach(var id in world.State.actors.Select(a=>a.id).ToArray())
            {
                world.State.Actor(id).majorRemaining=100; // Isolate the observed action from autonomous basic attacks.
                var held=world.ExecuteWorldCommand(new CombatCommand{id="fixture-hold/"+id,kind=CommandKind.IssueBasicOrder,actorId=id,actionId="Hold"});
                Assert.That(held.success,Is.True,held.reason);
            }
            world.SelectedTarget=Enemy.id;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)});
            Physics.SyncTransforms();yield return null;yield return null;
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P4-Experience"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        IEnumerator Press(Key key)
        {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;}
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id);
            Assert.That(button.isActiveAndEnabled&&button.interactable,Is.True,id+"; "+world.Message);
            var rect=(RectTransform)button.transform;Vector2 p=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},hits);
            Assert.That(p.x,Is.InRange(0f,(float)Screen.width));Assert.That(p.y,Is.InRange(0f,(float)Screen.height));
            Assert.That(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),Is.True,
                id+" actual raycast: "+string.Join(",",hits.Select(h=>h.gameObject.name))+"; "+p+"; "+world.Message);
            int observed=0;button.onClick.AddListener(()=>observed++); // Observe dispatch; never invoke callbacks.
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;yield return null;
            Assert.That(observed,Is.EqualTo(1),id+" pointer dispatch");
        }
        IEnumerator ClickGround(Vector3 target)
        {
            Vector2 p=world.BattleCamera.WorldToScreenPoint(target);var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},hits);
            Assert.That(hits,Is.Empty,"Fixture ground point must not be covered by HUD.");
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;
        }
        IEnumerator Until(Func<bool> condition,float seconds,string reason,Func<string> diagnostic=null)
        {
            var waitClock=System.Diagnostics.Stopwatch.StartNew();
            float start=Time.realtimeSinceStartup,simulationStart=world.State.simulationSeconds;
            int frameStart=Time.frameCount,frames=0;
            // Headless throughput must not shorten a wall-clock deadline for slow-motion movement.
            while(!condition()&&waitClock.Elapsed.TotalSeconds<seconds){frames++;yield return null;}
            if(diagnostic!=null)TestContext.WriteLine("P47_REAR_WAIT elapsed="+waitClock.Elapsed.TotalSeconds.ToString("R")+
                " engineElapsed="+(Time.realtimeSinceStartup-start).ToString("R")+
                " limit="+seconds+" loopCounter="+frames+" actualFrameDelta="+(Time.frameCount-frameStart)+
                " simulationDelta="+(world.State.simulationSeconds-simulationStart).ToString("R")+"; "+diagnostic());
            Assert.That(condition(),Is.True,reason+"; "+world.Message);
        }
        string Costs()=>CombatJson.Write(world.State.actors.Select(a=>new{a.id,a.inspiration,a.superiority,a.spellSlots,a.items}));
        static RngState FaceStream(params int[] faces)
        {
            for(ulong seed=1;seed<100000;seed++)
            {var initial=RandomStreams.Create(seed).backgroundCombat;var probe=CombatJson.Clone(initial);if(faces.All(f=>RandomStreams.D20(probe)==f))return initial;}
            throw new InvalidOperationException("No deterministic contact fixture stream.");
        }
        CombatResult Contact(string root,int damage,params string[] defenders)
        {
            // Explicit qualified-contact fixture. Actual domain computes each d20, defense, pending group and reaction options.
            world.State.random.backgroundCombat=FaceStream(defenders.Select(_=>12).ToArray());
            Enemy.attackBonus=defenders.Select(id=>world.State.Actor(id).armorClass).Max()+1-12;
            var result=world.ExecuteWorldCommand(new CombatCommand{id="contact/"+root,kind=CommandKind.IncomingBatch,actorId=Enemy.id,rootId=root,qualified=true,authority=CommandAuthority.System,
                incomingContacts=defenders.Select(id=>new IncomingContact{targetId=id,damage=damage,parryable=true,
                    reactionEligibleActorIds=world.State.actors.Where(a=>a.Alive&&world.HasLineOfEffect(BattleWorld.Position(a),BattleWorld.Position(world.State.Actor(id)))).Select(a=>a.id).ToList()}).ToList()});
            Assert.That(result.success,Is.True,result.reason);return result;
        }
        IEnumerator ChooseReaction(string actor,string defender,string reaction)
        {
            var choices=world.Session.GetReactionChoices().Where(q=>q.legal).ToList();int index=choices.FindIndex(q=>q.actorId==actor&&q.defenderId==defender&&q.reactionId==reaction);
            Assert.That(index,Is.InRange(0,5),"Fixture reaction must be available on first actual choices page.");
            yield return Click("reaction-choice-"+index); // P46: explicit cost choice executes once; shield may already be resolved.
        }

        [UnityTest] public IEnumerator ImportantAoeOpensOneAutomaticGroupBeforeAnyHpLossAndDeclinesOnce()
        {
            var hp=world.State.actors.ToDictionary(a=>a.id,a=>a.hp);int charges=world.State.charges;
            var result=Contact("p4-aoe",8,"fighter","rogue");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.ReactionPrompt));Assert.That(world.State.activePendingEvent.autoOpened);
            Assert.That(world.State.pendingThreatEvents.Count(g=>g.rootId=="p4-aoe"),Is.EqualTo(1));
            Assert.That(world.State.activePendingEvent.incomingIds.Count,Is.EqualTo(2));
            Assert.That(world.State.incomingEvents.Where(e=>e.rootId=="p4-aoe").All(e=>!e.damageApplied));
            foreach(var a in world.State.actors)Assert.That(a.hp,Is.EqualTo(hp[a.id]));
            yield return Click("reaction-decline");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(Fighter.hp,Is.EqualTo(hp["fighter"]-8));Assert.That(Rogue.hp,Is.EqualTo(hp["rogue"]-8));
            Assert.That(world.State.pendingThreatEvents.Single(g=>g.id==result.reactionEventId).stage,Is.EqualTo("Resolved"));Assert.That(world.State.charges,Is.EqualTo(charges));
        }
        [UnityTest] public IEnumerator FighterProtectsAllyWithActualDiceFreezeAndReturnsToOriginalFocus()
        {
            yield return Press(Key.Tab);Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus));
            int hp=Rogue.hp,charges=world.State.charges,superiority=Fighter.superiority;Contact("p4-protect",8,"rogue");
            Assert.That(Rogue.hp,Is.EqualTo(hp));world.Hud.RefreshView();
            Assert.That(world.Hud.ReactionCompactVisible);Assert.That(world.Hud.ReactionCompactText,Does.Contain("AC"));
            Assert.That(world.Hud.ReactionCompactText,Does.Contain("8伤害"));
            Assert.That(world.Hud.Canvas.transform.Find("Tactical commands").gameObject.activeInHierarchy,Is.False);
            Assert.That(world.Hud.Canvas.transform.Find("Post roll decision").gameObject.activeInHierarchy,Is.False);
            yield return ChooseReaction("fighter","rogue","parry");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(Time.timeScale,Is.Zero);Assert.That(Fighter.reactionRemaining,Is.EqualTo(world.Session.Config.reactionRecovery));
            Assert.That(Fighter.superiority,Is.EqualTo(superiority));Assert.That(world.State.charges,Is.EqualTo(charges));
            string ticket=world.State.pendingRoll.id;bool success=world.State.pendingRoll.hit;float budget=world.State.activePendingEvent.remainingUiSeconds;
            string actors=CombatJson.Write(world.State.actors),enemies=CombatJson.Write(world.State.enemies);float simulation=world.State.simulationSeconds;
            Vector3 camera=world.BattleCamera.transform.position;Quaternion rotation=world.BattleCamera.transform.rotation;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));yield return null;yield return null;
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(CombatJson.Write(world.State.actors),Is.EqualTo(actors));Assert.That(CombatJson.Write(world.State.enemies),Is.EqualTo(enemies));
            Assert.That(world.State.simulationSeconds,Is.EqualTo(simulation));Assert.That(world.State.activePendingEvent.remainingUiSeconds,Is.EqualTo(budget));
            Assert.That(world.BattleCamera.transform.position,Is.EqualTo(camera));Assert.That(world.BattleCamera.transform.rotation,Is.EqualTo(rotation));InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            yield return Until(()=>world.State.mode==CombatMode.PostRollDecision,3,"Actual Update must reveal the normal-duration dice animation");
            Assert.That(Time.timeScale,Is.EqualTo(world.Session.Config.reactionFocusScale));Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));Assert.That(Rogue.hp,Is.EqualTo(hp));
            world.Hud.RefreshView();world.Hud.DicePresentation.RefreshPresentation();
            Assert.That(world.Hud.ReactionCompactVisible);Assert.That(world.Hud.ReactionCompactText,Does.Contain("原始"));
            Assert.That(world.Hud.DicePresentation.IsVisible,Is.False,"Reaction result stays in its compact decision panel");
            Assert.That(world.Hud.Canvas.transform.Find("Post roll decision").gameObject.activeInHierarchy,Is.False);
            yield return Click("reaction-commit");Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(Fighter.superiority,Is.EqualTo(superiority-1));
            Assert.That(Rogue.hp,Is.EqualTo(hp-(success?4:8)));Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(world.State.rolls.Single(r=>r.id==ticket).stage,Is.EqualTo("Resolved"));
        }
        [UnityTest] public IEnumerator MageShieldUsesOriginalIncomingFaceWithoutDiceAnimationOrChargeCost()
        {
            int hp=Mage.hp,slots=Mage.spellSlots,charges=world.State.charges;Contact("p4-shield",6,"mage");
            var incoming=world.State.incomingEvents.Single(e=>e.rootId=="p4-shield");int total=incoming.total;ulong draws=world.State.random.backgroundCombat.drawCount,visible=world.State.random.visibleTactical.drawCount;
            yield return ChooseReaction("mage","mage","shield");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(world.State.rolls,Is.Empty);
            incoming=world.State.incomingEvents.Single(e=>e.rootId=="p4-shield");Assert.That(incoming.damageApplied);Assert.That(incoming.finalDamage,Is.Zero);Assert.That(incoming.rawFaces,Is.EqualTo(new[]{12}));Assert.That(incoming.total,Is.EqualTo(total));
            Assert.That(Mage.hp,Is.EqualTo(hp));Assert.That(Mage.spellSlots,Is.EqualTo(slots-1));Assert.That(Mage.reactionRemaining,Is.GreaterThan(0));Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible));
        }
        [UnityTest] public IEnumerator ReactionDecisionDeadlineExcludesActualPauseAndExplicitUnavailableUiSteps()
        {
            int hp=Fighter.hp;Contact("p4-ui-clock",8,"fighter");yield return Press(Key.Escape);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.CombatPaused));float remaining=world.State.activePendingEvent.remainingUiSeconds;
            // Disable only the world's automatic clock so the contract can be stepped exactly; HUD/camera remain actual components.
            world.enabled=false;world.Session.Tick(20,true);Assert.That(world.State.activePendingEvent.remainingUiSeconds,Is.EqualTo(remaining));Assert.That(Fighter.hp,Is.EqualTo(hp));
            Assert.That(world.Command(CommandKind.Resume).success);world.Session.Tick(20,false);Assert.That(world.State.activePendingEvent.remainingUiSeconds,Is.EqualTo(remaining));Assert.That(Fighter.hp,Is.EqualTo(hp));
            Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));world.Session.Tick(remaining-.01f,true);
            Assert.That(Fighter.hp,Is.EqualTo(hp));Assert.That(world.State.activePendingEvent.remainingUiSeconds,Is.EqualTo(.01f).Within(.0001f));
            world.Session.Tick(.02f,true);Assert.That(Fighter.hp,Is.EqualTo(hp-8));Assert.That(world.State.activePendingEvent,Is.Null);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));
            world.enabled=true;yield return null;
        }
        [UnityTest] public IEnumerator ZeroChargesStillAllowActualGroundMoveHoldReplacementAndMagePreparation()
        {
            world.State.charges=0;world.State.momentum=0;Fighter.majorRemaining=0;
            Assert.That(world.CancelSelectedOrder().success);yield return Press(Key.Tab);string costs=Costs();ulong visible=world.State.random.visibleTactical.drawCount;
            Vector3 start=BattleWorld.Position(Fighter),goal=start+world.Rig.ScreenMove(Vector2.right)*1.2f;
            yield return Click("order-move");Assert.That(world.MoveOrderArmed);yield return ClickGround(goal);
            Assert.That(Fighter.basicOrder.kind,Is.EqualTo(BasicOrderKind.Move));yield return Until(()=>BattleGeometry.DistanceXZ(start,BattleWorld.Position(Fighter))>.15f,3,"Free Focus movement must run on the real world clock");
            yield return Click("order-hold");Assert.That(world.PendingOrderLabel,Is.Not.Null);yield return Click("order-replace");
            Assert.That(Fighter.basicOrder.kind,Is.EqualTo(BasicOrderKind.Hold));Assert.That(Fighter.basicOrder.stage,Is.EqualTo("Holding"));
            yield return Press(Key.Digit3);yield return Click("order-prepare");yield return Click("order-replace");
            Assert.That(Mage.basicOrder.kind,Is.EqualTo(BasicOrderKind.Prepare));Assert.That(Mage.basicOrder.stage,Is.EqualTo("Preparing"));
            Assert.That(world.State.charges,Is.Zero);Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(Costs(),Is.EqualTo(costs));
            Assert.That(world.State.activeActorId,Is.EqualTo("fighter"));Assert.That(world.State.commandActorId,Is.EqualTo("mage"));
        }
        [UnityTest] public IEnumerator RogueCannotRollUntilActualRearMovementArrivesThenUsesOrdinaryConfirm()
        {
            Place(Enemy,new Vector3(2,0,0));Enemy.lockedX=4;Enemy.lockedZ=0;Place(Rogue,new Vector3(3.7f,0,0));
            yield return Press(Key.Tab);yield return Press(Key.Digit2);world.SelectedTarget=Enemy.id;
            // Release the fixture cooldown only after Focus selects Rogue; Hold prevents walking,
            // but still permits conservative AI attacks before this command actor is selected.
            Rogue.majorRemaining=0;
            Assert.That(world.Session.Quote("rogue",Enemy.id,"sneak").reason,Is.EqualTo("sneak_position_required"));
            int charges=world.State.charges;ulong draws=world.State.random.visibleTactical.drawCount;
            yield return Press(Key.Enter);Assert.That(world.State.pendingRoll,Is.Null);Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(world.TryGetRearDestination(out var destination,out var reason),Is.True,reason);Vector3 before=BattleWorld.Position(Rogue);
            yield return Click("order-rear");yield return Click("order-replace");
            Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Move));
            Assert.That(BattleGeometry.DistanceXZ(before,BattleWorld.Position(Rogue)),Is.LessThan(.5f),"Issuing a route may move through Update but must not teleport to the rear.");
            yield return Until(()=>Rogue.basicOrder.kind==BasicOrderKind.Hold&&Rogue.basicOrder.stage=="Holding"&&
                BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue),destination)<=world.Session.Config.orderArrivalDistance+.06f,
                12,"Rogue must traverse the actual arena and hold at the requested rear waypoint",()=>CombatJson.Write(new
                {
                    mode=world.State.mode.ToString(),scale=Time.timeScale,world.Menu,world.DungeonModal,
                    focus=world.State.focusBudget.secondsRemaining,hold=world.ImpactFeedback!=null&&world.ImpactFeedback.Holding,
                    error=world.Session.LastTickError,position=new {Rogue.x,Rogue.z},destination=new {destination.x,destination.z},
                    distance=BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue),destination),
                    world.Presentation.moveSpeed,world.Session.Config.orderArrivalDistance,
                    Rogue.heldBy,Rogue.windupRemaining,Rogue.staggerRemaining,Rogue.basicOrder
                }));
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue),destination),Is.LessThanOrEqualTo(world.Session.Config.orderArrivalDistance+.06f));
            Assert.That(world.Session.CanUseRearPosition("rogue",Enemy.id));Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(world.State.charges,Is.EqualTo(charges));
            yield return Press(Key.Enter);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze),world.Message);Assert.That(world.State.pendingRoll.actorId,Is.EqualTo("rogue"));Assert.That(world.State.charges,Is.EqualTo(charges-1));
        }
        [UnityTest] public IEnumerator BasicAttackStartsWithoutDamageThenRealUpdateMakesOneContactAndVisibleEffect()
        {
            Place(Enemy,new Vector3(1.7f,0,0));Fighter.majorRemaining=0;Fighter.attackBonus=20;
            world.State.random.backgroundCombat=FaceStream(12);int hp=Enemy.hp;ulong draws=world.State.random.backgroundCombat.drawCount;
            var result=world.BeginBasicAttack("fighter",Enemy.id);Assert.That(result.success,Is.True,result.reason);
            string intent=Fighter.basicAttackIntent.id;Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            Assert.That(Fighter.basicAttackIntent.remaining,Is.GreaterThan(0));Assert.That(world.ArtEffects.transform.Find("P3 cue attack"),Is.Not.Null);
            yield return Until(()=>Fighter.basicAttackIntent.resolved,2,"Actual Update must execute the contact phase once");
            Assert.That(Enemy.hp,Is.EqualTo(hp-world.Session.Config.BasicFor(Fighter).damage));Assert.That(world.State.random.backgroundCombat.drawCount,Is.GreaterThan(draws));
            Assert.That(world.State.incomingEvents.Count(e=>e.rootId==intent),Is.EqualTo(1));
            Assert.That(world.Feedback.GetComponentsInChildren<LineRenderer>().Length,Is.GreaterThan(0),"A successful contact must create a real world effect.");
            int after=Enemy.hp;yield return null;yield return null;Assert.That(Enemy.hp,Is.EqualTo(after));Assert.That(world.State.incomingEvents.Count(e=>e.rootId==intent),Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator BasicContactRechecksActualRangeAndMissDoesNotRollOrDamageMovedTarget()
        {
            Place(Enemy,new Vector3(1.7f,0,0));Fighter.majorRemaining=0;int hp=Enemy.hp;ulong draws=world.State.random.backgroundCombat.drawCount;
            var result=world.BeginBasicAttack("fighter",Enemy.id);Assert.That(result.success,Is.True,result.reason);
            Place(Enemy,new Vector3(6,0,0)); // Explicit target movement during the already-started swing.
            yield return Until(()=>Fighter.basicAttackIntent.resolved,2,"Actual contact must complete as a miss");
            Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            Assert.That(world.Feedback.transform.Find("Swing missed"),Is.Not.Null);Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator P46EmptyBudgetRetainsRealtimeSquadOrdersThroughActualUi()
        {
            world.State.focusBudget.secondsRemaining=0;
            int charges=world.State.charges;string active=world.State.activeActorId;
            yield return Click("free-orders");
            yield return Click("active-rogue");
            Assert.That(world.SelectedActor,Is.EqualTo("rogue"));Assert.That(world.State.activeActorId,Is.EqualTo(active));
            yield return Click("order-follow");yield return Click("order-replace");
            Assert.That(world.State.Actor("rogue").basicOrder.kind,Is.EqualTo(BasicOrderKind.Follow));
            Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(world.State.focusBudget.secondsRemaining,Is.Zero);
            Assert.That(Time.timeScale,Is.EqualTo(1));Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));
        }
        [UnityTest] public IEnumerator P46FocusDeadlineReturnsRealtimeWithoutTouchingActionDice()
        {
            int charges=world.State.charges;
            world.State.focusBudget.secondsRemaining=.7f;
            yield return Press(Key.Tab);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus));
            yield return Until(()=>world.State.mode==CombatMode.RealtimeCombat,2,"Budget deadline must leave Focus");
            Assert.That(world.State.focusBudget.secondsRemaining,Is.Zero);Assert.That(Time.timeScale,Is.EqualTo(1));
            Assert.That(world.State.charges,Is.EqualTo(charges));yield return Press(Key.Tab);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));
        }
        [UnityTest] public IEnumerator P46ReactionSettingsChangedWhilePausedApplyOnResume()
        {
            var before=world.State.reactionSettings.autoMode;
            yield return Press(Key.Escape);Assert.That(world.State.mode,Is.EqualTo(CombatMode.CombatPaused));
            yield return Click("reaction-mode");yield return Click("reaction-assistance");
            Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(before));
            yield return Press(Key.Escape);
            Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(world.ReactionPreferences.autoMode));
            Assert.That(world.State.reactionSettings.timeoutAcceptPaidRoll,Is.True);
            Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));
        }
    }
}
