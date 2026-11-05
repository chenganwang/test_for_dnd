using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class TutorialTests
    {
        TutorialService service;TutorialDefinition definition;string directory,path;
        TutorialContext Context(params string[] facts){return new TutorialContext{availableFacts=facts.ToList()};}
        [SetUp] public void Setup()
        {definition=new TutorialDefinition();service=TutorialService.Create(definition,"profile-instance","story-1");directory=Path.Combine(Path.GetTempPath(),"CombatV051Tutorial",Guid.NewGuid().ToString("N"));path=Path.Combine(directory,"tutorial.json");}
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        void Observe(string fact,float value=1){var result=service.Observe(fact,value,"real-source/"+fact);Assert.That(result.success,Is.True,result.reason);}
        TutorialStepProgress Step(string id){return service.State.steps.Single(s=>s.id==id);}
        void Persistent(){service=TutorialService.LoadOrCreate(path,definition,"profile-instance","story-1");Assert.That(service.Save().success);}
        void Reload(TutorialContext context=null){service=TutorialService.LoadOrCreate(path,definition,"profile-instance","story-1",context);}
        [Test] public void OnePromptAdvancesByRealMovementApproachInteractionThenWaitsForExistingCombat()
        {
            var view=service.GetView(Context());Assert.That(view.stepId,Is.EqualTo("move"));Assert.That(view.stepNumber,Is.EqualTo(1));Assert.That(view.prompt,Is.EqualTo("WASD 移动"));
            Observe("move.distance",1.5f);Assert.That(service.GetView(Context()).stepId,Is.EqualTo("move"));Observe("move.distance",2);Assert.That(service.GetView(Context()).stepId,Is.EqualTo("approach"));Observe("entry.approached");view=service.GetView(Context());Assert.That(view.stepId,Is.EqualTo("interact"));Assert.That(view.waiting);Assert.That(view.prompt,Is.EqualTo("靠近入口装置"));
            view=service.GetView(Context("entry.near"));Assert.That(view.waiting,Is.False);Assert.That(view.highlightKey,Is.EqualTo("interact"));Observe("entry.interacted");view=service.GetView(Context());Assert.That(view.stepId,Is.EqualTo("attack"));Assert.That(view.targetKey,Is.EqualTo("R1"));Assert.That(view.prompt,Is.EqualTo("前往东侧房间"));
            view=service.GetView(Context("combat.active","enemy.available"));Assert.That(view.prompt,Is.EqualTo("左键：基础攻击"));
        }
        [Test] public void ActionsPerformedAheadCompleteTheirOwnStepsWithoutChangingFirstPrompt()
        {
            Observe("dodge.started");Observe("guard.started");Observe("active.switched");Observe("entry.interacted");Assert.That(Step("dodge").status,Is.EqualTo("Completed"));Assert.That(Step("approach").status,Is.EqualTo("Completed"));Assert.That(service.GetView(Context()).stepId,Is.EqualTo("move"));Observe("move.distance",2);Assert.That(service.GetView(Context()).stepId,Is.EqualTo("attack"));Observe("basic.hit");Assert.That(service.GetView(Context()).stepId,Is.EqualTo("dice_available"));
        }
        [Test] public void CompleteObservedSequenceEndsOnlyAfterActualExecutionClearAndNextRoom()
        {
            foreach(var f in new[]{"entry.interacted","basic.hit","guard.started","dodge.started","active.switched","dice.available","focus.entered","selection.actor","selection.action","selection.target","roll.confirmed","roll.revealed","roll.committed"})Observe(f);Observe("move.distance",2);
            Assert.That(service.GetView(Context("queue.active")).stepId,Is.EqualTo("execute"));Observe("action.executed");Assert.That(service.GetView(Context()).stepId,Is.EqualTo("clear_r1"));Observe("encounter.r1_clear");Assert.That(service.GetView(Context()).stepId,Is.EqualTo("reach_r2"));Observe("room.r2_entered");Assert.That(service.State.status,Is.EqualTo("Completed"));Assert.That(service.GetView(Context()).visible,Is.False);Assert.That(service.State.steps.All(s=>s.status=="Completed"));
        }
        [Test] public void LegitimateTacticalConfirmationImpliesLegalSelectionsButCommitDoesNotImplyExecution()
        {
            Observe("roll.confirmed");foreach(var id in new[]{"dice_available","focus","select_actor","select_action","select_target","confirm"})Assert.That(Step(id).status,Is.EqualTo("Completed"));Assert.That(Step("reveal").status,Is.EqualTo("Pending"));Observe("roll.committed");Assert.That(Step("reveal").status,Is.EqualTo("Completed"));Assert.That(Step("execute").status,Is.EqualTo("Pending"));Observe("action.executed");Assert.That(Step("execute").status,Is.EqualTo("Completed"));
        }
        [Test] public void InitialAllowanceIsAvailableWithoutClaimingMomentumWasEarned()
        {
            var result=service.Observe("dice.available",1,"encounter-initial",playerInitiated:false);Assert.That(result.success);Assert.That(service.State.facts.Any(f=>f.key=="dice.earned"),Is.False);Assert.That(Step("dice_available").status,Is.EqualTo("Completed"));Observe("dice.earned");Assert.That(service.State.facts.Single(f=>f.key=="dice.earned").value,Is.EqualTo(1));
        }
        [Test] public void FocusWithZeroChargeDoesNotClaimAnAllowanceOrInventMomentum()
        {Observe("focus.entered");Assert.That(service.State.facts.Any(f=>f.key=="dice.available"),Is.False);Assert.That(Step("dice_available").status,Is.EqualTo("Pending"));}
        [Test] public void FailedDuplicateAndAICommandsCannotTeachPlayerActions()
        {
            string before=CombatJson.Write(service.State);Assert.That(service.Observe("basic.hit",accepted:false).success);Assert.That(service.Observe("dodge.started",duplicate:true).success);Assert.That(service.Observe("guard.started",playerInitiated:false).success);Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));
            Assert.That(service.Observe("roll.revealed",playerInitiated:false).success);Assert.That(Step("reveal").status,Is.EqualTo("Completed"));
        }
        [Test] public void MaxFactsAreIdempotentAndLowerOrRepeatedMovementDoesNotAccumulate()
        {
            Observe("move.distance",1);long revision=service.State.revision;Observe("move.distance",1);Observe("move.distance",.5f);Assert.That(service.State.revision,Is.EqualTo(revision));Assert.That(Step("move").status,Is.EqualTo("Pending"));Assert.That(service.State.facts.Count,Is.EqualTo(1));Observe("move.distance",2);Assert.That(Step("move").status,Is.EqualTo("Completed"));
        }
        [Test] public void UnknownInvalidOrCrossProfileBatchRollsBackAllTutorialFacts()
        {
            string before=CombatJson.Write(service.State);var result=service.ObserveMany(new[]{new TutorialObservation{factKey="guard.started"},new TutorialObservation{factKey="unknown"}});Assert.That(result.success,Is.False);Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));
            Assert.That(service.Observe("move.distance",float.NaN).success,Is.False);Assert.That(service.Observe("move.distance",-1).success,Is.False);Assert.That(service.Observe(new TutorialObservation{factKey="guard.started",profileInstanceId="wrong"}).reason,Is.EqualTo("tutorial_identity_mismatch"));Assert.That(service.Observe(new TutorialObservation{factKey="guard.started",storyModeId="challenge"}).reason,Is.EqualTo("tutorial_mode_mismatch"));
        }
        [Test] public void MissedR1OpportunitiesAreSkippedNotFalselyLearnedAndNeverResetWorld()
        {
            Observe("move.distance",2);Observe("active.switched");service.Reconcile(new TutorialContext{worldFacts=new List<string>{"encounter.r1_clear"}});
            foreach(var id in new[]{"attack","guard","dodge","focus","confirm","execute"}){Assert.That(Step(id).status,Is.EqualTo("Skipped"));Assert.That(Step(id).reason,Is.EqualTo("opportunity_passed"));}
            Assert.That(service.State.facts.Any(f=>f.key=="basic.hit"),Is.False);Assert.That(service.GetView(Context()).stepId,Is.EqualTo("reach_r2"));Observe("room.r2_entered");Assert.That(service.State.status,Is.EqualTo("Completed"));
        }
        [Test] public void OldSaveReconcileAcceptsOnlyDeclaredWorldFactsNotPlayerActionClaims()
        {
            var context=new TutorialContext{worldFacts=new List<string>{"room.r2_entered","basic.hit","dodge.started","roll.confirmed"}};service.Reconcile(context);Assert.That(Step("clear_r1").status,Is.EqualTo("Completed"));Assert.That(Step("reach_r2").status,Is.EqualTo("Completed"));Assert.That(Step("attack").status,Is.EqualTo("Skipped"));Assert.That(service.State.facts.Any(f=>f.key=="basic.hit"||f.key=="roll.confirmed"),Is.False);Assert.That(service.GetView(Context()).stepId,Is.EqualTo("move"));
        }
        [Test] public void ResetDuringAnotherCombatCanReplayCombatStepsWithoutRespawningR1()
        {
            Observe("encounter.r1_clear");service.Skip("skip");var context=Context("combat.active","enemy.available");context.worldFacts.Add("encounter.r1_clear");Assert.That(service.Reset("reset",context).success);Assert.That(Step("attack").status,Is.EqualTo("Pending"));Assert.That(Step("clear_r1").status,Is.EqualTo("Completed"));Observe("move.distance",2);Assert.That(service.GetView(context).stepId,Is.EqualTo("attack"));
        }
        [Test] public void SkipAndResetAreIdempotentAndPreserveEarlyHistoryWithoutTouchingGame()
        {
            Observe("guard.started");Assert.That(service.Skip("skip-once").success);Assert.That(service.State.status,Is.EqualTo("Skipped"));Assert.That(Step("guard").status,Is.EqualTo("Completed"));Assert.That(Step("attack").reason,Is.EqualTo("user_skipped"));Assert.That(service.Skip("skip-once").duplicate);Assert.That(service.Reset("reset-once").success);Assert.That(service.State.status,Is.EqualTo("Active"));Assert.That(service.State.resetEpoch,Is.EqualTo(1));Assert.That(service.State.facts,Is.Empty);Assert.That(service.Reset("reset-once").duplicate);Assert.That(service.State.resetEpoch,Is.EqualTo(1));Assert.That(service.Reset("skip-once").reason,Is.EqualTo("tutorial_command_payload_mismatch"));
        }
        [Test] public void UnboundModeAndSuppressionHideTheCardWithoutMarkingAnActionComplete()
        {
            var c=new TutorialContext{storyActive=false};service.Reconcile(c);string before=CombatJson.Write(service.State);Observe("basic.hit");Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));Assert.That(service.GetView(c).visible,Is.False);c.storyActive=true;c.suppress=true;service.Reconcile(c);Observe("basic.hit");Assert.That(Step("attack").status,Is.EqualTo("Completed"));Assert.That(service.GetView(c).visible,Is.False);c.suppress=false;Assert.That(service.GetView(c).visible);
        }
        [Test] public void DataOnlyDefinitionCanReorderStepsChangeThresholdAndRestoreCustomWorldFacts()
        {
            var custom=new TutorialDefinition{id="custom",version=2,facts=new List<TutorialFactDefinition>{new TutorialFactDefinition{key="walk",threshold=5},new TutorialFactDefinition{key="done",allowSystem=true,recoverable=true}},steps=new List<TutorialStepDefinition>{new TutorialStepDefinition{id="world",prompt="一个目标",requiredFacts=new List<string>{"done"}},new TutorialStepDefinition{id="walk",prompt="走到标记",requiredFacts=new List<string>{"walk"}}}};
            custom.phases.Clear();var t=TutorialService.Create(custom,"p","m");Assert.That(t.GetView(Context()).prompt,Is.EqualTo("一个目标"));t.Reconcile(new TutorialContext{worldFacts=new List<string>{"done"}});Assert.That(t.GetView(Context()).stepId,Is.EqualTo("walk"));t.Observe("walk",4);Assert.That(t.State.status,Is.EqualTo("Active"));t.Observe("walk",5);Assert.That(t.State.status,Is.EqualTo("Completed"));
        }
        [Test] public void BadDefinitionCyclesAndCompletionWithoutEvidenceAreRejected()
        {
            var bad=CombatJson.Clone(definition);bad.facts.First(f=>f.key=="move.distance").implies.Add("move.distance");Assert.Throws<InvalidOperationException>(()=>TutorialService.Create(bad,"p","m"));var state=CombatJson.Clone(service.State);state.steps[0].status="Completed";Assert.Throws<InvalidOperationException>(()=>new TutorialService(state,definition));
        }
        [Test] public void ObservationsSkipResetAndPersistenceNeverChangeCombatOrProfileSnapshot()
        {
            var config=new CombatConfig();var core=CombatState.CreatePartyP1("p","m",123,config);var outer=ProfileSession.CreateNew("p",456,config,new DungeonConfig());string coreBefore=CombatJson.Write(core),outerBefore=CombatJson.Write(outer.State),configBefore=CombatJson.Write(config);Persistent();Observe("move.distance",2);Observe("action.executed");service.Skip("skip");service.Reset("reset");service.Save();Reload();Assert.That(CombatJson.Write(core),Is.EqualTo(coreBefore));Assert.That(CombatJson.Write(outer.State),Is.EqualTo(outerBefore));Assert.That(CombatJson.Write(config),Is.EqualTo(configBefore));
        }
        [Test] public void ValidSidecarRestoresExactPartialFactsSkippedStatusResetEpochAndSource()
        {
            Persistent();Observe("move.distance",1.25f);Observe("guard.started");service.Skip("skip");service.Reset("reset");Observe("move.distance",.75f);string expected=CombatJson.Write(service.State);Reload();Assert.That(CombatJson.Write(service.State),Is.EqualTo(expected));Assert.That(service.State.facts.Single().sourceId,Is.EqualTo("real-source/move.distance"));
        }
        [Test] public void MissingSidecarSafelyStartsAndOldWorldProgressReconcilesOnLoad()
        {
            var context=new TutorialContext{worldFacts=new List<string>{"room.r2_entered"}};Reload(context);Assert.That(service.State.status,Is.EqualTo("Active"));Assert.That(service.GetView(context).stepId,Is.EqualTo("move"));Assert.That(Step("attack").status,Is.EqualTo("Skipped"));Assert.That(service.Save().success);Reload(context);Assert.That(Step("reach_r2").status,Is.EqualTo("Completed"));
        }
        [Test] public void CorruptSidecarUsesValidatedBackupWithoutReplayingFutureFacts()
        {
            Persistent();Observe("move.distance",2);string expected=CombatJson.Write(service.State);Observe("guard.started");File.WriteAllText(path,"partial-write");Reload();Assert.That(service.Store.UsedBackup);Assert.That(CombatJson.Write(service.State),Is.EqualTo(expected));Assert.That(Step("guard").status,Is.EqualTo("Pending"));Observe("dodge.started");Reload();Assert.That(Step("dodge").status,Is.EqualTo("Completed"));
        }
        [Test] public void EntirelyCorruptSidecarDefaultsSafelyAndRetainsRejectedBytesBeforeRecovery()
        {
            Directory.CreateDirectory(directory);File.WriteAllText(path,"broken-presentation-data");Reload();Assert.That(service.GetView(Context()).stepId,Is.EqualTo("move"));Observe("move.distance",2);Assert.That(Directory.GetFiles(directory,"tutorial.json.rejected-*").Select(File.ReadAllText),Does.Contain("broken-presentation-data"));Reload();Assert.That(Step("move").status,Is.EqualTo("Completed"));
        }
        [Test] public void WrongProfileSidecarIsNotAdoptedOrOverwritten()
        {
            Persistent();Observe("guard.started");string disk=File.ReadAllText(path);var other=TutorialService.LoadOrCreate(path,definition,"other-profile","other-story");Assert.That(other.State.facts,Is.Empty);Assert.That(other.Observe("move.distance",2).success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void AccidentallyPassingP2SavePathRefusesAllWritesAndRetainsGameplayBytes()
        {
            ProfileSession.CreateNew("p",1,new CombatConfig(),new DungeonConfig(),path);string before=File.ReadAllText(path);Reload();Assert.That(service.LoadWarning,Does.Contain("not_supported_tutorial_sidecar"));Assert.That(service.Observe("move.distance",2).success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(before));
        }
        [Test] public void ConcurrentSidecarWriterFailsBeforeSharedTempAndStaleWriterMustReload()
        {
            Persistent();var b=TutorialService.LoadOrCreate(path,definition,"profile-instance","story-1");TutorialResult attempted=null;service.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeWrite)attempted=b.Observe("dodge.started");};Observe("guard.started");service.Store.WriteFault=null;Assert.That(attempted.success,Is.False);Assert.That(b.State.facts,Is.Empty);Assert.That(b.Observe("dodge.started").reason,Is.EqualTo("stale_tutorial_authority"));b=TutorialService.LoadOrCreate(path,definition,"profile-instance","story-1");Assert.That(b.Observe("dodge.started").success);
        }
        [Test] public void EarlyFocusTemporarilyPrioritizesLegalSelectionsWithoutCompletingGuardOrDodge()
        {
            Observe("move.distance",2);Observe("entry.interacted");Observe("basic.hit");Observe("focus.entered");var focused=Context("combat.active","enemy.available","focus.active","dice.ready");var view=service.GetView(focused);Assert.That(view.stepId,Is.EqualTo("select_actor"));Assert.That(view.highlightKey,Is.EqualTo("actor"));Assert.That(Step("guard").status,Is.EqualTo("Pending"));Assert.That(Step("dodge").status,Is.EqualTo("Pending"));
            Observe("selection.actor");Assert.That(service.GetView(focused).stepId,Is.EqualTo("select_action"));Observe("selection.action");Assert.That(service.GetView(focused).stepId,Is.EqualTo("select_target"));Observe("selection.target");Assert.That(service.GetView(focused).stepId,Is.EqualTo("confirm"));Assert.That(service.GetView(Context("combat.active","enemy.available","realtime.active")).stepId,Is.EqualTo("guard"));
        }
        [Test] public void EarlyConfirmShowsRollThenDecisionWithoutIllegalRealtimePrompts()
        {
            Observe("roll.confirmed");string before=CombatJson.Write(service.State);var view=service.GetView(Context("combat.active","roll.active"));Assert.That(view.stepId,Is.EqualTo("reveal"));Assert.That(view.prompt,Does.Contain("完全暂停"));Assert.That(Step("move").status,Is.EqualTo("Pending"));Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));Observe("roll.revealed");view=service.GetView(Context("combat.active","decision.active"));Assert.That(view.stepId,Is.EqualTo("commit"));Assert.That(view.highlightKey,Is.EqualTo("commit"));Assert.That(Step("guard").status,Is.EqualTo("Pending"));
        }
        [Test] public void CancelAfterSpendingLastChargeAlwaysOffersReturnToRealtimeWithoutGrantingAnything()
        {
            Observe("roll.confirmed");Observe("roll.revealed");var view=service.GetView(Context("combat.active","focus.active"));Assert.That(view.stepId,Is.EqualTo("phase/focus-empty"));Assert.That(view.prompt,Does.Contain("Tab 返回实时"));Assert.That(view.highlightKey,Is.EqualTo("exit_focus"));Assert.That(Step("commit").status,Is.EqualTo("Pending"));Assert.That(Step("execute").status,Is.EqualTo("Pending"));Assert.That(service.State.facts.Any(f=>f.key=="dice.earned"),Is.False);
        }
        [Test] public void QueuedFocusAndRealtimeUseDifferentPromptsUntilActualExecution()
        {
            Observe("roll.committed");var focused=service.GetView(Context("combat.active","focus.active","queue.active"));Assert.That(focused.stepId,Is.EqualTo("execute"));Assert.That(focused.prompt,Does.Contain("Tab 返回实时"));var realtime=service.GetView(Context("combat.active","realtime.active","queue.active"));Assert.That(realtime.stepId,Is.EqualTo("execute"));Assert.That(realtime.prompt,Is.EqualTo("队员正在执行已提交的行动"));Assert.That(realtime.highlightKey,Is.Null);Assert.That(Step("execute").status,Is.EqualTo("Pending"));Observe("action.executed");Assert.That(service.GetView(Context("realtime.active")).stepId,Is.EqualTo("move"));
        }
        [Test] public void PhasePriorityAndFallbackRemainDataDrivenAndReadOnly()
        {
            var custom=CombatJson.Clone(definition);custom.phases.Insert(0,new TutorialPhaseDefinition{id="custom-phase",requiredContextFacts=new List<string>{"custom.active"},prioritySteps=new List<string>{"switch"},promptOverride="自定义单步提示",fallbackPrompt="自定义已完成",highlightKey="party"});var t=TutorialService.Create(custom,"p","m");string before=CombatJson.Write(t.State);var view=t.GetView(Context("custom.active","focus.active","dice.ready"));Assert.That(view.stepId,Is.EqualTo("switch"));Assert.That(view.prompt,Is.EqualTo("自定义单步提示"));Assert.That(CombatJson.Write(t.State),Is.EqualTo(before));t.Observe("active.switched");Assert.That(t.GetView(Context("custom.active")).prompt,Is.EqualTo("自定义已完成"));
        }
        static IEnumerable<TestCaseData> FaultCases()
        {foreach(string operation in new[]{"observe","skip","reset"})foreach(CombatWriteStage stage in Enum.GetValues(typeof(CombatWriteStage)))yield return new TestCaseData(operation,stage).SetName("TutorialAtomic_"+operation+"_"+stage);}
        [TestCaseSource(nameof(FaultCases))] public void SidecarFailureKeepsMemoryAndCommittedFileForEveryAtomicBoundary(string operation,CombatWriteStage stage)
        {
            Persistent();Observe("move.distance",1);if(operation=="reset")service.Skip("prior-skip");string before=CombatJson.Write(service.State),disk=File.ReadAllText(path);service.Store.WriteFault=p=>{if(p==stage)throw new IOException("tutorial-injected-"+stage);};
            var r=operation=="observe"?service.Observe("guard.started"):operation=="skip"?service.Skip("skip-fault"):service.Reset("reset-fault");Assert.That(r.success,Is.False);Assert.That(service.LastSaveError,Does.Contain("tutorial-injected"));Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();Assert.That(CombatJson.Write(service.State),Is.EqualTo(before));r=operation=="observe"?service.Observe("guard.started"):operation=="skip"?service.Skip("skip-fault"):service.Reset("reset-fault");Assert.That(r.success);Assert.That(service.LastSaveError,Is.Null);
        }
    }
}
