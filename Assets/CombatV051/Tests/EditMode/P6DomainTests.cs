using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // All coordinates, enemy HP and requested raw-seed searches below are explicit
    // pure-rule fixtures. Attacks/casts/payment/rolls still use real commands and RNG.
    public sealed class P6DomainTests
    {
        const string Paladin="char_demo_paladin";
        CombatSession session;int sequence;
        CombatState S=>session.State;
        ActorState P=>S.Actor(Paladin);
        ActorState Enemy=>S.enemies[0];
        CombatConfig config;
        [SetUp] public void Setup(){Make(SeedFor(14));}
        static ulong SeedFor(int raw)
        {for(ulong seed=1;seed<10000;seed++)if(RandomStreams.D20(RandomStreams.Create(seed).visibleTactical)==raw)return seed;throw new Exception("seed_not_found");}
        void Make(ulong seed,bool p5=false,Action<CombatState> before=null,Action<CombatState> sink=null)
        {
            config=new CombatConfig{p6=new P6Config(),p5=p5?new P5Config():null};var s=CombatState.CreatePartyForPreset("p6-domain","run-1",seed,config);
            foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=1000;s.enemies[0].armorClass=14;
            before?.Invoke(s);session=new CombatSession(s,config,persistenceSink:sink);sequence=0;
        }
        CombatCommand C(CommandKind kind,string actor=Paladin,string target=null)
        {return new CombatCommand{id="p6-rule/"+(++sequence),kind=kind,actorId=actor,targetId=target??Enemy.id,actionId=CombatSession.P6SmiteId};}
        CombatResult Run(CombatCommand c){var result=session.Execute(c);Assert.That(result.success,Is.True,result.reason);return result;}
        CombatResult Run(CommandKind k)=>Run(C(k));
        RollTicket Confirm(bool direct=false)
        {if(!direct)Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);c.p6=new P6CommandData{preacceptBaseResult=direct};Run(c);return S.pendingRoll;}
        RollTicket Reveal(bool direct=false){var ticket=Confirm(direct);Run(CommandKind.RevealRoll);return S.rolls.Find(t=>t.id==ticket.id);}
        QueuedAction Start(bool full=false)
        {
            if(full){var select=C(CommandKind.SelectSmiteOvercharge);select.flag=true;Run(select);}Run(CommandKind.Commit);Run(CommandKind.ExitFocus);var q=S.queue.Last();var c=C(CommandKind.StartQueued);c.queueId=q.id;Run(c);return S.queue.Last();
        }
        CombatResult Contact(string queue)
        {session.Tick(.36f);var c=C(CommandKind.ResolveQueued);c.queueId=queue;c.authority=CommandAuthority.System;return Run(c);}
        P6BindResult Bind(bool savePassed=false)
        {
            Enemy.wisSaveBonus=savePassed?100:-100;var c=C(CommandKind.RequestBindRadiance,"mage");Run(c);session.Tick(.76f);
            var finish=C(CommandKind.ResolveBindRadiance,"mage");finish.rootId=c.id;finish.authority=CommandAuthority.System;return Run(finish).p6.bind;
        }
        [Test] public void PresetIdentityAndActionLookupPreserveOriginalFighterAndActionLists()
        {
            Assert.That((int)ActorRole.Enemy,Is.EqualTo(3));Assert.That((int)ActorRole.Paladin,Is.EqualTo(4));Assert.That(S.actors.Select(a=>a.id),Is.EquivalentTo(new[]{Paladin,"mage","rogue"}));
            Assert.That(P.p6.classId,Is.EqualTo("class_paladin"));Assert.That(config.Action(CombatSession.P6SmiteId).spellCost,Is.Zero);Assert.That(config.Template(ActorRole.Fighter).role,Is.EqualTo(ActorRole.Fighter));Assert.That(config.actions.Any(x=>x.id==CombatSession.P6SmiteId),Is.False);
            var legacy=new CombatConfig();Assert.That(CombatJson.Write(legacy),Does.Not.Contain("\"p6\""));Assert.That(legacy.Action(CombatSession.P6SmiteId),Is.Null);
        }
        [TestCase(1,3,false,false)][TestCase(19,100,false,false)][TestCase(19,14,true,true)][TestCase(20,100,true,true)]
        public void ActualRawDiceDetermineHitAndExpandedCriticalSeparately(int raw,int ac,bool hit,bool critical)
        {Make(SeedFor(raw));Enemy.armorClass=ac;var t=Confirm();Assert.That(t.selectedFace,Is.EqualTo(raw));Assert.That(t.hit,Is.EqualTo(hit));Assert.That(t.critical,Is.EqualTo(critical));}
        [Test] public void FixedPlusTwoNeverChangesRawEighteenIntoNaturalTwentyOrCritical()
        {Make(SeedFor(18));Reveal();Run(CommandKind.Modify);Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(18));Assert.That(S.pendingRoll.total,Is.EqualTo(25));Assert.That(S.pendingRoll.critical,Is.False);Assert.That(P.inspiration,Is.EqualTo(1));}
        [Test] public void ConfirmSpendsOneAndHoldsOwnerButNoSpellSlotOrDamageDice()
        {
            int slots=P.spellSlots;ulong damage=S.p6.damageRandom.drawCount;var c=C(CommandKind.EnterFocus);Run(c);c=C(CommandKind.ConfirmRoll);var first=Run(c);
            Assert.That(S.charges,Is.EqualTo(1));Assert.That(P.spellSlots,Is.EqualTo(slots));Assert.That(P.heldBy,Is.EqualTo(first.ticketId));Assert.That(S.p6.damageRandom.drawCount,Is.EqualTo(damage));
            string before=CombatJson.Write(S);var duplicate=Run(c);Assert.That(duplicate.duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void UnpaidSelectionAndIllegalConfirmLeaveResourcesAndAllRngUnchanged()
        {
            Enemy.x=100;var before=CombatJson.Write(S);Assert.That(session.QuoteSmite(Paladin,Enemy.id).attack.legal,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Run(CommandKind.EnterFocus);before=CombatJson.Write(S);Assert.That(session.Execute(C(CommandKind.ConfirmRoll)).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void OverchargeReservesSharedInventoryAndOnlyLegalStartPaysSecondCharge()
        {
            Reveal();var c=C(CommandKind.SelectSmiteOvercharge);c.flag=true;Run(c);Assert.That(S.charges,Is.EqualTo(1));Assert.That(session.AvailableSharedCharges,Is.Zero);Run(CommandKind.Commit);
            var rogue=C(CommandKind.ConfirmRoll,"rogue");rogue.actionId="sneak";Assert.That(session.Execute(rogue).reason,Is.EqualTo("no_charge"));
            Run(CommandKind.ExitFocus);c=C(CommandKind.StartQueued);c.queueId=S.queue.Last().id;Run(c);Assert.That(S.charges,Is.Zero);Assert.That(S.rolls.Last().p6.overchargePaid,Is.True);Assert.That(S.rolls.Last().p6.overchargeHeld,Is.False);
        }
        [Test] public void PreStartRangeLossReleasesOnlyOverchargeAndNeverRefundsBase()
        {
            Reveal();var select=C(CommandKind.SelectSmiteOvercharge);select.flag=true;Run(select);Run(CommandKind.Commit);Run(CommandKind.ExitFocus);Enemy.x=100;var c=C(CommandKind.StartQueued);c.queueId=S.queue.Last().id;Run(c);
            Assert.That(S.charges,Is.EqualTo(1));Assert.That(session.AvailableSharedCharges,Is.EqualTo(1));Assert.That(P.heldBy,Is.Null);Assert.That(S.rolls.Last().stage,Is.EqualTo("Canceled"));
        }
        [Test] public void StartedOverchargeLostTargetKeepsBothPaymentsAndDoesNotRefundMomentum()
        {
            Reveal();var q=Start(true);Enemy.x=100;session.Tick(.36f);float momentum=S.momentum;var c=C(CommandKind.ResolveQueued);c.queueId=q.id;c.authority=CommandAuthority.System;Run(c);Assert.That(S.charges,Is.Zero);Assert.That(S.momentum,Is.EqualTo(momentum));Assert.That(S.p6.resourceEvents,Is.Empty);
        }
        [Test] public void RealExecutedMissAwardsHalfBaseOnceWhileCancelAwardsNothing()
        {
            Make(SeedFor(1));Reveal();var q=Start();session.Tick(.36f);float prior=S.momentum;var c=C(CommandKind.ResolveQueued);c.queueId=q.id;c.authority=CommandAuthority.System;Run(c);Assert.That(S.momentum-prior,Is.EqualTo(50).Within(.0001));Assert.That(S.p6.resourceEvents.Single().kind,Is.EqualTo("executed-miss"));string done=CombatJson.Write(S);Run(c);Assert.That(CombatJson.Write(S),Is.EqualTo(done));
            Make(SeedFor(1));Reveal();Run(CommandKind.Cancel);Assert.That(S.p6.resourceEvents,Is.Empty);Assert.That(S.charges,Is.EqualTo(1));
        }
        [Test] public void LaterArmorChangeCanMissFullSmiteButReturnsOnlyHalfBase()
        {
            Reveal();var q=Start(true);Enemy.armorClass=100;session.Tick(.36f);float prior=S.momentum;var c=C(CommandKind.ResolveQueued);c.queueId=q.id;c.authority=CommandAuthority.System;var r=Run(c);Assert.That(r.hit,Is.False);Assert.That(S.momentum-prior,Is.EqualTo(50).Within(.0001));Assert.That(S.charges,Is.Zero);Assert.That(S.rolls.Last().p6.overchargePaid,Is.True);
        }
        [Test] public void CriticalDamageDoublesWeaponAndRadiantDiceButNeverStrength()
        {
            Make(SeedFor(20));Reveal();var q=Start(true);var r=Contact(q.id);var d=r.p6.damages.Single();Assert.That(d.weaponFaces.Count,Is.EqualTo(4));Assert.That(d.radiantFaces.Count,Is.EqualTo(10));Assert.That(d.totalDamage,Is.EqualTo(d.weaponFaces.Sum()+3+d.radiantFaces.Sum()));Assert.That(d.strengthBonus,Is.EqualTo(3));Assert.That(S.p6.damageRandom.drawCount,Is.EqualTo(14));
        }
        [TestCase(true)][TestCase(false)] public void CompletedBindReliablyMarksRegardlessOfWisdomSave(bool pass)
        {
            string active=S.activeActorId;int charges=S.charges,slots=S.Actor("mage").spellSlots;var record=Bind(pass);Assert.That(record.savePassed,Is.EqualTo(pass));Assert.That(record.markApplied,Is.True);Assert.That(Enemy.statuses.Any(x=>x.id=="p6-radiant-mark"),Is.True);Assert.That(Enemy.statuses.Any(x=>x.id=="p6-root"),Is.EqualTo(!pass));Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(slots));Assert.That(S.activeActorId,Is.EqualTo(active));Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));
        }
        [TestCase("guard",1.2f,0)][TestCase("elite",.6f,0)][TestCase("boss",2f,.65f)]
        public void BindingControlUsesShortRootOrBossSlowWithoutCancelingAttack(string template,float seconds,float movement)
        {
            S.world.strings["enemyTemplate/"+Enemy.id]=template;Enemy.aiState=1;Enemy.attackGroupId="committed";var r=Bind();Assert.That(r.controlSeconds,Is.EqualTo(seconds));Assert.That(session.P6MovementMultiplier(Enemy.id),Is.EqualTo(movement));Assert.That(Enemy.aiState,Is.EqualTo(1));Assert.That(Enemy.attackGroupId,Is.EqualTo("committed"));
        }
        [Test] public void MageCannotBeRobbedByAiDuringCastAndRefusesOverlappingRequests()
        {
            var request=C(CommandKind.RequestBindRadiance,"mage");Run(request);var ai=C(CommandKind.StartBasicAttack,"mage");ai.authority=CommandAuthority.AllyAI;ai.flag=true;Assert.That(session.Execute(ai).success,Is.False);Assert.That(session.Execute(C(CommandKind.RequestBindRadiance,"mage")).success,Is.False);
            session.Tick(.8f);Assert.That(session.Execute(ai).success,Is.False,"Even at cast deadline the unresolved intent owns its action.");
        }
        [Test] public void OneBindRequestWaitsForActualBasicContactThenTakesOverRecovery()
        {
            var c=C(CommandKind.StartBasicAttack,"mage");c.actionId="cantrip";Run(c);float basicRemaining=S.Actor("mage").basicAttackIntent.remaining;
            Run(C(CommandKind.RequestBindRadiance,"mage"));Assert.That(S.Actor("mage").p6.bindIntent.stage,Is.EqualTo("WaitingBasic"));Assert.That(S.Actor("mage").basicAttackIntent.remaining,Is.EqualTo(basicRemaining));Assert.That(S.Actor("mage").basicAttackIntent.resolved,Is.False);
            session.Tick(.2f);Assert.That(S.Actor("mage").p6.bindIntent.stage,Is.EqualTo("WaitingBasic"));var contact=C(CommandKind.ResolveBasicAttack,"mage");contact.orderId=c.id;contact.authority=CommandAuthority.System;Run(contact);
            Assert.That(S.Actor("mage").basicAttackIntent.resolved,Is.True);Assert.That(S.Actor("mage").p6.bindIntent.stage,Is.EqualTo("Casting"));Assert.That(S.Actor("mage").p6.bindIntent.remaining,Is.EqualTo(.75f));Assert.That(S.incomingEvents.Count,Is.EqualTo(1));
        }
        [Test] public void MarkAdvantageSnapshotsButExpiredMarkGivesNoDamageMultiplier()
        {
            Bind(true);var t=Reveal();Assert.That(t.advantage,Is.True);foreach(var status in Enemy.statuses.Where(x=>x.p6!=null))status.remaining=.01f;var q=Start();var d=Contact(q.id).p6.damages.Single();Assert.That(d.attackFaces.Count,Is.EqualTo(2));Assert.That(d.radiantMultiplier,Is.EqualTo(1));Assert.That(d.markConsumed,Is.False);Assert.That(S.p6.markRewardCount,Is.Zero);
        }
        [Test] public void PositiveRadiantConsumesMarkAndAwardsCooperationOnceWithActualTypedDamage()
        {
            Bind(true);Reveal();var q=Start();session.Tick(.36f);float prior=S.momentum;var c=C(CommandKind.ResolveQueued);c.queueId=q.id;c.authority=CommandAuthority.System;var d=Run(c).p6.damages.Single();Assert.That(d.radiantMultiplier,Is.EqualTo(1.5f));Assert.That(d.radiantBeforeMitigation,Is.EqualTo((int)Math.Floor(d.radiantFaces.Sum()*1.5)));Assert.That(d.radiantApplied,Is.GreaterThan(0));Assert.That(d.markConsumed,Is.True);Assert.That(Enemy.statuses.Any(x=>x.p6!=null),Is.False);Assert.That(S.momentum-prior,Is.EqualTo(20).Within(.001));Assert.That(S.focusBudget.p6.rewards.Count(x=>x.category=="combo"),Is.EqualTo(1));
            string after=CombatJson.Write(S);Run(c);Assert.That(CombatJson.Write(S),Is.EqualTo(after));
        }
        [TestCase(true)][TestCase(false)] public void ImmunityOrAbsorptionIsAHitNotMissAndDoesNotConsumeMark(bool immunity)
        {
            Bind(true);if(immunity)Enemy.p6.radiantDamageMultiplier=0;else Enemy.p6.absorptionShield=10000;Reveal();var q=Start();var d=Contact(q.id).p6.damages.Single();Assert.That(d.hit,Is.True);Assert.That(d.radiantApplied,Is.Zero);Assert.That(d.markConsumed,Is.False);Assert.That(Enemy.statuses.Any(x=>x.p6!=null),Is.True);Assert.That(S.p6.resourceEvents.Any(x=>x.kind=="executed-miss"),Is.False);Assert.That(S.p6.markRewardCount,Is.Zero);
        }
        [Test] public void SameMageReplacesOldMarkAndCasterDeathRemovesUnusedMark()
        {
            Bind(true);var other=CombatJson.Clone(Enemy);other.id="second";other.statuses.Clear();other.x=2;S.enemies.Add(other);S.Actor("mage").p6.bindCooldown=0;S.Actor("mage").majorRemaining=0;var c=C(CommandKind.RequestBindRadiance,"mage",other.id);Run(c);session.Tick(.76f);var resolve=C(CommandKind.ResolveBindRadiance,"mage");resolve.rootId=c.id;resolve.authority=CommandAuthority.System;Run(resolve);Assert.That(Enemy.statuses.Any(x=>x.p6!=null),Is.False);Assert.That(S.Actor(other.id).statuses.Any(x=>x.p6!=null),Is.True);S.Actor("mage").hp=0;session.Tick(.1f);Assert.That(S.enemies.SelectMany(x=>x.statuses).Any(x=>x.p6!=null),Is.False);
        }
        [Test] public void BasicGreatswordUsesActualFrontSweepOncePerRootTargetAndDoesNotGainMomentum()
        {
            S.charges=0;S.momentumLedger.disengaged=true;var front=CombatJson.Clone(Enemy);front.id="front";front.x=1;front.z=.2f;S.enemies.Add(front);var rear=CombatJson.Clone(Enemy);rear.id="rear";rear.x=-1;S.enemies.Add(rear);
            var start=C(CommandKind.StartBasicAttack);start.p6=new P6CommandData{directionX=1};Run(start);session.Tick(.13f);var c=C(CommandKind.ResolveBasicAttack);c.orderId=start.id;c.authority=CommandAuthority.System;c.p6=new P6CommandData{targets=S.enemies.Select(x=>new P6TargetEvidence{targetId=x.id}).ToList()};var r=Run(c);Assert.That(r.p6.damages.Count,Is.EqualTo(2));Assert.That(S.Actor("rear").hp,Is.EqualTo(1000));Assert.That(S.p6.resourceEvents,Is.Empty);Assert.That(S.momentum,Is.Zero);Assert.That(r.p6.damages.All(x=>x.radiantFaces.Count==0),Is.True);string done=CombatJson.Write(S);Run(c);Assert.That(CombatJson.Write(S),Is.EqualTo(done));
        }
        [Test] public void PassiveRechargeIsTenPerEffectiveSimulationSecondWithoutEncounterLifetimeCap()
        {
            for(int i=0;i<7;i++){S.charges=0;S.momentum=0;session.Tick(10);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.momentum,Is.Zero);}Assert.That(S.momentumLedger.passiveEarned,Is.EqualTo(700));
            S.charges=0;S.momentum=0;Run(CommandKind.EnterFocus);session.Tick(1);Assert.That(S.momentum,Is.EqualTo(1.2f).Within(.001));
        }
        [Test] public void FullCapacityDiscardsMomentumAndIsolatedPracticeStillNeedsEffectiveThreat()
        {
            S.momentum=0;session.Tick(100);Assert.That(S.charges,Is.EqualTo(2));Assert.That(S.momentum,Is.Zero);S.charges=0;S.momentumLedger.disengaged=true;session.Tick(100);Assert.That(S.momentum,Is.Zero);S.world.strings["context"]="TRAINING";S.p6.training=true;session.Tick(100);Assert.That(S.momentum,Is.Zero);S.momentumLedger.disengaged=false;session.Tick(10);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.focusBudget.p6.rewards,Is.Empty);
        }
        [Test] public void OrdinaryRealGuardGivesTwentyFiveWithFourSimulationSecondTeamInterval()
        {
            S.charges=0;Run(CommandKind.StartGuard);session.Tick(.4f);Enemy.attackBonus=100;float before=S.momentum;
            var c=C(CommandKind.IncomingAttack,Enemy.id,Paladin);c.authority=CommandAuthority.System;c.rootId="guard-1";c.qualified=true;c.amount=2;Run(c);Assert.That(S.momentum-before,Is.EqualTo(25).Within(.001));
            c=C(CommandKind.IncomingAttack,Enemy.id,Paladin);c.authority=CommandAuthority.System;c.rootId="guard-2";c.qualified=true;c.amount=2;before=S.momentum;Run(c);Assert.That(S.momentum,Is.EqualTo(before));Assert.That(S.focusBudget.p6.rewards.Count(x=>x.category=="defense"),Is.EqualTo(1));
        }
        [Test] public void ZeroDamageContactCannotFarmMomentumOrFocusThroughARealParry()
        {
            S.charges=0;Run(CommandKind.StartGuard);var command=C(CommandKind.IncomingAttack,Enemy.id,Paladin);command.authority=CommandAuthority.System;command.rootId="zero-damage-contact";command.qualified=true;command.amount=0;var result=Run(command);
            Assert.That(result.reason,Is.EqualTo("parried"));Assert.That(S.incomingEvents.Single().damageApplied,Is.True);Assert.That(S.p6.resourceEvents,Is.Empty);Assert.That(S.focusBudget.p6.rewards,Is.Empty);Assert.That(S.momentum,Is.Zero);Assert.That(S.charges,Is.Zero);
        }
        [Test] public void KnownMissDoesNotOfferOverchargeAndCancelReleasesReservation()
        {
            Make(SeedFor(1));Reveal();var c=C(CommandKind.SelectSmiteOvercharge);c.flag=true;Assert.That(session.Execute(c).reason,Is.EqualTo("overcharge_requires_hit"));
            Make(SeedFor(14));Reveal();c=C(CommandKind.SelectSmiteOvercharge);c.flag=true;Run(c);Run(CommandKind.Cancel);Assert.That(session.AvailableSharedCharges,Is.EqualTo(1));Assert.That(P.heldBy,Is.Null);
        }
        [Test] public void QueueDeadlineStartsAtCommitRatherThanConfirmAndFreezeNeverAgesIt()
        {
            Confirm();session.Tick(20);Run(CommandKind.RevealRoll);session.Tick(2);Assert.That(S.pendingRoll.stage,Is.EqualTo("AwaitDecision"));Run(CommandKind.Commit);var t=S.rolls.Last();Assert.That(t.p6.startDeadline-t.p6.committedAt,Is.EqualTo(1));Run(CommandKind.ExitFocus);session.Tick(1.01f);Assert.That(S.rolls.Last().stage,Is.EqualTo("Canceled"));Assert.That(P.heldBy,Is.Null);
        }
        [Test] public void ZeroFocusDirectAcceptUsesVisibleRollThenQueueWithoutFreePostRoll()
        {
            S.focusBudget.secondsRemaining=0;var t=Confirm(true);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.charges,Is.EqualTo(1));ulong draws=S.random.visibleTactical.drawCount;Run(CommandKind.RevealRoll);Assert.That(S.mode,Is.EqualTo(CombatMode.QueuedExecution));Assert.That(S.pendingRoll,Is.Null);Assert.That(S.rolls.Last().stage,Is.EqualTo("Queued"));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(session.Execute(C(CommandKind.Modify)).success,Is.False);Assert.That(session.Execute(C(CommandKind.SelectSmiteOvercharge)).success,Is.False);
        }
        [Test] public void TrainingBlessingHasOwnCriticalSourceWithoutChangingRawAndDoesNotRewardFormalEconomy()
        {
            Make(SeedFor(14),before:s=>{s.world.strings["context"]="TRAINING";s.p6.training=true;s.p6.trainingBlessingEnabled=true;});Reveal();var q=Start(true);var d=Contact(q.id).p6.damages.Single();Assert.That(d.selectedRaw,Is.EqualTo(14));Assert.That(d.criticalSource,Is.EqualTo("训练祝福"));Assert.That(d.critical,Is.True);Assert.That(S.p6.trainingBlessingUsed,Is.True);Assert.That(S.p6.resourceEvents,Is.Empty);
        }
        [Test] public void SaveSinkFailureRollsBackChargeReservationDiceDamageAndMarkTogether()
        {
            bool fail=false;Make(SeedFor(14),sink:s=>{if(fail)throw new IOException("injected_sink");});Bind(true);Reveal();var select=C(CommandKind.SelectSmiteOvercharge);select.flag=true;string before=CombatJson.Write(S);fail=true;Assert.That(session.Execute(select).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));fail=false;Run(select);var q=Start(); // Already selected; Start(false) keeps its reservation.
            session.Tick(.36f);var finish=C(CommandKind.ResolveQueued);finish.queueId=q.id;finish.authority=CommandAuthority.System;before=CombatJson.Write(S);fail=true;Assert.That(session.Execute(finish).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));fail=false;Run(finish);Assert.That(S.rolls.Last().p6.damage.markConsumed,Is.True);
        }
        [Test] public void DamageStreamDoesNotAdvanceAttackEventRewardOrCosmeticStreams()
        {
            Reveal();var q=Start(true);session.Tick(.36f);ulong attack=S.random.visibleTactical.drawCount,background=S.random.backgroundCombat.drawCount,events=S.random.eventDice.drawCount,reward=S.random.contentReward.drawCount,cosmetic=S.random.cosmetic.drawCount;
            var c=C(CommandKind.ResolveQueued);c.queueId=q.id;c.authority=CommandAuthority.System;Run(c);Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(attack));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(background));Assert.That(S.random.eventDice.drawCount,Is.EqualTo(events));Assert.That(S.random.contentReward.drawCount,Is.EqualTo(reward));Assert.That(S.random.cosmetic.drawCount,Is.EqualTo(cosmetic));Assert.That(S.p6.damageRandom.drawCount,Is.GreaterThan(0));
        }
        [Test] public void RealAiAuthorityStillCannotSelectOverchargeBindOrTrainingRefill()
        {
            Reveal();var choose=C(CommandKind.SelectSmiteOvercharge);choose.flag=true;choose.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(choose).reason,Is.EqualTo("ai_precious_resource_forbidden"));
            var bind=C(CommandKind.RequestBindRadiance,"mage");bind.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(bind).reason,Is.EqualTo("ai_precious_resource_forbidden"));var refill=C(CommandKind.RefillPracticeResources);refill.flag=true;refill.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(refill).reason,Is.EqualTo("ai_precious_resource_forbidden"));
        }
        [Test] public void StaggerBeforeLargeFrameCancelsCastRatherThanBeingForgottenAfterTimerDecay()
        {
            var c=C(CommandKind.RequestBindRadiance,"mage");Run(c);S.Actor("mage").staggerRemaining=.1f;session.Tick(1);Assert.That(S.Actor("mage").p6.bindIntent.stage,Is.EqualTo("Canceled"));Assert.That(Enemy.statuses.Any(x=>x.p6!=null),Is.False);
        }
        [Test] public void WaitingBindSavesExactlyAndCancelNeverErasesTheAlreadyStartedBasicAttack()
        {
            var c=C(CommandKind.StartBasicAttack,"mage");c.actionId="cantrip";Run(c);var request=C(CommandKind.RequestBindRadiance,"mage");Run(request);string original=CombatJson.Write(S);session=new CombatSession(CombatJson.Read<CombatState>(original),config);Assert.That(CombatJson.Write(S),Is.EqualTo(original));
            var cancel=C(CommandKind.CancelBasicOrder,"mage");cancel.orderId=request.id;Run(cancel);Assert.That(S.Actor("mage").p6.bindIntent.stage,Is.EqualTo("Canceled"));Assert.That(S.Actor("mage").basicAttackIntent.resolved,Is.False);Assert.That(S.Actor("mage").windupRemaining,Is.GreaterThan(0));Assert.That(S.Actor("mage").p6.bindCooldown,Is.EqualTo(16));
        }
        [Test] public void GoldenNinetyOneUsesRealDamageStreamWithSixteenWeaponAndFortyEightRadiant()
        {
            Make(SeedFor(20));Bind(true);RngState fixture=null;
            for(ulong seed=1;seed<20000;seed++)
            {
                var original=RandomStreams.Create(seed).classResource;var sample=CombatJson.Clone(original);int weapon=0,radiant=0;for(int i=0;i<4;i++)weapon+=RandomStreams.NextInt(sample,1,7);for(int i=0;i<10;i++)radiant+=RandomStreams.NextInt(sample,1,9);
                if(weapon==16&&radiant==48){fixture=original;break;}
            }
            Assert.That(fixture,Is.Not.Null);S.p6.damageRandom=fixture;Reveal();var q=Start(true);var damage=Contact(q.id).p6.damages.Single();Assert.That(damage.weaponFaces.Sum(),Is.EqualTo(16));Assert.That(damage.radiantFaces.Sum(),Is.EqualTo(48));Assert.That(damage.totalDamage,Is.EqualTo(91));Assert.That(damage.strengthBonus,Is.EqualTo(3));
        }
        [Test] public void ActualResourceJsonCreatesAndExecutesP6WithoutOverwritingLegacyConfig()
        {
            string root=FindProjectRoot();var legacy=CombatJson.Read<CombatConfig>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/CombatConfig.json")));string original=CombatJson.Write(legacy);
            config=CombatJson.Clone(legacy);config.p5=CombatJson.Read<P5Config>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/P5CombatConfig.json")));config.p6=CombatJson.Read<P6Config>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/P6CombatConfig.json")));
            var state=CombatState.CreatePartyForPreset("p6-resource","resource-run",SeedFor(14),config);state.Actor(Paladin).x=0;state.Actor(Paladin).z=0;state.enemies[0].x=1;state.enemies[0].z=0;session=new CombatSession(state,config);sequence=0;
            Reveal();var q=Start(true);var result=Contact(q.id);Assert.That(result.success&&result.p6.damages.Count==1,Is.True);Assert.That(CombatJson.Write(legacy),Is.EqualTo(original));Assert.That(S.focusBudget.maximumSeconds,Is.EqualTo(120));
        }
        static string FindProjectRoot()
        {
            for(var directory=new DirectoryInfo(TestContext.CurrentContext.TestDirectory);directory!=null;directory=directory.Parent)if(File.Exists(Path.Combine(directory.FullName,"Assets/CombatV051/Resources/P6CombatConfig.json")))return directory.FullName;
            for(var directory=new DirectoryInfo(Directory.GetCurrentDirectory());directory!=null;directory=directory.Parent)if(File.Exists(Path.Combine(directory.FullName,"Assets/CombatV051/Resources/P6CombatConfig.json")))return directory.FullName;
            throw new IOException("actual_p6_resource_not_found");
        }
    }
}
