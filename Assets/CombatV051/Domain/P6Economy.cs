using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        void TickP6Economy(CombatState s,float dt)
        {
            if(dt<=0)return;if(s.charges>=s.chargeCap){s.momentum=0;return;}
            if(P6HasEffectiveThreat(s))s.momentumLedger.passiveEarned+=AddMomentumP4(s,(Config.p6.passiveMomentumPerSimulationSecond+V11Buff("core_devotion"))*dt);
        }
        bool P6HasEffectiveThreat(CombatState s)
        {
            if(!P6IsTraining(s))return HasP4Threat(s);
            // Practice remains a separate in-memory profile, but its demonstrated
            // recharge is the same simulated clock and requires real world evidence.
            return s.inCombat&&!s.encounterResolved&&!s.momentumLedger.disengaged&&s.enemies.Any(e=>e.Alive&&s.actors.Any(a=>a.Alive&&Distance(a,e)<=Config.effectiveCombatRange));
        }
        void P6AwardResource(CombatState s,string root,string kind,float nominal)
        {
            if(Config.p6==null||s.p6==null||!s.inCombat||s.encounterResolved||string.IsNullOrEmpty(root)||nominal<=0||!s.actors.Any(a=>a.Alive))return;
            var e=s.p6.resourceEvents.Find(x=>x.root==root);if(e==null){e=new P6ResourceEvent{root=root,kind=kind};s.p6.resourceEvents.Add(e);}
            float delta=Math.Max(0,nominal-e.qualifiedAmount);if(delta<=0)return;e.qualifiedAmount=nominal;e.kind=kind;float actual=AddMomentumP4(s,delta);e.creditedAmount+=actual;e.discardedAmount+=delta-actual;
        }
        void P6AwardMark(CombatState s,string root)
        {
            if(Config.p6==null||s.p6.resourceEvents.Any(x=>x.root==root&&x.kind=="radiant-cooperation")||s.p6.markRewardCount>=Config.p6.markRewardsPerEncounter)return;
            s.p6.markRewardCount++;P6AwardResource(s,root,"radiant-cooperation",Config.p6.markMomentum);AwardP6FocusRecovery(s,root,"combo");
        }
        void P6ObserveDefense(CombatState s,PendingIncoming incoming)
        {
            if(Config.p6==null||s.p6==null||incoming.baseDamage<=0||!incoming.damageApplied||incoming.stage!="Applied"||s.Actor(incoming.defenderId)?.isEnemy!=false||s.Actor(incoming.attackerId)?.isEnemy!=true)return;
            var group=s.pendingThreatEvents.Find(g=>g.id==incoming.reactionEventId);if(group?.fromReaction==true)return;
            bool defense=incoming.reason=="parried"||incoming.reason=="protected_by_preparation"||incoming.guarded&&incoming.hitBeforeReaction;
            bool dodge=incoming.reason=="avoided_by_dodge"&&s.momentumLedger.registeredDangerousThreats.Contains(incoming.rootId);
            if(!defense&&!dodge)return;if(s.p6.resourceEvents.Any(x=>x.root==incoming.rootId))return;
            if(s.simulationSeconds-s.p6.lastGuardSimulationTime+0.0001f<Config.p6.guardInterval)return;
            s.p6.lastGuardSimulationTime=s.simulationSeconds;V11Defense(s,incoming);P6AwardResource(s,incoming.rootId,"guard",Config.p6.guardMomentum+V11Buff("team_ward"));AwardP6FocusRecovery(s,incoming.rootId,"defense");
        }
    }
}
