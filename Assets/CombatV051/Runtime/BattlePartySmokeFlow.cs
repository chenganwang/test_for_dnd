using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace CombatV051.Runtime
{
    /// <summary>Explicit -v051-party-smoke fixture. Never attached by normal startup.</summary>
    public sealed class BattlePartySmokeFlow : MonoBehaviour
    {
        BattleWorld world;
        string directory;
        readonly List<object> checks = new List<object>();
        readonly List<object> fixtures = new List<object>();
        readonly List<object> incomingAttempts = new List<object>();
        readonly List<string> errors = new List<string>();
        readonly List<string> screenshots = new List<string>();

        public void Begin(BattleWorld owner, string evidenceDirectory)
        {
            Require(Path.IsPathRooted(evidenceDirectory), "An absolute, fresh evidence directory is required.");
            world = owner;
            directory = Path.GetFullPath(evidenceDirectory);
            Application.runInBackground = true;
            world.IgnoreApplicationFocus = true;
            DisableNativeInput();
            Application.logMessageReceived += Log;
            StartCoroutine(Run());
        }

        void DisableNativeInput()
        {
            world.Controls.Map.Disable();
            var raycaster = world.Hud.Canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
        }

        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message + "\n" + stack);
        }

        IEnumerator Run()
        {
            // Flatten nested enumerators so a failed wait/check still produces a truthful summary.
            Exception failure = null;
            var stack = new Stack<IEnumerator>();
            stack.Push(Flow());
            while (stack.Count > 0)
            {
                object value = null;
                bool next = false;
                try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                catch (Exception error) { failure = error; }
                if (failure != null) break;
                if (!next) { stack.Pop(); continue; }
                if (value is IEnumerator nested) { stack.Push(nested); continue; }
                yield return value;
            }
            if (failure != null) errors.Add(failure.ToString());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "summary.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                passed = failure == null && errors.Count == 0,
                phase = "V051-P1", engine = Application.unityVersion, utc = DateTime.UtcNow.ToString("o"),
                checks, errors, screenshots, fixtures, incomingAttempts,
                captureMode = "OFFSCREEN_RENDER_FIXTURE", normalWindowPresentationVerified = false,
                humanPlayers = 0, humanRuns = 0, usesTestFixtures = true,
                fixture = "Opt-in scripted commands, fixed positions/health/resources and one initial seed; actual Update, domain transactions, disk saves and GPU rendering. Reaction attacks are explicitly constructed qualified fixtures, not naturally encountered attacks. No human or normal-window playability is implied.",
                savePath = world.SavePath, graphicsDevice = SystemInfo.graphicsDeviceName,
                width = Screen.width, height = Screen.height
            }, Newtonsoft.Json.Formatting.Indented));
            Application.Quit(failure == null && errors.Count == 0 ? 0 : 2);
        }

        IEnumerator Flow()
        {
            Require(!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any(), "Evidence directory must be fresh.");
            Directory.CreateDirectory(directory);
            yield return null;
            yield return null;
            world.InitializePartyForTests(Path.Combine(directory, "Profiles", "party.json"));
            DisableNativeInput();
            Require(!world.Menu && world.State.IsPartyP1 && world.State.actors.Count == 3, "P1 party did not initialize.");
            Require(Path.GetFullPath(world.SavePath).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Smoke save escaped isolated evidence directory.");
            Require(world.State.actors.Select(a => a.role).Distinct().Count() == 3, "Fighter/Rogue/Mage party roles are not distinct.");
            Fixture("01-party-ai", "Fixed seed 5105101 only at initial fixture; positions prescribed for autonomous follow/attack; enemy HP500 and recovery100 suppress unrelated incoming attacks. Player resources retain template values.", () =>
            {
                world.State.random = RandomStreams.Create(5105101UL);
                Place("fighter", 0, 0); Place("rogue", -2, 1); Place("mage", -4, -2); Place("enemy-1", 3, 0);
                QuietEnemy();
            });
            var initialRotation = world.BattleCamera.transform.rotation;
            Require(world.BattleCamera.orthographic, "Party camera must be orthographic.");
            Require(Mathf.Abs(Mathf.DeltaAngle(initialRotation.eulerAngles.y, 45)) < .05f &&
                Mathf.Abs(Mathf.DeltaAngle(initialRotation.eulerAngles.x, 55)) < .05f, "Camera must start yaw45/pitch55.");
            foreach (var actorId in new[] { "rogue", "mage", "fighter" })
            {
                Good(world.SwitchActive(actorId), "switch active " + actorId);
                Require(world.State.activeActorId == actorId, "SwitchActive left the old controller selected.");
                yield return null;
                Require(Quaternion.Angle(initialRotation, world.BattleCamera.transform.rotation) < .01f, "Active switch rotated the fixed camera.");
            }
            var rareBefore = RareResources();
            int chargesBeforeAi = world.State.charges;
            var rogueBefore = Position("rogue");
            ulong aiDraws = world.State.random.backgroundCombat.drawCount;
            float beforeSim = world.State.simulationSeconds;
            yield return WaitUntil(() => world.State.random.backgroundCombat.drawCount > aiDraws &&
                Vector3.Distance(rogueBefore, Position("rogue")) > .1f, 5, "Autonomous allies did not move and make background attacks through real Update.");
            Require(world.State.simulationSeconds > beforeSim && RareResources() == rareBefore && world.State.charges >= chargesBeforeAi,
                "AI failed to advance naturally or spent a shared/scarce resource.");
            Require(world.State.actors.Where(a => a.id != "fighter").All(a => a.aiPolicyId == "conservative-v1"), "Allies did not run the declared AI policy.");
            Capture("01-party-realtime.png");
            Record("party_switch_and_autonomous_basics", "Three active switches retained yaw/pitch. Real Update moved allies and advanced background attack RNG without spending shared charges or class/slot/item/inspiration resources.");

            Fixture("02-two-actor-queue", "Set an explicit threshold fixture of one charge and Momentum99, then require real Update in effective combat to earn the second charge; this does not claim a naturally completed accumulation cycle. Reset actor cooldowns and place unobstructed actors/target. Hold unused companion Major timers at30 to prevent incidental AI attacks while demonstrating separate selected actors; remove Mage timer immediately before its selection. No result or natural face is injected.", () =>
            {
                Require(world.State.pendingRoll == null && !world.State.queue.Any(q => q.stage == "Queued" || q.stage == "Executing"), "Queue fixture requires an idle scenario boundary.");
                world.State.charges = 1;
                world.State.momentum = 99;
                foreach (var actor in world.State.actors) ResetTimers(actor);
                Place("fighter", 0, 0); Place("rogue", -2, 1); Place("mage", -2, -2); Place("enemy-1", 3, 0);
                world.State.Actor("mage").majorRemaining = 30;
                world.State.Actor("rogue").majorRemaining = 30;
                QuietEnemy();
            });
            Require(world.State.chargeCap == 2 && world.State.charges == 1 && world.State.momentum == 99,
                "Threshold fixture must begin at charges1/cap2/Momentum99.");
            var thresholdBefore = MomentumCounters();
            float earnedBefore = world.State.momentumLedger.totalEarned;
            float passiveBefore = world.State.momentumLedger.passiveEarned;
            yield return WaitUntil(() => world.State.charges == 2, 5, "Real effective-combat Update did not convert Momentum into the second shared charge.");
            Require(world.State.charges <= world.State.chargeCap && world.State.momentum >= 0 && world.State.momentum < 100 &&
                world.State.momentumLedger.totalEarned > earnedBefore && world.State.momentumLedger.passiveEarned > passiveBefore,
                "Threshold conversion did not respect the cap, drain100 Momentum, or record actual effective-combat earnings.");
            File.WriteAllText(Path.Combine(directory, "momentum-threshold-counters.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                usesThresholdFixture = true, naturalFullAccumulationCycleClaimed = false,
                before = thresholdBefore, after = MomentumCounters(),
                description = "Actual Update earned effective-combat Momentum after the explicit 1-charge/99-Momentum fixture. Background draw counts are reported, not assumed to increase during this passive threshold segment."
            }, Newtonsoft.Json.Formatting.Indented));
            Record("momentum_threshold_conversion", "From the explicit charge1/Momentum99 fixture, real effective-combat Update earned and recorded Momentum, drained100, and produced the second charge within cap2; counter/draw snapshots are saved.");
            Good(world.Command(CommandKind.EnterFocus), "enter party focus");
            Good(world.SelectCommandActor("fighter"), "select Fighter command owner");
            world.SelectedAction = "attack";
            Good(world.ConfirmCurrentDecision(), "confirm Fighter attack");
            string fighterTicket = world.State.pendingRoll.id;
            Require(world.State.charges == 1 && world.State.Actor("fighter").heldBy == fighterTicket &&
                string.IsNullOrEmpty(world.State.Actor("mage").heldBy), "First roll did not reserve only the Fighter Major while spending one shared charge.");
            yield return WaitForDecision();
            Good(world.ConfirmCurrentDecision(), "commit Fighter attack");
            world.State.Actor("mage").majorRemaining = 0; // Declared timer fixture, not a recharge command.
            Good(world.SelectCommandActor("mage"), "select Mage command owner");
            world.SelectedAction = "control";
            Good(world.ConfirmCurrentDecision(), "confirm Mage control");
            string mageTicket = world.State.pendingRoll.id;
            Require(world.State.charges == 0 && world.State.Actor("mage").heldBy == mageTicket &&
                world.State.Actor("fighter").heldBy == fighterTicket, "Two actors did not hold independent Major tokens against the shared two-charge supply.");
            yield return WaitForDecision();
            Good(world.ConfirmCurrentDecision(), "commit Mage control");
            Require(world.State.queue.Count(q => q.ticketId == fighterTicket || q.ticketId == mageTicket) == 2, "Two distinct actor commands were not queued.");
            Capture("02-two-actors-queued.png");
            Good(world.Command(CommandKind.ExitFocus), "execute party queue");
            yield return WaitUntil(() => world.State.queue.Count(q => (q.ticketId == fighterTicket || q.ticketId == mageTicket) && q.stage == "Resolved") == 2,
                8, "The two actor commands did not resolve through the real Update queue.");
            Require(world.State.charges == 0 && world.State.Actor("fighter").majorRemaining > 0 && world.State.Actor("mage").majorRemaining > 0,
                "Executed commands did not retain independent Major recovery or shared charge cost.");
            Record("two_actor_charge_and_major_ownership", "Fighter Attack and Mage Control each Confirm/Commit against shared2→1→0 charges, hold different actor Major tokens, then resolve through real queue Update with separate recovery.");

            Fixture("03-shield-incoming", "Preserve zero charges and remaining Mage slots. Set Mage/Fighter HP500, non-player Major timers30, stationary enemy recovery100. Enemy attack bonus = Mage AC minus8; send separate real background IncomingAttack commands (damage1) until a noncritical Shield-changing hit occurs. Every attempted original result is recorded; no seed reset or result injection.", () =>
            {
                Place("mage", 0, 0); Place("fighter", -2, 1); Place("rogue", -3, -2); Place("enemy-1", 1.5f, 0);
                foreach (var actor in world.State.actors) { actor.hp = actor.maxHp = 500; actor.majorRemaining = 30; actor.guardRemaining = 0; actor.dodgeRemaining = 0; }
                QuietEnemy();
                world.State.Actor("enemy-1").attackBonus = world.State.Actor("mage").armorClass - 8;
            });
            Require(world.State.Actor("mage").spellSlots > 0 && world.State.charges == 0, "Shield setup lost its remaining slot or zero-charge precondition.");
            string shieldWindow = OfferReaction("mage", "shield", false);
            Good(world.React(CommandKind.BeginReaction, shieldWindow), "open Shield prompt");
            Require(world.State.mode == CombatMode.ReactionPrompt && Time.timeScale > 0, "Shield should prompt without RollFreeze.");
            Capture("03-shield-prompt.png");
            int slotsBefore = world.State.Actor("mage").spellSlots;
            ulong visibleBeforeShield = world.State.random.visibleTactical.drawCount;
            ulong backgroundBeforeShield = world.State.random.backgroundCombat.drawCount;
            Good(world.ConfirmCurrentDecision(), "commit Shield");
            Require(world.State.mode != CombatMode.RollFreeze && world.State.pendingRoll == null && world.State.charges == 0 &&
                world.State.Actor("mage").spellSlots == slotsBefore - 1 && world.State.Actor("mage").reactionRemaining > 0,
                "Shield did not spend exactly one Slot plus Reaction without a charge or RollFreeze.");
            Require(world.State.random.visibleTactical.drawCount == visibleBeforeShield && world.State.random.backgroundCombat.drawCount == backgroundBeforeShield,
                "Shield confirmation rolled dice or rerolled the recorded incoming attack.");
            Capture("04-shield-resolved.png");
            Record("shield_without_charge_or_roll", "A recorded, qualified incoming attack offered Shield; real prompt/commit consumed one Slot plus Reaction at zero shared charges, with no tactical/background reroll or RollFreeze.");

            Fixture("04-parry-incoming", "Keep zero charges. Put Fighter and enemy1.5m apart, no guard/dodge; preserve Fighter Reaction and superiority. Enemy bonus equals Fighter AC so all non-natural1 attacks qualify. Send real background incoming attacks until an offered Parry window; original dice remain authoritative and every attempt is recorded.", () =>
            {
                Place("fighter", 0, 0); Place("mage", -3, 1); Place("rogue", -3, -2); Place("enemy-1", 1.5f, 0);
                QuietEnemy();
                world.State.Actor("enemy-1").attackBonus = world.State.Actor("fighter").armorClass;
                world.State.Actor("fighter").guardRemaining = 0;
                world.State.Actor("fighter").dodgeRemaining = 0;
            });
            int superiorityBefore = world.State.Actor("fighter").superiority;
            Require(superiorityBefore > 0 && world.State.Actor("fighter").reactionRemaining <= 0, "Parry fixture lacks an available Fighter Reaction or superiority die.");
            string parryWindow = OfferReaction("fighter", "parry", true);
            Good(world.React(CommandKind.BeginReaction, parryWindow), "open Parry prompt");
            Good(world.ConfirmCurrentDecision(), "confirm Parry roll");
            Require(world.State.mode == CombatMode.RollFreeze && Time.timeScale == 0 && world.State.charges == 0,
                "Parry at zero charges did not enter complete RollFreeze.");
            string parryTicket = world.State.pendingRoll.id;
            Require(world.State.pendingRoll.purpose == "Reaction" && world.State.pendingRoll.chargeSpent == 0 &&
                world.State.pendingRoll.chargePolicy == "None", "Parry roll did not use the independent Reaction charge policy.");
            string actorsFrozen = CombatJson.Write(world.State.actors), enemiesFrozen = CombatJson.Write(world.State.enemies);
            string environmentFrozen = CombatJson.Write(world.State.world);
            var frozenPosition = world.BattleCamera.transform.position;
            var frozenRotation = world.BattleCamera.transform.rotation;
            string faceFrozen = CombatJson.Write(world.State.pendingRoll.rawFaces);
            ulong drawsFrozen = world.State.random.visibleTactical.drawCount;
            Capture("05-parry-freeze.png");
            yield return new WaitForSecondsRealtime(.12f);
            Require(world.State.mode == CombatMode.RollFreeze && world.RollElapsed > 0, "Parry did not animate in unscaled time while frozen.");
            Require(actorsFrozen == CombatJson.Write(world.State.actors) && enemiesFrozen == CombatJson.Write(world.State.enemies) &&
                environmentFrozen == CombatJson.Write(world.State.world), "Actor/AI/world snapshot changed during Parry RollFreeze.");
            Require(Vector3.Distance(frozenPosition, world.BattleCamera.transform.position) < .0001f &&
                Quaternion.Angle(frozenRotation, world.BattleCamera.transform.rotation) < .01f, "Camera drifted during Parry RollFreeze.");
            Record("parry_zero_charge_complete_freeze", "A zero-charge Reaction roll froze every actor/AI/world DTO and the fixed camera while its unscaled dice UI advanced; its ticket explicitly uses chargePolicy None.");
            yield return WaitForDecision();
            Require(world.State.pendingRoll.id == parryTicket && faceFrozen == CombatJson.Write(world.State.pendingRoll.rawFaces) &&
                world.State.random.visibleTactical.drawCount == drawsFrozen, "Parry reveal changed its original face or drew new dice.");
            Capture("06-parry-decision.png");
            world.CapturePresentationSnapshot();
            Good(world.Session.Save(), "save full Parry decision");
            var savedPosition = world.BattleCamera.transform.position;
            var savedRotation = world.BattleCamera.transform.rotation;
            string completeSnapshot = CombatJson.Write(world.State);
            File.WriteAllText(Path.Combine(directory, "parry-before-restore.json"), completeSnapshot);
            world.Suspend();
            world.ContinueSaved();
            DisableNativeInput();
            Require(!world.Menu && completeSnapshot == CombatJson.Write(world.State), "Full P1 Parry decision changed after actual suspend/file-load/restore.");
            File.WriteAllText(Path.Combine(directory, "parry-after-restore.json"), CombatJson.Write(world.State));
            Require(Vector3.Distance(savedPosition, world.BattleCamera.transform.position) < .0001f &&
                Quaternion.Angle(savedRotation, world.BattleCamera.transform.rotation) < .01f, "Restored reaction lost the camera pose saved with the decision.");
            Good(world.Command(CommandKind.Commit, actor: "fighter", target: "enemy-1", action: "parry", ticket: parryTicket, id: "party-parry-commit-once"), "commit Parry decision");
            var duplicate = world.Command(CommandKind.Commit, actor: "fighter", target: "enemy-1", action: "parry", ticket: parryTicket, id: "party-parry-commit-once");
            Require(duplicate.success && duplicate.duplicate && world.State.charges == 0 && world.State.Actor("fighter").reactionRemaining > 0 &&
                world.State.Actor("fighter").superiority == superiorityBefore - 1, "Parry Commit was not idempotent or did not spend one class die and Reaction independently of charges.");
            Require(world.State.activeReaction == null && world.State.pendingRoll == null, "Committed Parry left an active decision.");
            Capture("07-parry-committed.png");
            Record("parry_full_restore_and_idempotent_commit", "Complete Parry decision JSON and its camera pose survived actual disk reload identically, then Commit spent one superiority die/Reaction once at zero shared charges and replay was idempotent.");
            Require(errors.Count == 0, "Runtime emitted errors; see actual log and summary.");
        }

        string OfferReaction(string actorId, string reactionId, bool parryable)
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                string root = "party-smoke-fixture/" + reactionId + "/" + attempt;
                var result = world.ExecuteWorldCommand(new CombatCommand
                {
                    id = root, rootId = root, kind = CommandKind.IncomingAttack, actorId = "enemy-1", targetId = actorId,
                    authority = CommandAuthority.System, qualified = true, amount = 1, parryable = parryable
                });
                incomingAttempts.Add(new { reactionId, attempt, explicitlyConstructedAttack = true, result = result.Clone() });
                Good(result, "record incoming " + root);
                var window = world.State.reactionWindows.LastOrDefault(w => w.defenderId == actorId && w.stage == "Offered");
                if (window == null) continue;
                var quote = world.Session.QuoteReaction(window.id, actorId, reactionId);
                Require(quote.legal, "Offered reaction is not legal: " + quote.reason);
                Require(quote.requiresD20 == (reactionId == "parry"), "Reaction die policy differs from its quote.");
                return window.id;
            }
            throw new InvalidOperationException("No qualifying " + reactionId + " window after 64 recorded real background attempts; no result was injected.");
        }

        void Fixture(string name, string explanation, Action setup)
        {
            string before = CombatJson.Write(world.State);
            setup();
            world.CapturePresentationSnapshot();
            Good(world.Session.Save(), "persist fixture " + name);
            File.WriteAllText(Path.Combine(directory, name + "-fixture-before.json"), before);
            File.WriteAllText(Path.Combine(directory, name + "-fixture-after.json"), CombatJson.Write(world.State));
            fixtures.Add(new { name, explanation, gameplayAcquisitionClaimed = false });
        }

        void QuietEnemy()
        {
            var enemy = world.State.Actor("enemy-1");
            enemy.hp = enemy.maxHp = 500;
            enemy.aiState = 2; enemy.aiRemaining = 100;
        }

        static void ResetTimers(ActorState actor)
        {
            Require(string.IsNullOrEmpty(actor.heldBy) && string.IsNullOrEmpty(actor.reactionHeldBy), "A fixture cannot erase a reserved actor token.");
            actor.majorRemaining = actor.bonusRemaining = actor.reactionRemaining = 0;
            actor.guardRemaining = actor.guardElapsed = actor.dodgeRemaining = actor.dodgeCooldown = 0;
            actor.aimRemaining = actor.windupRemaining = 0;
            actor.aimPreparing = actor.aimReady = false;
        }

        string RareResources() => CombatJson.Write(world.State.actors.Select(a => new { a.id, a.inspiration, a.superiority, a.spellSlots, a.items }).ToArray());
        object MomentumCounters() => new
        {
            world.State.charges, world.State.chargeCap, world.State.momentum, world.State.simulationSeconds,
            world.State.momentumLedger.totalEarned, world.State.momentumLedger.passiveEarned,
            backgroundDraws = world.State.random.backgroundCombat.drawCount,
            visibleDraws = world.State.random.visibleTactical.drawCount,
            factRootCount = world.State.momentumLedger.roots.Count, world.SimulatedFrames
        };
        Vector3 Position(string id) { var a = world.State.Actor(id); return new Vector3(a.x, a.heightY, a.z); }
        void Place(string id, float x, float z) { var a = world.State.Actor(id); a.x = x; a.z = z; }
        void Record(string name, string detail) { checks.Add(new { name, passed = true, detail }); }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Good(CombatResult result, string step) { Require(result != null && result.success, step + ": " + result?.reason); }
        IEnumerator WaitForDecision() => WaitUntil(() => world.State.mode == CombatMode.PostRollDecision && world.Session.Scale > 0 && world.Session.Scale <= .15f, 5, "Roll did not reveal into a live slow-time decision.");
        IEnumerator WaitUntil(Func<bool> predicate, float seconds, string failure)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(predicate(), failure);
        }

        void Capture(string filename)
        {
            world.Hud.RefreshView();
            var camera = world.BattleCamera; var canvas = world.Hud.Canvas;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; var oldDistance = canvas.planeDistance;
            var oldSort = canvas.sortingOrder; var oldOverride = canvas.overrideSorting;
            var oldActive = RenderTexture.active; var oldTarget = camera.targetTexture;
            var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = null;
            try
            {
                camera.targetTexture = target; canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.overrideSorting = true; canvas.sortingOrder = short.MaxValue;
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false); texture.Apply(false, false);
                var pixels = texture.GetPixels32(); var colors = new HashSet<int>(); int lit = 0;
                for (int i = 0; i < pixels.Length; i += 13)
                { var p = pixels[i]; colors.Add(p.r * 65536 + p.g * 256 + p.b); if (p.r > 20 || p.g > 20 || p.b > 20) lit++; }
                File.WriteAllBytes(Path.Combine(directory, filename), texture.EncodeToPNG());
                Require(colors.Count > 20 && lit > pixels.Length / 1000, "Offscreen capture lacks actual rendered detail.");
                screenshots.Add(filename);
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldDistance;
                canvas.overrideSorting = oldOverride; canvas.sortingOrder = oldSort;
                RenderTexture.ReleaseTemporary(target); if (texture != null) Destroy(texture);
            }
        }

        void OnDestroy() { Application.logMessageReceived -= Log; }
    }
}
