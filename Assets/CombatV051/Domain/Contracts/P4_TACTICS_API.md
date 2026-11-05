# P4 free orders, preparation, economy

This contract describes implemented source. Unity execution and player evidence are recorded separately by the root engine run.

## Public runtime bridge

`BattleWorld` exposes these methods; UI selections continue to use `SelectedActor`, `SelectedTarget`, and `SelectedAction`.

- `IssueMoveOrder(Vector3 point, bool replace = false)` validates an actual capsule route before issuing a persistent command.
- `IssueHoldOrder(bool replace = false)` holds the actor's current position.
- `IssueFocusTargetOrder(string targetId, bool replace = false)` overrides the AI target until explicitly replaced, canceled, defeated, or forced to evade.
- `IssueFollowOrder(bool replace = false)` releases the explicit order and resumes ordinary formation AI.
- `IssuePreparationOrder(bool replace = false)` chooses the selected actor's class preparation.
- `CancelSelectedOrder()` explicitly cancels that actor's order.
- `TryGetRearDestination(out Vector3 destination, out string reason)` is a read-only, cached collision/path query.
- `SelectedOrderSummary` supplies player-facing Chinese state text.
- `RefreshEngagementEvidence()` runs before the authoritative simulation tick. It updates actual line-of-effect engagement, prepared ally protection visibility, registered dangerous windup roots, and the existing boss half-health budget phase.
- `AdvanceBasicOrders(float simulatedDelta)` runs after the simulation tick. It resolves ready basic contacts and advances explicit movement through wall sweeps.
- `DirectMovementMayOverrideOrder(ActorState actor, bool hasMovementInput)` permits explicit real-time manual movement to cancel a retained order.
- `BeginBasicAttack(string actorId, string targetId, CommandAuthority authority = Player)` creates a persisted windup intent. Contact is a separate System command and rechecks live target, range, and line of effect.

All successful command receipts are owned by `CombatSession.Execute`. Runtime route caches are disposable: destination/order identity remains in the saved DTO and the engine reconstructs routes on demand. Move completion and completed rogue positioning become persistent Hold commands. Actual path polylines, current/target/range/destination rings, and their frozen presentation are saved together.

## Domain commands and preparation

`IssueBasicOrder` uses `actionId = Move | Hold | FocusTarget | Follow | Prepare`. A current active order requires `replaceExisting = true`. Move and rogue preparation require a qualified engine route; `x/z` are destinations, never a teleport instruction. `CompleteBasicOrder` requires System authority and actual arrival.

Fighter preparation stores its protected ally and initial front direction. After the preparation timer, the existing incoming pipeline can apply frontal protection at legal distance/height/visibility. Preparing alone grants no Momentum. A failed or interrupted preparation clears its guard state.

Rogue preparation moves to an actual reachable rear position. A SetupToken is created only after arrival behind an enemy with a committed attack root. The next eligible attack uses one token; moving the actor is not a d20 action. Waiting for a commitment expires on simulated time.

Mage preparation stores `preparationActionCategory = Attack | Save` (runtime chooses the selected action's category). It reveals the target's existing information after the stationary timer. Target and category are immutable for that token. Save preparation gives the target's Save disadvantage, not a beneficial advantage for the enemy. Damage, movement, or stagger interrupt preparation.

SetupToken grants no resource by itself. Actual utilization by another actor yields a single cooperative root worth 30. Confirm consumes the token once, and a later successful resolution qualifies its reward without consuming another token. Existing advantages do not add extra d20s beyond the standard advantage/disadvantage rule.

`ResetP4PreparationState(CombatState)` clears orders, tokens, unresolved basic intents, preparation cooldowns, one-use preparation flags, and setup histories for a new encounter or resolved exploration state. Loading an active encounter must not call it.

## Economy and registration

- New encounter: 2 charges, Momentum 0; ordinary cap 2, legal expanded cap 3.
- Full cap: Momentum stays 0. There is no stored overflow, pending claim, or hidden extra roll.
- Qualified effective engagement: 4 Momentum per simulated second.
- Basic actual hit: 1 Momentum, shared across the party with one simulated second cooldown.
- Defense 30; consumed cooperative setup 30; registered objective/dangerous interruption 40; registered high-threat kill 25.
- A root pays its maximum qualified amount, not a sum. High-threat victims and dangerous windup roots have their own resolved sets.
- Tactical budget: Normal 100, Elite 150, Boss phase 100. Passive/basic income is separate.
- Paid action derived chain: at most 60 Momentum. Refund-origin commands cannot generate another reward.
- A full-cap award still consumes its root/budget qualification; it cannot be replayed after spending.
- Training markers, resolved encounters, distant actors, and engine-proven disengagement generate no passive/tactical income.

`ConfigureP4Encounter(state, kind, training, objectiveRoots, highThreatRoots)` registers an encounter without restoring resources or clearing prior receipts. The profile owner calls it once for the actual encounter identity.

`AdvanceP4BudgetPhase(state, phaseId)` is a one-way Boss phase transition. It resets only the current tactical budget counter, records closed phases, and preserves charges, Momentum, root histories, and paid chains. The engine calls it at the existing live Boss half-health threshold.

The actual parameter entry is `Resources/CombatConfig.json`. Per-actor specialization parameters come from `CombatConfig.actorOverrides`; they do not mutate another party member's rules.
