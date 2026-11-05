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
using UnityEngine.UI;

namespace CombatV051.Tests
{
    public sealed class P3PresentationTests
    {
        BattleWorld world; Keyboard keyboard; string folder;
        InputSettings.BackgroundBehavior previousBackground;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode previousEditor;
#endif
        [UnitySetUp]public IEnumerator SetUp()
        {
            previousBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-P3-Presentation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            keyboard=InputSystem.AddDevice<Keyboard>();world=new GameObject("P3 actual presentation scene").AddComponent<BattleWorld>();
            world.InitializeDungeonForTests(Path.Combine(folder,"profile.json"));yield return null;
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=previousBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=previousEditor;
#endif
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P3-Presentation"))+Path.DirectorySeparatorChar;
            if(folder!=null&&Path.GetFullPath(folder).StartsWith(root,StringComparison.OrdinalIgnoreCase))Directory.Delete(folder,true);
        }
        IEnumerator Key(Key key,float seconds=.06f)
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));float until=Time.realtimeSinceStartup+seconds;
            while(Time.realtimeSinceStartup<until)yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        }
        string GameplayWithoutActivityClock()
        {
            // P4 counts unscaled activity during author dialogs; presentation still cannot alter gameplay/RNG.
            var copy=CombatJson.Clone(world.Profile.State);
            foreach(var mode in new[]{copy.story,copy.challenge}) if(mode!=null)
            {mode.elapsedRealSeconds=0;mode.explicitPauseSeconds=0;mode.focusLostSeconds=0;}
            return CombatJson.Write(copy);
        }
        [UnityTest]public IEnumerator DefaultDebugIsHiddenAndActualF1TogglesOnlyPresentation()
        {
            Assert.That(world.Hud.DebugOverlayVisible,Is.False);
            string before=GameplayWithoutActivityClock();
            float elapsed=world.Profile.ActiveMode.elapsedRealSeconds;
            yield return Key(UnityEngine.InputSystem.Key.F1);
            Assert.That(world.Hud.DebugOverlayVisible,Is.True);
            Assert.That(GameplayWithoutActivityClock(),Is.EqualTo(before),"F1 must not change gameplay or RNG while the world is paused.");
            Assert.That(world.Profile.ActiveMode.elapsedRealSeconds,Is.GreaterThanOrEqualTo(elapsed),"P4 activity time remains monotonic during dialogs.");
            yield return Key(UnityEngine.InputSystem.Key.F1);Assert.That(world.Hud.DebugOverlayVisible,Is.False);
        }
        [UnityTest]public IEnumerator IntroThenSingleMovementLessonObservesRealMotionAndEarlySwitch()
        {
            world.ChooseDungeonOption("close");yield return null;yield return null;
            world.Hud.RefreshView();Assert.That(world.Hud.VisibleTutorialText,Does.Contain("WASD"));
            yield return Key(UnityEngine.InputSystem.Key.Digit2);
            Assert.That(world.Tutorial.State.facts.Any(f=>f.key=="active.switched"),Is.True);
            var start=BattleWorld.Position(world.ActiveActor);
            yield return Key(UnityEngine.InputSystem.Key.W,.7f);
            Assert.That(BattleGeometry.DistanceXZ(start,BattleWorld.Position(world.ActiveActor)),Is.GreaterThan(2));
            Assert.That(world.Tutorial.State.facts.Any(f=>f.key=="move.distance"&&f.value>=2),Is.True);
            Assert.That(world.Hud.VisibleTutorialText,Does.Not.Contain("WASD"));
        }
        [UnityTest]public IEnumerator SkipResetAndReloadKeepGameplaySnapshotAndPresentationSeparate()
        {
            string before=CombatJson.Write(world.Profile.State);world.SkipPresentationTutorial();
            Assert.That(world.Tutorial.State.status,Is.EqualTo("Skipped"));
            Assert.That(CombatJson.Write(world.Profile.State),Is.EqualTo(before));
            world.ResetPresentationTutorial();Assert.That(world.Tutorial.State.status,Is.EqualTo("Active"));
            Assert.That(CombatJson.Write(world.Profile.State),Is.EqualTo(before));
            string instance=world.Profile.State.profileInstanceId;world.SkipPresentationTutorial();world.Suspend();world.ContinueSaved();yield return null;
            Assert.That(world.Profile.State.profileInstanceId,Is.EqualTo(instance));
            Assert.That(world.Tutorial.State.status,Is.EqualTo("Skipped"));
        }
        [UnityTest]public IEnumerator RealtimeHudHidesDebugDetailsAndNoInternalRoomIdsInObjective()
        {
            world.ChooseDungeonOption("close");yield return null;world.Hud.RefreshView();
            Assert.That(world.Hud.VisibleObjectiveText,Does.Not.Match(@"R[0-7]|v0\.5|timescale"));
            var visible=world.Hud.Canvas.GetComponentsInChildren<Text>().Where(t=>t.enabled&&t.gameObject.activeInHierarchy).Select(t=>t.text).ToArray();
            Assert.That(visible.Any(t=>t.Contains("Major 可用")||t.Contains("Bonus 可用")||t.Contains("世界时间 ×")||t.Contains("v0.5.1")),Is.False);
            Assert.That(world.Hud.Canvas.GetComponentsInChildren<ScrollRect>(true).Length,Is.GreaterThan(0),"Author text must support scrolling rather than truncate.");
        }
        [UnityTest]public IEnumerator TutorialAcceptedDiceAndMovementCannotMutateCombatRandomStreams()
        {
            string profile=GameplayWithoutActivityClock();
            world.Tutorial.Observe("guard.started",sourceId:"isolated-observation");world.Tutorial.Observe("roll.confirmed",sourceId:"isolated-observation-2");
            world.Hud.RefreshView();yield return null;
            Assert.That(GameplayWithoutActivityClock(),Is.EqualTo(profile));
            Assert.That(world.Tutorial.State.facts.Count,Is.GreaterThan(0));
        }
    }
}
