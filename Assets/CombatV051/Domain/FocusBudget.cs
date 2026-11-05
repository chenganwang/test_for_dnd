using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    // Central tuning entry. These startup values require recompilation; no new
    // CombatConfig fields are injected into historical profile configuration hashes.
    public static class FocusBudgetRules
    {
        public const int EconomyVersion=2;
        public const float InitialSeconds=15,MaximumSeconds=20,HardMaximumSeconds=24,LegacyInitialSeconds=8,ReentryWindow=1.2f,ReentryMultiplier=2;
        public const float PerfectDodgeRecovery=.75f,ParryRecovery=1,ReactionRecovery=1.25f,InterruptRecovery=1,BreakRecovery=1,EliteRecovery=1.5f,BossPhaseRecovery=2,EnvironmentRecovery=1,FocusRelicMultiplier=1.25f;
        public const float NormalRoomRecoveryCap=4,EliteRoomRecoveryCap=6,BossPhaseRecoveryCap=6,RepeatWindowSeconds=10;
        public const float ReactionDecisionSeconds=2.5f,ZeroBudgetRevealGrace=.15f;
        public const int ReactionOpportunitiesPerEncounter=6;
    }
    [Serializable] public sealed class FocusRepeatUse
    {public string key;public float multiplier;}
    [Serializable] public sealed class FocusRewardEvent
    {
        public string id,scopeId,category;
        public float highestOffered,credited,quotaConsumed;
        public List<FocusRepeatUse> repeats=new List<FocusRepeatUse>();
    }
    [Serializable] public sealed class FocusRecoveryRepeat
    {public string key;public float lastSimulationTime;public int count;}
    [Serializable] public sealed class FocusRecoveryScope
    {
        public string id,encounterId,phaseId,kind;
        public float consumed;
        public List<FocusRecoveryRepeat> repeats=new List<FocusRecoveryRepeat>();
    }
    [Serializable] public sealed partial class FocusBudgetState
    {
        public float secondsRemaining=FocusBudgetRules.InitialSeconds,secondsSpent,secondsRecovered,secondsDiscarded;
        public float activeSecondsSinceExit=FocusBudgetRules.ReentryWindow,entryDrainMultiplier=1,recoveryMultiplier=1,revealGraceRemaining;
        public int entryCount;
        public bool active;
        public List<string> rewardRoots=new List<string>();
        public int economyVersion=FocusBudgetRules.EconomyVersion;
        public float maximumSeconds=FocusBudgetRules.MaximumSeconds,lastRecoverySeconds;
        public long lastRecoverySequence;
        public string lastRecoveryKind;
        [JsonProperty(Required=Required.DisallowNull)] public List<FocusRewardEvent> rewardEvents=new List<FocusRewardEvent>();
        [JsonProperty(Required=Required.DisallowNull)] public List<FocusRecoveryScope> recoveryScopes=new List<FocusRecoveryScope>();
        [OnDeserializing] void BeginEconomyCompatibility(StreamingContext context){economyVersion=0;}
    }
    public sealed partial class CombatState
    {
        [JsonProperty(Required=Required.DisallowNull)] public FocusBudgetState focusBudget=new FocusBudgetState();
        public List<string> reactionOpportunityRoots=new List<string>();
        // Missing means a pre-budget P4/P1/P0 snapshot. Explicit null still fails JSON validation.
        [OnDeserializing] void BeginFocusCompatibility(StreamingContext context){focusBudget=null;}
        [OnDeserialized] void CompleteFocusCompatibility(StreamingContext context)
        {
            if(focusBudget!=null){CombatSession.MigrateFocusEconomy(this);return;}
            var originalMode=decisionResume!=null?decisionResume.priorMode:mode==CombatMode.CombatPaused?resumeMode:mode;
            focusBudget=new FocusBudgetState{secondsRemaining=FocusBudgetRules.LegacyInitialSeconds,active=originalMode==CombatMode.TacticalFocus||originalMode==CombatMode.PostRollDecision||originalMode==CombatMode.RollFreeze};
            if(reactionSettings!=null){reactionSettings.decisionSeconds=Math.Min(FocusBudgetRules.ReactionDecisionSeconds,reactionSettings.decisionSeconds);reactionSettings.pauseDecision=false;}
            foreach(var group in pendingThreatEvents)group.remainingUiSeconds=Math.Min(group.remainingUiSeconds,FocusBudgetRules.ReactionDecisionSeconds);
            foreach(var window in reactionWindows)window.remaining=Math.Min(window.remaining,FocusBudgetRules.ReactionDecisionSeconds);
            reactionOpportunityRoots=pendingThreatEvents.Where(g=>g.stage=="Choosing"||g.autoOpened||reactionWindows.Any(w=>w.reactionEventId==g.id&&w.attempted)).OrderByDescending(g=>g.id==activeReactionEventId).Select(g=>g.rootId).Distinct().Take(FocusBudgetRules.ReactionOpportunitiesPerEncounter).ToList();
        }
    }
    public sealed partial class CombatSession
    {
        static void ValidateFocusBudget(CombatState s)
        {
            if(s.focusBudget?.p6!=null){ValidateP6FocusState(s);return;}
            var b=s.focusBudget;Require(b!=null&&b.rewardRoots!=null&&b.rewardRoots.All(x=>!string.IsNullOrEmpty(x))&&b.rewardRoots.Distinct().Count()==b.rewardRoots.Count,"invalid_focus_budget");
            Require(new[]{b.secondsRemaining,b.secondsSpent,b.secondsRecovered,b.secondsDiscarded,b.activeSecondsSinceExit,b.revealGraceRemaining,b.maximumSeconds,b.lastRecoverySeconds}.All(x=>Finite(x)&&x>=0)&&b.maximumSeconds>=FocusBudgetRules.MaximumSeconds&&b.maximumSeconds<=FocusBudgetRules.HardMaximumSeconds&&b.secondsRemaining<=b.maximumSeconds&&b.activeSecondsSinceExit<=FocusBudgetRules.ReentryWindow&&b.revealGraceRemaining<=FocusBudgetRules.ZeroBudgetRevealGrace&&b.entryCount>=0&&b.lastRecoverySequence>=0,"invalid_focus_clock");
            Require((b.entryDrainMultiplier==1||b.entryDrainMultiplier==FocusBudgetRules.ReentryMultiplier)&&(b.recoveryMultiplier==1||b.recoveryMultiplier==FocusBudgetRules.FocusRelicMultiplier),"invalid_focus_multiplier");
            Require(s.reactionOpportunityRoots!=null&&s.reactionOpportunityRoots.All(x=>!string.IsNullOrEmpty(x))&&s.reactionOpportunityRoots.Distinct().Count()==s.reactionOpportunityRoots.Count&&s.reactionOpportunityRoots.Count<=FocusBudgetRules.ReactionOpportunitiesPerEncounter,"invalid_reaction_opportunities");
            Require(b.economyVersion==FocusBudgetRules.EconomyVersion&&b.rewardEvents!=null&&b.recoveryScopes!=null,"invalid_focus_economy_version");
            Require(b.recoveryScopes.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&!string.IsNullOrEmpty(x.encounterId)&&!string.IsNullOrEmpty(x.phaseId)&&new[]{"Normal","Elite","Boss"}.Contains(x.kind)&&Finite(x.consumed)&&x.consumed>=0&&x.consumed<=FocusScopeCap(x.kind)+.0001f&&x.repeats!=null&&x.repeats.All(v=>v!=null&&!string.IsNullOrEmpty(v.key)&&Finite(v.lastSimulationTime)&&v.lastSimulationTime>=0&&v.count>=1)&&x.repeats.Select(v=>v.key).Distinct().Count()==x.repeats.Count)&&b.recoveryScopes.Select(x=>x.id).Distinct().Count()==b.recoveryScopes.Count,"invalid_focus_scope");
            Require(b.rewardEvents.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&b.rewardRoots.Contains(x.id)&&b.recoveryScopes.Any(scope=>scope.id==x.scopeId)&&new[]{x.highestOffered,x.credited,x.quotaConsumed}.All(v=>Finite(v)&&v>=0)&&x.credited<=x.quotaConsumed+.0001f&&x.quotaConsumed<=x.highestOffered+.0001f&&x.repeats!=null&&x.repeats.All(v=>v!=null&&!string.IsNullOrEmpty(v.key)&&(v.multiplier==0||v.multiplier==.5f||v.multiplier==1))&&x.repeats.Select(v=>v.key).Distinct().Count()==x.repeats.Count)&&b.rewardEvents.Select(x=>x.id).Distinct().Count()==b.rewardEvents.Count,"invalid_focus_reward_event");
            foreach(var scope in b.recoveryScopes)Require(b.rewardEvents.Where(e=>e.scopeId==scope.id).Sum(e=>e.quotaConsumed)<=scope.consumed+.0001f,"invalid_focus_scope_accounting");
        }
        static void EnterFocusBudget(CombatState s)
        {
            if(s.focusBudget.p6!=null){EnterP6FocusBudget(s);return;}
            var b=s.focusBudget;Require(b.secondsRemaining>0,"focus_budget_empty");
            b.entryDrainMultiplier=b.entryCount>0&&b.activeSecondsSinceExit<FocusBudgetRules.ReentryWindow?FocusBudgetRules.ReentryMultiplier:1;
            b.entryCount++;b.active=true;b.revealGraceRemaining=0;
        }
        static void LeaveFocusBudget(CombatState s)
        {if(s.focusBudget.p6!=null){LeaveP6FocusBudget(s);return;}s.focusBudget.active=false;s.focusBudget.activeSecondsSinceExit=0;s.focusBudget.revealGraceRemaining=0;}
        static bool FocusClockRuns(CombatState s,bool ui)
        {return ui&&s.mode!=CombatMode.CombatPaused&&s.mode!=CombatMode.RollFreeze&&s.activePendingEvent==null&&s.decisionResume==null;}
        static bool FocusIsSpending(CombatState s,bool ui)
        {return s.inCombat&&FocusClockRuns(s,ui)&&s.focusBudget.active&&(s.mode==CombatMode.TacticalFocus||s.mode==CombatMode.PostRollDecision&&s.pendingRoll!=null&&s.pendingRoll.purpose=="Tactical");}
        bool AdvanceFocusClock(CombatState s,float realDelta,bool ui)
        {
            if(s.focusBudget.p6!=null)return AdvanceP6FocusClock(s,realDelta,ui);
            if(!FocusClockRuns(s,ui)||realDelta<=0)return false;var b=s.focusBudget;
            if(!FocusIsSpending(s,ui)){if(!b.active)b.activeSecondsSinceExit=Math.Min(FocusBudgetRules.ReentryWindow,b.activeSecondsSinceExit+realDelta);return false;}
            if(b.revealGraceRemaining>0){float grace=Math.Min(realDelta,b.revealGraceRemaining);b.revealGraceRemaining=Down(b.revealGraceRemaining,grace);realDelta-=grace;if(realDelta<=0&&b.revealGraceRemaining>0)return false;}
            float spent=Math.Min(b.secondsRemaining,realDelta*b.entryDrainMultiplier);b.secondsRemaining=Down(b.secondsRemaining,spent);b.secondsSpent+=spent;
            if(b.secondsRemaining>0||b.revealGraceRemaining>0)return false;
            var ticket=s.pendingRoll;
            if(ticket!=null&&ticket.purpose=="Tactical"&&ticket.stage=="AwaitDecision"){ticket.stage="Canceled";Release(s,ticket,true);s.pendingRollId=null;}
            LeaveFocusBudget(s);s.mode=s.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing")?CombatMode.QueuedExecution:CombatMode.RealtimeCombat;return true;
        }
        float TickFocusOnly(float realDelta,bool ui,float decisionDelta)
        {
            LastTickError=null;Require(Finite(realDelta)&&realDelta>=0,"invalid_delta");
            bool deadline=FocusDeadlineWillCross(State,decisionDelta,ui)||P5DeadlineWillCross(State,realDelta)||P6DeadlineWillCross(State,realDelta);
            if(!deadline){float dt=TickCore(realDelta);AdvanceFocusClock(State,decisionDelta,ui);return dt;}
            var before=State;State=State.Clone();
            try{float dt=TickCore(realDelta);AdvanceFocusClock(State,decisionDelta,ui);ExpireP5Tickets(State);ExpireP6Tickets(State);Validate(State);ValidateP4Tactics(State);ValidateP4Economy(State);ValidateP5State(State);ValidateP6State(State);ValidateV11(State);Persist(State);return dt;}
            catch(Exception ex){State=before;LastTickError=ex.Message;return 0;}
        }
        static bool HasReactionOpportunity(CombatState s,PendingThreatEvent g)
        {return s.reactionOpportunityRoots.Contains(g.rootId)||s.reactionOpportunityRoots.Count<FocusBudgetRules.ReactionOpportunitiesPerEncounter;}
        static float FocusScopeCap(string kind)=>kind=="Boss"?FocusBudgetRules.BossPhaseRecoveryCap:kind=="Elite"?FocusBudgetRules.EliteRoomRecoveryCap:FocusBudgetRules.NormalRoomRecoveryCap;
        static FocusRecoveryScope FocusScope(CombatState s)
        {
            string kind=s.momentumLedger.encounterKind;kind=kind=="Boss"||kind=="Elite"?kind:"Normal";
            string phase=kind=="Boss"?s.momentumLedger.phaseId:"initial",id=s.encounterId+"/focus/"+phase;
            var scope=s.focusBudget.recoveryScopes.Find(x=>x.id==id);
            if(scope==null){scope=new FocusRecoveryScope{id=id,encounterId=s.encounterId,phaseId=phase,kind=kind};s.focusBudget.recoveryScopes.Add(scope);}return scope;
        }
        internal static void MigrateFocusEconomy(CombatState s)
        {
            var b=s.focusBudget;if(b.economyVersion!=0)return;
            // Existing seconds, costs, history and RNG are untouched. No 15-second refill on load.
            b.economyVersion=FocusBudgetRules.EconomyVersion;b.maximumSeconds=FocusBudgetRules.MaximumSeconds;
            if(s.inCombat&&b.rewardRoots!=null)
            {
                float consumed=0;
                foreach(string root in b.rewardRoots)
                {if(root!=null&&root.StartsWith("defense/"+s.encounterId+"/",StringComparison.Ordinal))consumed+=.5f*b.recoveryMultiplier;
                 else if(root!=null&&root.StartsWith("kill/"+s.encounterId+"/",StringComparison.Ordinal))consumed+=.75f*b.recoveryMultiplier;}
                if(consumed>0){var scope=FocusScope(s);scope.consumed=Math.Min(FocusScopeCap(scope.kind),consumed);}
            }
        }
        bool FocusRewardAllowed(CombatState s)=>s.inCombat&&!s.encounterResolved&&!s.momentumLedger.training&&!s.momentumLedger.disengaged&&!IsP4Training(s)&&s.actors.Any(a=>a.Alive);
        void AwardFocusBudget(CombatState s,string root,string category,float seconds,string sourceId=null,bool lowRisk=false)
        {
            // P6 owns its explicit defense/combo/clear sources. Legacy break, kill,
            // environment and P5-combo hooks must not mint additional P6 Focus.
            if(s.focusBudget.p6!=null)
            {if(category=="boss-phase")AwardP6FocusRecovery(s,"boss-phase/"+s.momentumLedger.phaseId,"boss-phase");return;}
            if(!FocusRewardAllowed(s)||string.IsNullOrEmpty(root)||seconds<=0)return;
            var b=s.focusBudget;string id="event/"+s.encounterId+"/"+root;
            // Legacy defense roots remain consumed even though the richer v2 record did not exist.
            if(b.rewardRoots.Contains("defense/"+s.encounterId+"/"+root))return;
            var reward=b.rewardEvents.Find(x=>x.id==id);FocusRecoveryScope scope;
            if(reward==null)
            {
                if(b.rewardRoots.Contains(id))return;
                scope=FocusScope(s);reward=new FocusRewardEvent{id=id,scopeId=scope.id};b.rewardEvents.Add(reward);b.rewardRoots.Add(id);
            }
            else scope=b.recoveryScopes.Find(x=>x.id==reward.scopeId);
            float multiplier=1;
            if(lowRisk)
            {
                string key=category+"/"+sourceId;var use=reward.repeats.Find(x=>x.key==key);
                if(use==null)
                {
                    var repeat=scope.repeats.Find(x=>x.key==key);
                    if(repeat==null){repeat=new FocusRecoveryRepeat{key=key};scope.repeats.Add(repeat);}
                    if(repeat.count==0||s.simulationSeconds-repeat.lastSimulationTime>=FocusBudgetRules.RepeatWindowSeconds)repeat.count=0;
                    repeat.count++;repeat.lastSimulationTime=s.simulationSeconds;
                    use=new FocusRepeatUse{key=key,multiplier=repeat.count==1?1:repeat.count==2?.5f:0};reward.repeats.Add(use);
                }
                multiplier=use.multiplier;
            }
            float offered=seconds*b.recoveryMultiplier*multiplier;
            if(offered<=reward.highestOffered)return;
            float delta=offered-reward.highestOffered;reward.highestOffered=offered;reward.category=category;
            float quota=Math.Min(delta,Math.Max(0,FocusScopeCap(scope.kind)-scope.consumed));scope.consumed+=quota;reward.quotaConsumed+=quota;
            float earned=Math.Min(b.maximumSeconds-b.secondsRemaining,quota);reward.credited+=earned;b.secondsRemaining+=earned;b.secondsRecovered+=earned;b.secondsDiscarded+=delta-earned;
            if(earned>0){b.lastRecoverySequence++;b.lastRecoverySeconds=earned;b.lastRecoveryKind=category;}
        }
        bool FocusCriticalThreat(CombatState s,string root,ActorState defender,int damage)
        {return defender!=null&&!defender.isEnemy&&damage>0&&(s.momentumLedger.registeredDangerousThreats.Contains(root)||damage>=defender.maxHp*Config.importantReactionDamageFraction||damage>=defender.hp);}
        bool FocusHighThreat(CombatState s,ActorState enemy)=>enemy!=null&&s.momentumLedger.registeredHighThreats.Contains(enemy.id);
        void AwardFocusDefense(CombatState s,string root,ActorState defender,int damage,string kind,ActorState source)
        {
            if(defender==null||defender.isEnemy||damage<=0)return;
            bool critical=FocusCriticalThreat(s,root,defender,damage);
            if(kind=="dodge"&&critical)AwardFocusBudget(s,root,kind,FocusBudgetRules.PerfectDodgeRecovery,source?.id,!FocusHighThreat(s,source));
            else if(kind=="parry")AwardFocusBudget(s,root,kind,FocusBudgetRules.ParryRecovery,source?.id,!FocusHighThreat(s,source));
            else if(kind=="reaction"&&critical)AwardFocusBudget(s,root,kind,FocusBudgetRules.ReactionRecovery,source?.id,!FocusHighThreat(s,source));
        }
        void AwardFocusBreak(CombatState s,ActorState enemy,string root,float previousBreak)
        {
            if(enemy!=null&&enemy.isEnemy&&previousBreak>0&&enemy.breakValue<=0)
                AwardFocusBudget(s,root,"break",FocusBudgetRules.BreakRecovery,enemy.id,!FocusHighThreat(s,enemy));
        }
        CombatResult AwardFocusBossPhase(CombatState s,CombatCommand c)
        {
            Require(c.authority==CommandAuthority.System&&!c.refundOrigin&&!c.fromReaction&&c.qualified,"unqualified_world_fact");
            var boss=s.Actor(c.targetId);string template;
            Require(FocusRewardAllowed(s)&&s.momentumLedger.encounterKind=="Boss"&&boss!=null&&boss.isEnemy&&boss.Alive&&boss.maxHp>0&&boss.hp<=boss.maxHp*.5f&&s.world.strings.TryGetValue("enemyTemplate/"+boss.id,out template)&&template=="boss"&&c.rootId==boss.id+"/boss-half-health","boss_phase_not_reached");
            bool advanced=AwardFocusBossPhaseIfReached(s,boss,c.rootId);
            return new CombatResult{reason=advanced?"boss_phase_advanced":"boss_phase_already_recorded"};
        }
        bool AwardFocusBossPhaseIfReached(CombatState s,ActorState boss,string physicalRoot)
        {
            string template;
            if(!FocusRewardAllowed(s)||s.momentumLedger.encounterKind!="Boss"||boss==null||!boss.isEnemy||!boss.Alive||boss.maxHp<=0||boss.hp>boss.maxHp*.5f||!s.world.strings.TryGetValue("enemyTemplate/"+boss.id,out template)||template!="boss")return false;
            if(!AdvanceP4BudgetPhase(s,"boss-half-health"))return false;
            // The hit and phase reward use one physical root and one durable command. Apply
            // phase recovery first so any accompanying interrupt/Break only competes by max.
            AwardFocusBudget(s,physicalRoot,"boss-phase",FocusBudgetRules.BossPhaseRecovery,boss.id);return true;
        }
    }
}
