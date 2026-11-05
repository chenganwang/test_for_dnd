using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        ActionQuote V11Quote(CombatState s,CombatCommand c,bool free)
        {
            Require(Config.v11!=null&&s.inCombat&&!s.encounterResolved,"not_in_combat");var a=Live(s,c.actorId);var target=Live(s,c.targetId);
            Require(Config.AllowsAction(a,c.actionId),"ability_locked_or_wrong_character");var spec=V11AbilityCatalog.Action(c.actionId);Require(spec!=null,"unknown_action");
            bool self=V11AbilityCatalog.IsSelfTarget(c.actionId);Require(self?target.id==a.id:target.isEnemy,"invalid_target_team");
            if(free)Require(P6MajorAvailable(a),"major_unavailable");Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");
            Require(c.lineOfSight&&Distance(a,target)<=spec.range+.001f,"target_out_of_range");
            var q=new ActionQuote{legal=true,range=spec.range,rollType=spec.rollType,rollOwnerId=a.id,targetNumber=10};
            if(spec.rollType=="Attack"){q.targetNumber=EffectiveArmorClass(target);q.bonus=a.attackBonus;q.advantage=target.staggerRemaining>0||a.aimReady;q.disadvantage=a.statuses.Any(x=>x.id=="suppressed"&&x.remaining>0);}
            else if(spec.rollType=="Save"){q.rollOwnerId=target.id;q.targetNumber=8+a.wisdomModifier+a.proficiency;q.bonus=target.wisSaveBonus;q.disadvantage=target.staggerRemaining>0;q.advantage=target.statuses.Any(x=>x.id=="save-advantage"&&x.remaining>0);q.sources.Add("敌人WIS豁免；低于DC才封缚");}
            else{q.bonus=(c.actionId=="v11_support_ward"?a.intelligenceModifier:c.actionId=="v11_nature_mend"?a.wisdomModifier:a.charismaModifier)+a.proficiency;q.sources.Add("自身施法检定；达到DC10成功");}
            q.successProbability=Probability(q.rollType,q.bonus,q.targetNumber,q.advantage,q.disadvantage);return q;
        }
        bool TryV11Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(Config.v11==null)return false;
            if(c.kind==CommandKind.ConfirmRoll&&V11AbilityCatalog.IsCustom(c.actionId))
            {
                Require(s.mode==CombatMode.TacticalFocus&&s.pendingRoll==null&&s.activePendingEvent==null,"focus_required");Require(P6CanStartInteractiveRoll(s),"focus_decision_time_insufficient");Require(AvailableP6Charges(s)>0,"no_charge");
                var quote=V11Quote(s,c,true);var a=Live(s,c.actorId);var t=new RollTicket{id=Id(s,"v11-roll"),actorId=a.id,targetId=c.targetId,actionId=c.actionId,spec=V11AbilityCatalog.Action(c.actionId),rollType=quote.rollType,rollOwnerId=quote.rollOwnerId,targetNumber=quote.targetNumber,baseBonus=quote.bonus,modifierSources=quote.sources,advantage=quote.advantage,disadvantage=quote.disadvantage,createdRulesVersion=s.rulesVersion,v11=new V11ActionContext{customAction=true}};
                a.heldBy=t.id;a.majorRemaining=0;if(a.p6!=null)a.p6.basicRecovery=false;if(a.p5!=null)a.p5.basicRecovery=false;a.basicOrder=null;if(t.rollType=="Attack")a.aimReady=false;
                s.charges--;DrainMomentum(s);Roll(s,t);s.rolls.Add(t);s.pendingRollId=t.id;s.mode=CombatMode.RollFreeze;result=TicketResult(t);result.chargeSpent=1;return true;
            }
            if(c.kind==CommandKind.Commit&&s.pendingRoll?.v11?.customAction==true)
            {
                Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");var a=Live(s,t.actorId);Live(s,t.targetId);Require(a.heldBy==t.id,"action_hold_lost");
                var q=new QueuedAction{id=Id(s,"v11-queue"),ticketId=t.id,actorId=t.actorId,targetId=t.targetId,actionId=t.actionId};s.queue.Add(q);t.queueId=q.id;t.stage="Queued";t.v11.startDeadline=s.simulationSeconds+1;s.pendingRollId=null;s.mode=CombatMode.TacticalFocus;result=TicketResult(t);return true;
            }
            if(c.kind!=CommandKind.StartQueued&&c.kind!=CommandKind.ResolveQueued)return false;
            var queued=s.queue.Find(x=>x.id==c.queueId);var ticket=queued==null?null:s.rolls.Find(x=>x.id==queued.ticketId);if(ticket?.v11?.customAction!=true)return false;
            if(c.kind==CommandKind.StartQueued){result=V11Start(s,c,queued,ticket);return true;}
            result=V11Resolve(s,c,queued,ticket);return true;
        }
        CombatResult V11Start(CombatState s,CombatCommand c,QueuedAction q,RollTicket t)
        {
            Require(NormalMode(s)&&q.stage=="Queued","queue_not_pending");var a=s.Actor(t.actorId);var target=s.Actor(t.targetId);
            string invalid=s.simulationSeconds>=t.v11.startDeadline?"action_start_expired":a?.Alive!=true||target?.Alive!=true?"target_or_actor_unavailable":!c.lineOfSight||Distance(a,target)>t.spec.range+.001f?"execution_position_illegal":null;
            if(invalid!=null){CancelQueue(s,q,t,invalid);return new CombatResult{queueId=q.id,reason=invalid};}
            Require(a.heldBy==t.id&&a.majorRemaining<=0&&a.staggerRemaining<=0&&!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"held_action_unavailable");
            a.majorRemaining=Config.majorRecovery;a.windupRemaining=t.spec.windup;q.windupRemaining=t.spec.windup;q.resourcesSpent=true;q.stage="Executing";q.executionId=Id(s,"v11-execution");t.stage="Executing";return new CombatResult{queueId=q.id,executionId=q.executionId};
        }
        CombatResult V11Resolve(CombatState s,CombatCommand c,QueuedAction q,RollTicket t)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");Require(q.stage=="Executing"&&!q.effectApplied&&q.windupRemaining<=0,"queue_not_executing");var a=s.Actor(q.actorId);var target=s.Actor(q.targetId);
            if(a?.Alive!=true||target?.Alive!=true||a.staggerRemaining>0||!c.lineOfSight||Distance(a,target)>t.spec.range+.001f){CancelQueue(s,q,t,"execution_target_moved_or_blocked");return new CombatResult{queueId=q.id,reason=q.reason};}
            string beforeEffect=V14Enabled&&t.v14Correction!=null?CombatJson.Write(s.actors.Concat(s.enemies).Select(x=>new{x.id,x.hp,shield=x.p6?.absorptionShield,status=x.statuses}).ToArray()):null;
            var r=TicketResult(t);r.executionId=q.executionId;r.v11=new V11ActionResult();
            if(t.hit)
            {
                switch(t.actionId)
                {
                    case "v11_quick_shot":
                        int damage=V12Enabled?V12Damage(s,a,target,t.id,t.actionId,"direct",t.spec.damage*(t.critical?2:1),true,r):Math.Min(target.hp,t.spec.damage*(t.critical?2:1));if(!V12Enabled)target.hp-=damage;r.damage=damage;r.damageApplied=true;if(damage>0){BreakConcentrationFromDamage(s,target);AwardFocusBossPhaseIfReached(s,target,t.id);}r.v11.effects.Add(new V11EffectResult{actorId=a.id,targetId=target.id,kind="damage",amount=damage});break;
                    case "v11_nature_bind":
                        s.world.strings.TryGetValue("enemyTemplate/"+target.id,out var template);bool boss=template=="boss",elite=template=="elite";float duration=boss?Config.p6.bossSlowSeconds:elite?Config.p6.rootEliteSeconds:Config.p6.rootNormalSeconds;string condition=boss?"p6-slow":"p6-root";
                        V11Status(target,condition,a.id,duration);r.v11.effects.Add(new V11EffectResult{actorId=a.id,targetId=target.id,kind=condition,seconds=duration});break;
                    case "v11_nature_mend":
                        foreach(var member in s.actors.Where(x=>x.Alive)){int healed=V12HealExisting(s,Config,member,4);r.v11.effects.Add(new V11EffectResult{actorId=a.id,targetId=member.id,kind="heal",amount=healed});}break;
                    case "v11_support_ward":case "v11_oath_aegis":
                        foreach(var member in s.actors.Where(x=>x.Alive&&(t.actionId=="v11_support_ward"||x.id==a.id))){int amount=t.actionId=="v11_support_ward"?3:6;V11GrantShield(member,amount,6);r.v11.effects.Add(new V11EffectResult{actorId=a.id,targetId=member.id,kind="absorption",amount=amount,seconds=6});}break;
                }
            }
            V14CorrectionCommitted(s,t,t.hit&&beforeEffect!=null&&beforeEffect!=CombatJson.Write(s.actors.Concat(s.enemies).Select(x=>new{x.id,x.hp,shield=x.p6?.absorptionShield,status=x.statuses}).ToArray()));
            q.effectApplied=true;q.stage="Resolved";t.stage="Resolved";Release(s,t,false);a.windupRemaining=0;return r;
        }
    }
}
