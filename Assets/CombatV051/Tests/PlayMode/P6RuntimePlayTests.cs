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
    // Engine/input contracts with explicit placements, disabled enemy AI and a
    // seeded PCG stream for payment cases. Not natural play/balance evidence.
    public sealed class P6RuntimePlayTests
    {
        const string Paladin="char_demo_paladin";
        BattleWorld world;Keyboard keyboard;Mouse mouse;string directory,path;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        ActorState P=>world.State.Actor(Paladin);
        ActorState Mage=>world.State.Actor("mage");
        static void Place(ActorState a,float x,float z){a.x=x;a.z=z;a.heightY=0;}
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;updateMode=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P6-Runtime",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            Assert.That(Resources.Load<TextAsset>("P6CombatConfig"),Is.Not.Null,"Actual P6 resource must be imported.");
            world=new GameObject("P6 real Resources and input fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(path);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)});yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P6-Runtime"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        IEnumerator Press(Key key)
        {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;}
        IEnumerator Until(Func<bool> condition,float seconds,string description)
        {var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&clock.Elapsed.TotalSeconds<seconds)yield return null;Assert.That(condition(),Is.True,description+"; "+world.Message);}
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);Assert.That(button.interactable,Is.True,id+"; "+world.Message);
            var rect=(RectTransform)button.transform;Vector2 point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Assert.That(point.x,Is.InRange(0f,(float)Screen.width));Assert.That(point.y,Is.InRange(0f,(float)Screen.height));
            Assert.That(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),Is.True,id+" raycast: "+string.Join(",",hits.Select(h=>h.gameObject.name)));
            int clicks=0;button.onClick.AddListener(()=>clicks++);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
            Assert.That(clicks,Is.EqualTo(1),id+" must dispatch one real pointer event, never Invoke.");
            var card=button.GetComponent<P47FrontendCard>();if(card!=null)yield return Until(()=>!card.Confirming,2,id+" confirmation animation");
        }
        ProfileState Disk()=>CombatJson.Read<ProfileState>(CombatJson.Read<ProfileEnvelope>(File.ReadAllText(path)).payload);
        static RngState Face18()
        {
            for(ulong seed=1;seed<10000;seed++){var stream=RandomStreams.Create(seed).visibleTactical;if(RandomStreams.D20(CombatJson.Clone(stream))==18)return stream;}
            throw new InvalidOperationException("PCG fixture face not found");
        }
        IEnumerator CombatFixture()
        {
            // Only the dedicated entry test clicks every menu. Other cases reuse the
            // production frontend commands to reach an isolated authored-map encounter.
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");
            // Historical P6 eight-room compatibility fixture; V12 tests cover the new default map.
            world.Profile.EnableV13Departures(false); // Explicit original three-body snapshot adapter.
            var historicalRules=V12Defaults.Create();historicalRules.build=null;historicalRules.beam=null;world.Profile.SetNextV12Rules(historicalRules);
            world.Profile.SetNextV12Dungeon(null);world.ChooseFrontend("depart");
            Assert.That(world.Training,Is.True,world.Message);world.FinishP6Training();Assert.That(world.Profile,Is.Not.Null,world.Message);
            if(world.DungeonModal)world.ChooseDungeonOption("close");
            foreach(var actor in world.State.actors)Place(actor,26,-2);
            var enter=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true});Assert.That(enter.success,Is.True,enter.reason);
            var activate=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true});Assert.That(activate.success,Is.True,activate.reason);
            Place(P,26,-2);Place(Mage,25,-3);Place(world.State.Actor("rogue"),25,-4);
            for(int i=0;i<world.State.enemies.Count;i++){var e=world.State.enemies[i];Place(e,27+i*4,-2);e.aiState=2;e.aiRemaining=100;e.lockedX=e.x+1;e.lockedZ=e.z;}
            foreach(string id in new[]{"mage","rogue"})
            {var held=world.ExecuteWorldCommand(new CombatCommand{id="fixture-hold/"+id,kind=CommandKind.IssueBasicOrder,actorId=id,actionId="Hold"});Assert.That(held.success,Is.True,held.reason);}
            world.SelectedActor=Paladin;world.SelectedAction="ability_smite_recharge";world.SelectedTarget=world.State.enemies[0].id;world.QuickDice=false;
            Physics.SyncTransforms();yield return null;yield return null;
            Assert.That(world.P6Enabled,Is.True);Assert.That(world.State.actors.Select(a=>a.id),Is.EqualTo(new[]{Paladin,"mage","rogue"}));
            Assert.That(world.State.inCombat,Is.True);Assert.That(world.DungeonMap,Is.Not.Null);Assert.That(world.State.charges,Is.EqualTo(2));
        }
        string FrozenWorld()=>CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.queue,world.State.focusBudget,world.State.p6,world.State.simulationSeconds});

        [UnityTest] public IEnumerator RealFrontendPreparationAndIsolatedTrainingSkipPreserveProfileThenDepartOnOriginalMap()
        {
            Assert.That(world.FrontChoices.Select(c=>c.id),Is.EquivalentTo(new[]{"campaign","explore","characters"}));
            yield return Click("front-explore");yield return Click("front-map-ashen");Assert.That(world.FrontPage,Is.EqualTo("prepare"),world.Message);
            var profile=world.Profile;string meta=CombatJson.Write(profile.State.meta),story=CombatJson.Write(profile.State.story);Assert.That(profile.State.challenge,Is.Null);
            // Keep the original R0/R1 map contract while exercising the real P6 frontend and combat.
            var historicalRules=V12Defaults.Create();historicalRules.build=null;historicalRules.beam=null;profile.SetNextV12Rules(historicalRules);
            profile.SetNextV12Dungeon(null);
            yield return Click("front-depart");Assert.That(world.Training,Is.True,world.Message);Assert.That(world.Profile,Is.Null);Assert.That(world.State.p6.training,Is.True);Assert.That(world.State.focusBudget.p6.unlimitedTraining,Is.True);
            string disk=File.ReadAllText(path);world.RefillP6Practice();yield return null;Assert.That(File.ReadAllText(path),Is.EqualTo(disk),"Practice resource changes cannot write the adventure.");Assert.That(CombatJson.Write(Disk().meta),Is.EqualTo(meta));
            yield return Click("p6-finish");Assert.That(world.Training,Is.False);Assert.That(world.Profile,Is.Not.Null,world.Message);Assert.That(world.Menu,Is.False);
            Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Challenge));Assert.That(world.Profile.ActiveMode.p6Departure.recoveryPolicy,Is.EqualTo("ExactState"));Assert.That(world.Profile.ActiveRoom.templateId,Is.EqualTo("R0"));Assert.That(world.DungeonMap,Is.Not.Null);
            // StartTraining saves a current presentation snapshot; compare gameplay fields,
            // not outer receipt/revision or camera observer values.
            var original=CombatJson.Read<ModeState>(story);Assert.That(world.Profile.State.story.id,Is.EqualTo(original.id));Assert.That(CombatJson.Write(world.Profile.State.story.explorationCombat.actors),Is.EqualTo(CombatJson.Write(original.explorationCombat.actors)));
            Assert.That(CombatJson.Write(world.Profile.State.meta),Is.EqualTo(meta));Assert.That(world.State.activeActorId,Is.EqualTo(Paladin));Assert.That(world.State.focusBudget.p6.unlimitedTraining,Is.False);
            var forbidden=world.ExecuteWorldCommand(new CombatCommand{id="formal-refill-forbidden",kind=CommandKind.RefillPracticeResources,actorId=Paladin});Assert.That(forbidden.success,Is.False);Assert.That(forbidden.reason,Is.EqualTo("practice_only"));
        }
        [UnityTest] public IEnumerator TutorialPreferenceWriteFailureCannotLoopSkipBackIntoTraining()
        {
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");
            var historicalRules=V12Defaults.Create();historicalRules.build=null;historicalRules.beam=null;world.Profile.SetNextV12Rules(historicalRules);
            world.Profile.SetNextV12Dungeon(null);world.ChooseFrontend("depart");
            Assert.That(world.Training,Is.True,world.Message);Assert.That(world.Profile,Is.Null);
            // A file occupies the optional preference-directory path. The isolated
            // profile remains writable; no ordinary save or permission is changed.
            string blocked=Path.Combine(directory,"presentation");Assert.That(Directory.Exists(blocked),Is.False);File.WriteAllText(blocked,"intentional preference directory conflict");
            yield return Click("p6-finish");
            Assert.That(world.Training,Is.False,"Preference persistence must not restart the skipped tutorial.");Assert.That(world.Profile,Is.Not.Null,world.Message);Assert.That(world.Menu,Is.False);
            Assert.That(world.Profile.ActiveUsesP6,Is.True);Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Challenge));Assert.That(world.Profile.ActiveRoom.templateId,Is.EqualTo("R0"));
            Assert.That(Disk().challenge,Is.Not.Null);Assert.That(Disk().challenge.p6Departure,Is.Not.Null);Assert.That(File.ReadAllText(blocked),Is.EqualTo("intentional preference directory conflict"));
            Assert.That(world.Message,Does.Contain("教程偏好未保存"),"Departure should retain the preference failure message.");
        }
        [UnityTest] public IEnumerator ActualQAndConfirmFreezeForTwoSecondsThenBaseButtonCommitsOneChargeExactly()
        {
            yield return CombatFixture();world.State.random.visibleTactical=Face18();yield return Press(Key.Q);Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus),world.Message);Assert.That(world.SelectedActor,Is.EqualTo(Paladin));
            yield return Click("confirm");Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze),world.Message);Assert.That(world.CurrentRollDuration,Is.EqualTo(2).Within(.0001));Assert.That(world.State.charges,Is.EqualTo(1));string ticket=world.State.pendingRoll.id;
            string frozen=FrozenWorld();Vector3 camera=world.BattleCamera.transform.position;Quaternion rotation=world.BattleCamera.transform.rotation;int frames=0;var clock=System.Diagnostics.Stopwatch.StartNew();
            while(world.State.mode==CombatMode.RollFreeze&&clock.Elapsed.TotalSeconds<6)
            {Assert.That(FrozenWorld(),Is.EqualTo(frozen));Assert.That(world.BattleCamera.transform.position,Is.EqualTo(camera));Assert.That(world.BattleCamera.transform.rotation,Is.EqualTo(rotation));frames++;yield return null;}
            Assert.That(frames,Is.GreaterThan(1));Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision),world.Message);Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));Assert.That(world.State.pendingRoll.selectedFace,Is.EqualTo(18));
            yield return Click("commit");yield return Until(()=>world.State.p6.damageResults.Any(d=>d.rootId==ticket),4,"Prepaid base smite actual windup/contact");
            Assert.That(world.State.rolls.Single(t=>t.id==ticket).p6.overchargePaid,Is.False);Assert.That(world.State.charges,Is.EqualTo(1));Assert.That(world.State.mode,Is.Not.EqualTo(CombatMode.RollFreeze));
            var damage=world.State.p6.damageResults.Single(d=>d.rootId==ticket);Assert.That(damage.actionId,Is.EqualTo("ability_smite_recharge"));Assert.That(damage.radiantFaces.Count,Is.EqualTo(3));Assert.That(Disk().ActiveMode.encounters.Single(e=>e.id==world.Profile.ActiveMode.activeEncounterId).currentCombat.p6.damageResults.Any(d=>d.id==damage.id),Is.True);
        }
        [UnityTest] public IEnumerator ActualFullButtonPaysSecondChargeOnlyAtStartAndStoredResultCannotBecomeBase()
        {
            yield return CombatFixture();world.State.random.visibleTactical=Face18();yield return Press(Key.Q);yield return Click("confirm");
            yield return Until(()=>world.State.mode==CombatMode.PostRollDecision,6,"Natural standard roll presentation completes");string ticket=world.State.pendingRoll.id;Assert.That(world.State.pendingRoll.hit,Is.True);Assert.That(world.State.charges,Is.EqualTo(1));
            yield return Click("p6-full");yield return Until(()=>world.State.p6.damageResults.Any(d=>d.rootId==ticket),4,"Full smite contact");
            var stored=world.State.rolls.Single(t=>t.id==ticket);Assert.That(stored.p6.overchargeSelected&&stored.p6.overchargePaid,Is.True);Assert.That(world.State.charges,Is.Zero);Assert.That(world.State.p6.damageResults.Single(d=>d.rootId==ticket).radiantFaces.Count,Is.EqualTo(5));
            var current=Disk().ActiveMode.encounters.Single(e=>e.id==world.Profile.ActiveMode.activeEncounterId).currentCombat;
            Assert.That(current.rolls.Single(t=>t.id==ticket).p6.overchargePaid,Is.True);Assert.That(current.charges,Is.Zero);
        }
        [UnityTest] public IEnumerator SavedFullReservationRemainsClickableAfterCommitWriteFailureAndPaysSecondChargeOnce()
        {
            yield return CombatFixture();world.State.random.visibleTactical=Face18(); // Fixed successful roll, not natural RNG evidence.
            yield return Press(Key.Q);yield return Click("confirm");
            yield return Until(()=>world.State.mode==CombatMode.PostRollDecision,6,"Natural standard roll presentation completes");
            string ticket=world.State.pendingRoll.id,target=world.State.pendingRoll.targetId;
            var selected=world.ExecuteWorldCommand(new CombatCommand{id="reserve-full-before-failure",kind=CommandKind.SelectSmiteOvercharge,actorId=Paladin,targetId=target,ticketId=ticket,flag=true});
            Assert.That(selected.success,Is.True,selected.reason);Assert.That(world.State.pendingRoll.p6.overchargeHeld,Is.True);Assert.That(world.State.charges,Is.EqualTo(1));Assert.That(world.Session.AvailableSharedCharges,Is.Zero);
            var reserved=Disk().ActiveMode.encounters.Single(e=>e.id==world.Profile.ActiveMode.activeEncounterId).currentCombat;
            Assert.That(reserved.pendingRoll.id,Is.EqualTo(ticket));Assert.That(reserved.pendingRoll.p6.overchargeHeld,Is.True);Assert.That(reserved.charges,Is.EqualTo(1));
            string saved=File.ReadAllText(path);int faults=0;
            world.Profile.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeWrite){faults++;throw new IOException("P6 UI retry fixture Commit write failure");}};
            try
            {
                var failed=world.ExecuteWorldCommand(new CombatCommand{id="failed-full-commit",kind=CommandKind.Commit,actorId=Paladin,targetId=target,ticketId=ticket,actionId="ability_smite_recharge"});
                Assert.That(failed.success,Is.False);Assert.That(faults,Is.EqualTo(1));
            }
            finally{world.Profile.Store.WriteFault=null;}
            Assert.That(File.ReadAllText(path),Is.EqualTo(saved));Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));Assert.That(world.State.pendingRoll.p6.overchargeHeld,Is.True);Assert.That(world.Session.AvailableSharedCharges,Is.Zero);
            // Click refreshes the real HUD, asserts interactability and first raycast hit,
            // then queues pointer press/release. It never calls Button.onClick.Invoke.
            yield return Click("p6-full");yield return Until(()=>world.State.p6.damageResults.Any(d=>d.rootId==ticket),4,"Reserved full smite retry reaches actual contact");
            var result=world.State.rolls.Single(t=>t.id==ticket);Assert.That(result.p6.overchargePaid,Is.True);Assert.That(result.p6.overchargeHeld,Is.False);Assert.That(world.State.charges,Is.Zero);
            Assert.That(result.paidResourceIds.Count(id=>id=="shared-charge-overcharge/"+ticket),Is.EqualTo(1));Assert.That(world.State.p6.damageResults.Count(d=>d.rootId==ticket),Is.EqualTo(1));
            var stored=Disk().ActiveMode.encounters.Single(e=>e.id==world.Profile.ActiveMode.activeEncounterId).currentCombat;
            Assert.That(stored.charges,Is.Zero);Assert.That(stored.rolls.Single(t=>t.id==ticket).p6.overchargePaid,Is.True);Assert.That(stored.rolls.Single(t=>t.id==ticket).p6.overchargeHeld,Is.False);
            Assert.That(stored.rolls.Single(t=>t.id==ticket).paidResourceIds.Count(id=>id=="shared-charge-overcharge/"+ticket),Is.EqualTo(1));Assert.That(stored.p6.damageResults.Single(d=>d.rootId==ticket).radiantFaces.Count,Is.EqualTo(5));
        }
        [UnityTest] public IEnumerator ActualFUsesMageWithoutActiveSwitchAndSuccessfulBackgroundSaveStillAppliesRadiantMark()
        {
            yield return CombatFixture();var target=world.State.Actor(world.SelectedTarget);target.wisSaveBonus=100; // Explicit deterministic save-pass fixture, not natural RNG evidence.
            string active=world.State.activeActorId;int charge=world.State.charges,slots=Mage.spellSlots;ulong visible=world.State.random.visibleTactical.drawCount,backgroundDraws=world.State.random.backgroundCombat.drawCount;
            yield return Press(Key.F);Assert.That(Mage.p6.bindIntent,Is.Not.Null,world.Message);Assert.That(world.State.activeActorId,Is.EqualTo(active));Assert.That(world.State.mode,Is.Not.EqualTo(CombatMode.RollFreeze));
            yield return Until(()=>world.State.p6.bindResults.Count>0,3,"Real Update completes the mage cast");
            var cast=world.State.p6.bindResults.Last();Assert.That(cast.savePassed&&cast.markApplied,Is.True);Assert.That(world.State.Actor(target.id).statuses.Any(s=>s.p6!=null&&s.remaining>0),Is.True);
            Assert.That(world.State.activeActorId,Is.EqualTo(active));Assert.That(world.State.charges,Is.EqualTo(charge));Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(world.State.random.backgroundCombat.drawCount,Is.GreaterThan(backgroundDraws));
            Assert.That(world.State.mode,Is.Not.EqualTo(CombatMode.RollFreeze));Assert.That(Mage.p6.bindCooldown,Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator P6PresentationPostureHoldDoesNotStopOtherActorsOrWorldTimers()
        {
            yield return CombatFixture();var enemy=world.State.enemies[0];float start=world.State.simulationSeconds;float enemyTimer=enemy.aiRemaining;Vector3 player=BattleWorld.Position(P);
            world.HoldP6Postures(enemy.id,Paladin,.10f);Assert.That(world.ImpactFeedback.Holding,Is.False,"P6 presentation cannot use the legacy global hitstop gate.");
            Assert.That(world.GetComponentsInChildren<ArtActorView>().Single(a=>a.Archetype=="paladin").PostureHeld,Is.True,"The real actor art component must receive the local posture hold.");
            yield return Until(()=>world.State.simulationSeconds>start+.03f,1,"World simulation must continue during local posture hold");
            Assert.That(world.State.Actor(enemy.id).aiRemaining,Is.LessThan(enemyTimer));Assert.That(BattleWorld.Position(P),Is.EqualTo(player));Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));
        }
    }
}
