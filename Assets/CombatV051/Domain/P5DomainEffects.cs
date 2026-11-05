using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public float P5DisplacementDistance(P5ActionContext context,string targetId)
        {
            if(Config.p5==null||context==null)return 0;
            float distance=context.targetId==targetId?context.pushDistance:Config.p5.thunderDistance;string template;
            if(State.world.strings.TryGetValue("enemyTemplate/"+targetId,out template)&&template=="boss")distance*=Config.p5.bossPushMultiplier;return distance;
        }
        float P5DistanceFor(CombatState s,P5ActionContext context,string targetId)
        {
            float distance=context.targetId==targetId?context.pushDistance:Config.p5.thunderDistance;string template;
            if(s.world.strings.TryGetValue("enemyTemplate/"+targetId,out template)&&template=="boss")distance*=Config.p5.bossPushMultiplier;return distance;
        }
        int P5ApplyBreak(ActorState target,int amount)
        {
            if(target.statuses.Any(x=>x.id=="v13-break-immunity"&&x.remaining>0))return 0;
            if(amount<=0||target.p5==null||target.p5.breakRecovery>0||target.staggerRemaining>0)return 0;
            float prior=target.breakValue;target.breakValue=Math.Max(0,target.breakValue-amount);
            if(prior>0&&target.breakValue==0){bool boss=V13Enabled&&State.v13.boss?.actorId==target.id;target.staggerRemaining=boss?3:Config.p5.breakWindowSeconds;target.p5.breakRecovery=boss?9:Config.p5.breakWindowSeconds+Config.p5.breakRecoverySeconds;}
            return (int)Math.Ceiling(prior-target.breakValue);
        }
        bool P5Displace(CombatState s,ActorState source,ActorState target,P5ActionContext context,CombatCommand c,CombatResult result)
        {
            float max=P5DistanceFor(s,context,target.id);var evidence=P5Evidence(c,target.id);
            float x=target.x,z=target.z,dx=context.directionX,dz=context.directionZ;
            // Cone pushes are radial. A shove follows the confirmed chosen direction.
            if(s.rolls.Any(t=>t.p5==context&&t.actionId=="control")){dx=target.x-source.x;dz=target.z-source.z;float l=(float)Math.Sqrt(dx*dx+dz*dz);if(l>.001f){dx/=l;dz/=l;}else{dx=context.directionX;dz=context.directionZ;}}
            if(c.p5?.targets!=null)
            {Require(evidence!=null&&evidence.hasDisplacement,"displacement_evidence_required");x=evidence.x;z=evidence.z;}
            else{x+=dx*max;z+=dz*max;}
            float distance=DistanceTo(target,x,z);Require(distance<=max+.002f,"shove_destination_invalid");
            float fromX=target.x,fromZ=target.z;target.x=x;target.z=z;
            if(distance>.001f)result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=context.root,kind="push",fromX=fromX,fromZ=fromZ,x=x,z=z,effective=true});
            if(target.isEnemy&&evidence!=null&&evidence.impactRegistered&&!string.IsNullOrEmpty(evidence.impactSurfaceId)&&distance>=Config.p5.impactMinimumDistance&&Math.Abs(evidence.actualDistance-distance)<=.05f)
            {
                var surface=s.p5.impactSurfaces.Find(i=>i.id==evidence.impactSurfaceId);
                if(surface==null){surface=new P5ImpactUse{id=evidence.impactSurfaceId};s.p5.impactSurfaces.Add(surface);}
                string root=context.root+"/"+target.id;
                if(!surface.roots.Contains(root)&&surface.uses<Config.p5.impactMaximumUses)
                {
                    surface.roots.Add(root);surface.uses++;int damage=RandomStreams.NextInt(s.random.backgroundCombat,1,7);damage=P5Damage(s,target,damage,context.root,false,source);int broken=P5ApplyBreak(target,1);
                    result.damage+=damage;result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=context.root,kind="impact",damage=damage,breakDamage=broken,effective=true});
                }
            }
            return distance>.001f;
        }
        void P5Retreat(CombatState s,ActorState actor,P5ActionContext context,CombatCommand c,CombatResult result)
        {
            if(context.retreatDistance<=0)return;var evidence=P5Evidence(c,actor.id);
            float x=actor.x+context.directionX*context.retreatDistance,z=actor.z+context.directionZ*context.retreatDistance;
            if(c.p5?.targets!=null){Require(evidence!=null&&evidence.hasDisplacement,"retreat_evidence_required");x=evidence.x;z=evidence.z;}
            Require(DistanceTo(actor,x,z)<=context.retreatDistance+.002f,"retreat_destination_invalid");
            float beforeX=actor.x,beforeZ=actor.z;actor.x=x;actor.z=z;
            result.p5Effects.Add(new P5EffectResult{targetId=actor.id,root=context.root,kind="retreat",fromX=beforeX,fromZ=beforeZ,x=x,z=z,effective=DistanceTo(actor,beforeX,beforeZ)>.001f});
        }
        int P5Damage(CombatState s,ActorState target,int amount,string root,bool direct=false,ActorState explicitSource=null)
        {
            if(amount<=0)return 0;int nominal=amount,v12Before=target.hp;var owner=explicitSource??s.Actor(s.rolls.Find(t=>t.id==root)?.actorId);double scaled=V14SourceDamage(owner,amount);amount=V14Enabled?V14Mitigate(target,owner,scaled,"physical"):V12Enabled?V12Mitigate(target,amount):V11Absorb(target,amount);target.hp=Math.Max(0,target.hp-amount);target.aimPreparing=false;target.aimReady=false;target.aimRemaining=0;BreakConcentrationFromDamage(s,target);
            int applied=v12Before-target.hp;V14LogDamage(s,owner,target,root,"p5-effect",direct?"direct":"impact",nominal,scaled,applied);if(target.isEnemy)AwardFocusBossPhaseIfReached(s,target,root);var v12Ticket=direct?s.rolls.Find(t=>t.id==root):null;V12OnKilled(s,v12Ticket==null?null:s.Actor(v12Ticket.actorId),target,root,v12Ticket?.actionId,applied,v12Ticket?.id);if(v12Ticket!=null)V12OnDirectDamage(s,s.Actor(v12Ticket.actorId),target,root,v12Ticket.actionId,applied);
            return V12Enabled?applied:nominal;
        }
        CombatResult P5ResolveQueued(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s),"world_frozen");var q=s.queue.Find(x=>x.id==c.queueId);Require(q!=null&&q.stage=="Executing"&&!q.effectApplied,"queue_not_executing");Require(q.windupRemaining<=0,"windup_incomplete");
            var t=s.rolls.Find(x=>x.id==q.ticketId);var actor=s.Actor(q.actorId);var target=s.Actor(q.targetId);
            if(actor==null||!actor.Alive||target==null||!target.Alive){CancelQueue(s,q,t,"target_or_actor_unavailable");return new CombatResult{queueId=q.id,reason=q.reason};}
            if(t.actionId!="control"&&(!P5Line(c,target.id)||Distance(actor,target)>t.spec.range+.001f)){CancelQueue(s,q,t,"execution_target_moved_or_blocked");return new CombatResult{queueId=q.id,reason=q.reason};}
            var result=TicketResult(t);result.executionId=q.executionId;result.p5Effects=new List<P5EffectResult>();P5RefreshDamage(t);
            if(t.actionId=="control")P5ResolveThunder(s,actor,t,c,result);
            else
            {
                float priorBreak=target.breakValue;string threatened=target.attackGroupId;bool wasWindup=target.aiState==1;bool effective=false;
                if(t.actionId=="shove")
                {
                    if(t.hit)
                    {
                        bool moved=P5Displace(s,actor,target,t.p5,c,result);var rules=Config.RulesFor(actor.id);int extra=rules.firstShoveBonus&&!actor.p4FirstShoveUsed?rules.firstShoveBreakBonus:0;if(extra>0)actor.p4FirstShoveUsed=true;int broken=P5ApplyBreak(target,(int)t.spec.breakDamage+t.p5.bonusBreak+extra);
                        if(P5Evidence(c,target.id)?.allowInterrupt!=false){target.aiState=2;target.aiRemaining=Math.Max(.4f,target.aiRemaining);target.attackGroupId=null;}
                        effective=moved;
                        result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=t.id,kind="shove",breakDamage=broken,effective=effective});
                    }
                    else if(t.targetNumber-t.total>=1&&t.targetNumber-t.total<=2)
                    {t.p5.weakEffect=true;int broken=P5ApplyBreak(target,1);result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=t.id,kind="weak-shove",breakDamage=broken,weakEffect=true});}
                }
                else if(t.hit)
                {
                    result.damage=P5Damage(s,target,t.p5.damage,t.id,true);effective=result.damage>0;
                    if(t.actionId=="sneak")P5Retreat(s,actor,t.p5,c,result);
                    result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=t.id,kind=t.actionId,damage=result.damage,effective=effective});
                }
                P5Reward(s,t.p5,effective);
                if(!t.p5.weakEffect&&t.hit){AwardP4ActionOutcome(s,target,t.id,t.id,wasWindup&&target.aiState!=1,!target.Alive,threatened);AwardFocusBreak(s,target,t.id,priorBreak);}
            }
            V14CorrectionCommitted(s,t,result.damage>0||result.p5Effects.Any(x=>x.effective||x.breakDamage>0));q.effectApplied=true;q.stage="Resolved";t.stage="Resolved";Release(s,t,false);actor.windupRemaining=0;actor.p5.basicRecovery=false;
            return result;
        }
        void P5ResolveThunder(CombatState s,ActorState actor,RollTicket ticket,CombatCommand c,CombatResult result)
        {
            bool primaryUsed=false;
            foreach(var target in s.actors.Concat(s.enemies).Where(a=>a.Alive&&a.id!=actor.id).ToList())
            {
                if(!P5InCone(actor,target,ticket.p5.directionX,ticket.p5.directionZ,ticket.spec.range)||!P5Line(c,target.id)||!(P5Evidence(c,target.id)?.inArea??true))continue;
                var saved=ticket.p5.targets.Find(x=>x.targetId==target.id);
                if(saved==null)
                {
                    int natural=RandomStreams.D20(s.random.backgroundCombat);saved=new P5TargetRoll{targetId=target.id,rawFaces=new List<int>{natural},selectedFace=natural,bonus=P5ConSave(target),targetNumber=ticket.targetNumber};
                    saved.total=saved.selectedFace+saved.bonus;saved.savePassed=saved.total>=saved.targetNumber;ticket.p5.targets.Add(saved);
                }
                if(saved.effectApplied)continue;float oldBreak=target.breakValue;string windup=target.attackGroupId;bool wasWindup=target.aiState==1;
                int damage=saved.savePassed?ticket.p5.damage/2:ticket.p5.damage;saved.effectApplied=true;damage=P5Damage(s,target,damage,ticket.id,true);saved.damage=damage;result.damage+=damage;
                bool primary=target.id==ticket.targetId;bool moved=false;
                if(!saved.savePassed&&target.Alive)
                {
                    moved=P5Displace(s,actor,target,ticket.p5,c,result);
                    if(primary&&target.isEnemy&&ticket.p5.bonusBreak>0)P5ApplyBreak(target,ticket.p5.bonusBreak);
                }
                result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=ticket.id,kind="thunder",damage=damage,breakDamage=(int)Math.Ceiling(oldBreak-target.breakValue),effective=damage>0||moved});
                if(primary&&!saved.savePassed)primaryUsed=ticket.p5.opportunityKind=="bound"?moved:ticket.p5.opportunityKind=="off_balance"?oldBreak>target.breakValue:damage>0;
                if(target.isEnemy){AwardP4ActionOutcome(s,target,ticket.id,ticket.id,wasWindup&&target.aiState!=1,!target.Alive,windup);AwardFocusBreak(s,target,ticket.id,oldBreak);}
            }
            P5Reward(s,ticket.p5,primaryUsed);
        }
        void P5BraceContact(CombatState s,ActorState guard,ActorState source,string root)
        {
            if(Config.p5==null||guard?.p5?.preparation==null||source==null||!source.isEnemy)return;
            var prep=guard.p5.preparation;
            string template;bool actualCommitment=source.aiState==1&&source.attackGroupId==root&&s.world.strings.TryGetValue("enemyTemplate/"+source.id,out template)&&Config.p5.braceEligibleTemplateIds.Contains(template);
            if(prep.actionId!="f_brace"||!prep.ready||prep.used||!s.momentumLedger.registeredDangerousThreats.Contains(root)&&!actualCommitment)return;
            prep.used=true;P5Produce(s,"off_balance",guard,source,prep.sourceRoot);
            // A committed contact has happened, so the source enters recovery rather
            // than allowing each projectile of the same attack to become a new stance.
            source.aiState=2;source.aiRemaining=Math.Max(.4f,source.aiRemaining);source.attackGroupId=null;
        }
        void TickP5State(CombatState s,float dt)
        {
            if(dt<=0)return;
            foreach(var a in s.actors.Concat(s.enemies))
            {
                a.preparationCooldown=Down(a.preparationCooldown,dt);a.p5.shoveCooldown=Down(a.p5.shoveCooldown,dt);a.p5.sneakCooldown=Down(a.p5.sneakCooldown,dt);a.p5.thunderCooldown=Down(a.p5.thunderCooldown,dt);a.p5.breakRecovery=Down(a.p5.breakRecovery,dt);
                if(a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved)a.basicAttackIntent.remaining=Down(a.basicAttackIntent.remaining,dt);
                var prep=a.p5.preparation;var order=a.basicOrder;
                if(prep==null)continue;
                if(prep.used){if(prep.actionId=="f_brace"&&a.guardRemaining<=0&&order!=null&&order.kind==BasicOrderKind.Prepare){order.stage="Finished";order.reason="brace_complete";}continue;}
                if(!a.Alive||a.staggerRemaining>0||order==null||order.stage=="Canceled"||s.Actor(prep.targetId)?.Alive!=true||a.hp<order.startingHp)
                {prep.used=true;if(order!=null){order.stage="Canceled";order.reason="preparation_interrupted";}if(prep.actionId=="f_brace")a.guardRemaining=0;continue;}
                if(prep.actionId=="r_flank")
                {
                    prep.remaining=Down(prep.remaining,dt);order.elapsed+=dt;
                    if(prep.remaining==0){prep.used=true;if(order.kind==BasicOrderKind.Prepare){order.stage="Canceled";order.reason="flank_window_expired";}}continue;
                }
                if(!prep.ready)
                {
                    if(DistanceTo(a,order.startX,order.startZ)>.05f){prep.used=true;order.stage="Canceled";order.reason="preparation_moved";continue;}
                    prep.remaining=Down(prep.remaining,dt);order.remaining=prep.remaining;
                    if(prep.remaining>0)continue;prep.ready=true;
                    if(prep.actionId=="f_brace"){a.guardRemaining=Config.p5.braceSeconds;a.guardElapsed=0;order.stage="Holding";prep.remaining=Config.p5.braceSeconds;}
                    else{order.stage="Finished";prep.remaining=Config.p5.opportunitySeconds;}
                }
                else
                {
                    prep.remaining=Down(prep.remaining,dt);if(prep.remaining<=0){prep.used=true;if(order.kind==BasicOrderKind.Prepare)order.stage="Finished";}
                }
            }
            foreach(var token in s.p5.opportunities)
            {
                if(token.consumed||token.p5.status!="Available")continue;token.remaining=Down(token.remaining,dt);
                if(token.p5.kind=="bound")
                {var condition=s.Actor(token.targetId)?.statuses.Find(x=>x.p5?.instanceId==token.p5.sourceConditionId);token.remaining=condition==null?0:Math.Min(token.remaining,condition.remaining);}
                if(!P5TokenActive(s,token)){token.remaining=0;token.p5.status="Expired";}
            }
        }
        void P5BasicRollModifiers(CombatState s,ActorState source,ActorState target,CombatCommand command,out bool advantage,out bool disadvantage,out int acAdjustment)
        {
            var context=source.basicAttackIntent?.p5;
            advantage=source.role==ActorRole.Rogue&&IsBehind(source,target)||target.staggerRemaining>0||source.statuses.Any(x=>x.id=="flanking"&&x.remaining>0);
            disadvantage=source.statuses.Any(x=>x.id=="suppressed"&&x.remaining>0);
            // Enemy attack evidence is classified by its actual committed runtime
            // pattern. BasicFor(enemy) is only a fallback melee template.
            var evidence=P5Evidence(command,target.id);acAdjustment=(source.isEnemy||Config.BasicFor(source).ranged?evidence?.coverAcBonus??0:0)+Math.Max(0,(evidence?.frontalShieldAcBonus??0)-(context?.ignoreShieldAc??0));
        }
    }
}
