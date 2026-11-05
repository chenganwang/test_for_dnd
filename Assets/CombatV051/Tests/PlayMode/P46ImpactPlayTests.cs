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
    /// <summary>
    /// Engine integration of published combat facts and cosmetic feedback. Positions, initial
    /// health and PCG streams are explicit fixtures; every asserted hit/break/death is produced
    /// by a real combat command. These are automated fixtures, not human playthrough evidence.
    /// </summary>
    public sealed class P46ImpactPlayTests
    {
        BattleWorld world;
        string directory, file;
        ActorState Fighter => world.State.Actor("fighter");
        ActorState Rogue => world.State.Actor("rogue");
        ActorState Mage => world.State.Actor("mage");
        ActorState Enemy => world.State.enemies[0];

        [UnitySetUp] public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "ActionCrpg-P46Impact", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory, "party.json");
            world = new GameObject("P46 actual impact world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(file); world.IgnoreApplicationFocus = true;
            world.enabled = false; world.Controls.Map.Disable(); world.QuickDice = false;
            PrepareParty();
            world.ConfigureImpactSettings(FeedbackIntensity.Normal, FeedbackIntensity.Normal);
            Assert.That(world.ImpactFeedback, Is.Not.Null);
            world.ImpactFeedback.ResetObservation();
            yield return null;
        }

        [UnityTearDown] public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null; Time.timeScale = 1;
            string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ActionCrpg-P46Impact")) + Path.DirectorySeparatorChar;
            if (directory != null && Path.GetFullPath(directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        static void Place(ActorState actor, Vector3 position)
        { actor.x = position.x; actor.z = position.z; actor.heightY = position.y; }

        void PrepareParty()
        {
            Place(Fighter, Vector3.zero); Place(Rogue, new Vector3(.7f, 0, -1));
            Place(Mage, new Vector3(-.7f, 0, -1)); Place(Enemy, new Vector3(1.5f, 0, 0));
            foreach (var actorId in world.State.actors.Select(a => a.id).ToArray())
            {
                // Successful commands clone/publish the entire state. Rebind every iteration,
                // rather than mutating actors from the list that existed before the first Hold.
                var actor = world.State.Actor(actorId);
                actor.majorRemaining = 100; // Suppress unrelated autonomous basic attacks.
                actor.guardRemaining = actor.dodgeRemaining = actor.windupRemaining = actor.staggerRemaining = 0;
                Success(world.ExecuteWorldCommand(new CombatCommand { id = "impact-hold/" + actor.id,
                    kind = CommandKind.IssueBasicOrder, actorId = actor.id, actionId = "Hold" }));
            }
            Assert.That(world.State.actors.All(a => a.majorRemaining == 100 && a.basicOrder?.kind == BasicOrderKind.Hold),
                Is.True, "The published party, not discarded pre-command DTOs, must be isolated from autonomous attacks.");
            foreach (var enemy in world.State.enemies) { enemy.aiState = 2; enemy.aiRemaining = 1000; }
            world.SelectedActor = "fighter"; world.SelectedAction = "attack"; world.SelectedTarget = Enemy.id;
            Physics.SyncTransforms();
        }

        static void Success(CombatResult result) { Assert.That(result.success, Is.True, result.reason); }
        static RngState FaceStream(params int[] faces)
        {
            for (ulong seed = 1; seed < 100000; seed++)
            {
                var initial = RandomStreams.Create(seed).backgroundCombat;
                var probe = CombatJson.Clone(initial);
                if (faces.All(face => RandomStreams.D20(probe) == face)) return initial;
            }
            throw new InvalidOperationException("No deterministic real PCG fixture found.");
        }

        CombatResult Contact(string root, int damage, params string[] defenders)
        {
            world.State.random.backgroundCombat = FaceStream(defenders.Select(_ => 12).ToArray());
            Enemy.attackBonus = defenders.Select(id => world.State.Actor(id).armorClass).Max() + 1 - 12;
            // Contact geometry is an explicit fixture. The real domain draws each background d20,
            // computes legality, creates the group, defers HP and finally applies each item.
            var result = world.ExecuteWorldCommand(new CombatCommand { id = "impact-contact/" + root,
                kind = CommandKind.IncomingBatch, actorId = Enemy.id, rootId = root,
                qualified = true, authority = CommandAuthority.System,
                incomingContacts = defenders.Select(id => new IncomingContact { targetId = id, damage = damage, parryable = true,
                    reactionEligibleActorIds = world.State.actors.Where(a => a.Alive &&
                        world.HasLineOfEffect(BattleWorld.Position(a), BattleWorld.Position(world.State.Actor(id)))).Select(a => a.id).ToList()
                }).ToList() });
            Success(result); return result;
        }

        void NoReactionOpportunity()
        { foreach (var actor in world.State.actors) actor.reactionRemaining = 100; }

        PendingIncoming CriticalReactionContact(string root, int damage, string defender)
        {
            // A genuine natural 20 from the background stream, with an attainable Parry DC.
            world.State.random.backgroundCombat = FaceStream(20); Enemy.attackBonus = 0;
            Success(world.ExecuteWorldCommand(new CombatCommand { id = "impact-critical/" + root,
                kind = CommandKind.IncomingBatch, actorId = Enemy.id, rootId = root,
                qualified = true, authority = CommandAuthority.System,
                incomingContacts = new System.Collections.Generic.List<IncomingContact> { new IncomingContact {
                    targetId = defender, damage = damage, parryable = true,
                    reactionEligibleActorIds = world.State.actors.Where(a => a.Alive &&
                        world.HasLineOfEffect(BattleWorld.Position(a), BattleWorld.Position(world.State.Actor(defender)))).Select(a => a.id).ToList()
                } } }));
            var incoming = world.State.incomingEvents.Single(e => e.rootId == root);
            Assert.That(incoming.critical, Is.True); Assert.That(incoming.rawFaces, Is.EqualTo(new[] {20}));
            Assert.That(incoming.damageApplied, Is.False); Assert.That(world.State.mode, Is.EqualTo(CombatMode.ReactionPrompt));
            return incoming;
        }

        string RollAndAcceptParry(string defender, int natural, bool expectedHit)
        {
            var choice = world.Session.GetReactionChoices().Single(q => q.legal && q.actorId == "fighter" && q.defenderId == defender && q.reactionId == "parry");
            Success(world.SelectReactionChoice(choice)); world.State.random.visibleTactical = FaceStream(natural);
            Success(world.React(CommandKind.ConfirmReactionRoll));
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze)); Assert.That(Time.timeScale, Is.Zero);
            var ticket = world.State.pendingRoll; string id = ticket.id;
            Assert.That(ticket.rawFaces, Is.EqualTo(new[] {natural})); Assert.That(ticket.hit, Is.EqualTo(expectedHit));
            Assert.That(ticket.chargeSpent, Is.Zero); Assert.That(ticket.purpose, Is.EqualTo("Reaction"));
            Assert.That(Fighter.reactionRemaining, Is.EqualTo(world.Session.Config.reactionRecovery));
            world.AdvanceRollAnimation(world.CurrentRollDuration + .01f);
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.PostRollDecision));
            Success(world.React(CommandKind.CommitReaction));
            return id;
        }

        CombatResult ActualBasicHit(string root)
        {
            Fighter.majorRemaining = 0; Fighter.attackBonus = 30;
            world.State.random.backgroundCombat = FaceStream(12);
            var start = world.BeginBasicAttack("fighter", world.SelectedTarget); Success(start);
            string intent = Fighter.basicAttackIntent.id;
            Assert.That(world.State.incomingEvents.Any(e => e.rootId == intent), Is.False);
            world.Session.Tick(world.Session.Config.basicContactSeconds + .01f, true, 0);
            var result = world.ExecuteWorldCommand(new CombatCommand { id = root, kind = CommandKind.ResolveBasicAttack,
                actorId = "fighter", targetId = world.SelectedTarget, orderId = intent, authority = CommandAuthority.System });
            Success(result); return result;
        }

        CombatResult ActualQueuedHit(string action)
        {
            Fighter.majorRemaining = 0; Fighter.attackBonus = 30; Enemy.saveBonus = -100;
            world.State.random.visibleTactical = FaceStream(12); world.SelectedAction = action;
            Success(world.Command(CommandKind.EnterFocus)); Success(world.Command(CommandKind.ConfirmRoll));
            Assert.That(world.State.pendingRoll.hit, Is.True, "The real deterministic die must qualify.");
            Assert.That(world.State.pendingRoll.critical, Is.False);
            world.AdvanceRollAnimation(world.CurrentRollDuration + .01f);
            var committed = world.Command(CommandKind.Commit); Success(committed);
            Success(world.Command(CommandKind.ExitFocus));
            Success(world.Command(CommandKind.StartQueued, queue: committed.queueId));
            var queue = world.State.queue.Single(q => q.id == committed.queueId);
            world.Session.Tick(queue.windupRemaining + .01f, true, 0);
            return world.Command(CommandKind.ResolveQueued, queue: committed.queueId);
        }

        [UnityTest] public IEnumerator BatchWaitsForDeclineThenEmitsEachAppliedContactOnlyOnce()
        {
            int hpFighter = Fighter.hp, hpRogue = Rogue.hp, count = world.ImpactFeedback.EventCount;
            var contact = Contact("impact-batch", 8, "fighter", "rogue");
            Assert.That(world.State.mode, Is.EqualTo(CombatMode.ReactionPrompt));
            Assert.That(world.State.activePendingEvent.incomingIds.Count, Is.EqualTo(2));
            Assert.That(Fighter.hp, Is.EqualTo(hpFighter)); Assert.That(Rogue.hp, Is.EqualTo(hpRogue));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count), "Recorded threats are not yet damage.");
            var decline = new CombatCommand { id = "impact-decline-once", kind = CommandKind.DeclineReaction, reactionEventId = contact.reactionEventId };
            Success(world.ExecuteWorldCommand(decline));
            Assert.That(Fighter.hp, Is.EqualTo(hpFighter - 8)); Assert.That(Rogue.hp, Is.EqualTo(hpRogue - 8));
            Assert.That(world.State.incomingEvents.Where(e => e.rootId == "impact-batch").All(e => e.damageApplied && e.stage == "Applied"), Is.True);
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 2), "The result object describes only one contact; the observer must read all items.");
            string snapshot = CombatJson.Write(world.State);
            var repeated = world.ExecuteWorldCommand(decline); Success(repeated); Assert.That(repeated.duplicate, Is.True);
            world.ImpactFeedback.Observe(); world.ImpactFeedback.Observe();
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 2));
            Assert.That(CombatJson.Write(world.State), Is.EqualTo(snapshot), "Presentation and duplicate receipt cannot reapply damage or consume RNG.");
            yield return null;
        }

        [UnityTest] public IEnumerator OrdinaryNonCriticalContactIsLightWithoutHitStopOrShake()
        {
            int hp = Enemy.hp, count = world.ImpactFeedback.EventCount;
            var hit = ActualBasicHit("ordinary-contact");
            Assert.That(hit.hit && !hit.critical, Is.True); Assert.That(hit.damage, Is.GreaterThan(0));
            Assert.That(Enemy.hp, Is.EqualTo(hp - hit.damage));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Light));
            Assert.That(world.ImpactFeedback.LastStopSeconds, Is.Zero); Assert.That(world.ImpactFeedback.LastShakeStrength, Is.Zero);
            Assert.That(world.ImpactFeedback.Holding, Is.False); Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(world.Feedback.GetComponentsInChildren<LineRenderer>().Length, Is.GreaterThan(0), "Actual contact keeps a rendered impact.");
            yield return null;
        }

        [UnityTest] public IEnumerator RealQueuedDamageHasFeedbackEvenWithoutPendingIncomingAndDoesNotReplay()
        {
            int hp = Enemy.hp, incomingCount = world.State.incomingEvents.Count, count = world.ImpactFeedback.EventCount;
            var hit = ActualQueuedHit("attack"); Success(hit);
            Assert.That(hit.damage, Is.GreaterThan(0)); Assert.That(Enemy.hp, Is.EqualTo(hp - hit.damage));
            Assert.That(world.State.incomingEvents.Count, Is.EqualTo(incomingCount), "Queued tactical damage has its own authoritative fact path.");
            Assert.That(world.State.queue.Single().effectApplied, Is.True); Assert.That(world.State.queue.Single().stage, Is.EqualTo("Resolved"));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Medium));
            string rng = CombatJson.Write(world.State.random); world.ImpactFeedback.Observe(); world.ImpactFeedback.Observe();
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1)); Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            yield return null;
        }

        [UnityTest] public IEnumerator ActualShoveBreaksInitialOnePointGuardAndEmitsOneBreakEdge()
        {
            Enemy.breakValue = 1; int hp = Enemy.hp, count = world.ImpactFeedback.EventCount;
            world.ImpactFeedback.ResetObservation(); var hit = ActualQueuedHit("shove"); Success(hit);
            Assert.That(Enemy.breakValue, Is.Zero); Assert.That(Enemy.staggerRemaining, Is.GreaterThan(0));
            Assert.That(Enemy.hp, Is.EqualTo(hp), "A real shove/Break is not fabricated HP damage.");
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Heavy));
            Assert.That(world.ImpactFeedback.LastEvent, Is.EqualTo("break"));
            int settled = world.ImpactFeedback.EventCount;
            Assert.That(settled, Is.EqualTo(count + 2), "One queue effect plus one actual transition into Break.");
            world.ImpactFeedback.Observe(); world.Session.Tick(.1f, true, 0); world.ImpactFeedback.Observe();
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(settled), "A continuing stagger must not re-trigger a Break.");
            yield return null;
        }

        [UnityTest] public IEnumerator ActualHeavyPlayerDamageHoldsWorldSimulationThenRestoresRealtimeAutomatically()
        {
            NoReactionOpportunity(); int hp = Fighter.hp;
            Contact("impact-heavy", 8, "fighter");
            Assert.That(Fighter.hp, Is.EqualTo(hp - 8)); Assert.That(world.State.mode, Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Heavy));
            Assert.That(world.ImpactFeedback.LastStopSeconds, Is.InRange(.05f, .10f));
            Assert.That(world.ImpactFeedback.LastShakeStrength, Is.GreaterThan(0));
            Assert.That(world.ImpactFeedback.Holding, Is.True, "A real impact must survive command presentation and expose a hold.");
            Assert.That(Time.timeScale, Is.Zero);
            float simulation = world.State.simulationSeconds; string rng = CombatJson.Write(world.State.random);
            world.enabled = true;
            // Update and the cosmetic clock are real components. Assert frozen samples while the hold remains.
            float deadline = Time.realtimeSinceStartup + 1;
            while (world.ImpactFeedback.Holding && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (world.ImpactFeedback.Holding) Assert.That(world.State.simulationSeconds, Is.EqualTo(simulation));
            }
            yield return null; world.enabled = false;
            Assert.That(world.ImpactFeedback.Holding, Is.False); Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(world.State.simulationSeconds, Is.GreaterThan(simulation)); Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            Assert.That(Fighter.hp, Is.EqualTo(hp - 8), "Ending a cosmetic hold must not apply the contact again.");
        }

        [UnityTest] public IEnumerator FocusAndRollFreezeTakePriorityAndNeverDeferAnOldImpactStop()
        {
            NoReactionOpportunity(); Fighter.majorRemaining = 0;
            Success(world.Command(CommandKind.EnterFocus)); Contact("impact-in-focus", 8, "fighter");
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Heavy));
            Assert.That(world.ImpactFeedback.LastStopSeconds, Is.Zero); Assert.That(world.ImpactFeedback.LastShakeStrength, Is.Zero);
            Assert.That(world.ImpactFeedback.Holding, Is.False); Assert.That(Time.timeScale, Is.EqualTo(world.Session.Config.focusScale));
            Success(world.Command(CommandKind.ConfirmRoll)); string snapshot = CombatJson.Write(world.State);
            world.ImpactFeedback.Observe(); world.ImpactFeedback.RefreshVisualClock();
            Assert.That(Time.timeScale, Is.Zero); Assert.That(world.State.mode, Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(CombatJson.Write(world.State), Is.EqualTo(snapshot));
            yield return new WaitForSecondsRealtime(.2f);
            world.AdvanceRollAnimation(world.CurrentRollDuration + .01f); Success(world.Command(CommandKind.Cancel)); Success(world.Command(CommandKind.ExitFocus));
            Assert.That(world.ImpactFeedback.StopRemaining, Is.Zero); Assert.That(world.ImpactFeedback.Holding, Is.False); Assert.That(Time.timeScale, Is.EqualTo(1));
        }

        [TestCase(FeedbackIntensity.Off, 0f)]
        [TestCase(FeedbackIntensity.Low, .45f)]
        [TestCase(FeedbackIntensity.Normal, 1f)]
        public void ImpactIntensityPersistsToAnActualSettingsFileAndScalesOnlyPresentation(FeedbackIntensity setting, float factor)
        {
            string resources = CombatJson.Write(new { world.State.random, world.State.charges, world.State.momentum,
                actors = world.State.actors.Select(a => new { a.hp, a.superiority, a.spellSlots, a.inspiration }).ToArray() });
            world.ConfigureImpactSettings(setting, setting);
            string settings = Path.Combine(directory, "p4-player-settings.json"); Assert.That(File.Exists(settings), Is.True);
            world.Suspend(); world.ContinueSaved();
            Assert.That(world.ShakeSetting, Is.EqualTo(setting)); Assert.That(world.HitStopSetting, Is.EqualTo(setting));
            Assert.That(CombatJson.Write(new { world.State.random, world.State.charges, world.State.momentum,
                actors = world.State.actors.Select(a => new { a.hp, a.superiority, a.spellSlots, a.inspiration }).ToArray() }), Is.EqualTo(resources));
            NoReactionOpportunity(); Contact("impact-setting", 8, "fighter");
            var tuning = world.ImpactFeedback.Config.levels[(int)ImpactLevel.Heavy];
            Assert.That(world.ImpactFeedback.LastStopSeconds, Is.EqualTo(tuning.stopSeconds * factor).Within(.0001f));
            Assert.That(world.ImpactFeedback.LastShakeStrength, Is.EqualTo(tuning.shakeStrength * factor).Within(.0001f));
            Assert.That(Fighter.hp, Is.EqualTo(Fighter.maxHp - 8), "Settings do not soften damage.");
        }

        [UnityTest] public IEnumerator RealFileReloadSeedsAppliedFactsWithoutHistoricalHitsOrAudio()
        {
            ActualBasicHit("impact-before-save"); int hp = Enemy.hp, count = world.ImpactFeedback.EventCount;
            string rng = CombatJson.Write(world.State.random); int audioHits = world.Audio.PlayedCount(AudioCue.EnemyHit);
            world.Suspend(); world.ContinueSaved();
            world.ImpactFeedback.Observe(); world.ImpactFeedback.ResetObservation(); world.ImpactFeedback.Observe();
            Assert.That(Enemy.hp, Is.EqualTo(hp)); Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count)); Assert.That(world.Audio.PlayedCount(AudioCue.EnemyHit), Is.EqualTo(audioHits));
            Assert.That(world.ImpactFeedback.Holding, Is.False); Assert.That(world.ImpactFeedback.BossKillCount, Is.Zero);
            yield return null;
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count)); Assert.That(world.Audio.PlayedCount(AudioCue.EnemyHit), Is.EqualTo(audioHits));
        }

        [UnityTest] public IEnumerator AutomaticReactionDeadlinePublishesTwoContactsAndFeedbackOnce()
        {
            int hpFighter = Fighter.hp, hpRogue = Rogue.hp, count = world.ImpactFeedback.EventCount;
            Contact("impact-timeout", 8, "fighter", "rogue");
            Assert.That(world.State.activePendingEvent, Is.Not.Null); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count));
            world.enabled = true; float deadline = Time.realtimeSinceStartup + 6;
            while (world.State.activePendingEvent != null && Time.realtimeSinceStartup < deadline) yield return null;
            world.enabled = false;
            Assert.That(world.State.activePendingEvent, Is.Null, "Actual UI-clock Update must settle the unpaid timeout.");
            Assert.That(Fighter.hp, Is.EqualTo(hpFighter - 8)); Assert.That(Rogue.hp, Is.EqualTo(hpRogue - 8));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 2), string.Join("\n", world.ImpactFeedback.RecentEvents));
            world.ImpactFeedback.Observe(); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 2));
        }

        [UnityTest] public IEnumerator SuccessfulAllyParryKeepsDefenseFeedbackWhenACriticalStillDealsReducedDamage()
        {
            int hp = Rogue.hp, charges = world.State.charges, superiority = Fighter.superiority;
            int count = world.ImpactFeedback.EventCount, parryAudio = world.Audio.PlayedCount(AudioCue.Parry), criticalAudio = world.Audio.PlayedCount(AudioCue.Critical);
            var incoming = CriticalReactionContact("impact-protect-critical", 3, "rogue"); string incomingId = incoming.id;
            Assert.That(incoming.baseDamage, Is.LessThan(Rogue.maxHp * world.Session.Config.importantReactionDamageFraction));
            Assert.That(incoming.prospectiveDamage, Is.EqualTo(6), "Critical threat severity uses actual doubled damage.");
            Assert.That(world.State.activePendingEvent.important, Is.True); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count));
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            string ticket = RollAndAcceptParry("rogue", 20, true);
            incoming = world.State.incomingEvents.Single(e => e.id == incomingId);
            Assert.That(incoming.damageApplied, Is.True); Assert.That(incoming.reason, Is.EqualTo("reaction_parry"));
            Assert.That(incoming.finalDamage, Is.EqualTo(3)); Assert.That(Rogue.hp, Is.EqualTo(hp - 3));
            Assert.That(world.State.charges, Is.EqualTo(charges)); Assert.That(Fighter.superiority, Is.EqualTo(superiority - 1));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(world.State.rolls.Single(t => t.id == ticket).stage, Is.EqualTo("Resolved"));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Medium)); Assert.That(world.ImpactFeedback.LastEvent, Is.EqualTo("reaction"));
            Assert.That(world.Audio.PlayedCount(AudioCue.Parry), Is.EqualTo(parryAudio + 1));
            int diceCritical=world.Audio.DiceSoundTimeline.Count(e=>e.ticket==ticket&&e.cue=="Critical");
            Assert.That(diceCritical,Is.EqualTo(1),"P47 natural 20 parry has its dedicated roll outcome sound.");
            Assert.That(world.Audio.PlayedCount(AudioCue.Critical), Is.EqualTo(criticalAudio+diceCritical), "Only the paid natural 20 may sound; successful defense must not add an incoming-critical cue.");
            world.ImpactFeedback.Observe(); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            yield return null;
        }

        [UnityTest] public IEnumerator ShieldStoppingALethalSmallHitUsesDefenseFeedbackWithoutAnyReactionDice()
        {
            Mage.hp = 2; world.ImpactFeedback.ResetObservation();
            int charges = world.State.charges, slots = Mage.spellSlots, count = world.ImpactFeedback.EventCount, parryAudio = world.Audio.PlayedCount(AudioCue.Parry);
            ulong visibleDraws = world.State.random.visibleTactical.drawCount;
            Contact("impact-lethal-shield", 3, "mage");
            var incoming = world.State.incomingEvents.Single(e => e.rootId == "impact-lethal-shield"); string id = incoming.id;
            Assert.That(incoming.baseDamage, Is.LessThan(Mage.maxHp * world.Session.Config.importantReactionDamageFraction));
            Assert.That(world.State.activePendingEvent.lethal, Is.True); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count));
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            var choice = world.Session.GetReactionChoices().Single(q => q.legal && q.actorId == "mage" && q.defenderId == "mage" && q.reactionId == "shield");
            Success(world.SelectReactionChoice(choice)); Success(world.React(CommandKind.CommitReaction));
            incoming = world.State.incomingEvents.Single(e => e.id == id);
            Assert.That(incoming.damageApplied, Is.True); Assert.That(incoming.finalDamage, Is.Zero); Assert.That(incoming.rawFaces, Is.EqualTo(new[] {12}));
            Assert.That(Mage.hp, Is.EqualTo(2)); Assert.That(Mage.spellSlots, Is.EqualTo(slots - 1));
            Assert.That(Mage.reactionRemaining, Is.EqualTo(world.Session.Config.reactionRecovery)); Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(world.State.rolls, Is.Empty); Assert.That(world.State.random.visibleTactical.drawCount, Is.EqualTo(visibleDraws));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Medium)); Assert.That(world.ImpactFeedback.LastEvent, Is.EqualTo("reaction"));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1)); Assert.That(world.Audio.PlayedCount(AudioCue.Parry), Is.EqualTo(parryAudio + 1));
            yield return null;
        }

        [UnityTest] public IEnumerator AcceptedFailedParryStillPaysItsCostAndKeepsTheActualCriticalHitFeedback()
        {
            int hp = Rogue.hp, charges = world.State.charges, superiority = Fighter.superiority;
            int count = world.ImpactFeedback.EventCount, parryAudio = world.Audio.PlayedCount(AudioCue.Parry), criticalAudio = world.Audio.PlayedCount(AudioCue.Critical);
            var incoming = CriticalReactionContact("impact-failed-parry", 3, "rogue"); string incomingId = incoming.id;
            ulong backgroundDraws = world.State.random.backgroundCombat.drawCount;
            string ticket = RollAndAcceptParry("rogue", 1, false);
            incoming = world.State.incomingEvents.Single(e => e.id == incomingId);
            Assert.That(incoming.reason, Is.EqualTo("reaction_parry"), "The reason alone is not proof that a defense succeeded.");
            Assert.That(incoming.finalDamage, Is.EqualTo(6)); Assert.That(Rogue.hp, Is.EqualTo(hp - 6));
            Assert.That(Fighter.superiority, Is.EqualTo(superiority - 1)); Assert.That(world.State.charges, Is.EqualTo(charges));
            Assert.That(Fighter.reactionRemaining, Is.EqualTo(world.Session.Config.reactionRecovery));
            Assert.That(world.State.rolls.Single(t => t.id == ticket).hit, Is.False); Assert.That(world.State.rolls.Single(t => t.id == ticket).stage, Is.EqualTo("Resolved"));
            Assert.That(world.State.random.backgroundCombat.drawCount, Is.EqualTo(backgroundDraws));
            Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1)); Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Heavy));
            Assert.That(world.ImpactFeedback.LastEvent, Is.EqualTo("critical")); Assert.That(world.Audio.PlayedCount(AudioCue.Critical), Is.EqualTo(criticalAudio + 1));
            Assert.That(world.Audio.PlayedCount(AudioCue.Parry), Is.EqualTo(parryAudio));
            yield return null;
        }

        [UnityTest] public IEnumerator CriticalContactStopsLaterActorsAndEnemyStageWithinTheSameActualUpdate()
        {
            NoReactionOpportunity(); Fighter.majorRemaining = Rogue.majorRemaining = 0;
            Fighter.attackBonus = Rogue.attackBonus = 30;
            world.State.random.backgroundCombat = FaceStream(20, 12);
            Success(world.BeginBasicAttack("fighter", Enemy.id));
            Success(world.BeginBasicAttack("rogue", Enemy.id, CommandAuthority.AllyAI));
            string first = Fighter.basicAttackIntent.id, second = Rogue.basicAttackIntent.id;
            world.Session.Tick(world.Session.Config.basicContactSeconds + .01f, true, 0);
            Enemy.aiRemaining = 20;
            Assert.That(Fighter.basicAttackIntent.remaining, Is.Zero); Assert.That(Rogue.basicAttackIntent.remaining, Is.Zero);
            // Run exactly one actual component Update. Automatic Update stays disabled so a
            // later frame cannot conceal an unwanted same-frame second contact or enemy tick.
            world.SendMessage("Update", SendMessageOptions.RequireReceiver);
            Assert.That(Fighter.basicAttackIntent.resolved, Is.True);
            Assert.That(Fighter.majorRemaining, Is.EqualTo(world.Session.Config.majorRecovery));
            Assert.That(Fighter.windupRemaining, Is.Zero);
            Assert.That(world.State.incomingEvents.Count(e => e.rootId == first && e.damageApplied && e.critical), Is.EqualTo(1));
            Assert.That(Rogue.basicAttackIntent.resolved, Is.False, "Later ready contacts must wait for the cosmetic hold to finish.");
            Assert.That(world.State.incomingEvents.Any(e => e.rootId == second), Is.False);
            Assert.That(Enemy.aiRemaining, Is.EqualTo(20), "The later enemy stage must not advance after an earlier contact began the hold.");
            Assert.That(world.ImpactFeedback.Holding, Is.True);
            string allies = CombatJson.Write(world.State.actors);
            world.AdvanceParty(.1f); world.AdvanceBasicOrders(.1f);
            Assert.That(CombatJson.Write(world.State.actors), Is.EqualTo(allies), "Direct public executors honor the same hold boundary.");
            yield return new WaitForSecondsRealtime(.14f);
            world.AdvanceBasicOrders(.01f);
            Assert.That(Rogue.basicAttackIntent.resolved, Is.True);
            Assert.That(world.State.incomingEvents.Count(e => e.rootId == first), Is.EqualTo(1));
            Assert.That(world.State.incomingEvents.Count(e => e.rootId == second), Is.EqualTo(1));
        }

        [UnityTest] public IEnumerator ArenaAoEFinishesEveryCommittedVictimAndRecoveryBeforeDeferringTheNextEnemy()
        {
            NoReactionOpportunity(); Place(Fighter, Vector3.zero); Place(Rogue, new Vector3(0, 0, .2f)); Place(Mage, new Vector3(-6, 0, -6));
            var second = CombatJson.Clone(Enemy); second.id = "impact-later-arena-enemy"; second.aiState = 2; second.aiRemaining = .25f;
            Place(second, new Vector3(5, 0, 4)); world.State.enemies.Add(second);
            string source = Enemy.id, root = world.State.encounterId + "/impact-arena-group";
            Enemy.attackBonus = 30; Enemy.aiState = 1; Enemy.aiRemaining = 0; Enemy.attackSequence = 1;
            Enemy.attackGroupId = root; Enemy.attackTargetId = "fighter"; Enemy.lockedX = Fighter.x; Enemy.lockedZ = Fighter.z;
            world.State.random.backgroundCombat = FaceStream(20, 12); Physics.SyncTransforms(); world.ImpactFeedback.ResetObservation();
            int hpFighter = Fighter.hp, hpRogue = Rogue.hp;
            Assert.That(BattleGeometry.Contains(world.EnemyShape(Enemy), BattleWorld.Position(Fighter)), Is.True);
            Assert.That(BattleGeometry.Contains(world.EnemyShape(Enemy), BattleWorld.Position(Rogue)), Is.True);
            world.AdvanceEnemies(.1f);
            var applied = world.State.incomingEvents.Where(e => e.rootId == root).ToArray();
            Assert.That(applied.Length, Is.EqualTo(2)); Assert.That(applied.All(e => e.damageApplied), Is.True, "Do not truncate a committed AoE when its first item starts a hold.");
            Assert.That(Fighter.hp, Is.EqualTo(hpFighter - world.Presentation.enemyDamage * 2));
            Assert.That(Rogue.hp, Is.EqualTo(hpRogue - world.Presentation.enemyDamage));
            Assert.That(world.State.Actor(source).aiState, Is.EqualTo(2));
            Assert.That(world.State.Actor(source).aiRemaining, Is.EqualTo(world.Presentation.enemyRecoverySeconds));
            Assert.That(world.State.Actor(second.id).aiRemaining, Is.EqualTo(.25f)); Assert.That(world.ImpactFeedback.Holding, Is.True);
            world.AdvanceEnemies(.1f); Assert.That(world.State.Actor(second.id).aiRemaining, Is.EqualTo(.25f));
            yield return new WaitForSecondsRealtime(.14f);
            world.AdvanceEnemies(.05f);
            Assert.That(world.State.Actor(second.id).aiRemaining, Is.EqualTo(.20f).Within(.0001f));
            Assert.That(world.State.incomingEvents.Count(e => e.rootId == root), Is.EqualTo(2), "Completed recovery must prevent replaying the first attack.");
        }

        [UnityTest] public IEnumerator RealDungeonAoECompletesItsLockedPatternThenStopsBeforeTheNextEnemy()
        {
            file = Path.Combine(directory, "dungeon-hold.json"); world.InitializeDungeonForTests(file); world.ChooseDungeonOption("close"); world.enabled = false;
            EnterRoom("R1"); NoReactionOpportunity();
            var guard = world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id)?.id == "guard");
            var scout = world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id)?.id == "scout");
            string guardId = guard.id, scoutId = scout.id;
            Place(guard, new Vector3(28, 0, -3)); Place(Fighter, new Vector3(30, 0, -3));
            Place(Rogue, new Vector3(30, 0, -2.9f)); Place(Mage, new Vector3(20, 0, -7));
            foreach (var actor in world.State.actors) actor.majorRemaining = 100;
            guard.attackBonus = 30; guard.aiState = 0; guard.aiRemaining = 0; scout.aiState = 2; scout.aiRemaining = 100;
            Physics.SyncTransforms(); world.AdvanceDungeonEnemies(.01f);
            guard = world.State.Actor(guardId);
            Assert.That(guard.aiState, Is.EqualTo(1), "Use the real R1 enemy's generated, locked windup.");
            string root = guard.attackGroupId;
            var pattern = world.Profile.EnemyTemplateFor(guardId).patterns.Single(p => p.id == world.State.world.strings["enemyPattern/" + guardId]);
            Assert.That(pattern.shape, Is.EqualTo("Cone"));
            float nextEnemyRemaining = world.State.Actor(scoutId).aiRemaining;
            guard.aiRemaining = 0; // Due-contact timing fixture; the actual stored shape and command still decide the victims and result.
            world.State.random.backgroundCombat = FaceStream(20, 12); world.ImpactFeedback.ResetObservation();
            world.AdvanceDungeonEnemies(.01f);
            var applied = world.State.incomingEvents.Where(e => e.rootId == root).ToArray();
            Assert.That(applied.Length, Is.EqualTo(2)); Assert.That(applied.All(e => e.damageApplied), Is.True);
            Assert.That(world.State.Actor(guardId).aiState, Is.EqualTo(2));
            Assert.That(world.State.Actor(guardId).aiRemaining, Is.EqualTo(pattern.recovery));
            Assert.That(world.State.Actor(scoutId).aiRemaining, Is.EqualTo(nextEnemyRemaining));
            Assert.That(world.ImpactFeedback.Holding, Is.True);
            yield return new WaitForSecondsRealtime(.14f);
            world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.Actor(scoutId).aiRemaining, Is.EqualTo(nextEnemyRemaining - .01f).Within(.0001f));
            Assert.That(world.State.incomingEvents.Count(e => e.rootId == root), Is.EqualTo(2));
        }

        void ProfileAction(ProfileCommand command)
        { var result = world.ProfileAction(command); Assert.That(result.success, Is.True, command.kind + ": " + result.reason); }
        void EnterRoom(string room)
        {
            var geometry = world.DungeonConfig.Room(room); int index = 0;
            foreach (var actor in world.State.actors) { Place(actor, new Vector3(geometry.x + index * .6f, 0, geometry.z - index * .5f)); index++; }
            ProfileAction(new ProfileCommand { kind = ProfileCommandKind.EnterRoom, roomId = room, qualified = true });
            if (!world.Profile.ActiveRoom.resolved && !string.IsNullOrEmpty(geometry.encounterKind))
                ProfileAction(new ProfileCommand { kind = ProfileCommandKind.ActivateEncounter, roomId = room, qualified = true });
        }
        void ClearEarlierRoomFixture()
        {
            // These earlier rooms only locate the real R7 template. Their zero-HP setup is
            // expressly not combat/playthrough evidence; the asserted Boss death below is a real hit.
            foreach (var enemy in world.State.enemies) enemy.hp = 0;
            ProfileAction(new ProfileCommand { kind = ProfileCommandKind.ResolveEncounter, encounterId = world.Profile.ActiveEncounter.id });
            if (world.Profile.PendingReward != null) ProfileAction(new ProfileCommand { kind = ProfileCommandKind.SkipReward, rewardId = world.Profile.PendingReward.id });
        }

        [UnityTest] public IEnumerator RealBossLastHitSurvivesImmediateClearRebindAndCannotReplayOnLoad()
        {
            file = Path.Combine(directory, "dungeon.json"); world.InitializeDungeonForTests(file); world.ChooseDungeonOption("close"); world.enabled = false;
            EnterRoom("R1"); ClearEarlierRoomFixture(); EnterRoom("R2"); EnterRoom("R3"); ClearEarlierRoomFixture(); EnterRoom("R2");
            Place(Fighter, new Vector3(48, 0, 8)); ProfileAction(new ProfileCommand { kind = ProfileCommandKind.OpenGate, gateId = "P24_SMASH", qualified = true });
            EnterRoom("R4"); ClearEarlierRoomFixture(); EnterRoom("R5"); ClearEarlierRoomFixture(); EnterRoom("R6"); EnterRoom("R7");
            var boss = world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id)?.id == "boss"); string bossId = boss.id;
            Assert.That(world.Profile.EnemyTemplateFor(bossId).id, Is.EqualTo("boss"));
            // Isolate the configured boss's last hit. No assertion treats the other enemy as a player kill.
            foreach (var enemy in world.State.enemies) { enemy.aiState = 2; enemy.aiRemaining = 1000; if (enemy.id != bossId) enemy.hp = 0; }
            boss.hp = 1; Place(boss, new Vector3(-29, 0, 24)); Place(Fighter, new Vector3(-27.5f, 0, 24));
            foreach (var actor in world.State.actors) { actor.majorRemaining = 100; actor.reactionRemaining = 100; }
            world.SelectedActor = "fighter"; world.SelectedTarget = bossId; world.SelectedAction = "attack";
            Physics.SyncTransforms(); Assert.That(world.HasLineOfEffect(BattleWorld.Position(Fighter), BattleWorld.Position(boss)), Is.True);
            world.ImpactFeedback.ResetObservation(); int count = world.ImpactFeedback.EventCount;
            var hit = ActualBasicHit("actual-boss-last-contact");
            Assert.That(hit.hit, Is.True); Assert.That(world.State.Actor(bossId).hp, Is.Zero); Assert.That(hit.damage, Is.GreaterThanOrEqualTo(1));
            Assert.That(world.ImpactFeedback.BossKillCount, Is.EqualTo(1)); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            Assert.That(world.ImpactFeedback.LastEvent, Is.EqualTo("boss-kill")); Assert.That(world.ImpactFeedback.LastLevel, Is.EqualTo(ImpactLevel.Heavy));
            Assert.That(world.ImpactFeedback.MonochromeActive, Is.True);
            world.ResolveDungeonEncounter(); // Immediate real profile transition removes the active encounter's enemies.
            Assert.That(world.Profile.ActiveRoom.resolved, Is.True, world.Message); Assert.That(world.State.inCombat, Is.False);
            world.ImpactFeedback.Observe(); Assert.That(world.ImpactFeedback.BossKillCount, Is.EqualTo(1));
            world.Suspend(); world.ContinueSaved(); world.ImpactFeedback.Observe();
            Assert.That(world.ImpactFeedback.BossKillCount, Is.EqualTo(1)); Assert.That(world.ImpactFeedback.EventCount, Is.EqualTo(count + 1));
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(world.ImpactFeedback.MonochromeActive, Is.False);
        }
    }
}
