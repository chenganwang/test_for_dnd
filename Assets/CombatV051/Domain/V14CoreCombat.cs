using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        static V14CoreState V14Core(ActorState actor)
        {if(actor.v14Core==null)actor.v14Core=new V14CoreState();return actor.v14Core;}
        static V14CoreEncounter V14Encounter(CombatState s,ActorState actor)
        {
            var data=V14Core(actor);var e=data.encounters.Find(x=>x.encounterId==s.encounterId);
            if(e==null){e=new V14CoreEncounter{encounterId=s.encounterId};data.encounters.Add(e);}return e;
        }
        void ValidateV14Core(CombatState s)
        {
            if(!V14Enabled){Require(Config.v11?.v14BuildVersion==null&&Config.v11?.v14FinnGrowthAction==null&&s.actors.All(a=>a.v14Core==null)&&s.rolls.All(t=>t.v14Correction==null),"v14_core_state_without_rules");return;}
            Require(Config.v11?.v14BuildVersion==V14BuildCatalog.Version&&Config.v11.v14FinnGrowthAction==Config.v14.finnGrowthAction,"v14_build_snapshot_invalid");
            foreach(var actor in s.actors.Where(a=>a.v14Core!=null))
            {
                var d=actor.v14Core;Require(d.encounters!=null&&d.encounters.All(e=>e!=null&&!string.IsNullOrEmpty(e.encounterId)&&e.settlementRoots!=null&&e.dodgeRoots!=null&&e.settlementRoots.Count<=2&&
                    e.settlementRoots.All(x=>!string.IsNullOrEmpty(x))&&e.dodgeRoots.All(x=>!string.IsNullOrEmpty(x))&&e.settlementRoots.Distinct().Count()==e.settlementRoots.Count&&e.dodgeRoots.Distinct().Count()==e.dodgeRoots.Count)&&
                    d.encounters.Select(e=>e.encounterId).Distinct().Count()==d.encounters.Count,"v14_core_receipts_invalid");
                Require(Finite(d.dodgeUntil)&&d.dodgeUntil>=0&&Finite(d.lastChaseRefund)&&d.lastChaseRefund>=-4&&d.playerHeroRoots!=null&&d.playerHeroRoots.All(x=>!string.IsNullOrEmpty(x))&&d.playerHeroRoots.Distinct().Count()==d.playerHeroRoots.Count,"v14_core_timers_invalid");
            }
            foreach(var ticket in s.rolls.Where(t=>t.v14Correction!=null))
            {
                var r=ticket.v14Correction;Require(r.sourceId==V12Evan&&s.actors.Any(a=>a.id==r.sourceId)&&!string.IsNullOrEmpty(r.encounterId)&&!string.IsNullOrEmpty(r.paymentId)&&
                    (r.method=="plus2"||r.method=="reroll")&&r.originalFaces!=null&&r.originalFaces.Count<=2&&r.originalFaces.All(x=>x>=1&&x<=20)&&
                    (!r.actualEffect||r.beforeRecorded&&!r.beforeSuccess&&r.afterSuccess)&&(!r.settled||r.actualEffect),"v14_correction_receipt_invalid");
            }
        }
        public bool V14FirstCorrectionFree(CombatState s)
        {
            var actor=s.Actor(V12Evan);
            return actor!=null&&s.inCombat&&!s.encounterResolved&&!string.IsNullOrEmpty(s.encounterId)&&V14CoreBuff("v14_evan_firstproof",actor.id)&&
                actor.v14Core?.encounters.Any(e=>e.encounterId==s.encounterId&&e.freeCorrectionUsed)!=true;
        }
        public bool V14CanReview=>V14CoreBuff("v14_evan_review",V12Evan);
        public V14CorrectionQuote QuoteV14Correction(string targetId=null)
        {
            var s=State;var actor=s.Actor(V12Evan);var ticket=s.pendingRoll;var q=new V14CorrectionQuote{free=V14FirstCorrectionFree(s),canReroll=V14CanReview&&ticket!=null};
            if(!V14Enabled||actor?.Alive!=true||!Config.AllowsAction(actor,"v13_evan_correct"))q.reason="ability_locked_or_wrong_character";
            else if((!q.free&&actor.inspiration<=0)||actor.reactionRemaining>0||actor.windupRemaining>0||V12ActionActive(actor)||!string.IsNullOrEmpty(actor.reactionHeldBy))q.reason="correction_resource_or_reaction_unavailable";
            else if(ticket!=null&&(s.mode!=CombatMode.PostRollDecision||ticket.stage!="AwaitDecision"||!s.actors.Any(a=>a.id==ticket.rollOwnerId)||ticket.modifyUsed||ticket.precisionUsed||ticket.rerollUsed||!(ticket.rollType=="Attack"||ticket.rollType=="Defense"||ticket.rollType=="Check")))q.reason="correction_ineligible_roll";
            else if(ticket==null&&(!SimulationMode(s)||s.activePendingEvent!=null||!s.actors.Any(a=>a.id==(targetId??s.activeActorId)&&a.Alive)||Member(s,V12Evan).correctionUntil>s.simulationSeconds))q.reason="correction_party_target_required";
            else q.legal=true;return q;
        }
        void V14BeforeCoreCommand(CombatState s,CombatCommand c)
        {
            if(!V14Enabled)return;
            if(c.kind==CommandKind.Reroll||c.kind==CommandKind.Modify||c.kind==CommandKind.Precision)
                Require(s.pendingRoll!=null&&!s.pendingRoll.modifyUsed&&!s.pendingRoll.rerollUsed&&!s.pendingRoll.precisionUsed,"manual_intervention_already_used");
        }
        bool TryV14Correction(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(!V14Enabled||c.kind!=CommandKind.RequestHeroAction||c.actionId!="v13_evan_correct")return false;
            Require(c.authority==CommandAuthority.Player,"player_order_required");
            var actor=Live(s,V12Evan);Require(c.actorId==actor.id&&Config.AllowsAction(actor,c.actionId),"ability_locked_or_wrong_character");
            bool free=V14FirstCorrectionFree(s);
            Require((free||actor.inspiration>0)&&actor.reactionRemaining<=0&&actor.windupRemaining<=0&&!V12ActionActive(actor)&&string.IsNullOrEmpty(actor.reactionHeldBy),"correction_resource_or_reaction_unavailable");
            bool reroll=c.v14CorrectionChoice=="reroll";
            Require(string.IsNullOrEmpty(c.v14CorrectionChoice)||c.v14CorrectionChoice=="plus2"||reroll,"correction_choice_unknown");
            if(reroll)Require(V14CanReview,"review_not_owned");
            string payment="v14-correction/"+c.id;var ticket=s.pendingRoll;
            if(ticket!=null)
            {
                Require(s.mode==CombatMode.PostRollDecision&&ticket.stage=="AwaitDecision"&&s.actors.Any(a=>a.id==ticket.rollOwnerId)&&
                    !ticket.modifyUsed&&!ticket.precisionUsed&&!ticket.rerollUsed&&(ticket.rollType=="Attack"||ticket.rollType=="Defense"||ticket.rollType=="Check"),"correction_ineligible_roll");
                ticket.v14Correction=new V14CorrectionRecord{sourceId=actor.id,encounterId=s.encounterId,paymentId=payment,method=reroll?"reroll":"plus2",
                    originalFaces=new List<int>(ticket.rawFaces),originalTotal=ticket.total,beforeRecorded=true,beforeSuccess=ticket.hit};
                ticket.modifyUsed=true;ticket.paidResourceIds.Add(payment);
                if(reroll){ticket.rerollUsed=true;Roll(s,ticket);ticket.modifierSources.Add("埃文复核 · 强制采用整组新骰");}
                else{ticket.flatBonus+=2;Evaluate(s,ticket);ticket.modifierSources.Add("埃文校正 +2");}
                ticket.v14Correction.afterSuccess=ticket.hit;result=TicketResult(ticket);
                if(reroll){ticket.stage="Rolling";if(ticket.purpose=="Reaction")s.activeReaction.stage="Rolling";s.mode=CombatMode.RollFreeze;}
            }
            else
            {
                Require(!reroll,"review_requires_revealed_roll");
                Require(SimulationMode(s)&&s.activePendingEvent==null&&s.actors.Any(a=>a.id==c.targetId&&a.Alive),"correction_party_target_required");
                var member=Member(s,actor.id);Require(member.correctionUntil<=s.simulationSeconds,"correction_already_prepared");
                member.correctionTarget=c.targetId;member.correctionUntil=s.simulationSeconds+8;
                V14Core(actor).preparedCorrectionEncounter=s.encounterId;V14Core(actor).preparedCorrectionPayment=payment;result=new CombatResult();
            }
            if(free)V14Encounter(s,actor).freeCorrectionUsed=true;else actor.inspiration--;
            actor.reactionRemaining=Config.reactionRecovery;return true;
        }
        void V14PrepareCorrectionTicket(CombatState s,RollTicket ticket)
        {
            if(!V14Enabled)return;var actor=s.Actor(V12Evan);var data=actor?.v14Core;
            if(data==null||data.preparedCorrectionEncounter!=s.encounterId||string.IsNullOrEmpty(data.preparedCorrectionPayment))return;
            var member=Member(s,V12Evan);if(member==null||member.correctionUntil<=s.simulationSeconds||member.correctionTarget!=ticket.rollOwnerId)return;
            ticket.v14Correction=new V14CorrectionRecord{sourceId=actor.id,encounterId=s.encounterId,paymentId=data.preparedCorrectionPayment,method="plus2"};
            ticket.paidResourceIds.Add(data.preparedCorrectionPayment);data.preparedCorrectionPayment=null;data.preparedCorrectionEncounter=null;
        }
        void V14OnRollEvaluated(CombatState s,RollTicket ticket)
        {
            var r=ticket.v14Correction;if(!V14Enabled||r==null||r.beforeRecorded)return;
            r.originalFaces=new List<int>(ticket.rawFaces);r.originalTotal=ticket.total-2;
            r.beforeSuccess=ticket.rollType=="Attack"?AttackHits(ticket.selectedFace,r.originalTotal,ticket.targetNumber):r.originalTotal>=ticket.targetNumber;
            r.afterSuccess=ticket.hit;r.beforeRecorded=true;
        }
        public void V14CorrectionCommitted(CombatState s,RollTicket ticket,bool actualEffect)
        {
            var r=ticket?.v14Correction;if(!V14Enabled||r==null||r.settled||r.beforeSuccess||!r.afterSuccess||!ticket.hit||!actualEffect)return;
            if(r.encounterId==s.encounterId&&s.inCombat&&V14CoreBuff("v14_evan_settle",r.sourceId))
            {
                r.actualEffect=true;
                // Background chase rolls keep their existing result/command receipt instead
                // of entering the paid tactical-roll history. Their actual effect is already
                // committed here, so use the same persisted encounter/root reward ledger.
                if(!s.rolls.Contains(ticket))V14SettleCorrection(s,ticket);
            }
        }
        void V14SettleCorrection(CombatState s,RollTicket ticket)
        {
            var r=ticket.v14Correction;if(r==null||!r.actualEffect||r.settled)return;r.settled=true;var actor=s.Actor(r.sourceId);
            if(actor==null||r.encounterId!=s.encounterId||!s.inCombat||!V14CoreBuff("v14_evan_settle",actor.id))return;
            string root=ticket.purpose=="Reaction"?s.incomingEvents.Find(x=>x.id==ticket.incomingId)?.rootId??ticket.id:ticket.id;
            var e=V14Encounter(s,actor);if(e.settlementRoots.Count>=2||e.settlementRoots.Contains(root))return;
            e.settlementRoots.Add(root);P6AwardResource(s,root,"v14-settle",30);
            // Existing Focus rewards use this exact root key. Higher same-root rewards
            // have already committed in this command, so settlement never adds to them.
            CreditP6Focus(s,s.encounterId+"/p6-focus/"+root,s.encounterId,"combo",2,s.focusBudget.p6.rules.comboPerEncounter,false);
            V12Emit(s,null,root,actor.id,ticket.targetId,"v13_evan_correct","v14-settle",30);
        }
        void V14AfterCoreCommand(CombatState s)
        {
            if(!V14Enabled)return;
            foreach(var ticket in s.rolls.Where(t=>t.v14Correction!=null&&t.v14Correction.actualEffect&&!t.v14Correction.settled))
                V14SettleCorrection(s,ticket);
        }
        public void V14ObserveMiraDodge(CombatState s,PendingIncoming incoming)
        {
            if(!V14Enabled||!s.inCombat||incoming?.defenderId!=V12Mira||incoming.reason!="avoided_by_dodge"||incoming.baseDamage<=0||
                Config.v11.coreActorId!=V12Mira)return;
            var actor=s.Actor(V12Mira);if(actor?.Alive!=true||!V13Physical(s,actor)||s.Actor(incoming.attackerId)?.isEnemy!=true)return;
            string root=V14RegisteredThreatRoot(s,incoming.rootId);if(root!=null)V14GrantVerifiedAvoid(s,actor,root);
        }
        public void V14RememberHeroCommand(CombatState s,ActorState actor,V12HeroIntent intent,bool playerCommand)
        {if(V14Enabled&&actor?.id==V12Mira&&playerCommand&&!V14Core(actor).playerHeroRoots.Contains(intent.id))V14Core(actor).playerHeroRoots.Add(intent.id);}
        public bool V14MiraPlayerHero(ActorState actor,string root)=>actor?.v14Core?.playerHeroRoots.Contains(root)==true;
        bool V14MiraReady(CombatState s,ActorState actor,bool playerCommand)
        {var data=actor?.v14Core;return V14Enabled&&actor?.id==V12Mira&&Config.v11.coreActorId==V12Mira&&actor.Alive&&playerCommand&&data!=null&&
            s.inCombat&&data.dodgeEncounter==s.encounterId&&data.dodgeUntil>s.simulationSeconds&&!string.IsNullOrEmpty(data.dodgeRoot);}
        bool V14MiraActionEligible(ActorState actor,string action)
        {
            bool press=V14CoreBuff("v14_mira_press",actor.id),opening=V14CoreBuff("v14_mira_opening",actor.id);
            bool breakflow=V14CoreBuff("v14_mira_breakflow",actor.id);
            // The original prepared basic and the new skill counter are one reward.
            // R4 alone does not increase basic damage, but spending its baseline 1.5
            // counter must consume the same opportunity before a later heavy attack.
            if(action==Config.BasicFor(actor).id)return press||opening||breakflow;
            return action=="v12_mira_chase"?(press||opening):action=="v12_mira_break"&&(press||breakflow);
        }
        public float V14MiraDamageMultiplier(CombatState s,ActorState actor,string action,bool playerCommand)
        {
            if(!V14MiraReady(s,actor,playerCommand)||!V14MiraActionEligible(actor,action))return 1;
            if(V14CoreBuff("v14_mira_press",actor.id))return action==Config.BasicFor(actor).id?2:1.5f;
            if(V14CoreBuff("v14_mira_opening",actor.id))return action==Config.BasicFor(actor).id?1.75f:action=="v12_mira_chase"?1.25f:1;
            return 1;
        }
        public void V14MiraConsume(CombatState s,ActorState actor,ActorState target,string root,string action,int actual,bool playerCommand)
        {
            if(actual<=0||!V14MiraReady(s,actor,playerCommand)||!V14MiraActionEligible(actor,action))return;
            var data=actor.v14Core;data.dodgeUntil=0;data.dodgeRoot=null;actor.v12.readinessRemaining=0;
            if(action=="v12_mira_break"&&V14CoreBuff("v14_mira_breakflow",actor.id)&&target?.Alive==true)
            {float prior=target.breakValue;P5ApplyBreak(target,2);AwardFocusBreak(s,target,root,prior);V12Emit(s,null,root,actor.id,target.id,action,"v14-breakflow",2);}
            if(V14CoreBuff("v14_mira_press",actor.id)&&(data.lastChaseEncounter!=s.encounterId||s.simulationSeconds-data.lastChaseRefund>=4-.00001f))
            {actor.v12.basicCooldown=Math.Max(0,actor.v12.basicCooldown-2);data.lastChaseRefund=s.simulationSeconds;data.lastChaseEncounter=s.encounterId;}
            V12Emit(s,null,root,actor.id,target?.id??actor.id,action,"v14-counter",actual);
        }
    }
}
