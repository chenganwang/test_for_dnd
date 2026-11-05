using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        static SafeEventState FindEvent(ModeState mode,string id){return mode.events.Find(e=>e.id==id||e.templateId==id);}
        public EventQuote QuoteEvent(string eventId,string actorId)
        {
            try{return QuoteEventInternal(ActiveMode,eventId,actorId,ActiveCombat.State);}
            catch(Exception ex){return new EventQuote{legal=false,reason=ex.Message,eventId=eventId,actorId=actorId};}
        }
        EventQuote QuoteEventInternal(ModeState mode,string eventId,string actorId,CombatState combat)
        {
            EnsureSafe(mode,true);var e=FindEvent(mode,eventId);Require(e!=null&&e.roomId==mode.currentRoomId,"event_not_in_current_room");Require(e.stage!="Committed","event_already_committed");var template=ConfigFor(mode).Event(e.templateId);
            var actor=combat.Actor(actorId);Require(actor!=null&&(actor.Alive||e.rollCount>0)&&!actor.isEnemy,"event_actor_unavailable");Require(Near(combat.Actor(combat.activeActorId),template.x,template.z,template.range),"event_out_of_reach");
            if(e.rollCount>0)Require(e.actorId==actorId,"event_actor_locked");
            int bonus=e.rollCount>0?e.bonus:Ability(actor,template.ability)+actor.proficiency+template.preparation;
            bool advantage=e.rollCount>0?e.advantage:template.advantage,disadvantage=e.rollCount>0?e.disadvantage:template.disadvantage;int dc=e.rollCount>0?e.dc:template.dc;
            int success=0,count=0;for(int a=1;a<=20;a++)for(int b=1;b<=(advantage!=disadvantage?20:1);b++){int face=advantage==disadvantage?a:advantage?Math.Max(a,b):Math.Min(a,b);if(face+bonus>=dc)success++;count++;}
            return new EventQuote{legal=true,eventId=e.id,actorId=actorId,ability=template.ability,dc=dc,bonus=bonus,advantage=advantage,disadvantage=disadvantage,successProbability=(double)success/count};
        }
        static int Ability(ActorState actor,string ability)
        {switch(ability){case"Strength":return actor.strengthModifier;case"Dexterity":return actor.dexterityModifier;case"Intelligence":return actor.intelligenceModifier;case"Wisdom":return actor.wisdomModifier;case"Charisma":return actor.charismaModifier;case"Constitution":return actor.constitutionModifier;default:throw new InvalidOperationException("unknown_event_ability");}}
        ProfileResult ApplyEvent(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;EnsureSafe(mode,true);EnsureRewardFinished(mode);var e=FindEvent(mode,c.eventId??mode.activeEventId);Require(e!=null,"unknown_event");var combat=Current(mode);
            if(c.kind==ProfileCommandKind.OpenEvent)
            {
                Require(string.IsNullOrEmpty(mode.activeEventId)||mode.activeEventId==e.id,"another_event_active");
                string actor=e.rollCount>0?e.actorId:c.actorId;var quote=QuoteEventInternal(mode,e.id,actor,combat);
                if(e.rollCount==0){e.stage="Quoted";e.actorId=actor;e.bonus=quote.bonus;e.dc=quote.dc;e.advantage=quote.advantage;e.disadvantage=quote.disadvantage;}
                mode.activeEventId=e.id;combat.resumeMode=CombatMode.RealtimeCombat;combat.mode=CombatMode.CombatPaused;
            }
            else
            {
                Require(mode.activeEventId==e.id,"event_not_open");
                switch(c.kind)
                {
                    case ProfileCommandKind.RollEvent:
                    {
                        Require(e.stage=="Quoted"&&e.rollCount==0,"event_initial_roll_used");var quote=QuoteEventInternal(mode,e.id,c.actorId,combat);e.actorId=c.actorId;e.bonus=quote.bonus;e.dc=quote.dc;e.advantage=quote.advantage;e.disadvantage=quote.disadvantage;RollEvent(mode,e);break;
                    }
                    case ProfileCommandKind.RerollEvent:
                    {
                        Require(e.stage=="AwaitDecision"&&!e.success&&!e.rerollUsed&&e.rollCount==1,"event_reroll_unavailable");Require(c.actorId==e.actorId,"event_actor_locked");var actor=combat.Actor(e.actorId);Require(actor!=null&&actor.Alive&&actor.inspiration>0,"inspiration_unavailable");actor.inspiration--;e.rerollUsed=true;RollEvent(mode,e);break;
                    }
                    case ProfileCommandKind.ConfirmEvent:
                        Require(e.stage=="AwaitDecision","event_result_required");if(e.success)AddOnce(mode.flags,e.worldEffect);ApplyV12EventEffect(mode,e,false);e.stage="Committed";CloseSafeEvent(mode,combat);ApplyV12CommittedExploration(mode,e);break;
                    case ProfileCommandKind.UseEventAlternative:
                        Require(e.stage=="Quoted"||e.stage=="AwaitDecision","event_alternative_unavailable");e.usedAlternative=true;ApplyV12EventEffect(mode,e,e.templateId=="V12_READ"&&e.rollCount==0);e.stage="Committed";CloseSafeEvent(mode,combat);ApplyV12CommittedExploration(mode,e);break;
                    case ProfileCommandKind.CloseEvent:CloseSafeEvent(mode,combat);break;
                    default:throw new InvalidOperationException("unsupported_event_command");
                }
            }
            return new ProfileResult{eventId=e.id};
        }
        static void CloseSafeEvent(ModeState mode,CombatState combat){mode.activeEventId=null;combat.mode=CombatMode.RealtimeCombat;combat.resumeMode=CombatMode.RealtimeCombat;}
        static void RollEvent(ModeState mode,SafeEventState e)
        {
            e.rawFaces.Clear();int face=RandomStreams.D20(mode.random.eventDice);e.rawFaces.Add(face);
            if(e.advantage!=e.disadvantage){int second=RandomStreams.D20(mode.random.eventDice);e.rawFaces.Add(second);face=e.advantage?Math.Max(face,second):Math.Min(face,second);}
            e.selectedFace=face;e.total=face+e.bonus;e.success=e.total>=e.dc;e.rollCount++;e.stage="AwaitDecision";
        }
        ProfileResult Rest(ProfileState s,ProfileCommand c)
        {
            if(HasV12Map(s.ActiveMode))return V12Rest(s,c);
            var mode=s.ActiveMode;EnsureSafe(mode);EnsureRewardFinished(mode);Require(mode.Room(mode.currentRoomId).templateId=="R6"&&c.qualified,"rest_location_required");Require(!mode.usedRestIds.Contains("REST_R6"),"rest_already_used");var combat=Current(mode);Require(Near(combat.Actor(combat.activeActorId),0,22,6),"rest_out_of_reach");
            if(c.selectionId=="heal")
            {
                Require(combat.actors.Any(a=>a.hp<a.maxHp),"nothing_to_heal");foreach(var actor in combat.actors)actor.hp=Math.Min(actor.maxHp,actor.hp+Math.Max(1,(int)Math.Ceiling(actor.maxHp*Config.restHealFraction)));
            }
            else
            {
                Require(c.selectionId=="resource","unknown_rest_choice");var actor=combat.Actor(c.actorId);Require(actor!=null&&!actor.isEnemy&&actor.Alive,"rest_actor_unavailable");var template=BuildConfig(mode).Template(actor.role);
                switch(actor.role){case ActorRole.Fighter:Require(actor.superiority<template.superiority,"resource_already_full");actor.superiority++;break;case ActorRole.Mage:Require(actor.spellSlots<template.spellSlots,"resource_already_full");actor.spellSlots++;break;case ActorRole.Paladin:case ActorRole.Rogue:Require(actor.inspiration<template.inspiration,"resource_already_full");actor.inspiration++;break;default:throw new InvalidOperationException("invalid_party_role");}
            }
            mode.usedRestIds.Add("REST_R6");return new ProfileResult();
        }
        ProfileResult TrainParty(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;EnsureSafe(mode);EnsureRewardFinished(mode);
            Require(mode.kind==ProfileModeKind.Story&&mode.Room(mode.currentRoomId).templateId=="R0","training_story_entry_required");
            Require(s.meta.trainingLevel==0,"training_already_complete");Require(s.meta.research>=3,"research_unavailable");
            var combat=Current(mode);Require(Near(combat.Actor(combat.activeActorId),0,0,6),"training_out_of_reach");
            s.meta.research-=3;s.meta.trainingLevel=1;foreach(var actor in combat.actors){actor.maxHp+=2;actor.hp+=2;}
            return new ProfileResult();
        }
        void CreateReward(ModeState mode,RoomState room)
        {
            string id=room.id+"/reward";if(mode.claimedRewardIds.Contains(id))return;
            var pool=Config.rules.Where(r=>r.kind=="Relic"&&!mode.build.relicSlots.Contains(r.id)).Select(r=>r.id).ToList();
            for(int i=pool.Count-1;i>0;i--){int j=RandomStreams.NextInt(mode.random.contentReward,0,i+1);string temp=pool[i];pool[i]=pool[j];pool[j]=temp;}
            mode.pendingReward=new RewardOffer{id=id,roomId=room.id,candidates=pool.Take(3).ToList()};
        }
        ProfileResult Reward(ProfileState s,ProfileCommand c)
        {
            if(s.ActiveMode.v11Departure!=null)return V11Reward(s,c);
            var mode=s.ActiveMode;EnsureSafe(mode);var offer=mode.pendingReward;Require(offer!=null&&offer.stage=="Offered"&&(string.IsNullOrEmpty(c.rewardId)||c.rewardId==offer.id),"reward_not_available");
            if(c.kind==ProfileCommandKind.ClaimReward)
            {
                Require(offer.candidates.Contains(c.selectionId)&&!mode.build.relicSlots.Contains(c.selectionId),"invalid_reward_choice");Require(c.replacementSlot>=-1&&c.replacementSlot<2,"invalid_replacement_slot");int slot=c.replacementSlot>=0?c.replacementSlot:mode.build.relicSlots.FindIndex(x=>x==null);Require(slot>=0,"explicit_replacement_required");mode.build.relicSlots[slot]=c.selectionId;offer.chosenId=c.selectionId;offer.stage="Claimed";
            }
            else offer.stage="Skipped";
            mode.explorationCombat.focusBudget.recoveryMultiplier=mode.build.relicSlots.Contains("FOCUS")?FocusBudgetRules.FocusRelicMultiplier:1;
            AddOnce(mode.claimedRewardIds,offer.id);return new ProfileResult{rewardId=offer.id};
        }
        ProfileResult Settle(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;bool victory=c.selectionId=="victory",defeat=c.selectionId=="defeat",abandon=c.selectionId=="abandon";Require(victory||defeat||abandon,"settlement_kind_required");
            if(s.meta.settledRunIds.Contains(mode.id))return new ProfileResult{settlementId=mode.id,reason="already_settled"};
            if(victory){EnsureSafe(mode);EnsureRewardFinished(mode);Require(mode.Room(HasV12Map(mode)?"V12_R6":"R7").resolved,"boss_not_resolved");}
            else{Require(mode.kind==ProfileModeKind.Challenge,"story_retry_required");if(defeat){var combat=Current(mode);Require(!CombatSession.V13PartyAlive(combat)&&combat.incomingEvents.All(e=>e.damageApplied),"party_not_defeated");}}
            int earned=mode.kind==ProfileModeKind.Challenge?mode.runEarnings+(victory?3:0):0;s.meta.research+=earned;s.meta.settledRunIds.Add(mode.id);s.meta.lastSettlement=new SettlementState{runId=mode.id,kind=mode.kind,victory=victory,earned=earned,elapsedRealSeconds=mode.elapsedRealSeconds};
            if(mode.kind==ProfileModeKind.Challenge)
            {
                var active=Encounter(mode);if(active!=null){active.stage="Abandoned";active.currentCombat=null;active.encounterStartSnapshot=null;mode.activeEncounterId=null;}
                mode.activeEventId=null;mode.explorationCombat.mode=CombatMode.RealtimeCombat;mode.stage="Settled";if(mode.pendingReward!=null&&mode.pendingReward.stage=="Offered"){mode.pendingReward.stage="Skipped";AddOnce(mode.claimedRewardIds,mode.pendingReward.id);}s.activeMode=ProfileModeKind.Story;
            }
            else AddOnce(mode.flags,"STORY_COMPLETE");
            return new ProfileResult{settlementId=mode.id};
        }
    }
}
