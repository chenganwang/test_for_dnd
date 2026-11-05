using System;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public abstract class PartyTestFixture
    {
        protected CombatSession session;protected int sequence;
        protected CombatState S {get{return session.State;}}
        [SetUp] public virtual void Setup()
        {
            var config=new CombatConfig{passiveMomentumPerSecond=0};session=new CombatSession(CombatState.CreatePartyP1("party-tests","party-run",42,config),config);sequence=0;
            S.Actor("fighter").x=0;S.Actor("fighter").z=0;S.Actor("mage").x=0;S.Actor("mage").z=1;S.Actor("rogue").x=1;S.Actor("rogue").z=-1;S.Actor("enemy-1").x=1;S.Actor("enemy-1").z=0;S.Actor("enemy-1").lockedZ=1;
        }
        protected CombatCommand C(CommandKind kind,string actor="fighter"){return new CombatCommand{id="p1-cmd-"+(++sequence),kind=kind,actorId=actor};}
        protected CombatResult Run(CommandKind kind,string actor="fighter",Action<CombatCommand> fill=null)
        {var c=C(kind,actor);if(fill!=null)fill(c);var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        protected static RngState BackgroundFor(int natural)
        {
            for(ulong seed=1;seed<10000;seed++){var r=RandomStreams.Create(seed).backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(r))==natural)return r;}throw new Exception("seed not found");
        }
        protected ReactionWindow Offer(string defender="fighter",int natural=12,int damage=4,string root=null)
        {
            S.Actor("enemy-1").attackBonus=0;S.Actor(defender).armorClass=10;S.random.backgroundCombat=BackgroundFor(natural);
            var r=session.ResolveIncoming("contact-"+(++sequence),"enemy-1",defender,damage,false,12,root??"incoming-root-"+sequence);
            Assert.That(r.success,Is.True,r.reason);Assert.That(r.deferred,Is.True,r.reason);Assert.That(r.damage,Is.Zero);return S.reactionWindows.Single(w=>w.id==r.reactionWindowId);
        }
        protected void Begin(ReactionWindow w,string reaction)
        {Run(CommandKind.BeginReaction,w.defenderId,c=>{c.reactionWindowId=w.id;c.reactionId=reaction;});}
        protected string Queue(string actor,string action)
        {Run(CommandKind.ConfirmRoll,actor,c=>c.actionId=action);Run(CommandKind.RevealRoll);return Run(CommandKind.Commit).queueId;}
    }
    public sealed class PartyRuleTests : PartyTestFixture
    {
        [Test] public void PartyTemplatesAreIndependentAndP0CreationStaysSingleFighter()
        {
            Assert.That(S.schemaVersion,Is.EqualTo(3));Assert.That(S.actors.Select(a=>a.role),Is.EquivalentTo(new[]{ActorRole.Fighter,ActorRole.Rogue,ActorRole.Mage}));
            Assert.That(S.actors.Select(a=>a.maxHp).Distinct().Count(),Is.EqualTo(3));S.Actor("mage").hp--;Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));
            Assert.That(CombatState.Create("p","m",1).actors.Count,Is.EqualTo(1));Assert.That(S.Actor("enemy-1").hp,Is.EqualTo(session.Config.partyEnemyHealth));
        }
        [Test] public void P0ProfileRejectsPartyOnlyActionsAndReactionCommands()
        {
            var p0=new CombatSession(CombatState.Create("p0","p0",1),new CombatConfig());Assert.That(p0.Quote("fighter","enemy-1","control").legal,Is.False);Assert.That(p0.Execute(new CombatCommand{id="reaction",kind=CommandKind.BeginReaction}).success,Is.False);Assert.That(p0.State.schemaVersion,Is.EqualTo(2));
        }
        [Test] public void SwitchActivePreservesCommandSelectionTicketAndCostButClearsOldHeldInputs()
        {
            Run(CommandKind.StartGuard);float bonus=S.Actor("fighter").bonusRemaining;Assert.That(session.SwitchActive("switch-m","mage").success);Assert.That(S.Actor("fighter").guardRemaining,Is.Zero);Assert.That(S.Actor("fighter").bonusRemaining,Is.EqualTo(bonus));Assert.That(S.commandActorId,Is.EqualTo("fighter"));
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll,"mage",c=>c.actionId="control");Run(CommandKind.RevealRoll);string ticket=S.pendingRollId;int charge=S.charges;
            Assert.That(session.SwitchActive("switch-r","rogue").success);Assert.That(S.commandActorId,Is.EqualTo("fighter"));Assert.That(S.pendingRoll.actorId,Is.EqualTo("mage"));Assert.That(S.pendingRollId,Is.EqualTo(ticket));Assert.That(S.charges,Is.EqualTo(charge));
            Assert.That(session.SelectCommandActor("select-r","rogue").success,Is.False);
        }
        [Test] public void FocusCommandSelectionDoesNotSwitchActiveAndFrozenSwitchDoesNotMutate()
        {
            Run(CommandKind.EnterFocus);Assert.That(session.SelectCommandActor("select","mage").success);Assert.That(S.activeActorId,Is.EqualTo("fighter"));Run(CommandKind.ConfirmRoll,"mage",c=>c.actionId="control");string before=CombatJson.Write(S);Assert.That(session.SwitchActive("frozen-switch","rogue").success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void SharedTwoChargesQueueDifferentActorsWithoutStealingEitherMajor()
        {
            S.charges=2;Run(CommandKind.EnterFocus);string a=Queue("fighter","attack");int charge=S.charges;var denied=session.Execute(C(CommandKind.ConfirmRoll));Assert.That(denied.success,Is.False);Assert.That(S.charges,Is.EqualTo(charge));
            string b=Queue("mage","control");Assert.That(S.charges,Is.Zero);Assert.That(S.Actor("fighter").heldBy,Is.Not.EqualTo(S.Actor("mage").heldBy));Run(CommandKind.ExitFocus);
            Run(CommandKind.StartQueued,fill:c=>c.queueId=a);Run(CommandKind.StartQueued,fill:c=>c.queueId=b);Assert.That(S.queue.Count(q=>q.stage=="Executing"),Is.EqualTo(2));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));session.Tick(1);
            Run(CommandKind.ResolveQueued,fill:c=>c.queueId=a);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=b);Assert.That(S.Actor("fighter").heldBy,Is.Null);Assert.That(S.Actor("mage").heldBy,Is.Null);
        }
        [TestCase(CommandKind.ConfirmRoll)] [TestCase(CommandKind.Modify)] [TestCase(CommandKind.Reroll)] [TestCase(CommandKind.Precision)] [TestCase(CommandKind.BeginReaction)] [TestCase(CommandKind.CommitReaction)]
        public void AllyAIForbiddenResourceCommandsRejectAtomically(CommandKind kind)
        {var c=C(kind,"mage");c.authority=CommandAuthority.AllyAI;string before=CombatJson.Write(S);Assert.That(session.Execute(c).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void AllyCantripUsesBackgroundD20OwnMajorAndNoSlotOrChargeInFocus()
        {
            Run(CommandKind.EnterFocus);S.Actor("mage").x=-5;S.Actor("mage").z=0;ulong visible=S.random.visibleTactical.drawCount,background=S.random.backgroundCombat.drawCount;int slots=S.Actor("mage").spellSlots,charge=S.charges;
            Run(CommandKind.BasicAttack,"mage",c=>c.authority=CommandAuthority.AllyAI);Assert.That(S.random.backgroundCombat.drawCount,Is.GreaterThan(background));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(slots));Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.Actor("mage").majorRemaining,Is.GreaterThan(0));Assert.That(S.Actor("fighter").majorRemaining,Is.Zero);
        }
        [Test] public void AIPlayerAndEnemyAuthorityCannotControlTheWrongTeamOrStealAQueuedMajor()
        {
            var c=C(CommandKind.BasicAttack,"enemy-1");c.targetId="fighter";c.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(c).success,Is.False);c.id="player-enemy";c.authority=CommandAuthority.Player;Assert.That(session.Execute(c).success,Is.False);
            Run(CommandKind.EnterFocus);Queue("fighter","attack");c=C(CommandKind.BasicAttack);c.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(c).success,Is.False);
        }
        [Test] public void RoleSpecificActionAndSneakRearConditionRejectBeforeSpending()
        {
            Run(CommandKind.EnterFocus);int charge=S.charges;var c=C(CommandKind.ConfirmRoll,"fighter");c.actionId="control";Assert.That(session.Execute(c).success,Is.False);
            S.Actor("rogue").z=1;c=C(CommandKind.ConfirmRoll,"rogue");c.actionId="sneak";Assert.That(session.Execute(c).success,Is.False);Assert.That(S.charges,Is.EqualTo(charge));
            S.Actor("rogue").z=-1;var quote=session.Quote("rogue","enemy-1","sneak");Assert.That(quote.legal&&quote.advantage,Is.True);Run(CommandKind.ConfirmRoll,"rogue",x=>x.actionId="sneak");Assert.That(S.pendingRoll.rawFaces.Count,Is.EqualTo(2));
        }
        [Test] public void RearPositionUsesWorldAimPointAndIsInvariantUnderTranslation()
        {
            S.Actor("enemy-1").lockedX=1;S.Actor("enemy-1").lockedZ=3;var before=session.Quote("rogue","enemy-1","sneak");Assert.That(before.legal&&before.advantage);
            foreach(var a in S.actors.Concat(S.enemies)){a.x+=20;a.z-=30;a.lockedX+=20;a.lockedZ-=30;}
            var after=session.Quote("rogue","enemy-1","sneak");Assert.That(after.legal&&after.advantage);Assert.That(after.successProbability,Is.EqualTo(before.successProbability));
        }
        [Test] public void ChoosingReactionQuoteRecognizesItsOwnHoldWithoutChangingState()
        {var w=Offer("mage");Begin(w,"shield");string before=CombatJson.Write(S);var q=session.QuoteReaction(w.id,"mage","shield");Assert.That(q.legal,q.reason);Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void EnvironmentalIncomingSaveUsesDexterityRatherThanStrengthInP1()
        {S.Actor("rogue").saveBonus=-20;S.Actor("rogue").dexSaveBonus=5;S.random.backgroundCombat=BackgroundFor(10);var r=session.ResolveIncoming("ground-fire","enemy-1","rogue",6,true,12);Assert.That(r.success);Assert.That(r.total,Is.EqualTo(15));Assert.That(r.damage,Is.EqualTo(3));}
        [Test] public void SneakDoesConditionalDamageAndMageControlUsesEnemyWisdomSaveAndSlotAtStartup()
        {
            S.charges=2;S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.EnterFocus);string sneak=Queue("rogue","sneak");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=sneak);session.Tick(1);int hp=S.Actor("enemy-1").hp;var hit=Run(CommandKind.ResolveQueued,fill:c=>c.queueId=sneak);Assert.That(hit.damage,Is.EqualTo(14));Assert.That(S.Actor("enemy-1").hp,Is.EqualTo(hp-14));
            S.random.visibleTactical=CombatRuleTests.StreamFor(1);S.Actor("enemy-1").wisSaveBonus=-5;Run(CommandKind.EnterFocus);string control=Queue("mage","control");Assert.That(S.rolls.Last().rollOwnerId,Is.EqualTo("enemy-1"));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(2));Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=control);Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=control);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="restrained"&&x.remaining>0));
        }
        [Test] public void ShieldDefersDamageUsesOriginalAttackNoD20AndCostsOnceAtCommit()
        {
            var w=Offer("mage");int hp=S.Actor("mage").hp,slots=S.Actor("mage").spellSlots,charge=S.charges;ulong bg=S.random.backgroundCombat.drawCount,visible=S.random.visibleTactical.drawCount;
            var quote=session.QuoteReaction(w.id,"mage","shield");Assert.That(quote.legal);Assert.That(quote.currentArmorClass,Is.EqualTo(10));Assert.That(quote.incomingTotal,Is.EqualTo(12));Assert.That(quote.afterArmorClass,Is.EqualTo(15));Begin(w,"shield");Assert.That(S.Actor("mage").hp,Is.EqualTo(hp));Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(1));Assert.That(S.pendingRoll,Is.Null);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));
            var c=C(CommandKind.CommitReaction,"mage");c.reactionWindowId=w.id;var r=session.Execute(c);Assert.That(r.success,r.reason);Assert.That(r.damageApplied&&!r.deferred);Assert.That(r.damage,Is.Zero);Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(slots-1));Assert.That(S.Actor("mage").reactionRemaining,Is.GreaterThan(0));Assert.That(session.Execute(c).duplicate);Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(bg));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.Actor("mage").hp,Is.EqualTo(hp));
        }
        [TestCase(20)] [TestCase(19)]
        public void ShieldIsNotOfferedWhenACBonusCannotChangeAttack(int natural)
        {S.Actor("fighter").reactionRemaining=8;S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;S.random.backgroundCombat=BackgroundFor(natural);var r=session.ResolveIncoming("attack","enemy-1","mage",1);Assert.That(r.success);Assert.That(r.deferred,Is.False);Assert.That(S.reactionWindows,Is.Empty);}
        [Test] public void SaveDamageDoesNotOfferShieldAndDodgeSkipsBothRollAndReaction()
        {
            ulong background=S.random.backgroundCombat.drawCount;Run(CommandKind.StartDodge,"mage");var r=session.ResolveIncoming("dodge","enemy-1","mage",3);Assert.That(r.success);Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(background));Assert.That(S.reactionWindows,Is.Empty);
            session.Tick(1);r=session.ResolveIncoming("save","enemy-1","mage",3,true);Assert.That(r.success&&!r.deferred);Assert.That(S.reactionWindows,Is.Empty);
        }
        [Test] public void ParryHasOwnD20ZeroChargeAndCanCoexistWithSuspendedPaidMajor()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);string paid=S.pendingRollId,raw=CombatJson.Write(S.pendingRoll.rawFaces),held=S.Actor("fighter").heldBy;int charge=S.charges;
            var w=Offer();ulong bg=S.random.backgroundCombat.drawCount;Begin(w,"parry");Assert.That(S.rolls.Find(t=>t.id==paid).stage,Is.EqualTo("SuspendedAwaitDecision"));Assert.That(S.Actor("fighter").heldBy,Is.EqualTo(held));Assert.That(S.Actor("fighter").reactionHeldBy,Is.EqualTo(w.id));
            S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll,fill:c=>c.reactionWindowId=w.id);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.pendingRoll.chargeSpent,Is.Zero);Assert.That(S.pendingRoll.purpose,Is.EqualTo("Reaction"));Assert.That(session.Tick(2),Is.Zero);Run(CommandKind.RevealRoll);Run(CommandKind.Commit);
            Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(S.pendingRollId,Is.EqualTo(paid));Assert.That(CombatJson.Write(S.pendingRoll.rawFaces),Is.EqualTo(raw));Assert.That(S.Actor("fighter").heldBy,Is.EqualTo(held));Assert.That(S.Actor("fighter").reactionHeldBy,Is.Null);Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(bg));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));
        }
        [Test] public void ParryRerollAndModifySpendOnlyOwnInspirationAndCancelDoesNotReopenWindow()
        {
            var w=Offer();Begin(w,"parry");Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);ulong before=S.random.visibleTactical.drawCount;int charge=S.charges;
            Run(CommandKind.Modify);Run(CommandKind.Reroll);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.random.visibleTactical.drawCount,Is.GreaterThan(before));Run(CommandKind.RevealRoll);Assert.That(session.Execute(C(CommandKind.Precision)).success,Is.False);Run(CommandKind.Cancel);
            Assert.That(S.Actor("fighter").inspiration,Is.Zero);Assert.That(S.Actor("fighter").superiority,Is.EqualTo(2));Assert.That(S.Actor("fighter").reactionRemaining,Is.GreaterThan(0));Assert.That(S.charges,Is.EqualTo(charge));var c=C(CommandKind.BeginReaction);c.reactionWindowId=w.id;c.reactionId="parry";Assert.That(session.Execute(c).success,Is.False);Assert.That(S.Actor("fighter").hp,Is.EqualTo(26));
        }
        [Test] public void ReactionCannotDoubleReserveTheLastSpellSlotAlreadyHeldByMajor()
        {
            S.Actor("fighter").reactionRemaining=8;S.Actor("mage").spellSlots=1;Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll,"mage",c=>c.actionId="control");Run(CommandKind.RevealRoll);S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;S.random.backgroundCombat=BackgroundFor(12);var r=session.ResolveIncoming("held-slot","enemy-1","mage",2);Assert.That(r.success&&!r.deferred);Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(1));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));
        }
        [Test] public void ShieldAndControlHoldTwoSeparateSlotsThenReleaseOnlyTheirOwnCost()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll,"mage",c=>c.actionId="control");Run(CommandKind.RevealRoll);string original=S.pendingRollId;var w=Offer("mage");Begin(w,"shield");Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(2));
            Run(CommandKind.CommitReaction,"mage");Assert.That(S.pendingRollId,Is.EqualTo(original));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(1));Run(CommandKind.Cancel);Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));Assert.That(S.Actor("mage").heldSpellSlots,Is.Zero);
        }
        [Test] public void ExecutingMajorCanResolveDuringReactionWithoutClearingReactionHold()
        {
            Run(CommandKind.EnterFocus);string queue=Queue("fighter","attack");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=queue);session.Tick(.2f);var w=Offer();Begin(w,"parry");
            session.Tick(2);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=queue);Assert.That(S.Actor("fighter").heldBy,Is.Null);Assert.That(S.Actor("fighter").reactionHeldBy,Is.EqualTo(w.id));Run(CommandKind.DeclineReaction);Assert.That(S.Actor("fighter").reactionHeldBy,Is.Null);Assert.That(S.queue.Single().stage,Is.EqualTo("Resolved"));
        }
        [Test] public void BoundedReactionBacklogNeverDropsDamageOrReordersSameDefender()
        {
            session.Config.maxPendingIncoming=1;Offer();S.random.backgroundCombat=BackgroundFor(12);var later=session.ResolveIncoming("overflow","enemy-1","fighter",5);Assert.That(later.success&&later.deferred);Assert.That(S.reactionWindows.Count,Is.EqualTo(2));Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));session.Tick(6);session.Tick(6);Assert.That(S.Actor("fighter").hp,Is.EqualTo(21));Assert.That(S.incomingEvents.All(e=>e.damageApplied));
        }
        [Test] public void PaidParryTimeoutAcceptsCurrentResultAndRetainsPaidInspiration()
        {
            Run(CommandKind.SetReactionSettings,fill:c=>c.reactionSettings=new ReactionSettings{timeoutAcceptPaidRoll=true});
            var w=Offer();Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);Run(CommandKind.Modify);session.Tick(6);Assert.That(S.activeReaction,Is.Null);Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));Assert.That(S.Actor("fighter").inspiration,Is.EqualTo(1));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);
        }
        [Test] public void OfferedTimeoutAppliesDamageOnceWithNoRngRedrawAndNoFreeRecovery()
        {
            var w=Offer();int hp=S.Actor("fighter").hp;ulong draws=S.random.backgroundCombat.drawCount;session.Tick(session.Config.reactionOfferSeconds+.01f);Assert.That(S.Actor("fighter").hp,Is.EqualTo(hp-4));Assert.That(S.reactionWindows.Find(x=>x.id==w.id).stage,Is.EqualTo("Declined"));session.Tick(1);Assert.That(S.Actor("fighter").hp,Is.EqualTo(hp-4));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));
        }
        [Test] public void MultipleDefendersHaveDistinctEventsButRepeatedRootDefenderCannotRollAgain()
        {
            var fighter=Offer("fighter",12,2,"aoe");var mage=Offer("mage",12,2,"aoe");Assert.That(fighter.incomingId,Is.Not.EqualTo(mage.incomingId));ulong draws=S.random.backgroundCombat.drawCount;Assert.That(session.ResolveIncoming("duplicate","enemy-1","fighter",2,false,12,"aoe").success,Is.False);Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            Begin(fighter,"parry");var c=C(CommandKind.BeginReaction,"mage");c.reactionWindowId=mage.id;c.reactionId="shield";Assert.That(session.Execute(c).success);Assert.That(S.activeReaction.id,Is.EqualTo(mage.id));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);
        }
        [Test] public void SameDefenderDamageWaitsForEarlierReactionAndAttackerDeathDoesNotEraseContact()
        {
            var w=Offer("fighter",12,4);S.random.backgroundCombat=BackgroundFor(12);var second=session.ResolveIncoming("later","enemy-1","fighter",5);Assert.That(second.success&&second.deferred);Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));S.Actor("enemy-1").hp=0;session.Tick(6);session.Tick(6);Assert.That(S.Actor("fighter").hp,Is.EqualTo(21));Assert.That(S.incomingEvents.All(e=>e.damageApplied));
        }
        [Test] public void PendingIncomingPreventsPrematureVictoryAndDeadDefenderCancelsLaterWindows()
        {
            var w=Offer("fighter",12,100);Offer("fighter",12,4);S.Actor("enemy-1").hp=0;Assert.That(session.Execute(C(CommandKind.EndEncounter)).success,Is.False);session.Tick(6);Assert.That(S.Actor("fighter").hp,Is.Zero);Assert.That(S.reactionWindows.All(x=>x.stage=="Declined"));Assert.That(S.incomingEvents.All(e=>e.damageApplied));Assert.That(S.incomingEvents.Last().stage,Is.EqualTo("Canceled"));
        }
        [Test] public void OfferedWindowsAndCooldownsFreezeAndPauseWithoutExpiring()
        {
            var w=Offer();Begin(w,"parry");Run(CommandKind.ConfirmReactionRoll);float remaining=S.activeReaction.remaining;float major=S.Actor("fighter").majorRemaining;session.Tick(10);Assert.That(S.activeReaction.remaining,Is.EqualTo(remaining));Assert.That(S.Actor("fighter").majorRemaining,Is.EqualTo(major));Run(CommandKind.Pause);string before=CombatJson.Write(S);session.Tick(10);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Run(CommandKind.Resume);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));
        }
        void ApplyControl()
        {
            S.random.visibleTactical=CombatRuleTests.StreamFor(1);S.Actor("enemy-1").wisSaveBonus=-5;Run(CommandKind.EnterFocus);string q=Queue("mage","control");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=q);session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=q);
            Assert.That(S.Actor("mage").concentrationId,Is.Not.Null);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="restrained"));Assert.That(S.Actor("enemy-1").aiRemaining,Is.Zero);
        }
        [TestCase(false)] [TestCase(true)] public void ActualCasterDamageOrDeathBreaksOnlyItsCurrentConcentrationInSameTransaction(bool lethal)
        {
            ApplyControl();S.Actor("enemy-1").statuses.Add(new StatusState{id="unrelated",sourceId="other",remaining=5});S.Actor("mage").dexSaveBonus=-50;
            var r=session.ResolveIncoming("break-control","enemy-1","mage",lethal?100:1,true);Assert.That(r.success&&r.damageApplied);Assert.That(S.Actor("mage").concentrationId,Is.Null);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="restrained"),Is.False);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="unrelated"));
        }
        [Test] public void ControlExpiryClearsCasterConcentrationAndShieldAvoidedDamageDoesNotBreakIt()
        {
            ApplyControl();var w=Offer("mage");Begin(w,"shield");Run(CommandKind.CommitReaction,"mage");Assert.That(S.Actor("mage").concentrationId,Is.Not.Null);session.Tick(3);Assert.That(S.Actor("mage").concentrationId,Is.Null);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="restrained"),Is.False);
        }
    }
}
