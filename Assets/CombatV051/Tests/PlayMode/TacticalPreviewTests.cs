using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    /// <summary>Real scene geometry and actual persistence; movement/seed fixtures are explicit.</summary>
    public sealed class TacticalPreviewTests
    {
        BattleWorld world;
        string directory, file;
        const string Prefix = "tacticalPreview/";

        [UnitySetUp]
        public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "ActionCrpgV051-TacticalPreview", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "profile.json");
            world = new GameObject("Actual tactical preview test world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file);
            world.Controls.Map.Disable(); // Input routes have separate genuine device tests; this suite isolates spatial projection.
            foreach (var actor in world.State.actors) actor.majorRemaining = 20;
            foreach (var enemy in world.State.enemies) { enemy.aiRemaining = 100; enemy.hp = enemy.maxHp = 500; }
            yield return null;
            Place(world.State.Actor("fighter"), new Vector3(-4, 0, -4));
            Place(world.State.Actor("rogue"), new Vector3(-5, 0, -3));
            Place(world.State.Actor("mage"), new Vector3(-6, 0, -4));
            Place(world.State.enemies[0], new Vector3(-1, 0, -4));
            foreach (var actor in world.State.actors)
            {
                actor.majorRemaining = 0; actor.guardRemaining = 0; actor.dodgeRemaining = 0;
                actor.windupRemaining = 0; actor.staggerRemaining = 0; actor.aimPreparing = false;
            }
            world.SetFocusScale(0); // Stable simulation while selecting members; camera still follows its genuine focus policy.
            world.Presentation.rollAnimationSeconds = 20; // Keep the explicit two-frame Freeze observation away from automatic reveal.
            Physics.SyncTransforms();
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ActionCrpgV051-TacticalPreview")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) && Path.GetFullPath(directory).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
            Time.timeScale = 1;
        }

        static void Place(ActorState actor, Vector3 p) { actor.x = p.x; actor.z = p.z; actor.heightY = p.y; }
        Transform Root => world.transform.Find("Tactical spatial preview");
        AreaPreview Circle(string name) => Root.Find(name).GetComponent<AreaPreview>();
        bool Visible => Root && Root.gameObject.activeSelf;

        string VisualSnapshot()
        {
            if (!Root) return "absent";
            return CombatJson.Write(new
            {
                visible = Root.gameObject.activeSelf,
                summary = world.TacticalSpatialSummary,
                lines = Root.GetComponentsInChildren<LineRenderer>(true).Select(line => new
                {
                    parent = line.transform.parent.name, name = line.name, active = line.gameObject.activeInHierarchy,
                    points = Enumerable.Range(0, line.positionCount).Select(i => new[] { line.GetPosition(i).x, line.GetPosition(i).y, line.GetPosition(i).z }).ToArray()
                }).ToArray()
            });
        }

        string StoredPreview() => CombatJson.Write(new
        {
            numbers = world.State.world.numbers.Where(p => p.Key.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(p => p.Key).ToArray(),
            strings = world.State.world.strings.Where(p => p.Key.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(p => p.Key).ToArray()
        });

        static string GameplayWithoutPresentation(CombatState state)
        {
            var clone = state.Clone();
            foreach (string key in clone.world.numbers.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) clone.world.numbers.Remove(key);
            foreach (string key in clone.world.strings.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToArray()) clone.world.strings.Remove(key);
            return CombatJson.Write(clone);
        }

        [UnityTest]
        public IEnumerator EverySelectedRoleShowsPositionTargetRangeAndItsAiDestinationOrLegalRearPlanWithoutMovingTheCameraOrGameplay()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True, world.Message);
            foreach (string id in new[] { "fighter", "rogue", "mage" })
            {
                Assert.That(world.SelectCommandActor(id).success, Is.True, world.Message);
                var actor = world.State.Actor(id);
                actor.aiMoveX = actor.x + 1.25f; actor.aiMoveZ = actor.z + .75f;
                world.SelectedTarget = world.State.enemies[0].id;
                actor.aiTargetId = id == "rogue" ? null : world.SelectedTarget; // A follower with no current threat is distinct from the selected command target.
                string before = CombatJson.Write(world.State);
                Vector3 camera = world.BattleCamera.transform.position; Quaternion rotation = world.BattleCamera.transform.rotation;
                float size = world.BattleCamera.orthographicSize;
                world.RefreshTacticalPreview();
                Assert.That(Visible, Is.True);
                Assert.That(Circle("Current position").Shape.Origin, Is.EqualTo(BattleWorld.Position(actor)));
                if (id == "rogue")
                {
                    Assert.That(Circle("Current target").gameObject.activeSelf, Is.False);
                    Assert.That(world.TacticalSpatialSummary, Does.Contain("当前AI目标 无").And.Contain("指令 " + world.SelectedTarget));
                }
                else Assert.That(Circle("Current target").Shape.Origin, Is.EqualTo(BattleWorld.Position(world.State.enemies[0])));
                Assert.That(Circle("Action range").Shape.Origin, Is.EqualTo(BattleWorld.Position(actor)));
                Assert.That(Circle("Action range").Shape.Radius, Is.EqualTo(world.Session.Config.Action(world.SelectedAction).range));
                Vector3 expected = id == world.State.activeActorId ? BattleWorld.Position(actor) : new Vector3(actor.aiMoveX, actor.heightY, actor.aiMoveZ);
                if (id == "rogue")
                {
                    Assert.That(world.TryGetRearDestination(out expected, out string reason), Is.True, reason);
                    Assert.That(world.TryOrderPath(BattleWorld.Position(actor), expected, out var path), Is.True,
                        "The v052 preview must correspond to a real reachable rear route, not an invented AI destination.");
                    Assert.That(path.Last(), Is.EqualTo(expected));
                    var enemy = world.State.Actor(world.SelectedTarget);
                    Vector3 enemyFacing = new Vector3(enemy.lockedX, enemy.heightY, enemy.lockedZ) - BattleWorld.Position(enemy);
                    Assert.That(Vector3.Dot((expected - BattleWorld.Position(enemy)).normalized, enemyFacing.normalized),
                        Is.LessThanOrEqualTo(world.Session.Config.rogueBackstabDot), "The suggested destination must actually be behind the target.");
                    Assert.That(world.TacticalSpatialSummary, Does.Contain("可到达侧后位置"));
                }
                Assert.That(Circle("Expected position").Shape.Origin, Is.EqualTo(expected));
                Assert.That(CombatJson.Write(world.State), Is.EqualTo(before), "Refresh must not change DTOs, command receipts, resources or any RNG stream.");
                Assert.That(world.BattleCamera.transform.position, Is.EqualTo(camera));
                Assert.That(world.BattleCamera.transform.rotation, Is.EqualTo(rotation));
                Assert.That(world.BattleCamera.orthographicSize, Is.EqualTo(size));
                string gameplay = GameplayWithoutPresentation(world.State);
                world.CaptureTacticalPreviewSnapshot();
                Assert.That(world.State.world.strings[Prefix + "actor"], Is.EqualTo(id));
                Assert.That(world.State.world.strings[Prefix + "target"], Is.EqualTo(id == "rogue" ? "" : world.SelectedTarget));
                Assert.That(world.State.world.strings[Prefix + "commandTarget"], Is.EqualTo(world.SelectedTarget));
                Assert.That(GameplayWithoutPresentation(world.State), Is.EqualTo(gameplay), "Capture writes only the presentation namespace.");
                Assert.That(world.TacticalSpatialSummary, Does.Contain("预计").And.Contain("射程"));
            }
            Assert.That(Root.parent, Is.EqualTo(world.transform), "Map rebuilding must not own the tactical preview root.");
            Assert.That(world.State.activeActorId, Is.EqualTo("fighter"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator QueuedPreviewUsesThePaidTicketRangeAndTheSameRealWallSweepAsMovement()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            world.SelectedActor = "fighter"; world.SelectedAction = "attack";
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            Assert.That(world.Command(CommandKind.RevealRoll).success, Is.True);
            Assert.That(world.Command(CommandKind.Commit).success, Is.True);
            var queued = world.State.queue.Single(q => q.stage == "Queued");
            var paid = world.State.rolls.Single(t => t.id == queued.ticketId);
            float paidRange = paid.spec.range;
            Place(world.State.Actor("fighter"), new Vector3(-5, 0, -5));
            Place(world.State.Actor(queued.targetId), new Vector3(5, 0, -5));
            // An explicit fixture moves the target after the real roll/Commit; no fake queue is inserted.
            world.Session.Config.Action("attack").range = 99;
            var wall = BattleVisual.Box("Tactical preview sweep fixture", new Vector3(-3, 1, -5), new Vector3(.3f, 2, 3), Color.gray, world.transform, 8);
            Physics.SyncTransforms();
            var actor = world.State.Actor("fighter"); var target = world.State.Actor(queued.targetId);
            Vector3 delta = BattleGeometry.Ground(BattleWorld.Position(target) - BattleWorld.Position(actor));
            Vector3 expected = BattleGeometry.MoveAgainstWalls(BattleWorld.Position(actor), delta.normalized * (delta.magnitude - paidRange + .01f), .36f, 1 << 8);
            string before = CombatJson.Write(world.State);
            world.RefreshTacticalPreview();
            Assert.That(Circle("Action range").Shape.Radius, Is.EqualTo(paidRange).And.Not.EqualTo(99));
            Assert.That(Circle("Expected position").Shape.Origin, Is.EqualTo(expected));
            Assert.That(expected.x, Is.LessThan(-3.4f), "The capsule must stop on the near side of the actual fixture wall.");
            var path = Root.Find("Expected movement").GetComponent<LineRenderer>();
            Assert.That(path.GetPosition(0), Is.EqualTo(BattleWorld.Position(actor) + Vector3.up * .055f));
            Assert.That(path.GetPosition(1), Is.EqualTo(expected + Vector3.up * .055f));
            Assert.That(CombatJson.Write(world.State), Is.EqualTo(before));
            Assert.That(world.TacticalSpatialSummary, Does.Contain("遮挡"));
            UnityEngine.Object.Destroy(wall); yield return null;
        }

        [UnityTest]
        public IEnumerator RollFreezePreservesExactPreviewAndItsFileSnapshotAcrossAFreshWorld()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            world.RefreshTacticalPreview(); world.CaptureTacticalPreviewSnapshot();
            string visual = VisualSnapshot(), stored = StoredPreview();
            Assert.That(world.Command(CommandKind.ConfirmRoll).success, Is.True, world.Message);
            string ticket = world.State.pendingRoll.id; int[] faces = world.State.pendingRoll.rawFaces.ToArray();
            Vector3 camera = world.BattleCamera.transform.position; Quaternion rotation = world.BattleCamera.transform.rotation;
            string rng = CombatJson.Write(world.State.random);
            for (int i = 0; i < 2; i++)
            {
                world.RefreshTacticalPreview(); world.CaptureTacticalPreviewSnapshot();
                Assert.That(VisualSnapshot(), Is.EqualTo(visual)); Assert.That(StoredPreview(), Is.EqualTo(stored));
                yield return null;
                Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
                Assert.That(world.BattleCamera.transform.position, Is.EqualTo(camera));
                Assert.That(world.BattleCamera.transform.rotation, Is.EqualTo(rotation));
                Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            }
            world.Suspend();
            Assert.That(File.Exists(file), Is.True);
            UnityEngine.Object.Destroy(world.gameObject); yield return null;
            world = new GameObject("Fresh tactical preview load world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file, false); world.Controls.Map.Disable(); world.Presentation.rollAnimationSeconds = 20;
            world.ContinueSaved();
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze), world.Message);
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(ticket)); Assert.That(world.State.pendingRoll.rawFaces, Is.EqualTo(faces));
            Assert.That(StoredPreview(), Is.EqualTo(stored)); Assert.That(VisualSnapshot(), Is.EqualTo(visual));
            Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            Assert.That(world.BattleCamera.transform.position, Is.EqualTo(camera));
            for (int i = 0; i < 2; i++) { yield return null; Assert.That(VisualSnapshot(), Is.EqualTo(visual)); }
        }

        [UnityTest]
        public IEnumerator AReactionOriginatingInRealtimeStaysWithoutTacticalCirclesAfterFreezeAndReload()
        {
            world.RefreshTacticalPreview(); world.CaptureTacticalPreviewSnapshot();
            Assert.That(Visible, Is.False); Assert.That(world.TacticalSpatialSummary, Is.Empty);
            var fighter = world.State.Actor("fighter"); var enemy = world.State.enemies[0];
            Place(enemy, BattleWorld.Position(fighter) + Vector3.right * 2);
            world.State.random.backgroundCombat = RandomStreams.Create(19).backgroundCombat;
            enemy.attackBonus = fighter.armorClass + 2 - 12; // Seed 19's first real background d20 is 12.
            fighter.reactionRemaining = 0; fighter.guardRemaining = 0; fighter.dodgeRemaining = 0;
            var hit = world.Session.ResolveIncoming("preview-reaction-incoming", enemy.id, fighter.id, 4, false, 12, "preview-reaction-root", true);
            Assert.That(hit.success && hit.deferred, Is.True, hit.reason);
            Assert.That(world.React(CommandKind.BeginReaction, hit.reactionWindowId).success, Is.True, world.Message);
            world.RefreshTacticalPreview(); Assert.That(Visible, Is.False);
            Assert.That(world.React(CommandKind.ConfirmReactionRoll).success, Is.True, world.Message);
            world.RefreshTacticalPreview(); world.CaptureTacticalPreviewSnapshot();
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(world.State.world.numbers[Prefix + "visible"], Is.Zero);
            string stored = StoredPreview(), rng = CombatJson.Write(world.State.random), ticket = world.State.pendingRoll.id;
            world.Suspend();
            UnityEngine.Object.Destroy(world.gameObject); yield return null;
            world = new GameObject("Fresh realtime reaction preview load").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file, false); world.Controls.Map.Disable(); world.Presentation.rollAnimationSeconds = 20;
            world.ContinueSaved();
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(world.State.pendingRoll.id, Is.EqualTo(ticket)); Assert.That(StoredPreview(), Is.EqualTo(stored));
            Assert.That(Visible, Is.False); Assert.That(world.TacticalSpatialSummary, Is.Empty);
            Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            yield return null;
            Assert.That(Visible, Is.False); Assert.That(world.TacticalSpatialSummary, Is.Empty);
        }
    }
}
