using System;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class ActionExclusionTests
    {
        CombatSession session;int sequence;
        CombatState S {get{return session.State;}}
        ActorState Fighter {get{return S.Actor("fighter");}}
        [SetUp] public void Setup(){var state=CombatState.Create("exclusion","p0",11);state.charges=1;session=new CombatSession(state,new CombatConfig{passiveMomentumPerSecond=0});sequence=0;}
        CombatCommand C(CommandKind kind){return new CombatCommand{id="exclude-"+(++sequence),kind=kind};}
        CombatResult Run(CommandKind kind,string queueId=null){var c=C(kind);c.queueId=queueId;var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        string PrepareQueue(bool executing)
        {
            session.Config.actions[0].spellCost=1;Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);string id=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);if(executing)Run(CommandKind.StartQueued,id);return id;
        }
        [TestCase(CommandKind.StartAim,"queued")] [TestCase(CommandKind.StartGuard,"queued")] [TestCase(CommandKind.StartDodge,"queued")]
        [TestCase(CommandKind.StartAim,"executing")] [TestCase(CommandKind.StartGuard,"executing")] [TestCase(CommandKind.StartDodge,"executing")]
        [TestCase(CommandKind.StartAim,"windup")] [TestCase(CommandKind.StartGuard,"windup")] [TestCase(CommandKind.StartDodge,"windup")]
        [TestCase(CommandKind.StartAim,"stagger")] [TestCase(CommandKind.StartGuard,"stagger")] [TestCase(CommandKind.StartDodge,"stagger")]
        public void CommittedWindingOrStaggeredActorCannotStartVoluntaryActions(CommandKind kind,string fixture)
        {
            if(fixture=="queued"||fixture=="executing")PrepareQueue(fixture=="executing");
            else if(fixture=="windup")Fighter.windupRemaining=.3f;else Fighter.staggerRemaining=.5f;
            Fighter.lockedX=.8f;Fighter.lockedZ=.6f;string before=CombatJson.Write(S);var c=C(kind);c.x=-7;c.z=2;
            var r=session.Execute(c);Assert.That(r.success,Is.False);Assert.That(r.reason,Is.EqualTo("actor_busy"));Assert.That(CombatJson.Write(S),Is.EqualTo(before),"Rejected input must not change direction, protection, cooldown, holds, RNG or fees.");
        }
        [TestCase(false,CommandKind.StartDodge)] [TestCase(true,CommandKind.StartDodge)] [TestCase(false,CommandKind.StartGuard)] [TestCase(true,CommandKind.StartGuard)]
        public void RejectedDefenseDoesNotAvoidIncomingOrEarnMomentum(bool executing,CommandKind defense)
        {
            PrepareQueue(executing);Assert.That(session.Execute(C(defense)).success,Is.False);int hp=Fighter.hp;ulong draws=S.random.backgroundCombat.drawCount;
            var r=session.ResolveIncoming("real-incoming","enemy-1","fighter",3,true,30,"one-attack");Assert.That(r.success);Assert.That(r.reason,Is.EqualTo("save_failed"));Assert.That(r.damage,Is.EqualTo(3));Assert.That(Fighter.hp,Is.EqualTo(hp-3));Assert.That(S.momentum,Is.Zero);Assert.That(S.random.backgroundCombat.drawCount,Is.GreaterThan(draws));
        }
        [TestCase("aim")] [TestCase("guard")] [TestCase("dodge")]
        public void QueueCannotStartWhileVoluntaryActionIsAlreadyActive(string fixture)
        {
            string id=PrepareQueue(false);
            // Simulate an older saved combination; the startup gate must protect its costs as well.
            if(fixture=="aim"){Fighter.aimPreparing=true;Fighter.aimRemaining=.4f;}else if(fixture=="guard")Fighter.guardRemaining=.4f;else Fighter.dodgeRemaining=.2f;
            string before=CombatJson.Write(S);var c=C(CommandKind.StartQueued);c.queueId=id;var r=session.Execute(c);Assert.That(r.success,Is.False);Assert.That(r.reason,Is.EqualTo("actor_busy"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(Fighter.spellSlots,Is.EqualTo(1));Assert.That(Fighter.majorRemaining,Is.Zero);
        }
        [TestCase(false)] [TestCase(true)] public void ExplicitQueueCancelReleasesActorForDodgeWithoutRefundingPaidCharge(bool executing)
        {
            string id=PrepareQueue(executing);Run(CommandKind.CancelQueued,id);var c=C(CommandKind.StartDodge);c.x=3;c.z=4;Assert.That(session.Execute(c).success);Assert.That(Fighter.lockedX,Is.EqualTo(.6f).Within(.0001));Assert.That(Fighter.lockedZ,Is.EqualTo(.8f).Within(.0001));Assert.That(Fighter.dodgeRemaining,Is.GreaterThan(0));Assert.That(S.charges,Is.Zero);Assert.That(Fighter.spellSlots,Is.EqualTo(executing?0:1));
        }
        [Test] public void MajorRecoveryAloneDoesNotBlockLegalDodgeAfterBasicAttack()
        {
            S.Actor("enemy-1").x=-1;Run(CommandKind.BasicAttack);Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));Assert.That(Fighter.heldBy,Is.Null);Run(CommandKind.StartDodge);Assert.That(Fighter.dodgeRemaining,Is.GreaterThan(0));
        }
        [TestCase(3f,4f,.6f,.8f)] [TestCase(0f,0f,0f,1f)] [TestCase(-10f,0f,-1f,0f)]
        public void SuccessfulDodgeNormalizesAndWritesDirectionAtomically(float x,float z,float expectedX,float expectedZ)
        {
            var c=C(CommandKind.StartDodge);c.x=x;c.z=z;Assert.That(session.Execute(c).success);Assert.That(Fighter.lockedX,Is.EqualTo(expectedX).Within(.0001));Assert.That(Fighter.lockedZ,Is.EqualTo(expectedZ).Within(.0001));Assert.That(session.Execute(c).duplicate);Assert.That(Fighter.dodgeCooldown,Is.EqualTo(session.Config.dodgeRecovery));
        }
        [Test] public void NonFiniteDodgeDirectionRejectsWithoutProtectionOrStateChange(){string before=CombatJson.Write(S);var c=C(CommandKind.StartDodge);c.x=float.NaN;c.z=1;Assert.That(session.Execute(c).reason,Is.EqualTo("invalid_dodge_direction"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
    }
}
