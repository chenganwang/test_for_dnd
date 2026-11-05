using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        static void ValidateReactionSettings(ReactionSettings value,CombatState state=null)
        {Require(value!=null&&Enum.IsDefined(typeof(ReactionAutoMode),value.autoMode)&&Finite(value.decisionSeconds)&&value.decisionSeconds>0&&value.decisionSeconds<=P6ReactionSeconds(state)&&!value.pauseDecision,"reaction_settings_invalid");}
        static void ValidateP4ReactionConfiguration(CombatConfig c)
        {
            Require(Finite(c.importantReactionDamageFraction)&&c.importantReactionDamageFraction>0&&c.importantReactionDamageFraction<=1&&Finite(c.reactionFocusScale)&&c.reactionFocusScale>=0&&c.reactionFocusScale<=1&&Finite(c.allyShieldRange)&&c.allyShieldRange>0,"invalid_reaction_config");
            Require(c.reactions!=null&&c.reactions.Select(r=>r.id).Distinct().Count()==c.reactions.Count&&c.reactions.All(r=>r!=null&&(r.id=="shield"||r.id=="parry")&&r.spellCost>=0&&r.superiorityCost>=0&&Finite(r.protectionReduction)&&r.protectionReduction>0&&r.protectionReduction<=1),"invalid_reaction_spec");
            Require(c.actorOverrides.All(r=>r.firstShoveBreakBonus>=0&&r.nearMissMaximumDeficit>0&&r.nearMissBonus>0),"invalid_specialization_correction");
        }
        public List<ReactionQuote> GetReactionChoices(string eventId=null)
        {return Choices(State,eventId??State.activeReactionEventId);}
        List<ReactionQuote> Choices(CombatState s,string eventId)
        {
            var choices=new List<ReactionQuote>();
            foreach(var w in s.reactionWindows.Where(w=>(eventId==null||w.reactionEventId==eventId)&&(w.stage=="Offered"||w.stage=="Choosing")))
                foreach(var a in s.actors.Where(a=>a.Alive))foreach(var spec in Config.reactions)
                {var q=QuoteReactionSafe(s,w,a.id,spec.id);if(q!=null)choices.Add(q);}
            return choices;
        }
        public ReactionQuote QuoteReaction(string windowId,string actorId,string reactionId)
        {
            var w=State.reactionWindows.Find(x=>x.id==windowId);
            try{return QuoteReactionInternal(State,windowId,actorId,reactionId,w!=null&&State.activeReactionWindowId==w.id&&w.reactorId==actorId&&w.reactionId==reactionId);}
            catch(Exception ex){return new ReactionQuote{legal=false,reason=ex.Message,windowId=windowId,actorId=actorId,reactionId=reactionId};}
        }
        ReactionQuote QuoteReactionSafe(CombatState s,ReactionWindow w,string actorId,string reactionId)
        {try{return QuoteReactionInternal(s,w.id,actorId,reactionId,s.activeReactionWindowId==w.id&&w.reactorId==actorId&&w.reactionId==reactionId);}catch(InvalidOperationException){return null;}}
        ReactionQuote QuoteReactionInternal(CombatState s,string windowId,string actorId,string reactionId,bool ownHold)
        {
            Require(s.IsPartyP1&&s.inCombat,"party_reaction_unavailable");var w=s.reactionWindows.Find(x=>x.id==windowId);
            Require(w!=null&&(w.stage=="Offered"||ownHold&&(w.stage=="Choosing"||w.stage=="Rolling"||w.stage=="AwaitDecision")),"reaction_window_unavailable");
            var e=s.incomingEvents.Find(x=>x.id==w.incomingId);var a=Live(s,actorId);var target=Live(s,w.defenderId);var spec=ownHold&&w.spec!=null?w.spec:Config.Reaction(reactionId);
            bool lifeShield=V13Enabled&&a.id==V12Finn&&Config.v13.finnTree=="guard"&&reactionId=="shield";
            if(lifeShield){spec=CombatJson.Clone(spec);spec.spellCost=0;spec.shieldAcBonus=0;Require(a.v12.basicCooldown<=0&&a.windupRemaining<=0&&!V12ActionActive(a),"hero_basic_cooldown");}
            Require(e!=null&&!e.damageApplied&&e.sourceKind=="Attack"&&e.hitBeforeReaction&&e.prospectiveDamage>0,"reaction_trigger_invalid");
            Require(!a.isEnemy&&!target.isEnemy&&spec!=null&&spec.id==reactionId&&spec.role==a.role,"reaction_role_forbidden");
            Require(e.reactionEligibleActorIds==null||e.reactionEligibleActorIds.Contains(a.id),"reaction_line_of_sight_blocked");
            Require(a.staggerRemaining<=0&&(ownHold&&w.reactionSpent||a.reactionRemaining<=0)&&(string.IsNullOrEmpty(a.reactionHeldBy)||ownHold&&a.reactionHeldBy==w.id),"reaction_unavailable");
            int ownSpell=ownHold&&w.spec!=null?w.spec.spellCost:0,ownSuperiority=ownHold&&w.spec!=null?w.spec.superiorityCost:0;
            Require(a.spellSlots-a.heldSpellSlots+ownSpell>=spec.spellCost&&a.superiority-a.heldSuperiority+ownSuperiority>=spec.superiorityCost,"reaction_resource_unavailable");
            var q=new ReactionQuote{legal=true,windowId=w.id,eventId=w.reactionEventId,incomingId=e.id,actorId=a.id,defenderId=target.id,reactionId=reactionId,requiresD20=spec.requiresD20,incomingTotal=e.total,currentArmorClass=e.effectiveArmorClass,afterArmorClass=e.effectiveArmorClass+spec.shieldAcBonus,spellCost=spec.spellCost,superiorityCost=spec.superiorityCost,prospectiveDamage=e.prospectiveDamage};
            if(reactionId=="shield")
            {
                if(lifeShield){q.range=6;q.damageReduction=Math.Min(e.prospectiveDamage,Math.Max(1,(int)Math.Floor(target.maxHp*Config.v13.shieldFraction)));return q;}
                q.range=Config.allyShieldRange;Require(a.id==target.id||Config.RulesFor(a.id).allyShield,"shield_self_only");
                Require(a.id==target.id||Distance(a,target)<=q.range&&Math.Abs(a.heightY-target.heightY)<=2,"reaction_out_of_range");
                Require(!e.critical&&e.total<q.afterArmorClass,"shield_cannot_change_result");q.damageReduction=e.prospectiveDamage;
            }
            else if(reactionId=="parry")
            {
                q.range=Config.RulesFor(a.id).protectionRange;
                Require(e.parryable,"incoming_not_parryable");Require(a.id==target.id||spec.protectAllies,"protection_target_invalid");
                Require(Distance(a,target)<=q.range&&Math.Abs(a.heightY-target.heightY)<=2,"reaction_out_of_range");
                q.bonus=a.strengthModifier+a.proficiency;q.targetNumber=e.total;
                q.successProbability=Math.Max(0,Math.Min(20,21+q.bonus-q.targetNumber))/20d;
                Require(q.successProbability>0,"reaction_cannot_change_result");
                q.damageReduction=a.id==target.id?e.prospectiveDamage:Math.Max(1,(float)Math.Floor(e.prospectiveDamage*spec.protectionReduction));
            }
            else throw new InvalidOperationException("unknown_reaction");
            return q;
        }
        static ReactionWindow ActiveWindow(CombatState s,CombatCommand c)
        {var w=s.activeReaction;Require(w!=null&&(string.IsNullOrEmpty(c.reactionWindowId)||c.reactionWindowId==w.id),"active_reaction_mismatch");Require(string.IsNullOrEmpty(c.incomingId)||c.incomingId==w.incomingId,"incoming_window_mismatch");Require(c.lineOfSight,"reaction_line_of_sight_blocked");return w;}
        void OpenReactionEvent(CombatState s,PendingThreatEvent group,bool automatic)
        {
            Require(s.activePendingEvent==null&&s.decisionResume==null&&SimulationMode(s),"reaction_decision_busy");
            Require(group.stage=="Offered","reaction_event_unavailable");
            Require(HasReactionOpportunity(s,group),"reaction_opportunities_empty");
            if(!s.reactionOpportunityRoots.Contains(group.rootId))s.reactionOpportunityRoots.Add(group.rootId);
            var context=new DecisionResumeContext{priorMode=s.mode,priorCommandActorId=s.commandActorId,reactionEventId=group.id};
            if(s.pendingRoll!=null){Require(s.pendingRoll.purpose=="Tactical"&&s.pendingRoll.stage=="AwaitDecision","pending_roll_busy");context.suspendedTicketId=s.pendingRoll.id;s.pendingRoll.stage="SuspendedAwaitDecision";}
            s.pendingRollId=null;s.decisionResume=context;s.activeReactionEventId=group.id;group.stage="Choosing";group.autoOpened=automatic;s.mode=CombatMode.ReactionPrompt;
        }
        CombatResult BeginReaction(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s),"world_frozen");Require(c.lineOfSight,"reaction_line_of_sight_blocked");var w=s.reactionWindows.Find(x=>x.id==c.reactionWindowId);Require(w!=null,"reaction_window_unavailable");
            var group=s.pendingThreatEvents.Find(e=>e.id==w.reactionEventId);Require(group!=null,"reaction_event_missing");
            Require(s.activePendingEvent==null||s.activeReactionEventId==group.id,"reaction_decision_busy");
            if(s.activeReaction!=null)
            {
                Require(s.activeReaction.stage=="Choosing"&&!s.activeReaction.reactionSpent,"reaction_already_committed");
                ReleaseReactionHold(s,s.activeReaction);s.activeReaction.stage="Offered";s.activeReaction.reactorId=null;s.activeReaction.spec=null;s.activeReaction.reactionId=null;s.activeReactionWindowId=null;
            }
            var q=QuoteReactionInternal(s,w.id,c.actorId,c.reactionId,false);
            Require(string.IsNullOrEmpty(c.incomingId)||c.incomingId==w.incomingId,"incoming_window_mismatch");
            if(s.activePendingEvent==null)OpenReactionEvent(s,group,false);
            w.attempted=true;w.stage="Choosing";w.reactionId=c.reactionId;w.reactorId=c.actorId;w.spec=CombatJson.Clone(Config.Reaction(c.reactionId));w.remaining=group.remainingUiSeconds;
            if(V13Enabled&&w.reactorId==V12Finn&&Config.v13.finnTree=="guard"&&w.reactionId=="shield"){w.spec.spellCost=0;w.spec.shieldAcBonus=0;}
            var a=s.Actor(w.reactorId);a.reactionHeldBy=w.id;a.heldSpellSlots+=w.spec.spellCost;a.heldSuperiority+=w.spec.superiorityCost;
            s.activeReactionWindowId=w.id;s.decisionResume.reactionWindowId=w.id;s.mode=CombatMode.ReactionPrompt;
            return new CombatResult{reactionEventId=group.id,reactionWindowId=w.id,incomingId=w.incomingId,deferred=true,prospectiveDamage=q.prospectiveDamage,disposition="reaction_choosing"};
        }
        CombatResult ConfirmReaction(CombatState s,CombatCommand c)
        {
            Require(s.mode==CombatMode.ReactionPrompt&&s.pendingRoll==null,"reaction_prompt_required");var w=ActiveWindow(s,c);Require(w.stage=="Choosing"&&w.spec.requiresD20,"reaction_does_not_roll");
            var q=QuoteReactionInternal(s,w.id,w.reactorId,w.reactionId,true);var e=s.incomingEvents.Find(x=>x.id==w.incomingId);var a=s.Actor(w.reactorId);
            Require(!w.reactionSpent,"reaction_already_paid");a.reactionRemaining=Config.reactionRecovery*V11BuffCatalog.ReactionMultiplier(Config.v11);w.reactionSpent=true;w.paymentId=c.id;
            var t=new RollTicket{id=Id(s,"reaction-roll"),actorId=a.id,targetId=e.attackerId,actionId=w.reactionId,rollOwnerId=a.id,rollType="Defense",purpose="Reaction",reactionWindowId=w.id,incomingId=e.id,chargePolicy="None",chargeSpent=0,createdRulesVersion=s.rulesVersion,baseBonus=q.bonus,targetNumber=q.targetNumber,spec=new ActionSpec{id=w.reactionId,rollType="Defense",damage=0}};
            t.paidResourceIds.Add("reaction/"+c.id);Roll(s,t);s.rolls.Add(t);s.pendingRollId=t.id;w.ticketId=t.id;w.stage="Rolling";s.mode=CombatMode.RollFreeze;
            var result=TicketResult(t);result.reactionEventId=w.reactionEventId;result.reactionWindowId=w.id;result.incomingId=e.id;return result;
        }
        static void ReleaseReactionHold(CombatState s,ReactionWindow w)
        {
            var a=s.Actor(w.reactorId??w.defenderId);
            if(w.spec!=null&&a!=null&&a.reactionHeldBy==w.id){a.heldSpellSlots-=w.spec.spellCost;a.heldSuperiority-=w.spec.superiorityCost;a.reactionHeldBy=null;}
        }
        CombatResult FinishReaction(CombatState s,ReactionWindow w,bool commit)
        {
            Require(w.stage!="Resolved"&&w.stage!="Declined","reaction_window_unavailable");var e=s.incomingEvents.Find(x=>x.id==w.incomingId);Require(e!=null&&!e.damageApplied,"incoming_already_resolved");
            bool active=s.activeReactionWindowId==w.id;var a=s.Actor(w.reactorId??w.defenderId);var t=s.rolls.Find(x=>x.id==w.ticketId);
            if(commit)
            {
                Require(active&&w.spec!=null&&a!=null&&a.Alive&&a.reactionHeldBy==w.id,"reaction_hold_lost");
                Require(w.spec.requiresD20?t!=null&&t.stage=="AwaitDecision"&&s.mode==CombatMode.PostRollDecision:w.stage=="Choosing"&&s.mode==CombatMode.ReactionPrompt,"reaction_not_ready");
                var quote=QuoteReactionInternal(s,w.id,a.id,w.reactionId,true);
                Require(a.spellSlots>=w.spec.spellCost&&a.superiority>=w.spec.superiorityCost,"reaction_resource_lost");
                if(!w.reactionSpent){a.reactionRemaining=Config.reactionRecovery*V11BuffCatalog.ReactionMultiplier(Config.v11);w.reactionSpent=true;w.paymentId=Id(s,"reaction-pay");}
                a.spellSlots-=w.spec.spellCost;a.superiority-=w.spec.superiorityCost;w.resourcesSpent=true;
                int damageBefore=e.prospectiveDamage;
                if(V13Enabled&&a.id==V12Finn&&Config.v13.finnTree=="guard"&&w.reactionId=="shield")
                {var target=s.Actor(w.defenderId);V11GrantShield(target,Math.Max(1,(int)Math.Floor(target.maxHp*Config.v13.shieldFraction)),Config.v13.shieldSeconds);a.v12.basicCooldown=Config.v13.shieldCooldown;Member(s,a.id).appearanceUntil=s.simulationSeconds+1;}
                else if(w.reactionId=="shield")
                {
                    var target=s.Actor(w.defenderId);target.statuses.RemoveAll(x=>x.id=="shield");target.statuses.Add(new StatusState{id="shield",sourceId=w.id,remaining=w.spec.shieldSeconds,armorClassBonus=w.spec.shieldAcBonus});
                    e.effectiveArmorClass+=w.spec.shieldAcBonus;if(!e.critical&&e.total<e.effectiveArmorClass)e.prospectiveDamage=0;
                }
                else if(t!=null&&t.hit)
                {
                    e.prospectiveDamage=Math.Max(0,e.prospectiveDamage-(int)quote.damageReduction);
                    if(a.id==w.defenderId){var attacker=s.Actor(e.attackerId);if(attacker!=null&&attacker.Alive){float priorBreak=attacker.breakValue;attacker.breakValue=Math.Max(0,attacker.breakValue-1);if(attacker.breakValue<=0)attacker.staggerRemaining=2;AwardFocusBreak(s,attacker,e.rootId,priorBreak);}}
                    Award(s,"parry",e.rootId,"enemy/"+e.attackerId,true);
                    AwardFocusDefense(s,e.rootId,s.Actor(e.defenderId),damageBefore,"parry",s.Actor(e.attackerId));
                }
                V14CorrectionCommitted(s,t,e.prospectiveDamage<damageBefore);if(e.prospectiveDamage<damageBefore)AwardFocusDefense(s,e.rootId,s.Actor(e.defenderId),e.baseDamage*(e.critical?2:1),"reaction",s.Actor(e.attackerId));
            }
            ReleaseReactionHold(s,w);if(t!=null)t.stage=commit?"Resolved":"Canceled";w.stage=commit?"Resolved":"Declined";e.stage="Ready";e.reason=commit?"reaction_"+w.reactionId:"reaction_declined";
            var group=s.pendingThreatEvents.Find(g=>g.id==w.reactionEventId);if(group!=null&&!group.respondedDefenderIds.Contains(w.defenderId))group.respondedDefenderIds.Add(w.defenderId);
            if(active){s.pendingRollId=null;s.activeReactionWindowId=null;if(s.decisionResume!=null)s.decisionResume.reactionWindowId=null;s.mode=CombatMode.ReactionPrompt;}
            DrainIncoming(s);CloseCompletedReactionEvent(s,group);
            var result=IncomingResult(e);result.reactionWindowId=w.id;result.ticketId=w.ticketId;return result;
        }
        CombatResult DeclineReactionCommand(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s),"world_frozen");
            if(!string.IsNullOrEmpty(c.reactionWindowId))
            {var w=s.reactionWindows.Find(x=>x.id==c.reactionWindowId);Require(w!=null,"reaction_window_unavailable");return FinishReaction(s,w,false);}
            var group=s.pendingThreatEvents.Find(g=>g.id==(c.reactionEventId??s.activeReactionEventId));Require(group!=null&&group.stage!="Resolved","reaction_event_unavailable");
            foreach(var w in s.reactionWindows.Where(w=>w.reactionEventId==group.id&&w.stage!="Resolved"&&w.stage!="Declined").ToList())if(w.stage!="Resolved"&&w.stage!="Declined")FinishReaction(s,w,false);
            CloseCompletedReactionEvent(s,group);return new CombatResult{reactionEventId=group.id,reason="reaction_declined"};
        }
        void CloseCompletedReactionEvent(CombatState s,PendingThreatEvent group)
        {
            if(group==null||group.stage=="Resolved")return;
            var selected=s.activeReaction;
            if(selected!=null&&selected.reactionEventId==group.id&&!s.Actor(selected.reactorId).Alive)
            {
                ReleaseReactionHold(s,selected);selected.stage="Declined";var ticket=s.rolls.Find(t=>t.id==selected.ticketId);if(ticket!=null)ticket.stage="Canceled";
                var incoming=s.incomingEvents.Find(e=>e.id==selected.incomingId);incoming.stage="Ready";incoming.reason="reactor_unavailable";
                s.pendingRollId=null;s.activeReactionWindowId=null;s.mode=CombatMode.ReactionPrompt;
            }
            foreach(var w in s.reactionWindows.Where(w=>w.reactionEventId==group.id&&w.stage=="Offered").ToList())
                if(!HasReactionOpportunity(s,group)||!s.actors.Any(a=>Config.reactions.Any(spec=>QuoteReactionSafe(s,w,a.id,spec.id)!=null)))
                {w.stage="Declined";var e=s.incomingEvents.Find(x=>x.id==w.incomingId);e.stage="Ready";e.reason="reaction_no_longer_legal";if(!group.respondedDefenderIds.Contains(w.defenderId))group.respondedDefenderIds.Add(w.defenderId);}
            DrainIncoming(s);
            if(s.reactionWindows.Any(w=>w.reactionEventId==group.id&&w.stage!="Resolved"&&w.stage!="Declined"))return;
            group.stage="Resolved";if(s.activeReactionEventId==group.id)RestoreDecision(s);
        }
        void RestoreDecision(CombatState s)
        {
            var context=s.decisionResume;s.decisionResume=null;s.activeReactionWindowId=null;s.activeReactionEventId=null;
            if(context==null){s.mode=CombatMode.RealtimeCombat;return;}s.commandActorId=context.priorCommandActorId;
            if(!string.IsNullOrEmpty(context.suspendedTicketId))
            {
                var t=s.rolls.Find(x=>x.id==context.suspendedTicketId);var actor=s.Actor(t.actorId);var target=s.Actor(t.targetId);
                if(actor!=null&&actor.Alive&&target!=null&&target.Alive){t.stage="AwaitDecision";s.pendingRollId=t.id;s.mode=CombatMode.PostRollDecision;return;}
                t.stage="Canceled";Release(s,t,true);s.pendingRollId=null;s.mode=CombatMode.TacticalFocus;return;
            }
            s.mode=context.priorMode==CombatMode.QueuedExecution&&s.queue.All(q=>q.stage!="Queued"&&q.stage!="Executing")?CombatMode.RealtimeCombat:context.priorMode;
        }
        CombatResult RecordIncoming(CombatState s,CombatCommand c)
        {
            Require(!c.refundOrigin,"refund_cannot_generate_reward");
            var batch=new CombatCommand{id=c.id,actorId=c.actorId,rootId=c.rootId,qualified=c.qualified,fromReaction=c.fromReaction,p5=c.p5,incomingContacts=new List<IncomingContact>{new IncomingContact{targetId=c.targetId,damage=(int)c.amount,dc=c.dc,savingThrow=c.kind==CommandKind.IncomingSave,parryable=c.parryable}}};
            Require(Finite(c.amount)&&c.amount>=0&&c.amount<=10000,"invalid_incoming_fact");return RecordIncomingBatch(s,batch);
        }
        CombatResult RecordIncomingBatch(CombatState s,CombatCommand c)
        {
            Require(!c.refundOrigin,"refund_cannot_generate_reward");
            Require(s.inCombat&&c.qualified&&!string.IsNullOrEmpty(c.rootId)&&c.incomingContacts!=null&&c.incomingContacts.Count>0&&c.incomingContacts.Count<=3,"invalid_incoming_batch");
            var source=s.Actor(c.actorId);Require(source!=null,"actor_unavailable");Require(c.incomingContacts.Select(x=>x.targetId).Distinct().Count()==c.incomingContacts.Count,"duplicate_incoming_target");
            foreach(var contact in c.incomingContacts){var target=s.Actor(contact.targetId);Require(target!=null&&source.isEnemy!=target.isEnemy&&contact.damage>=0&&contact.damage<=10000,"invalid_incoming_fact");Require(!s.incomingRoots.Contains(c.rootId+"/"+target.id),"incoming_already_resolved");}
            if(!SimulationMode(s))
            {
                Require(!s.deferredIncomingBatches.Any(b=>b.rootId==c.rootId),"incoming_already_queued");s.deferredIncomingBatches.Add(new DeferredIncomingBatch{commandId=c.id,actorId=c.actorId,rootId=c.rootId,fromReaction=c.fromReaction,contacts=CombatJson.Clone(c.incomingContacts)});
                return new CombatResult{deferred=true,reason="incoming_queued_while_frozen"};
            }
            var group=s.pendingThreatEvents.Find(g=>g.rootId==c.rootId);
            if(group==null){group=new PendingThreatEvent{id=Id(s,"threat"),rootId=c.rootId,remainingUiSeconds=s.reactionSettings.decisionSeconds,arrivalSequence=s.sequence,fromReaction=c.fromReaction};s.pendingThreatEvents.Add(group);}
            PendingIncoming first=null;
            foreach(var contact in c.incomingContacts)
            {
                var target=s.Actor(contact.targetId);s.incomingRoots.Add(c.rootId+"/"+target.id);
                bool preparedGuard=target.basicOrder!=null&&target.basicOrder.kind==BasicOrderKind.Prepare&&target.basicOrder.stage=="Holding";
                var guardActor=target.guardRemaining>0&&!preparedGuard?target:(!contact.savingThrow&&contact.parryable?FindP4PreparedProtector(s,source,target):null);
                if(guardActor!=null&&guardActor.id!=target.id&&contact.reactionEligibleActorIds!=null&&!contact.reactionEligibleActorIds.Contains(guardActor.id))guardActor=null;
                var e=new PendingIncoming{id=Id(s,"incoming"),reactionEventId=group.id,rootId=c.rootId,attackerId=source.id,defenderId=target.id,sourceKind=contact.savingThrow?"Save":"Attack",baseArmorClass=EffectiveArmorClass(target),effectiveArmorClass=EffectiveArmorClass(target),baseDamage=contact.damage,dc=contact.dc,createdSimulationTime=s.simulationSeconds,arrivalSequence=s.sequence,parryable=contact.parryable,stage="Ready",reactionEligibleActorIds=contact.reactionEligibleActorIds==null?null:new List<string>(contact.reactionEligibleActorIds)};
                s.incomingEvents.Add(e);group.incomingIds.Add(e.id);if(first==null)first=e;
                if(!target.Alive){e.reason="defender_unavailable";}
                else if(target.dodgeRemaining>0){e.reason="avoided_by_dodge";if(!target.isEnemy){Award(s,"perfect-dodge",c.rootId,"enemy/"+source.id,true);if(!group.fromReaction)AwardFocusDefense(s,c.rootId,target,contact.damage,"dodge",source);}}
                else if(guardActor!=null&&guardActor.guardElapsed<=Config.parrySeconds){e.reason=guardActor.id==target.id?"parried":"protected_by_preparation";e.guardActorId=guardActor.id;if(!guardActor.isEnemy){Award(s,"parry",c.rootId,"enemy/"+source.id,true);if(!group.fromReaction)AwardFocusDefense(s,c.rootId,target,contact.damage,"parry",source);}float priorBreak=source.breakValue;if(Config.p5!=null)P5ApplyBreak(source,1);else{source.breakValue=Math.Max(0,source.breakValue-1);if(source.breakValue<=0)source.staggerRemaining=2;}if(!group.fromReaction){AwardFocusBreak(s,source,c.rootId,priorBreak);P5BraceContact(s,guardActor,source,c.rootId);}}
                else
                {
                    bool save=contact.savingThrow;int natural=RandomStreams.D20(s.random.backgroundCombat);e.rawFaces.Add(natural);
                    if(!save&&Config.p5!=null){bool advantage,disadvantage;int ac;P5BasicRollModifiers(s,source,target,c,out advantage,out disadvantage,out ac);e.effectiveArmorClass+=ac;if(advantage!=disadvantage){int second=RandomStreams.D20(s.random.backgroundCombat);e.rawFaces.Add(second);natural=advantage?Math.Max(natural,second):Math.Min(natural,second);}}
                    else if(!save&&(source.role==ActorRole.Rogue&&IsBehind(source,target)||GetP4SetupAdvantage(s,source,target,"Attack"))){int second=RandomStreams.D20(s.random.backgroundCombat);e.rawFaces.Add(second);natural=Math.Max(natural,second);}
                    e.selectedFace=natural;e.total=natural+(save?target.dexSaveBonus+V13OwnSaveBonus(s,target):source.attackBonus)+(V13Enabled&&!save&&s.v13.activeRule==3&&s.v13.ruleUntil>s.simulationSeconds?-1:0);e.critical=!save&&natural==20;e.hitBeforeReaction=save||AttackHits(natural,e.total,e.effectiveArmorClass);
                    bool savePassed=save&&e.total>=contact.dc;e.prospectiveDamage=e.hitBeforeReaction?(savePassed?e.baseDamage/2:e.baseDamage*(e.critical?2:1)):0;
                    if(e.hitBeforeReaction&&guardActor!=null){e.guarded=true;e.guardActorId=guardActor.id;e.prospectiveDamage=(e.prospectiveDamage+1)/2;if(!group.fromReaction)P5BraceContact(s,guardActor,source,c.rootId);}
                    e.reason=save?(savePassed?"save_success_half":"save_failed"):e.hitBeforeReaction?"hit":"defended";
                    if(!target.isEnemy&&e.prospectiveDamage>0&&!save&&!group.fromReaction&&group.stage!="Resolved"&&HasReactionOpportunity(s,group))
                    {
                        var w=new ReactionWindow{id=Id(s,"reaction-window"),reactionEventId=group.id,incomingId=e.id,defenderId=target.id,remaining=group.remainingUiSeconds};s.reactionWindows.Add(w);e.reactionWindowId=w.id;
                        bool useful=s.actors.Any(a=>Config.reactions.Any(spec=>QuoteReactionSafe(s,w,a.id,spec.id)!=null));
                        if(useful){e.stage="AwaitReaction";group.lethal|=e.prospectiveDamage>=target.hp;group.important|=e.prospectiveDamage>=target.maxHp*Config.importantReactionDamageFraction||group.lethal;}
                        else{s.reactionWindows.Remove(w);e.reactionWindowId=null;}
                    }
                }
            }
            DrainIncoming(s);CloseCompletedReactionEvent(s,group);ProcessP4ReactionTransitions(s);var result=IncomingResult(first);result.reactionEventId=group.id;return result;
        }
        static CombatResult IncomingResult(PendingIncoming e)
        {return new CombatResult{incomingId=e.id,reactionEventId=e.reactionEventId,reactionWindowId=e.reactionWindowId,deferred=!e.damageApplied,damageApplied=e.damageApplied,disposition=e.stage,damage=e.damageApplied?e.finalDamage:0,prospectiveDamage=e.prospectiveDamage,hit=e.hitBeforeReaction,critical=e.critical,total=e.total,rawFaces=new List<int>(e.rawFaces),reason=e.reason};}
        void DrainIncoming(CombatState s)
        {
            foreach(var group in s.incomingEvents.Where(e=>!e.damageApplied).GroupBy(e=>e.defenderId))foreach(var e in group.OrderBy(x=>x.arrivalSequence))
            {
                var target=s.Actor(e.defenderId);
                if(target==null||!target.Alive)
                {
                    var w=s.reactionWindows.Find(x=>x.id==e.reactionWindowId);
                    if(w!=null&&w.stage!="Resolved"&&w.stage!="Declined")
                    {ReleaseReactionHold(s,w);w.stage="Declined";var t=s.rolls.Find(x=>x.id==w.ticketId);if(t!=null)t.stage="Canceled";if(s.activeReactionWindowId==w.id){s.pendingRollId=null;s.activeReactionWindowId=null;if(s.mode!=CombatMode.CombatPaused)s.mode=CombatMode.ReactionPrompt;}}
                    e.finalDamage=0;e.damageApplied=true;e.stage="Canceled";e.reason="defender_unavailable";continue;
                }
                if(e.stage!="Ready")break;
                // Ordinary held Guard is useful mitigation, but is not a precise Focus recovery source.
                int v12HpBefore=target.hp;var damageSource=s.Actor(e.attackerId);double damageBase=V13OwnBasicDamage(s,e,target);
                if(V14Enabled&&damageSource?.id==V12Mira&&damageSource.basicAttackIntent?.rootId==e.rootId&&damageSource.basicAttackIntent.actionId==Config.BasicFor(damageSource).id)
                {bool player=damageSource.basicAttackIntent.v14PlayerCommand;double multiplier=V14MiraDamageMultiplier(s,damageSource,damageSource.basicAttackIntent.actionId,player);if(player&&V12Passive(damageSource)&&damageSource.v12.readinessRemaining>0)multiplier=Math.Max(multiplier,Config.v12.readinessMultiplier);damageBase*=multiplier;}
                double scaled=V14SourceDamage(damageSource,damageBase);e.finalDamage=V14Enabled?V14Mitigate(target,damageSource,scaled,"physical"):V12Enabled?V12Mitigate(target,damageBase,null):V11Absorb(target,e.prospectiveDamage);target.hp=Math.Max(0,target.hp-e.finalDamage);e.damageApplied=true;e.stage="Applied";
                V14LogDamage(s,damageSource,target,e.rootId,"direct-attack","direct",damageBase,scaled,v12HpBefore-target.hp);
                if(e.finalDamage>0&&target.isEnemy&&s.momentumLedger.encounterKind=="Boss"&&!s.pendingThreatEvents.Any(g=>g.id==e.reactionEventId&&g.fromReaction))AwardFocusBossPhaseIfReached(s,target,e.rootId);
                if(e.finalDamage>0){target.aimPreparing=false;target.aimRemaining=0;target.aimReady=false;BreakConcentrationFromDamage(s,target);}
                if(e.guarded&&e.finalDamage>0&&!target.isEnemy)Award(s,"guard",e.rootId,"enemy/"+e.attackerId,true);
                V12OnKilled(s,s.Actor(e.attackerId),target,e.rootId,"direct-attack",v12HpBefore-target.hp);P6ObserveDefense(s,e);V12ObserveDodge(s,e);if(e.sourceKind=="Attack"&&!s.pendingThreatEvents.Any(g=>g.id==e.reactionEventId&&g.fromReaction))V12OnDirectDamage(s,s.Actor(e.attackerId),target,e.rootId,"direct-attack",v12HpBefore-target.hp);
            }
        }
        void ProcessP4ReactionTransitions(CombatState s)
        {
            if(!s.IsPartyP1||!SimulationMode(s))return;
            if(s.deferredIncomingBatches.Count>0)
            {
                var buffered=s.deferredIncomingBatches.ToList();s.deferredIncomingBatches.Clear();
                foreach(var b in buffered)RecordIncomingBatch(s,new CombatCommand{id=b.commandId,actorId=b.actorId,rootId=b.rootId,qualified=true,fromReaction=b.fromReaction,incomingContacts=b.contacts});
            }
            foreach(var group in s.pendingThreatEvents.Where(g=>g.stage!="Resolved").ToList())CloseCompletedReactionEvent(s,group);
            if(s.activePendingEvent!=null||s.decisionResume!=null)return;
            if(!(NormalMode(s)||s.mode==CombatMode.TacticalFocus||s.mode==CombatMode.PostRollDecision))return;
            var next=s.pendingThreatEvents.Where(g=>g.stage=="Offered"&&Choices(s,g.id).Count>0&&(s.reactionSettings.autoMode==ReactionAutoMode.AllLegal||s.reactionSettings.autoMode==ReactionAutoMode.ImportantLegal&&(g.important||g.lethal))).OrderByDescending(g=>g.lethal).ThenBy(g=>g.arrivalSequence).FirstOrDefault();
            if(next!=null)OpenReactionEvent(s,next,true);
        }
        public float Tick(float realDelta,bool decisionUiAvailable,float? decisionDelta=null)
        {
            float uiDelta=decisionDelta??realDelta;
            Require(Finite(realDelta)&&realDelta>=0&&Finite(uiDelta)&&uiDelta>=0,"invalid_delta");
            if(V13FateBusy(State)&&State.mode!=CombatMode.CombatPaused){if(State.v13.fate.stage=="Deciding"&&decisionUiAvailable)State.v13.fate.decision=Down(State.v13.fate.decision,uiDelta);return TickCore(realDelta);}
            return State.IsPartyP1?TickParty(realDelta,decisionUiAvailable,uiDelta):TickFocusOnly(realDelta,decisionUiAvailable,uiDelta);
        }
        float TickParty(float realDelta,bool decisionUiAvailable,float decisionDelta)
        {
            LastTickError=null;Require(Finite(realDelta)&&realDelta>=0,"invalid_delta");
            if(State.mode==CombatMode.RollFreeze||State.mode==CombatMode.CombatPaused)return TickCore(realDelta);
            if(!State.pendingThreatEvents.Any(g=>g.stage!="Resolved")&&State.deferredIncomingBatches.Count==0)return TickFocusOnly(realDelta,decisionUiAvailable,decisionDelta);
            var before=State;State=State.Clone();
            try
            {
                bool canDecide=decisionUiAvailable&&State.mode!=CombatMode.RollFreeze&&State.mode!=CombatMode.CombatPaused;
                float dt=TickCore(realDelta);bool changed=AdvanceFocusClock(State,decisionDelta,decisionUiAvailable);changed|=ExpireP5Tickets(State);changed|=ExpireP6Tickets(State);
                var active=State.activePendingEvent;
                var visible=active==null?State.pendingThreatEvents.Where(g=>g.stage=="Offered").OrderBy(g=>g.arrivalSequence).FirstOrDefault():active;
                if(canDecide&&visible!=null)
                {
                    visible.remainingUiSeconds=Down(visible.remainingUiSeconds,decisionDelta);
                    foreach(var w in State.reactionWindows.Where(w=>w.reactionEventId==visible.id))w.remaining=visible.remainingUiSeconds;
                    if(visible.remainingUiSeconds<=0)
                    {
                        var selected=State.activeReaction;
                        if(selected!=null&&selected.reactionEventId==visible.id&&selected.reactionSpent&&selected.stage=="AwaitDecision")
                        {bool legal=State.reactionSettings.timeoutAcceptPaidRoll&&QuoteReactionSafe(State,selected,selected.reactorId,selected.reactionId)!=null;FinishReaction(State,selected,legal);}
                        foreach(var w in State.reactionWindows.Where(w=>w.reactionEventId==visible.id&&w.stage!="Resolved"&&w.stage!="Declined").ToList())if(w.stage!="Resolved"&&w.stage!="Declined")FinishReaction(State,w,false);
                        CloseCompletedReactionEvent(State,visible);changed=true;
                    }
                }
                var previousEvent=State.activeReactionEventId;int applied=State.incomingEvents.Count(e=>e.damageApplied),resolved=State.pendingThreatEvents.Count(g=>g.stage=="Resolved");
                ProcessP4ReactionTransitions(State);changed|=previousEvent!=State.activeReactionEventId||applied!=State.incomingEvents.Count(e=>e.damageApplied)||resolved!=State.pendingThreatEvents.Count(g=>g.stage=="Resolved");
                Validate(State);ValidateP4Tactics(State);ValidateP4Economy(State);ValidateP5State(State);ValidateP6State(State);ValidateV11(State);if(changed)Persist(State);return dt;
            }
            catch(Exception ex){LastTickError=ex.Message;State=before;return 0;}
        }
    }
}

