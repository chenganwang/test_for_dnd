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
    /// <summary>Actual Input System and GraphicRaycaster routes. Only incoming-hit conditions are fixture-controlled.</summary>
    public sealed class ReactionInputPlayTests
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
            directory = Path.Combine(Path.GetTempPath(), "ActionCrpgV051-ReactionInput", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "profile.json");
            world = new GameObject("Actual P1 reaction input world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file);
            // Explicit deterministic encounter fixture: suppress unrelated attacks, retain normal AI movement.
            world.State.random = RandomStreams.Create(19);
            foreach (var actor in world.State.actors) actor.majorRemaining = 20;
            foreach (var enemy in world.State.enemies) { enemy.aiRemaining = 100; enemy.hp = enemy.maxHp = 500; }
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
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ActionCrpgV051-ReactionInput")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) && Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
            Time.timeScale = 1;
        }

        ActorState Fighter => world.State.Actor("fighter");
        ActorState Mage => world.State.Actor("mage");
        ActorState Enemy => world.State.enemies[0];
        static void Place(ActorState actor, Vector3 point) { actor.x = point.x; actor.z = point.z; actor.heightY = point.y; }

        IEnumerator KeyPress(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
        }

        IEnumerator ClickUi(string id, bool forbidFreeze = false)
        {
            world.Hud.RefreshView();
            yield return null; // Newly activated panel Graphics need a render registration before querying their depth.
            Canvas.ForceUpdateCanvases();
            Button button = world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b => b.gameObject.name == id);
            Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True,
                "The actual reaction button must be visible and legal: " + id + "; " + world.Message);
            var rect = (RectTransform)button.transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            var graphic = button.targetGraphic;
            var raycaster = world.Hud.Canvas.GetComponent<GraphicRaycaster>();
            string diagnostic = id + "; " + Screen.width + "x" + Screen.height + "; " + point + "; " +
                string.Join(",", hits.Select(hit => hit.gameObject.name)) + "; panel=" + button.transform.parent.name +
                "; panelActive=" + button.transform.parent.gameObject.activeInHierarchy + "; depth=" + graphic.depth +
                "; cull=" + graphic.canvasRenderer.cull + "; raycasterEnabled=" + raycaster.isActiveAndEnabled;
            TestContext.WriteLine(diagnostic);
            Assert.That(point.x, Is.InRange(0f, (float)Screen.width));
            Assert.That(point.y, Is.InRange(0f, (float)Screen.height));
            Assert.That(hits.Count > 0 && (hits[0].gameObject == button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)),
                Is.True, "The real raycaster must reach this button: " + diagnostic);
            int observedClicks = 0;
            button.onClick.AddListener(() => observedClicks++); // Observe only. Never invoke onClick or its callback.
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            if (forbidFreeze) Assert.That(world.State.mode, Is.Not.EqualTo(CombatMode.RollFreeze));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 }); yield return null;
            if (forbidFreeze) Assert.That(world.State.mode, Is.Not.EqualTo(CombatMode.RollFreeze));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            if (forbidFreeze) Assert.That(world.State.mode, Is.Not.EqualTo(CombatMode.RollFreeze));
            Assert.That(observedClicks, Is.EqualTo(1), "Actual UI dispatch count: " + diagnostic);
        }

        PendingIncoming OfferIncoming(string defenderId, string rootId, int damage = 4)
        {
            var defender = world.State.Actor(defenderId);
            defender.guardRemaining = 0; defender.dodgeRemaining = 0; defender.reactionRemaining = 0;
            // Seed 19's first background d20 is 12. Keep real class AC and make a noncritical hit Shield can change.
            var probe = CombatJson.Clone(world.State.random.backgroundCombat);
            Assert.That(RandomStreams.D20(probe), Is.EqualTo(12), "This fixture must retain the known background stream.");
            Enemy.attackBonus = defender.armorClass + 2 - 12;
            Place(Enemy, BattleWorld.Position(defender) + Vector3.right * 2);
            Enemy.aiRemaining = 100; Physics.SyncTransforms();
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(Enemy), BattleWorld.Position(defender)), Is.True);
            var result = world.Session.ResolveIncoming("incoming/" + rootId, Enemy.id, defenderId, damage, false, 12, rootId, true);
            Assert.That(result.success && result.deferred, Is.True, result.reason);
            var incoming = world.State.incomingEvents.Single(e => e.rootId == rootId && e.defenderId == defenderId);
            Assert.That(incoming.rawFaces, Is.EqualTo(new[] { 12 }));
            Assert.That(incoming.damageApplied, Is.False);
            Assert.That(world.State.reactionWindows.Single(w => w.id == incoming.reactionWindowId).stage, Is.EqualTo("Offered"));
            world.Hud.RefreshView();
            return incoming;
        }

        IEnumerator WaitForAutomaticReveal()
        {
            float until = Time.realtimeSinceStartup + 3;
            while (world.State.mode == CombatMode.RollFreeze && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision), "The ordinary animation Update must Reveal automatically.");
            Assert.That(Time.timeScale, Is.EqualTo(world.Session.Config.focusScale));
        }

        string Resources() => CombatJson.Write(world.State.actors.OrderBy(a => a.id).Select(a => new
        { a.id, a.inspiration, a.superiority, a.spellSlots, a.items }).ToArray());

        static string SimulationSnapshot(CombatState state)
        {
            var copy = state.Clone(); copy.realCombatSeconds = 0; // Telemetry continues; simulation does not.
            return CombatJson.Write(copy);
        }

        string VisualSnapshot() => CombatJson.Write(new
        {
            actors = world.transform.Find("Actor views").GetComponentsInChildren<Transform>(true).Select(t => new
            { t.name, t.gameObject.activeSelf, position = t.position.ToString("F6"), rotation = t.rotation.ToString("F6") }).ToArray(),
            previews = world.GetComponentsInChildren<AreaPreview>(true).Select(p => new
            {
                p.gameObject.activeSelf,
                lines = p.GetComponentsInChildren<LineRenderer>(true).Select(line => Enumerable.Range(0, line.positionCount)
                    .Select(i => line.GetPosition(i).ToString("F6")).ToArray()).ToArray()
            }).ToArray()
        });

        [UnityTest]
        public IEnumerator RealNumberKeysChooseWhoWControlsAndFocusNumbersOnlySelectTheCommandActor()
        {
            Quaternion yaw = world.BattleCamera.transform.rotation;
            string resources = Resources(); int charges = world.State.charges;
            ulong visible = world.State.random.visibleTactical.drawCount, backgroundDraws = world.State.random.backgroundCombat.drawCount;
            string[] ids = { "fighter", "rogue", "mage" };
            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3 };
            for (int selected = 0; selected < ids.Length; selected++)
            {
                Vector3 right = world.Rig.ScreenMove(Vector2.right), up = world.Rig.ScreenMove(Vector2.up);
                Place(world.State.Actor(ids[selected]), Vector3.zero);
                Place(world.State.Actor(ids[(selected + 1) % 3]), right * 4);
                Place(world.State.Actor(ids[(selected + 2) % 3]), -right * 4);
                Place(Enemy, new Vector3(10, 0, 8)); Enemy.aiRemaining = 100;
                yield return KeyPress(keys[selected]);
                Assert.That(world.State.activeActorId, Is.EqualTo(ids[selected]));
                var positions = world.State.actors.ToDictionary(a => a.id, BattleWorld.Position);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                float heldSince = Time.realtimeSinceStartup;
                int heldFrames = 0;
                while (Time.realtimeSinceStartup - heldSince < .08f && heldFrames++ < 4096) yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
                Assert.That(Time.realtimeSinceStartup - heldSince, Is.GreaterThanOrEqualTo(.08f));
                Vector3 moved = BattleWorld.Position(world.State.Actor(ids[selected])) - positions[ids[selected]];
                Assert.That(moved.magnitude, Is.GreaterThan(.01f));
                Assert.That(Vector3.Dot(moved.normalized, up), Is.GreaterThan(.999f));
                // AI remains enabled: allies may follow, but do not mirror W as extra directly controlled actors.
                foreach (var ally in world.State.actors.Where(a => a.id != ids[selected]))
                {
                    Vector3 aiMoved = BattleWorld.Position(ally) - positions[ally.id];
                    Assert.That(ally.aiPolicyId, Is.EqualTo("conservative-v1"));
                    if (aiMoved.magnitude > .01f) Assert.That(Vector3.Dot(aiMoved.normalized, up), Is.LessThan(.98f));
                }
                Assert.That(Quaternion.Angle(yaw, world.BattleCamera.transform.rotation), Is.LessThan(.001f));
            }
            yield return KeyPress(Key.Tab);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            string active = world.State.activeActorId;
            yield return KeyPress(Key.Digit2);
            Assert.That(world.State.commandActorId, Is.EqualTo("rogue"));
            Assert.That(world.SelectedActor, Is.EqualTo("rogue"));
            Assert.That(world.State.activeActorId, Is.EqualTo(active));
            yield return KeyPress(Key.Digit3);
            Assert.That(world.State.commandActorId, Is.EqualTo("mage"));
            Assert.That(world.SelectedActor, Is.EqualTo("mage"));
            Assert.That(world.State.activeActorId, Is.EqualTo(active));
            Assert.That(Resources(), Is.EqualTo(resources));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(Quaternion.Angle(yaw, world.BattleCamera.transform.rotation), Is.LessThan(.001f));
        }

        [UnityTest]
        public IEnumerator ActualShieldOfferAndConfirmClicksSpendOneSlotWithoutFreezeOrRerollingTheAttack()
        {
            Place(Mage, Vector3.zero); Place(Fighter, new Vector3(-4, 0, -3));
            int hp = Mage.hp, slots = Mage.spellSlots, charges = world.State.charges, superiority = Mage.superiority;
            int rollCount = world.State.rolls.Count;
            ulong visible = world.State.random.visibleTactical.drawCount;
            string enemyId = Enemy.id;
            var incoming = OfferIncoming("mage", "shield-ui");
            string incomingId = incoming.id; int total = incoming.total;
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            yield return ClickUi("reaction-offer-accept", true);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.ReactionPrompt));
            Assert.That(Mage.spellSlots, Is.EqualTo(slots));
            Assert.That(Mage.heldSpellSlots, Is.EqualTo(1));
            yield return ClickUi("reaction-confirm", true);
            var applied = world.State.incomingEvents.Single(e => e.id == incomingId);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(applied.damageApplied, Is.True); Assert.That(applied.finalDamage, Is.Zero);
            Assert.That(applied.rawFaces, Is.EqualTo(new[] { 12 })); Assert.That(applied.total, Is.EqualTo(total));
            Assert.That(Mage.hp, Is.EqualTo(hp)); Assert.That(Mage.spellSlots, Is.EqualTo(slots - 1));
            Assert.That(Mage.heldSpellSlots, Is.Zero); Assert.That(Mage.reactionRemaining, Is.GreaterThan(0));
            Assert.That(Mage.superiority, Is.EqualTo(superiority));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.rolls.Count, Is.EqualTo(rollCount));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            var duplicate = world.Session.ResolveIncoming("incoming/shield-ui", enemyId, "mage", 4, false, 12, "shield-ui", true);
            Assert.That(duplicate.success && duplicate.duplicate, Is.True);
            Assert.That(world.State.incomingEvents.Count(e => e.id == incomingId), Is.EqualTo(1));
            Assert.That(Mage.hp, Is.EqualTo(hp)); Assert.That(Mage.spellSlots, Is.EqualTo(slots - 1));
        }

        [UnityTest]
        public IEnumerator ActualParryButtonsFreezeTheWorldRestoreTheSameSavedTicketAndCommitOnce()
        {
            Place(Fighter, Vector3.zero);
            int hp = Fighter.hp, superiority = Fighter.superiority, inspiration = Fighter.inspiration, charges = world.State.charges;
            var incoming = OfferIncoming("fighter", "parry-ui");
            string incomingId = incoming.id;
            ulong originalBackground = world.State.random.backgroundCombat.drawCount;
            Enemy.aiState = 1; Enemy.aiRemaining = 100; Enemy.attackSequence = 1;
            Enemy.attackGroupId = "independent-visible-warning"; Enemy.lockedX = Fighter.x; Enemy.lockedZ = Fighter.z;
            yield return ClickUi("reaction-offer-accept");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.ReactionPrompt));
            yield return ClickUi("reaction-confirm");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            Assert.That(world.State.pendingRoll.purpose, Is.EqualTo("Reaction"));
            Assert.That(world.State.pendingRoll.chargeSpent, Is.Zero);
            string ticket = world.State.pendingRoll.id; int[] raw = world.State.pendingRoll.rawFaces.ToArray();
            bool parried = world.State.pendingRoll.hit;
            string frozen = SimulationSnapshot(world.State), visual = VisualSnapshot();
            Vector3 camera = world.BattleCamera.transform.position; Quaternion orientation = world.BattleCamera.transform.rotation;
            float size = world.BattleCamera.orthographicSize;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            for (int frame = 0; frame < 2; frame++)
            {
                yield return null;
                Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), "Default dice duration must leave these two observation frames frozen.");
                Assert.That(SimulationSnapshot(world.State), Is.EqualTo(frozen));
                Assert.That(VisualSnapshot(), Is.EqualTo(visual));
                Assert.That(world.BattleCamera.transform.position, Is.EqualTo(camera));
                Assert.That(Quaternion.Angle(world.BattleCamera.transform.rotation, orientation), Is.LessThan(.001f));
                Assert.That(world.BattleCamera.orthographicSize, Is.EqualTo(size));
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            world.Suspend();
            var disk = CombatSession.Load(file, world.Session.Config, "p1-party").State;
            Assert.That(disk.pendingRoll.id, Is.EqualTo(ticket)); Assert.That(disk.pendingRoll.rawFaces, Is.EqualTo(raw));
            Assert.That(disk.actors.Single(a => a.id == "fighter").superiority, Is.EqualTo(superiority));
            world.ContinueSaved();
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(ticket));
            yield return WaitForAutomaticReveal();
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(ticket)); Assert.That(world.State.pendingRoll.rawFaces, Is.EqualTo(raw));
            yield return ClickUi("reaction-commit");
            Assert.That(world.State.rolls.Single(t => t.id == ticket).stage, Is.EqualTo("Resolved"));
            Assert.That(world.State.incomingEvents.Single(e => e.id == incomingId).damageApplied, Is.True);
            Assert.That(Fighter.superiority, Is.EqualTo(superiority - 1));
            Assert.That(Fighter.inspiration, Is.EqualTo(inspiration)); Assert.That(Fighter.reactionRemaining, Is.GreaterThan(0));
            Assert.That(Fighter.hp, Is.EqualTo(hp - (parried ? 0 : 4)));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(originalBackground));
            Assert.That(world.State.pendingRoll, Is.Null); Assert.That(world.State.activeReaction, Is.Null);
            Assert.That(world.State.reactionWindows.Single(w => w.incomingId == incomingId).resourcesSpent, Is.True);
        }

        [UnityTest]
        public IEnumerator ActualRAndEnterTemporarilySuspendANormalPostRollAndReturnToItsOriginalCommitButton()
        {
            Place(Fighter, Vector3.zero); Place(Mage, new Vector3(-2, 0, -1)); Place(Enemy, new Vector3(2, 0, 0));
            Fighter.majorRemaining = 0;
            yield return KeyPress(Key.Tab);
            yield return KeyPress(Key.Enter);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            yield return WaitForAutomaticReveal();
            string normalTicket = world.State.pendingRoll.id; int[] normalRaw = world.State.pendingRoll.rawFaces.ToArray();
            string majorOwner = Fighter.heldBy; int charges = world.State.charges, slots = Mage.spellSlots;
            ulong visible = world.State.random.visibleTactical.drawCount;
            var incoming = OfferIncoming("mage", "suspended-normal-ui");
            string incomingId = incoming.id;
            yield return KeyPress(Key.R);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.ReactionPrompt));
            Assert.That(world.State.decisionResume.suspendedTicketId, Is.EqualTo(normalTicket));
            Assert.That(world.State.rolls.Single(t => t.id == normalTicket).stage, Is.EqualTo("SuspendedAwaitDecision"));
            Assert.That(Fighter.heldBy, Is.EqualTo(majorOwner));
            yield return KeyPress(Key.Enter); // Shield requires no dice, and must restore the suspended ordinary result.
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision));
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(normalTicket));
            Assert.That(world.State.pendingRoll.rawFaces, Is.EqualTo(normalRaw));
            Assert.That(world.State.pendingRoll.purpose, Is.EqualTo("Tactical"));
            Assert.That(world.State.decisionResume, Is.Null);
            Assert.That(Fighter.heldBy, Is.EqualTo(majorOwner));
            Assert.That(Mage.spellSlots, Is.EqualTo(slots - 1));
            Assert.That(world.State.incomingEvents.Single(e => e.id == incomingId).damageApplied, Is.True);
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
            yield return ClickUi("commit");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.State.queue.Count(q => q.ticketId == normalTicket), Is.EqualTo(1));
            Assert.That(world.State.queue.Single(q => q.ticketId == normalTicket).stage, Is.EqualTo("Queued"));
            Assert.That(world.State.charges, Is.EqualTo(charges));
        }

        [UnityTest]
        public IEnumerator ActualCancelQueueButtonReleasesSelectedActorsHoldWithoutRefundingTheCharge()
        {
            Place(Fighter, Vector3.zero); Place(Enemy, new Vector3(3, 0, 0));
            Fighter.majorRemaining = 0;
            yield return KeyPress(Key.Tab);
            yield return KeyPress(Key.Enter);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            yield return WaitForAutomaticReveal();
            string ticket = world.State.pendingRoll.id;
            yield return ClickUi("commit");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.TacticalFocus));
            string queueId = world.State.queue.Single(q => q.ticketId == ticket).id;
            Assert.That(world.SelectedActor, Is.EqualTo("fighter"));
            Assert.That(Fighter.heldBy, Is.EqualTo(ticket));
            Assert.That(world.State.queue.Single(q => q.id == queueId).resourcesSpent, Is.False);
            int charges = world.State.charges, slots = Fighter.spellSlots;
            ulong visible = world.State.random.visibleTactical.drawCount;
            yield return ClickUi("cancel-queue");
            Assert.That(world.State.queue.Single(q => q.id == queueId).stage, Is.EqualTo("Canceled"));
            Assert.That(world.State.rolls.Single(t => t.id == ticket).stage, Is.EqualTo("Canceled"));
            Assert.That(Fighter.heldBy, Is.Null.Or.Empty);
            Assert.That(Fighter.heldSpellSlots, Is.Zero);
            Assert.That(Fighter.heldItems, Is.Zero);
            Assert.That(Fighter.majorRemaining, Is.Zero);
            Assert.That(Fighter.spellSlots, Is.EqualTo(slots));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
        }
    }
}
