using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    // Presentation-only reducer. Inputs contain observations, never mutable gameplay state.
    public sealed class TutorialService
    {
        public TutorialState State {get;private set;}
        public TutorialDefinition Definition {get;private set;}
        public TutorialStore Store {get;private set;}
        public string LastSaveError {get;private set;}
        public string LoadWarning {get;private set;}
        TutorialContext context=new TutorialContext();
        public TutorialService(TutorialState state,TutorialDefinition definition,string path=null)
        {
            Definition=CombatJson.Clone(definition??throw new ArgumentNullException(nameof(definition)));ValidateDefinition();
            State=CombatJson.Clone(state??throw new ArgumentNullException(nameof(state)));Validate(State);if(path!=null)Store=new TutorialStore(path,Validate);
        }
        public static TutorialService Create(TutorialDefinition definition,string profileInstanceId,string storyModeId,string path=null)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            var state=new TutorialState{definitionId=definition.id,definitionVersion=definition.version,profileInstanceId=profileInstanceId,storyModeId=storyModeId,steps=definition.steps.Select(s=>new TutorialStepProgress{id=s.id}).ToList()};
            return new TutorialService(state,definition,path);
        }
        public static TutorialService LoadOrCreate(string path,TutorialDefinition definition,string profileInstanceId,string storyModeId,TutorialContext context=null)
        {
            var service=Create(definition,profileInstanceId,storyModeId,path);
            if(service.Store!=null)
            {
                try
                {
                    var loaded=service.Store.Load();Require(loaded.profileInstanceId==profileInstanceId&&loaded.storyModeId==storyModeId,"tutorial_identity_mismatch");service.State=loaded;
                }
                catch(Exception ex){service.LoadWarning=ex.Message;}
            }
            service.context=CopyContext(context);var candidate=CombatJson.Clone(service.State);if(context!=null)service.ReconcileCandidate(candidate,service.context);
            // Reconciliation is presentation recovery; it neither writes nor executes gameplay.
            service.State=candidate;return service;
        }
        static TutorialContext CopyContext(TutorialContext value){return value==null?new TutorialContext():CombatJson.Clone(value);}
        public TutorialResult Observe(string factKey,float value=1,string sourceId=null,bool accepted=true,bool duplicate=false,bool playerInitiated=true)
        {return Observe(new TutorialObservation{factKey=factKey,value=value,sourceId=sourceId,accepted=accepted,duplicate=duplicate,playerInitiated=playerInitiated});}
        public TutorialResult Observe(TutorialObservation observation){return ObserveMany(new[]{observation});}
        public TutorialResult ObserveMany(IEnumerable<TutorialObservation> observations)
        {
            if(State.status!="Active"||!context.storyActive)return Result(true,false,"tutorial_inactive");
            try
            {
                Require(observations!=null,"observations_required");var next=CombatJson.Clone(State);
                foreach(var observation in observations)
                {
                    Require(observation!=null,"observation_required");
                    if(!observation.accepted||observation.duplicate)continue;
                    Require(string.IsNullOrEmpty(observation.profileInstanceId)||observation.profileInstanceId==State.profileInstanceId,"tutorial_identity_mismatch");
                    Require(string.IsNullOrEmpty(observation.storyModeId)||observation.storyModeId==State.storyModeId,"tutorial_mode_mismatch");
                    var fact=Definition.facts.Find(f=>f.key==observation.factKey);Require(fact!=null,"unknown_tutorial_fact");
                    Require(Finite(observation.value)&&observation.value>=0,"invalid_tutorial_value");if(!observation.playerInitiated&&!fact.allowSystem)continue;
                    Remember(next,fact,observation.value,observation.sourceId);
                }
                Advance(next,context);return Publish(next);
            }
            catch(Exception ex){return Result(false,false,ex.Message);}
        }
        public TutorialResult Reconcile(TutorialContext value)
        {
            try{var supplied=CopyContext(value);ValidateContext(supplied);var next=CombatJson.Clone(State);ReconcileCandidate(next,supplied);var result=Publish(next);if(result.success)context=supplied;return result;}
            catch(Exception ex){return Result(false,false,ex.Message);}
        }
        void ReconcileCandidate(TutorialState next,TutorialContext supplied)
        {
            ValidateContext(supplied);if(next.status!="Active"||!supplied.storyActive)return;
            foreach(string key in supplied.worldFacts)
            {var fact=Definition.facts.Find(f=>f.key==key);if(fact!=null&&fact.recoverable)Remember(next,fact,fact.threshold,"world/reconcile");}
            Advance(next,supplied);
        }
        public TutorialResult Skip(string commandId)
        {
            var duplicate=Receipt(commandId,"skip");if(duplicate!=null)return duplicate;
            var next=CombatJson.Clone(State);next.status="Skipped";foreach(var step in next.steps.Where(s=>s.status=="Pending")){step.status="Skipped";step.reason="user_skipped";}
            next.receipts.Add(new TutorialReceipt{id=commandId,kind="skip"});return Publish(next);
        }
        public TutorialResult Reset(string commandId,TutorialContext value=null)
        {
            var duplicate=Receipt(commandId,"reset");if(duplicate!=null)return duplicate;
            try
            {
                var supplied=CopyContext(value??context);ValidateContext(supplied);var next=CombatJson.Clone(State);next.resetEpoch++;next.status="Active";next.facts.Clear();next.steps=Definition.steps.Select(s=>new TutorialStepProgress{id=s.id}).ToList();next.receipts.Add(new TutorialReceipt{id=commandId,kind="reset"});ReconcileCandidate(next,supplied);var result=Publish(next);if(result.success)context=supplied;return result;
            }
            catch(Exception ex){return Result(false,false,ex.Message);}
        }
        TutorialResult Receipt(string id,string kind)
        {
            if(string.IsNullOrWhiteSpace(id))return Result(false,false,"tutorial_command_id_required");var old=State.receipts.Find(r=>r.id==id);if(old==null)return null;
            var result=Result(old.kind==kind,false,old.kind==kind?"duplicate":"tutorial_command_payload_mismatch");result.duplicate=old.kind==kind;return result;
        }
        public TutorialView GetView(TutorialContext value)
        {
            var supplied=value??context;ValidateContext(supplied);var view=new TutorialView{stepCount=Definition.steps.Count,finished=State.status=="Completed",skipped=State.status=="Skipped"};
            if(State.status!="Active")return view;
            foreach(var phase in Definition.phases)
            {
                if(!phase.requiredContextFacts.All(supplied.availableFacts.Contains)||phase.excludedContextFacts.Any(supplied.availableFacts.Contains))continue;
                string prioritized=phase.prioritySteps.FirstOrDefault(id=>State.steps.Any(s=>s.id==id&&s.status=="Pending"));
                if(prioritized!=null)
                {
                    FillStepView(view,State.steps.FindIndex(s=>s.id==prioritized),supplied);view.waiting|=phase.waiting;
                    if(!string.IsNullOrEmpty(phase.promptOverride)){view.prompt=phase.promptOverride;view.highlightKey=phase.highlightKey;view.targetKey=phase.targetKey;}
                }
                else{view.stepId="phase/"+phase.id;view.prompt=phase.fallbackPrompt;view.highlightKey=phase.highlightKey;view.targetKey=phase.targetKey;view.waiting=phase.waiting;view.visible=supplied.storyActive&&!supplied.suppress;}
                return view;
            }
            int index=State.steps.FindIndex(s=>s.status=="Pending");if(index<0){view.finished=true;return view;}FillStepView(view,index,supplied);return view;
        }
        void FillStepView(TutorialView view,int index,TutorialContext supplied)
        {
            var step=Definition.steps[index];bool available=step.availableFacts.All(supplied.availableFacts.Contains);view.stepId=step.id;view.stepNumber=index+1;view.waiting=!available;
            view.prompt=available||string.IsNullOrEmpty(step.waitingPrompt)?step.prompt:step.waitingPrompt;view.highlightKey=available?step.highlightKey:null;view.targetKey=available||string.IsNullOrEmpty(step.waitingTargetKey)?step.targetKey:step.waitingTargetKey;view.visible=supplied.storyActive&&!supplied.suppress;
        }
        public TutorialResult Save()
        {
            try{Validate(State);if(Store!=null)Store.Save(State);LastSaveError=null;return Result(true,false,Store==null?"memory_only":"saved");}
            catch(Exception ex){LastSaveError=ex.Message;return Result(false,false,ex.Message);}
        }
        TutorialResult Publish(TutorialState next)
        {
            if(CombatJson.Write(next)==CombatJson.Write(State))return Result(true,false,"unchanged");
            next.revision=State.revision+1;
            try{Validate(next);if(Store!=null)Store.Save(next);State=next;LastSaveError=null;return Result(true,true,"ok");}
            catch(Exception ex){LastSaveError=ex.Message;return Result(false,false,ex.Message);}
        }
        TutorialResult Result(bool success,bool changed,string reason){return new TutorialResult{success=success,changed=changed,reason=reason,revision=State.revision};}
        void Remember(TutorialState next,TutorialFactDefinition definition,float value,string source)
        {
            var record=next.facts.Find(f=>f.key==definition.key);
            if(record==null){record=new TutorialFactRecord{key=definition.key,value=value,sourceId=source};next.facts.Add(record);}else if(value>record.value){record.value=value;record.sourceId=source;}
            if(record.value>=definition.threshold)foreach(string implied in definition.implies){var fact=Definition.facts.Find(f=>f.key==implied);Remember(next,fact,fact.threshold,source);}
        }
        bool Has(TutorialState state,string key)
        {var fact=Definition.facts.Find(f=>f.key==key);var record=state.facts.Find(f=>f.key==key);return fact!=null&&record!=null&&record.value>=fact.threshold;}
        void Advance(TutorialState next,TutorialContext supplied)
        {
            if(next.status!="Active")return;
            for(int i=0;i<Definition.steps.Count;i++)
            {
                var rule=Definition.steps[i];var progress=next.steps[i];if(progress.status=="Completed")continue;
                if(rule.requiredFacts.All(key=>Has(next,key))){progress.status="Completed";progress.reason="observed_fact";}
                else if(progress.status=="Pending"&&rule.retireOnFacts.Any(key=>Has(next,key))&&!(rule.retireUnlessFacts.Count>0&&rule.retireUnlessFacts.All(supplied.availableFacts.Contains)))
                {progress.status="Skipped";progress.reason="opportunity_passed";}
            }
            if(next.steps.All(s=>s.status!="Pending"))next.status="Completed";
        }
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static void ValidateContext(TutorialContext value){Require(value!=null&&value.availableFacts!=null&&value.worldFacts!=null,"invalid_tutorial_context");}
        void ValidateDefinition()
        {
            Require(!string.IsNullOrWhiteSpace(Definition.id)&&Definition.version>0&&Definition.facts!=null&&Definition.steps!=null&&Definition.steps.Count>0&&Definition.phases!=null,"invalid_tutorial_definition");
            Require(Definition.facts.All(f=>f!=null&&!string.IsNullOrEmpty(f.key)&&Finite(f.threshold)&&f.threshold>0&&f.implies!=null)&&Definition.facts.Select(f=>f.key).Distinct().Count()==Definition.facts.Count,"invalid_tutorial_facts");
            foreach(var fact in Definition.facts)CheckImplications(fact,new HashSet<string>());
            Require(Definition.steps.All(s=>s!=null&&!string.IsNullOrEmpty(s.id)&&!string.IsNullOrEmpty(s.prompt)&&s.requiredFacts!=null&&s.requiredFacts.Count>0&&s.availableFacts!=null&&s.retireOnFacts!=null&&s.retireUnlessFacts!=null&&s.requiredFacts.Concat(s.retireOnFacts).All(k=>Definition.facts.Any(f=>f.key==k)))&&Definition.steps.Select(s=>s.id).Distinct().Count()==Definition.steps.Count,"invalid_tutorial_steps");
            Require(Definition.phases.All(p=>p!=null&&!string.IsNullOrEmpty(p.id)&&!string.IsNullOrEmpty(p.fallbackPrompt)&&p.requiredContextFacts!=null&&p.requiredContextFacts.Count>0&&p.excludedContextFacts!=null&&p.prioritySteps!=null&&p.prioritySteps.All(id=>Definition.steps.Any(s=>s.id==id)))&&Definition.phases.Select(p=>p.id).Distinct().Count()==Definition.phases.Count,"invalid_tutorial_phases");
        }
        void CheckImplications(TutorialFactDefinition fact,HashSet<string> path)
        {Require(path.Add(fact.key),"tutorial_implication_cycle");foreach(string key in fact.implies){var next=Definition.facts.Find(f=>f.key==key);Require(next!=null,"unknown_tutorial_implication");CheckImplications(next,new HashSet<string>(path));}}
        public void Validate(TutorialState state)
        {
            Require(state!=null&&state.schemaVersion==1&&state.definitionId==Definition.id&&state.definitionVersion==Definition.version,"tutorial_definition_mismatch");
            Require(!string.IsNullOrWhiteSpace(state.profileInstanceId)&&!string.IsNullOrWhiteSpace(state.storyModeId)&&state.revision>=0&&state.resetEpoch>=0&&new[]{"Active","Skipped","Completed"}.Contains(state.status),"invalid_tutorial_identity");
            Require(state.steps!=null&&state.steps.Count==Definition.steps.Count&&state.steps.Select(s=>s.id).SequenceEqual(Definition.steps.Select(s=>s.id))&&state.steps.All(s=>new[]{"Pending","Completed","Skipped"}.Contains(s.status)),"invalid_tutorial_progress");
            Require(state.facts!=null&&state.facts.All(f=>f!=null&&Definition.facts.Any(d=>d.key==f.key)&&Finite(f.value)&&f.value>=0)&&state.facts.Select(f=>f.key).Distinct().Count()==state.facts.Count,"invalid_tutorial_evidence");
            for(int i=0;i<state.steps.Count;i++)if(state.steps[i].status=="Completed")Require(Definition.steps[i].requiredFacts.All(k=>Has(state,k)),"tutorial_completion_without_fact");
            Require(state.status=="Active"||state.steps.All(s=>s.status!="Pending"),"tutorial_terminal_has_pending_steps");
            Require(state.receipts!=null&&state.receipts.All(r=>r!=null&&!string.IsNullOrEmpty(r.id)&&(r.kind=="reset"||r.kind=="skip"))&&state.receipts.Select(r=>r.id).Distinct().Count()==state.receipts.Count,"invalid_tutorial_receipts");
        }
    }
}
