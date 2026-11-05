using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool p4Mode;
        sealed class P4EncounterObservation
        {
            public string id, room, mode;
            public float activeRealSeconds, simulationSeconds, basic, passive, tactical, total;
            public int importantCommands, focusEntries, autoReactions, initialCharge;
            public float initialFocus,finalFocus,focusSpent,focusRecovered,focusDiscarded;
            public float focusThinkingRealSeconds;
            public float startSpent,startRecovered,startDiscarded;
        }
        readonly Dictionary<string, P4EncounterObservation> p4Encounters = new Dictionary<string, P4EncounterObservation>();
        readonly List<object> p4Runs = new List<object>();
        bool p4FreezeSeen, p4ReactionSeen;
        List<Vector3> p4CombatPath;
        Vector3 p4CombatGoal;
        float p4NextPath;
        float p4NextProgress;
        void DriveP4Combat(Vector3 goal)
        {
            var from=BattleWorld.Position(world.ActiveActor);
            if(p4CombatPath==null || Vector3.Distance(goal,p4CombatGoal)>.8f || Time.unscaledTime>p4NextPath)
            {
                world.TryOrderPath(from,goal,out p4CombatPath);p4CombatGoal=goal;p4NextPath=Time.unscaledTime+.75f;
            }
            if(p4CombatPath!=null)
            {
                while(p4CombatPath.Count>0 && BattleGeometry.DistanceXZ(from,p4CombatPath[0])<.3f)p4CombatPath.RemoveAt(0);
                if(p4CombatPath.Count>0){Drive(p4CombatPath[0],.05f);return;}
            }
            Drive(goal,.1f);
        }
        public void BeginP4(BattleWorld owner, string evidenceDirectory) { p4Mode = true; Begin(owner, evidenceDirectory); }
        void ObserveP4Metrics()
        {
            if(Time.unscaledTime>=p4NextProgress)
            {
                p4NextProgress=Time.unscaledTime+3;
                Write("progress.json",new {stage,realSeconds=Time.realtimeSinceStartup-started,mode=world.State.mode.ToString(),room=world.Profile?.ActiveRoom?.templateId,actors=world.State.actors.Select(a=>new{a.id,a.hp}),encounters=p4Encounters.Values});
            }
            var encounter = world.Profile?.ActiveEncounter;
            if (encounter == null || !world.State.inCombat) return;
            if (!p4Encounters.TryGetValue(encounter.id, out var metric))
            {
                metric = new P4EncounterObservation { id = encounter.id, room = world.Profile.ActiveRoom.templateId, mode = world.Profile.State.activeMode.ToString(), initialCharge = encounter.initialCharge };
                p4Encounters[encounter.id] = metric;
            }
            var s = world.State;
            if (!world.Menu && s.mode != CombatMode.CombatPaused) metric.activeRealSeconds += Time.unscaledDeltaTime;
            if(!world.Menu&&!world.DungeonModal&&s.focusBudget.active&&s.activePendingEvent==null&&s.decisionResume==null&&
                (s.mode==CombatMode.TacticalFocus||s.mode==CombatMode.PostRollDecision&&s.pendingRoll?.purpose=="Tactical"))metric.focusThinkingRealSeconds+=Time.unscaledDeltaTime;
            metric.simulationSeconds = s.simulationSeconds;
            metric.basic = s.momentumLedger.basicEarned; metric.passive = s.momentumLedger.passiveEarned;
            metric.tactical = s.momentumLedger.defenseEarned + s.momentumLedger.setupEarned + s.momentumLedger.objectiveEarned + s.momentumLedger.highThreatEarned;
            metric.total = s.momentumLedger.totalEarned;
            metric.importantCommands = s.rolls.Count(r => r.purpose == "Tactical" && r.chargeSpent > 0);
            metric.autoReactions = s.pendingThreatEvents.Count(e => e.autoOpened);
            var budget=s.focusBudget;var initial=encounter.encounterStartSnapshot.focusBudget;
            metric.startSpent=initial.secondsSpent;metric.startRecovered=initial.secondsRecovered;metric.startDiscarded=initial.secondsDiscarded;
            metric.initialFocus=initial.secondsRemaining;metric.finalFocus=budget.secondsRemaining;
            metric.focusSpent=budget.secondsSpent-initial.secondsSpent;metric.focusRecovered=budget.secondsRecovered-initial.secondsRecovered;metric.focusDiscarded=budget.secondsDiscarded-initial.secondsDiscarded;
        }
        IEnumerator Front(string id)
        {
            int index = world.FrontChoices.FindIndex(c => c.id == id && c.enabled);
            Require(index >= 0, "P4 front choice absent: " + id); yield return ClickUi("front-" + index);
        }
        IEnumerator FlowP4()
        {
            yield return null; yield return null;
            world.InitializeFrontendForTests(System.IO.Path.Combine(directory, "Profiles", "profile.json"));
            stage = "p4-title"; Require(world.Menu && world.FrontPage == "main", "Normal title did not appear"); Capture("00-p4-title.png");
            Require(!world.FrontChoices.First(c => c.id == "challenge").enabled, "Challenge was unlocked before tutorial");
            yield return Front("settings"); Capture("01-p4-settings.png"); yield return ClickUi("resume");
            yield return Front("story"); Require(world.FrontPage == "prepare", world.Message); Capture("02-p4-preparation.png");
            yield return Front("depart"); Require(!world.Menu && world.State.actors.Count == 3, "Departure failed");
            Record("p4_ordinary_entry", "Actual title, settings, locked challenge, story preparation and departure used UI pointer clicks. No direct new-game shortcut.");
            yield return P4Route();
            var story = world.Profile.State.story;
            p4Runs.Add(new { kind = "Story", activeRealSeconds = story.elapsedRealSeconds, story.explicitPauseSeconds, story.focusLostSeconds });
            stage = "p4-story-settlement"; Capture("17-p4-victory.png");
            yield return ClickChoice("settle:victory"); yield return ClickChoice("growth-menu");
            Require(world.Menu && world.FrontPage == "growth", "Settlement did not return to growth"); Capture("18-p4-growth.png");
            yield return Front("prepare-return"); yield return Front("cycle:mage"); yield return Front("law");
            yield return Front("depart");
            Require(world.Profile.State.activeMode == ProfileModeKind.Challenge, "Prepared challenge did not start");
            string snapshot = CombatJson.Write(world.Profile.ActiveMode.departurePreparation);
            yield return Press(Key.Escape); yield return ClickUi("save");
            string actors = CombatJson.Write(world.Profile.ActiveCombat.State.actors);
            yield return Front("growth"); yield return Front("prepare-return"); yield return Front("cycle:mage"); yield return Front("main");
            yield return Front("continue");
            Require(CombatJson.Write(world.Profile.ActiveMode.departurePreparation) == snapshot, "Continue changed original departure snapshot");
            Require(CombatJson.Write(world.State.actors) == actors, "Continue refilled or changed suspended resources");
            if (world.State.mode == CombatMode.CombatPaused) yield return Press(Key.Escape);
            Capture("19-p4-challenge-continued.png");
            Record("p4_growth_departure_continue", "Story settlement→growth→prepared new challenge; changing meta preparation while suspended then Continue preserved full original actor resources and specialization snapshot.");
            yield return P4Route();
            var run = world.Profile.State.challenge;
            p4Runs.Add(new { kind = "Challenge", activeRealSeconds = run.elapsedRealSeconds, run.explicitPauseSeconds, run.focusLostSeconds, target6to8Minutes = run.elapsedRealSeconds >= 360 && run.elapsedRealSeconds <= 480 });
            stage = "p4-challenge-settlement"; Capture("27-p4-challenge-victory.png");
            yield return ClickChoice("settle:victory"); yield return ClickChoice("growth-menu");
            yield return Front("prepare-return"); yield return Front("depart");
            Require(world.Profile.State.activeMode == ProfileModeKind.Challenge && world.Profile.State.challenge.id != run.id, "Next departure did not make a distinct run");
            Capture("28-p4-next-departure.png");
            Require(p4FreezeSeen, "No actual tactical RollFreeze observed");
            Require(p4ReactionSeen, "No automatic legal reaction was observed during this playthrough; do not claim its Player UI was verified");
            Record("p4_full_loop", "Actual unmodified story and challenge completed through ordinary input, settlement/growth and a distinct next departure. Duration is activity wall time; timing targets and subjective fun are separately reported.");
            completed = true;
        }
        IEnumerator P4Route()
        {
            foreach (string room in new[] { "R1", "R3", "R4", "R5", "R7" })
            {
                if (room == "R3") { yield return P4Walk(new Vector3(52, 0, 2)); yield return P4Event("EVENT_A"); }
                if (room == "R4")
                {
                    yield return P4Walk(new Vector3(52, 0, 0));
                    if (world.Profile.ActiveMode.HasAbility("SMASH") && !world.Profile.IsPassageOpen("P24_SMASH"))
                    { yield return P4Walk(new Vector3(48, 0, 8)); yield return Press(Key.E); }
                }
                if (room == "R7")
                {
                    yield return P4Walk(new Vector3(0, 0, 22)); yield return Press(Key.E);
                    if (world.DungeonChoices.Any(c => c.id == "rest:heal" && c.enabled)) yield return ClickChoice("rest:heal");
                    else if (world.DungeonModal && world.DungeonChoices.Any(c => c.id == "close")) yield return ClickChoice("close");
                    yield return P4Walk(new Vector3(0, 0, 14)); yield return Press(Key.E);
                }
                var target = world.DungeonConfig.Room(room);
                stage = "p4-enter-" + world.Profile.State.activeMode + "-" + room;
                yield return WalkTo(() => new Vector3(target.x, 0, target.z), .6f, 55, () => world.Profile.IsInEncounter);
                yield return Wait(() => world.Profile.IsInEncounter, 10, "Encounter entry did not include all followers");
                var start = world.Profile.ActiveEncounter.encounterStartSnapshot;
                string completedEncounterId=world.Profile.ActiveEncounter.id;
                Require(world.Profile.ActiveEncounter.initialCharge == 2 && start.charges == 2 && start.momentum == 0, "Registered encounter did not start at 2 / 0");
                Capture((room == "R1" ? "03" : room == "R4" ? "12" : room == "R5" ? "14" : "16") + "-" + world.Profile.State.activeMode + "-" + room + "-entry.png");
                yield return P4Combat();
                Require(world.Profile.ActiveMode.Room(room).resolved, "Ordinary combat did not resolve " + room);
                if(p4Encounters.TryGetValue(completedEncounterId,out var ended))
                {
                    var final=world.State.focusBudget;ended.finalFocus=final.secondsRemaining;
                    // Include the last committed contact using actual cumulative counters, never inference.
                    ended.focusSpent=final.secondsSpent-ended.startSpent;
                    ended.focusRecovered=final.secondsRecovered-ended.startRecovered;
                    ended.focusDiscarded=final.secondsDiscarded-ended.startDiscarded;
                }
                if (world.Profile.PendingReward != null)
                {
                    var choice = world.DungeonChoices.FirstOrDefault(c => c.id.StartsWith("reward:") && c.enabled);
                    if (choice != null && world.Profile.ActiveMode.build.relicSlots.Any(x => x == null)) yield return ClickChoice(choice.id);
                    else yield return ClickChoice("skip-reward");
                }
                if (room == "R3") yield return P4Event("EVENT_B");
                Record("p4_encounter_" + world.Profile.State.activeMode + "_" + room, "Actual WASD, basic attacks, selected tactical commands, automatic reactions and real AI; no HP, positions, outcomes, timer, config or RNG injection.");
            }
        }
        IEnumerator P4Walk(Vector3 point) { stage = "p4-explore"; yield return WalkTo(() => point, 1.1f, 55); }
        IEnumerator P4Event(string id)
        {
            var ev = world.DungeonConfig.Event(id); yield return P4Walk(new Vector3(ev.x, 0, ev.z));
            yield return Press(Key.E);
            if (!world.DungeonModal) yield break;
            if (world.DungeonChoices.Any(c => c.id == "actor:mage" && c.enabled)) yield return ClickChoice("actor:mage");
            if (world.DungeonChoices.Any(c => c.id == "event-roll")) yield return ClickChoice("event-roll");
            if (world.DungeonChoices.Any(c => c.id == "event-accept")) yield return ClickChoice("event-accept");
            if (world.DungeonModal && world.DungeonChoices.Any(c => c.id == "close")) yield return ClickChoice("close");
        }
        IEnumerator P4Combat()
        {
            string room = world.Profile.ActiveRoom.templateId; stage = "p4-combat-" + room;
            float deadline = Time.realtimeSinceStartup + 180;
            while (world.Profile.IsInEncounter && !world.DungeonModal && Time.realtimeSinceStartup < deadline)
            {
                if (world.State.mode == CombatMode.RollFreeze) { Keys(); yield return null; continue; }
                if (world.State.mode == CombatMode.ReactionPrompt)
                {
                    if (Array.IndexOf(Environment.GetCommandLineArgs(), "-p47-reaction-dwell") >= 0)
                    {
                        Keys(); yield return new WaitForSecondsRealtime(1.2f);
                        if (world.State.mode != CombatMode.ReactionPrompt) continue;
                    }
                    Keys(); var choices = world.Session.GetReactionChoices().Where(q => q.legal).ToList();
                    if (!p4ReactionSeen) { Capture("09-p4-automatic-reaction.png"); p4ReactionSeen = true; }
                    if (world.State.activeReaction == null && choices.Count > 0) yield return ClickUi("reaction-choice-0");
                    if (world.State.mode == CombatMode.ReactionPrompt && world.State.activeReaction != null) yield return ClickUi("reaction-confirm");
                    else if (world.State.mode == CombatMode.ReactionPrompt) yield return ClickUi("reaction-decline");
                    continue;
                }
                if (world.State.mode == CombatMode.PostRollDecision)
                {
                    freezeArmed = false;
                    if (!p4FreezeSeen && world.State.pendingRoll?.purpose == "Tactical") { Require(freezeCaptured, "Actual freeze frames were not captured"); Capture("07-p4-post-roll.png"); p4FreezeSeen = true; }
                    if(P46Performance.Active) yield return new WaitForSecondsRealtime(.45f);
                    if(world.State.mode==CombatMode.PostRollDecision) yield return ClickUi(world.State.pendingRoll?.purpose=="Reaction"?"reaction-commit":"commit"); continue;
                }
                if (world.State.mode == CombatMode.TacticalFocus)
                {
                    if (world.State.queue.Any(q => q.stage == "Queued")) { yield return ClickUi("exit"); continue; }
                    yield return ClickUi("fighter"); yield return ClickUi("attack");
                    if(P46Performance.Active) yield return new WaitForSecondsRealtime(.65f);
                    if(world.State.mode!=CombatMode.TacticalFocus) continue;
                    int cycle = world.State.enemies.Count;
                    while (!world.Session.Quote("fighter", world.SelectedTarget, "attack").legal && cycle-- > 0) yield return ClickUi("target");
                    var q = world.Session.Quote("fighter", world.SelectedTarget, "attack");
                    if (q.legal && world.State.charges > 0 && world.HasLineOfEffect(BattleWorld.Position(world.State.Actor("fighter")), BattleWorld.Position(world.State.Actor(world.SelectedTarget))))
                    {
                        if (!p4FreezeSeen) { Capture("04-p4-focus-free-orders.png"); freezeArmed = true; }
                        yield return ClickUi("confirm");
                    }
                    else yield return ClickUi("exit");
                    continue;
                }
                if (!world.ActiveActor.Alive)
                {
                    int living = world.State.actors.FindIndex(a => a.Alive);
                    Require(living >= 0, "Party defeated during real playthrough"); yield return Press(living == 0 ? Key.Digit1 : living == 1 ? Key.Digit2 : Key.Digit3); continue;
                }
                // A real player choice: take direct control of the back-line target and rely on
                // its legal reaction instead of the ally AI automatically stepping out of every warning.
                if(!p4ReactionSeen && p4FreezeSeen && room=="R1" && world.State.Actor("mage").Alive && world.State.activeActorId!="mage")
                {yield return Press(Key.Digit3);continue;}
                var enemy = Enemy(false); if (enemy == null) { yield return null; continue; }
                var fighter = world.State.Actor("fighter");
                if (world.State.focusBudget.secondsRemaining > .5f && world.State.charges > 0 && fighter.Alive && fighter.majorRemaining <= 0 && fighter.guardRemaining <= 0 && fighter.windupRemaining <= 0 && string.IsNullOrEmpty(fighter.heldBy) &&
                    world.State.enemies.Any(e => e.Alive && BattleGeometry.DistanceXZ(BattleWorld.Position(fighter), BattleWorld.Position(e)) <= world.Session.Config.Action("attack").range && world.HasLineOfEffect(BattleWorld.Position(fighter), BattleWorld.Position(e))))
                { Keys(); yield return Press(Key.Tab); if(world.State.mode==CombatMode.TacticalFocus&&p4Encounters.TryGetValue(world.Profile.ActiveEncounter.id,out var metric))metric.focusEntries++; continue; }
                var actor = world.ActiveActor;
                if (room == "R5" && !world.Profile.ActiveMode.HasFlag("COVER_SHIFTED") && actor.bonusRemaining <= 0)
                {
                    var cover = new Vector3(26, 0, 24);
                    if (BattleGeometry.DistanceXZ(BattleWorld.Position(actor), cover) > 2) { Drive(cover, 1.8f); yield return null; continue; }
                    Keys(); yield return Press(Key.E); if (world.DungeonModal) break;
                }
                bool imminent = world.State.enemies.Any(e => e.Alive && e.aiState == 1 && e.aiRemaining < .27f && BattleGeometry.Contains(world.EnemyShape(e), BattleWorld.Position(actor)));
                bool demonstrateReaction=!p4ReactionSeen && actor.role==ActorRole.Mage;
                if (imminent && !demonstrateReaction && actor.bonusRemaining <= 0 && actor.guardRemaining <= 0 && actor.dodgeRemaining <= 0 && actor.windupRemaining <= 0 && string.IsNullOrEmpty(actor.heldBy))
                { Keys(); AimAt(BattleWorld.Position(enemy)); MouseState(2); yield return new WaitForSecondsRealtime(.29f); MouseState(0); yield return null; continue; }
                if (imminent && !demonstrateReaction && actor.dodgeCooldown <= 0 && actor.dodgeRemaining <= 0 && string.IsNullOrEmpty(actor.heldBy))
                { Keys(Key.Space); yield return null; yield return null; Keys(); yield return null; continue; }
                float range = world.Session.Config.BasicFor(actor).range;
                if (BattleGeometry.DistanceXZ(BattleWorld.Position(actor), BattleWorld.Position(enemy)) > range - .15f || !world.HasLineOfEffect(BattleWorld.Position(actor),BattleWorld.Position(enemy)))
                { DriveP4Combat(BattleWorld.Position(enemy)); yield return null; continue; }
                Keys();
                if (actor.majorRemaining <= 0 && actor.windupRemaining <= 0 && actor.dodgeRemaining <= 0 && actor.guardRemaining <= 0 && string.IsNullOrEmpty(actor.heldBy)) yield return ClickWorld(BattleWorld.Position(enemy));
                else yield return null;
            }
            Keys(); MouseState(0);
            Require(world.Profile.ActiveMode.Room(room).resolved, "Encounter not cleared by real inputs: " + room + "; " + world.Message + "; HP " + string.Join(",",world.State.actors.Select(a=>a.id+":"+a.hp)));
        }
    }
}
