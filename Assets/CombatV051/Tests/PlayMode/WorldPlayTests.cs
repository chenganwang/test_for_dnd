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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace CombatV051.Tests
{
    /// <summary>Actual P0 world/components and queued Input System events, with explicit editor focus policy.</summary>
    public sealed class WorldPlayTests
    {
        BattleWorld world;
        Keyboard keyboard;
        Mouse mouse;
        string directory, file;
        InputSettings.BackgroundBehavior background;
        InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif

        [UnitySetUp]
        public IEnumerator Setup()
        {
            background = InputSystem.settings.backgroundBehavior;
            updateMode = InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            directory = Path.Combine(Path.GetTempPath(), "ActionCrpgV051-WorldTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "profile.json");
            world = new GameObject("Actual P0 test world").AddComponent<BattleWorld>();
            world.InitializeForTests(file);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f) });
            yield return null; yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = background; InputSystem.settings.updateMode = updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (!string.IsNullOrEmpty(directory))
            {
                string expectedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ActionCrpgV051-WorldTests")) + Path.DirectorySeparatorChar;
                string resolved = Path.GetFullPath(directory);
                if (resolved.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
            Time.timeScale = 1;
        }

        IEnumerator KeyPress(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        IEnumerator WaitForActualBasicContact(ulong drawsBefore)
        {
            float deadline = Time.realtimeSinceStartup + 2;
            while (world.ActiveActor.basicAttackIntent == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.ActiveActor.basicAttackIntent, Is.Not.Null,
                "The actual mouse input must create the persisted attack intent: " + world.Message);
            string intentId = world.ActiveActor.basicAttackIntent.id;
            if (!world.ActiveActor.basicAttackIntent.resolved)
                Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(drawsBefore), "A windup must not roll before contact.");
            while (!world.ActiveActor.basicAttackIntent.resolved && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.ActiveActor.basicAttackIntent.resolved, Is.True,
                "Actual Update must finish the configured contact delay; " + world.Message + "; tick=" + world.Session.LastTickError);
            Assert.That(world.ActiveActor.basicAttackIntent.id, Is.EqualTo(intentId));
            Assert.That(world.State.receipts.Count(r => r.id == intentId + "/contact"), Is.EqualTo(1),
                "The world executor must produce exactly one contact transaction for the real click.");
        }

        void Place(Vector3 actor, Vector3 enemy)
        {
            world.ActiveActor.x = actor.x; world.ActiveActor.z = actor.z; world.ActiveActor.heightY = actor.y;
            var target = world.State.enemies[0]; target.x = enemy.x; target.z = enemy.z; target.heightY = enemy.y;
            target.aiState = 0; target.aiRemaining = 50;
            Physics.SyncTransforms();
        }

        IEnumerator ClickUi(string id)
        {
            Button button = world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b => b.gameObject.name == id);
            Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, "UI control must actually be available: " + id);
            Canvas.ForceUpdateCanvases();
            var rectangle = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rectangle.TransformPoint(rectangle.rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            string diagnostic = "id=" + id + "; screen=" + Screen.width + "x" + Screen.height + "; point=" + point +
                "; rays=" + string.Join(",", hits.Select(h => h.gameObject.name)) + "; module=" + EventSystem.current.currentInputModule;
            TestContext.WriteLine(diagnostic);
            Assert.That(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)), Is.True,
                "The real raycaster must reach the intended button before sending mouse input: " + diagnostic);
            int clicks = 0;
            button.onClick.AddListener(() => clicks++); // Observe actual UI dispatch; never invoke the callback in a test.
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            var module = EventSystem.current.currentInputModule as InputSystemUIInputModule;
            TestContext.WriteLine("pointOver=" + EventSystem.current.IsPointerOverGameObject(mouse.deviceId) +
                "; uiPointEnabled=" + (module != null && module.point.action.enabled) + "; uiClickEnabled=" + (module != null && module.leftClick.action.enabled));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            Assert.That(clicks, Is.EqualTo(1), "Actual UI click dispatch count: " + diagnostic + "; message=" + world.Message);
        }

        static string SimulationSnapshot(CombatState state)
        {
            var copy = state.Clone();
            copy.realCombatSeconds = 0; // Session duration is unscaled telemetry, not world simulation.
            return CombatJson.Write(copy);
        }

        static AreaPreview TargetMarker(BattleWorld target)
        {
            // The selection circle starts at radius 1 before first use, then uses .8.
            // Enemy warnings and status markers have different radii in this fixed P0 fixture.
            return target.GetComponentsInChildren<AreaPreview>(true).Single(p => p.Shape.Kind == AreaKind.Circle &&
                (Mathf.Abs(p.Shape.Radius - .8f) < .0001f || Mathf.Abs(p.Shape.Radius - 1f) < .0001f));
        }

        static string WorldVisualSnapshot(BattleWorld target)
        {
            var actorViews = target.transform.Find("Actor views");
            var poses = actorViews.GetComponentsInChildren<Transform>(true)
                .Select(t => new { t.name, t.gameObject.activeSelf, position = t.position.ToString("F6"), rotation = t.rotation.ToString("F6") }).ToArray();
            var outlines = target.GetComponentsInChildren<AreaPreview>(true).Select(p => new
            {
                p.gameObject.activeSelf,
                kind = p.Shape.Kind.ToString(), origin = p.Shape.Origin.ToString("F6"), direction = p.Shape.Direction.ToString("F6"),
                p.Shape.Radius, p.Shape.InnerRadius, p.Shape.Length, p.Shape.Width, p.Shape.HalfAngleDegrees,
                lines = p.GetComponentsInChildren<LineRenderer>(true).Select(line => new
                {
                    line.gameObject.activeSelf,
                    points = Enumerable.Range(0, line.positionCount).Select(i => line.GetPosition(i).ToString("F6")).ToArray()
                }).ToArray()
            }).ToArray();
            return CombatJson.Write(new { poses, outlines });
        }

        [UnityTest]
        public IEnumerator ActualWInputMovesScreenUpAndTabKeepsTheFixedCameraOrientation()
        {
            Place(Vector3.zero, new Vector3(6, 0, 0));
            yield return null;
            Vector3 before = BattleWorld.Position(world.ActiveActor);
            Quaternion orientation = world.BattleCamera.transform.rotation;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            float heldSince = Time.realtimeSinceStartup;
            int heldFrames = 0;
            while (Time.realtimeSinceStartup - heldSince < .08f && heldFrames++ < 4096) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            Assert.That(Time.realtimeSinceStartup - heldSince, Is.GreaterThanOrEqualTo(.08f), "Observe real movement time, not four unconstrained headless frames.");
            Vector3 displacement = BattleWorld.Position(world.ActiveActor) - before;
            Assert.That(displacement.magnitude, Is.GreaterThan(.01f));
            Assert.That(Vector3.Dot(displacement.normalized, world.Rig.ScreenMove(Vector2.up)), Is.GreaterThan(.999f));
            Assert.That(displacement.y, Is.Zero);
            yield return KeyPress(Key.Tab);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(Time.timeScale, Is.EqualTo(world.Session.Config.focusScale));
            Assert.That(Quaternion.Angle(orientation, world.BattleCamera.transform.rotation), Is.LessThan(.001f));
            Vector3 stopped = BattleWorld.Position(world.ActiveActor);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); yield return null; yield return null;
            Assert.That(BattleWorld.Position(world.ActiveActor), Is.EqualTo(stopped));
        }

        [UnityTest]
        public IEnumerator ActualMouseAttackUsesBackgroundD20WithoutChargeOrFreeze()
        {
            Place(Vector3.zero, new Vector3(1.7f, 0, 0));
            yield return null;
            Vector2 screen = world.BattleCamera.WorldToScreenPoint(BattleWorld.Position(world.State.enemies[0]));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null; yield return null;
            int charges = world.State.charges;
            ulong visible = world.State.random.visibleTactical.drawCount, backgroundDraws = world.State.random.backgroundCombat.drawCount;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 1 }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null;
            yield return WaitForActualBasicContact(backgroundDraws);
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.GreaterThan(backgroundDraws));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(world.ActiveActor.majorRemaining, Is.GreaterThan(0));
            Assert.That(Time.timeScale, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RealMouseBasicAttackOnlyRollsAndSpendsMajorWhenEnemyIsInsideAimCone()
        {
            Place(Vector3.zero, new Vector3(1.7f, 0, 0));
            world.Rig.Step(5); // Settle the real camera so both ground points stay clear of the left information panel.
            yield return null;
            int enemyHp = world.State.enemies[0].hp, charges = world.State.charges;
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            ulong tacticalDraws = world.State.random.visibleTactical.drawCount;
            Assert.That(world.ActiveActor.majorRemaining, Is.Zero);

            var groundPoints = new[] { new Vector3(-2, 0, 0), new Vector3(1.7f, 0, 0) };
            for (int i = 0; i < groundPoints.Length; i++)
            {
                Vector2 screen = world.BattleCamera.WorldToScreenPoint(groundPoints[i]);
                Assert.That(screen.x, Is.InRange(0f, (float)Screen.width));
                Assert.That(screen.y, Is.InRange(0f, (float)Screen.height));
                Canvas.ForceUpdateCanvases();
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, hits);
                Assert.That(hits, Is.Empty, "Ground click must reach the world, not a HUD graphic: " + screen + "; " +
                    string.Join(",", hits.Select(hit => hit.gameObject.name)));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null; yield return null;
                Assert.That(BattleGeometry.DistanceXZ(world.WorldAimPoint, groundPoints[i]), Is.LessThan(.01f),
                    "The real mouse ray must aim at the intended ground point before attacking.");
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen, buttons = 1 }); yield return null; yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }); yield return null;
                if (i == 0)
                {
                    Assert.That(world.ActiveActor.basicAttackIntent, Is.Null, "The opposite aim direction must not create a delayed attack either.");
                    Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws),
                        "An enemy behind the mouse-directed slash must not trigger a background d20.");
                    Assert.That(world.ActiveActor.majorRemaining, Is.Zero, "An empty slash direction must not spend Major.");
                    Assert.That(world.State.enemies[0].hp, Is.EqualTo(enemyHp));
                }
                else
                {
                    yield return WaitForActualBasicContact(backgroundDraws);
                    Assert.That(world.State.random.backgroundCombat.drawCount, Is.GreaterThan(backgroundDraws),
                        "A real mouse click toward the in-range enemy must execute the background attack check.");
                    Assert.That(world.ActiveActor.majorRemaining, Is.GreaterThan(0));
                }
                Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(tacticalDraws));
                Assert.That(world.State.charges, Is.EqualTo(charges));
                Assert.That(world.State.mode, Is.EqualTo(CombatMode.RealtimeCombat));
            }
        }

        [UnityTest]
        public IEnumerator RealUiPointerClicksFocusAndConfirmWithoutLeakingABasicAttack()
        {
            Place(Vector3.zero, new Vector3(1.7f, 0, 0));
            world.Presentation.rollAnimationSeconds = 10;
            yield return null;
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            int hp = world.State.enemies[0].hp, charges = world.State.charges;
            yield return ClickUi("focus");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(world.State.enemies[0].hp, Is.EqualTo(hp));
            Assert.That(world.ActiveActor.majorRemaining, Is.Zero);
            yield return ClickUi("confirm");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            Assert.That(world.State.charges, Is.EqualTo(charges - 1));
            Assert.That(world.State.rolls.Count, Is.EqualTo(1));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(world.State.enemies[0].hp, Is.EqualTo(hp));
        }

        [UnityTest]
        public IEnumerator FreezeStopsAllWorldDtosCameraAndExistingPreviewsWhileRollUiAdvances()
        {
            Place(Vector3.zero, new Vector3(3, 0, 0));
            yield return KeyPress(Key.Tab);
            world.ActiveActor.bonusRemaining = 3; world.ActiveActor.reactionRemaining = 4;
            world.ActiveActor.dodgeCooldown = 2;
            world.ActiveActor.statuses.Add(new StatusState { id = "freeze-regression", remaining = 3, tickRemaining = .4f });
            var enemy = world.State.enemies[0]; enemy.aiState = 1; enemy.aiRemaining = .9f;
            enemy.guardRemaining = 1; // The caster must remain legal at ConfirmRoll; freeze still covers a live guard timer.
            enemy.attackSequence = 1; enemy.attackGroupId = "fixture-telegraph"; enemy.lockedX = world.ActiveActor.x; enemy.lockedZ = world.ActiveActor.z;
            // Keep the real animation state open long enough for deterministic multi-frame assertions.
            // The normal-duration automatic transition is covered separately below.
            world.Presentation.rollAnimationSeconds = 10;
            yield return KeyPress(Key.Enter);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(world.GetComponentsInChildren<AreaPreview>().Length, Is.GreaterThanOrEqualTo(2));
            string stateBefore = SimulationSnapshot(world.State), visualsBefore = WorldVisualSnapshot(world);
            Vector3 cameraPosition = world.BattleCamera.transform.position; Quaternion cameraRotation = world.BattleCamera.transform.rotation;
            float cameraSize = world.BattleCamera.orthographicSize, elapsed = world.RollElapsed;
            float realDuration = world.State.realCombatSeconds;
            int frames = world.SimulatedFrames;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Space));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(10, 10), buttons = 1 });
            for (int i = 0; i < 8; i++) yield return null;
            Assert.That(SimulationSnapshot(world.State), Is.EqualTo(stateBefore));
            Assert.That(WorldVisualSnapshot(world), Is.EqualTo(visualsBefore));
            Assert.That(world.BattleCamera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(world.BattleCamera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(world.BattleCamera.orthographicSize, Is.EqualTo(cameraSize));
            Assert.That(world.SimulatedFrames, Is.EqualTo(frames));
            Assert.That(world.RollElapsed, Is.GreaterThan(elapsed));
            Assert.That(world.State.realCombatSeconds, Is.GreaterThan(realDuration));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .5f) });
            string beforeReload = CombatJson.Write(world.State);
            world.Suspend(); world.ContinueSaved();
            Assert.That(CombatJson.Write(world.State), Is.EqualTo(beforeReload), "Real-file reload must retain the same complete confirmed-roll state.");
            world.AdvanceRollAnimation(11);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision));
            Assert.That(Time.timeScale, Is.EqualTo(world.Session.Config.focusScale));
            float simulationBefore = world.State.simulationSeconds;
            yield return null; yield return null;
            Assert.That(world.State.simulationSeconds, Is.GreaterThan(simulationBefore));
            Assert.That(world.ActiveActor.statuses[0].remaining, Is.LessThan(3));
            yield return KeyPress(Key.Escape);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            string paused = CombatJson.Write(world.State);
            Vector3 pausedCamera = world.BattleCamera.transform.position;
            yield return null; yield return null; yield return null;
            Assert.That(CombatJson.Write(world.State), Is.EqualTo(paused));
            Assert.That(world.BattleCamera.transform.position, Is.EqualTo(pausedCamera));
        }

        [UnityTest]
        public IEnumerator DefaultAnimationAutomaticallyReturnsToSlowDecisionThenCommitExecutesConfiguredRange()
        {
            Place(Vector3.zero, new Vector3(6.5f, 0, 0));
            world.Session.Config.Action("attack").range = 6.7f;
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            int charges = world.State.charges; ulong draws = world.State.random.visibleTactical.drawCount;
            float deadline = Time.realtimeSinceStartup + 3;
            while (world.State.mode == CombatMode.RollFreeze && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(draws));
            Assert.That(world.Session.Config.focusScale, Is.EqualTo(.1f).Within(.0001f), "v052 default Focus runs at one tenth simulation speed.");
            Assert.That(Time.timeScale, Is.EqualTo(world.Session.Config.focusScale).Within(.0001f));
            var commit = world.Command(CommandKind.Commit);
            Assert.That(commit.success, Is.True, world.Message);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.Command(CommandKind.ExitFocus).success, Is.True);
            yield return null; yield return null;
            Assert.That(world.State.queue.Single(q => q.id == commit.queueId).stage, Is.EqualTo("Executing"));
            Assert.That(world.ActiveActor.x, Is.EqualTo(0).Within(.00001f), "A configured 6.7m action must not auto-move to the obsolete hardcoded 6m threshold.");
            deadline = Time.realtimeSinceStartup + 3;
            while (world.State.queue.Single(q => q.id == commit.queueId).stage == "Executing" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.queue.Single(q => q.id == commit.queueId).stage, Is.EqualTo("Resolved"));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(draws));
        }

        [UnityTest]
        public IEnumerator VisuallyHiddenWallStillRejectsTargetBeforeAnyChargeOrRngConsumption()
        {
            Place(new Vector3(-11, 0, 0), new Vector3(-13, 0, 0));
            yield return null;
            Renderer west = world.GetComponentsInChildren<Renderer>().First(r => r.gameObject.name == "West wall");
            Assert.That(west.enabled, Is.False);
            Assert.That(west.GetComponent<Collider>().enabled, Is.True);
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            int charges = world.State.charges; ulong draws = world.State.random.visibleTactical.drawCount;
            var result = world.Command(CommandKind.ConfirmRoll);
            Assert.That(result.success, Is.False);
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(draws));
            Assert.That(world.State.pendingRoll, Is.Null);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
        }

        [UnityTest]
        public IEnumerator SuccessfulShovePersistsItsCollisionClampedEndpointAtomically()
        {
            Place(new Vector3(9, 0, 0), new Vector3(11, 0, 0));
            world.State.enemies[0].saveBonus = -100; // Explicit guaranteed-failed-save fixture, not a fabricated roll.
            world.SelectedAction = "shove";
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            Assert.That(world.State.pendingRoll.hit, Is.True);
            world.AdvanceRollAnimation(world.CurrentRollDuration);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));
            var committed = world.Command(CommandKind.Commit); Assert.That(committed.success, Is.True,committed.reason);
            Assert.That(world.Command(CommandKind.ExitFocus).success, Is.True);
            float deadline = Time.realtimeSinceStartup + 3;
            while (world.State.queue.Single(q => q.id == committed.queueId).stage != "Resolved" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.queue.Single(q => q.id == committed.queueId).stage, Is.EqualTo("Resolved"));
            float finalX = world.State.enemies[0].x;
            Assert.That(finalX, Is.GreaterThan(11).And.LessThanOrEqualTo(11.44f));
            var loaded = CombatSession.Load(file, world.Session.Config);
            Assert.That(loaded.State.enemies[0].x, Is.EqualTo(finalX));
            Assert.That(loaded.State.queue.Single(q => q.id == committed.queueId).effectApplied, Is.True);
        }

        [UnityTest]
        public IEnumerator FocusAccessibilityAndQuickDiceSettingsSurviveActualFileReload()
        {
            world.SetFocusScale(0); world.SetQuickDice(true);
            world.Suspend(); world.ContinueSaved();
            Assert.That(world.Session.Config.focusScale, Is.Zero);
            Assert.That(world.QuickDice, Is.True);
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            float before = world.State.simulationSeconds;
            yield return null; yield return null;
            Assert.That(world.State.simulationSeconds, Is.EqualTo(before));
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            float deadline = Time.realtimeSinceStartup + 3;
            while (world.State.mode == CombatMode.RollFreeze && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision));
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FreshWorldLoadsFarAwayRollFreezeWithTheSavedCameraAndActorFacing()
        {
            Place(new Vector3(8, 0, 7), new Vector3(10, 0, 7));
            world.Rig.Step(5);
            Vector2 pointer = world.BattleCamera.WorldToScreenPoint(BattleWorld.Position(world.State.enemies[0]));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer }); yield return null; yield return null;
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            world.Rig.Step(.07f); // Preserve a real partially transitioned Focus zoom, not a reconstructed default.
            world.Presentation.rollAnimationSeconds = 10;
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            Vector3 savedPosition = world.BattleCamera.transform.position;
            Quaternion savedRotation = world.BattleCamera.transform.rotation;
            float savedSize = world.BattleCamera.orthographicSize;
            Vector3 savedAim = world.WorldAimPoint;
            Quaternion savedFacing = world.transform.Find("Actor views/fighter").rotation;
            int savedVisiblePreviews = world.GetComponentsInChildren<AreaPreview>().Length;
            string ticket = world.State.pendingRoll.id;
            int[] faces = world.State.pendingRoll.rawFaces.ToArray();
            world.Suspend();
            UnityEngine.Object.Destroy(world.gameObject); world = null; yield return null;

            world = new GameObject("Fresh process equivalent P0 world").AddComponent<BattleWorld>();
            world.InitializeForTests(file, false);
            world.Presentation.rollAnimationSeconds = 10;
            world.ContinueSaved();
            Assert.That(world.Menu, Is.False, world.Message);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(ticket));
            Assert.That(world.State.pendingRoll.rawFaces, Is.EqualTo(faces));
            Assert.That(BattleWorld.Position(world.ActiveActor), Is.EqualTo(new Vector3(8, 0, 7)));
            Assert.That(Vector3.Distance(world.BattleCamera.transform.position, savedPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(world.BattleCamera.transform.rotation, savedRotation), Is.LessThan(.001f));
            Assert.That(world.BattleCamera.orthographicSize, Is.EqualTo(savedSize).Within(.0001f));
            Assert.That(world.WorldAimPoint, Is.EqualTo(savedAim));
            Assert.That(world.GetComponentsInChildren<AreaPreview>().Length, Is.EqualTo(savedVisiblePreviews),
                "Fresh-load RollFreeze must reconstruct the existing target indicator before freezing presentation.");
            var restoredTarget = world.GetComponentsInChildren<AreaPreview>().Single(p => p.Shape.Kind == AreaKind.Circle && Mathf.Abs(p.Shape.Radius - .8f) < .0001f);
            Assert.That(restoredTarget.Shape.Origin, Is.EqualTo(BattleWorld.Position(world.State.Actor(world.State.pendingRoll.targetId))));
            Assert.That(Quaternion.Angle(world.transform.Find("Actor views/fighter").rotation, savedFacing), Is.LessThan(.001f));
            string snapshot = SimulationSnapshot(world.State);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.That(SimulationSnapshot(world.State), Is.EqualTo(snapshot));
            Assert.That(Vector3.Distance(world.BattleCamera.transform.position, savedPosition), Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(world.transform.Find("Actor views/fighter").rotation, savedFacing), Is.LessThan(.001f));
        }

        [UnityTest]
        public IEnumerator PausedFocusWithoutPendingRollRestoresItsTargetInTheSameAndANewWorld()
        {
            Place(Vector3.zero, new Vector3(4, 0, 2));
            yield return KeyPress(Key.Tab);
            Assert.That(TargetMarker(world).gameObject.activeSelf, Is.True);
            yield return KeyPress(Key.Escape);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            Assert.That(world.State.resumeMode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.State.pendingRoll, Is.Null);
            Vector3 expected = BattleWorld.Position(world.State.enemies[0]);
            world.Suspend();
            for (int reload = 0; reload < 2; reload++)
            {
                // Deliberately stale derived view simulates a prior selection; gameplay/save DTOs are untouched.
                var stale = TargetMarker(world);
                stale.SetShape(AreaShape.Circle(new Vector3(-7, 0, 5), .8f)); stale.gameObject.SetActive(false);
                world.ContinueSaved();
                Assert.That(world.Menu, Is.False, world.Message);
                Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
                Assert.That(TargetMarker(world).gameObject.activeSelf, Is.True);
                Assert.That(TargetMarker(world).Shape.Origin, Is.EqualTo(expected));
                yield return null;
            }
            UnityEngine.Object.Destroy(world.gameObject); world = null; yield return null;
            world = new GameObject("Fresh paused Focus presentation world").AddComponent<BattleWorld>();
            world.InitializeForTests(file, false); world.ContinueSaved();
            Assert.That(world.Menu, Is.False, world.Message);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            Assert.That(world.State.resumeMode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.State.pendingRoll, Is.Null);
            Assert.That(TargetMarker(world).gameObject.activeSelf, Is.True);
            Assert.That(TargetMarker(world).Shape.Origin, Is.EqualTo(expected));
            yield return null; yield return null;
            Assert.That(TargetMarker(world).gameObject.activeSelf, Is.True);
            Assert.That(TargetMarker(world).Shape.Origin, Is.EqualTo(expected));
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PausedRealtimeLoadClearsAStaleTargetCircleInTheSameAndANewWorld()
        {
            Place(Vector3.zero, new Vector3(4, 0, 2));
            yield return KeyPress(Key.Escape);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            Assert.That(world.State.resumeMode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(world.State.pendingRoll, Is.Null);
            world.Suspend();
            for (int reload = 0; reload < 2; reload++)
            {
                var stale = TargetMarker(world);
                stale.SetShape(AreaShape.Circle(new Vector3(-7, 0, 5), .8f)); stale.gameObject.SetActive(true);
                world.ContinueSaved();
                Assert.That(world.Menu, Is.False, world.Message);
                Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
                Assert.That(TargetMarker(world).gameObject.activeSelf, Is.False, "A cached selection must not leak into a paused realtime save.");
                yield return null;
            }
            UnityEngine.Object.Destroy(world.gameObject); world = null; yield return null;
            world = new GameObject("Fresh paused realtime presentation world").AddComponent<BattleWorld>();
            world.InitializeForTests(file, false); world.ContinueSaved();
            Assert.That(world.Menu, Is.False, world.Message);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            Assert.That(world.State.resumeMode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(TargetMarker(world).gameObject.activeSelf, Is.False);
            yield return null; yield return null;
            Assert.That(TargetMarker(world).gameObject.activeSelf, Is.False);
        }
    }
}
