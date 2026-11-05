using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class P6FocusConfig
    {
        public float initialSeconds=90,maximumSeconds=120,maximumRecoveredSeconds=60;
        public float visitSeconds=15,reopenCooldownSimSeconds=2,interactiveMinimumSeconds=3;
        public float worldScale=.12f,reactionDecisionSeconds=6;
        public float defenseRecovery=2,comboRecovery=3,clearRecovery=8,bossPhaseRecovery=8,echoRecovery=6;
        public int defensePerEncounter=2,comboPerEncounter=2;
    }
    [Serializable] public sealed class P6FocusRewardReceipt
    {
        public string id,encounterId,category;
        public float offered,credited;
    }
    [Serializable] public sealed class P6FocusState
    {
        public P6FocusConfig rules=new P6FocusConfig();
        public float currentVisitSpent,reopenCooldownRemainingSim,lastObservedSimulation;
        public bool unlimitedTraining;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public float capacityBonus;
        public List<P6FocusRewardReceipt> rewards=new List<P6FocusRewardReceipt>();
    }
    public sealed partial class FocusBudgetState
    {
        // An absent P6 extension serializes exactly as the pre-P6 snapshot did.
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6FocusState p6;
    }
    public sealed partial class CombatSession
    {
        public static void ValidateP6FocusConfiguration(P6FocusConfig c)
        {
            Require(c!=null,"p6_focus_config_missing");
            Require(new[]{c.initialSeconds,c.maximumSeconds,c.maximumRecoveredSeconds,c.visitSeconds,c.reopenCooldownSimSeconds,
                c.interactiveMinimumSeconds,c.worldScale,c.reactionDecisionSeconds,c.defenseRecovery,c.comboRecovery,
                c.clearRecovery,c.bossPhaseRecovery,c.echoRecovery}.All(x=>Finite(x)&&x>=0),"p6_focus_config_invalid");
            Require(c.initialSeconds>0&&c.initialSeconds<=90&&c.maximumSeconds>=c.initialSeconds&&c.maximumSeconds<=120&&
                c.maximumRecoveredSeconds<=60&&c.visitSeconds>0&&c.visitSeconds<=15&&c.reopenCooldownSimSeconds>=2&&
                c.interactiveMinimumSeconds>=3&&c.interactiveMinimumSeconds<=c.visitSeconds&&
                (c.worldScale==0||c.worldScale>=.1f&&c.worldScale<=.15f)&&c.reactionDecisionSeconds>0&&c.reactionDecisionSeconds<=6&&
                c.defensePerEncounter==2&&c.comboPerEncounter==2,"p6_focus_config_limits");
        }
        static P6FocusConfig CopyP6FocusRules(P6FocusConfig c)=>new P6FocusConfig
        {
            initialSeconds=c.initialSeconds,maximumSeconds=c.maximumSeconds,maximumRecoveredSeconds=c.maximumRecoveredSeconds,
            visitSeconds=c.visitSeconds,reopenCooldownSimSeconds=c.reopenCooldownSimSeconds,interactiveMinimumSeconds=c.interactiveMinimumSeconds,
            worldScale=c.worldScale,reactionDecisionSeconds=c.reactionDecisionSeconds,defenseRecovery=c.defenseRecovery,
            comboRecovery=c.comboRecovery,clearRecovery=c.clearRecovery,bossPhaseRecovery=c.bossPhaseRecovery,echoRecovery=c.echoRecovery,
            defensePerEncounter=c.defensePerEncounter,comboPerEncounter=c.comboPerEncounter
        };
        public static void InitializeP6Focus(CombatState s,P6FocusConfig rules,bool newRun=false,bool unlimitedTraining=false)
        {
            Require(s!=null,"p6_focus_state_missing");ValidateP6FocusConfiguration(rules);
            if(s.focusBudget?.p6!=null)
            {
                if(unlimitedTraining)
                {
                    Require(IsP4Training(s)&&s.p6!=null&&s.p6.training,"p6_training_context_required");
                    s.focusBudget.p6.unlimitedTraining=true;
                }
                // Encounter reconstruction rebases the simulation clock, never refills.
                // Even an accidentally repeated newRun flag cannot erase a live ledger.
                s.focusBudget.p6.lastObservedSimulation=s.simulationSeconds;return;
            }
            Require(!unlimitedTraining||IsP4Training(s)&&s.p6!=null&&s.p6.training,"p6_training_context_required");
            s.focusBudget=new FocusBudgetState{secondsRemaining=rules.initialSeconds,maximumSeconds=rules.maximumSeconds,
                p6=new P6FocusState{rules=CopyP6FocusRules(rules),lastObservedSimulation=s.simulationSeconds,unlimitedTraining=unlimitedTraining}};
            if(s.reactionSettings!=null)s.reactionSettings.decisionSeconds=rules.reactionDecisionSeconds;
        }
        public static float P6ReactionSeconds(CombatState s)=>s?.focusBudget?.p6?.rules.reactionDecisionSeconds??FocusBudgetRules.ReactionDecisionSeconds;
        public static float P6VisitRemaining(CombatState s)
        {
            var p=s?.focusBudget?.p6;
            return p==null?0:p.unlimitedTraining?float.MaxValue:Math.Max(0,p.rules.visitSeconds-p.currentVisitSpent);
        }
        public static bool P6CanStartInteractiveRoll(CombatState s)
        {
            var b=s?.focusBudget;var p=b?.p6;
            return p!=null&&b.active&&s.mode==CombatMode.TacticalFocus&&s.activePendingEvent==null&&s.decisionResume==null&&
                (p.unlimitedTraining||b.secondsRemaining+0.00001f>=p.rules.interactiveMinimumSeconds&&
                    P6VisitRemaining(s)+0.00001f>=p.rules.interactiveMinimumSeconds);
        }
        static void ValidateP6FocusState(CombatState s)
        {
            var b=s.focusBudget;var p=b.p6;ValidateP6FocusConfiguration(p.rules);
            Require(b.rewardRoots!=null&&b.rewardEvents!=null&&b.recoveryScopes!=null&&p.rewards!=null,"p6_focus_lists_invalid");
            Require(new[]{b.secondsRemaining,b.secondsSpent,b.secondsRecovered,b.secondsDiscarded,b.maximumSeconds,b.lastRecoverySeconds,
                b.activeSecondsSinceExit,b.recoveryMultiplier,b.revealGraceRemaining,
                p.currentVisitSpent,p.reopenCooldownRemainingSim,p.lastObservedSimulation,p.capacityBonus}.All(x=>Finite(x)&&x>=0),"p6_focus_clock_invalid");
            Require(p.capacityBonus<=15&&b.maximumSeconds==p.rules.maximumSeconds+p.capacityBonus&&b.secondsRemaining<=b.maximumSeconds+.0001f&&
                b.secondsRecovered<=p.rules.maximumRecoveredSeconds+.0001f&&p.currentVisitSpent<=p.rules.visitSeconds+.0001f&&
                p.reopenCooldownRemainingSim<=p.rules.reopenCooldownSimSeconds+.0001f&&b.entryDrainMultiplier==1&&b.revealGraceRemaining==0&&
                b.entryCount>=0&&b.lastRecoverySequence>=0,"p6_focus_limits_invalid");
            Require(b.secondsRemaining+b.secondsSpent<=p.rules.initialSeconds+b.secondsRecovered+.002f,"p6_focus_balance_invalid");
            Require(!p.unlimitedTraining||IsP4Training(s)&&s.p6!=null&&s.p6.training,"p6_training_context_required");
            Require(p.rewards.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&!string.IsNullOrEmpty(x.encounterId)&&
                new[]{"defense","combo","clear","boss-phase","echo","v12-capacity","v12-exploration"}.Contains(x.category)&&Finite(x.offered)&&x.offered>=0&&
                Finite(x.credited)&&x.credited>=0&&x.credited<=x.offered+.0001f)&&
                p.rewards.Select(x=>x.id).Distinct().Count()==p.rewards.Count,"p6_focus_receipts_invalid");
            Require(Math.Abs(p.rewards.Sum(x=>x.credited)-b.secondsRecovered)<.002f,"p6_focus_accounting_invalid");
            foreach(var group in p.rewards.GroupBy(x=>x.encounterId))
                Require(group.Count(x=>x.category=="defense")<=p.rules.defensePerEncounter&&group.Count(x=>x.category=="combo")<=p.rules.comboPerEncounter&&
                    group.Count(x=>x.category=="clear")<=1,"p6_focus_event_limit");
            Require(s.reactionOpportunityRoots!=null&&s.reactionOpportunityRoots.All(x=>!string.IsNullOrEmpty(x))&&
                s.reactionOpportunityRoots.Distinct().Count()==s.reactionOpportunityRoots.Count&&
                s.reactionOpportunityRoots.Count<=FocusBudgetRules.ReactionOpportunitiesPerEncounter,"invalid_reaction_opportunities");
        }
        static void EnterP6FocusBudget(CombatState s)
        {
            var b=s.focusBudget;var p=b.p6;
            Require(p.unlimitedTraining||b.secondsRemaining>0,"focus_budget_empty");
            Require(p.unlimitedTraining||p.reopenCooldownRemainingSim<=.00001f,"p6_focus_reopen_cooldown");
            b.active=true;b.entryCount++;b.entryDrainMultiplier=1;b.revealGraceRemaining=0;
            p.currentVisitSpent=0;p.lastObservedSimulation=s.simulationSeconds;
        }
        static void LeaveP6FocusBudget(CombatState s)
        {
            var b=s.focusBudget;var p=b.p6;
            if(b.active)p.reopenCooldownRemainingSim=p.rules.reopenCooldownSimSeconds;
            b.active=false;b.activeSecondsSinceExit=0;b.revealGraceRemaining=0;b.entryDrainMultiplier=1;
            p.lastObservedSimulation=s.simulationSeconds;
        }
        static bool FocusDeadlineWillCross(CombatState s,float decisionDelta,bool ui)
        {
            if(decisionDelta<=0||!FocusIsSpending(s,ui))return false;
            var b=s.focusBudget;
            if(b.p6!=null)return !b.p6.unlimitedTraining&&decisionDelta>=Math.Min(b.secondsRemaining,P6VisitRemaining(s));
            return decisionDelta>=b.revealGraceRemaining+b.secondsRemaining/b.entryDrainMultiplier;
        }
        void AdvanceP6FocusCooldown(CombatState s,bool ui)
        {
            var b=s.focusBudget;var p=b.p6;
            float simulationDelta=Math.Max(0,s.simulationSeconds-p.lastObservedSimulation);p.lastObservedSimulation=s.simulationSeconds;
            if(!ui||b.active||!HasP4Threat(s)||s.mode==CombatMode.RollFreeze||s.mode==CombatMode.CombatPaused)return;
            p.reopenCooldownRemainingSim=Down(p.reopenCooldownRemainingSim,simulationDelta);
        }
        bool AdvanceP6FocusClock(CombatState s,float decisionDelta,bool ui)
        {
            AdvanceP6FocusCooldown(s,ui);
            var b=s.focusBudget;var p=b.p6;
            if(!FocusIsSpending(s,ui)||decisionDelta<=0||p.unlimitedTraining)return false;
            float spent=Math.Min(decisionDelta,Math.Min(b.secondsRemaining,P6VisitRemaining(s)));
            b.secondsRemaining=Down(b.secondsRemaining,spent);b.secondsSpent+=spent;p.currentVisitSpent+=spent;
            if(b.secondsRemaining>.00001f&&P6VisitRemaining(s)>.00001f)return false;
            // A paid Smite has already authorized its base result. Its owner commits
            // that exact ticket without rolling, refunding or spending optional costs.
            bool handled=AutoCommitP6SmiteOnTimeout(s);
            var ticket=s.pendingRoll;
            if(!handled&&ticket!=null&&ticket.purpose=="Tactical"&&ticket.stage=="AwaitDecision")
            {ticket.stage="Canceled";Release(s,ticket,true);s.pendingRollId=null;}
            LeaveP6FocusBudget(s);s.commandActorId=s.activeActorId;
            s.mode=s.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing")?CombatMode.QueuedExecution:CombatMode.RealtimeCombat;
            return true;
        }
        // Called only by validated combat/profile transactions, not presentation observers.
        // Qualification (real defense/mark damage/encounter completion) belongs to the caller;
        // this ledger enforces source identity, frequency, capacity and global actual credit.
        public static float AwardP6FocusRecovery(CombatState s,string root,string category)
        {
            var b=s?.focusBudget;var p=b?.p6;
            if(p==null||string.IsNullOrEmpty(root)||string.IsNullOrEmpty(s.encounterId)||p.unlimitedTraining||
                IsP4Training(s)||s.p6!=null&&s.p6.training||s.momentumLedger.training||!s.actors.Any(a=>a.Alive))return 0;
            if(s.mode==CombatMode.CombatPaused||s.mode==CombatMode.RollFreeze)return 0;
            if(category!="clear"&&(!s.inCombat||s.encounterResolved||s.momentumLedger.disengaged))return 0;
            float offered;int limit;
            switch(category)
            {
                case "defense":offered=p.rules.defenseRecovery;limit=p.rules.defensePerEncounter;break;
                case "combo":offered=p.rules.comboRecovery;limit=p.rules.comboPerEncounter;break;
                case "clear":
                    if(s.enemies.Count==0||s.enemies.Any(a=>a.Alive)||s.momentumLedger.encounterKind=="Boss")return 0;
                    offered=p.rules.clearRecovery;limit=1;break;
                case "boss-phase":offered=p.rules.bossPhaseRecovery;limit=int.MaxValue;break;
                case "echo":offered=p.rules.echoRecovery;limit=int.MaxValue;break;
                default:return 0;
            }
            string id=s.encounterId+"/p6-focus/"+root;
            return CreditP6Focus(s,id,s.encounterId,category,offered,limit,false);
        }
        static float CreditP6Focus(CombatState s,string id,string scope,string category,float offered,int limit,bool runScope)
        {
            var b=s?.focusBudget;var p=b?.p6;
            if(p==null||string.IsNullOrEmpty(scope)||p.unlimitedTraining||IsP4Training(s)||s.p6?.training==true||s.mode==CombatMode.CombatPaused||s.mode==CombatMode.RollFreeze||!s.actors.Any(a=>a.Alive))return 0;
            if(p.rewards.Any(x=>x.id==id)||p.rewards.Count(x=>(runScope||x.encounterId==scope)&&x.category==category)>=limit)return 0;
            float earned=Math.Min(offered,Math.Min(Math.Max(0,b.maximumSeconds-b.secondsRemaining),Math.Max(0,p.rules.maximumRecoveredSeconds-b.secondsRecovered)));
            p.rewards.Add(new P6FocusRewardReceipt{id=id,encounterId=scope,category=category,offered=offered,credited=earned});
            b.secondsRemaining+=earned;b.secondsRecovered+=earned;b.secondsDiscarded+=offered-earned;
            if(earned>0){b.lastRecoverySequence++;b.lastRecoverySeconds=earned;b.lastRecoveryKind=category;}
            return earned;
        }
    }
}
