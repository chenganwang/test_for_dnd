using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Positions, incoming raw faces and initial health are deterministic state fixtures.
    // Every reward is produced by a real combat/profile command, never by a reward helper.
    public sealed class P47FocusBudgetTests : PartyTestFixture
    {
        void Incoming(string root, string defender="fighter", int damage=8)
        {var r=session.ResolveIncoming("p47-in-"+(++sequence),"enemy-1",defender,damage,false,12,root);Assert.That(r.success,r.reason);}
        void Dodge(string root,int damage=8)
        {S.Actor("fighter").dodgeRemaining=.5f;Incoming(root,damage:damage);}
        void HighThreat(string kind="elite")
        {S.momentumLedger.registeredHighThreats.Add("enemy-1");S.world.strings["enemyTemplate/enemy-1"]=kind;}
        CombatCommand Phase()
        {var c=C(CommandKind.AwardFact);c.authority=CommandAuthority.System;c.actionId="focus-boss-phase";c.targetId="enemy-1";c.qualified=true;c.rootId=c.factId="enemy-1/boss-half-health";return c;}
        void Boss()
        {CombatSession.ConfigureP4Encounter(S,"Boss",highThreatRoots:new[]{"enemy-1"});S.world.strings["enemyTemplate/enemy-1"]="boss";}
        string Shove(bool breaks,bool dangerous,bool hit=true)
        {
            S.Actor("enemy-1").breakValue=breaks?1:10;S.Actor("enemy-1").breakMaximum=10;
            S.Actor("enemy-1").saveBonus=hit?-50:50;S.Actor("enemy-1").aiState=1;S.Actor("enemy-1").aiRemaining=10;S.Actor("enemy-1").attackGroupId="dangerous-windup";
            if(dangerous)S.momentumLedger.registeredDangerousThreats.Add("dangerous-windup");
            Run(CommandKind.EnterFocus);string queue=Queue("fighter","shove");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=queue);
            session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=queue);return queue;
        }
        [Test] public void NewRunStartsAtFifteenWithTwentyCapAndCanNeverExceedHardTwentyFour()
        {
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));Assert.That(S.focusBudget.maximumSeconds,Is.EqualTo(20));
            var legal=S.Clone();legal.focusBudget.maximumSeconds=24;legal.focusBudget.secondsRemaining=24;Assert.DoesNotThrow(()=>new CombatSession(legal,session.Config));
            legal.focusBudget.maximumSeconds=24.01f;Assert.Throws<InvalidOperationException>(()=>new CombatSession(legal,session.Config));
            legal=S.Clone();legal.focusBudget.secondsRemaining=20.01f;Assert.Throws<InvalidOperationException>(()=>new CombatSession(legal,session.Config));
        }
        [TestCase(1,0f)] [TestCase(8,.75f)]
        public void OnlyAnActuallyAvoidedCriticalHitPaysPerfectDodge(int damage,float reward)
        {
            S.focusBudget.secondsRemaining=1;int hp=S.Actor("fighter").hp;Dodge("dodge",damage);
            Assert.That(S.Actor("fighter").hp,Is.EqualTo(hp));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(1+reward));
            Assert.That(S.incomingEvents.Single().reason,Is.EqualTo("avoided_by_dodge"));
        }
        [TestCase(true,1f)] [TestCase(false,0f)]
        public void PreciseParryPaysButOrdinaryHeldGuardDoesNot(bool precise,float reward)
        {
            S.focusBudget.secondsRemaining=1;S.Actor("fighter").guardRemaining=1;S.Actor("fighter").guardElapsed=precise?0:session.Config.parrySeconds+.1f;
            S.Actor("enemy-1").breakValue=5;S.random.backgroundCombat=BackgroundFor(12);S.Actor("enemy-1").attackBonus=0;S.Actor("fighter").armorClass=10;
            Incoming("guard",damage:2);if(S.activePendingEvent!=null)Run(CommandKind.DeclineReaction);
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(1+reward));Assert.That(S.Actor("enemy-1").breakValue,Is.EqualTo(precise?4:5));
        }
        [TestCase(true)] [TestCase(false)]
        public void SamePhysicalAoeUsesHighestDefenseRewardRegardlessOfTargetOrder(bool dodgeFirst)
        {
            S.focusBudget.secondsRemaining=1;S.Actor("fighter").dodgeRemaining=1;S.Actor("rogue").guardRemaining=1;S.Actor("rogue").guardElapsed=0;S.Actor("enemy-1").breakValue=5;
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.rootId="one-aoe";
            var dodge=new IncomingContact{targetId="fighter",damage=8};var parry=new IncomingContact{targetId="rogue",damage=8};
            c.incomingContacts=dodgeFirst?new List<IncomingContact>{dodge,parry}:new List<IncomingContact>{parry,dodge};
            Assert.That(session.Execute(c).success);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1));Assert.That(S.focusBudget.rewardEvents.Count,Is.EqualTo(1));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(1));
            Assert.That(session.Execute(c).duplicate);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1));
        }
        [TestCase("shield",true,1.25f)] [TestCase("parry",true,1.25f)] [TestCase("parry",false,0f)]
        public void HighRiskReactionPaysOnlyAfterSuccessfulActualMitigation(string kind,bool success,float recovery)
        {
            S.focusBudget.secondsRemaining=1;S.Actor("enemy-1").breakValue=5;string actor=kind=="shield"?"mage":"fighter";
            var window=Offer(actor,12,8);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Begin(window,kind);
            if(kind=="parry"){S.random.visibleTactical=CombatRuleTests.StreamFor(success?20:1);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);Assert.That(S.pendingRoll.hit,Is.EqualTo(success));}
            int before=S.Actor(actor).hp;Run(CommandKind.CommitReaction);
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(recovery));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(1+recovery));
            Assert.That(S.Actor(actor).hp,Is.EqualTo(kind=="shield"||success?before:before-8));
            Assert.That(S.Actor(actor).reactionRemaining,Is.GreaterThan(0));Assert.That(S.charges,Is.EqualTo(2));
        }
        [Test] public void CriticalNaturalTwentyCannotBeShieldedOrGenerateShieldRecovery()
        {
            S.focusBudget.secondsRemaining=1;S.random.backgroundCombat=BackgroundFor(20);S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;
            var r=session.ResolveIncoming("critical","enemy-1","mage",6,false,12,"critical-root");Assert.That(r.success);Assert.That(S.incomingEvents.Single().critical);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
            if(r.reactionWindowId!=null)Assert.That(session.QuoteReaction(r.reactionWindowId,"mage","shield").legal,Is.False);
        }
        [Test] public void LaterSuccessfulShieldUpgradesTheSameAoeDodgeToHighestRewardWithoutDoubleCredit()
        {
            S.focusBudget.secondsRemaining=1;S.Actor("fighter").dodgeRemaining=1;S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;S.random.backgroundCombat=BackgroundFor(12);
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.rootId="dodge-then-shield";
            c.incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=8},new IncomingContact{targetId="mage",damage=8}};
            Assert.That(session.Execute(c).success);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(.75f));var w=S.reactionWindows.Single();Begin(w,"shield");Run(CommandKind.CommitReaction);
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1.25f));Assert.That(S.focusBudget.rewardEvents.Count,Is.EqualTo(1));Assert.That(S.focusBudget.rewardEvents.Single().highestOffered,Is.EqualTo(1.25f));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(1.25f));
        }
        [Test] public void PaidSuccessfulRollThatTimesOutUnacceptedHasNoFocusRewardOrFeeRefund()
        {
            S.focusBudget.secondsRemaining=1;var w=Offer("fighter",12,8);Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);
            Assert.That(S.pendingRoll.hit);float reaction=S.Actor("fighter").reactionRemaining;ulong draws=S.random.visibleTactical.drawCount;session.Tick(0,true,2.5f);
            Assert.That(S.Actor("fighter").hp,Is.EqualTo(22));Assert.That(S.Actor("fighter").reactionRemaining,Is.EqualTo(reaction));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
        }
        [TestCase(CommandKind.IncomingAttack)] [TestCase(CommandKind.IncomingBatch)]
        public void RefundOriginCannotCreateADefensiveRewardOrAnIncomingContact(CommandKind kind)
        {
            S.Actor("fighter").dodgeRemaining=1;var c=C(kind,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.refundOrigin=true;c.rootId="refund-root";c.targetId="fighter";c.amount=8;c.incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=8}};
            string before=CombatJson.Write(S);Assert.That(session.Execute(c).reason,Is.EqualTo("refund_cannot_generate_reward"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [TestCase(true,true,true,1f)] [TestCase(true,false,true,1f)] [TestCase(false,true,true,1f)] [TestCase(false,false,true,0f)] [TestCase(true,true,false,0f)]
        public void RealShoveBreakAndDangerousInterruptShareOneRewardAndMissPaysNothing(bool breaks,bool dangerous,bool hit,float recovery)
        {
            S.focusBudget.secondsRemaining=1;Shove(breaks,dangerous,hit);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(recovery));
            Assert.That(S.Actor("enemy-1").aiState,Is.EqualTo(hit?0:1));Assert.That(S.focusBudget.rewardEvents.Count,Is.EqualTo(recovery>0?1:0));
        }
        [TestCase("guard",false,0f)] [TestCase("guard",true,0f)] [TestCase("elite",true,1.5f)] [TestCase("boss",true,0f)] [TestCase("mechanism",true,0f)]
        public void OnlyActualRegisteredEliteKillRecoversFocus(string template,bool registered,float recovery)
        {
            S.focusBudget.secondsRemaining=1;S.world.strings["enemyTemplate/enemy-1"]=template;if(registered)S.momentumLedger.registeredHighThreats.Add("enemy-1");
            S.Actor("enemy-1").hp=1;S.random.backgroundCombat=BackgroundFor(20);Run(CommandKind.BasicAttack);
            Assert.That(S.Actor("enemy-1").Alive,Is.False);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(recovery));
        }
        [Test] public void RepeatingSameLowRiskEnemyDecaysAndSimulationWindowResetStillSharesRoomCap()
        {
            S.focusBudget.secondsRemaining=1;Dodge("low-1");Dodge("low-2");Dodge("low-3");
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1.125f));Assert.That(S.focusBudget.rewardEvents.Select(e=>e.highestOffered),Is.EqualTo(new[]{.75f,.375f,0f}));
            session.Tick(10);Dodge("later-1");Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1.875f));
            Assert.That(S.focusBudget.recoveryScopes.Count,Is.EqualTo(1));
        }
        [Test] public void ReloadKeepsLowRiskDecayAndAlreadyDiscardedRoomQuota()
        {
            S.focusBudget.secondsRemaining=20;Dodge("saved-low-1");Dodge("saved-low-2");string ledger=CombatJson.Write(S.focusBudget.recoveryScopes);
            session=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),session.Config);Assert.That(CombatJson.Write(S.focusBudget.recoveryScopes),Is.EqualTo(ledger));
            Run(CommandKind.EnterFocus);session.Tick(1);Run(CommandKind.ExitFocus);Dodge("saved-low-3");Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(19));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(1.125f));
        }
        [TestCase("Normal",4f)] [TestCase("Elite",6f)] [TestCase("Boss",6f)]
        public void RepeatedValidHighThreatDefensesHaveFiniteRoomOrBossPhaseAllowance(string kind,float cap)
        {
            CombatSession.ConfigureP4Encounter(S,kind,highThreatRoots:new[]{"enemy-1"});S.focusBudget.secondsRemaining=1;
            for(int i=0;i<12;i++)Dodge("legitimate-"+i);
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(cap));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(cap));Assert.That(S.focusBudget.secondsDiscarded,Is.EqualTo(9-cap));
        }
        [Test] public void FullCapConsumesTheSourceAndRoomAllowanceWithoutBankingAfterFocusSpending()
        {
            HighThreat();S.focusBudget.secondsRemaining=20;for(int i=0;i<6;i++)Dodge("full-"+i);
            Assert.That(S.focusBudget.secondsDiscarded,Is.EqualTo(4.5f));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(4));
            Run(CommandKind.EnterFocus);session.Tick(2);Run(CommandKind.ExitFocus);float remaining=S.focusBudget.secondsRemaining;Dodge("new-after-spend");
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(remaining));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
            Assert.That(session.ResolveIncoming("replay-full","enemy-1","fighter",8,false,12,"full-0").success,Is.False);
        }
        [TestCase("training")] [TestCase("training-context")] [TestCase("disengaged")] [TestCase("reaction-origin")]
        public void TrainingDisengagementAndReactionOriginNeverGenerateFocus(string blocked)
        {
            S.focusBudget.secondsRemaining=1;S.Actor("fighter").dodgeRemaining=1;
            if(blocked=="training")S.momentumLedger.training=true;if(blocked=="training-context")S.world.strings["context"]="TRAINING";if(blocked=="disengaged")S.momentumLedger.disengaged=true;
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.fromReaction=blocked=="reaction-origin";c.rootId="blocked";c.incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=8}};
            Assert.That(session.Execute(c).success);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Assert.That(S.focusBudget.rewardEvents,Is.Empty);
        }
        [Test] public void BossTransitionRequiresRealHalfHealthAndSavesOneRewardInTheNewFinitePhase()
        {
            Boss();S.focusBudget.secondsRemaining=1;var command=Phase();Assert.That(session.Execute(command).reason,Is.EqualTo("boss_phase_not_reached"));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
            S.Actor("enemy-1").hp=S.Actor("enemy-1").maxHp/2;Assert.That(session.Execute(command).success);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(2));Assert.That(S.momentumLedger.phaseId,Is.EqualTo("boss-half-health"));
            Assert.That(session.Execute(command).duplicate);Assert.That(session.Execute(Phase()).reason,Is.EqualTo("boss_phase_already_recorded"));
            for(int i=0;i<8;i++)Dodge("phase-defense-"+i);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(6));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(6));
        }
        [TestCase(false)] [TestCase(true)]
        public void ActualBasicOrQueuedDamageCrossingHalfHealthTransitionsAndRewardsInThatSameCommand(bool queued)
        {
            Boss();S.focusBudget.secondsRemaining=1;S.Actor("enemy-1").hp=S.Actor("enemy-1").maxHp/2+1;
            if(queued)
            {
                S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.EnterFocus);string queue=Queue("fighter","attack");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=queue);session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=queue);
            }
            else {S.random.backgroundCombat=BackgroundFor(20);Run(CommandKind.BasicAttack);}
            Assert.That(S.Actor("enemy-1").hp,Is.LessThanOrEqualTo(S.Actor("enemy-1").maxHp/2));Assert.That(S.momentumLedger.phaseId,Is.EqualTo("boss-half-health"));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(2));Assert.That(S.focusBudget.rewardEvents.Single().category,Is.EqualTo("boss-phase"));
            Assert.That(session.Execute(Phase()).reason,Is.EqualTo("boss_phase_already_recorded"));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(2));
        }
        [Test] public void ConfiguredDamagingControlAndDangerousInterruptAtBossHalfHealthPayOnlyTheHigherPhaseReward()
        {
            // The live shipped Control deals zero damage. A valid configurable damage value
            // exercises this combined transaction without adding a production ability.
            Boss();S.focusBudget.secondsRemaining=1;session.Config.Action("control").damage=2;S.Actor("enemy-1").hp=S.Actor("enemy-1").maxHp/2+1;S.Actor("enemy-1").wisSaveBonus=-50;
            S.Actor("enemy-1").aiState=1;S.Actor("enemy-1").aiRemaining=10;S.Actor("enemy-1").attackGroupId="boss-windup";S.momentumLedger.registeredDangerousThreats.Add("boss-windup");
            Run(CommandKind.EnterFocus);string queue=Queue("mage","control");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=queue);session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=queue);
            Assert.That(S.Actor("enemy-1").aiState,Is.Zero);Assert.That(S.Actor("enemy-1").statuses.Any(x=>x.id=="restrained"));Assert.That(S.momentumLedger.resolvedDangerousThreats,Does.Contain("boss-windup"));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(2));Assert.That(S.focusBudget.rewardEvents.Count,Is.EqualTo(1));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(2));
        }
        [TestCase("refund")] [TestCase("reaction")] [TestCase("player")]
        public void BossAndEnvironmentSourcesRejectRefundReactionAndPlayerOrigins(string origin)
        {
            Boss();S.focusBudget.secondsRemaining=1;S.Actor("enemy-1").hp=1;var c=Phase();c.refundOrigin=origin=="refund";c.fromReaction=origin=="reaction";if(origin=="player")c.authority=CommandAuthority.Player;
            Assert.That(session.Execute(c).success,Is.False);Assert.That(S.momentumLedger.phaseId,Is.Not.EqualTo("boss-half-health"));
            c.id+="/environment";c.actionId="environment";c.rootId=c.factId="objective";S.momentumLedger.registeredObjectives.Add("objective");session.Execute(c);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
        }
        [Test] public void LegacyEconomyKeepsSpentSecondsRngAndPaidTicketAndCountsOldSourcesTowardRoomCap()
        {
            S.focusBudget.secondsRemaining=2;S.focusBudget.secondsSpent=6;S.focusBudget.rewardRoots.Add("defense/"+S.encounterId+"/old-root");Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);
            string ticket=CombatJson.Write(S.pendingRoll),rng=CombatJson.Write(S.random);var json=JObject.Parse(CombatJson.Write(S));var budget=(JObject)json["focusBudget"];
            foreach(var name in new[]{"economyVersion","maximumSeconds","lastRecoverySeconds","lastRecoverySequence","lastRecoveryKind","rewardEvents","recoveryScopes"})budget.Remove(name);
            session=new CombatSession(CombatJson.Read<CombatState>(json.ToString()),session.Config);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(2));Assert.That(S.focusBudget.secondsSpent,Is.EqualTo(6));Assert.That(CombatJson.Write(S.pendingRoll),Is.EqualTo(ticket));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(.5f));
            Run(CommandKind.RevealRoll);Run(CommandKind.Cancel);Run(CommandKind.ExitFocus);Dodge("old-root");Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
            session=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),session.Config);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(2));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(.5f));
        }
    }

    public sealed class P47FocusProfileTests : ProfileTestFixture
    {
        [Test] public void RecoveryLedgersCrossRoomsWithoutRefillAndWholeEncounterRetryRestoresTheOldLedger()
        {
            Travel("R1");Activate();CState.focusBudget.secondsRemaining=1;CState.Actor("fighter").dodgeRemaining=1;
            Assert.That(profile.ActiveCombat.ResolveIncoming("room1-defense",CState.enemies[0].id,"fighter",8,false,12,"room1-root").success);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(1.75f));Win();
            string previous=CombatJson.Write(CState.focusBudget);Travel("R2");Travel("R3");Activate();Assert.That(CombatJson.Write(CState.focusBudget),Is.EqualTo(previous));CState.Actor("fighter").dodgeRemaining=1;
            Assert.That(profile.ActiveCombat.ResolveIncoming("room3-defense",CState.enemies[0].id,"fighter",8,false,12,"room3-root").success);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(2.5f));Assert.That(CState.focusBudget.recoveryScopes.Count,Is.EqualTo(2));
            Run(ProfileCommandKind.RetryStoryEncounter);Assert.That(CombatJson.Write(CState.focusBudget),Is.EqualTo(previous));Assert.That(CState.focusBudget.rewardEvents.Count,Is.EqualTo(1));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void ActualBossPhaseAndRecoveryAreOneAtomicProfileTransaction(CombatWriteStage stage)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P47FocusBoss",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try
            {
                First();Third();Upper();Travel("R7");Activate();CState.focusBudget.secondsRemaining=1;var boss=CState.enemies.Single(e=>CState.world.strings["enemyTemplate/"+e.id]=="boss");boss.hp=boss.maxHp/2;Assert.That(profile.SaveCurrent("capture-boss-position").success);
                profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);Assert.That(profile.SaveCurrent("before-phase").success);
                var c=new CombatCommand{id="p47-boss-phase",kind=CommandKind.AwardFact,authority=CommandAuthority.System,actorId="fighter",targetId=boss.id,rootId=boss.id+"/boss-half-health",factId=boss.id+"/boss-half-health",actionId="focus-boss-phase",qualified=true};
                string state=CombatJson.Write(profile.State),combat=CombatJson.Write(CState),disk=File.ReadAllText(path);profile.Store.WriteFault=p=>{if(p==stage)throw new IOException("p47-boss-phase-fault");};
                Assert.That(profile.ExecuteCombat(c).success,Is.False);Assert.That(CombatJson.Write(profile.State),Is.EqualTo(state));Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
                profile=ProfileSession.Load(path,BaseConfig,profile.Config);var committed=profile.ExecuteCombat(c);Assert.That(committed.success,committed.reason);Assert.That(CState.momentumLedger.phaseId,Is.EqualTo("boss-half-health"));Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(2));Assert.That(profile.ExecuteCombat(c).duplicate);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(2));
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void PaidReactionRecoveryFailureRollsBackResultResourcesRngAndBothFiles(CombatWriteStage stage)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P47FocusReaction",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try
            {
                profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);Assert.That(profile.SaveCurrent("initial").success);Travel("R1");Activate();CState.focusBudget.secondsRemaining=1;
                var enemy=CState.enemies[0];enemy.x=CState.Actor("fighter").x+1;enemy.z=CState.Actor("fighter").z;enemy.attackBonus=0;CState.Actor("fighter").armorClass=10;CState.random.backgroundCombat=Stream("background",12);
                var incoming=profile.ActiveCombat.ResolveIncoming("paid-incoming",enemy.id,"fighter",8,false,12,"paid-root");Assert.That(incoming.success&&incoming.deferred);
                Core(CommandKind.BeginReaction,c=>{c.reactionWindowId=incoming.reactionWindowId;c.reactionId="parry";});CState.random.visibleTactical=CombatRuleTests.StreamFor(20);Core(CommandKind.ConfirmReactionRoll);Core(CommandKind.RevealRoll);
                string state=CombatJson.Write(profile.State),combat=CombatJson.Write(CState),main=File.ReadAllText(path),backup=File.ReadAllText(path+".bak");var command=new CombatCommand{id="commit-paid-recovery",kind=CommandKind.CommitReaction,actorId="fighter"};
                profile.Store.WriteFault=p=>{if(p==stage)throw new IOException("p47-recovery-fault");};Assert.That(profile.ExecuteCombat(command).success,Is.False);Assert.That(CombatJson.Write(profile.State),Is.EqualTo(state));Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));Assert.That(File.ReadAllText(path),Is.EqualTo(main));Assert.That(File.ReadAllText(path+".bak"),Is.EqualTo(stage==CombatWriteStage.AfterReplace||stage==CombatWriteStage.AfterVerify?main:backup));
                profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));Assert.That(profile.ExecuteCombat(command).success);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(1.25f));Assert.That(profile.ExecuteCombat(command).duplicate);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(1.25f));
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        void ToElite()
        {First();Third();Travel("R2");Position(48,11);Run(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});Travel("R4");Activate();Win();Travel("R5");Activate();Position(26,22);}
        ProfileCommand Environment()=>P(ProfileCommandKind.UseEnvironment,c=>{c.actorId="fighter";c.selectionId="R5_SHIFT";c.sourceFactId="physical-cover-shift";c.qualified=true;});
        [Test] public void ImportantEnvironmentPaysOnlyWithActualWorldChangeAndCanNeverBeRepeated()
        {
            ToElite();CState.focusBudget.secondsRemaining=1;var c=Environment();c.qualified=false;Assert.That(profile.Execute(c).success,Is.False);Assert.That(CState.focusBudget.secondsRecovered,Is.Zero);Assert.That(M.HasFlag("COVER_SHIFTED"),Is.False);
            c.qualified=true;Assert.That(profile.Execute(c).success);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(1));Assert.That(M.HasFlag("COVER_SHIFTED"));Assert.That(CState.Actor("fighter").bonusRemaining,Is.GreaterThan(0));Assert.That(profile.Execute(c).duplicate);Assert.That(profile.Execute(Environment()).success,Is.False);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(1));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void EnvironmentRecoveryAndWorldFlagCommitTogetherOrRollBackTogether(CombatWriteStage stage)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P47FocusProfile",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try
            {
                ToElite();CState.focusBudget.secondsRemaining=1;Assert.That(profile.SaveCurrent("capture-environment-state").success);profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);Assert.That(profile.SaveCurrent("before-environment").success);
                string state=CombatJson.Write(profile.State),combat=CombatJson.Write(CState),disk=File.ReadAllText(path);var c=Environment();profile.Store.WriteFault=p=>{if(p==stage)throw new IOException("p47-environment-fault");};
                Assert.That(profile.Execute(c).success,Is.False);Assert.That(CombatJson.Write(profile.State),Is.EqualTo(state));Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
                profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(profile.Execute(c).success);Assert.That(CState.focusBudget.secondsRecovered,Is.EqualTo(1));Assert.That(M.HasFlag("COVER_SHIFTED"));
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        [Test] public void OldP46FileRetainsRemainingBudgetAndHashesAndDoesNotRewriteOnRead()
        {
            string dir=Path.Combine(Path.GetTempPath(),"P47FocusMigration",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try
            {
                Travel("R1");Activate();Core(CommandKind.EnterFocus);profile.Tick(0,true,4);Core(CommandKind.ConfirmRoll);profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);Assert.That(profile.SaveCurrent("paid").success);
                string rng=CombatJson.Write(CState.random),ticket=CombatJson.Write(CState.pendingRoll),config=profile.State.baseCombatConfigHash;float remaining=CState.focusBudget.secondsRemaining;
                var envelope=JObject.Parse(File.ReadAllText(path));var payload=JObject.Parse((string)envelope["payload"]);
                foreach(var obj in payload.Descendants().OfType<JObject>().Where(o=>o["rulesProfileId"]!=null))foreach(string name in new[]{"economyVersion","maximumSeconds","lastRecoverySeconds","lastRecoverySequence","lastRecoveryKind","rewardEvents","recoveryScopes"})((JObject)obj["focusBudget"]).Remove(name);
                envelope["payload"]=payload.ToString(Newtonsoft.Json.Formatting.None);envelope["integrity"]=CombatStore.Digest((string)envelope["payload"]);File.WriteAllText(path,envelope.ToString(Newtonsoft.Json.Formatting.None));byte[] old=File.ReadAllBytes(path);
                profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(File.ReadAllBytes(path),Is.EqualTo(old));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(remaining));Assert.That(CState.focusBudget.maximumSeconds,Is.EqualTo(20));Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(config));Assert.That(CombatJson.Write(CState.random),Is.EqualTo(rng));Assert.That(CombatJson.Write(CState.pendingRoll),Is.EqualTo(ticket));
                Assert.That(profile.SaveCurrent("migrated-p47").success);profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(remaining));
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
    }
}
