using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public static void InitializeP5State(CombatState s,CombatConfig config)
        {
            if(config.p5==null)return;
            if(s.p5==null)s.p5=new P5CombatState();
            foreach(var actor in s.actors.Concat(s.enemies))
            {
                if(actor.p5!=null)continue;
                actor.p5=new P5ActorState();string template;
                if(actor.isEnemy&&s.world.strings.TryGetValue("enemyTemplate/"+actor.id,out template)&&config.p5.frontalShieldTemplateIds.Contains(template))actor.p5.frontalShieldAcBonus=config.p5.frontalShieldAcBonus;
            }
        }
        public static void ResetP5EncounterState(CombatState s)
        {
            if(s.p5==null)return;
            s.p5=new P5CombatState();
            foreach(var a in s.actors.Concat(s.enemies)){if(a.p5!=null)a.p5=new P5ActorState{frontalShieldAcBonus=a.p5.frontalShieldAcBonus};a.statuses.RemoveAll(x=>x.p5!=null);}
        }
        public float P5MovementMultiplier(string actorId)
        {
            var a=State.Actor(actorId);if(a==null||Config.p5==null)return 1;
            float multiplier=1;
            foreach(var status in a.statuses)if(status.remaining>0&&status.p5!=null)multiplier=Math.Min(multiplier,status.p5.moveMultiplier);
            if(a.p5?.preparation!=null&&a.p5.preparation.actionId=="f_brace"&&a.p5.preparation.ready&&a.guardRemaining>0)multiplier=Math.Min(multiplier,Config.p5.braceMoveMultiplier);
            return multiplier;
        }
        // Only paid tactical actions may take over the tail of a completed basic
        // recovery. Free followups still obey the unchanged basic attack cadence.
        public bool P5MajorAvailable(ActorState actor)=>actor!=null&&!P5Busy(actor)&&(actor.majorRemaining<=0||Config.p5!=null&&actor.p5!=null&&actor.p5.basicRecovery);
        public ActionQuote QuoteP5(CombatCommand command)
        {
            try{return Config.p5==null?QuoteInternal(State,command.actorId,command.targetId,command.actionId,true):P5Quote(State,command,true);}
            catch(Exception ex){return new ActionQuote{legal=false,reason=ex.Message};}
        }
        public List<P5OpportunityQuote> GetOpportunityChoices(string actorId,string targetId,string actionId)
        {
            var result=new List<P5OpportunityQuote>();if(Config.p5==null||State.p5==null)return result;
            foreach(var token in State.p5.opportunities.Where(x=>x.targetId==targetId&&P5TokenActive(State,x)))
            {
                var row=new P5OpportunityQuote{id=token.id,kind=token.p5.kind,ownerId=token.ownerId,targetId=token.targetId,neutral=token.p5.neutral,remaining=token.remaining};
                try{P5SelectedToken(State,actorId,targetId,actionId,token.id);row.legal=true;row.description=P5BonusDescription(actionId,token.p5.kind);}
                catch(Exception ex){row.reason=ex.Message;}
                result.Add(row);
            }
            return result;
        }
        static string P5BonusDescription(string action,string kind)
        {
            if(action=="basic_followup")return kind=="off_balance"?"额外削韧 1":kind=="exposed"?"忽略正面盾防最多 2 AC":"命中短推 0.8m";
            if(action=="shove")return kind=="bound"?"推距 +1m，额外削韧 1":"推距 +1m";
            if(action=="sneak")return kind=="off_balance"?"本次攻击优势":kind=="exposed"?"忽略正面盾防最多 2 AC，额外 1d6":"命中后安全撤步 1.5m";
            return kind=="off_balance"?"主目标抵抗失败额外削韧 2":kind=="exposed"?"主目标 CON 豁免 -2":"主目标抵抗失败推距 +1m";
        }
        static bool P5TokenActive(CombatState s,SetupToken t)
        {
            if(t==null||t.p5==null||t.consumed||t.remaining<=0||t.p5.status!="Available"||t.p5.encounterAttemptId!=s.encounterId)return false;
            var target=s.Actor(t.targetId);if(target==null||!target.Alive)return false;
            return t.p5.kind!="bound"||target.statuses.Any(c=>c.p5!=null&&c.p5.instanceId==t.p5.sourceConditionId&&c.remaining>0);
        }
        SetupToken P5SelectedToken(CombatState s,string actorId,string targetId,string actionId,string id)
        {
            if(string.IsNullOrEmpty(id))return null;
            var token=s.p5.opportunities.Find(x=>x.id==id);Require(token!=null,"opportunity_missing");Require(P5TokenActive(s,token),token.consumed?"opportunity_already_used":"opportunity_expired");
            Require(token.targetId==targetId,"opportunity_wrong_target");Require(token.p5.neutral||token.ownerId!=actorId,"opportunity_requires_other_actor");
            Require(token.p5.neutral||s.actors.Any(a=>a.id==token.ownerId),"opportunity_source_invalid");
            Require(actionId=="basic_followup"||actionId=="sneak"||actionId=="control"||actionId=="shove"&&(token.p5.kind=="off_balance"||token.p5.kind=="bound"),"opportunity_not_applicable");return token;
        }
        void P5Produce(CombatState s,string kind,ActorState owner,ActorState target,string root,string condition=null,bool neutral=false)
        {
            if(Config.p5==null||target==null||!target.isEnemy||!target.Alive||string.IsNullOrEmpty(root)||!s.inCombat)return;
            string production=s.encounterId+"/"+root+"/"+target.id;
            if(s.p5.productionRoots.Contains(production))return;s.p5.productionRoots.Add(production);
            if(s.p5.opportunities.Any(x=>x.targetId==target.id&&x.p5.kind==kind&&P5TokenActive(s,x)))return;
            float duration=Config.p5.opportunitySeconds;
            if(kind=="bound")
            {
                var source=target.statuses.Find(x=>x.p5!=null&&x.p5.instanceId==condition&&x.remaining>0);if(source==null)return;duration=Math.Min(duration,source.remaining);
            }
            s.p5.opportunities.Add(new SetupToken{id=production+"/opportunity",ownerId=owner?.id,targetId=target.id,remaining=duration,threatId=root,p5=new P5OpportunityData{kind=kind,sourceActionId=root,sourceConditionId=condition,encounterAttemptId=s.encounterId,neutral=neutral,createdSimulationTime=s.simulationSeconds}});
        }
        P5ActionContext P5Context(CombatState s,CombatCommand c,string root,bool paid)
        {
            var token=P5SelectedToken(s,c.actorId,c.targetId,c.actionId,c.p5?.opportunityId);
            var context=new P5ActionContext{root=root,encounterAttemptId=s.encounterId,targetId=c.targetId,paid=paid,startDeadlineSimulationTime=s.simulationSeconds+Config.p5.readyStartSeconds};
            var actor=s.Actor(c.actorId);var target=s.Actor(c.targetId);
            float dx=c.p5?.directionX??0,dz=c.p5?.directionZ??0;
            if(dx*dx+dz*dz<.00001f){dx=target.x-actor.x;dz=target.z-actor.z;if(c.actionId=="sneak"){dx=-dx;dz=-dz;}}
            float length=(float)Math.Sqrt(dx*dx+dz*dz);context.directionX=length<.001f?0:dx/length;context.directionZ=length<.001f?1:dz/length;
            context.pushDistance=c.actionId=="shove"?Config.p5.shoveDistance:c.actionId=="control"?Config.p5.thunderDistance:0;
            if(token==null)return context;
            context.opportunityId=token.id;context.opportunityKind=token.p5.kind;context.ownerId=token.ownerId;context.sourceConditionId=token.p5.sourceConditionId;context.neutral=token.p5.neutral;context.crossActor=!context.neutral&&token.ownerId!=c.actorId;
            string kind=token.p5.kind;
            if(c.actionId=="basic_followup")
            {context.bonusBreak=kind=="off_balance"?1:0;context.ignoreShieldAc=kind=="exposed"?2:0;context.pushDistance=kind=="bound"?Config.p5.followupDistance:0;}
            else if(c.actionId=="shove"){context.pushDistance+=1;context.bonusBreak=kind=="bound"?1:0;}
            else if(c.actionId=="sneak"){context.advantage=kind=="off_balance";context.ignoreShieldAc=kind=="exposed"?2:0;context.extraDamageDice=kind=="exposed"?1:0;context.retreatDistance=kind=="bound"?Config.p5.retreatDistance:0;}
            else if(c.actionId=="control"){context.bonusBreak=kind=="off_balance"?2:0;context.savePenalty=kind=="exposed"?2:0;if(kind=="bound")context.pushDistance+=1;}
            return context;
        }
        static void P5Consume(CombatState s,P5ActionContext context,string consumer)
        {
            if(string.IsNullOrEmpty(context.opportunityId))return;
            var token=s.p5.opportunities.Find(x=>x.id==context.opportunityId);Require(token!=null&&P5TokenActive(s,token),"opportunity_already_used");
            token.consumed=true;token.usedBy=consumer;token.usedRoot=context.root;token.p5.status="Consumed";
            s.consumedSetupIds.Add(token.id);s.setupUses.Add(new SetupUse{root=context.root,tokenId=token.id,ownerId=token.ownerId,consumerId=consumer,targetId=token.targetId});
        }
        static bool P5Busy(ActorState a)=>a.staggerRemaining>0||a.windupRemaining>0||!string.IsNullOrEmpty(a.heldBy)||(a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved);
        static void P5MajorFree(ActorState a)
        {Require(!P5Busy(a)&&(a.majorRemaining<=0||a.p5!=null&&a.p5.basicRecovery),"major_unavailable");}
        float P5Cooldown(ActorState a,string action)=>action=="shove"?a.p5.shoveCooldown:action=="sneak"?a.p5.sneakCooldown:action=="control"?a.p5.thunderCooldown:0;
        P5TargetEvidence P5Evidence(CombatCommand c,string targetId)
        {return c.p5?.targets?.Find(x=>x.targetId==targetId);}
        bool P5Line(CombatCommand c,string targetId)=>c.p5?.targets==null?c.lineOfSight:P5Evidence(c,targetId)?.lineOfSight==true;
        bool P5AreaCanStart(CombatState s,ActorState source,RollTicket ticket,CombatCommand c)=>s.actors.Concat(s.enemies).Any(a=>a.Alive&&a.id!=source.id&&P5Line(c,a.id)&&(P5Evidence(c,a.id)?.inArea??true)&&P5InCone(source,a,ticket.p5.directionX,ticket.p5.directionZ,ticket.spec.range));
        bool P5InCone(ActorState actor,ActorState target,float dx,float dz,float range)
        {
            float tx=target.x-actor.x,tz=target.z-actor.z,d=(float)Math.Sqrt(tx*tx+tz*tz);if(d>range+.001f)return false;if(d<.001f)return true;
            float norm=(float)Math.Sqrt(dx*dx+dz*dz);if(norm<.001f)return false;
            return (tx*dx+tz*dz)/(d*norm)>=(float)Math.Cos(Config.p5.thunderAngle*.5*Math.PI/180)-.0001f;
        }
        int P5Stability(CombatState s,ActorState target)
        {string value;return s.world.strings.TryGetValue("enemyTemplate/"+target.id,out value)?value=="boss"?Config.p5.bossStabilityDc:value=="elite"?Config.p5.eliteStabilityDc:Config.p5.ordinaryStabilityDc:Config.p5.ordinaryStabilityDc;}
        static int P5ConSave(ActorState target)=>target.constitutionModifier;
        ActionQuote P5Quote(CombatState s,CombatCommand c,bool checkFree)
        {
            Require(s.inCombat&&!s.encounterResolved,"not_in_combat");var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(!a.isEnemy&&target.isEnemy,"invalid_target_team");
            var spec=Config.Action(c.actionId);Require(spec!=null,"unknown_action");Require(Config.AllowsAction(a,c.actionId),"role_action_forbidden");
            if(checkFree)P5MajorFree(a);Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");Require(P5Cooldown(a,c.actionId)<=0,"ability_cooldown");
            Require(Distance(a,target)<=spec.range+.001f,"target_out_of_range");Require(P5Line(c,target.id),"line_of_sight_blocked");
            Require(a.spellSlots-a.heldSpellSlots>=spec.spellCost&&a.items-a.heldItems>=spec.itemCost,"long_term_resource_unavailable");
            var context=P5Context(s,c,"preview",true);var q=new ActionQuote{legal=true,range=spec.range,rollType=spec.rollType,spellCost=spec.spellCost,itemCost=spec.itemCost};
            if(spec.rollType=="Check"){q.rollOwnerId=a.id;q.targetNumber=P5Stability(s,target);q.bonus=a.strengthModifier+a.proficiency;}
            else if(spec.rollType=="Save")
            {
                Require(P5InCone(a,target,context.directionX,context.directionZ,spec.range)&&(P5Evidence(c,target.id)?.inArea??true),"primary_target_outside_area");
                q.rollOwnerId=target.id;q.targetNumber=8+a.intelligenceModifier+a.proficiency;q.bonus=P5ConSave(target)-context.savePenalty;
                q.advantage=target.statuses.Any(x=>x.id=="save-advantage"&&x.remaining>0);
                q.disadvantage=target.staggerRemaining>0;
            }
            else
            {
                q.rollOwnerId=a.id;q.targetNumber=EffectiveArmorClass(target);q.bonus=a.attackBonus;
                var evidence=P5Evidence(c,target.id);if(spec.ranged)q.targetNumber+=evidence?.coverAcBonus??0;
                q.targetNumber+=Math.Max(0,(evidence?.frontalShieldAcBonus??0)-context.ignoreShieldAc);
                q.advantage=context.advantage||IsBehind(a,target)&&a.role==ActorRole.Rogue||target.staggerRemaining>0||a.statuses.Any(x=>x.id=="flanking"&&x.remaining>0)||spec.ranged&&a.aimReady;
                q.disadvantage=a.statuses.Any(x=>x.id=="suppressed"&&x.remaining>0);
                if(c.actionId=="sneak")
                {
                    bool ally=s.actors.Any(x=>x.id!=a.id&&x.Alive&&x.staggerRemaining<=0&&!x.statuses.Any(y=>y.id=="incapacitated"&&y.remaining>0)&&Distance(x,target)<=2.001f);
                    bool finalAdvantage=q.advantage&&!q.disadvantage,finalDisadvantage=q.disadvantage&&!q.advantage;
                    Require(!finalDisadvantage&&(finalAdvantage||ally),"sneak_requires_advantage_or_ally");
                }
            }
            if(!string.IsNullOrEmpty(context.opportunityId))q.sources.Add("协同："+context.opportunityKind+"（"+(context.neutral?"环境":context.ownerId)+"）");
            if(q.advantage)q.sources.Add("优势来源");if(q.disadvantage)q.sources.Add("劣势来源");
            q.successProbability=Probability(q.rollType,q.bonus,q.targetNumber,q.advantage,q.disadvantage);return q;
        }
        void ValidateP5Configuration()
        {
            var p=Config.p5;if(p==null)return;
            Require(p.version=="p5.1"&&p.actions!=null&&p.actions.Count==3&&p.actions.Select(x=>x.id).OrderBy(x=>x).SequenceEqual(new[]{"control","shove","sneak"}),"invalid_p5_config");
            Require(new[]{p.opportunitySeconds,p.readyStartSeconds,p.prepareSeconds,p.prepareCooldown,p.braceSeconds,p.flankAuthorizationSeconds,p.frostSeconds,p.shoveDistance,p.thunderDistance,p.followupDistance,p.retreatDistance,p.breakWindowSeconds,p.breakRecoverySeconds,p.impactMinimumDistance,p.shoveCooldown,p.sneakCooldown,p.thunderCooldown}.All(x=>Finite(x)&&x>0),"invalid_p5_timers");
            Require(p.followupMomentum>=0&&p.followupMomentum<=10&&p.comboMomentum>=0&&p.comboMomentum<=60&&p.comboFocus>=0&&Finite(p.comboFocus)&&p.impactMaximumUses==2&&p.thunderAngle>0&&p.thunderAngle<=180&&p.frostMoveMultiplier>0&&p.frostMoveMultiplier<=1&&p.braceMoveMultiplier>0&&p.braceMoveMultiplier<=1&&p.bossPushMultiplier>=0&&p.bossPushMultiplier<=1&&p.braceEligibleTemplateIds!=null&&p.frontalShieldTemplateIds!=null&&p.frontalShieldAcBonus>=0&&p.frontalShieldAcBonus<=2&&p.ordinaryStabilityDc>0&&p.eliteStabilityDc>0&&p.bossStabilityDc>0,"invalid_p5_limits");
            Require(p.actions.All(a=>a!=null&&new[]{"Attack","Save","Check"}.Contains(a.rollType)&&Finite(a.range)&&a.range>0&&Finite(a.windup)&&a.windup>=0&&Finite(a.breakDamage)&&a.breakDamage>=0&&a.damage>=0&&a.spellCost>=0&&a.itemCost>=0),"invalid_p5_action_spec");
        }
        void ValidateP5State(CombatState s)
        {
            if(Config.p5==null){Require(s.p5==null,"p5_state_without_rules");return;}
            Require(s.p5!=null&&s.p5.version=="p5.1"&&s.p5.opportunities!=null&&s.p5.productionRoots!=null&&s.p5.impactSurfaces!=null,"missing_p5_state");
            Require(s.p5.productionRoots.Distinct().Count()==s.p5.productionRoots.Count&&s.p5.opportunities.Select(x=>x.id).Distinct().Count()==s.p5.opportunities.Count,"duplicate_p5_source");
            foreach(var a in s.actors.Concat(s.enemies))
            {
                Require(a.p5!=null&&new[]{a.p5.shoveCooldown,a.p5.sneakCooldown,a.p5.thunderCooldown,a.p5.breakRecovery}.All(x=>Finite(x)&&x>=0),"invalid_p5_actor");
                if(a.p5.preparation!=null)Require(Finite(a.p5.preparation.remaining)&&a.p5.preparation.remaining>=0,"invalid_p5_preparation");
                foreach(var c in a.statuses.Where(x=>x.p5!=null))Require(!string.IsNullOrEmpty(c.p5.instanceId)&&c.p5.moveMultiplier>0&&c.p5.moveMultiplier<=1,"invalid_p5_condition");
            }
            foreach(var t in s.p5.opportunities)
            {
                Require(t!=null&&t.p5!=null&&new[]{"off_balance","exposed","bound"}.Contains(t.p5.kind)&&new[]{"Available","Consumed","Expired"}.Contains(t.p5.status)&&s.enemies.Any(a=>a.id==t.targetId)&&(t.p5.neutral||s.actors.Any(a=>a.id==t.ownerId))&&Finite(t.remaining)&&t.remaining>=0&&t.p5.encounterAttemptId==s.encounterId,"invalid_p5_opportunity");
                Require(!t.consumed||t.p5.status=="Consumed"&&!string.IsNullOrEmpty(t.usedRoot),"invalid_p5_consumption");
            }
            Require(s.p5.opportunities.Where(x=>P5TokenActive(s,x)).GroupBy(x=>x.targetId+"/"+x.p5.kind).All(x=>x.Count()==1),"duplicate_p5_target_kind");
            Require(s.p5.impactSurfaces.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&x.uses>=0&&x.uses<=Config.p5.impactMaximumUses&&x.roots!=null&&x.roots.Distinct().Count()==x.roots.Count),"invalid_p5_impact");
            foreach(var t in s.rolls.Where(x=>x.p5!=null))ValidateP5Context(s,t.p5,t.actorId);
            foreach(var a in s.actors.Where(x=>x.basicAttackIntent?.p5!=null))ValidateP5Context(s,a.basicAttackIntent.p5,a.id);
        }
        static void ValidateP5Context(CombatState s,P5ActionContext c,string actor)
        {
            Require(c.encounterAttemptId==s.encounterId&&!string.IsNullOrEmpty(c.root)&&Finite(c.startDeadlineSimulationTime)&&c.startDeadlineSimulationTime>=0&&Finite(c.directionX)&&Finite(c.directionZ)&&c.targets!=null&&c.damageFaces!=null,"invalid_p5_context");
            if(!string.IsNullOrEmpty(c.opportunityId))Require(s.p5.opportunities.Any(t=>t.id==c.opportunityId&&t.consumed&&t.usedRoot==c.root&&t.usedBy==actor),"p5_context_consumption_missing");
        }
    }
}
