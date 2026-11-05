using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        bool TryP6Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(Config.p6==null)return false;
            if(TryP6TrainingCommand(s,c,out result))return true;
            switch(c.kind)
            {
                case CommandKind.RequestBindRadiance:result=P6RequestBind(s,c);return true;
                case CommandKind.ResolveBindRadiance:result=P6ResolveBind(s,c);return true;
                case CommandKind.SelectSmiteOvercharge:result=P6SelectOvercharge(s,c);return true;
                case CommandKind.CancelBasicOrder:
                    var binder=s.Actor(c.actorId);if(P6BindActive(binder)){Require(SimulationMode(s),"world_frozen");Require(string.IsNullOrEmpty(c.orderId)||c.orderId==binder.p6.bindIntent.id,"basic_order_missing");bool casting=binder.p6.bindIntent.stage=="Casting";binder.p6.bindIntent.stage="Canceled";binder.p6.bindIntent.reason="explicit_cancel";if(casting)binder.windupRemaining=0;result=new CombatResult{orderId=binder.p6.bindIntent.id};return true;}return false;
                case CommandKind.ConfirmRoll:if(c.actionId==P6SmiteId){result=P6ConfirmSmite(s,c);return true;}Require(AvailableP6Charges(s)>0,"no_charge");return false;
                case CommandKind.RevealRoll:
                    if(s.pendingRoll?.p6!=null){var t=Pending(s,c,"Rolling");Require(s.mode==CombatMode.RollFreeze,"roll_freeze_required");if(t.p6.preacceptBaseResult){result=P6CommitSmite(s,t,true);result.p6.directExecution=true;}else{t.stage="AwaitDecision";s.mode=CombatMode.PostRollDecision;result=TicketResult(t);}return true;}return false;
                case CommandKind.Commit:if(s.pendingRoll?.p6!=null){Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");result=P6CommitSmite(s,Pending(s,c,"AwaitDecision"),false);return true;}return false;
                case CommandKind.StartQueued:
                    var start=s.queue.Find(x=>x.id==c.queueId);if(start!=null&&s.rolls.Find(t=>t.id==start.ticketId)?.p6!=null){result=P6StartSmite(s,c,start);return true;}return false;
                case CommandKind.ResolveQueued:
                    var finish=s.queue.Find(x=>x.id==c.queueId);if(finish!=null&&s.rolls.Find(t=>t.id==finish.ticketId)?.p6!=null){result=P6ResolveSmite(s,c,finish);return true;}return false;
                case CommandKind.Reroll:case CommandKind.Modify:
                    if(s.pendingRoll?.p6!=null){Require(!s.pendingRoll.p6.preacceptBaseResult,"direct_result_preaccepted");s.pendingRoll.p6.overchargeHeld=false;s.pendingRoll.p6.overchargeSelected=false;}return false;
                case CommandKind.StartBasicAttack:if(s.Actor(c.actorId)?.role==ActorRole.Paladin){result=P6StartBasic(s,c);return true;}return false;
                case CommandKind.ResolveBasicAttack:if(s.Actor(c.actorId)?.basicAttackIntent?.p6!=null){result=P6ResolveBasic(s,c);return true;}return false;
                case CommandKind.BasicAttack:
                    if(s.Actor(c.actorId)?.role==ActorRole.Paladin){P6StartBasic(s,c);s.Actor(c.actorId).basicAttackIntent.remaining=0;result=P6ResolveBasic(s,new CombatCommand{id=c.id+"/contact",kind=CommandKind.ResolveBasicAttack,actorId=c.actorId,orderId=c.id,authority=CommandAuthority.System,lineOfSight=c.lineOfSight,p6=c.p6});return true;}return false;
            }
            return false;
        }
        CombatResult P6ConfirmSmite(CombatState s,CombatCommand c)
        {
            bool direct=c.p6?.preacceptBaseResult==true;
            Require(s.pendingRoll==null&&s.activePendingEvent==null,"finish_pending_roll_first");
            Require(direct?NormalMode(s):s.mode==CombatMode.TacticalFocus,"focus_required");
            if(!direct)Require(P6CanStartInteractiveRoll(s),"focus_decision_time_insufficient");
            Require(c.p6?.acceptBaseOnTimeout!=false,"smite_base_authorization_required");Require(AvailableP6Charges(s)>0,"no_charge");
            var quote=P6QuoteSmite(s,c,true);var a=Live(s,c.actorId);var target=Live(s,c.targetId);var mark=P6Mark(s,target);string id=Id(s,"smite-roll");
            var t=new RollTicket{id=id,actorId=a.id,targetId=target.id,actionId=P6SmiteId,spec=Config.p6.SmiteSpec(),rollType="Attack",rollOwnerId=a.id,advantage=quote.advantage,disadvantage=quote.disadvantage,modifierSources=quote.sources,targetNumber=quote.targetNumber,baseBonus=quote.bonus,createdRulesVersion=s.rulesVersion,p6=new P6SmiteContext{markId=mark?.p6.id,preacceptBaseResult=direct,criticalMinimumRaw=Config.p6.smiteCriticalRaw}};
            if(Config.v11!=null)t.v11=new V11ActionContext{guardRadiantDice=V11ConfirmGuard(a)};
            a.heldBy=id;a.majorRemaining=0;a.p6.basicRecovery=false;if(a.p5!=null)a.p5.basicRecovery=false;a.basicOrder=null;
            s.charges--;DrainMomentum(s);Roll(s,t);s.rolls.Add(t);s.pendingRollId=id;s.mode=CombatMode.RollFreeze;
            var result=TicketResult(t);result.chargeSpent=1;result.p6=new P6ActionResult{directExecution=direct};return result;
        }
        CombatResult P6SelectOvercharge(CombatState s,CombatCommand c)
        {
            Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");Require(t.p6!=null&&!t.p6.preacceptBaseResult,"smite_overcharge_unavailable");
            if(c.flag){Require(t.hit,"overcharge_requires_hit");Require(t.p6.overchargeHeld||AvailableP6Charges(s)>0,"no_charge");t.p6.overchargeHeld=true;t.p6.overchargeSelected=true;}
            else{t.p6.overchargeHeld=false;t.p6.overchargeSelected=false;}
            var r=TicketResult(t);r.p6=new P6ActionResult{overchargeHeld=t.p6.overchargeHeld};return r;
        }
        CombatResult P6CommitSmite(CombatState s,RollTicket t,bool automatic)
        {
            Require(t.p6!=null&&(t.stage=="AwaitDecision"||automatic&&t.stage=="Rolling"),"ticket_stage_mismatch");
            if(automatic){t.p6.overchargeHeld=false;t.p6.overchargeSelected=false;}
            var a=s.Actor(t.actorId);var target=s.Actor(t.targetId);
            if(a?.Alive!=true||target?.Alive!=true){t.stage="Canceled";Release(s,t,true);s.pendingRollId=null;LeaveFocusBudget(s);s.mode=CombatMode.RealtimeCombat;var canceled=TicketResult(t);canceled.reason="target_or_actor_unavailable";canceled.p6=new P6ActionResult();return canceled;}
            Require(a.heldBy==t.id,"action_hold_lost");
            var q=new QueuedAction{id=Id(s,"smite-queue"),ticketId=t.id,actorId=t.actorId,targetId=t.targetId,actionId=t.actionId};
            t.p6.committedAt=s.simulationSeconds;t.p6.startDeadline=s.simulationSeconds+Config.p6.queuedStartSeconds;s.queue.Add(q);t.queueId=q.id;t.stage="Queued";s.pendingRollId=null;
            if(automatic){LeaveFocusBudget(s);s.mode=CombatMode.QueuedExecution;}else s.mode=CombatMode.TacticalFocus;
            var result=TicketResult(t);result.p6=new P6ActionResult{overchargeHeld=t.p6.overchargeHeld,directExecution=automatic};return result;
        }
        bool AutoCommitP6SmiteOnTimeout(CombatState s)
        {var t=s.pendingRoll;if(t?.p6==null||t.stage!="AwaitDecision")return false;P6CommitSmite(s,t,true);return true;}
        CombatResult P6StartSmite(CombatState s,CombatCommand c,QueuedAction q)
        {
            Require(NormalMode(s),"realtime_execution_required");Require(q.stage=="Queued","queue_not_pending");var t=s.rolls.Find(x=>x.id==q.ticketId);var a=s.Actor(t.actorId);var target=s.Actor(t.targetId);
            string invalid=s.simulationSeconds>=t.p6.startDeadline?"action_start_expired":a?.Alive!=true||target?.Alive!=true?"target_or_actor_unavailable":!P6Line(c,target.id)||Distance(a,target)>t.spec.range+.001f?"execution_position_illegal":null;
            if(invalid!=null){CancelQueue(s,q,t,invalid);return new CombatResult{queueId=q.id,reason=invalid};}
            Require(a.heldBy==t.id&&a.staggerRemaining<=0&&a.majorRemaining<=0,"held_action_unavailable");Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"actor_busy");
            if(t.p6.overchargeSelected){Require(t.p6.overchargeHeld&&s.charges>0,"held_resource_lost");s.charges--;t.p6.overchargeHeld=false;t.p6.overchargePaid=true;t.paidResourceIds.Add("shared-charge-overcharge/"+t.id);DrainMomentum(s);}
            if(V13Enabled)Member(s,a.id).actionUntil=s.simulationSeconds+Config.v13.actionSeconds;a.majorRemaining=Config.p6.smiteWindup+Config.p6.smiteRecovery;a.windupRemaining=Config.p6.smiteWindup;q.windupRemaining=Config.p6.smiteWindup;q.resourcesSpent=true;q.stage="Executing";q.executionId=Id(s,"smite-execution");t.stage="Executing";
            return new CombatResult{queueId=q.id,executionId=q.executionId,chargeSpent=t.p6.overchargePaid?1:0,p6=new P6ActionResult{overchargePaid=t.p6.overchargePaid}};
        }
        CombatResult P6ResolveSmite(CombatState s,CombatCommand c,QueuedAction q)
        {
            Require(SimulationMode(s),"world_frozen");Require(q.stage=="Executing"&&!q.effectApplied&&q.windupRemaining<=0,"queue_not_executing");var t=s.rolls.Find(x=>x.id==q.ticketId);var a=s.Actor(t.actorId);var target=s.Actor(t.targetId);
            if(a?.Alive!=true||target?.Alive!=true||!P6Line(c,target.id)||Distance(a,target)>t.spec.range+.001f){CancelQueue(s,q,t,"execution_target_moved_or_blocked");return new CombatResult{queueId=q.id,reason=q.reason};}
            t.targetNumber=EffectiveArmorClass(target);Evaluate(s,t);t.p6.executed=true;
            bool blessing=t.hit&&t.p6.overchargePaid&&P6IsTraining(s)&&s.p6.trainingBlessingEnabled&&!s.p6.trainingBlessingUsed;
            if(blessing){t.critical=true;t.p6.criticalSource="训练祝福";s.p6.trainingBlessingUsed=true;}
            var mark=P6Mark(s,target,t.p6.markId);if(t.p6.markId==null)mark=null;
            int radiant=(V11Core(a)?V11Buff("core_smite_dice")+(t.p6.overchargePaid?V11Buff("core_full_dice"):0):0)+(t.v11?.guardRadiantDice??0)+Config.p6.smiteRadiantDice+(t.p6.overchargePaid?Config.p6.overchargeRadiantDice:0)+(P6IsTraining(s)&&s.p6.trainingUpgradeId=="practice_radiant"?2:0);
            var damage=P6ApplyDamage(s,a,target,t.id,P6SmiteId,t.rawFaces,t.selectedFace,t.total,t.targetNumber,t.hit,t.critical,t.p6.criticalSource,radiant,mark,t.p6.overchargePaid);
            if(Config.v11!=null)damage.v11=new V11DamageBreakdown{radiantDiceBonus=V11Core(a)?V11Buff("core_smite_dice"):0,fullDiceBonus=V11Core(a)&&t.p6.overchargePaid?V11Buff("core_full_dice"):0,guardDiceBonus=t.v11?.guardRadiantDice??0,smiteAttackBonus=V11Core(a)?V11Buff("core_smite_accuracy"):0,buffIds=Config.v11.buffs.Select(x=>x.id).ToList()};
            t.p6.damage=damage;
            if(!t.hit&&!t.p6.missCompensated){t.p6.missCompensated=true;P6AwardResource(s,t.id,"executed-miss",Config.p6.missMomentum);}
            q.effectApplied=true;q.stage="Resolved";t.stage="Resolved";Release(s,t,false);a.windupRemaining=0;a.majorRemaining=Config.p6.smiteRecovery;
            var result=TicketResult(t);result.damage=damage.totalDamage;result.damageApplied=true;result.executionId=q.executionId;result.p6=new P6ActionResult{overchargePaid=t.p6.overchargePaid,damages=new List<P6DamageResult>{damage}};return result;
        }
        void P6CheckBind(CombatState s,ActorState a,ActorState target,bool free)
        {
            Require(Config.p6!=null&&s.inCombat&&!s.encounterResolved&&a.role==ActorRole.Mage&&!a.isEnemy&&target.isEnemy&&(Config.v11==null||Config.AllowsAction(a,P6BindId)),"bind_target_invalid");
            bool waitingBasic=a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved&&string.IsNullOrEmpty(a.heldBy)&&a.staggerRemaining<=0&&!P6BindActive(a);
            if(free)Require(P6MajorAvailable(a)||waitingBasic,"major_unavailable");Require(a.p6.bindCooldown<=0,"bind_cooldown");Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"actor_busy");Require(Distance(a,target)<=Config.p6.bindRange+.001f,"target_out_of_range");
        }
        CombatResult P6RequestBind(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&s.mode!=CombatMode.PostRollDecision&&s.activePendingEvent==null,"bind_unavailable");Require(c.authority==CommandAuthority.Player,"player_order_required");var a=Live(s,c.actorId);var target=Live(s,c.targetId);P6CheckBind(s,a,target,true);Require(P6Line(c,target.id),"line_of_sight_blocked");
            if(V13Enabled)Member(s,a.id).actionUntil=s.simulationSeconds+Config.v13.actionSeconds;bool wait=a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved;
            if(!wait){a.majorRemaining=Config.p6.bindWindup+Config.p6.smiteRecovery;a.windupRemaining=Config.p6.bindWindup;}a.p6.basicRecovery=false;if(a.p5!=null)a.p5.basicRecovery=false;a.basicOrder=null;
            a.p6.bindCooldown=P6IsTraining(s)&&s.p6.trainingUpgradeId=="practice_bind"?12:Config.p6.bindCooldown;a.p6.bindIntent=new P6BindIntent{id=c.id,targetId=target.id,stage=wait?"WaitingBasic":"Casting",remaining=Config.p6.bindWindup,startingHp=a.hp};
            return new CombatResult{orderId=c.id,p6=new P6ActionResult{bindIntentId=c.id}};
        }
        CombatResult P6ResolveBind(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=s.Actor(c.actorId);var b=a?.p6?.bindIntent;Require(b!=null&&b.stage=="Casting"&&b.id==(c.orderId??c.rootId)&&b.remaining<=0,"bind_not_ready");var target=s.Actor(b.targetId);
            if(!a.Alive||a.staggerRemaining>0||target?.Alive!=true||!P6Line(c,target.id)||Distance(a,target)>Config.p6.bindRange+.001f){b.stage="Canceled";b.reason="bind_target_invalid";a.windupRemaining=0;return new CombatResult{reason=b.reason,orderId=b.id};}
            foreach(var other in s.enemies)other.statuses.RemoveAll(x=>x.p6?.casterId==a.id);
            string markId=b.id+"/radiant-mark";target.statuses.Add(new StatusState{id="p6-radiant-mark",sourceId=a.id,remaining=Config.p6.markSeconds,p6=new P6RadiantMark{id=markId,casterId=a.id,rootId=b.id,multiplier=Math.Min(1.75f,Config.p6.radiantMultiplier+V11BuffCatalog.Value(Config.v11,"core_radiant_resonance"))}});
            int raw=RandomStreams.D20(s.random.backgroundCombat),dc=8+a.intelligenceModifier+a.proficiency,total=raw+target.wisSaveBonus;
            var record=new P6BindResult{id=b.id,casterId=a.id,targetId=target.id,markId=markId,raw=raw,total=total,dc=dc,savePassed=total>=dc,markApplied=true};
            if(!record.savePassed){bool boss=s.world.strings.TryGetValue("enemyTemplate/"+target.id,out var template)&&template=="boss";bool elite=template=="elite"||s.momentumLedger.registeredHighThreats.Contains(target.id);record.controlKind=boss?"p6-slow":"p6-root";record.controlSeconds=boss?Config.p6.bossSlowSeconds:elite?Config.p6.rootEliteSeconds:Config.p6.rootNormalSeconds;target.statuses.RemoveAll(x=>x.id==record.controlKind);target.statuses.Add(new StatusState{id=record.controlKind,sourceId=a.id,remaining=record.controlSeconds});}
            s.p6.bindResults.Add(record);b.stage="Resolved";a.windupRemaining=0;a.majorRemaining=Config.p6.smiteRecovery;return new CombatResult{orderId=b.id,rawFaces=new List<int>{raw},total=total,hit=!record.savePassed,p6=new P6ActionResult{bindIntentId=b.id,bind=record},reason=record.savePassed?"bind_resisted_mark_applied":"bind_and_mark_applied"};
        }
        bool P6DeadlineWillCross(CombatState s,float realDelta)
        {
            if(Config.p6==null||!SimulationMode(s))return false;float after=s.simulationSeconds+realDelta*Scale;
            return s.rolls.Any(t=>t.p6!=null&&t.stage=="Queued"&&(after>=t.p6.startDeadline||s.Actor(t.actorId)?.Alive!=true||s.Actor(t.targetId)?.Alive!=true));
        }
        bool ExpireP6Tickets(CombatState s)
        {
            if(Config.p6==null)return false;bool changed=false;
            foreach(var t in s.rolls.Where(t=>t.p6!=null&&t.stage=="Queued"))if(s.simulationSeconds>=t.p6.startDeadline||s.Actor(t.actorId)?.Alive!=true||s.Actor(t.targetId)?.Alive!=true){CancelQueue(s,s.queue.Find(q=>q.ticketId==t.id),t,"action_start_expired");changed=true;}
            return changed;
        }
    }
}
