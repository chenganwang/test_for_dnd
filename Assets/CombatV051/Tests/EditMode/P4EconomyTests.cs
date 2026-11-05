using System;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P4EconomyTests
    {
        CombatSession session;int sequence;
        CombatState S=>session.State;
        [SetUp] public void Setup(){var config=new CombatConfig{passiveMomentumPerSecond=0};var state=CombatState.CreatePartyP1("economy","p4",19,config);state.charges=0;foreach(var a in state.actors){a.x=0;a.z=0;}state.enemies[0].x=1;state.enemies[0].hp=state.enemies[0].maxHp=1000;session=new CombatSession(state,config);sequence=0;}
        CombatResult Fact(string kind,string root,string chain=null,bool register=true)
        {
            if(register){if(kind=="threat-kill")S.momentumLedger.registeredHighThreats.Add(root);else if(!S.momentumLedger.registeredObjectives.Contains(root))S.momentumLedger.registeredObjectives.Add(root);}
            return session.Execute(new CombatCommand{id="fact-"+(++sequence),kind=CommandKind.AwardFact,authority=CommandAuthority.System,actionId=kind,rootId=root,factId=root,qualified=true,paidChainId=chain});
        }
        [Test] public void NewEncounterHasTwoButLargerCapDoesNotGrantThird(){var fresh=CombatState.CreatePartyP1("new","p4",1);fresh.chargeCap=3;Assert.That(fresh.charges,Is.EqualTo(2));Assert.That(fresh.momentum,Is.Zero);}
        [Test] public void FullCapCannotBankPassiveOrQualifiedAwardForAfterSpending()
        {
            S.charges=2;session.Config.passiveMomentumPerSecond=4;session.Tick(50);Assert.That(S.momentumLedger.passiveEarned,Is.Zero);Assert.That(Fact("environment","one").success);Assert.That(S.momentum,Is.Zero);Assert.That(S.momentumLedger.fullCapDiscarded,Is.EqualTo(40));
            S.charges=1;Assert.That(Fact("environment","one").success);Assert.That(S.momentum,Is.Zero);session.Tick(1);Assert.That(S.momentum,Is.EqualTo(4));
        }
        [Test] public void OneHundredConvertsAndFullCapDiscardsRemainder(){S.charges=1;S.momentum=95;Assert.That(Fact("environment","one").success);Assert.That(S.charges,Is.EqualTo(2));Assert.That(S.momentum,Is.Zero);Assert.That(S.momentumLedger.fullCapDiscarded,Is.EqualTo(35));}
        [Test] public void PassiveUsesSimulationSecondsAndFreezeDoesNotAccumulate()
        {
            session.Config.passiveMomentumPerSecond=4;Assert.That(session.Execute(new CombatCommand{id="focus",kind=CommandKind.EnterFocus}).success);session.Tick(5);Assert.That(S.momentum,Is.EqualTo(2).Within(.0001));S.charges=1;
            Assert.That(session.Execute(new CombatCommand{id="roll",kind=CommandKind.ConfirmRoll}).success);float before=S.momentum;session.Tick(100);Assert.That(S.momentum,Is.EqualTo(before));
        }
        [TestCase("training")] [TestCase("context")] [TestCase("distant")] [TestCase("dead")] [TestCase("resolved")] [TestCase("disengaged")]
        public void IneligibleEncountersCannotGeneratePassiveOrTacticalAwards(string fixture)
        {
            session.Config.passiveMomentumPerSecond=4;if(fixture=="training")S.momentumLedger.training=true;if(fixture=="context")S.world.strings["context"]="TRAINING";if(fixture=="distant")S.enemies[0].x=100;if(fixture=="dead")S.enemies[0].hp=0;if(fixture=="resolved")S.encounterResolved=true;if(fixture=="disengaged")S.momentumLedger.disengaged=true;
            session.Tick(20);Fact("environment","one");Assert.That(S.momentum,Is.Zero);Assert.That(S.momentumLedger.totalEarned,Is.Zero);
        }
        [Test] public void BasicsHaveOneSecondSharedTeamCooldown()
        {
            foreach(string id in new[]{"fighter","rogue","mage"}){S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Assert.That(session.Execute(new CombatCommand{id="basic-"+id,kind=CommandKind.BasicAttack,actorId=id}).success);}
            Assert.That(S.momentum,Is.EqualTo(1));session.Tick(1.21f);S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Assert.That(session.Execute(new CombatCommand{id="later",kind=CommandKind.BasicAttack}).success);Assert.That(S.momentum,Is.EqualTo(2));
        }
        [Test] public void SameRootOnlyPaysMaximumRegardlessOfCallbackOrder()
        {
            Assert.That(Fact("threat-kill","same").success);Assert.That(S.momentum,Is.EqualTo(25));Assert.That(Fact("interrupt","same").success);Assert.That(S.momentum,Is.EqualTo(40));Assert.That(Fact("environment","same").success);Assert.That(S.momentum,Is.EqualTo(40));Assert.That(S.momentumLedger.p4Roots.Single().highest,Is.EqualTo(40));
        }
        [TestCase("Normal",100)] [TestCase("Elite",150)] [TestCase("Boss",100)]
        public void TacticalBudgetsLeavePassiveAvailable(string kind,float budget)
        {
            S.chargeCap=3;CombatSession.ConfigureP4Encounter(S,kind);for(int i=0;i<7;i++)Assert.That(Fact("environment","root-"+i).success);
            Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(budget));Assert.That(S.momentumLedger.totalEarned,Is.EqualTo(budget));session.Config.passiveMomentumPerSecond=4;session.Tick(1);Assert.That(S.momentumLedger.totalEarned,Is.EqualTo(budget+4));
        }
        [Test] public void OnePaidChainCannotRestoreMoreThanSixtyAcrossDistinctRoots()
        {
            CombatSession.ConfigureP4Encounter(S,"Elite");for(int i=0;i<3;i++)Assert.That(Fact("environment","root-"+i,"paid-ticket").success);Assert.That(S.momentum,Is.EqualTo(60));Assert.That(S.momentumLedger.paidChains.Single().awarded,Is.EqualTo(60));
            Assert.That(Fact("environment","other-root","other-ticket").success);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.momentum,Is.Zero);
        }
        [Test] public void UnregisteredObjectiveAndRefundCannotMintResources()
        {
            Assert.That(Fact("environment","unknown",register:false).success);Assert.That(Fact("interrupt","unknown-attack",register:false).success);Assert.That(Fact("threat-kill","unknown-enemy",register:false).success);Assert.That(S.momentum,Is.Zero);
            var c=new CombatCommand{id="refund",kind=CommandKind.AwardFact,authority=CommandAuthority.System,actionId="environment",qualified=true,rootId="refund",factId="refund",refundOrigin=true};S.momentumLedger.registeredObjectives.Add("refund");string before=CombatJson.Write(S);Assert.That(session.Execute(c).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void SaveRestorePreservesRootAndChainDeduplication()
        {
            Fact("environment","a","paid");session=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),session.Config);Fact("environment","a","paid");Fact("environment","b","paid");Assert.That(S.momentum,Is.EqualTo(60));Assert.That(S.momentumLedger.p4Roots.Count,Is.EqualTo(2));
        }
        [Test] public void ReconfigurationDoesNotRefillOrEraseExistingBudget(){Fact("environment","a");CombatSession.ConfigureP4Encounter(S,"Normal",objectiveRoots:new[]{"a","b"});Assert.That(S.charges,Is.Zero);Assert.That(S.momentum,Is.EqualTo(40));Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(40));}
        [Test] public void BossBudgetPhaseIsOneWayAndKeepsChargesSourcesAndPaidChain()
        {
            CombatSession.ConfigureP4Encounter(S,"Boss");Fact("environment","a","paid");int charge=S.charges;float momentum=S.momentum;
            Assert.That(CombatSession.AdvanceP4BudgetPhase(S,"half-health"));Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.momentum,Is.EqualTo(momentum));Assert.That(S.momentumLedger.tacticalAwarded,Is.Zero);
            Assert.That(CombatSession.AdvanceP4BudgetPhase(S,"initial"),Is.False);Assert.That(CombatSession.AdvanceP4BudgetPhase(S,"half-health"),Is.False);Fact("environment","a","paid");Assert.That(S.momentum,Is.EqualTo(40));Fact("environment","b","paid");Assert.That(S.momentum,Is.EqualTo(60));Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(20));
        }
        [Test] public void AlreadyRegisteredKillCannotBeAwardedByLaterDifferentRoots()
        {
            S.momentumLedger.registeredHighThreats.Add(S.enemies[0].id);S.enemies[0].hp=1;S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Assert.That(session.Execute(new CombatCommand{id="kill",kind=CommandKind.BasicAttack}).success);Assert.That(S.momentum,Is.EqualTo(25));Assert.That(S.momentumLedger.resolvedThreats,Does.Contain("enemy-1"));
            S.enemies[0].hp=1;S.Actor("rogue").majorRemaining=0;S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Assert.That(session.Execute(new CombatCommand{id="repeat-fixture-kill",actorId="rogue",kind=CommandKind.BasicAttack}).success);Assert.That(S.momentum,Is.EqualTo(25));
        }
    }
}
