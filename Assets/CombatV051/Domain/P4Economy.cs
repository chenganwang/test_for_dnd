using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class P4RewardRoot
    {
        public string id, category, paidChainId;
        public float highest, credited;
        public bool tactical, refund;
    }
    [Serializable] public sealed class P4RewardChain { public string id; public float awarded; }
    public sealed partial class MomentumLedger
    {
        public string encounterKind="Normal",phaseId="initial";
        public float tacticalAwarded,basicEarned,defenseEarned,setupEarned,objectiveEarned,highThreatEarned,fullCapDiscarded;
        public List<P4RewardRoot> p4Roots=new List<P4RewardRoot>();
        public List<P4RewardChain> paidChains=new List<P4RewardChain>();
        public List<string> registeredObjectives=new List<string>(),registeredHighThreats=new List<string>(),resolvedThreats=new List<string>();
        public List<string> registeredDangerousThreats=new List<string>(),resolvedDangerousThreats=new List<string>(),closedBudgetPhases=new List<string>();
        public bool training,disengaged;
    }
    public sealed partial class CombatCommand
    {
        public string paidChainId;
        public bool refundOrigin;
    }
    public sealed partial class CombatSession
    {
        static void ValidateP4Configuration(CombatConfig c)
        {
            Require(new[]{c.tacticalBudgetNormal,c.tacticalBudgetElite,c.tacticalBudgetBossPhase,c.paidChainMomentumCap,c.basicHitInternalCooldown,c.preparationSeconds,c.preparationEffectSeconds,c.preparationRepeatSeconds,c.basicContactSeconds,c.preparationRange,c.orderArrivalDistance,c.orderTimeoutSeconds}.All(x=>Finite(x)&&x>0),"invalid_p4_config");
            Require(c.paidChainMomentumCap<=60&&c.actorOverrides!=null&&c.actorOverrides.All(x=>x!=null&&!string.IsNullOrEmpty(x.actorId)&&Finite(x.protectionRange)&&x.protectionRange>0&&Finite(x.flankWindowSeconds)&&x.flankWindowSeconds>0&&Finite(x.studySeconds)&&x.studySeconds>0&&Finite(x.preparationMoveMultiplier)&&x.preparationMoveMultiplier>0&&x.preparationMoveMultiplier<=1)&&c.actorOverrides.Select(x=>x.actorId).Distinct().Count()==c.actorOverrides.Count,"invalid_p4_actor_config");
        }
        // Existing Award/AddMomentum/DrainMomentum are adapters to this single ledger.
        void AwardP4(CombatState s,string kind,string root,string template,bool qualified)
        {
            Require(qualified&&!string.IsNullOrEmpty(root),"unqualified_world_fact");
            float value;
            switch(kind)
            {
                case "basic-hit":value=Config.basicHitMomentum;break;
                case "perfect-dodge":value=Config.perfectDodgeMomentum;break;
                case "parry":value=Config.parryMomentum;break;
                case "guard":value=Config.guardMomentum;break;
                case "setup":case "flank":case "weakpoint":value=Config.flankMomentum;break;
                case "interrupt":value=Config.interruptMomentum;break;
                case "environment":value=Config.environmentMomentum;break;
                case "threat-kill":value=Config.threatKillMomentum;break;
                case "aim":return; // Completing preparation is not a repeatable resource faucet.
                default:throw new InvalidOperationException("unknown_momentum_source");
            }
            AwardP4Root(s,kind,root,value,kind!="basic-hit",null,false);
        }
        void AwardP4Root(CombatState s,string category,string root,float amount,bool tactical,string chainId,bool refund)
        {
            if(Config.p6!=null)return; // P6 has its own bounded real-outcome awards, sharing the same balance.
            if(!s.inCombat||s.encounterResolved||s.momentumLedger.training||s.momentumLedger.disengaged||IsP4Training(s)||refund)return;
            var ledger=s.momentumLedger;
            if(category!="threat-kill"&&category!="registered-kill"&&category!="interrupt"&&!HasP4Threat(s)&&!(category=="setup"&&P5EffectiveRewardRoot(s,root)))return;
            if((category=="environment"||category=="objective-interrupt")&&!ledger.registeredObjectives.Contains(root))return;
            if(category=="threat-kill"&&!ledger.registeredHighThreats.Contains(root))return;
            var item=ledger.p4Roots.Find(x=>x.id==root);
            if(item==null){item=new P4RewardRoot{id=root};ledger.p4Roots.Add(item);}
            if(item.refund)return;
            if(category=="basic-hit")
            {
                if(item.highest>0||s.simulationSeconds-ledger.lastBasicHit+0.00001f<Config.basicHitInternalCooldown)return;
                ledger.lastBasicHit=s.simulationSeconds;
            }
            amount=Math.Max(0,amount);
            if(amount<=item.highest)return;
            float previous=item.highest,delta=amount-previous;
            item.highest=amount;item.category=category;item.tactical=tactical;item.paidChainId=chainId??item.paidChainId;
            if(!ledger.roots.Contains(root))ledger.roots.Add(root);
            if(tactical)
            {
                float budget=ledger.encounterKind=="Elite"?Config.tacticalBudgetElite:ledger.encounterKind=="Boss"?Config.tacticalBudgetBossPhase:Config.tacticalBudgetNormal;
                delta=Math.Min(delta,Math.Max(0,budget-ledger.tacticalAwarded));
            }
            if(!string.IsNullOrEmpty(item.paidChainId))
            {
                var chain=ledger.paidChains.Find(x=>x.id==item.paidChainId);
                if(chain==null){chain=new P4RewardChain{id=item.paidChainId};ledger.paidChains.Add(chain);}
                delta=Math.Min(delta,Math.Max(0,Config.paidChainMomentumCap-chain.awarded));
                chain.awarded+=delta;
            }
            // Budget and root qualifications are consumed even if full: there is no pending award.
            if(tactical)ledger.tacticalAwarded+=delta;
            float earned=AddMomentumP4(s,delta);item.credited+=earned;
            switch(category)
            {
                case "basic-hit":ledger.basicEarned+=earned;break;
                case "guard":case "parry":case "perfect-dodge":ledger.defenseEarned+=earned;break;
                case "setup":case "flank":case "weakpoint":ledger.setupEarned+=earned;break;
                case "interrupt":case "objective-interrupt":case "environment":ledger.objectiveEarned+=earned;break;
                case "threat-kill":case "registered-kill":ledger.highThreatEarned+=earned;break;
            }
        }
        float AddMomentumP4(CombatState s,float amount)
        {
            amount=Math.Max(0,amount);var ledger=s.momentumLedger;
            if(s.charges>=s.chargeCap){s.momentum=0;ledger.fullCapDiscarded+=amount;return 0;}
            float room=Math.Max(0,100*(s.chargeCap-s.charges)-s.momentum);
            float earned=Math.Min(room,amount);ledger.fullCapDiscarded+=amount-earned;
            s.momentum+=earned;ledger.totalEarned+=earned;DrainMomentumP4(s);return earned;
        }
        static void DrainMomentumP4(CombatState s)
        {
            while(s.momentum>=100&&s.charges<s.chargeCap){s.momentum-=100;s.charges++;}
            if(s.charges>=s.chargeCap)s.momentum=0;
            else s.momentum=Math.Max(0,Math.Min(99.99999f,s.momentum));
        }
        bool HasP4Threat(CombatState s)
        {
            if(!s.inCombat||s.encounterResolved||s.momentumLedger.training||s.momentumLedger.disengaged||IsP4Training(s))return false;
            return s.enemies.Any(e=>e.Alive&&s.actors.Any(a=>a.Alive&&Distance(a,e)<=Config.effectiveCombatRange));
        }
        static bool IsP4Training(CombatState s)=>s.world.strings.TryGetValue("context",out var context)&&context=="TRAINING";
        void TickP4Economy(CombatState s,float dt)
        {
            if(dt<=0)return;
            if(s.charges>=s.chargeCap){s.momentum=0;return;}
            if(HasP4Threat(s))s.momentumLedger.passiveEarned+=AddMomentumP4(s,Config.passiveMomentumPerSecond*dt);
        }
        // Profile setup calls this on a newly registered encounter, never on re-entry/load.
        public static void ConfigureP4Encounter(CombatState state,string kind,bool training=false,IEnumerable<string> objectiveRoots=null,IEnumerable<string> highThreatRoots=null)
        {
            state.momentumLedger.encounterKind=kind??"Normal";state.momentumLedger.training=training;
            if(objectiveRoots!=null)state.momentumLedger.registeredObjectives=objectiveRoots.Distinct().ToList();
            if(highThreatRoots!=null)state.momentumLedger.registeredHighThreats=highThreatRoots.Distinct().ToList();
        }
        public static bool AdvanceP4BudgetPhase(CombatState state,string phaseId)
        {
            var ledger=state.momentumLedger;
            if(ledger.encounterKind!="Boss"||string.IsNullOrEmpty(phaseId)||phaseId==ledger.phaseId||ledger.closedBudgetPhases.Contains(phaseId))return false;
            ledger.closedBudgetPhases.Add(ledger.phaseId);ledger.phaseId=phaseId;ledger.tacticalAwarded=0;
            return true;
        }
        void AwardP4ActionOutcome(CombatState s,ActorState target,string root,string paidChain,bool interrupted,bool killed,string interruptedThreatRoot=null)
        {
            float award=0;string category=null;
            if(interrupted&&!string.IsNullOrEmpty(interruptedThreatRoot)&&s.momentumLedger.registeredDangerousThreats.Contains(interruptedThreatRoot)&&!s.momentumLedger.resolvedDangerousThreats.Contains(interruptedThreatRoot))
            {s.momentumLedger.resolvedDangerousThreats.Add(interruptedThreatRoot);award=Config.interruptMomentum;category="interrupt";AwardFocusBudget(s,root,"interrupt",FocusBudgetRules.InterruptRecovery,target.id,!FocusHighThreat(s,target));}
            if(killed&&s.momentumLedger.registeredHighThreats.Contains(target.id)&&!s.momentumLedger.resolvedThreats.Contains(target.id))
            {s.momentumLedger.resolvedThreats.Add(target.id);string template;if(s.world.strings.TryGetValue("enemyTemplate/"+target.id,out template)&&template=="elite"&&!s.focusBudget.rewardRoots.Contains("kill/"+s.encounterId+"/"+target.id))AwardFocusBudget(s,root,"elite-kill",FocusBudgetRules.EliteRecovery,target.id);if(Config.threatKillMomentum>award){award=Config.threatKillMomentum;category="registered-kill";}}
            if(category!=null)AwardP4Root(s,category,root,award,true,paidChain,false);
        }
        void ValidateP4Economy(CombatState s)
        {
            Require(s.charges<s.chargeCap||s.momentum==0,"full_cap_momentum_must_be_zero");
            Require(s.momentumLedger.p4Roots!=null&&s.momentumLedger.paidChains!=null,"missing_p4_ledger");
            var ledger=s.momentumLedger;
            float budget=ledger.encounterKind=="Elite"?Config.tacticalBudgetElite:ledger.encounterKind=="Boss"?Config.tacticalBudgetBossPhase:Config.tacticalBudgetNormal;
            Require(ledger.registeredObjectives!=null&&ledger.registeredHighThreats!=null&&ledger.resolvedThreats!=null&&ledger.registeredDangerousThreats!=null&&ledger.resolvedDangerousThreats!=null&&ledger.closedBudgetPhases!=null&&Finite(ledger.tacticalAwarded)&&ledger.tacticalAwarded>=0&&ledger.tacticalAwarded<=budget+.001f,"invalid_p4_budget");
            Require(ledger.paidChains.Select(c=>c.id).Distinct().Count()==ledger.paidChains.Count,"duplicate_p4_paid_chain");
            Require(s.momentumLedger.p4Roots.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&Finite(x.highest)&&x.highest>=0&&Finite(x.credited)&&x.credited>=0),"invalid_p4_reward_root");
            Require(s.momentumLedger.p4Roots.Select(x=>x.id).Distinct().Count()==s.momentumLedger.p4Roots.Count,"duplicate_p4_reward_root");
            Require(s.momentumLedger.paidChains.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&Finite(x.awarded)&&x.awarded>=0&&x.awarded<=Config.paidChainMomentumCap+.001f),"invalid_p4_paid_chain");
        }
    }
}
