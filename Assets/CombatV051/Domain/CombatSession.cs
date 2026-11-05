using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class ActionQuote
    {
        public bool legal,advantage,disadvantage;
        public string reason,rollOwnerId,rollType;
        public int targetNumber,bonus,spellCost,itemCost;
        public float range;
        public double successProbability;
        public List<string> sources=new List<string>();
    }
    public sealed partial class CombatSession
    {
        public CombatState State {get;private set;}
        public CombatConfig Config {get;private set;}
        public CombatStore Store {get;private set;}
        readonly Action<CombatState> persistenceSink;
        public float Scale {get{return ScaleFor(State.mode);}}
        public CombatSession(CombatState state,CombatConfig config,string savePath=null,Action<CombatState> persistenceSink=null)
        {
            Require(savePath==null||persistenceSink==null,"store_and_sink_are_exclusive");this.persistenceSink=persistenceSink;
            State=state??throw new ArgumentNullException(nameof(state));Config=config??throw new ArgumentNullException(nameof(config));Validate(State);ValidateConfig(Config);ValidateP4Tactics(State);ValidateP4Economy(State);ValidateP5Configuration();ValidateP5State(State);ValidateP6Configuration();ValidateP6State(State);ValidateV11(State);ValidateV12(State);ValidateV13(State);
            if(savePath!=null)Store=new CombatStore(savePath,state.rulesProfileId);
        }
        public static CombatSession Load(string path,CombatConfig config){var store=new CombatStore(path);var session=new CombatSession(store.Load(),config);session.Store=store;return session;}
        public static CombatSession Load(string path,CombatConfig config,string expectedRulesProfileId){var store=new CombatStore(path,expectedRulesProfileId);var session=new CombatSession(store.Load(),config);session.Store=store;return session;}
        public CombatResult Save()
        {
            try{Validate(State);ValidateP4Tactics(State);ValidateP4Economy(State);ValidateP5State(State);ValidateP6State(State);ValidateV11(State);ValidateV12(State);ValidateV13(State);Persist(State);return new CombatResult{success=true,reason=Store==null&&persistenceSink==null?"memory_only":"saved",revision=State.revision};}
            catch(Exception ex){return Fail("save_failed:"+ex.Message);}
        }
        public CombatResult Execute(CombatCommand command)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.id))return Fail("command_id_required");
            string fingerprint=CombatStore.Digest(CombatJson.Write(command));
            var receipt=State.receipts.Find(r=>r.id==command.id);
            if(receipt!=null){if(receipt.fingerprint!=fingerprint)return Fail("command_payload_mismatch");var replay=receipt.result.Clone();replay.duplicate=true;return replay;}
            if(command.expectedRevision>=0&&command.expectedRevision!=State.revision)return Fail("stale_revision");
            var before=State;var candidate=State.Clone();
            try
            {
                v12Spatial=command.v12;
                CombatResult result;
                using(DomainTiming.Measure("Domain.Apply"))
                {int v12EventStart=candidate.v12?.events.Count??0;result=Apply(candidate,command);V12CleanupSources(candidate);V12CollectResult(candidate,result,v12EventStart);P6AfterCommand(candidate,command);V13AfterCommand(candidate,command,result);ProcessP4ReactionTransitions(candidate);P6CleanupMarks(candidate);CleanupV11Sustain(candidate);candidate.revision++;result.success=true;result.reason=result.reason??"ok";result.revision=candidate.revision;}
                candidate.receipts.Add(new CommandReceipt{id=command.id,fingerprint=fingerprint,result=result.Clone()});
                using(DomainTiming.Measure("Domain.Validate")){Validate(candidate);ValidateP4Tactics(candidate);ValidateP4Economy(candidate);ValidateP5State(candidate);ValidateP6State(candidate);ValidateV11(candidate);ValidateV12(candidate);ValidateV13(candidate);}
                Persist(candidate);State=candidate;return result;
            }
            catch(Exception ex){State=before;return Fail(ex.Message);}
            finally{v12Spatial=null;}
        }
        CombatResult Fail(string reason){return new CombatResult{success=false,reason=reason,revision=State.revision};}
        void Persist(CombatState candidate){if(Store!=null)Store.Save(candidate);else if(persistenceSink!=null)persistenceSink(candidate);}
        static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static float Distance(ActorState a,ActorState b){float x=a.x-b.x,z=a.z-b.z;return(float)Math.Sqrt(x*x+z*z);}
        static ActorState Live(CombatState s,string id){var a=s.Actor(id);Require(a!=null&&a.Alive,"actor_unavailable");return a;}
        static bool NormalMode(CombatState s){return s.mode==CombatMode.RealtimeCombat||s.mode==CombatMode.QueuedExecution;}
        static bool SimulationMode(CombatState s){return s.mode!=CombatMode.RollFreeze&&s.mode!=CombatMode.CombatPaused;}
        static void MajorFree(ActorState a){Require(a.majorRemaining<=0&&string.IsNullOrEmpty(a.heldBy)&&a.staggerRemaining<=0&&a.windupRemaining<=0&&(a.basicAttackIntent==null||a.basicAttackIntent.resolved)&&!P6BindActive(a)&&!V12ActionActive(a),"major_unavailable");}
        static void VoluntaryActionFree(ActorState a){Require(string.IsNullOrEmpty(a.heldBy)&&a.windupRemaining<=0&&a.staggerRemaining<=0&&!P6BindActive(a)&&!V12ActionActive(a),"actor_busy");}
        float ScaleFor(CombatMode mode){if(mode!=CombatMode.CombatPaused&&V13FateBusy(State))return State.v13.fate.stage=="Rolled"?0:.12f;return mode==CombatMode.RollFreeze||mode==CombatMode.CombatPaused?0:State.activePendingEvent!=null?(State.reactionSettings.pauseDecision?0:Config.p6?.focus.worldScale??Config.reactionFocusScale):mode==CombatMode.TacticalFocus||mode==CombatMode.PostRollDecision||mode==CombatMode.ReactionPrompt?Config.p6?.focus.worldScale??Config.focusScale:1;}
        string Id(CombatState s,string kind){return s.modeInstanceId+"/"+s.encounterId+"/"+kind+"/"+(++s.sequence);}
        public ActionQuote Quote(string actorId,string targetId,string actionId)
        {
            try{return QuoteInternal(State,actorId,targetId,actionId,true);}
            catch(Exception ex){return new ActionQuote{legal=false,reason=ex.Message};}
        }
        ActionQuote QuoteInternal(CombatState s,string actorId,string targetId,string actionId,bool checkFree)
        {
            if(V12Enabled&&V12Action(actionId)){var heroTarget=s.Actor(targetId);return V12Quote(s,new CombatCommand{actorId=actorId,targetId=targetId,actionId=actionId,qualified=true,x=heroTarget?.x??0,z=heroTarget?.z??0},checkFree);}
            if(Config.v11!=null&&V11AbilityCatalog.IsCustom(actionId))return V11Quote(s,new CombatCommand{actorId=actorId,targetId=targetId,actionId=actionId},checkFree);
            if(Config.p6!=null&&actionId==P6SmiteId)return P6QuoteSmite(s,new CombatCommand{actorId=actorId,targetId=targetId,actionId=actionId},checkFree);
            if(Config.p5!=null)return P5Quote(s,new CombatCommand{actorId=actorId,targetId=targetId,actionId=actionId},checkFree);
            Require(s.inCombat&&!s.encounterResolved,"not_in_combat");var actor=Live(s,actorId);var target=Live(s,targetId);Require(!actor.isEnemy&&target.isEnemy,"invalid_target_team");
            var spec=Config.Action(actionId);Require(spec!=null,"unknown_action");if(checkFree)MajorFree(actor);Require(!actor.aimPreparing&&actor.guardRemaining<=0&&actor.dodgeRemaining<=0&&!PreparingP4(actor),"actor_busy");
            Require(s.IsPartyP1||Config.actions.Any(a=>a.id==actionId),"action_requires_party_profile");
            if(s.IsPartyP1){var template=Config.Template(actor.role);Require(template!=null&&Config.AllowsAction(actor,actionId),"role_action_forbidden");if(spec.requiresFlanking)Require(IsBehind(actor,target),"sneak_position_required");}
            Require(Distance(actor,target)<=spec.range+.001f,"target_out_of_range");Require(actor.spellSlots-actor.heldSpellSlots>=spec.spellCost&&actor.items-actor.heldItems>=spec.itemCost,"long_term_resource_unavailable");
            var q=new ActionQuote{legal=true,range=spec.range,spellCost=spec.spellCost,itemCost=spec.itemCost,rollType=spec.rollType};
            if(spec.rollType=="Attack")
            {
                q.rollOwnerId=actor.id;q.targetNumber=EffectiveArmorClass(target);q.bonus=actor.attackBonus;
                if(s.IsPartyP1&&actor.role==ActorRole.Rogue&&IsBehind(actor,target)){q.advantage=true;q.sources.Add("Rear position");}
                if(GetP4SetupAdvantage(s,actor,target,"Attack")){q.advantage=true;q.sources.Add("Prepared teamwork");}
                if(spec.ranged&&actor.aimReady){q.advantage=true;q.sources.Add("Aim");}
                if(target.staggerRemaining>0){q.advantage=true;q.sources.Add("Target staggered");}
                if(actor.statuses.Exists(x=>x.id=="flanking"&&x.remaining>0)){q.advantage=true;q.sources.Add("Flanking");}
                if(actor.statuses.Exists(x=>x.id=="suppressed"&&x.remaining>0)){q.disadvantage=true;q.sources.Add("Suppressed");}
            }
            else
            {
                q.rollOwnerId=target.id;q.targetNumber=8+(spec.saveAbility=="Wisdom"?actor.intelligenceModifier:actor.strengthModifier)+actor.proficiency;q.bonus=spec.saveAbility=="Wisdom"?target.wisSaveBonus:target.saveBonus;
                if(target.statuses.Exists(x=>x.id=="save-advantage"&&x.remaining>0)){q.advantage=true;q.sources.Add("Target save advantage");}
                if(target.staggerRemaining>0){q.disadvantage=true;q.sources.Add("Target staggered");}
                if(GetP4SetupAdvantage(s,actor,target,"Save")){q.disadvantage=true;q.sources.Add("Prepared teamwork: enemy save disadvantage");}
            }
            q.successProbability=Probability(q.rollType,q.bonus,q.targetNumber,q.advantage,q.disadvantage);return q;
        }
        public static double Probability(string rollType,int bonus,int target,bool advantage,bool disadvantage)
        {
            int success=0,count=0;for(int a=1;a<=20;a++)for(int b=1;b<=(advantage!=disadvantage?20:1);b++)
            {int selected=advantage==disadvantage?a:advantage?Math.Max(a,b):Math.Min(a,b);bool hit=rollType=="Attack"?AttackHits(selected,selected+bonus,target):rollType=="Check"?selected+bonus>=target:selected+bonus<target;if(hit)success++;count++;}return(double)success/count;
        }
        static bool AttackHits(int natural,int total,int ac){return natural==20||(natural!=1&&total>=ac);}
        void Roll(CombatState s,RollTicket ticket,bool background=false)
        {
            if(ticket.rawFaces.Count==0)V13PrepareRoll(s,ticket);
            var stream=background?s.random.backgroundCombat:s.random.visibleTactical;
            if(background)ticket.modifierSources.Add("后台攻击骰 · 实时预接受");
            ticket.rngBefore=stream.drawCount;ticket.rawFaces.Clear();int first=RandomStreams.D20(stream);ticket.rawFaces.Add(first);
            int selected=first;if(ticket.advantage!=ticket.disadvantage){int second=RandomStreams.D20(stream);ticket.rawFaces.Add(second);selected=ticket.advantage?Math.Max(first,second):Math.Min(first,second);}
            ticket.selectedFace=selected;ticket.rngAfter=stream.drawCount;Evaluate(s,ticket);V14OnRollEvaluated(s,ticket);
        }
        static void Evaluate(CombatState s,RollTicket ticket)
        {
            // Bonuses were snapshotted at ConfirmRoll, not re-read from a changing actor after payment.
            int bonus=ticket.baseBonus;ticket.total=ticket.selectedFace+bonus+ticket.precisionBonus+ticket.flatBonus;
            ticket.critical=ticket.rollType=="Attack"&&ticket.selectedFace==20;
            ticket.hit=ticket.rollType=="Attack"?AttackHits(ticket.selectedFace,ticket.total,ticket.targetNumber):ticket.rollType=="Defense"||ticket.rollType=="Check"?ticket.total>=ticket.targetNumber:ticket.total<ticket.targetNumber;
            P5RefreshDamage(ticket);P6Evaluate(ticket);
        }
        static CombatResult TicketResult(RollTicket t){return new CombatResult{ticketId=t.id,queueId=t.queueId,rawFaces=new List<int>(t.rawFaces),total=t.total,hit=t.hit,critical=t.critical};}
        static RollTicket Pending(CombatState s,CombatCommand c,string stage)
        {var t=s.pendingRoll;Require(t!=null&&(string.IsNullOrEmpty(c.ticketId)||c.ticketId==t.id),"pending_ticket_mismatch");Require(t.stage==stage,"ticket_stage_mismatch");return t;}
        static void Release(CombatState s,RollTicket t,bool costsHeld)
        {
            P6ClearReleasedHold(t);var a=s.Actor(t.actorId);if(a==null)return;if(a.heldBy==t.id)a.heldBy=null;
            if(costsHeld){a.heldSpellSlots-=t.spec.spellCost;a.heldItems-=t.spec.itemCost;}
        }
        CombatResult Apply(CombatState s,CombatCommand c)
        {
            var result=new CombatResult();
            bool systemIncoming=c.authority==CommandAuthority.System&&(c.kind==CommandKind.IncomingBatch||c.kind==CommandKind.IncomingAttack||c.kind==CommandKind.IncomingSave)&&s.IsPartyP1;
            if(s.mode==CombatMode.CombatPaused)Require(c.kind==CommandKind.Resume||systemIncoming,"combat_paused");
            if(s.mode==CombatMode.RollFreeze)Require(c.kind==CommandKind.RevealRoll||c.kind==CommandKind.Pause||systemIncoming,"world_frozen");
            V13BeforeCommand(s,c);ValidateAuthority(s,c);
            CombatResult v13Result;if(TryV13Command(s,c,out v13Result))return v13Result;
            CombatResult v12Result;if(TryV12Command(s,c,out v12Result))return v12Result;
            CombatResult v11Result;if(TryV11Command(s,c,out v11Result))return v11Result;
            CombatResult p6Result;if(TryP6Command(s,c,out p6Result))return p6Result;
            CombatResult p5Result;if(TryP5Command(s,c,out p5Result))return p5Result;
            if(c.kind==CommandKind.AwardFact&&c.actionId=="focus-boss-phase")
            {Require(SimulationMode(s)&&s.inCombat&&!string.IsNullOrEmpty(c.factId),"unqualified_world_fact");return AwardFocusBossPhase(s,c);}
            CombatResult p4Result;if(TryP4Command(s,c,out p4Result))
            {
                if(Config.p6!=null&&c.kind==CommandKind.ResolveBasicAttack&&s.Actor(c.actorId)?.basicAttackIntent?.resolved==true)s.Actor(c.actorId).p6.basicRecovery=true;
                if(c.kind==CommandKind.AwardFact&&c.actionId=="environment"&&!c.refundOrigin&&!c.fromReaction&&s.momentumLedger.registeredObjectives.Contains(c.rootId))
                    AwardFocusBudget(s,c.rootId,"environment",FocusBudgetRules.EnvironmentRecovery);
                return p4Result;
            }
            CombatResult partyResult;if(TryPartyCommand(s,c,out partyResult))return partyResult;
            switch(c.kind)
            {
                case CommandKind.Pause:Require(s.mode!=CombatMode.CombatPaused,"already_paused");s.resumeMode=s.mode;s.mode=CombatMode.CombatPaused;return result;
                case CommandKind.Resume:Require(s.mode==CombatMode.CombatPaused,"not_paused");s.mode=s.resumeMode;return result;
                case CommandKind.EnterFocus:Require(s.inCombat&&NormalMode(s),"cannot_enter_focus");EnterFocusBudget(s);s.mode=CombatMode.TacticalFocus;return result;
                case CommandKind.ExitFocus:Require(s.mode==CombatMode.TacticalFocus&&s.pendingRoll==null&&s.activeReaction==null&&s.decisionResume==null,"finish_pending_roll_first");LeaveFocusBudget(s);s.mode=s.queue.Exists(q=>q.stage=="Queued"||q.stage=="Executing")?CombatMode.QueuedExecution:CombatMode.RealtimeCombat;return result;
                case CommandKind.ConfirmRoll:
                {
                    Require(s.mode==CombatMode.TacticalFocus&&s.pendingRoll==null,"focus_required");Require(s.focusBudget.secondsRemaining>0,"focus_budget_empty");Require(s.charges>0,"no_charge");Require(c.lineOfSight,"line_of_sight_blocked");var q=QuoteInternal(s,c.actorId,c.targetId,c.actionId,true);var actor=Live(s,c.actorId);
                    var t=new RollTicket{id=Id(s,"roll"),actorId=c.actorId,targetId=c.targetId,actionId=c.actionId,spec=CombatJson.Clone(Config.Action(c.actionId)),rollOwnerId=q.rollOwnerId,rollType=q.rollType,advantage=q.advantage,disadvantage=q.disadvantage,modifierSources=q.sources,targetNumber=q.targetNumber,baseBonus=q.bonus,createdRulesVersion=s.rulesVersion};
                    actor.heldBy=t.id;actor.heldSpellSlots+=t.spec.spellCost;actor.heldItems+=t.spec.itemCost;if(t.spec.ranged&&t.rollType=="Attack")actor.aimReady=false;
                    s.charges--;DrainMomentum(s);ConsumeP4Setup(s,actor,s.Actor(c.targetId),t.rollType,t.id,false,t.id);Roll(s,t);s.rolls.Add(t);s.pendingRollId=t.id;s.mode=CombatMode.RollFreeze;result=TicketResult(t);result.chargeSpent=1;return result;
                }
                case CommandKind.RevealRoll:{Require(s.mode==CombatMode.RollFreeze,"roll_freeze_required");var t=Pending(s,c,"Rolling");t.stage="AwaitDecision";if(t.purpose=="Reaction")s.activeReaction.stage="AwaitDecision";else if(s.focusBudget.secondsRemaining<=0)s.focusBudget.revealGraceRemaining=FocusBudgetRules.ZeroBudgetRevealGrace;s.mode=CombatMode.PostRollDecision;return TicketResult(t);}
                case CommandKind.Reroll:
                {
                    Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");var a=Live(s,t.actorId);Require(!t.rerollUsed&&a.inspiration>0,"inspiration_unavailable");
                    Require(t.rollType=="Attack"||t.rollType=="Check"&&t.rollOwnerId==a.id||t.purpose=="Reaction","cannot_modify_enemy_save");a.inspiration--;t.rerollUsed=true;t.paidResourceIds.Add("inspiration/"+c.id);Roll(s,t);t.stage="Rolling";if(t.purpose=="Reaction")s.activeReaction.stage="Rolling";s.mode=CombatMode.RollFreeze;return TicketResult(t);
                }
                case CommandKind.Modify:
                {
                    Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");var a=Live(s,t.actorId);Require(t.rollType=="Attack"||t.rollType=="Check"&&t.rollOwnerId==a.id||t.purpose=="Reaction","cannot_modify_enemy_save");Require(!t.modifyUsed&&a.inspiration>0,"inspiration_unavailable");
                    a.inspiration--;t.modifyUsed=true;t.flatBonus=2;t.paidResourceIds.Add("inspiration-modify/"+c.id);t.modifierSources.Add("Inspiration +2");Evaluate(s,t);return TicketResult(t);
                }
                case CommandKind.Precision:
                {
                    Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");var a=Live(s,t.actorId);Require(t.rollType=="Attack"&&(!s.IsPartyP1||a.role==ActorRole.Fighter),"cannot_modify_enemy_save");Require(!t.precisionUsed&&a.superiority-a.heldSuperiority>0,"superiority_unavailable");
                    a.superiority--;t.precisionUsed=true;var rules=Config.RulesFor(a.id);
                    bool correction=rules.nearMissCorrection&&!a.p4NearMissUsed&&!t.hit&&t.selectedFace!=1&&t.targetNumber-t.total>0&&t.targetNumber-t.total<=rules.nearMissMaximumDeficit;
                    t.precisionBonus=correction?rules.nearMissBonus:RandomStreams.NextInt(s.random.classResource,1,7);if(correction)a.p4NearMissUsed=true;
                    t.paidResourceIds.Add("superiority/"+c.id);t.modifierSources.Add((correction?"Near-miss correction +":"Precision +")+t.precisionBonus);Evaluate(s,t);return TicketResult(t);
                }
                case CommandKind.Cancel:
                {Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");t.stage="Canceled";Release(s,t,true);s.pendingRollId=null;s.mode=CombatMode.TacticalFocus;return TicketResult(t);}
                case CommandKind.Commit:
                {
                    Require(s.mode==CombatMode.PostRollDecision,"post_roll_required");var t=Pending(s,c,"AwaitDecision");Live(s,t.targetId);var a=Live(s,t.actorId);Require(a.heldBy==t.id,"action_hold_lost");
                    var q=new QueuedAction{id=Id(s,"queue"),ticketId=t.id,actorId=t.actorId,targetId=t.targetId,actionId=t.actionId};s.queue.Add(q);t.queueId=q.id;t.stage="Queued";s.pendingRollId=null;s.mode=CombatMode.TacticalFocus;return TicketResult(t);
                }
                case CommandKind.StartQueued:
                {
                    Require(NormalMode(s),"realtime_execution_required");var q=s.queue.Find(x=>x.id==c.queueId);Require(q!=null&&q.stage=="Queued","queue_not_pending");var t=s.rolls.Find(x=>x.id==q.ticketId);var a=s.Actor(q.actorId);var target=s.Actor(q.targetId);
                    if(a==null||!a.Alive||target==null||!target.Alive){CancelQueue(s,q,t,"target_or_actor_unavailable");return new CombatResult{queueId=q.id,reason=q.reason};}
                    Require(a.heldBy==t.id&&a.majorRemaining<=0&&a.staggerRemaining<=0,"held_action_unavailable");Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");Require(Config.p5!=null&&t.actionId=="control"?P5AreaCanStart(s,a,t,c):c.lineOfSight&&Distance(a,target)<=t.spec.range+.001f,"execution_position_illegal");
                    Require(a.spellSlots>=t.spec.spellCost&&a.items>=t.spec.itemCost,"held_resource_lost");a.spellSlots-=t.spec.spellCost;a.items-=t.spec.itemCost;a.heldSpellSlots-=t.spec.spellCost;a.heldItems-=t.spec.itemCost;
                    a.majorRemaining=Config.majorRecovery;a.windupRemaining=t.spec.windup;q.windupRemaining=t.spec.windup;q.resourcesSpent=true;q.stage="Executing";q.executionId=Id(s,"execution");t.stage="Executing";
                    if(Config.p5!=null){a.p5.basicRecovery=false;if(t.actionId=="shove")a.p5.shoveCooldown=Config.p5.shoveCooldown;else if(t.actionId=="sneak")a.p5.sneakCooldown=Config.p5.sneakCooldown;else if(t.actionId=="control")a.p5.thunderCooldown=Config.p5.thunderCooldown;}
                    return new CombatResult{queueId=q.id,executionId=q.executionId};
                }
                case CommandKind.CancelQueued:
                {var q=s.queue.Find(x=>x.id==c.queueId);Require(q!=null&&(q.stage=="Queued"||q.stage=="Executing"),"queue_not_cancelable");CancelQueue(s,q,s.rolls.Find(x=>x.id==q.ticketId),"explicit_cancel");return new CombatResult{queueId=q.id};}
                case CommandKind.ResolveQueued:
                {
                    Require(SimulationMode(s),"world_frozen");var q=s.queue.Find(x=>x.id==c.queueId);Require(q!=null&&q.stage=="Executing"&&!q.effectApplied,"queue_not_executing");Require(q.windupRemaining<=0,"windup_incomplete");var t=s.rolls.Find(x=>x.id==q.ticketId);var a=s.Actor(q.actorId);var target=s.Actor(q.targetId);
                    if(a==null||!a.Alive||target==null||!target.Alive){CancelQueue(s,q,t,"target_or_actor_unavailable");return new CombatResult{queueId=q.id,reason=q.reason};}
                    if(!c.lineOfSight||Distance(a,target)>t.spec.range+.001f){CancelQueue(s,q,t,"execution_target_moved_or_blocked");return new CombatResult{queueId=q.id,reason=q.reason};}
                    result=TicketResult(t);result.executionId=q.executionId;string interruptedThreatRoot=target.attackGroupId;bool dangerousWindup=target.aiState==1&&!string.IsNullOrEmpty(interruptedThreatRoot);float breakBefore=target.breakValue;
                    if(t.hit)
                    {
                        if(t.actionId=="shove")
                        {float dx=target.x-a.x,dz=target.z-a.z;float len=(float)Math.Sqrt(dx*dx+dz*dz);if(len<.001f){dx=1;len=1;}
                            if(c.qualified){Require(Finite(c.x)&&Finite(c.z)&&Math.Sqrt((c.x-target.x)*(c.x-target.x)+(c.z-target.z)*(c.z-target.z))<=t.spec.shoveDistance+.001f,"shove_destination_invalid");target.x=c.x;target.z=c.z;}
                            else{target.x+=dx/len*t.spec.shoveDistance;target.z+=dz/len*t.spec.shoveDistance;}
                            var rules=Config.RulesFor(a.id);int extra=rules.firstShoveBonus&&!a.p4FirstShoveUsed?rules.firstShoveBreakBonus:0;if(extra>0)a.p4FirstShoveUsed=true;
                            target.breakValue=Math.Max(0,target.breakValue-t.spec.breakDamage-extra);target.aiState=0;target.aiRemaining=1;target.attackGroupId=null;if(target.breakValue<=0)target.staggerRemaining=2;}
                        else{result.damage=(t.spec.damage+(t.spec.requiresFlanking&&IsBehind(a,target)?t.spec.conditionalDamage:0))*(t.critical?2:1);target.hp=Math.Max(0,target.hp-result.damage);
                            if(!string.IsNullOrEmpty(t.spec.effectId)&&target.Alive){BreakConcentration(s,a);target.statuses.RemoveAll(x=>x.id==t.spec.effectId);target.statuses.Add(new StatusState{id=t.spec.effectId,sourceId=a.id,concentrationId=q.executionId,remaining=t.spec.effectSeconds});target.aiState=0;target.aiRemaining=0;target.attackGroupId=null;a.concentrationId=q.executionId;}}
                    }
                    ConsumeP4Setup(s,a,target,t.spec.rollType=="Attack"?"Attack":"Save",t.id,t.hit,t.id);
                    if(t.hit){if(result.damage>0)AwardFocusBossPhaseIfReached(s,target,t.id);AwardP4ActionOutcome(s,target,t.id,t.id,dangerousWindup&&target.aiState!=1,!target.Alive,interruptedThreatRoot);AwardFocusBreak(s,target,t.id,breakBefore);}
                    V14CorrectionCommitted(s,t,t.hit&&(result.damage>0||target.breakValue<breakBefore||t.actionId=="shove"||target.statuses.Any(x=>x.concentrationId==q.executionId)));
                    q.effectApplied=true;q.stage="Resolved";t.stage="Resolved";Release(s,t,false);a.windupRemaining=0;return result;
                }
                case CommandKind.BasicAttack:
                {
                    Require(s.inCombat&&SimulationMode(s)&&(IsAI(c)||NormalMode(s)),"basic_attack_not_allowed");var a=Live(s,c.actorId);var target=Live(s,c.targetId);Require(a.isEnemy!=target.isEnemy,"invalid_target_team");MajorFree(a);Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0&&!PreparingP4(a),"actor_busy");var basic=s.IsPartyP1?Config.BasicFor(a):new BasicAttackSpec{range=Config.basicRange,damage=Config.basicDamage};Require(c.lineOfSight&&Distance(a,target)<=basic.range+.001f,"target_out_of_range");
                    a.majorRemaining=Config.majorRecovery;result=Incoming(s,new CombatCommand{id=c.id,kind=CommandKind.IncomingAttack,actorId=a.id,targetId=target.id,amount=basic.damage,rootId=c.id,qualified=true,parryable=!basic.ranged});
                    if(!a.isEnemy)ConsumeP4Setup(s,a,target,"Attack",c.id,result.damage>0);
                    if(result.hit&&!a.isEnemy){AwardP4ActionOutcome(s,target,c.id,null,false,!target.Alive);Award(s,"basic-hit",c.id,c.templateId??"basic-attack",true);}return result;
                }
                case CommandKind.StartAim:{Require(s.inCombat&&NormalMode(s),"realtime_required");var a=Live(s,c.actorId);VoluntaryActionFree(a);Require(!a.aimPreparing&&!a.aimReady&&a.dodgeRemaining<=0&&a.guardRemaining<=0,"aim_unavailable");a.aimPreparing=true;a.aimRemaining=Config.aimSeconds;return result;}
                case CommandKind.CancelAim:{var a=Live(s,c.actorId);a.aimPreparing=false;a.aimRemaining=0;return result;}
                case CommandKind.StartGuard:{Require(NormalMode(s)||s.IsPartyP1&&IsAI(c)&&SimulationMode(s),"realtime_required");var a=Live(s,c.actorId);VoluntaryActionFree(a);Require(!a.aimPreparing&&a.dodgeRemaining<=0&&a.guardRemaining<=0&&a.bonusRemaining<=0,"guard_unavailable");a.guardRemaining=Config.guardSeconds;a.guardElapsed=0;a.bonusRemaining=Config.bonusRecovery;return result;}
                case CommandKind.EndGuard:{var a=Live(s,c.actorId);a.guardRemaining=0;return result;}
                case CommandKind.StartDodge:
                {
                    Require(NormalMode(s)||s.IsPartyP1&&IsAI(c)&&SimulationMode(s),"realtime_required");var a=Live(s,c.actorId);VoluntaryActionFree(a);Require(!a.aimPreparing&&a.dodgeCooldown<=0&&a.dodgeRemaining<=0,"dodge_unavailable");Require(Finite(c.x)&&Finite(c.z),"invalid_dodge_direction");
                    double length=Math.Sqrt((double)c.x*c.x+(double)c.z*c.z);a.lockedX=length<.0001?0:(float)(c.x/length);a.lockedZ=length<.0001?1:(float)(c.z/length);
                    a.guardRemaining=0;a.dodgeRemaining=Config.dodgeSeconds;a.dodgeCooldown=V12DodgeRecovery();return result;
                }
                case CommandKind.AwardFact:{Require(s.inCombat&&SimulationMode(s),"world_frozen");Require(c.qualified&&!string.IsNullOrEmpty(c.factId)&&!string.IsNullOrEmpty(c.rootId),"unqualified_world_fact");var a=Live(s,c.actorId);Require(!a.isEnemy,"momentum_owner_invalid");if(c.actionId=="focus-boss-phase")return AwardFocusBossPhase(s,c);if(c.actionId=="guard"||c.actionId=="parry")Require(a.guardRemaining>0,"guard_not_active");Award(s,c.actionId,c.rootId,c.templateId??c.actionId,true);if(c.actionId=="environment"&&c.authority==CommandAuthority.System&&!c.refundOrigin&&!c.fromReaction&&s.momentumLedger.registeredObjectives.Contains(c.rootId))AwardFocusBudget(s,c.rootId,"environment",FocusBudgetRules.EnvironmentRecovery);return result;}
                case CommandKind.IncomingAttack:case CommandKind.IncomingSave:return Incoming(s,c);
                case CommandKind.EndEncounter:Require(s.inCombat,"encounter_already_ended");Require(!V12Enabled||s.v12?.beam?.stage!="Warning","finish_environment_first");Require(!s.incomingEvents.Exists(e=>!e.damageApplied)&&s.activePendingEvent==null&&s.deferredIncomingBatches.Count==0,"finish_incoming_first");Require(s.enemies.TrueForAll(a=>!a.Alive)||!V13PartyAlive(s),"encounter_not_resolved");Require(s.pendingRoll==null,"finish_pending_roll_first");foreach(var q in s.queue.Where(q=>q.stage=="Queued"||q.stage=="Executing"))CancelQueue(s,q,s.rolls.Find(t=>t.id==q.ticketId),"encounter_ended");if(s.p5!=null)foreach(var opportunity in s.p5.opportunities.Where(x=>!x.consumed)){opportunity.remaining=0;opportunity.p5.status="Expired";}V11EncounterCleared(s);V13ClearEncounter(s);ResetV12EncounterState(s);LeaveFocusBudget(s);s.inCombat=false;s.encounterResolved=true;s.momentum=0;s.mode=CombatMode.RealtimeCombat;if(Config.p6!=null)AwardP6FocusRecovery(s,"formal-clear/"+s.encounterId,"clear");return result;
                default:throw new InvalidOperationException("unsupported_command");
            }
        }
        static void CancelQueue(CombatState s,QueuedAction q,RollTicket t,string reason){Release(s,t,!q.resourcesSpent);q.stage="Canceled";q.reason=reason;t.stage="Canceled";var a=s.Actor(q.actorId);if(a!=null)a.windupRemaining=0;}
        public CombatResult ResolveIncoming(string commandId,string attackerId,string defenderId,int damage,bool savingThrow=false,int dc=12,string rootId=null,bool parryable=true)
        {return Execute(new CombatCommand{id=commandId,kind=savingThrow?CommandKind.IncomingSave:CommandKind.IncomingAttack,actorId=attackerId,targetId=defenderId,amount=damage,dc=dc,rootId=rootId??commandId,qualified=true,authority=CommandAuthority.System,parryable=parryable});}
        CombatResult Incoming(CombatState s,CombatCommand c)
        {
            if(s.IsPartyP1)return RecordIncoming(s,c);
            Require(s.inCombat&&SimulationMode(s),"world_frozen");var source=Live(s,c.actorId);var target=Live(s,c.targetId);Require(source.isEnemy!=target.isEnemy&&c.qualified&&c.amount>=0&&c.amount<=10000&&!string.IsNullOrEmpty(c.rootId),"invalid_incoming_fact");
            string root=c.rootId+"/"+target.id;Require(!s.incomingRoots.Contains(root),"incoming_already_resolved");s.incomingRoots.Add(root);var r=new CombatResult();
            if(target.dodgeRemaining>0){r.reason="avoided_by_dodge";if(!target.isEnemy)Award(s,"perfect-dodge",c.rootId,"enemy/"+source.id,true);return r;}
            if(target.guardRemaining>0&&target.guardElapsed<=Config.parrySeconds){r.reason="parried";if(!target.isEnemy)Award(s,"parry",c.rootId,"enemy/"+source.id,true);source.breakValue=Math.Max(0,source.breakValue-1);if(source.breakValue<=0)source.staggerRemaining=2;return r;}
            int natural=RandomStreams.D20(s.random.backgroundCombat);r.rawFaces.Add(natural);bool save=c.kind==CommandKind.IncomingSave;r.total=natural+(save?target.saveBonus:source.attackBonus);r.critical=!save&&natural==20;
            bool savePassed=save&&r.total>=c.dc;r.hit=save||AttackHits(natural,r.total,target.armorClass);int amount=(int)c.amount;
            if(r.hit){r.damage=savePassed?amount/2:amount*(r.critical?2:1);if(target.guardRemaining>0){r.damage=(r.damage+1)/2;if(!target.isEnemy)Award(s,"guard",c.rootId,"enemy/"+source.id,true);}target.hp=Math.Max(0,target.hp-r.damage);if(r.damage>0){target.aimPreparing=false;target.aimRemaining=0;target.aimReady=false;}}r.reason=save?(savePassed?"save_success_half":"save_failed"):r.hit?"hit":"defended";return r;
        }
        public float Tick(float realDelta)
        {
            return Tick(realDelta,true);
        }
        float TickCore(float realDelta)
        {
            Require(!float.IsNaN(realDelta)&&!float.IsInfinity(realDelta)&&realDelta>=0,"invalid_delta");if(State.inCombat&&State.mode!=CombatMode.CombatPaused)State.realCombatSeconds+=realDelta;float dt=realDelta*Scale;if(dt==0)return 0;
            State.simulationSeconds+=dt;
            CancelP4InterruptedPreparation(State);CancelP6InterruptedCasts(State);
            foreach(var a in State.actors.Concat(State.enemies))
            {
                a.majorRemaining=Down(a.majorRemaining,dt);a.bonusRemaining=Down(a.bonusRemaining,dt);a.reactionRemaining=Down(a.reactionRemaining,dt);a.dodgeRemaining=Down(a.dodgeRemaining,dt);a.dodgeCooldown=Down(a.dodgeCooldown,dt);a.windupRemaining=Down(a.windupRemaining,dt);
                if(a.guardRemaining>0)a.guardElapsed+=dt;a.guardRemaining=Down(a.guardRemaining,dt);
                if(a.staggerRemaining>0){a.staggerRemaining=Down(a.staggerRemaining,dt);if(a.staggerRemaining==0)a.breakValue=a.breakMaximum;}
                foreach(var status in a.statuses)status.remaining=Down(status.remaining,dt);a.statuses.RemoveAll(x=>x.remaining<=0&&x.v12==null);
                if(a.aimPreparing&&a.Alive){a.aimRemaining=Down(a.aimRemaining,dt);if(a.aimRemaining==0){a.aimPreparing=false;a.aimReady=true;}}
            }
            if(Config.p5==null)TickP4State(State,dt);else TickP5State(State,dt);TickP6State(State,dt);TickV11(State,dt);TickV12(State,dt);TickV13(State,dt);
            foreach(var q in State.queue)if(q.stage=="Executing")q.windupRemaining=Down(q.windupRemaining,dt);
            if(State.IsPartyP1)CleanupConcentration(State);
            if(Config.p6==null)TickP4Economy(State,dt);else TickP6Economy(State,dt);
            State.revision++;return dt;
        }
        static float Down(float value,float delta){return Math.Max(0,value-delta);}
        bool HasThreat(CombatState s){return HasP4Threat(s);}
        void Award(CombatState s,string kind,string root,string template,bool qualified){AwardP4(s,kind,root,template,qualified);}
        float AddMomentum(CombatState s,float amount){return AddMomentumP4(s,amount);}
        void DrainMomentum(CombatState s){DrainMomentumP4(s);}
        static void ValidateConfig(CombatConfig c)
        {ValidateP4Configuration(c);ValidateP4ReactionConfiguration(c);Require((c.focusScale==0||c.focusScale>=.10f&&c.focusScale<=.15f)&&c.majorRecovery>0&&c.aimSeconds>0&&c.guardSeconds>0&&c.passiveMomentumPerSecond>=0&&c.passiveEncounterCap>=0&&c.totalEncounterMomentumCap>=0&&c.effectiveCombatRange>0,"invalid_config");Require(c.actions!=null&&c.actions.Count>0,"missing_actions");foreach(var a in c.actions)Require(!string.IsNullOrEmpty(a.id)&&(a.rollType=="Attack"||a.rollType=="Save")&&a.range>0&&a.windup>=0&&a.spellCost>=0&&a.itemCost>=0,"invalid_action_spec");}
        public static void Validate(CombatState s)
        {
            if(s!=null)ValidateFocusBudget(s);
            Require(s!=null&&((s.rulesProfileId=="p0-single"&&s.schemaVersion==2&&s.contentVersion=="actioncrpg-v0.5.1-p0"&&s.rulesVersion=="v051-p0-1")||(s.rulesProfileId=="p1-party"&&s.schemaVersion==3&&s.contentVersion=="actioncrpg-v0.5.1-p1"&&s.rulesVersion=="v051-p1-1")),"unsupported_state_version");
            Require(!string.IsNullOrEmpty(s.profileId)&&!string.IsNullOrEmpty(s.modeInstanceId)&&!string.IsNullOrEmpty(s.encounterId),"missing_identity");Require(Enum.IsDefined(typeof(CombatMode),s.mode)&&Enum.IsDefined(typeof(CombatMode),s.resumeMode),"invalid_mode");
            Require(s.chargeCap>=2&&s.chargeCap<=3&&s.charges>=0&&s.charges<=s.chargeCap&&Finite(s.momentum)&&s.momentum>=0&&s.momentum<=100,"charge_momentum_invalid");
            Require(s.actors!=null&&s.actors.Count>=1&&s.actors.Count<=3&&s.enemies!=null&&s.rolls!=null&&s.queue!=null&&s.receipts!=null&&s.world!=null&&s.random!=null&&s.momentumLedger!=null&&s.world.numbers!=null&&s.world.strings!=null&&s.world.telegraphs!=null&&s.world.projectiles!=null,"missing_state_domain");var ids=new HashSet<string>();
            foreach(var a in s.actors.Concat(s.enemies))
            {Require(a!=null&&!string.IsNullOrEmpty(a.id)&&ids.Add(a.id),"actor_id_invalid");Require(a.maxHp>0&&a.hp>=0&&a.hp<=a.maxHp&&Finite(a.x)&&Finite(a.z)&&Finite(a.heightY),"actor_health_position_invalid");Require(a.inspiration>=0&&a.superiority>=0&&a.spellSlots>=0&&a.items>=0&&a.heldSpellSlots>=0&&a.heldItems>=0&&a.heldSpellSlots<=a.spellSlots&&a.heldItems<=a.items,"actor_resource_invalid");Require(Finite(a.majorRemaining)&&a.majorRemaining>=0&&Finite(a.aiRemaining)&&Finite(a.windupRemaining),"actor_timer_invalid");if(!string.IsNullOrEmpty(a.heldBy))Require(s.rolls.Exists(t=>t.id==a.heldBy&&t.actorId==a.id&&t.purpose=="Tactical"&&(t.stage=="Rolling"||t.stage=="AwaitDecision"||t.stage=="SuspendedAwaitDecision"||t.stage=="Queued"||t.stage=="Executing")),"orphan_action_hold");}
            Require(s.actors.All(a=>!a.isEnemy)&&s.enemies.All(a=>a.isEnemy)&&s.actors.Exists(a=>a.id==s.activeActorId),"party_ownership_invalid");
            var rollIds=new HashSet<string>();foreach(var t in s.rolls){Require(t!=null&&!string.IsNullOrEmpty(t.id)&&rollIds.Add(t.id)&&t.spec!=null&&ids.Contains(t.actorId)&&ids.Contains(t.targetId)&&t.rawFaces!=null&&(t.rawFaces.Count==1||t.rawFaces.Count==2)&&t.rawFaces.All(x=>x>=1&&x<=20)&&t.selectedFace>=1&&t.selectedFace<=20,"roll_ticket_invalid");Require(new[]{"Rolling","AwaitDecision","SuspendedAwaitDecision","Queued","Executing","Resolved","Canceled"}.Contains(t.stage)&&((t.purpose=="Tactical"&&t.chargeSpent==1&&t.chargePolicy=="SharedCharge")||(s.IsPartyP1&&t.purpose=="Reaction"&&t.chargeSpent==0&&t.chargePolicy=="None"))&&t.actionId==t.spec.id&&t.rollType==t.spec.rollType,"roll_contract_invalid");}
            if(!string.IsNullOrEmpty(s.pendingRollId))Require(s.pendingRoll!=null&&(s.pendingRoll.stage=="Rolling"||s.pendingRoll.stage=="AwaitDecision"),"pending_ticket_invalid");
            CombatMode effective=s.mode==CombatMode.CombatPaused?s.resumeMode:s.mode;if(effective==CombatMode.RollFreeze)Require(s.pendingRoll!=null&&s.pendingRoll.stage=="Rolling","freeze_without_roll");if(effective==CombatMode.PostRollDecision)Require(s.pendingRoll!=null&&s.pendingRoll.stage=="AwaitDecision","decision_without_roll");
            var queueIds=new HashSet<string>();foreach(var q in s.queue)Require(q!=null&&!string.IsNullOrEmpty(q.id)&&queueIds.Add(q.id)&&s.rolls.Exists(t=>t.id==q.ticketId&&t.actorId==q.actorId&&t.targetId==q.targetId&&t.queueId==q.id)&&Finite(q.windupRemaining)&&q.windupRemaining>=0&&new[]{"Queued","Executing","Resolved","Canceled"}.Contains(q.stage),"queue_invalid");Require(s.queue.Select(q=>q.ticketId).Distinct().Count()==s.queue.Count,"duplicate_ticket_queue");
            foreach(var a in s.actors.Concat(s.enemies))
            {
                var unpaid=s.rolls.Where(t=>t.purpose=="Tactical"&&t.actorId==a.id&&(t.stage=="Rolling"||t.stage=="AwaitDecision"||t.stage=="SuspendedAwaitDecision"||t.stage=="Queued")).ToList();
                var heldWindow=s.reactionWindows.Find(w=>w.id==a.reactionHeldBy);
                Require(a.heldSpellSlots==unpaid.Sum(t=>t.spec.spellCost)+(heldWindow==null?0:heldWindow.spec.spellCost)&&a.heldItems==unpaid.Sum(t=>t.spec.itemCost),"resource_hold_mismatch");
                var active=s.rolls.Where(t=>t.purpose=="Tactical"&&t.actorId==a.id&&(t.stage=="Rolling"||t.stage=="AwaitDecision"||t.stage=="SuspendedAwaitDecision"||t.stage=="Queued"||t.stage=="Executing")).ToList();Require(active.Count<=1&&(active.Count==0?string.IsNullOrEmpty(a.heldBy):a.heldBy==active[0].id),"action_hold_mismatch");
            }
            Require(Finite(s.simulationSeconds)&&s.simulationSeconds>=0&&Finite(s.realCombatSeconds)&&s.realCombatSeconds>=0&&s.world.numbers.Values.All(Finite),"world_time_or_number_invalid");
            foreach(var t in s.world.telegraphs)Require(t!=null&&Finite(t.x)&&Finite(t.z)&&Finite(t.remaining)&&Finite(t.radius)&&t.radius>=0,"telegraph_invalid");
            foreach(var p in s.world.projectiles)Require(p!=null&&Finite(p.x)&&Finite(p.z)&&Finite(p.remaining)&&Finite(p.speed),"projectile_invalid");
            Require(s.random.streamVersion=="v051-streams-1","invalid_stream_version");foreach(var r in new[]{s.random.layout,s.random.contentReward,s.random.visibleTactical,s.random.backgroundCombat,s.random.classResource,s.random.eventDice,s.random.cosmetic})Require(r!=null&&r.algorithmVersion=="pcg32-v1"&&(r.increment&1UL)==1,"invalid_random_stream");
            Require(s.receipts.All(r=>r!=null&&!string.IsNullOrEmpty(r.id)&&r.result!=null)&&s.receipts.Select(r=>r.id).Distinct().Count()==s.receipts.Count,"receipt_invalid");
            ValidatePartyState(s);
        }
        static bool Finite(float x){return !float.IsNaN(x)&&!float.IsInfinity(x);}
    }
}
