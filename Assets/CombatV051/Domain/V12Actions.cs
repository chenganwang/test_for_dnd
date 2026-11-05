using System;
using System.Linq;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public float V12ChaseRange(string actorId)=>V12FormUp("v12_mira_hunt",actorId)?Config.v12.build.huntUpgradeRange:Config.v12?.chaseRange??0;
        public ActionQuote QuoteHero(CombatCommand c)
        {try{return V12Quote(State,c,true);}catch(Exception e){return new ActionQuote{legal=false,reason=e.Message};}}
        ActionQuote V12Quote(CombatState s,CombatCommand c,bool free)
        {
            Require(V12Enabled&&s.inCombat&&!s.encounterResolved,"v12_not_in_combat");var a=Live(s,c.actorId);var spec=V12Spec(c.actionId,Config.v12);
            if(c.actionId=="v12_mira_chase"&&spec!=null)spec.range=V12ChaseRange(a.id);
            Require(spec!=null&&Config.AllowsAction(a,c.actionId)&&(c.actionId==HeroBasic(a.id)||c.actionId==HeroKey(a.id)),"ability_locked_or_wrong_character");
            Require(!V12ActionActive(a)&&!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");
            bool waiting=a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved&&string.IsNullOrEmpty(a.heldBy)&&a.staggerRemaining<=0&&!P6BindActive(a);
            if(V13Enabled){Require(V13PartyAlive(s),"core_defeated");if(free)Require(Member(s,a.id).actionUntil<=s.simulationSeconds&&Member(s,a.id).inputUntil<=s.simulationSeconds,"actor_busy");}
            if(free)Require(P6MajorAvailable(a)||waiting&&c.actionId==HeroBasic(a.id),"major_unavailable");
            if(c.actionId==HeroBasic(a.id))Require(a.v12.basicCooldown<=0,"hero_basic_cooldown");
            ActorState target;var anchor=V13Anchor(s,a);
            if(c.actionId=="v13_finn_shield"||c.actionId=="v13_finn_prayer")
            {target=Live(s,c.targetId);Require(!target.isEnemy&&V13Physical(s,target)&&Distance(anchor,target)<=6&&c.lineOfSight,"physical_ally_required");if(c.actionId=="v13_finn_prayer")Require(Member(s,a.id).prayers>0,"prayer_empty");}
            else if(c.actionId=="v12_finn_grove")
            {Require(Finite(c.x)&&Finite(c.z)&&c.qualified&&c.lineOfSight&&DistanceTo(anchor,c.x,c.z)<=spec.range+.001f,"grove_destination_invalid");target=a;}
            else{target=Live(s,c.targetId);Require(target.isEnemy&&c.lineOfSight,"line_of_sight_blocked");Require(Distance(anchor,target)<=spec.range+.001f+(!V13Enabled&&c.actionId=="v12_mira_chase"?Config.BasicFor(a).range:0),"target_out_of_range");}
            if(c.actionId==V14DetonateId){Require(V14Enabled,"v14_rules_required");Require(V14RemainingTicks(V14OwnParasite(target,a),true,s.simulationSeconds)>0,"own_parasite_no_unpaid_ticks");Require(AvailableP6Charges(s)>0,"no_charge");}
            var q=new ActionQuote{legal=true,range=spec.range,rollType=spec.rollType,rollOwnerId=a.id,targetNumber=EffectiveArmorClass(target),bonus=a.attackBonus};
            if(spec.rollType=="Attack")
            {q.advantage=V13Enabled&&target.statuses.Any(x=>x.sourceId==V12Evan&&x.v12?.family=="opening"&&x.stacks>0&&x.remaining>0)||target.staggerRemaining>0||a.aimReady||c.actionId=="v12_evan_final"&&V12HasOpening(target);q.disadvantage=a.statuses.Any(x=>(x.id=="suppressed"||x.id=="attack-disadvantage")&&x.remaining>0);q.sources.Add(c.actionId=="v12_evan_final"&&V12HasOpening(target)?"破绽：定稿优势":"基础攻击加值");q.successProbability=Probability("Attack",q.bonus,q.targetNumber,q.advantage,q.disadvantage);}
            else{q.targetNumber=0;q.bonus=0;q.successProbability=1;q.sources.Add("合法施作，无额外d20");}return q;
        }
        bool TryV12Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(!V12Enabled)return false;
            if(TryV12EnvironmentCommand(s,c,out result))return true;
            if(c.kind==CommandKind.ResolveHeroTick){Require(c.authority==CommandAuthority.System&&SimulationMode(s),"system_command_required");result=V12ResolveTicks(s);return true;}
            if(c.kind==CommandKind.RequestHeroAction){result=V12Request(s,c);return true;}
            if(c.kind==CommandKind.ResolveHeroAction){result=V12ResolveIntent(s,c);return true;}
            if(c.kind==CommandKind.CancelBasicOrder&&V12ActionActive(s.Actor(c.actorId)))
            {Require(SimulationMode(s),"world_frozen");var a=s.Actor(c.actorId);Require(string.IsNullOrEmpty(c.orderId)||c.orderId==a.v12.intent.id,"basic_order_missing");a.v12.intent.stage="Canceled";a.v12.intent.reason="explicit_cancel";a.windupRemaining=0;result=new CombatResult{orderId=a.v12.intent.id};return true;}
            if(c.kind==CommandKind.ConfirmRoll&&V12Action(c.actionId))
            {
                Require(c.actionId=="v12_evan_final"||c.actionId=="v12_mira_break","hero_no_d20_action");bool direct=V13Enabled&&c.p6?.preacceptBaseResult==true;Require((direct?NormalMode(s):s.mode==CombatMode.TacticalFocus)&&s.pendingRoll==null&&s.activePendingEvent==null,"focus_required");if(!direct)Require(P6CanStartInteractiveRoll(s),"focus_decision_time_insufficient");Require(AvailableP6Charges(s)>0,"no_charge");
                var q=V12Quote(s,c,true);var a=Live(s,c.actorId);var t=new RollTicket{id=Id(s,"v12-roll"),actorId=a.id,targetId=c.targetId,actionId=c.actionId,spec=V12Spec(c.actionId,Config.v12),rollType="Attack",rollOwnerId=a.id,targetNumber=q.targetNumber,baseBonus=q.bonus,modifierSources=q.sources,advantage=q.advantage,disadvantage=q.disadvantage,createdRulesVersion=s.rulesVersion,v12=new V12ActionContext()};
                a.heldBy=t.id;a.majorRemaining=0;a.p6.basicRecovery=false;a.p5.basicRecovery=false;a.basicOrder=null;a.aimReady=false;s.charges--;DrainMomentum(s);Roll(s,t,direct);s.rolls.Add(t);s.pendingRollId=t.id;s.mode=CombatMode.RollFreeze;result=TicketResult(t);result.chargeSpent=1;if(direct){t.stage="AwaitDecision";s.mode=CombatMode.PostRollDecision;CombatResult committed;TryV12Command(s,new CombatCommand{kind=CommandKind.Commit,ticketId=t.id},out committed);s.mode=CombatMode.QueuedExecution;}return true;
            }
            if(c.kind==CommandKind.Commit&&s.pendingRoll?.v12!=null)
            {Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");var a=Live(s,t.actorId);Live(s,t.targetId);Require(a.heldBy==t.id,"action_hold_lost");var q=new QueuedAction{id=Id(s,"v12-queue"),ticketId=t.id,actorId=t.actorId,targetId=t.targetId,actionId=t.actionId};s.queue.Add(q);t.queueId=q.id;t.stage="Queued";t.v12.startDeadline=s.simulationSeconds+1;s.pendingRollId=null;s.mode=CombatMode.TacticalFocus;result=TicketResult(t);return true;}
            if(c.kind!=CommandKind.StartQueued&&c.kind!=CommandKind.ResolveQueued)return false;
            var queued=s.queue.Find(q=>q.id==c.queueId);var ticket=queued==null?null:s.rolls.Find(t=>t.id==queued.ticketId);if(ticket?.v12==null)return false;
            var actor=s.Actor(ticket.actorId);var victim=s.Actor(ticket.targetId);
            if(c.kind==CommandKind.StartQueued)
            {
                Require(NormalMode(s)&&queued.stage=="Queued","queue_not_pending");
                if(s.simulationSeconds>=ticket.v12.startDeadline||actor?.Alive!=true||victim?.Alive!=true||!c.lineOfSight||Distance(actor,victim)>ticket.spec.range+.001f){CancelQueue(s,queued,ticket,"execution_position_illegal");result=new CombatResult{queueId=queued.id,reason=queued.reason};return true;}
                Require(actor.heldBy==ticket.id&&actor.staggerRemaining<=0&&actor.majorRemaining<=0,"held_action_unavailable");actor.majorRemaining=Config.majorRecovery;if(V13Enabled)Member(s,actor.id).actionUntil=s.simulationSeconds+Config.v13.actionSeconds;actor.windupRemaining=ticket.spec.windup;queued.windupRemaining=ticket.spec.windup;queued.resourcesSpent=true;queued.stage="Executing";queued.executionId=Id(s,"v12-execution");ticket.stage="Executing";result=new CombatResult{queueId=queued.id,executionId=queued.executionId};return true;
            }
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");Require(queued.stage=="Executing"&&!queued.effectApplied&&queued.windupRemaining<=0,"queue_not_executing");
            if(actor?.Alive!=true||victim?.Alive!=true||actor.staggerRemaining>0||!c.lineOfSight||Distance(actor,victim)>ticket.spec.range+.001f){CancelQueue(s,queued,ticket,"execution_target_moved_or_blocked");result=new CombatResult{queueId=queued.id,reason=queued.reason};return true;}
            ticket.targetNumber=EffectiveArmorClass(victim);Evaluate(s,ticket);result=TicketResult(ticket);result.executionId=queued.executionId;result.damageApplied=true;
            if(ticket.hit){float coefficient=ticket.actionId=="v12_evan_final"?V12FinalCoefficient(actor,victim):Config.v12.breakCoefficient;int amount=V12RollBase(s,actor,coefficient*(float)V14MiraDamageMultiplier(s,actor,ticket.actionId,true),ticket.critical);result.damage=V12Damage(s,actor,victim,ticket.id,ticket.actionId,"direct",amount,true,result);if(ticket.actionId=="v12_mira_break"&&result.damage>0){float prior=victim.breakValue;P5ApplyBreak(victim,Config.v12.breakDamage);AwardFocusBreak(s,victim,ticket.id,prior);V12Sweep(s,actor,victim,ticket.id,amount,result);}V14MiraConsume(s,actor,victim,ticket.id,ticket.actionId,result.damage,true);AwardP4ActionOutcome(s,victim,ticket.id,ticket.id,false,!victim.Alive);}
            V14CorrectionCommitted(s,ticket,result.damage>0);queued.effectApplied=true;queued.stage="Resolved";ticket.stage="Resolved";Release(s,ticket,false);actor.windupRemaining=0;return true;
        }
        CombatResult V12Request(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&s.mode!=CombatMode.PostRollDecision&&s.activePendingEvent==null,"hero_action_unavailable");Require(c.actionId==HeroBasic(c.actorId)||c.actionId=="v12_finn_grove"||c.actionId=="v13_finn_prayer"||c.actionId==V14DetonateId,"hero_requires_visible_roll");
            var a=Live(s,c.actorId);bool ai=c.authority==CommandAuthority.AllyAI;
            Require(c.authority==CommandAuthority.Player||ai,"player_order_required");if(ai)Require((c.actionId=="v12_evan_mark"||c.actionId=="v12_finn_seed")&&!HasPlayerOrder(a)&&a.id!=s.activeActorId,"ai_precious_resource_forbidden");
            V12Quote(s,c,true);var spec=V12Spec(c.actionId,Config.v12);
            if(c.actionId=="v12_mira_chase")Require(c.qualified&&Finite(c.x)&&Finite(c.z)&&DistanceTo(a,c.x,c.z)<=V12ChaseRange(a.id)+.001f&&DistanceTo(s.Actor(c.targetId),c.x,c.z)<=Config.BasicFor(a).range+.001f,"chase_destination_invalid");
            bool detonate=c.actionId==V14DetonateId;bool paid=c.actionId=="v12_finn_grove"||c.actionId=="v13_finn_prayer";if(paid){Require(AvailableP6Charges(s)>0,"no_charge");s.charges--;DrainMomentum(s);}
            if(c.actionId=="v13_finn_prayer"){var member=Member(s,a.id);member.prayers--;member.prayerSpent.Add(c.id);}
            if(V13Enabled)Member(s,a.id).actionUntil=s.simulationSeconds+Config.v13.actionSeconds;
            bool waiting=a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved;var i=new V12HeroIntent{id=c.id,actionId=c.actionId,targetId=c.targetId,stage=waiting?"WaitingBasic":c.actionId=="v12_mira_chase"?"Moving":"Casting",remaining=spec.windup,startX=a.x,startZ=a.z,destinationX=c.x,destinationZ=c.z,startingHp=a.hp,deadline=s.simulationSeconds+Config.v12.chaseTimeout};a.v12.intent=i;V14RememberHeroCommand(s,a,i,c.authority==CommandAuthority.Player);
            if(!paid&&!detonate)a.v12.basicCooldown=c.actionId=="v12_evan_mark"?Config.v12.markCooldown:c.actionId=="v12_mira_chase"?Config.v12.chaseCooldown:c.actionId=="v13_finn_shield"?Config.v13.shieldCooldown:Config.v12.seedCooldown;
            a.basicOrder=null;if(!waiting){a.majorRemaining=0;a.windupRemaining=spec.windup;}a.p6.basicRecovery=false;a.p5.basicRecovery=false;
            return new CombatResult{orderId=i.id,chargeSpent=paid?1:0};
        }
        CombatResult V12ResolveIntent(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=s.Actor(c.actorId);var i=a?.v12?.intent;Require(i!=null&&(i.stage=="Casting"||i.stage=="Moving")&&i.id==c.orderId&&i.remaining<=0,"hero_action_not_ready");
            if(i.actionId==V14DetonateId)return V14ResolveDetonation(s,c,a,i);
            var r=new CombatResult{orderId=i.id,damageApplied=true};var target=s.Actor(i.targetId);bool grove=i.actionId=="v12_finn_grove";bool support=i.actionId=="v13_finn_shield"||i.actionId=="v13_finn_prayer";bool assist=V13Enabled&&!V13Physical(s,a);
            bool legal=a.Alive&&a.staggerRemaining<=0&&c.lineOfSight&&(grove?DistanceTo(a,i.destinationX,i.destinationZ)<=Config.v12.groveRange+.001f:target?.Alive==true&&(support?!target.isEnemy&&V13Physical(s,target):target.isEnemy)&&(assist&&i.actionId=="v12_mira_chase"?DistanceTo(target,i.destinationX,i.destinationZ):Distance(a,target))<=(i.actionId=="v12_mira_chase"?Config.BasicFor(a).range:V12Spec(i.actionId,Config.v12).range)+.001f);
            if(i.actionId=="v12_mira_chase")legal&=c.qualified&&(assist||DistanceTo(a,i.destinationX,i.destinationZ)<=Config.orderArrivalDistance+.01f);
            if(V14Enabled&&assist&&i.actionId=="v12_mira_chase")legal&=target!=null&&Distance(V13Anchor(s,a),target)<=V12ChaseRange(a.id)+.001f;
            if(!legal){i.stage="Canceled";i.reason="hero_target_moved_or_blocked";a.windupRemaining=0;r.reason=i.reason;return r;}
            i.stage="Resolved";a.windupRemaining=0;a.majorRemaining=V13Enabled?Math.Max(0,Member(s,a.id).actionUntil-s.simulationSeconds):Config.p6.smiteRecovery;
            if(support){if(i.actionId=="v13_finn_shield")V11GrantShield(target,Math.Max(1,(int)Math.Floor(target.maxHp*(Config.v13.shieldFraction+(V12Form("v13_finn_shelter",a.id)?V12FormUp("v13_finn_shelter",a.id)?.04f:.02f:0)))),Config.v13.shieldSeconds);else{V13Heal(target,target.maxHp*(Config.v13.prayerFraction+(V12Form("v13_finn_prayer",a.id)?V12FormUp("v13_finn_prayer",a.id)?.08f:.04f:0)));target.statuses.RemoveAll(x=>x.id=="restrained"||x.id=="p6-root"||x.v12?.family=="entangle");}V12Emit(s,r,i.id,a.id,target.id,i.actionId,"heal",0);}
            else if(i.actionId=="v12_evan_mark")V12ApplyMark(s,a,target,i.id,r);
            else if(i.actionId=="v12_finn_seed"){V12ApplyParasite(s,a,target,i.id,Config.v12.parasiteSeconds,0,r);V14SeedExtra(s,a,target,i.id,r);}
            else if(grove)
            {s.v12.groves.RemoveAll(g=>g.sourceId==a.id);foreach(var e in s.enemies)e.statuses.RemoveAll(x=>x.v12?.family=="entangle"&&x.sourceId==a.id);var groveState=new V12Grove{id=i.id,sourceId=a.id,x=i.destinationX,z=i.destinationZ,radius=Config.v12.groveRadius,expiresAt=s.simulationSeconds+Config.v12.groveSeconds,nextTick=s.simulationSeconds+1};s.v12.groves.Add(groveState);V12Emit(s,r,i.id,a.id,a.id,i.actionId,"grove",0);V12Bloom(s,a,groveState,r);V12UpdateGroves(s,r);}
            else
            {
                if(V12Passive(a)&&!assist&&DistanceTo(a,i.startX,i.startZ)>=(V13Enabled?Config.v13.chaseRewardTravel:Config.v12.chaseMinimumTravel))a.v12.readinessRemaining=Config.v12.readinessSeconds;
                RollTicket chaseTicket=null;
                if(V13Enabled)
                {chaseTicket=new RollTicket{id=i.id,actorId=a.id,rollOwnerId=a.id,targetId=target.id,actionId=i.actionId,spec=V12Spec(i.actionId,Config.v12),rollType="Attack",baseBonus=a.attackBonus,targetNumber=EffectiveArmorClass(target),chargeSpent=0};Roll(s,chaseTicket,true);r.rawFaces.AddRange(chaseTicket.rawFaces);r.total=chaseTicket.total;r.hit=chaseTicket.hit;r.critical=chaseTicket.critical;}
                else{int raw=RandomStreams.D20(s.random.backgroundCombat);r.rawFaces.Add(raw);r.total=raw+a.attackBonus;r.hit=AttackHits(raw,r.total,EffectiveArmorClass(target));r.critical=r.hit&&raw==20;}
                if(r.hit)r.damage=V12Damage(s,a,target,i.id,i.actionId,"direct",V12RollBase(s,a,Config.v12.chaseCoefficient*(assist&&Member(s,a.id).assistRewardUntil>s.simulationSeconds?1.2f:1)*(float)V14MiraDamageMultiplier(s,a,i.actionId,V14MiraPlayerHero(a,i.id)),r.critical),true,r);
                V14CorrectionCommitted(s,chaseTicket,r.damage>0);
            }
            if(i.actionId=="v12_mira_chase")V14MiraConsume(s,a,target,i.id,i.actionId,r.damage,V14MiraPlayerHero(a,i.id));
            if(assist&&i.actionId=="v12_mira_chase"&&r.hit)Member(s,a.id).assistRewardUntil=0;
            return r;
        }
    }
}

