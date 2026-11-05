# P1 Runtime API — implementation contract

Namespace `CombatV051.Domain`. P1 Domain implemented and verified by 205 .NET/NUnit tests (114 P0 + 91 P1); Unity/Player integration is a separate pending check. P0 `Create`, constructor, existing commands and tests retain behavior.

```csharp
CombatState.CreatePartyP1(string profileId,string modeInstanceId,ulong seed,CombatConfig config=null)
CombatSession.Load(string path,CombatConfig config,string expectedRulesProfileId)
// P1 runtime always passes "p1-party"; constructor pins Store to State.rulesProfileId.
session.SwitchActive(string commandId,string actorId)
session.SelectCommandActor(string commandId,string actorId)
session.QuoteReaction(string windowId,string actorId,string reactionId)
session.Config.Template(ActorRole role) // ActorTemplate
session.Config.BasicFor(ActorState actor) // BasicAttackSpec: id, range, damage, ranged
session.Config.Action(string id) // ActionSpec incl. range, windup, costs
```

ActorRole: Fighter/Rogue/Mage/Enemy. FormationPreference: Front/Mid/Back.
ActorTemplate: role, formation, hp, armorClass, attackBonus, proficiency, six `*Modifier` fields, saveBonus(STR), dexSaveBonus, wisSaveBonus, inspiration, superiority, spellSlots, items, basic, List<string> actionIds. Config actorTemplates/partyActions/reactions are JSON public fields; partyEnemyHealth defaults to 120. Existing Fighter uses config.basicRange/basicDamage; other roles use template.basic. Fighter actions attack/shove; Rogue sneak requires rear positioning against the vector from target.x/z to target.lockedX/Z (locked fields are world aim coordinates), advantage plus conditional damage; Mage control is enemy WIS Save vs INT DC, one held/spent Slot, restrained status 2.5 simulation seconds. Mage basic cantrip free/background D20. Environmental ResolveIncoming(... savingThrow:true) uses defender DEX in P1; the P0 path retains its existing STR example.

Control chooses a small explicit concentration rule: actual damage/death removes the owner's current matching concentration statuses in the same transaction; no extra CON roll. StatusState.concentrationId links to queue execution ID and sourceId links to caster. Expiry clears caster.concentrationId. Zero damage from Shield does not break it. Control cancels the old enemy windup and sets aiRemaining=0; Runtime must skip movement/AI execution while restrained is active, using the one status timer.

ActorState keeps x/z/heightY/lockedX/Z, adds role/formation, six independent modifiers, aiTargetId/aiMoveX/aiMoveZ/aiDecisionRemaining/aiPolicyId. AI owns its movement-intent data; Tick does not advance aiDecisionRemaining. Existing heldBy is Major only; reactionHeldBy is independent window ID. heldSpellSlots aggregates unpaid Major and Reaction; heldSuperiority reserves Parry cost. Runtime must reread DTO after successful Execute.

State.commandActorId is independent of activeActorId. SwitchActive clears prior active aim preparation and guard, without refunding Bonus or moving any queue/roll ownership. SelectCommandActor is Focus only, no pending roll/active Reaction. State.IsPartyP1 identifies explicit profile.

Use `CommandAuthority.AllyAI` (flag=true compatibility accepted) for BasicAttack/StartGuard/StartDodge/CancelAim/EndGuard. AI can act during Focus/PostRoll/ReactionPrompt at simulation speed, with normal actor readiness checks. No AI Confirm, edit, scarce resource or Reaction commands.

Reaction API:

- `State.reactionWindows`: List<ReactionWindow> fields id,incomingId,defenderId,stage,reactionId,ticketId,remaining,attempted,resourcesSpent,spec. Offered windows can be enumerated. State.activeReaction is computed from activeReactionWindowId.
- stages: Offered / Choosing / Rolling / AwaitDecision / Resolved / Declined. QuoteReaction(windowId,defenderId,"shield" or "parry") is pure and returns legal/reason/requiresD20/incomingTotal/currentArmorClass/afterArmorClass/spellCost/superiorityCost/prospectiveDamage/bonus/targetNumber/successProbability.
- Execute BeginReaction with reactionWindowId, reactionId, actorId=defenderId. Enters ReactionPrompt (Focus scale). Shield: CommitReaction/DeclineReaction directly; no roll. Parry: ConfirmReactionRoll -> RollFreeze; RevealRoll -> PostRollDecision. Shared Reroll/Modify/Commit/Cancel route by current ticket.purpose. Precision stays Fighter Tactical Attack only.
- RollTicket.purpose: "Tactical" / "Reaction"; reactionWindowId, incomingId, chargePolicy: "SharedCharge" / "None". Reaction chargeSpent=0.
- CombatResult.deferred=true means damage=0, prospectiveDamage describes queued damage; HP authoritative in DTO. damageApplied/disposition/incomingId/reactionWindowId tell UI actual finalization. Do not deduct HP in Runtime.
- ResolveIncoming retains existing signature with trailing optional bool parryable=true. Geometry still supplied by runtime. Reaction triggers only enemy->party and qualifying hit; Parry also requires melee range. Incoming.original background result is recorded once, never rerolled by Shield or restoring context.
- Normal PostRoll may be suspended once in State.decisionResume; it retains Major/long-term holds and result. Commit/Cancel of Reaction restores it. No nested active Reaction. Windows not selected expire by simulation time; no expiry in Freeze/Pause. Tick may atomically finalize expiration and return 0 on persistence failure, retaining pre-tick state.
- Config.reactionOfferSeconds=.8, reactionDecisionSeconds=3 (simulation seconds), reactionRecovery=6. LastTickError reports failed deadline publication and is cleared at the next Tick. Pure simulation without open Reaction windows uses the existing Tick path; it does not serialize/write every frame.
- `FinalizeIncoming` is a System command for already-ready events; cannot skip an active decision. EndEncounter refuses while incoming damage/decision remains.

P1 profile schema3/content actioncrpg-v0.5.1-p1/rules v051-p1-1. P1 runtime uses an independent path (root-selected CombatV051/P1/party-campaign.json); Load(...,"p1-party") rejects schema2 before backup recovery/write. No legacy import or implicit rewrite. Fresh P0 regression states still use explicit p0-single schema2 and independent test directories.

Stable test fixture, not normal gameplay: seed19 backgroundCombat's first D20 is12. Set enemy attackBonus0 and defender AC10 before contact, in melee range with no guard/dodge. Real ResolveIncoming then offers Mage Shield or Fighter Parry. It creates and persists the event normally; tests never inject a resolved window or replace the natural face afterward.
