using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        bool TryP5Command(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(Config.p5==null)return false;
            if(c.p5!=null)
            {
                Require(Finite(c.p5.directionX)&&Finite(c.p5.directionZ),"invalid_p5_direction");
                if(c.p5.targets!=null)Require(c.p5.targets.All(x=>x!=null&&!string.IsNullOrEmpty(x.targetId)&&Finite(x.x)&&Finite(x.z)&&Finite(x.actualDistance)&&x.actualDistance>=0&&x.coverAcBonus>=0&&x.coverAcBonus<=2&&x.frontalShieldAcBonus>=0&&x.frontalShieldAcBonus<=2)&&c.p5.targets.Select(x=>x.targetId).Distinct().Count()==c.p5.targets.Count,"invalid_p5_geometry");
            }
            switch(c.kind)
            {
                case CommandKind.StartPreparation:result=P5Prepare(s,c);return true;
                case CommandKind.IssueBasicOrder:
                    if(c.actionId=="Prepare"){result=P5Prepare(s,c);return true;}
                    if(c.actionId=="Followup"){result=P5FollowupOrder(s,c);return true;}return false;
                case CommandKind.CompleteBasicOrder:
                    if(s.Actor(c.actorId)?.basicOrder?.kind==BasicOrderKind.Prepare&&s.Actor(c.actorId).role==ActorRole.Rogue){result=P5FlankArrived(s,c);return true;}return false;
                case CommandKind.CancelBasicOrder:case CommandKind.CancelPreparation:
                    if(s.Actor(c.actorId)?.p5!=null)s.Actor(c.actorId).p5.preparation=null;return false;
                case CommandKind.StartBasicAttack:result=P5StartBasic(s,c);return true;
                case CommandKind.ResolveBasicAttack:result=P5ResolveBasic(s,c);return true;
                case CommandKind.BasicAttack:
                    // Historical one-step helper uses the same P5 contact reducer and never
                    // implicitly consumes an opportunity. Runtime always uses separate steps.
                    result=P5StartBasic(s,c);s.Actor(c.actorId).basicAttackIntent.remaining=0;
                    result=P5ResolveBasic(s,new CombatCommand{id=c.id+"/contact",kind=CommandKind.ResolveBasicAttack,actorId=c.actorId,orderId=c.id,authority=CommandAuthority.System,lineOfSight=c.lineOfSight,p5=c.p5});return true;
                case CommandKind.ConfirmRoll:result=P5Confirm(s,c);return true;
                case CommandKind.ResolveQueued:result=P5ResolveQueued(s,c);return true;
                case CommandKind.Commit:
                    if(s.pendingRoll?.purpose=="Tactical"&&s.pendingRoll.p5!=null&&P5TicketExpired(s,s.pendingRoll)){var ticket=s.pendingRoll;P5CancelTicket(s,ticket,"action_start_expired");result=TicketResult(ticket);result.reason="action_start_expired";return true;}return false;
                case CommandKind.StartQueued:
                    var queued=s.queue.Find(x=>x.id==c.queueId);var t=queued==null?null:s.rolls.Find(x=>x.id==queued.ticketId);
                    if(queued?.stage=="Queued"&&t?.p5!=null&&P5TicketExpired(s,t)){CancelQueue(s,queued,t,"action_start_expired");result=new CombatResult{queueId=queued.id,reason="action_start_expired"};return true;}return false;
                default:return false;
            }
        }
        CombatResult P5Prepare(CombatState s,CombatCommand c)
        {
            Require(s.inCombat&&SimulationMode(s)&&s.mode!=CombatMode.PostRollDecision&&s.mode!=CombatMode.ReactionPrompt,"basic_order_unavailable");Require(c.authority==CommandAuthority.Player&&!c.flag,"player_order_required");
            var a=Live(s,c.actorId);Require(!a.isEnemy,"invalid_party_actor");VoluntaryActionFree(a);Require(a.basicAttackIntent==null||a.basicAttackIntent.resolved,"basic_contact_pending");
            Require(!ActiveOrder(a)||c.replaceExisting,"basic_order_replacement_required");Require(a.preparationCooldown<=0&&!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"preparation_unavailable");
            var target=Live(s,string.IsNullOrEmpty(c.targetId)?a.id:c.targetId);
            float prepareSeconds=Config.p5.prepareSeconds*(a.role==ActorRole.Mage?Config.RulesFor(a.id).studySeconds:1);
            var order=new BasicOrder{id=c.id,targetId=target.id,kind=BasicOrderKind.Prepare,startX=a.x,startZ=a.z,x=c.x,z=c.z,startingHp=a.hp,stage="Preparing",remaining=prepareSeconds};
            var preparation=new P5PreparationState{sourceRoot=c.id,targetId=target.id,remaining=prepareSeconds};
            if(a.role==ActorRole.Fighter)
            {
                Require(!target.isEnemy&&P5Line(c,target.id)&&Distance(a,target)<=Config.RulesFor(a.id).protectionRange,"protection_target_out_of_range");
                order.protectTargetId=target.id;preparation.actionId="f_brace";
                var enemy=s.enemies.Where(e=>e.Alive).OrderBy(e=>Distance(a,e)).FirstOrDefault();float dx=c.p5?.directionX??0,dz=c.p5?.directionZ??0;
                if(dx*dx+dz*dz<.0001f){dx=enemy==null?0:enemy.x-a.x;dz=enemy==null?1:enemy.z-a.z;}float length=(float)Math.Sqrt(dx*dx+dz*dz);order.directionX=length<.001f?0:dx/length;order.directionZ=length<.001f?1:dz/length;
            }
            else
            {
                Require(target.isEnemy&&P5Line(c,target.id)&&Distance(a,target)<=(a.role==ActorRole.Rogue?5:7)+.001f,"preparation_target_invalid");
                if(a.role==ActorRole.Rogue){Require(c.qualified,"rear_destination_unreachable");Require(Finite(c.x)&&Finite(c.z),"invalid_order_destination");order.stage="Active";preparation.actionId="r_flank";preparation.remaining=Config.p5.flankAuthorizationSeconds*(Config.RulesFor(a.id).flankWindowSeconds/4f);}
                else{Require(a.role==ActorRole.Mage,"preparation_role_invalid");preparation.actionId="m_frost";}
            }
            a.guardRemaining=0;a.aimPreparing=false;a.aimRemaining=0;a.p5.preparation=preparation;a.preparationCooldown=Config.p5.prepareCooldown*(Config.preparationRepeatSeconds/6f);a.basicOrder=order;return new CombatResult{orderId=order.id};
        }
        CombatResult P5FollowupOrder(CombatState s,CombatCommand c)
        {
            Require(s.inCombat&&SimulationMode(s)&&s.mode!=CombatMode.PostRollDecision&&s.activePendingEvent==null,"basic_order_unavailable");Require(c.authority==CommandAuthority.Player&&!c.flag,"player_order_required");
            var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(!a.isEnemy&&target.isEnemy,"invalid_party_actor");Require(string.IsNullOrEmpty(a.heldBy)&&a.staggerRemaining<=0,"actor_busy");Require(!ActiveOrder(a)||c.replaceExisting,"basic_order_replacement_required");
            Require(P5SelectedToken(s,a.id,target.id,"basic_followup",c.p5?.opportunityId)!=null,"opportunity_required");
            a.basicOrder=new BasicOrder{id=c.id,kind=BasicOrderKind.Followup,targetId=target.id,p5=CombatJson.Clone(c.p5),startingHp=a.hp};return new CombatResult{orderId=c.id};
        }
        CombatResult P5FlankArrived(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=Live(s,c.actorId);var o=a.basicOrder;
            Require(o!=null&&o.id==c.orderId&&ActiveOrder(a)&&a.p5.preparation!=null,"basic_order_missing");var target=Live(s,o.targetId);
            Require(DistanceTo(a,o.x,o.z)<=Config.orderArrivalDistance+.001f&&P5Line(c,target.id)&&IsBehind(a,target),"sneak_position_required");
            if(target.aiState!=1||string.IsNullOrEmpty(target.attackGroupId)){o.stage="WaitingWindow";return new CombatResult{orderId=o.id,reason="waiting_facing_commitment"};}
            a.p5.preparation.ready=true;a.p5.preparation.threatId=target.attackGroupId;a.p5.preparation.remaining=Config.p5.flankAuthorizationSeconds*(Config.RulesFor(a.id).flankWindowSeconds/4f);o.kind=BasicOrderKind.Hold;o.stage="Holding";return new CombatResult{orderId=o.id,reason="flank_attack_armed"};
        }
        CombatResult P5StartBasic(CombatState s,CombatCommand c)
        {
            bool followup=c.actionId=="basic_followup";
            Require(s.inCombat&&SimulationMode(s)&&(IsAI(c)||NormalMode(s)||followup&&c.authority==CommandAuthority.System),"basic_attack_not_allowed");
            var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(!a.isEnemy||c.authority!=CommandAuthority.Player,"invalid_party_actor");MajorFree(a);
            Require(followup||!ActiveOrder(a)||a.basicOrder.kind!=BasicOrderKind.Followup,"followup_order_pending");
            Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");var basic=Config.BasicFor(a);
            Require(a.isEnemy!=target.isEnemy&&P5Line(c,target.id)&&Distance(a,target)<=basic.range+.001f,"target_out_of_range");
            P5ActionContext context;
            if(followup)
            {
                Require(!IsAI(c),"ai_opportunity_forbidden");
                if(c.authority==CommandAuthority.System)
                {
                    var o=a.basicOrder;Require(o!=null&&o.kind==BasicOrderKind.Followup&&o.id==c.orderId&&o.targetId==target.id&&ActiveOrder(a)&&o.p5?.opportunityId==c.p5?.opportunityId,"followup_order_authorization_missing");
                }
                else Require(c.authority==CommandAuthority.Player,"player_order_required");
                Require(!string.IsNullOrEmpty(c.p5?.opportunityId),"opportunity_required");context=P5Context(s,c,c.id,false);P5Consume(s,context,a.id);
                if(a.basicOrder?.kind==BasicOrderKind.Followup){a.basicOrder.stage="Finished";a.basicOrder.reason="followup_started";}
            }
            else{Require(string.IsNullOrEmpty(c.p5?.opportunityId),"explicit_followup_required");context=P5Context(s,new CombatCommand{actorId=a.id,targetId=target.id,actionId=basic.id,p5=c.p5},c.id,false);}
            a.basicAttackIntent=new BasicAttackIntent{id=c.id,targetId=target.id,actionId=followup?"basic_followup":basic.id,rootId=c.id,remaining=Config.basicContactSeconds,p5=context,v14PlayerCommand=c.authority==CommandAuthority.Player};a.windupRemaining=Config.basicContactSeconds;
            return new CombatResult{basicAttackId=c.id};
        }
        CombatResult P5ResolveBasic(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=Live(s,c.actorId);var intent=a.basicAttackIntent;
            Require(intent!=null&&!intent.resolved&&intent.id==c.orderId&&intent.remaining<=0,"basic_contact_not_ready");intent.resolved=true;a.windupRemaining=0;a.majorRemaining=Config.majorRecovery;a.p5.basicRecovery=true;
            var target=s.Actor(intent.targetId);var basic=Config.BasicFor(a);var result=new CombatResult{basicAttackId=intent.id,p5Effects=new List<P5EffectResult>()};
            if(target==null||!target.Alive||!P5Line(c,target.id)||Distance(a,target)>basic.range+.001f){result.reason="basic_swing_missed";return result;}
            var prep=a.p5.preparation;bool frost=intent.actionId!="basic_followup"&&prep!=null&&prep.actionId=="m_frost"&&prep.ready&&!prep.used&&prep.remaining>0;
            bool flank=intent.actionId!="basic_followup"&&prep!=null&&prep.actionId=="r_flank"&&prep.ready&&!prep.used&&prep.remaining>0&&prep.targetId==target.id&&prep.threatId==target.attackGroupId&&target.aiState==1&&IsBehind(a,target);
            if(frost||flank)prep.used=true;
            int damage=frost?RandomStreams.NextInt(s.random.backgroundCombat,1,9):basic.damage;if(intent.actionId==basic.id)damage=V12BasicPreparedDamage(s,a,damage);
            // P5 background attack uses the existing incoming/reaction transaction. A locked
            // context is passed, never re-selecting a live opportunity at contact time.
            var incoming=new CombatCommand{id=intent.id,kind=CommandKind.IncomingAttack,actorId=a.id,targetId=target.id,amount=damage,rootId=intent.rootId,qualified=true,parryable=!basic.ranged,authority=CommandAuthority.System,p5=c.p5};
            int hpBefore=target.hp;result=Incoming(s,incoming);result.basicAttackId=intent.id;result.p5Effects=new List<P5EffectResult>();
            V11OnBasicHpDamage(s,a,target,intent,basic.id,hpBefore-target.hp,result);V12ConsumePreparedBasic(s,a,target,intent,hpBefore-target.hp);
            bool actual=result.damage>0;
            if(actual)
            {
                if(frost&&target.Alive)
                {
                    string condition=intent.rootId+"/frost";target.statuses.Add(new StatusState{id="p5-slow",sourceId=a.id,remaining=Config.p5.frostSeconds,p5=new P5ConditionData{instanceId=condition,sourceActionId=intent.rootId,moveMultiplier=Config.p5.frostMoveMultiplier}});
                    P5Produce(s,"bound",a,target,intent.rootId,condition);
                }
                else if(flank)P5Produce(s,"exposed",a,target,intent.rootId);
                if(intent.actionId=="basic_followup")
                {
                    float prior=target.breakValue;
                    if(intent.p5.bonusBreak>0)P5ApplyBreak(target,intent.p5.bonusBreak);
                    bool moved=intent.p5.pushDistance>0&&target.Alive&&P5Displace(s,a,target,intent.p5,c,result);
                    bool used=intent.p5.opportunityKind=="bound"?moved:intent.p5.opportunityKind=="exposed"?true:prior>target.breakValue;
                    P5Reward(s,intent.p5,used);
                    // The free-followup increment does not mint Focus merely by
                    // crossing Break; independent elite/phase outcomes still qualify.
                }
                AwardP4ActionOutcome(s,target,intent.rootId,null,false,!target.Alive);AwardP4(s,"basic-hit",intent.rootId,"basic-attack",true);
            }
            result.p5Effects.Add(new P5EffectResult{targetId=target.id,root=intent.rootId,kind=frost?"frost":intent.actionId,damage=result.damage,effective=actual});return result;
        }
        CombatResult P5Confirm(CombatState s,CombatCommand c)
        {
            Require(s.mode==CombatMode.TacticalFocus&&s.pendingRoll==null,"focus_required");Require(s.focusBudget.secondsRemaining>0,"focus_budget_empty");Require(AvailableP6Charges(s)>0,"no_charge");
            var quote=P5Quote(s,c,true);var actor=Live(s,c.actorId);string id=Id(s,"roll");var context=P5Context(s,c,id,true);
            var ticket=new RollTicket{id=id,actorId=actor.id,targetId=c.targetId,actionId=c.actionId,spec=CombatJson.Clone(Config.Action(c.actionId)),rollType=quote.rollType,rollOwnerId=quote.rollOwnerId,advantage=quote.advantage,disadvantage=quote.disadvantage,modifierSources=quote.sources,targetNumber=quote.targetNumber,baseBonus=quote.bonus,createdRulesVersion=s.rulesVersion,p5=context};
            actor.heldBy=id;actor.heldSpellSlots+=ticket.spec.spellCost;actor.heldItems+=ticket.spec.itemCost;
            if(actor.p5.basicRecovery){actor.majorRemaining=0;actor.p5.basicRecovery=false;}
            if(ticket.spec.ranged&&ticket.rollType=="Attack")actor.aimReady=false;
            P5Consume(s,context,actor.id);s.charges--;DrainMomentum(s);Roll(s,ticket);P5PrepareDamage(s,ticket);
            if(c.actionId=="control")P5RollThunderTargets(s,ticket,c);
            s.rolls.Add(ticket);s.pendingRollId=id;s.mode=CombatMode.RollFreeze;var result=TicketResult(ticket);result.chargeSpent=1;return result;
        }
        void P5PrepareDamage(CombatState s,RollTicket t)
        {
            if(t.actionId=="control"){for(int i=0;i<2;i++)t.p5.damageFaces.Add(RandomStreams.NextInt(s.random.backgroundCombat,1,9));}
            else if(t.actionId=="sneak"){for(int i=0;i<(2+t.p5.extraDamageDice)*2;i++)t.p5.damageFaces.Add(RandomStreams.NextInt(s.random.backgroundCombat,1,7));}
            P5RefreshDamage(t);
        }
        static void P5RefreshDamage(RollTicket t)
        {
            if(t.p5==null)return;
            if(t.actionId=="control")t.p5.damage=t.p5.damageFaces.Sum();
            else if(t.actionId=="sneak")t.p5.damage=t.spec.damage*(t.critical?2:1)+t.p5.damageFaces.Take((2+t.p5.extraDamageDice)*(t.critical?2:1)).Sum();
            else t.p5.damage=t.spec.damage*(t.critical?2:1);
        }
        void P5RollThunderTargets(CombatState s,RollTicket ticket,CombatCommand c)
        {
            var a=s.Actor(ticket.actorId);
            foreach(var target in s.actors.Concat(s.enemies).Where(x=>x.Alive&&x.id!=a.id))
            {
                if(!P5InCone(a,target,ticket.p5.directionX,ticket.p5.directionZ,ticket.spec.range)||!P5Line(c,target.id)||!(P5Evidence(c,target.id)?.inArea??true))continue;
                var r=new P5TargetRoll{targetId=target.id,bonus=P5ConSave(target),targetNumber=ticket.targetNumber,inAreaAtConfirm=true};
                if(target.id==ticket.targetId){r.rawFaces=new List<int>(ticket.rawFaces);r.selectedFace=ticket.selectedFace;r.bonus=ticket.baseBonus;r.total=ticket.total;}
                else
                {r.selectedFace=RandomStreams.D20(s.random.visibleTactical);r.rawFaces.Add(r.selectedFace);bool advantage=target.statuses.Any(x=>x.id=="save-advantage"&&x.remaining>0),disadvantage=target.staggerRemaining>0;if(advantage!=disadvantage){int second=RandomStreams.D20(s.random.visibleTactical);r.rawFaces.Add(second);r.selectedFace=advantage?Math.Max(r.selectedFace,second):Math.Min(r.selectedFace,second);}r.total=r.selectedFace+r.bonus;}
                r.savePassed=r.total>=r.targetNumber;ticket.p5.targets.Add(r);
            }
            ticket.rngAfter=s.random.visibleTactical.drawCount;
        }
        void P5Reward(CombatState s,P5ActionContext context,bool effective)
        {
            context.effective|=effective;if(!effective||context.rewarded||!context.crossActor||context.neutral)return;
            context.rewarded=true;var use=s.setupUses.Find(x=>x.root==context.root);if(use!=null)use.rewarded=true;
            AwardP4Root(s,"setup",context.root,context.paid?Config.p5.comboMomentum:Config.p5.followupMomentum,true,context.paid?context.root:null,false);
            if(context.paid)AwardFocusBudget(s,context.root,"combo",Config.p5.comboFocus);
            var owner=s.Actor(context.ownerId);var target=s.Actor(context.targetId);
            if(owner!=null&&target!=null&&Config.RulesFor(owner.id).firstExposeExtension&&!owner.p4ExposeUsed&&target.staggerRemaining>0){owner.p4ExposeUsed=true;target.staggerRemaining+=1;}
        }
        bool P5EffectiveRewardRoot(CombatState s,string root)
        {
            if(Config.p5==null||s.p5==null)return false;
            var context=s.rolls.Where(t=>t.p5!=null&&t.id==root).Select(t=>t.p5).FirstOrDefault()??s.actors.Where(a=>a.basicAttackIntent?.p5!=null&&a.basicAttackIntent.rootId==root).Select(a=>a.basicAttackIntent.p5).FirstOrDefault();
            return context!=null&&context.effective&&context.crossActor&&!context.neutral&&context.encounterAttemptId==s.encounterId&&s.p5.opportunities.Any(t=>t.id==context.opportunityId&&t.consumed&&t.usedRoot==root);
        }
        static bool P5TicketExpired(CombatState s,RollTicket t)=>t.p5!=null&&s.simulationSeconds>=t.p5.startDeadlineSimulationTime;
        static void P5CancelTicket(CombatState s,RollTicket ticket,string reason)
        {
            var q=s.queue.Find(x=>x.ticketId==ticket.id);
            if(q!=null){CancelQueue(s,q,ticket,reason);return;}
            Release(s,ticket,true);ticket.stage="Canceled";
            if(s.pendingRollId==ticket.id){s.pendingRollId=null;if(s.mode==CombatMode.PostRollDecision)s.mode=CombatMode.TacticalFocus;}
            if(s.decisionResume?.suspendedTicketId==ticket.id){s.decisionResume.suspendedTicketId=null;if(s.decisionResume.priorMode==CombatMode.PostRollDecision)s.decisionResume.priorMode=CombatMode.TacticalFocus;}
        }
        bool P5DeadlineWillCross(CombatState s,float realDelta)
        {
            if(Config.p5==null||!SimulationMode(s)||realDelta<=0)return false;float after=s.simulationSeconds+realDelta*Scale;
            return s.rolls.Any(t=>t.purpose=="Tactical"&&t.p5!=null&&(t.stage=="AwaitDecision"||t.stage=="SuspendedAwaitDecision"||t.stage=="Queued")&&(after>=t.p5.startDeadlineSimulationTime||s.Actor(t.actorId)?.Alive!=true||s.Actor(t.targetId)?.Alive!=true));
        }
        bool ExpireP5Tickets(CombatState s)
        {
            if(Config.p5==null)return false;bool changed=false;
            foreach(var ticket in s.rolls.Where(t=>t.purpose=="Tactical"&&t.p5!=null&&(t.stage=="AwaitDecision"||t.stage=="SuspendedAwaitDecision"||t.stage=="Queued")))
                if(P5TicketExpired(s,ticket)||s.Actor(ticket.actorId)?.Alive!=true||s.Actor(ticket.targetId)?.Alive!=true){P5CancelTicket(s,ticket,"action_start_expired");changed=true;}
            return changed;
        }
    }
}

