using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class CombatRuleTests
    {
        CombatSession session;int next;
        CombatState S {get{return session.State;}}
        ActorState Fighter {get{return S.Actor("fighter");}}
        ActorState Enemy {get{return S.Actor("enemy-1");}}
        [SetUp] public void Setup(){var state=CombatState.Create("rules","campaign",42);state.charges=1;session=new CombatSession(state,new CombatConfig{passiveMomentumPerSecond=0});next=0;}
        CombatCommand C(CommandKind kind){return new CombatCommand{id="test-"+(++next),kind=kind};}
        CombatResult Run(CommandKind kind){var r=session.Execute(C(kind));Assert.That(r.success,Is.True,r.reason);return r;}
        void FocusRoll(int face=12,string action="attack")
        {
            S.random.visibleTactical=StreamFor(face);Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);c.actionId=action;var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);
        }
        void Decision(int face=12,string action="attack"){FocusRoll(face,action);Run(CommandKind.RevealRoll);}
        string Queue(int face=12){Decision(face);string id=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);return id;}
        CombatResult Q(CommandKind kind,string id){var c=C(kind);c.queueId=id;var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        public static RngState StreamFor(int first)
        {
            for(ulong seed=1;seed<10000;seed++){var r=RandomStreams.Create(seed).visibleTactical;var copy=CombatJson.Clone(r);if(RandomStreams.D20(copy)==first)return r;}
            throw new InvalidOperationException("No fixture seed");
        }
        [Test] public void FreshStateHasTwoSharedChargesAndNoStoredDice(){var fresh=CombatState.Create("new","campaign",1);Assert.That(fresh.charges,Is.EqualTo(2));Assert.That(fresh.chargeCap,Is.EqualTo(2));Assert.That(fresh.momentum,Is.Zero);Assert.That(fresh.rolls,Is.Empty);Assert.That(fresh.actors.Count,Is.EqualTo(1));}
        [Test] public void QuoteDoesNotChargeHoldOrDrawAndExplainsAim(){Fighter.aimReady=true;string before=CombatJson.Write(S);var quote=session.Quote("fighter","enemy-1","attack");Assert.That(quote.legal);Assert.That(quote.sources,Does.Contain("Aim"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [TestCase("cooldown")] [TestCase("dead")] [TestCase("target")] [TestCase("range")] [TestCase("charge")] [TestCase("resource")] [TestCase("los")]
        public void IllegalConfirmNeverConsumesOrDraws(string reason)
        {
            Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);
            if(reason=="cooldown")Fighter.majorRemaining=1;if(reason=="dead")Fighter.hp=0;if(reason=="target")c.targetId="absent";if(reason=="range")Enemy.x=100;if(reason=="charge")S.charges=0;if(reason=="resource")session.Config.actions[0].spellCost=2;if(reason=="los")c.lineOfSight=false;
            string before=CombatJson.Write(S);var r=session.Execute(c);Assert.That(r.success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void ConfirmTenTimesPaysOnceAndCannotSwapPayload()
        {
            Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);var first=session.Execute(c);Assert.That(first.success);ulong draws=S.random.visibleTactical.drawCount;
            for(int i=0;i<10;i++){var r=session.Execute(c);Assert.That(r.duplicate);Assert.That(r.rawFaces,Is.EqualTo(first.rawFaces));}
            Assert.That(S.charges,Is.Zero);Assert.That(S.rolls.Count,Is.EqualTo(1));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));c.targetId="wrong";Assert.That(session.Execute(c).reason,Is.EqualTo("command_payload_mismatch"));
        }
        [Test] public void StaleQuoteIsRejectedWithoutAnyMutation(){var c=C(CommandKind.EnterFocus);c.expectedRevision=999;string before=CombatJson.Write(S);Assert.That(session.Execute(c).reason,Is.EqualTo("stale_revision"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void RollFreezeStopsEveryDomainTimerAndRevealsToSlowTime()
        {
            Fighter.dodgeCooldown=1;Fighter.statuses.Add(new StatusState{id="buff",remaining=3});Enemy.aiState=1;Enemy.aiRemaining=2;FocusRoll();string before=CombatJson.Write(S);
            Assert.That(session.Scale,Is.Zero);Assert.That(session.Tick(5),Is.Zero);var frozen=S.Clone();frozen.realCombatSeconds=0;Assert.That(CombatJson.Write(frozen),Is.EqualTo(before));Assert.That(S.realCombatSeconds,Is.EqualTo(5));Run(CommandKind.RevealRoll);Assert.That(session.Scale,Is.EqualTo(.1f));Assert.That(session.Tick(1),Is.EqualTo(.1f));Assert.That(Fighter.dodgeCooldown,Is.EqualTo(.9f).Within(.001));
        }
        [Test] public void PauseRestoresExactRollStageWithoutNewDraws(){FocusRoll();ulong draws=S.random.visibleTactical.drawCount;Run(CommandKind.Pause);Assert.That(session.Tick(20),Is.Zero);Run(CommandKind.Resume);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));}
        [Test] public void CannotCloseFocusOrCommitBeforeReveal(){FocusRoll();Assert.That(session.Execute(C(CommandKind.ExitFocus)).success,Is.False);Assert.That(session.Execute(C(CommandKind.Commit)).success,Is.False);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));}
        [Test] public void PostRollCancelKeepsChargeAndPaidResourcesButReleasesHolds()
        {
            session.Config.actions[0].spellCost=1;session.Config.actions[0].itemCost=1;Decision();Run(CommandKind.Modify);Run(CommandKind.Precision);Run(CommandKind.Cancel);
            Assert.That(S.charges,Is.Zero);Assert.That(Fighter.inspiration,Is.EqualTo(1));Assert.That(Fighter.superiority,Is.EqualTo(1));Assert.That(Fighter.spellSlots,Is.EqualTo(1));Assert.That(Fighter.items,Is.EqualTo(1));Assert.That(Fighter.heldSpellSlots,Is.Zero);Assert.That(Fighter.heldItems,Is.Zero);Assert.That(Fighter.heldBy,Is.Null);Assert.That(Fighter.majorRemaining,Is.Zero);Assert.That(S.queue,Is.Empty);
        }
        [Test] public void RerollPaysOnceAdvancesAndFreezesAgain()
        {
            Decision(8);int charges=S.charges;ulong draws=S.random.visibleTactical.drawCount;var c=C(CommandKind.Reroll);var r=session.Execute(c);Assert.That(r.success);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.pendingRoll.stage,Is.EqualTo("Rolling"));Assert.That(S.random.visibleTactical.drawCount,Is.GreaterThan(draws));Assert.That(Fighter.inspiration,Is.EqualTo(1));Assert.That(S.charges,Is.EqualTo(charges));Assert.That(session.Execute(c).duplicate);Run(CommandKind.RevealRoll);Assert.That(session.Execute(C(CommandKind.Reroll)).success,Is.False);
        }
        [Test] public void ModifyUsesSameInspirationPoolAndCannotRepeat(){Decision();var c=C(CommandKind.Modify);int total=S.pendingRoll.total;Assert.That(session.Execute(c).success);Assert.That(S.pendingRoll.total,Is.EqualTo(total+2));Assert.That(session.Execute(c).duplicate);Assert.That(session.Execute(C(CommandKind.Modify)).success,Is.False);Run(CommandKind.Reroll);Assert.That(Fighter.inspiration,Is.Zero);}
        [Test] public void PrecisionUsesClassStreamAndPreservesNaturalFailure(){Decision(1);ulong visible=S.random.visibleTactical.drawCount,background=S.random.backgroundCombat.drawCount;Run(CommandKind.Precision);Assert.That(S.pendingRoll.precisionBonus,Is.InRange(1,6));Assert.That(S.pendingRoll.hit,Is.False);Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(1));Assert.That(S.random.classResource.drawCount,Is.GreaterThan(0));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(background));}
        [TestCase(1,false,false)] [TestCase(7,false,false)] [TestCase(8,true,false)] [TestCase(20,true,true)]
        public void AttackUsesACWithExplicitNaturalRules(int face,bool hit,bool critical){Decision(face);Assert.That(S.pendingRoll.total,Is.EqualTo(face+5));Assert.That(S.pendingRoll.hit,Is.EqualTo(hit));Assert.That(S.pendingRoll.critical,Is.EqualTo(critical));}
        [Test] public void AdvantageDisadvantageCancelWithoutCountingSources()
        {
            Fighter.aimReady=true;Fighter.statuses.Add(new StatusState{id="flanking",remaining=10});Fighter.statuses.Add(new StatusState{id="suppressed",remaining=10});FocusRoll();Assert.That(S.pendingRoll.advantage&&S.pendingRoll.disadvantage);Assert.That(S.pendingRoll.rawFaces.Count,Is.EqualTo(1));Assert.That(S.pendingRoll.modifierSources.Count,Is.EqualTo(3));
        }
        [TestCase(true,false)] [TestCase(false,true)] [TestCase(true,true)] [TestCase(false,false)]
        public void ExactAttackProbabilityMatchesAllFourHundredPairs(bool advantage,bool disadvantage)
        {
            int hits=0;for(int a=1;a<=20;a++)for(int b=1;b<=20;b++){int f=advantage==disadvantage?a:advantage?Math.Max(a,b):Math.Min(a,b);if(f==20||f!=1&&f+5>=13)hits++;}
            Assert.That(CombatSession.Probability("Attack",5,13,advantage,disadvantage),Is.EqualTo(hits/400.0).Within(1e-12));
        }
        [Test] public void AdvantageShowsBothOriginalFacesAndUsesMaximum(){Fighter.aimReady=true;FocusRoll();Assert.That(S.pendingRoll.rawFaces.Count,Is.EqualTo(2));Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(S.pendingRoll.rawFaces.Max()));Assert.That(Fighter.aimReady,Is.False);}
        [Test] public void ShoveRollBelongsToEnemySaveAndCannotSpendFighterModifiers()
        {
            Enemy.x=-1;Enemy.saveBonus=-50;Decision(20,"shove");Assert.That(S.pendingRoll.rollOwnerId,Is.EqualTo("enemy-1"));Assert.That(S.pendingRoll.targetNumber,Is.EqualTo(13));Assert.That(S.pendingRoll.hit,Is.True,"A natural 20 Save is not an automatic success.");
            foreach(var kind in new[]{CommandKind.Reroll,CommandKind.Modify,CommandKind.Precision})Assert.That(session.Execute(C(kind)).reason,Is.EqualTo("cannot_modify_enemy_save"));Assert.That(Fighter.inspiration,Is.EqualTo(2));
        }
        [Test] public void CommitOnlyQueuesAndStartSpendsCapturedCostsExactlyOnce()
        {
            session.Config.actions[0].spellCost=1;session.Config.actions[0].itemCost=1;string q=Queue();Assert.That(Fighter.spellSlots,Is.EqualTo(1));Assert.That(Fighter.majorRemaining,Is.Zero);session.Config.actions[0].spellCost=9;
            var c=C(CommandKind.StartQueued);c.queueId=q;Assert.That(session.Execute(c).success);Assert.That(Fighter.spellSlots,Is.Zero);Assert.That(Fighter.items,Is.Zero);Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));Assert.That(session.Execute(c).duplicate);Assert.That(Fighter.spellSlots,Is.Zero);
        }
        [Test] public void DeadTargetCancelsUnstartedQueueWithoutLongTermCost(){session.Config.actions[0].spellCost=1;string q=Queue();Enemy.hp=0;Q(CommandKind.StartQueued,q);Assert.That(S.queue[0].stage,Is.EqualTo("Canceled"));Assert.That(Fighter.spellSlots,Is.EqualTo(1));Assert.That(Fighter.majorRemaining,Is.Zero);Assert.That(Fighter.heldBy,Is.Null);Assert.That(S.charges,Is.Zero);}
        [Test] public void TargetMovesOutOfRangeDoesNotConsumeAtStart(){string q=Queue();Enemy.x=100;var c=C(CommandKind.StartQueued);c.queueId=q;Assert.That(session.Execute(c).reason,Is.EqualTo("execution_position_illegal"));Assert.That(Fighter.majorRemaining,Is.Zero);Assert.That(S.queue[0].stage,Is.EqualTo("Queued"));}
        [Test] public void ExecutingActionCannotRefundOrApplyDamageTwice()
        {
            string q=Queue(20);Q(CommandKind.StartQueued,q);Assert.That(session.Execute(new CombatCommand{id="early",kind=CommandKind.ResolveQueued,queueId=q}).reason,Is.EqualTo("windup_incomplete"));session.Tick(1);int hp=Enemy.hp;var c=C(CommandKind.ResolveQueued);c.queueId=q;Assert.That(session.Execute(c).damage,Is.EqualTo(8));Assert.That(Enemy.hp,Is.EqualTo(hp-8));Assert.That(session.Execute(c).duplicate);Assert.That(Enemy.hp,Is.EqualTo(hp-8));Assert.That(Fighter.heldBy,Is.Null);
        }
        [Test] public void BlockedAfterWindupCancelsWithoutRefundingStartedResources(){session.Config.actions[0].spellCost=1;string q=Queue();Q(CommandKind.StartQueued,q);session.Tick(1);var c=C(CommandKind.ResolveQueued);c.queueId=q;c.lineOfSight=false;int hp=Enemy.hp;Assert.That(session.Execute(c).success);Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(Fighter.spellSlots,Is.Zero);Assert.That(S.queue[0].stage,Is.EqualTo("Canceled"));}
        [Test] public void ShoveAppliesEngineClippedDestinationInSameTransaction()
        {
            Enemy.x=-1;Enemy.saveBonus=-50;Decision(2,"shove");string q=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);Q(CommandKind.StartQueued,q);session.Tick(1);var c=C(CommandKind.ResolveQueued);c.queueId=q;c.qualified=true;c.x=-.75f;c.z=0;Assert.That(session.Execute(c).success);Assert.That(Enemy.x,Is.EqualTo(-.75f));Assert.That(Enemy.breakValue,Is.EqualTo(2));
        }
        [Test] public void BasicAttackUsesMajorBackgroundD20AndNoCharge(){Enemy.x=-1;S.random.backgroundCombat=StreamFor(20);ulong visible=S.random.visibleTactical.drawCount;int charge=S.charges;var r=Run(CommandKind.BasicAttack);Assert.That(r.rawFaces.Count,Is.EqualTo(1));Assert.That(r.damage,Is.EqualTo(4));Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));}
        [Test] public void HeldMajorBlocksBackgroundAIWithoutConsumingItsResources(){Decision();Enemy.x=-1;var c=C(CommandKind.BasicAttack);c.flag=true;int hp=Enemy.hp;ulong draws=S.random.backgroundCombat.drawCount;Assert.That(session.Execute(c).reason,Is.EqualTo("major_unavailable"));Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));}
        [TestCase(CommandKind.ConfirmRoll)] [TestCase(CommandKind.Modify)] [TestCase(CommandKind.Reroll)] [TestCase(CommandKind.Precision)]
        public void AIHasNoPreciousResourcePermission(CommandKind kind){var c=C(kind);c.flag=true;string before=CombatJson.Write(S);Assert.That(session.Execute(c).reason,Is.EqualTo("ai_precious_resource_forbidden"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void TwoActorsShareChargeButCannotShareMajor()
        {
            S.charges=2;S.actors.Add(new ActorState{id="other",x=-3,z=1});Decision();Run(CommandKind.Commit);var duplicateActor=C(CommandKind.ConfirmRoll);Assert.That(session.Execute(duplicateActor).reason,Is.EqualTo("major_unavailable"));Assert.That(S.charges,Is.EqualTo(1));
            var other=C(CommandKind.ConfirmRoll);other.actorId="other";Assert.That(session.Execute(other).success);Assert.That(S.charges,Is.Zero);Assert.That(Fighter.heldBy,Is.Not.EqualTo(S.Actor("other").heldBy));
        }
        [Test] public void FullChargesCannotStoreAHiddenThirdRoll(){S.charges=2;session.Config.passiveMomentumPerSecond=4;session.Tick(100);Assert.That(S.momentum,Is.Zero);Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.momentum,Is.Zero);Assert.That(S.pendingRoll.chargeSpent,Is.EqualTo(1));}
        [Test] public void SameRegisteredRootCannotFarmMomentum()
        {
            S.momentumLedger.registeredObjectives.Add("mechanism");for(int i=0;i<4;i++){var c=C(CommandKind.AwardFact);c.authority=CommandAuthority.System;c.actionId="environment";c.qualified=true;c.factId="fact-"+i;c.rootId="mechanism";Assert.That(session.Execute(c).success);Assert.That(S.momentum,Is.EqualTo(40));}
        }
        [Test] public void RefundAndUnqualifiedFactsCannotCreateMomentum(){var c=C(CommandKind.AwardFact);c.actionId="refund";c.qualified=true;c.factId="r";c.rootId="r";Assert.That(session.Execute(c).success,Is.False);c=C(CommandKind.AwardFact);c.actionId="perfect-dodge";Assert.That(session.Execute(c).success,Is.False);Assert.That(S.momentum,Is.Zero);}
        [Test] public void PassiveSupplyStopsAtFullAndARealLowHealthThreatRemainsEffective()
        {
            session.Config.passiveMomentumPerSecond=4;session.Tick(1000);Assert.That(S.momentumLedger.passiveEarned,Is.EqualTo(100));Assert.That(S.charges,Is.EqualTo(2));Assert.That(S.momentum,Is.Zero);session.Tick(1000);Assert.That(S.momentum,Is.Zero);
            Enemy.hp=1;S.charges=1;session.Tick(1);Assert.That(S.momentum,Is.EqualTo(4));
        }
        [Test] public void AimRequiresPreparationAndCannotDodgeAttackOrFarmAfterReady()
        {
            Enemy.x=-1;Run(CommandKind.StartAim);Assert.That(session.Execute(C(CommandKind.StartDodge)).success,Is.False);Assert.That(session.Execute(C(CommandKind.BasicAttack)).success,Is.False);session.Tick(.9f);Assert.That(Fighter.aimReady,Is.False);session.Tick(.2f);Assert.That(Fighter.aimReady);Assert.That(S.momentum,Is.Zero);Assert.That(session.Execute(C(CommandKind.StartAim)).success,Is.False);
        }
        [Test] public void CancelMovementPreparationAndTakingDamageLoseAim(){Run(CommandKind.StartAim);Run(CommandKind.CancelAim);session.Tick(2);Assert.That(Fighter.aimReady,Is.False);Run(CommandKind.StartAim);S.random.backgroundCombat=StreamFor(20);Assert.That(session.ResolveIncoming("aim-hit","enemy-1","fighter",1).success);Assert.That(Fighter.aimPreparing,Is.False);}
        [Test] public void GuardOnlyRewardsActualQualifiedAttackAndReplayedRootCannotRewardAgain()
        {
            Run(CommandKind.StartGuard);Assert.That(S.momentum,Is.Zero);ulong draws=S.random.backgroundCombat.drawCount;var r=session.ResolveIncoming("guard-hit","enemy-1","fighter",3,rootId:"barrage");Assert.That(r.reason,Is.EqualTo("parried"));Assert.That(r.damage,Is.Zero);Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));Assert.That(S.momentum,Is.EqualTo(30));Assert.That(session.ResolveIncoming("guard-hit-2","enemy-1","fighter",3,rootId:"barrage").success,Is.False);Assert.That(S.momentum,Is.EqualTo(30));
        }
        [Test] public void DodgeAvoidsIncomingWithoutSaveAndOneBarrageIsOneSource(){Run(CommandKind.StartDodge);ulong before=S.random.backgroundCombat.drawCount;var r=session.ResolveIncoming("dodge-hit","enemy-1","fighter",4,true,12,"barrage");Assert.That(r.reason,Is.EqualTo("avoided_by_dodge"));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(before));Assert.That(S.momentum,Is.EqualTo(30));Assert.That(session.ResolveIncoming("other-hit","enemy-1","fighter",4,true,12,"barrage").success,Is.False);}
        [TestCase(1,30,2)] [TestCase(20,-30,4)] public void IncomingSaveUsesTotalAndHalvesOnlyOnSuccess(int face,int bonus,int damage){Fighter.saveBonus=bonus;S.random.backgroundCombat=StreamFor(face);var r=session.ResolveIncoming("save","enemy-1","fighter",4,true,12);Assert.That(r.success);Assert.That(r.damage,Is.EqualTo(damage));}
        [Test] public void BackgroundAndCosmeticsCannotChangeVisibleRoll(){var before=CombatJson.Clone(S.random.visibleTactical);for(int i=0;i<100;i++){RandomStreams.D20(S.random.backgroundCombat);RandomStreams.D20(S.random.cosmetic);RandomStreams.D20(S.random.eventDice);}Assert.That(RandomStreams.D20(S.random.visibleTactical),Is.EqualTo(RandomStreams.D20(before)));}
        [Test] public void PcgPublishedReferenceVectorIsPreserved(){var r=new RngState{increment=109};RandomStreams.NextUInt(r);r.state+=42;RandomStreams.NextUInt(r);uint[] expected={0xa15c02b7,0x7b47f409,0xba1d3330,0x83d2f293,0xbfa4784b,0xcbed606e};foreach(uint value in expected)Assert.That(RandomStreams.NextUInt(r),Is.EqualTo(value));}
        [Test] public void InvalidStreamAndFourthPartyMemberFailValidation(){var copy=S.Clone();copy.random.visibleTactical.increment=2;Assert.Throws<InvalidOperationException>(()=>CombatSession.Validate(copy));copy=S.Clone();copy.actors.Add(new ActorState{id="b"});copy.actors.Add(new ActorState{id="c"});copy.actors.Add(new ActorState{id="d"});Assert.Throws<InvalidOperationException>(()=>CombatSession.Validate(copy));}
        [TestCase(true)] [TestCase(false)] public void DeadOrDistantPartyCannotFarmPassiveOrAim(bool dead){session.Config.passiveMomentumPerSecond=2;if(dead)Fighter.hp=0;else Fighter.x=-100;session.Tick(30);Assert.That(S.momentum,Is.Zero);if(!dead){Run(CommandKind.StartAim);session.Tick(2);Assert.That(S.momentum,Is.Zero);}}
        [Test] public void AccessibilityFocusPauseAllowsDecisionCommandsButNoWorldTick(){session=new CombatSession(CombatState.Create("rules","campaign",1),new CombatConfig{focusScale=0});Run(CommandKind.EnterFocus);Assert.That(session.Tick(3),Is.Zero);Assert.That(S.realCombatSeconds,Is.EqualTo(3));Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(session.Scale,Is.Zero);Run(CommandKind.Cancel);Run(CommandKind.ExitFocus);Assert.That(session.Scale,Is.EqualTo(1));}
        [TestCase(CommandKind.CancelAim)] [TestCase(CommandKind.EndGuard)] [TestCase(CommandKind.CancelQueued)] [TestCase(CommandKind.BasicAttack)] [TestCase(CommandKind.Modify)]
        public void RollFreezeRejectsWorldAndResourceCommands(CommandKind kind){FocusRoll();string before=CombatJson.Write(S);Assert.That(session.Execute(C(kind)).reason,Is.EqualTo("world_frozen"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void PausedPostRollCannotModifyOrCancelUntilResume(){Decision();Run(CommandKind.Pause);Assert.That(session.Execute(C(CommandKind.Modify)).reason,Is.EqualTo("combat_paused"));Assert.That(session.Execute(C(CommandKind.Cancel)).reason,Is.EqualTo("combat_paused"));Run(CommandKind.Resume);Run(CommandKind.Modify);}
        [Test] public void SaveValidationRejectsUnbackedResourceHold(){Fighter.heldSpellSlots=1;Assert.Throws<InvalidOperationException>(()=>CombatSession.Validate(S));}
    }
}
