using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public const string P6SmiteId="ability_smite_recharge",P6BindId="ability_bind_radiance";
        public int AvailableSharedCharges {get{return AvailableP6Charges(State);}}
        static int AvailableP6Charges(CombatState s)=>s.charges-s.rolls.Count(t=>t.p6!=null&&t.p6.overchargeHeld)-V14ReservedCharges(s);
        public static void InitializeP6State(CombatState s,CombatConfig config,bool newRun=false)
        {
            if(config?.p6==null)return;
            if(s.p6==null)s.p6=new P6CombatState{rulesVersion=config.p6.rulesVersion,presetId=config.p6.presetId,encounterId=s.encounterId,damageRandom=P6DamageStream(s.random.masterSeed)};
            foreach(var a in s.actors.Concat(s.enemies))if(a.p6==null)a.p6=new P6ActorState{characterId=a.id,classId=a.role==ActorRole.Paladin?"class_paladin":a.role==ActorRole.Mage?"class_mage":a.role==ActorRole.Rogue?"class_rogue":"enemy",weaponId=a.role==ActorRole.Paladin?"weapon_demo_greatsword":a.role==ActorRole.Mage?"mage-staff":a.role==ActorRole.Rogue?"rogue-blades":null};
            bool training=s.world.strings.TryGetValue("context",out var context)&&context=="TRAINING";s.p6.training=training;
            InitializeP6Focus(s,config.p6.focus,newRun,training);
        }
        static RngState P6DamageStream(ulong seed)
        {
            unchecked{ulong h=14695981039346656037UL;for(int i=0;i<8;i++){h^=(byte)(seed>>(i*8));h*=1099511628211UL;}foreach(char c in "p6/damage/v1"){h^=(byte)c;h*=1099511628211UL;}
                var r=new RngState{increment=(h<<1)|1};RandomStreams.NextUInt(r);r.state+=h^0x9e3779b97f4a7c15UL;RandomStreams.NextUInt(r);r.drawCount=0;return r;}
        }
        public static void ResetP6EncounterState(CombatState s)
        {
            ResetV12EncounterState(s);if(s.p6==null)return;s.p6.encounterId=s.encounterId;s.p6.resourceEvents.Clear();s.p6.damageResults.Clear();s.p6.bindResults.Clear();s.p6.lastGuardSimulationTime=-999;s.p6.markRewardCount=0;
            foreach(var a in s.actors.Concat(s.enemies)){a.statuses.RemoveAll(x=>x.p6!=null||x.id=="p6-root"||x.id=="p6-slow"||x.id=="v11-oath-guard"||x.id=="v11-afterlight");if(a.p6!=null){a.p6.bindIntent=null;a.p6.basicRecovery=false;}if(a.v11!=null)a.v11.finnSustain=null;}
        }
        static bool P6BindActive(ActorState a)=>a?.p6?.bindIntent!=null&&(a.p6.bindIntent.stage=="Casting"||a.p6.bindIntent.stage=="WaitingBasic");
        public bool P6MajorAvailable(ActorState a)=>a!=null&&(!V13Enabled||Member(State,a.id)==null||Member(State,a.id).actionUntil<=State.simulationSeconds)&&a.Alive&&string.IsNullOrEmpty(a.heldBy)&&a.staggerRemaining<=0&&a.windupRemaining<=0&&(a.basicAttackIntent==null||a.basicAttackIntent.resolved)&&!P6BindActive(a)&&!V12ActionActive(a)&&(a.majorRemaining<=0||a.p6?.basicRecovery==true||a.p5?.basicRecovery==true);
        public float P6MovementMultiplier(string actorId)
        {var a=State.Actor(actorId);if(a==null)return 1;if(a.statuses.Any(x=>x.id=="p6-root"&&x.remaining>0))return 0;return a.statuses.Any(x=>x.id=="p6-slow"&&x.remaining>0)?Config.p6.bossSlowMultiplier:1;}
        static bool P6Line(CombatCommand c,string targetId)
        {if(c.p6?.targets==null)return c.lineOfSight;var evidence=c.p6.targets.Find(x=>x.targetId==targetId);return evidence!=null&&evidence.lineOfSight;}
        static bool P6IsTraining(CombatState s)=>s.p6?.training==true&&s.world.strings.TryGetValue("context",out var value)&&value=="TRAINING";
        static StatusState P6Mark(CombatState s,ActorState target,string markId=null)
        {return target.statuses.Where(x=>x.id=="p6-radiant-mark"&&x.p6!=null&&x.remaining>0&&(markId==null||x.p6.id==markId)&&s.Actor(x.p6.casterId)?.Alive==true).OrderByDescending(x=>x.p6.multiplier).FirstOrDefault();}
        public P6SmiteQuote QuoteSmite(string actorId,string targetId)
        {
            var result=new P6SmiteQuote{availableCharges=AvailableSharedCharges};
            try{Require(Config.p6!=null,"p6_preset_required");result.attack=P6QuoteSmite(State,new CombatCommand{actorId=actorId,targetId=targetId,actionId=P6SmiteId},true);result.interactiveAllowed=P6CanStartInteractiveRoll(State);result.directAllowed=NormalMode(State)&&State.pendingRoll==null&&State.activePendingEvent==null&&result.availableCharges>=1;result.canOvercharge=result.availableCharges>=2;result.markRemaining=P6Mark(State,State.Actor(targetId))?.remaining??0;result.criticalProbability=P6CriticalProbability(result.attack.bonus,result.attack.targetNumber,result.attack.advantage,result.attack.disadvantage,Config.p6.smiteCriticalRaw);result.reason=result.availableCharges<=0?"no_charge":!result.interactiveAllowed?"focus_decision_time_insufficient":"ok";}
            catch(Exception ex){result.attack=new ActionQuote{legal=false,reason=ex.Message};result.reason=ex.Message;}return result;
        }
        public ActionQuote QuoteBind(string actorId,string targetId)
        {try{var a=Live(State,actorId);var target=Live(State,targetId);P6CheckBind(State,a,target,true);return new ActionQuote{legal=true,range=Config.p6.bindRange,rollType="Save",rollOwnerId=target.id,bonus=target.wisSaveBonus,targetNumber=8+a.intelligenceModifier+a.proficiency};}catch(Exception ex){return new ActionQuote{legal=false,reason=ex.Message};}}
        ActionQuote P6QuoteSmite(CombatState s,CombatCommand c,bool free)
        {
            Require(Config.p6!=null&&s.inCombat&&!s.encounterResolved,"not_in_combat");var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(a.role==ActorRole.Paladin&&!a.isEnemy&&target.isEnemy&&(Config.v11==null||Config.AllowsAction(a,P6SmiteId)),"role_action_forbidden");
            if(free)Require(P6MajorAvailable(a),"major_unavailable");Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"actor_busy");Require(P6Line(c,target.id)&&Distance(a,target)<=Config.p6.smiteRange+.001f,"target_out_of_range");
            var q=new ActionQuote{legal=true,rollType="Attack",rollOwnerId=a.id,bonus=a.attackBonus+(V11Core(a)?V11Buff("core_smite_accuracy"):0),targetNumber=EffectiveArmorClass(target),range=Config.p6.smiteRange};
            if(V11Core(a)&&V11Buff("core_smite_accuracy")>0)q.sources.Add("誓锋校准 +"+V11Buff("core_smite_accuracy"));
            if(P6Mark(s,target)!=null){q.advantage=true;q.sources.Add("裂光印：攻击优势");}if(target.staggerRemaining>0){q.advantage=true;q.sources.Add("暴露：攻击优势");}
            if(V13Enabled&&target.statuses.Any(x=>x.sourceId==V12Evan&&x.v12?.family=="opening"&&x.remaining>0&&x.stacks>0)){q.advantage=true;q.sources.Add("埃文弱点：下一次技能攻击优势");}
            if(a.statuses.Any(x=>x.id=="attack-disadvantage"&&x.remaining>0)){q.disadvantage=true;q.sources.Add("攻击劣势");}q.successProbability=Probability("Attack",q.bonus,q.targetNumber,q.advantage,q.disadvantage);return q;
        }
        public static double P6CriticalProbability(int bonus,int armorClass,bool advantage,bool disadvantage,int minimumRaw=19)
        {int passed=0,total=0;for(int a=1;a<=20;a++)for(int b=1;b<=(advantage!=disadvantage?20:1);b++){int face=advantage==disadvantage?a:advantage?Math.Max(a,b):Math.Min(a,b);if(face>=minimumRaw&&AttackHits(face,face+bonus,armorClass))passed++;total++;}return(double)passed/total;}
        static void P6Evaluate(RollTicket t)
        {if(t.p6==null)return;t.critical=t.hit&&t.selectedFace>=t.p6.criticalMinimumRaw;t.p6.criticalSource=t.critical?(t.selectedFace==20?"自然20":"誓锋"):null;}
        static void P6ClearReleasedHold(RollTicket t){if(t.p6!=null)t.p6.overchargeHeld=false;}
        static void P6CleanupMarks(CombatState s)
        {if(s.p6==null)return;foreach(var target in s.actors.Concat(s.enemies))target.statuses.RemoveAll(x=>x.p6!=null&&(!target.Alive||s.Actor(x.p6.casterId)?.Alive!=true));}
        static void CancelP6InterruptedCasts(CombatState s)
        {if(s.p6==null)return;foreach(var a in s.actors){var b=a.p6?.bindIntent;if(P6BindActive(a)&&(!a.Alive||a.staggerRemaining>0)){bool casting=b.stage=="Casting";b.stage="Canceled";b.reason="caster_interrupted";if(casting)a.windupRemaining=0;}}}
        void P6BeginWaitingCast(ActorState a)
        {var b=a.p6?.bindIntent;if(b?.stage!="WaitingBasic"||a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved)return;b.stage="Casting";b.remaining=Config.p6.bindWindup;a.majorRemaining=Config.p6.bindWindup+Config.p6.smiteRecovery;a.windupRemaining=Config.p6.bindWindup;a.p6.basicRecovery=false;if(a.p5!=null)a.p5.basicRecovery=false;}
        void P6AfterCommand(CombatState s,CombatCommand command)
        {if(Config.p6==null)return;var a=s.Actor(command.actorId);if(command.kind==CommandKind.ResolveBasicAttack&&a?.p6!=null&&a.basicAttackIntent?.resolved==true){a.p6.basicRecovery=true;P6BeginWaitingCast(a);}CancelP6InterruptedCasts(s);}
        void TickP6State(CombatState s,float dt)
        {
            if(Config.p6==null||dt<=0)return;
            foreach(var a in s.actors.Concat(s.enemies))if(a.p6!=null){a.p6.bindCooldown=Down(a.p6.bindCooldown,dt);var b=a.p6.bindIntent;if(b?.stage=="WaitingBasic"){P6BeginWaitingCast(a);continue;}if(b!=null&&b.stage=="Casting"){if(!a.Alive||a.staggerRemaining>0){b.stage="Canceled";b.reason="caster_interrupted";a.windupRemaining=0;}else b.remaining=Down(b.remaining,dt);}}
            P6CleanupMarks(s);
        }
        void ValidateP6Configuration()
        {
            if(Config.p6==null)return;var c=Config.p6;Require(c.rulesVersion=="p6-smite-070-1"&&c.presetId=="preset_greatsword_smite"&&c.paladinTemplate!=null&&c.paladinTemplate.role==ActorRole.Paladin&&c.focus!=null,"invalid_p6_config");ValidateP6FocusConfiguration(c.focus);
            Require(new[]{c.passiveMomentumPerSimulationSecond,c.guardMomentum,c.guardInterval,c.markMomentum,c.missMomentum,c.basicInterval,c.basicWindup,c.basicRange,c.basicAngle,c.smiteRange,c.smiteWindup,c.smiteRecovery,c.queuedStartSeconds,c.bindRange,c.bindWindup,c.bindCooldown,c.markSeconds,c.radiantMultiplier,c.rootNormalSeconds,c.rootEliteSeconds,c.bossSlowSeconds,c.bossSlowMultiplier}.All(x=>Finite(x)&&x>0),"invalid_p6_parameters");
            Require(c.capacity==2&&c.maximumCapacity==3&&c.newEncounterFloor==2&&c.markRewardsPerEncounter==2&&c.weaponDice==2&&c.weaponSides==6&&c.smiteRadiantDice==3&&c.overchargeRadiantDice==2&&c.radiantSides==8&&c.smiteCriticalRaw>=17&&c.smiteCriticalRaw<=20&&c.radiantMultiplier<=1.75f&&c.bossSlowMultiplier<=1&&c.missMomentum<=50&&c.basicAngle<=180,"invalid_p6_balance_bounds");
        }
        void ValidateP6State(CombatState s)
        {
            if(Config.p6==null){Require(s.p6==null&&s.focusBudget?.p6==null&&s.rolls.All(t=>t.p6==null),"p6_state_without_rules");return;}
            Require(s.focusBudget?.p6!=null,"p6_focus_state_missing");
            Require(s.p6!=null&&s.p6.rulesVersion==Config.p6.rulesVersion&&s.p6.presetId==Config.p6.presetId&&s.p6.damageRandom!=null&&(s.p6.damageRandom.increment&1UL)==1&&s.p6.damageRandom.algorithmVersion=="pcg32-v1","invalid_p6_state");
            Require(s.p6.resourceEvents!=null&&s.p6.damageResults!=null&&s.p6.bindResults!=null&&s.p6.markRewardCount>=0&&s.p6.markRewardCount<=Config.p6.markRewardsPerEncounter&&Finite(s.p6.lastGuardSimulationTime),"invalid_p6_ledger");
            Require(s.p6.resourceEvents.Select(x=>x.root).Distinct().Count()==s.p6.resourceEvents.Count&&s.p6.resourceEvents.All(x=>!string.IsNullOrEmpty(x.root)&&Finite(x.qualifiedAmount)&&x.qualifiedAmount>=0&&Finite(x.creditedAmount)&&x.creditedAmount>=0&&x.creditedAmount<=x.qualifiedAmount),"invalid_p6_resource_events");
            Require(s.p6.damageResults.Select(x=>x.id).Distinct().Count()==s.p6.damageResults.Count,"duplicate_p6_damage");
            foreach(var d in s.p6.damageResults)
            {
                Require(d!=null&&!string.IsNullOrEmpty(d.rootId)&&d.id==d.rootId+"/damage/"+d.targetId&&s.Actor(d.actorId)!=null&&s.Actor(d.targetId)!=null&&d.attackFaces!=null&&d.attackFaces.Count>=1&&d.attackFaces.Count<=2&&d.attackFaces.All(x=>x>=1&&x<=20)&&d.selectedRaw>=1&&d.selectedRaw<=20,"invalid_p6_damage_identity");
                Require(d.weaponFaces!=null&&d.radiantFaces!=null&&d.weaponFaces.All(x=>x>=1&&x<=6)&&d.radiantFaces.All(x=>x>=1&&x<=8)&&d.weaponFaces.Count<=4&&d.radiantFaces.Count<=(Config.v11==null?14:18)&&new[]{d.physicalBeforeMitigation,d.radiantBeforeMitigation,d.physicalApplied,d.radiantApplied,d.absorbed,d.totalDamage}.All(x=>x>=0)&&d.totalDamage==d.physicalApplied+d.radiantApplied&&Finite(d.radiantMultiplier)&&d.radiantMultiplier>=1&&d.radiantMultiplier<=1.75f,"invalid_p6_damage_components");
                Require(!d.markConsumed||d.hit&&d.radiantApplied>0&&!string.IsNullOrEmpty(d.markId)&&d.actionId==P6SmiteId,"invalid_p6_mark_consumption");
            }
            Require(s.p6.bindResults.Select(x=>x.id).Distinct().Count()==s.p6.bindResults.Count&&s.p6.bindResults.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&s.Actor(x.casterId)?.role==ActorRole.Mage&&s.Actor(x.targetId)!=null&&x.raw>=1&&x.raw<=20&&Finite(x.controlSeconds)&&x.controlSeconds>=0),"invalid_p6_bind_results");
            Require(AvailableP6Charges(s)>=0,"p6_shared_charge_overreserved");
            foreach(var a in s.actors.Concat(s.enemies))
            {Require(a.p6!=null&&Finite(a.p6.bindCooldown)&&a.p6.bindCooldown>=0&&Finite(a.p6.physicalDamageMultiplier)&&a.p6.physicalDamageMultiplier>=0&&a.p6.physicalDamageMultiplier<=2&&Finite(a.p6.radiantDamageMultiplier)&&a.p6.radiantDamageMultiplier>=0&&a.p6.radiantDamageMultiplier<=2&&a.p6.absorptionShield>=0,"invalid_p6_actor");var b=a.p6.bindIntent;if(b!=null)Require(!string.IsNullOrEmpty(b.id)&&s.Actor(b.targetId)!=null&&new[]{"WaitingBasic","Casting","Resolved","Canceled"}.Contains(b.stage)&&Finite(b.remaining)&&b.remaining>=0&&a.role==ActorRole.Mage,"invalid_p6_bind_intent");foreach(var status in a.statuses.Where(x=>x.p6!=null))Require(status.id=="p6-radiant-mark"&&!string.IsNullOrEmpty(status.p6.id)&&s.Actor(status.p6.casterId)?.role==ActorRole.Mage&&Finite(status.p6.multiplier)&&status.p6.multiplier>=1&&status.p6.multiplier<=1.75f,"invalid_p6_mark");}
            foreach(var t in s.rolls.Where(t=>t.p6!=null))
            {var p=t.p6;Require(t.actionId==P6SmiteId&&t.purpose=="Tactical"&&t.p5==null&&s.Actor(t.actorId)?.role==ActorRole.Paladin&&p.criticalMinimumRaw>=17&&p.criticalMinimumRaw<=20&&Finite(p.committedAt)&&Finite(p.startDeadline),"invalid_p6_ticket");Require(!p.overchargeHeld||p.overchargeSelected&&!p.overchargePaid&&new[]{"Rolling","AwaitDecision","SuspendedAwaitDecision","Queued"}.Contains(t.stage),"invalid_p6_charge_hold");Require(!p.preacceptBaseResult||!p.overchargeSelected&&!t.rerollUsed&&!t.modifyUsed,"invalid_p6_direct_ticket");}
        }
    }
}
