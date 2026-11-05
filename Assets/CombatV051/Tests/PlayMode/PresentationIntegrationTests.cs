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
    public sealed class PresentationIntegrationTests
    {
        BattleWorld world;
        [UnitySetUp] public IEnumerator Setup()
        {
            var path = Path.Combine(Path.GetTempPath(), "V051-PresentationIntegration", Guid.NewGuid().ToString("N"), "party.json");
            world = new GameObject("Real P1 configuration and control scene").AddComponent<BattleWorld>();
            world.InitializePartyForTests(path);
            world.Controls.Map.Disable();
            world.State.enemies[0].aiRemaining = 100;
            foreach (var actor in world.State.actors) if (actor.id != "fighter") actor.majorRemaining = 100;
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            Time.timeScale = 1;
        }
        [UnityTest] public IEnumerator ActualResourceConfigReplacesDefaultsAndDefinesAllRoleParameters()
        {
            var declared = CombatJson.Read<CombatConfig>(Resources.Load<TextAsset>("CombatConfig").text);
            var live = world.Session.Config;
            Assert.That(live.actions.Count, Is.EqualTo(declared.actions.Count), "JSON lists must replace constructor defaults, never silently append duplicate actions.");
            Assert.That(live.actions.Select(a => a.id).Distinct().Count(), Is.EqualTo(live.actions.Count));
            Assert.That(live.actorTemplates.Count, Is.EqualTo(declared.actorTemplates.Count));
            Assert.That(live.Action("attack").damage, Is.EqualTo(declared.Action("attack").damage));
            Assert.That(live.Template(ActorRole.Mage).spellSlots, Is.EqualTo(declared.Template(ActorRole.Mage).spellSlots));
            Assert.That(world.State.Actor("mage").maxHp, Is.EqualTo(declared.Template(ActorRole.Mage).hp));
            Assert.That(world.State.enemies[0].maxHp, Is.EqualTo(declared.partyEnemyHealth));
            yield return null;
        }
        [UnityTest] public IEnumerator MageControlPaysAtStartupAndActuallyStopsEnemyMovementUntilExpiration()
        {
            var target = world.State.enemies[0]; target.x = 1; target.z = 0; target.hp = target.maxHp = 500;
            target.wisSaveBonus = -100; // Explicit guaranteed-failed WIS fixture; natural face is still really rolled.
            var mage = world.State.Actor("mage"); mage.x = -2; mage.z = -1; mage.majorRemaining = 0;
            world.State.Actor("rogue").majorRemaining = 100;
            var slots = mage.spellSlots;
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            Assert.That(world.SelectCommandActor("mage").success, Is.True);
            Assert.That(world.SelectedAction, Is.EqualTo("control"));
            Assert.That(world.ConfirmCurrentDecision().success, Is.True, world.Message);
            Assert.That(world.State.Actor("mage").spellSlots, Is.EqualTo(slots), "Confirm only holds long-term costs.");
            float deadline = Time.realtimeSinceStartup + 4;
            while (world.State.mode == CombatMode.RollFreeze && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.pendingRoll.hit, Is.True);
            Assert.That(world.ConfirmCurrentDecision().success, Is.True);
            Assert.That(world.State.Actor("mage").spellSlots, Is.EqualTo(slots));
            Assert.That(world.Command(CommandKind.ExitFocus).success, Is.True);
            deadline = Time.realtimeSinceStartup + 4;
            while (!world.State.enemies[0].statuses.Any(s => s.id == "restrained") && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(world.State.enemies[0].statuses.Any(s => s.id == "restrained" && s.remaining > 0), Is.True);
            Assert.That(world.State.Actor("mage").spellSlots, Is.EqualTo(slots - 1));
            Vector3 before = BattleWorld.Position(world.State.enemies[0]);
            world.MoveActor(world.State.enemies[0], Vector3.right);
            Assert.That(BattleWorld.Position(world.State.enemies[0]), Is.EqualTo(before));
            deadline = Time.realtimeSinceStartup + 4;
            while (world.State.enemies[0].statuses.Any(s => s.id == "restrained" && s.remaining > 0) && Time.realtimeSinceStartup < deadline) yield return null;
            before = BattleWorld.Position(world.State.enemies[0]);
            world.MoveActor(world.State.enemies[0], Vector3.right * .4f);
            Assert.That(BattleWorld.Position(world.State.enemies[0]).x, Is.GreaterThan(before.x + .3f));
        }
    }
}
