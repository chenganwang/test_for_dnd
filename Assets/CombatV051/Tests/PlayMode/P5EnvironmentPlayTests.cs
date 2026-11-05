using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    public sealed class P5EnvironmentPlayTests
    {
        GameObject fixtures;
        BattleWorld world;
        readonly Vector3 origin = new Vector3(200, 0, 200);
        const int WallMask = 1 << 8;

        [SetUp] public void Setup() { fixtures = new GameObject("P5 actual physics fixtures"); }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            if (fixtures) UnityEngine.Object.Destroy(fixtures);
            yield return null; yield return null; Time.timeScale = 1;
        }
        GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name; box.layer = 8;
            box.transform.SetParent(fixtures.transform); box.transform.position = origin + position; box.transform.localScale = size;
            return box;
        }
        void CreateWorld(bool dungeon = false)
        {
            world = new GameObject("P5 environment integration").AddComponent<BattleWorld>();
            string path = Path.Combine(Path.GetTempPath(), "ActionCRPG-P5Environment", Guid.NewGuid().ToString("N"), "profile.json");
            if (dungeon) world.InitializeDungeonForTests(path);
            else world.InitializePartyForTests(path);
            world.Controls.Map.Disable(); world.Menu = true;
            world.Session.Config.p5 = new P5Config();
            CombatSession.InitializeP5State(world.State, world.Session.Config);
        }

        [Test] public void LowCoverIsDirectionalRangedProtectionWithoutBecomingOpaqueOrStacking()
        {
            Box("cover A", new Vector3(0, .45f, 0), new Vector3(4, .9f, .6f)).AddComponent<P5LowCover>();
            Box("cover B", new Vector3(0, .45f, .9f), new Vector3(4, .9f, .6f)).AddComponent<P5LowCover>();
            Physics.SyncTransforms();
            Vector3 front = origin + Vector3.back * 3, behind = origin + Vector3.forward * 3;
            Assert.That(P5EnvironmentGeometry.HasLineOfEffect(front, behind, WallMask), Is.True);
            Assert.That(P5EnvironmentGeometry.CoverArmorBonus(front, behind, true, WallMask), Is.EqualTo(2));
            Assert.That(P5EnvironmentGeometry.CoverArmorBonus(front, behind, false, WallMask), Is.Zero);
            Assert.That(P5EnvironmentGeometry.CoverArmorBonus(behind + Vector3.right * 3, behind, true, WallMask), Is.Zero);
            Assert.That(P5EnvironmentGeometry.CoverArmorBonus(front + Vector3.up * 4, behind + Vector3.up * 4, true, WallMask), Is.Zero);
            Assert.That(BattleGeometry.MoveAgainstWalls(front, Vector3.forward * 6, .36f, WallMask).z, Is.LessThan(origin.z), "Cover remains a physical obstacle.");
            Box("opaque full wall", new Vector3(0, 1.4f, 2), new Vector3(4, 2.8f, .3f)); Physics.SyncTransforms();
            Assert.That(P5EnvironmentGeometry.HasLineOfEffect(front, behind, WallMask), Is.False);
            Assert.That(P5EnvironmentGeometry.CoverArmorBonus(front, behind, true, WallMask), Is.Zero, "An illegal attack is not described as merely cover-protected.");
        }

        [Test] public void RegisteredImpactRequiresRealTravelAndARealSweptContactIncludingThinWalls()
        {
            var wall = Box("thin registered wall", new Vector3(2, 1, 0), new Vector3(.02f, 2, 5));
            wall.AddComponent<P5ImpactSurface>().SurfaceId = "test/impact"; Physics.SyncTransforms();
            var moved = P5EnvironmentGeometry.SweepPush(origin, Vector3.right * 8, .36f, WallMask);
            Assert.That(moved.HasImpact, Is.True); Assert.That(moved.collision, Is.EqualTo(wall.GetComponent<Collider>()));
            Assert.That(moved.actualDistance, Is.EqualTo(moved.end.x - origin.x).Within(.001f));
            Assert.That(moved.end.x, Is.LessThan(origin.x + 1.64f), "A long push cannot tunnel through the thin collider.");
            var near = P5EnvironmentGeometry.SweepPush(origin + Vector3.right * 1.2f, Vector3.right * 2, .36f, WallMask);
            Assert.That(near.collision, Is.Not.Null); Assert.That(near.actualDistance, Is.LessThan(.6f)); Assert.That(near.HasImpact, Is.False);
            var shortMove = P5EnvironmentGeometry.SweepPush(origin, Vector3.right * .4f, .36f, WallMask);
            Assert.That(shortMove.HasImpact, Is.False); Assert.That(shortMove.collision, Is.Null);
            var registration = wall.GetComponent<P5ImpactSurface>(); registration.RestrictContactSpan = true;
            registration.ContactCenter = origin + Vector3.right * 2; registration.ContactTangent = Vector3.forward; registration.ContactHalfWidth = .4f;
            var outsideNode = P5EnvironmentGeometry.SweepPush(origin + Vector3.forward, Vector3.right * 8, .36f, WallMask);
            Assert.That(outsideNode.collision, Is.Not.Null); Assert.That(outsideNode.impactSurfaceId, Is.Null,
                "The rest of a long wall must not inherit the visibly marked impact span.");
        }

        [Test] public void StartingOverlapAndOrdinaryWallsDoNotFabricateRegisteredImpactEvidence()
        {
            Box("ordinary wall", new Vector3(2, 1, 0), new Vector3(.2f, 2, 5)); Physics.SyncTransforms();
            var ordinary = P5EnvironmentGeometry.SweepPush(origin, Vector3.right * 5, .36f, WallMask);
            Assert.That(ordinary.collision, Is.Not.Null); Assert.That(ordinary.impactSurfaceId, Is.Null); Assert.That(ordinary.HasImpact, Is.False);
            var overlapping = P5EnvironmentGeometry.SweepPush(origin + Vector3.right * 1.6f, Vector3.right * 5, .36f, WallMask);
            Assert.That(overlapping.initialOverlap, Is.True); Assert.That(overlapping.actualDistance, Is.Zero);
            Assert.That(overlapping.end, Is.EqualTo(overlapping.start)); Assert.That(overlapping.HasImpact, Is.False);
        }

        [UnityTest] public IEnumerator ExistingR1GetsExactlyOneOptionalCoverAndOneWallIdentityWithoutReplacingItsGeometry()
        {
            CreateWorld(true);
            var oldWalls = world.DungeonMap.GetComponentsInChildren<Collider>().Where(c => c.name == "Wall R1").Select(c => c.GetInstanceID()).ToArray();
            world.ReconcileP5Environment(); world.ReconcileP5Environment();
            Assert.That(world.DungeonMap.GetComponentsInChildren<P5LowCover>().Length, Is.EqualTo(1));
            var surface = world.DungeonMap.GetComponentsInChildren<P5ImpactSurface>().Single();
            Assert.That(surface.SurfaceId, Is.EqualTo(BattleWorld.P5R1ImpactId));
            CollectionAssert.Contains(oldWalls, surface.GetComponent<Collider>().GetInstanceID(), "The impact surface must be an existing physical wall.");
            CollectionAssert.AreEquivalent(oldWalls, world.DungeonMap.GetComponentsInChildren<Collider>().Where(c => c.name == "Wall R1").Select(c => c.GetInstanceID()));
            world.Session.Config.p5 = null; world.ReconcileP5Environment();
            Assert.That(world.DungeonMap.GetComponentsInChildren<P5LowCover>().Length, Is.Zero, "Legacy configuration must not acquire a new blocking cover.");
            yield return null;
        }

        [UnityTest] public IEnumerator QueuedEvidenceUsesLockedActorTargetAndDirectionAndNeverMutatesTheirPositions()
        {
            CreateWorld(); var actor = world.State.Actor("fighter"); var enemy = world.State.enemies[0];
            actor.x = origin.x - 1; actor.z = origin.z; enemy.x = origin.x; enemy.z = origin.z;
            var wall = Box("locked push wall", new Vector3(2, 1, 0), new Vector3(.2f, 2, 4));
            wall.AddComponent<P5ImpactSurface>().SurfaceId = "test/locked"; Physics.SyncTransforms();
            world.State.rolls.Add(new RollTicket { id = "p5-locked", actorId = actor.id, targetId = enemy.id, actionId = "shove",
                spec = new ActionSpec { range = 2.5f }, p5 = new P5ActionContext { targetId = enemy.id, directionX = 1, pushDistance = 3 } });
            world.State.queue.Add(new QueuedAction { id = "p5-queue", ticketId = "p5-locked", actorId = actor.id, targetId = enemy.id });
            world.SelectedActor = "mage"; world.SelectedTarget = "not-the-ticket-target";
            var command = new CombatCommand { kind = CommandKind.ResolveQueued, queueId = "p5-queue", actorId = "mage", targetId = "wrong" };
            world.PopulateP5CommandEvidence(command);
            Assert.That(command.actorId, Is.EqualTo(actor.id)); Assert.That(command.targetId, Is.EqualTo(enemy.id));
            var evidence = command.p5.targets.Single();
            Assert.That(evidence.hasDisplacement && evidence.impactRegistered, Is.True);
            Assert.That(evidence.actualDistance, Is.GreaterThanOrEqualTo(.6f)); Assert.That(evidence.x, Is.LessThan(origin.x + 1.55f));
            Assert.That(enemy.x, Is.EqualTo(origin.x)); Assert.That(enemy.z, Is.EqualTo(origin.z));
            Assert.That(world.State.p5?.impactSurfaces.Count ?? 0, Is.Zero, "Geometry queries cannot spend a surface or roll damage.");
            enemy.aiState = 1; world.State.world.strings["enemyTemplate/" + enemy.id] = "boss";
            world.PopulateP5CommandEvidence(command);
            Assert.That(command.p5.targets.Single().allowInterrupt, Is.False, "P5.1 cannot cancel a committed Boss action by assuming parryable means interruptible.");
            Assert.That(command.p5.targets.Single().hasDisplacement, Is.True, "Protecting commitment does not make legal displacement disappear.");
            world.State.world.strings["enemyTemplate/" + enemy.id] = "guard";
            world.PopulateP5CommandEvidence(command);
            Assert.That(command.p5.targets.Single().allowInterrupt, Is.True);
            yield return null;
        }

        [UnityTest] public IEnumerator ActualRegisteredScoutContactGetsCoverArmorWhileMeleeAndSavesDoNot()
        {
            world = new GameObject("P5 actual scout cover integration").AddComponent<BattleWorld>();
            string path = Path.Combine(Path.GetTempPath(), "ActionCRPG-P5Environment", Guid.NewGuid().ToString("N"), "profile.json");
            world.InitializeDungeonForTests(path, "p5-cover", true, true);
            world.Controls.Map.Disable(); world.ChooseDungeonOption("close"); world.enabled = false;
            // Explicit placement fixture. Entry and the enemy contact still use their real
            // runtime/profile paths, actual R1 cover and unchanged configured attack roll.
            foreach (var actor in world.State.actors) { actor.x = 26; actor.z = 0; }
            var enter = world.ProfileAction(new ProfileCommand { kind = ProfileCommandKind.EnterRoom, roomId = "R1", qualified = true });
            Assert.That(enter.success, Is.True, enter.reason);
            var activate = world.ProfileAction(new ProfileCommand { kind = ProfileCommandKind.ActivateEncounter, qualified = true });
            Assert.That(activate.success, Is.True, activate.reason);
            var mage = world.State.Actor("mage"); mage.x = 26; mage.z = 6;
            world.State.Actor("fighter").x = 21; world.State.Actor("fighter").z = 6;
            world.State.Actor("rogue").x = 31; world.State.Actor("rogue").z = 6;
            foreach (var enemy in world.State.enemies) { enemy.aiState = 2; enemy.aiRemaining = 100; }
            var scout = world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id).id == "scout");
            var guard = world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id).id == "guard");
            scout.x = 26; scout.z = 0; scout.aiState = 1; scout.aiRemaining = .01f;
            scout.attackGroupId = "fixture/actual-scout-cover"; scout.attackTargetId = mage.id; scout.lockedX = mage.x; scout.lockedZ = mage.z;
            world.State.world.strings["enemyPattern/" + scout.id] = "scout-shot";
            world.State.world.strings["enemyPattern/" + guard.id] = "strike";
            guard.x = scout.x; guard.z = scout.z;
            Physics.SyncTransforms();
            Assert.That(world.DungeonConfig.enemies.Single(e => e.id == "scout").patterns.Single(p => p.id == "scout-shot").parryable, Is.True,
                "Actual parryable projectile must still receive the ranged cover rule.");
            var saveEvidence = new CombatCommand { kind = CommandKind.IncomingBatch, actorId = scout.id,
                incomingContacts = new List<IncomingContact> { new IncomingContact { targetId = mage.id, savingThrow = true } } };
            world.PopulateP5CommandEvidence(saveEvidence);
            Assert.That(saveEvidence.p5.targets.Single().coverAcBonus, Is.Zero);
            var meleeEvidence = new CombatCommand { kind = CommandKind.IncomingAttack, actorId = guard.id, targetId = mage.id };
            world.PopulateP5CommandEvidence(meleeEvidence);
            Assert.That(meleeEvidence.p5.targets.Single().coverAcBonus, Is.Zero);
            world.AdvanceDungeonEnemies(.1f);
            var actual = world.State.incomingEvents.Single(e => e.rootId == "fixture/actual-scout-cover" && e.defenderId == "mage");
            Assert.That(actual.rawFaces.Count, Is.GreaterThan(0), "The actual configured attack must roll; the fixture does not inject a result.");
            Assert.That(actual.effectiveArmorClass, Is.EqualTo(actual.baseArmorClass + 2),
                "The real IncomingBatch transaction must consume cover, not merely its visual/geometry preview.");
            yield return null;
        }

        [UnityTest] public IEnumerator ThunderEvidenceIncludesActualFriendlyAreaAndPrimaryOnlyExtraPushWhileFreezeDoesNotMoveGeometry()
        {
            CreateWorld(); var mage = world.State.Actor("mage"); var fighter = world.State.Actor("fighter"); var enemy = world.State.enemies[0];
            mage.x = origin.x; mage.z = origin.z; fighter.x = origin.x + .5f; fighter.z = origin.z + 1; enemy.x = origin.x; enemy.z = origin.z + 2;
            world.State.rolls.Add(new RollTicket { id = "cone", actorId = mage.id, targetId = enemy.id, actionId = "control", spec = new ActionSpec { range = 3 },
                p5 = new P5ActionContext { targetId = enemy.id, directionZ = 1, pushDistance = 3 } });
            world.State.queue.Add(new QueuedAction { id = "cone-queue", ticketId = "cone", actorId = mage.id, targetId = enemy.id });
            var command = new CombatCommand { kind = CommandKind.ResolveQueued, queueId = "cone-queue" };
            world.PopulateP5CommandEvidence(command);
            var friend = command.p5.targets.Single(e => e.targetId == fighter.id); var target = command.p5.targets.Single(e => e.targetId == enemy.id);
            Assert.That(friend.inArea && friend.lineOfSight, Is.True); Assert.That(friend.actualDistance, Is.EqualTo(2).Within(.001f));
            Assert.That(target.actualDistance, Is.EqualTo(3).Within(.001f));
            Assert.That(command.p5.targets.Any(e => e.targetId == mage.id), Is.False);
            float sim = world.State.simulationSeconds; Vector3 old = new Vector3(enemy.x, 0, enemy.z);
            world.State.mode = CombatMode.RollFreeze;
            yield return null; yield return null;
            Assert.That(world.State.simulationSeconds, Is.EqualTo(sim));
            Assert.That(new Vector3(enemy.x, 0, enemy.z), Is.EqualTo(old));
        }
    }
}
