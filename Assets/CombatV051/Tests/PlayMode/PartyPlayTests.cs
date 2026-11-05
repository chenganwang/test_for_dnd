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
    /// <summary>Real BattleWorld Update, actors, wall sweeps and saved DTOs; no synthetic AI-only model.</summary>
    public sealed class PartyPlayTests
    {
        BattleWorld world;
        string directory, file;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "ActionCrpgV051-PartyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "profile.json");
            world = new GameObject("Actual P1 party test world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file);
            world.Controls.Map.Disable(); // These tests isolate autonomous Update; real player input has separate coverage.
            foreach (var enemy in world.State.enemies) { enemy.aiRemaining = 100; enemy.hp = enemy.maxHp = 500; }
            yield return null;
            Assert.That(world.State.actors.Count, Is.EqualTo(3));
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ActionCrpgV051-PartyTests")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) && Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
            Time.timeScale = 1;
        }

        ActorState Fighter => world.State.Actor("fighter");
        ActorState Rogue => world.State.Actor("rogue");
        ActorState Mage => world.State.Actor("mage");
        ActorState Enemy => world.State.enemies[0];

        static void Position(ActorState actor, Vector3 point)
        { actor.x = point.x; actor.z = point.z; actor.heightY = point.y; }

        void Place(Vector3 fighter, Vector3 rogue, Vector3 mage, Vector3 enemy)
        {
            Position(Fighter, fighter); Position(Rogue, rogue); Position(Mage, mage); Position(Enemy, enemy);
            foreach (var actor in world.State.actors)
            {
                actor.majorRemaining = 0; actor.guardRemaining = 0; actor.dodgeRemaining = 0;
                actor.aimPreparing = false; actor.aimRemaining = 0; actor.windupRemaining = 0;
            }
            Enemy.aiState = 0; Enemy.aiRemaining = 100; Enemy.hp = Enemy.maxHp = 500;
            Physics.SyncTransforms();
        }

        string PreciousResources() => CombatJson.Write(world.State.actors.OrderBy(a => a.id).Select(a => new
        { a.id, a.inspiration, a.superiority, a.spellSlots, a.items, a.heldSpellSlots, a.heldItems, a.heldSuperiority }).ToArray());

        static string SimulationSnapshot(CombatState state)
        {
            var copy = state.Clone(); copy.realCombatSeconds = 0;
            return CombatJson.Write(copy);
        }

        string PartyIntents() => CombatJson.Write(world.State.actors.OrderBy(a => a.id).Select(a => new
        { a.id, a.aiState, a.aiTargetId, a.aiMoveX, a.aiMoveZ, a.aiDecisionRemaining, a.aiPolicyId, a.x, a.z }).ToArray());

        [UnityTest]
        public IEnumerator FollowersKeepDistinctMidAndBackPositionsAndFollowTheActiveMember()
        {
            Place(Vector3.zero, new Vector3(-6, 0, -3), new Vector3(-6, 0, -6), new Vector3(10, 0, 8));
            Vector3 rogueStart = BattleWorld.Position(Rogue), mageStart = BattleWorld.Position(Mage);
            ulong draws = world.State.random.backgroundCombat.drawCount;
            float until = Time.realtimeSinceStartup + 4;
            while ((BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue), Vector3.zero) > 3 ||
                BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), Vector3.zero) > 3) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue), rogueStart), Is.GreaterThan(1));
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), mageStart), Is.GreaterThan(1));
            Assert.That(BattleWorld.Position(Fighter), Is.EqualTo(Vector3.zero), "AI cannot drive the active member.");
            Vector3 forward = world.Rig.ScreenMove(Vector2.up);
            Assert.That(Vector3.Dot(BattleWorld.Position(Rogue), forward), Is.GreaterThan(Vector3.Dot(BattleWorld.Position(Mage), forward)),
                "Mid and Back must have different depths.");
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue), BattleWorld.Position(Mage)), Is.GreaterThan(.7f));
            Assert.That(Rogue.aiPolicyId, Is.EqualTo("conservative-v1"));
            Assert.That(Mage.aiState, Is.EqualTo(0));
            Vector3 mageBeforeMove = BattleWorld.Position(Mage);
            Position(Fighter, new Vector3(2, 0, 0));
            until = Time.realtimeSinceStartup + 2;
            while (BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), mageBeforeMove) < 1 && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), mageBeforeMove), Is.GreaterThan(.9f));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(draws));
        }

        [UnityTest]
        public IEnumerator SwitchingToRogueLeavesFighterInFrontAndMageAtItsCantripRange()
        {
            Place(new Vector3(-2, 0, -1), Vector3.zero, new Vector3(-3, 0, -2), new Vector3(6, 0, 0));
            Assert.That(world.SwitchActive("rogue").success, Is.True);
            Fighter.majorRemaining = 20; Mage.majorRemaining = 20; // Isolate formation from hit effects.
            float until = Time.realtimeSinceStartup + 3;
            while ((Fighter.x < 3 || Mage.x < -.2f) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(world.State.activeActorId, Is.EqualTo("rogue"));
            Assert.That(BattleWorld.Position(Rogue), Is.EqualTo(Vector3.zero));
            Assert.That(Fighter.x, Is.GreaterThan(Mage.x + 1), "Front must occupy a different band from Back.");
            Assert.That(Fighter.aiTargetId, Is.EqualTo(Enemy.id));
            Assert.That(Mage.aiTargetId, Is.EqualTo(Enemy.id));
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), BattleWorld.Position(Enemy)),
                Is.GreaterThan(world.Session.Config.BasicFor(Fighter).range));
        }

        [UnityTest]
        public IEnumerator RogueBasicAttackAndMageFreeCantripUseOnlyTheirMajorAndBackgroundDice()
        {
            Place(Vector3.zero, new Vector3(1.2f, 0, 1), new Vector3(-3, 0, 0), new Vector3(1.8f, 0, 0));
            string resources = PreciousResources(); int charges = world.State.charges;
            ulong background = world.State.random.backgroundCombat.drawCount;
            ulong visible = world.State.random.visibleTactical.drawCount, classDice = world.State.random.classResource.drawCount;
            ulong eventDice = world.State.random.eventDice.drawCount;
            float until = Time.realtimeSinceStartup + 2;
            while ((Rogue.majorRemaining <= 0 || Mage.majorRemaining <= 0) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(Rogue.majorRemaining, Is.GreaterThan(0));
            Assert.That(Mage.majorRemaining, Is.GreaterThan(0));
            Assert.That(Fighter.majorRemaining, Is.Zero);
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.GreaterThanOrEqualTo(background + 2));
            Assert.That(PreciousResources(), Is.EqualTo(resources));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visible));
            Assert.That(world.State.random.classResource.drawCount, Is.EqualTo(classDice));
            Assert.That(world.State.random.eventDice.drawCount, Is.EqualTo(eventDice));
            Assert.That(world.State.pendingRoll, Is.Null);
            Assert.That(world.State.queue, Is.Empty);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Mage), BattleWorld.Position(Enemy)),
                Is.GreaterThan(world.Session.Config.BasicFor(Fighter).range), "Cantrip must genuinely work outside melee range.");
        }

        [UnityTest]
        public IEnumerator BothAiMembersLeaveTheActualCircleTelegraphBeforeItResolves()
        {
            Place(new Vector3(-4, 0, -4), Vector3.zero, new Vector3(.2f, 0, .7f), new Vector3(0, 0, 4));
            foreach (var actor in world.State.actors)
            {
                // Setup runs a real Update and may already have started a free Cantrip.
                // This movement-only fixture must exclude that previously committed contact as well as new attacks.
                actor.basicAttackIntent = null;
                actor.windupRemaining = 0;
                actor.majorRemaining = 20;
            }
            Enemy.attackSequence = 3; Enemy.attackGroupId = "party-test/circle"; Enemy.attackTargetId = Rogue.id;
            Enemy.lockedX = 0; Enemy.lockedZ = 0; Enemy.aiState = 1; Enemy.aiRemaining = 1.4f;
            AreaShape circle = world.EnemyShape(Enemy);
            int rogueHp = Rogue.hp, mageHp = Mage.hp;
            ulong background = world.State.random.backgroundCombat.drawCount;
            string resources = PreciousResources();
            Assert.That(BattleGeometry.Contains(circle, BattleWorld.Position(Rogue)), Is.True);
            Assert.That(BattleGeometry.Contains(circle, BattleWorld.Position(Mage)), Is.True);
            yield return null;
            Assert.That(Rogue.aiState, Is.EqualTo(3));
            Assert.That(Mage.aiState, Is.EqualTo(3));
            Assert.That(world.GetComponentsInChildren<AreaPreview>().Any(p => p.Shape.Kind == AreaKind.Circle &&
                Mathf.Abs(p.Shape.Radius - circle.Radius) < .001f), Is.True, "Enemy preview must really be present.");
            float until = Time.realtimeSinceStartup + 3;
            while (Enemy.aiState == 1 && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(Enemy.aiState, Is.EqualTo(2), "The real enemy Update must resolve its telegraph.");
            Assert.That(BattleGeometry.Contains(circle, BattleWorld.Position(Rogue)), Is.False);
            Assert.That(BattleGeometry.Contains(circle, BattleWorld.Position(Mage)), Is.False);
            Assert.That(Rogue.hp, Is.EqualTo(rogueHp)); Assert.That(Mage.hp, Is.EqualTo(mageHp));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(background), "Running outside the actual AoE must avoid the Save entirely.");
            Assert.That(PreciousResources(), Is.EqualTo(resources));
        }

        [UnityTest]
        public IEnumerator QueuedNonActiveFighterKeepsItsActionAndAiCannotMoveOrSpendIt()
        {
            Place(Vector3.zero, new Vector3(-3, 0, -1), new Vector3(-4, 0, -3), new Vector3(3, 0, 0));
            Assert.That(world.SwitchActive("rogue").success, Is.True);
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(world.SelectCommandActor("fighter").success, Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll, actor: "fighter", target: Enemy.id, action: "attack").success, Is.True, world.Message);
            world.AdvanceRollAnimation(2); // Explicit animation completion; AI ownership is this test's subject.
            var commit = world.Command(CommandKind.Commit);
            Assert.That(commit.success, Is.True, world.Message);
            Mage.majorRemaining = 20;
            Vector3 before = BattleWorld.Position(Fighter); string owner = Fighter.heldBy;
            int charges = world.State.charges;
            for (int i = 0; i < 6; i++) yield return null;
            Assert.That(BattleWorld.Position(Fighter), Is.EqualTo(before));
            Assert.That(Fighter.heldBy, Is.EqualTo(owner));
            Assert.That(Fighter.majorRemaining, Is.Zero);
            Assert.That(Fighter.aiState, Is.EqualTo(4));
            Assert.That(world.State.queue.Single(q => q.id == commit.queueId).stage, Is.EqualTo("Queued"));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.Command(CommandKind.CancelQueued, queue: commit.queueId).success, Is.True);
        }

        [UnityTest]
        public IEnumerator RollFreezeStopsEveryPartyIntentPositionAndResourceEvenWithPositiveManualDelta()
        {
            Place(Vector3.zero, new Vector3(-4, 0, -2), new Vector3(-5, 0, -3), new Vector3(3, 0, 0));
            world.Presentation.rollAnimationSeconds = 10;
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(world.SelectCommandActor("fighter").success, Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll, actor: "fighter", target: Enemy.id, action: "attack").success, Is.True, world.Message);
            string state = SimulationSnapshot(world.State), intents = PartyIntents();
            Vector3 camera = world.BattleCamera.transform.position;
            world.AdvanceParty(1); // Defensive public API guard must also refuse a positive dt while frozen.
            for (int i = 0; i < 5; i++) yield return null;
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(SimulationSnapshot(world.State), Is.EqualTo(state));
            Assert.That(PartyIntents(), Is.EqualTo(intents));
            Assert.That(world.BattleCamera.transform.position, Is.EqualTo(camera));
            Assert.That(Time.timeScale, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PausedPartyIntentsAndPreciousResourcesSurviveAnActualFileReload()
        {
            Place(Vector3.zero, new Vector3(-4, 0, -2), new Vector3(-5, 0, -3), new Vector3(3, 0, 0));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(world.Command(CommandKind.Pause).success, Is.True);
            world.Suspend();
            string intents = PartyIntents(), resources = PreciousResources();
            int charges = world.State.charges;
            var disk = CombatSession.Load(file, world.Session.Config, "p1-party").State;
            Assert.That(disk.actors.Count, Is.EqualTo(3));
            Assert.That(disk.actors.Single(a => a.id == "mage").aiPolicyId, Is.EqualTo("conservative-v1"));
            world.ContinueSaved();
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.CombatPaused));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(PartyIntents(), Is.EqualTo(intents));
            Assert.That(PreciousResources(), Is.EqualTo(resources));
            Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.actors.Count, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PartyFollowingUsesTheRealWallColliderAndNeverTeleportsThroughIt()
        {
            Place(new Vector3(3, 0, 0), new Vector3(-4, 0, 0), new Vector3(-4, 0, -1), new Vector3(10, 0, 8));
            var wall = BattleVisual.Box("Party test wall", new Vector3(0, 1.2f, 0), new Vector3(.4f, 2.4f, 14),
                Color.gray, world.transform, 8, true);
            Physics.SyncTransforms();
            Vector3 before = BattleWorld.Position(Rogue);
            float until = Time.realtimeSinceStartup + 1;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                Assert.That(Rogue.x, Is.LessThan(-.54f), "The follower cannot cross a solid, sufficiently long wall.");
                Assert.That(Mage.x, Is.LessThan(-.54f));
            }
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue), before), Is.GreaterThan(.1f));
            Assert.That(wall.GetComponent<Collider>().enabled, Is.True);
        }
    }
}
