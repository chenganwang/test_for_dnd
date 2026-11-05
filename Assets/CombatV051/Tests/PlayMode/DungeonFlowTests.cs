using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    /// <summary>Actual scene/input/UI/profile integration. Position and clear fixtures skip combat grind explicitly.</summary>
    public sealed class DungeonFlowTests
    {
        BattleWorld world; Keyboard keyboard; string directory,file;
        InputSettings.BackgroundBehavior background; InputSettings.UpdateMode update;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editor;
#endif
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P2-Flow",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);file=Path.Combine(directory,"profile.json");
            background=InputSystem.settings.backgroundBehavior;update=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editor=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();world=new GameObject("Actual P2 flow world").AddComponent<BattleWorld>();world.InitializeDungeonForTests(file);
            yield return null;
            Assert.That(world.Profile,Is.Not.Null,world.Message);Assert.That(world.DungeonModal,Is.True);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P2-Flow"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
            Time.timeScale=1;
        }
        IEnumerator KeyPress(Key key)
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        }
        void PlaceParty(float x,float z)
        {int i=0;foreach(var actor in world.State.actors){actor.x=x+i++*.55f;actor.z=z;actor.majorRemaining=20;}Physics.SyncTransforms();}
        void Enter(string room)
        {
            var r=world.DungeonConfig.Room(room);PlaceParty(r.x,r.z);
            var result=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId=room,qualified=true});Assert.That(result.success,Is.True,result.reason);
        }
        void ClearFixture(string room)
        {
            Enter(room);var started=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId=room,qualified=true});Assert.That(started.success,Is.True,started.reason);
            foreach(var enemy in world.State.enemies)enemy.hp=0;
            world.ResolveDungeonEncounter();Assert.That(world.Profile.ActiveRoom.resolved,Is.True,world.Message);
            if(world.Profile.PendingReward!=null)world.ChooseDungeonOption("skip-reward");
        }
        [UnityTest] public IEnumerator ActualResourceIntroAndModalPreventInputLeakThenCameraRelativeMovementWorks()
        {
            Assert.That(world.DungeonConfig.rooms.Count,Is.EqualTo(8));Assert.That(world.State.actors.Count,Is.EqualTo(3));
            Assert.That(world.DungeonMenuText,Does.Contain(StoryText.Load().title),"The actual imported author title must be displayed, not merely parsed.");
            Assert.That(world.BattleCamera.orthographic,Is.True);Assert.That(world.Rig.Yaw,Is.EqualTo(45).Within(.0001f));Assert.That(world.Rig.Pitch,Is.EqualTo(55).Within(.0001f));
            var before=BattleWorld.Position(world.ActiveActor);yield return KeyPress(Key.W);yield return KeyPress(Key.E);yield return KeyPress(Key.Tab);
            Assert.That(BattleWorld.Position(world.ActiveActor),Is.EqualTo(before));Assert.That(Time.timeScale,Is.Zero);Assert.That(world.State.charges,Is.Zero);
            world.ChooseDungeonOption("close");Assert.That(world.DungeonModal,Is.False);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));float until=Time.realtimeSinceStartup+.15f;
            while(Time.realtimeSinceStartup<until)yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            var moved=BattleWorld.Position(world.ActiveActor)-before;
            Assert.That(moved.magnitude,Is.GreaterThan(.1f));Assert.That(Vector3.Dot(moved.normalized,world.Rig.ScreenMove(Vector2.up)),Is.GreaterThan(.98f));
            Assert.That(world.Profile.ActiveMode.elapsedRealSeconds,Is.GreaterThan(0));
        }
        [UnityTest] public IEnumerator ActualEOpensIndependentEventAndClosingReloadingNeverRefreshesPaidResult()
        {
            world.ChooseDungeonOption("close");ClearFixture("R1");Enter("R2");PlaceParty(52,2);
            yield return KeyPress(Key.E);Assert.That(world.DungeonDialogTitle,Does.Contain("选择队员"));
            Assert.That(world.DungeonChoices.Any(c=>c.id=="actor:mage"&&c.enabled),Is.True);
            string combatRandom=CombatJson.Write(world.State.random);world.ChooseDungeonOption("actor:mage");world.ChooseDungeonOption("event-roll");
            Assert.That(world.Profile.ActiveSafeEvent.stage,Is.EqualTo("AwaitDecision"),world.Message);
            string eventResult=CombatJson.Write(world.Profile.ActiveSafeEvent);var pos=BattleWorld.Position(world.ActiveActor);
            yield return KeyPress(Key.Tab);yield return KeyPress(Key.W);
            Assert.That(BattleWorld.Position(world.ActiveActor),Is.EqualTo(pos));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(combatRandom));Assert.That(world.State.charges,Is.Zero);
            world.ChooseDungeonOption("close");Assert.That(world.DungeonModal,Is.False);
            world.ContinueSaved();Assert.That(world.Profile.ActiveSafeEvent,Is.Null);
            yield return KeyPress(Key.E);world.ChooseDungeonOption("actor:mage");
            Assert.That(CombatJson.Write(world.Profile.ActiveSafeEvent),Is.EqualTo(eventResult));
            bool success=world.Profile.ActiveSafeEvent.success;world.ChooseDungeonOption("event-accept");
            Assert.That(world.Profile.ActiveMode.HasFlag("ROUTE_OPEN"),Is.EqualTo(success));Assert.That(world.State.charges,Is.Zero);
            Assert.That(world.Profile.ActiveMode.events.Single(e=>e.templateId=="EVENT_A").stage,Is.EqualTo("Committed"));
            yield return null;
        }
        [UnityTest] public IEnumerator ShortRestUiSuppliesActualLocationAndConsumesOnceWithoutGeneratingCharge()
        {
            world.ChooseDungeonOption("close");ClearFixture("R1");Enter("R2");ClearFixture("R3");Enter("R2");
            PlaceParty(48,8);var gate=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.OpenGate,gateId="P24_SMASH",actorId="fighter",qualified=true});Assert.That(gate.success,Is.True,gate.reason);
            ClearFixture("R4");ClearFixture("R5");Enter("R6");PlaceParty(0,22);
            foreach(var actor in world.State.actors)actor.hp=Math.Max(1,actor.maxHp-8);
            yield return KeyPress(Key.E);Assert.That(world.DungeonDialogTitle,Does.Contain("短休"));
            var old=world.State.actors.Select(a=>a.hp).ToArray();world.ChooseDungeonOption("rest:heal");
            Assert.That(world.Profile.ActiveMode.usedRestIds,Does.Contain("REST_R6"),world.Message);
            Assert.That(world.State.actors.Select((a,i)=>a.hp>old[i]).All(x=>x),Is.True);Assert.That(world.State.charges,Is.Zero);
            yield return KeyPress(Key.E);Assert.That(world.DungeonChoices.Where(c=>c.id.StartsWith("rest:")).All(c=>!c.enabled),Is.True);
            yield return null;
        }
        [UnityTest] public IEnumerator ZeroFocusTerminalStateShowsDefeatAndWholeRetryWithoutAnExtraTabPress()
        {
            world.ChooseDungeonOption("close");Enter("R1");
            var start=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId="R1",qualified=true});Assert.That(start.success,Is.True,start.reason);
            world.SetFocusScale(0);Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            // Terminal-damage fixture isolates the zero-delta Update boundary; reaction damage itself has separate tests.
            foreach(var actor in world.State.actors)actor.hp=0;
            yield return null;yield return null;
            Assert.That(Time.timeScale,Is.Zero);Assert.That(world.DungeonDialogTitle,Is.EqualTo("小队倒下"));
            Assert.That(world.DungeonChoices.Any(c=>c.id=="retry"&&c.enabled),Is.True);
            world.ChooseDungeonOption("retry");
            Assert.That(world.State.actors.All(a=>a.Alive),Is.True,world.Message);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(world.DungeonModal,Is.False);Assert.That(world.Profile.ActiveEncounter.attemptNumber,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ModeSwitchAndDiskContinueRestoreDiceSpeedAndGlobalFocusConsistently()
        {
            world.ChooseDungeonOption("close");ClearFixture("R1");Enter("R0");
            world.SetQuickDice(true);world.SetFocusScale(.1f);
            var start=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.StartChallenge,selectionId="BASE"});Assert.That(start.success,Is.True,start.reason);
            Assert.That(world.QuickDice,Is.False,"A fresh mode has its defined standard-animation default.");Assert.That(world.Session.Config.focusScale,Is.EqualTo(.1f));
            world.SetQuickDice(false);
            var back=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ContinueStory});Assert.That(back.success,Is.True,back.reason);
            Assert.That(world.QuickDice,Is.True,"Returning to Story must restore the same preference as a disk continuation.");
            world.ContinueSaved();Assert.That(world.QuickDice,Is.True);Assert.That(world.Session.Config.focusScale,Is.EqualTo(.1f));
            Assert.That(world.Profile.State.focusScale,Is.EqualTo(.1f));yield return null;
        }
    }
}
