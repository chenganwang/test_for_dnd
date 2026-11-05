# P4.2 Reaction integration — v0.5.2

Domain implements a grouped, persisted response before applying confirmed damage. This extends the existing CombatSession and Profile persistence sink; it does not create a second combat state or save file.

## Runtime contact boundary

Send one System `CombatCommand` with `kind=IncomingBatch`, unique `id`, enemy `actorId`, stable `rootId`, `qualified=true`, and all `incomingContacts` established by the same attack. Each `IncomingContact` contains `targetId`, integer `damage`, `dc`, `savingThrow`, `parryable`, and `reactionEligibleActorIds` from actual wall/height visibility. Null eligibility exists only for isolated Domain geometry tests. The Domain still checks actor health, distance, height, role, held resources and usefulness. Do not send speculative preview overlap as confirmed contact.

Freeze/explicit pause queues this command without drawing a background die. Reveal/Resume processes queued contacts before choosing the next automatic panel. A repeated root + defender cannot generate a new result. `fromReaction=true` prevents a response from creating another response window.

`State.activePendingEvent` is the visible group; it may exist while `State.activeReaction` is null. One group lists all contacts; the user chooses a response for each target. The same target cannot receive multiple choices. A current unconfirmed choice can be changed, releasing only its own holds; the group's time budget remains unchanged. Another event cannot replace the current panel.

## Public interface

```csharp
List<ReactionQuote> CombatSession.GetReactionChoices(string eventId = null);
ReactionQuote CombatSession.QuoteReaction(string windowId, string actorId, string reactionId);
float CombatSession.Tick(float realDelta, bool decisionUiAvailable);
// ProfileSession exposes the same Tick overload and forwards the latest inner state.
```

Each legal quote exposes `eventId`, `windowId`, `incomingId`, payer `actorId`, protected `defenderId`, `reactionId`, `requiresD20`, `spellCost`, `superiorityCost`, `range`, `damageReduction`, `incomingTotal`, current/after AC, and defense `bonus`, `targetNumber`, `successProbability`.

- Select: `BeginReaction`, `actorId=quote.actorId`, `reactionWindowId=quote.windowId`, `incomingId=quote.incomingId`, `reactionId=quote.reactionId`. Supply fresh `lineOfSight` from payer to protected target.
- D20 confirm: `ConfirmReactionRoll`, same window/incoming and fresh LOS. The confirmation transaction immediately spends actor Reaction, writes its payment ID, and draws one visible Defense roll. Shared Charges are unchanged.
- Shield confirm: `CommitReaction`, same window/incoming and fresh LOS. No D20 is drawn; actor Reaction and the spell slot are paid together.
- Reaction postroll: `Modify`, `Reroll`, `Commit`, `Cancel` route through the one pending ticket. Runtime should also supply fresh LOS on Commit; no separate raw dice are generated for UI.
- Decline selected target: `DeclineReaction` with `reactionWindowId`. Decline group: omit window and provide `reactionEventId` (or use current group).
- `SetReactionSettings` with a `ReactionSettings` DTO changes `autoMode` (`ImportantLegal`, `AllLegal`, `Manual`), `decisionSeconds` (6–60), `pauseDecision`. Existing groups retain their original remaining time.

## Time / payment / persistence

Default automatic mode opens a useful affordable response to confirmed damage at least 20% of max HP or lethal damage. Natural 20 never offers Shield as an AC miss. Fighter protection can still mitigate a parryable critical hit if its Defense check can succeed.

ReactionFocus uses .1 simulation scale; accessibility `pauseDecision=true` freezes the world while the UI remains actionable. Explicit combat pause and RollFreeze take priority. Runtime passes `decisionUiAvailable=false` during focus loss or a modal that blocks the reaction. Six seconds are active UI seconds, not simulated seconds; dice animation, reroll animation, explicit pause and unavailable UI consume none. Switching choices, editing, rerolling, save/load and settings changes do not reset the current budget.

Unpaid timeout declines. A paid D20 AwaitDecision timeout accepts its current legal result; it never draws another die. If it is no longer legal, the confirmed incoming damage proceeds and already spent Reaction/Inspiration remain spent. Fighter class resource is held while choosing/rolling and paid on Commit/accepted timeout, including a failed defense roll. Explicit cancel releases that unspent class hold; it does not refund Reaction or edits.

The original Tactical postroll ticket may be suspended once. It retains its paid Charge, result and Major/class holds. When the group finishes, the original mode/command actor and same ticket return. Invalid dead owner/target cancels that original unpaid execution, releasing only its remaining holds. Commit returns to TacticalFocus; it never automatically starts queued actions.

Execute and deadline settlement clone, validate, persist through the existing single sink, then publish. Any failure restores the entire original state, including RNG, already paid costs, group budget, world positions and prior context. `LastTickError` exposes deadline persistence failures. Ordinary timer progression is captured by the existing periodic/safe save; it does not add a second writer.

## Bounded specialization decisions

`CombatConfig.actorOverrides` is resolved per actor by `RulesFor(actorId)` and snapshotted by the Profile departure configuration.

- F_GUARD: protection radius 4.5 instead of 3; real distance, height and visibility remain required.
- F_PRESSURE: first landed Shove each encounter adds `firstShoveBreakBonus` (default 1) actual break damage.
- F_ADEPT: existing Precision action, once per encounter, converts a non-natural-1 Attack failure with deficit at most 2 into deterministic +2 for one owned Superiority. Other Precision uses retain the existing d6. No free resource is created.
- M_WARD: permits Mage Shield on an ally within 5, with the same usefulness and one-slot checks.
- M_FOCUS: first nonlethal actual HP damage preserves an already active concentration once; subsequent damage or becoming down breaks it normally.
- R_FLANK, R_MOBILE, R_DISTRACT and M_INSIGHT are consumed by P4 preparation code.

Fighter self Parry negates its own hit; allied protection removes floor(50% of that target's pending damage), minimum 1, on successful D20. `ReactionSpec` owns the requiresD20/cost/protectAllies/reduction definition; actor override owns resolved radius. Mage Shield adds +5 AC for 2 seconds and can negate only a noncritical hit whose original total is below the resulting AC.

The free Fighter Prepare order has a separate actual guard effect: after the preparation finishes, a held guard protects its chosen target only in its forward direction, range, height and current line of sight, against parryable non-Save contact. Runtime updates `BasicOrder.protectionLineOfSight` from geometry. `PendingIncoming.guardActorId` records who blocked the contact. Perfect timing reports `protected_by_preparation`; the later held guard reduces damage by half. Merely preparing earns nothing. This physical block spends neither Reaction nor Superiority and does not create a D20 ticket.

`ResetP4ReactionSpecializations(state)` resets the three per-encounter once markers. `ResetP4PreparationState(state)` clears prior orders/tokens/intents. Profile calls these only when building a new encounter or cleaning exploration state; loading/resuming paid combat never resets them or replenishes resources.

## Evidence boundary

Pure .NET/NUnit tests exercise transactions, saved state and deterministic RNG. They do not prove Unity input, geometry, panel layout, standalone build or human playability. The parent task records those categories separately. Legacy P1 deadline and payment expectations changed intentionally according to v052: 6 active UI seconds; confirmed Reaction remains spent after cancel; paid timeout accepts the existing result.
