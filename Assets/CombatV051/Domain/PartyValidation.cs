using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        static void ValidatePartyState(CombatState s)
        {
            Require(s.reactionWindows!=null&&s.incomingEvents!=null,"missing_reaction_domain");
            if(!s.IsPartyP1)
            {
                Require(s.reactionWindows.Count==0&&s.incomingEvents.Count==0&&s.activeReaction==null&&s.decisionResume==null&&s.mode!=CombatMode.ReactionPrompt&&s.rolls.All(t=>t.purpose=="Tactical"&&t.stage!="SuspendedAwaitDecision"),"p1_state_in_p0_profile");
                return;
            }
            bool v11Party=s.actors.Count==3&&s.actors.All(a=>a.v11!=null);
            Require(s.actors.Count==3&&(v11Party||s.actors.Select(a=>a.role).Distinct().Count()==3)&&s.actors.All(a=>a.role!=ActorRole.Enemy)&&s.actors.Any(a=>a.id==s.commandActorId),"party_template_invalid");
            if(v11Party)Require(s.actors.All(a=>V11Catalog.RoleMatches(a.id,a.role)&&a.p6!=null&&a.p6.characterId==a.id),"v11_party_identity_invalid");
            if(s.p6!=null&&!v11Party)Require(P6CharacterCatalog.All.All(c=>s.actors.Any(a=>a.id==c.id&&a.role==c.role&&a.p6!=null&&a.p6.characterId==c.id&&a.p6.classId==c.classId&&a.p6.weaponId==c.weaponId)),"p6_party_identity_invalid");
            foreach(var a in s.actors.Concat(s.enemies))
            {
                Require(Enum.IsDefined(typeof(ActorRole),a.role)&&Enum.IsDefined(typeof(FormationPreference),a.formation)&&a.isEnemy==(a.role==ActorRole.Enemy),"actor_role_invalid");
                Require(a.heldSuperiority>=0&&a.heldSuperiority<=a.superiority&&Finite(a.reactionRemaining)&&a.reactionRemaining>=0&&Finite(a.bonusRemaining)&&a.bonusRemaining>=0&&Finite(a.aiMoveX)&&Finite(a.aiMoveZ)&&Finite(a.aiDecisionRemaining),"party_resource_timer_invalid");
                var w=s.reactionWindows.Find(x=>x.id==a.reactionHeldBy);
                Require(string.IsNullOrEmpty(a.reactionHeldBy)?a.heldSuperiority==0:w!=null&&w.reactorId==a.id&&w.spec!=null&&!w.resourcesSpent&&(w.stage=="Choosing"||w.stage=="Rolling"||w.stage=="AwaitDecision")&&a.heldSuperiority==w.spec.superiorityCost,"reaction_hold_mismatch");
            }
            Require(s.incomingEvents.Select(e=>e.id).Distinct().Count()==s.incomingEvents.Count&&s.incomingEvents.Select(e=>e.rootId+"/"+e.defenderId).Distinct().Count()==s.incomingEvents.Count,"duplicate_incoming");
            foreach(var e in s.incomingEvents)
            {
                Require(!string.IsNullOrEmpty(e.id)&&!string.IsNullOrEmpty(e.rootId)&&s.Actor(e.attackerId)!=null&&s.Actor(e.defenderId)!=null&&s.incomingRoots.Contains(e.rootId+"/"+e.defenderId),"incoming_identity_invalid");
                Require(e.rawFaces!=null&&e.rawFaces.Count<=2&&e.rawFaces.All(x=>x>=1&&x<=20)&&e.prospectiveDamage>=0&&e.finalDamage>=0&&Finite(e.createdSimulationTime),"incoming_result_invalid");
                Require(new[]{"Ready","AwaitReaction","Applied","Canceled"}.Contains(e.stage)&&e.damageApplied==(e.stage=="Applied"||e.stage=="Canceled"),"incoming_stage_invalid");
                if(e.stage=="AwaitReaction")Require(s.reactionWindows.Exists(w=>w.id==e.reactionWindowId&&w.incomingId==e.id&&w.stage!="Resolved"&&w.stage!="Declined"),"incoming_window_missing");
            }
            Require(s.reactionWindows.Select(w=>w.id).Distinct().Count()==s.reactionWindows.Count,"duplicate_reaction_window");
            foreach(var w in s.reactionWindows)
            {
                Require(!string.IsNullOrEmpty(w.id)&&s.incomingEvents.Exists(e=>e.id==w.incomingId&&e.defenderId==w.defenderId&&e.reactionWindowId==w.id)&&Finite(w.remaining)&&w.remaining>=0&&w.remaining<=P6ReactionSeconds(s),"reaction_window_invalid");
                Require(new[]{"Offered","Choosing","Rolling","AwaitDecision","Resolved","Declined"}.Contains(w.stage),"reaction_stage_invalid");
                if(w.stage=="Choosing"||w.stage=="Rolling"||w.stage=="AwaitDecision")Require(w.attempted&&w.spec!=null&&s.activeReactionWindowId==w.id&&s.Actor(w.reactorId).reactionHeldBy==w.id,"reaction_owner_invalid");
                if(w.resourcesSpent)Require(w.stage=="Resolved"&&w.spec!=null,"reaction_spend_invalid");
                if(w.reactionSpent)Require(w.spec!=null&&!string.IsNullOrEmpty(w.paymentId)&&!string.IsNullOrEmpty(w.reactorId),"reaction_payment_missing");
                if(w.stage=="Rolling"||w.stage=="AwaitDecision")Require(w.reactionSpent,"reaction_roll_unpaid");
                if(!string.IsNullOrEmpty(w.ticketId))Require(s.rolls.Exists(t=>t.id==w.ticketId&&t.purpose=="Reaction"&&t.incomingId==w.incomingId&&t.reactionWindowId==w.id),"reaction_ticket_missing");
            }
            var liveRolls=s.rolls.Where(t=>t.stage=="Rolling"||t.stage=="AwaitDecision").ToList();Require(liveRolls.Count<=1&&(liveRolls.Count==0?string.IsNullOrEmpty(s.pendingRollId):s.pendingRollId==liveRolls[0].id),"visible_roll_channel_invalid");
            var suspended=s.rolls.Where(t=>t.stage=="SuspendedAwaitDecision").ToList();Require(suspended.Count<=1,"nested_suspended_decision");
            Require(s.pendingThreatEvents!=null&&s.deferredIncomingBatches!=null,"missing_pending_events");ValidateReactionSettings(s.reactionSettings,s);
            Require(s.pendingThreatEvents.Select(g=>g.id).Distinct().Count()==s.pendingThreatEvents.Count&&s.pendingThreatEvents.Select(g=>g.rootId).Distinct().Count()==s.pendingThreatEvents.Count,"duplicate_pending_event");
            Require(s.incomingEvents.All(e=>s.pendingThreatEvents.Exists(g=>g.id==e.reactionEventId&&g.rootId==e.rootId&&g.incomingIds.Contains(e.id))),"incoming_group_missing");
            Require(s.reactionWindows.All(w=>s.incomingEvents.Exists(e=>e.id==w.incomingId&&e.reactionEventId==w.reactionEventId)),"window_group_mismatch");
            Require(s.deferredIncomingBatches.Select(b=>b.rootId).Distinct().Count()==s.deferredIncomingBatches.Count,"duplicate_deferred_incoming");
            foreach(var b in s.deferredIncomingBatches)Require(!string.IsNullOrEmpty(b.commandId)&&!string.IsNullOrEmpty(b.rootId)&&s.Actor(b.actorId)!=null&&b.contacts!=null&&b.contacts.Count>0&&b.contacts.Count<=3&&b.contacts.All(c=>s.Actor(c.targetId)!=null&&c.damage>=0&&c.damage<=10000)&&b.contacts.Select(c=>c.targetId).Distinct().Count()==b.contacts.Count,"deferred_incoming_invalid");
            foreach(var g in s.pendingThreatEvents)
            {
                Require(!string.IsNullOrEmpty(g.id)&&!string.IsNullOrEmpty(g.rootId)&&new[]{"Offered","Choosing","Resolved"}.Contains(g.stage)&&Finite(g.remainingUiSeconds)&&g.remainingUiSeconds>=0&&g.remainingUiSeconds<=P6ReactionSeconds(s),"pending_event_invalid");
                Require(g.incomingIds!=null&&g.incomingIds.Count>0&&g.incomingIds.Distinct().Count()==g.incomingIds.Count&&g.incomingIds.All(id=>s.incomingEvents.Exists(e=>e.id==id&&e.reactionEventId==g.id))&&g.respondedDefenderIds.Distinct().Count()==g.respondedDefenderIds.Count,"pending_event_children_invalid");
                if(g.stage=="Resolved")Require(s.reactionWindows.Where(w=>w.reactionEventId==g.id).All(w=>w.stage=="Resolved"||w.stage=="Declined"),"resolved_group_has_open_window");
            }
            if(s.activePendingEvent==null)Require(s.activeReaction==null&&s.decisionResume==null&&suspended.Count==0&&s.mode!=CombatMode.ReactionPrompt,"orphan_reaction_context");
            else
            {
                Require(s.activePendingEvent.stage=="Choosing"&&s.decisionResume!=null&&s.decisionResume.reactionEventId==s.activePendingEvent.id,"missing_reaction_context");
                Require(string.IsNullOrEmpty(s.decisionResume.suspendedTicketId)?suspended.Count==0:suspended.Count==1&&suspended[0].id==s.decisionResume.suspendedTicketId&&suspended[0].purpose=="Tactical","suspended_ticket_mismatch");
                var mode=s.mode==CombatMode.CombatPaused?s.resumeMode:s.mode;Require(mode==CombatMode.ReactionPrompt||mode==CombatMode.RollFreeze||mode==CombatMode.PostRollDecision,"reaction_mode_invalid");
                if(mode==CombatMode.ReactionPrompt)Require(s.activeReaction==null||s.activeReaction.stage=="Choosing","reaction_visible_ticket_mismatch");
                else Require(s.activeReaction!=null&&s.pendingRoll!=null&&s.pendingRoll.purpose=="Reaction"&&s.pendingRoll.reactionWindowId==s.activeReaction.id,"reaction_visible_ticket_mismatch");
            }
            foreach(var t in s.rolls.Where(t=>t.purpose=="Reaction"))Require(t.rollType=="Defense"&&s.reactionWindows.Exists(w=>w.ticketId==t.id)&&t.stage!="Queued"&&t.stage!="Executing"&&t.stage!="SuspendedAwaitDecision","reaction_roll_invalid");
            Require(s.rolls.All(t=>t.createdRulesVersion==s.rulesVersion),"ticket_rules_version_mismatch");
        }
    }
}
