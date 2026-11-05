using System;
using System.IO;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P46DecisionClockTests:PartyTestFixture
    {
        [Test] public void SeparateFocusDeltaPreservesWorldSimulationAndRealActivityDelta()
        {
            Run(CommandKind.EnterFocus);float simulated=S.simulationSeconds,real=S.realCombatSeconds;
            float dt=session.Tick(10,true,.25f);
            Assert.That(dt,Is.EqualTo(10*session.Config.focusScale));Assert.That(S.simulationSeconds,Is.EqualTo(simulated+dt));Assert.That(S.realCombatSeconds,Is.EqualTo(real+10));
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14.75f));Assert.That(S.focusBudget.secondsSpent,Is.EqualTo(.25f));
            session.Tick(0,true,.75f);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));Assert.That(S.simulationSeconds,Is.EqualTo(simulated+dt));
        }
        [Test] public void P0SeparateDecisionDeltaAndLegacyOverloadsKeepTheirSpecifiedClocks()
        {
            session=new CombatSession(CombatState.Create("clock-p0","run",5),new CombatConfig());Run(CommandKind.EnterFocus);
            session.Tick(4,true,.5f);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14.5f));
            session.Tick(.5f);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));
            session.Tick(.5f,true);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(13.5f));
        }
        [Test] public void SeparateReactionDeltaDoesNotBorrowPriorFrameOrRefreshAfterChoice()
        {
            var w=Offer("fighter",12,8);string group=S.activeReactionEventId;float real=S.realCombatSeconds;
            session.Tick(9,true,.25f);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(2.25f));Assert.That(S.realCombatSeconds,Is.EqualTo(real+9));
            Begin(w,"parry");session.Tick(0,true,.25f);Assert.That(S.activeReactionEventId,Is.EqualTo(group));Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(2));
            int hp=S.Actor("fighter").hp;session.Tick(.01f,true,2);Assert.That(S.activePendingEvent,Is.Null);Assert.That(S.Actor("fighter").hp,Is.LessThan(hp));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));
        }
        [Test] public void UnavailableUiPauseAndFreezeRejectDecisionElapsedButStillTrackAllowedRealTime()
        {
            Run(CommandKind.EnterFocus);session.Tick(3,false,3);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));
            Run(CommandKind.Pause);session.Tick(4,true,4);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));Run(CommandKind.Resume);
            Run(CommandKind.ConfirmRoll);ulong draws=S.random.visibleTactical.drawCount;session.Tick(5,true,5);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));
            Run(CommandKind.RevealRoll);session.Tick(5,true,0);Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));
            session.Tick(0,true,15);Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(S.charges,Is.EqualTo(1));
        }
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(-1f)]
        public void InvalidDecisionDeltaFailsBeforeAnyStateMutation(float delta)
        {
            Run(CommandKind.EnterFocus);string before=CombatJson.Write(S);Assert.Throws<InvalidOperationException>(()=>session.Tick(2,true,delta));Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void IndependentDeadlineWriteFailureRetainsPaidRollAndAllClocks()
        {
            string dir=Path.Combine(Path.GetTempPath(),"P46-Decision",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"combat.json");
            try
            {
                session=new CombatSession(S.Clone(),session.Config,path);Assert.That(session.Save().success);Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);session.Tick(0,true,14);Assert.That(session.Save().success);
                string before=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=stage=>{if(stage==CombatWriteStage.AfterReplace)throw new IOException("decision-clock-write-fault");};
                Assert.That(session.Tick(.25f,true,1),Is.Zero);Assert.That(session.LastTickError,Does.Contain("decision-clock-write-fault"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
    }
    public sealed class P46DecisionProfileClockTests:ProfileTestFixture
    {
        [Test] public void ProfileForwardsDecisionTimeWhileRetainingOriginalRunActivityClock()
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);float before=profile.ActiveMode.elapsedRealSeconds;
            profile.Tick(4,true,.25f);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(14.75f));Assert.That(profile.ActiveMode.elapsedRealSeconds,Is.EqualTo(before+4));
            string snapshot=CombatJson.Write(profile.State),combat=CombatJson.Write(CState);Assert.Throws<InvalidOperationException>(()=>profile.Tick(3,true,float.NaN));
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(snapshot));Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));
        }
    }
}
