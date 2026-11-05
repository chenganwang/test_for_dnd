using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Fixed geometry/RNG and direct reward-helper calls are explicit domain fixtures.
    // Reward-source qualification is covered by combat/profile integration suites.
    public sealed class P6FocusTests
    {
        CombatSession session;int sequence;
        CombatState S=>session.State;
        const string Paladin=P6CharacterCatalog.PaladinId;
        [SetUp] public void Setup()
        {
            var config=new CombatConfig{p6=new P6Config(),passiveMomentumPerSecond=0};
            var s=CombatState.CreatePartyForPreset("p6-focus-tests","p6-focus-run",43,config);
            s.Actor(Paladin).x=0;s.Actor(Paladin).z=0;s.enemies[0].x=1;s.enemies[0].z=0;
            session=new CombatSession(s,config);sequence=0;
        }
        CombatCommand C(CommandKind kind)=>new CombatCommand{id="p6-focus/"+(++sequence),kind=kind,actorId=Paladin,targetId=S.enemies[0].id,actionId=CombatSession.P6SmiteId};
        CombatResult Do(CommandKind kind)
        {var result=session.Execute(C(kind));Assert.That(result.success,Is.True,result.reason);return result;}
        float Award(string root,string kind="defense")=>CombatSession.AwardP6FocusRecovery(S,root,kind);
        void FillToCapacity()
        {
            // Explicit already-qualified echo fixtures exercise only the shared ledger.
            for(int i=0;i<5;i++)Assert.That(Award("fill/"+i,"echo"),Is.EqualTo(6));
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(120));
        }
        [Test] public void NewBudgetIsNinetyAndOneHundredTwentyAndRepeatedInitializationCannotRefill()
        {
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(90));Assert.That(S.focusBudget.maximumSeconds,Is.EqualTo(120));
            Assert.That(S.focusBudget.p6.rules.maximumRecoveredSeconds,Is.EqualTo(60));Assert.That(S.reactionSettings.decisionSeconds,Is.EqualTo(6));
            Do(CommandKind.EnterFocus);session.Tick(2);Do(CommandKind.ExitFocus);
            CombatSession.InitializeP6Focus(S,new P6FocusConfig(),true);
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(88));Assert.That(S.focusBudget.secondsSpent,Is.EqualTo(2));
            Assert.That(S.focusBudget.p6.reopenCooldownRemainingSim,Is.EqualTo(2));
        }
        [Test] public void DecisionTimeAndWorldTimeUseDifferentClocksAndVisitExpiresAtFifteen()
        {
            Do(CommandKind.EnterFocus);float simulation=S.simulationSeconds;
            session.Tick(1);Assert.That(session.Scale,Is.EqualTo(.12f));Assert.That(S.simulationSeconds-simulation,Is.EqualTo(.12f).Within(.0001));
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(89));session.Tick(14);
            Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(75));
            Assert.That(S.focusBudget.p6.currentVisitSpent,Is.EqualTo(15));Assert.That(S.focusBudget.active,Is.False);
        }
        [Test] public void ReopenRequiresTwoEffectiveSimulationSecondsAndNeverDoublesDrain()
        {
            Do(CommandKind.EnterFocus);session.Tick(1);Do(CommandKind.ExitFocus);
            Assert.That(session.Execute(C(CommandKind.EnterFocus)).reason,Is.EqualTo("p6_focus_reopen_cooldown"));
            session.Tick(5,false);Assert.That(S.focusBudget.p6.reopenCooldownRemainingSim,Is.EqualTo(2));
            S.momentumLedger.disengaged=true;session.Tick(3);Assert.That(S.focusBudget.p6.reopenCooldownRemainingSim,Is.EqualTo(2));
            S.momentumLedger.disengaged=false;session.Tick(1);Assert.That(S.focusBudget.p6.reopenCooldownRemainingSim,Is.EqualTo(1).Within(.0001));
            session.Tick(1);Do(CommandKind.EnterFocus);session.Tick(1);
            Assert.That(S.focusBudget.entryDrainMultiplier,Is.EqualTo(1));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(88));
        }
        [Test] public void FreezePauseAndUnavailableDecisionUiSpendNeitherGlobalNorVisitBudget()
        {
            Do(CommandKind.EnterFocus);session.Tick(2,false);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(90));
            Do(CommandKind.Pause);session.Tick(60);Do(CommandKind.Resume);
            Do(CommandKind.ConfirmRoll);string ticket=S.pendingRoll.id;ulong draws=S.random.visibleTactical.drawCount;
            session.Tick(60);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(90));
            Assert.That(S.focusBudget.p6.currentVisitSpent,Is.Zero);Assert.That(S.pendingRoll.id,Is.EqualTo(ticket));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));
            Do(CommandKind.RevealRoll);session.Tick(1);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(89));
        }
        [Test] public void InteractiveConfirmationNeedsThreeSecondsInBothBudgetsWithoutChargingOnRejection()
        {
            Do(CommandKind.EnterFocus);session.Tick(12);Assert.That(CombatSession.P6CanStartInteractiveRoll(S),Is.True);
            session.Tick(.01f);Assert.That(CombatSession.P6CanStartInteractiveRoll(S),Is.False);
            int charges=S.charges;ulong draws=S.random.visibleTactical.drawCount;var failed=session.Execute(C(CommandKind.ConfirmRoll));
            Assert.That(failed.success,Is.False);Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));
            Assert.That(S.pendingRoll,Is.Null);
        }
        [TestCase(2.99f,false)] [TestCase(3f,true)]
        public void GlobalThresholdIndependentlyRejectsEvenWhenVisitHasTime(float remaining,bool allowed)
        {
            S.focusBudget.secondsRemaining=remaining;Do(CommandKind.EnterFocus);
            Assert.That(CombatSession.P6CanStartInteractiveRoll(S),Is.EqualTo(allowed));
        }
        [Test] public void PaidSmiteTimeoutQueuesSameBaseResultWithoutNewChargeOrNewRoll()
        {
            S.focusBudget.secondsRemaining=3;Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll);string id=S.pendingRoll.id;
            Do(CommandKind.RevealRoll);int charge=S.charges;string rng=CombatJson.Write(S.random);int face=S.pendingRoll.selectedFace;
            session.Tick(3);
            Assert.That(session.LastTickError,Is.Null);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.mode,Is.EqualTo(CombatMode.QueuedExecution));
            Assert.That(S.queue.Single().ticketId,Is.EqualTo(id));Assert.That(S.rolls.Single(t=>t.id==id).selectedFace,Is.EqualTo(face));
            Assert.That(S.charges,Is.EqualTo(charge));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));Assert.That(S.focusBudget.active,Is.False);
            Assert.That(S.commandActorId,Is.EqualTo(S.activeActorId));
        }
        [Test] public void DeadlinePersistenceFailureRestoresTheEntirePaidDecisionAndBudget()
        {
            bool fail=false;session=new CombatSession(S,session.Config,persistenceSink:_=>{if(fail)throw new IOException("p6-focus-disk-fixture");});
            S.focusBudget.secondsRemaining=3;Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll);Do(CommandKind.RevealRoll);
            string before=CombatJson.Write(S);fail=true;session.Tick(3);
            Assert.That(session.LastTickError,Does.Contain("p6-focus-disk-fixture"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void VoluntaryCancelKeepsPaidFeeAndReleasesItsActorInsteadOfAutoCommitting()
        {
            Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll);Do(CommandKind.RevealRoll);int charge=S.charges;
            Do(CommandKind.Cancel);Do(CommandKind.ExitFocus);
            Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.queue,Is.Empty);Assert.That(S.Actor(Paladin).heldBy,Is.Null);
        }
        [Test] public void SixSecondReactionKeepsTheInterruptedFocusVisitAndDoesNotRefillItsWindow()
        {
            Do(CommandKind.EnterFocus);session.Tick(4);float focus=S.focusBudget.secondsRemaining,visit=S.focusBudget.p6.currentVisitSpent;
            S.Actor("mage").armorClass=10;S.enemies[0].attackBonus=0;
            RngState stream=null;
            for(ulong seed=1;seed<10000;seed++)
            {var candidate=RandomStreams.Create(seed).backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(candidate))==12){stream=candidate;break;}}
            Assert.That(stream,Is.Not.Null);S.random.backgroundCombat=stream;
            var incoming=session.ResolveIncoming("p6-focus-reaction",S.enemies[0].id,"mage",4,false,12,"reaction-root");Assert.That(incoming.success,Is.True,incoming.reason);
            var window=S.reactionWindows.Single();
            var begin=session.Execute(new CombatCommand{id="p6-begin-reaction",kind=CommandKind.BeginReaction,actorId="mage",reactionWindowId=window.id,reactionId="shield"});
            Assert.That(begin.success,Is.True,begin.reason);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(6));
            session.Tick(1);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(5));
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(focus));Assert.That(S.focusBudget.p6.currentVisitSpent,Is.EqualTo(visit));
            var decline=session.Execute(new CombatCommand{id="p6-decline-reaction",kind=CommandKind.DeclineReaction,actorId="mage",reactionWindowId=window.id});
            Assert.That(decline.success,Is.True,decline.reason);Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(CombatSession.P6VisitRemaining(S),Is.EqualTo(11));
        }
        [Test] public void DefenseAndComboAreEachLimitedToTwoPerEncounterAndOnePhysicalRootCannotDoubleCredit()
        {
            Assert.That(Award("a"),Is.EqualTo(2));Assert.That(Award("a","combo"),Is.Zero);Assert.That(Award("b"),Is.EqualTo(2));Assert.That(Award("c"),Is.Zero);
            Assert.That(Award("d","combo"),Is.EqualTo(3));Assert.That(Award("e","combo"),Is.EqualTo(3));Assert.That(Award("f","combo"),Is.Zero);
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(10));Assert.That(S.focusBudget.p6.rewards.Count,Is.EqualTo(4));
        }
        [Test] public void FullCapacityConsumesTheEventButNotGlobalActualRecoveryAllowance()
        {
            FillToCapacity();Assert.That(Award("full"),Is.Zero);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(30));
            Do(CommandKind.EnterFocus);session.Tick(4);Assert.That(Award("full"),Is.Zero);Assert.That(Award("after"),Is.EqualTo(2));
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(32));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(118));
        }
        [Test] public void PartialOverflowOnlyAccountsActualCreditAndCanNeverBeClaimedLater()
        {
            FillToCapacity();Do(CommandKind.EnterFocus);session.Tick(1);
            Assert.That(Award("partial","combo"),Is.EqualTo(1));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(31));
            session.Tick(3);Assert.That(Award("partial","combo"),Is.Zero);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(117));
        }
        [Test] public void GlobalSixtyActualRecoveryStopsAcrossEncounterChanges()
        {
            // Explicit prior expenditure makes room; changing encounter identity must not reset the run ledger.
            S.focusBudget.secondsRemaining=1;
            for(int i=0;i<10;i++){S.encounterId="encounter/"+i;Assert.That(Award("echo","echo"),Is.EqualTo(6));}
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(60));Assert.That(Award("extra","echo"),Is.Zero);
            CombatSession.InitializeP6Focus(S,new P6FocusConfig(),true);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(60));
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(61));
        }
        [Test] public void ClearOnlyCreditsAnActuallyDefeatedNonBossEncounterOnce()
        {
            Assert.That(Award("clear","clear"),Is.Zero);foreach(var e in S.enemies)e.hp=0;
            Assert.That(Award("clear","clear"),Is.EqualTo(8));Assert.That(Award("other-id","clear"),Is.Zero);
            S.encounterId="boss";S.momentumLedger.encounterKind="Boss";Assert.That(Award("boss-clear","clear"),Is.Zero);
        }
        [Test] public void BossPhaseRewardsUsePhaseIdentityInsteadOfAHealthOscillationOrWholeEncounterCap()
        {
            S.momentumLedger.encounterKind="Boss";
            Assert.That(Award("phase/1","boss-phase"),Is.EqualTo(8));Assert.That(Award("phase/1","boss-phase"),Is.Zero);
            Assert.That(Award("phase/2","boss-phase"),Is.EqualTo(8));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(16));
        }
        [Test] public void TrainingCannotCreditFormalLedgerAndItsUnlimitedReadingIsExplicit()
        {
            var config=session.Config;var state=CombatState.CreatePartyForPreset("training","p6-training",43,config);state.world.strings["context"]="TRAINING";
            CombatSession.InitializeP6State(state,config,true);session=new CombatSession(state,config);
            Assert.That(S.focusBudget.p6.unlimitedTraining,Is.True);Do(CommandKind.EnterFocus);session.Tick(120);
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(90));Assert.That(S.focusBudget.p6.currentVisitSpent,Is.Zero);
            Assert.That(Award("training-defense"),Is.Zero);Assert.That(S.focusBudget.p6.rewards,Is.Empty);
        }
        [Test] public void PausedOrFrozenRecoveryCannotCreateDeferredCredit()
        {
            Do(CommandKind.Pause);Assert.That(Award("paused"),Is.Zero);Do(CommandKind.Resume);
            Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll);Assert.That(Award("frozen"),Is.Zero);
            Assert.That(S.focusBudget.p6.rewards,Is.Empty);
        }
        [Test] public void ActualFileReloadPreservesVisitCooldownSourceReceiptsAndRulesSnapshot()
        {
            string dir=Path.Combine(Path.GetTempPath(),"P6FocusTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try
            {
                session=new CombatSession(S,session.Config,Path.Combine(dir,"combat.json"));Award("one");Do(CommandKind.EnterFocus);session.Tick(3);Do(CommandKind.ExitFocus);Assert.That(session.Save().success,Is.True);
                string before=CombatJson.Write(S);var loaded=CombatSession.Load(Path.Combine(dir,"combat.json"),session.Config,S.rulesProfileId);
                Assert.That(CombatJson.Write(loaded.State),Is.EqualTo(before));session=loaded;Assert.That(Award("one"),Is.Zero);
                Assert.That(S.focusBudget.p6.currentVisitSpent,Is.EqualTo(3));Assert.That(S.focusBudget.p6.reopenCooldownRemainingSim,Is.EqualTo(2));
            }
            finally{Directory.Delete(dir,true);}
        }
        [TestCase("NaN")] [TestCase("quota")] [TestCase("visit")] [TestCase("training")] [TestCase("balance")]
        public void InvalidBudgetSnapshotsAreRejected(string fault)
        {
            if(fault=="NaN")S.focusBudget.p6.currentVisitSpent=float.NaN;
            if(fault=="quota")S.focusBudget.secondsRecovered=61;
            if(fault=="visit")S.focusBudget.p6.currentVisitSpent=16;
            if(fault=="training")S.focusBudget.p6.unlimitedTraining=true;
            if(fault=="balance")S.focusBudget.secondsRemaining=120;
            Assert.Throws<InvalidOperationException>(()=>CombatSession.Validate(S));
        }
        [Test] public void LegacyBudgetHasNoSerializedP6ExtensionAndRetainsItsOldRules()
        {
            var config=new CombatConfig();var old=CombatState.CreatePartyP1("legacy","old-run",43,config);
            Assert.That(old.focusBudget.p6,Is.Null);Assert.That(CombatJson.Write(old.focusBudget),Does.Not.Contain("\"p6\""));
            Assert.That(old.focusBudget.secondsRemaining,Is.EqualTo(15));Assert.That(old.focusBudget.maximumSeconds,Is.EqualTo(20));
            Assert.That(CombatSession.P6ReactionSeconds(old),Is.EqualTo(2.5f));
        }
    }
}
