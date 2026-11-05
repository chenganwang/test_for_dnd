using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P46FocusBudgetTests:PartyTestFixture
    {
        [TestCase(CombatMode.TacticalFocus)] [TestCase(CombatMode.RollFreeze)] [TestCase(CombatMode.PostRollDecision)]
        public void LegacyPausedDecisionResumesWithActiveBudgetAndCannotRemainInUnlimitedFocus(CombatMode previousMode)
        {
            Run(CommandKind.EnterFocus);if(previousMode!=CombatMode.TacticalFocus)Run(CommandKind.ConfirmRoll);if(previousMode==CombatMode.PostRollDecision)Run(CommandKind.RevealRoll);Run(CommandKind.Pause);
            var json=JObject.Parse(CombatJson.Write(S));json.Remove("focusBudget");json.Remove("reactionOpportunityRoots");
            var config=session.Config;session=new CombatSession(CombatJson.Read<CombatState>(json.ToString()),config);
            Assert.That(S.focusBudget.active,Is.True);session.Tick(10);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(8));Run(CommandKind.Resume);
            if(previousMode==CombatMode.RollFreeze){session.Tick(10);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(8));Run(CommandKind.RevealRoll);}
            session.Tick(1);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(7));session.Tick(7);Assert.That(S.focusBudget.secondsRemaining,Is.Zero);Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));
        }
        [Test] public void LegacyRealtimeReactionDoesNotInventAnActiveFocusEntry()
        {
            Offer("mage",12,6);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));
            var json=JObject.Parse(CombatJson.Write(S));json.Remove("focusBudget");
            var restored=CombatJson.Read<CombatState>(json.ToString());Assert.That(restored.focusBudget.active,Is.False);
        }
        [Test] public void ZeroBudgetRealtimeCommandSelectionAndFreeMoveKeepActiveActorResourcesAndTimeScale()
        {
            S.focusBudget.secondsRemaining=0;S.charges=0;string rng=CombatJson.Write(S.random);Run(CommandKind.SelectCommandActor,"mage");Assert.That(S.commandActorId,Is.EqualTo("mage"));Assert.That(S.activeActorId,Is.EqualTo("fighter"));
            Run(CommandKind.IssueBasicOrder,"mage",c=>{c.actionId="Move";c.qualified=true;c.x=3;c.z=2;});Assert.That(S.Actor("mage").basicOrder.kind,Is.EqualTo(BasicOrderKind.Move));Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(session.Scale,Is.EqualTo(1));Assert.That(S.focusBudget.secondsRemaining,Is.Zero);Assert.That(S.charges,Is.Zero);Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(2));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));
            Offer("mage",12,6);Assert.That(session.Execute(C(CommandKind.SelectCommandActor,"rogue")).reason,Is.EqualTo("finish_pending_roll_first"));Assert.That(S.commandActorId,Is.EqualTo("mage"));
        }
        [Test] public void FocusAndOrdinaryPostRollSpendRealActiveTimeButFreezePauseAndUnavailableUiDoNot()
        {
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(15));Run(CommandKind.EnterFocus);session.Tick(1);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));
            session.Tick(2,false);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));Run(CommandKind.Pause);session.Tick(20);Run(CommandKind.Resume);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));
            Run(CommandKind.ConfirmRoll);session.Tick(20);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(14));Run(CommandKind.RevealRoll);session.Tick(.5f);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(13.5f));
            Run(CommandKind.Cancel);Run(CommandKind.ExitFocus);session.Tick(3);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(13.5f));Assert.That(S.focusBudget.secondsSpent,Is.EqualTo(1.5f));
        }
        [Test] public void RapidReentryDoublesDrainUntilNextEntryAndOnlyActiveTimeClearsThePenalty()
        {
            Run(CommandKind.EnterFocus);session.Tick(1);Run(CommandKind.ExitFocus);session.Tick(20,false);Run(CommandKind.EnterFocus);Assert.That(S.focusBudget.entryDrainMultiplier,Is.EqualTo(2));session.Tick(.5f);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(13));
            Run(CommandKind.ExitFocus);Run(CommandKind.Pause);session.Tick(20);Run(CommandKind.Resume);session.Tick(1.2f);Run(CommandKind.EnterFocus);Assert.That(S.focusBudget.entryDrainMultiplier,Is.EqualTo(1));Assert.That(S.focusBudget.entryCount,Is.EqualTo(3));
        }
        [TestCase(false)] [TestCase(true)] public void ExhaustionLeavesSelectionOrCancelsPaidOrdinaryRollWithoutRefund(bool paid)
        {
            Run(CommandKind.EnterFocus);if(paid){Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);Run(CommandKind.Modify);}
            int charge=S.charges,inspiration=S.Actor("fighter").inspiration;ulong draws=S.random.visibleTactical.drawCount;session.Tick(15);
            Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(S.focusBudget.secondsRemaining,Is.Zero);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.Actor("fighter").heldBy,Is.Null);
            Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.Actor("fighter").inspiration,Is.EqualTo(inspiration));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(session.Execute(C(CommandKind.EnterFocus)).reason,Is.EqualTo("focus_budget_empty"));if(paid)Assert.That(S.rolls.Single().stage,Is.EqualTo("Canceled"));
        }
        [Test] public void ExhaustionPreservesCommittedQueueAndReturnsToRealtimeExecution()
        {
            Run(CommandKind.EnterFocus);string queue=Queue("fighter","attack");session.Tick(15);Assert.That(S.mode,Is.EqualTo(CombatMode.QueuedExecution));Assert.That(S.queue.Single().stage,Is.EqualTo("Queued"));Run(CommandKind.StartQueued,fill:c=>c.queueId=queue);session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=queue);Assert.That(S.queue.Single().effectApplied);
        }
        [Test] public void ZeroBudgetDuringRollStillRevealsThenGivesShortPostRollBeforeDefaultCancel()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);S.focusBudget.secondsRemaining=0;ulong draws=S.random.visibleTactical.drawCount;session.Tick(60);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Run(CommandKind.RevealRoll);Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));session.Tick(.1f);Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));session.Tick(.06f);Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));
        }
        [Test] public void CriticalDefenseAwardsOnceAndFullCapDiscardsCannotBeClaimedAfterSpending()
        {
            S.focusBudget.secondsRemaining=20;S.momentumLedger.registeredHighThreats.Add("enemy-1");S.Actor("fighter").dodgeRemaining=.5f;
            var first=session.ResolveIncoming("defense1","enemy-1","fighter",8,false,12,"same-danger");Assert.That(first.success);Assert.That(S.focusBudget.secondsDiscarded,Is.EqualTo(.75f));
            Run(CommandKind.EnterFocus);session.Tick(1);S.Actor("fighter").dodgeRemaining=.5f;Assert.That(session.ResolveIncoming("defense-repeat","enemy-1","fighter",8,false,12,"same-danger").success,Is.False);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(19));
            S.focusBudget.recoveryMultiplier=1.25f;Assert.That(session.ResolveIncoming("defense2","enemy-1","fighter",8,false,12,"new-danger").success);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(19.9375f));Assert.That(S.focusBudget.rewardRoots.Count,Is.EqualTo(2));
        }
        [Test] public void RegisteredEliteKillRecoversBudgetOnlyForRealSuccessfulKill()
        {
            S.focusBudget.secondsRemaining=1;S.world.strings["enemyTemplate/enemy-1"]="elite";S.Actor("enemy-1").hp=1;S.momentumLedger.registeredHighThreats.Add("enemy-1");S.random.backgroundCombat=BackgroundFor(20);
            var command=C(CommandKind.BasicAttack);var r=session.Execute(command);Assert.That(r.success,r.reason);Assert.That(S.Actor("enemy-1").Alive,Is.False);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(2.5f));Assert.That(session.Execute(command).duplicate);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(2.5f));
        }
        [Test] public void LowDamageOrReactionOriginDefenseCannotFarmTheFocusBudget()
        {
            S.focusBudget.secondsRemaining=3;S.Actor("fighter").dodgeRemaining=.5f;
            Assert.That(session.ResolveIncoming("small","enemy-1","fighter",1,false,12,"small-root").success);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(3));
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.fromReaction=true;c.rootId="reaction-defense";c.incomingContacts=new System.Collections.Generic.List<IncomingContact>{new IncomingContact{targetId="fighter",damage=20}};
            Assert.That(session.Execute(c).success);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(3));Assert.That(S.focusBudget.rewardRoots,Is.Empty);
        }
        [Test] public void SameAoeSelectionChangesAndLoadUseOneOpportunityAndOneUnrefreshedTimer()
        {
            S.Actor("fighter").armorClass=S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;
            for(ulong seed=1;seed<100000;seed++){var candidate=RandomStreams.Create(seed).backgroundCombat;var probe=CombatJson.Clone(candidate);if(RandomStreams.D20(probe)==12&&RandomStreams.D20(probe)==12){S.random.backgroundCombat=candidate;break;}}
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.rootId="same-aoe";c.incomingContacts=new System.Collections.Generic.List<IncomingContact>{new IncomingContact{targetId="fighter",damage=6},new IncomingContact{targetId="mage",damage=6}};Assert.That(session.Execute(c).success);Assert.That(S.reactionWindows.Count,Is.EqualTo(2));Assert.That(S.reactionOpportunityRoots.Count,Is.EqualTo(1));
            var first=S.reactionWindows.Single(w=>w.defenderId=="fighter");Begin(first,"parry");session.Tick(.5f);var second=S.reactionWindows.Single(w=>w.defenderId=="mage");Begin(second,"shield");Run(CommandKind.SetReactionSettings,fill:x=>x.reactionSettings=new ReactionSettings{autoMode=ReactionAutoMode.Manual});session=new CombatSession(S.Clone(),session.Config);
            Assert.That(S.reactionOpportunityRoots.Count,Is.EqualTo(1));Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(2));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(1));
        }
        [Test] public void CurrentSnapshotsCannotReintroduceLongTimersAndExplicitNullBudgetIsNotLegacy()
        {
            Offer("mage",12,6);var altered=S.Clone();altered.activePendingEvent.remainingUiSeconds=20;Assert.Throws<InvalidOperationException>(()=>new CombatSession(altered,session.Config));
            var json=JObject.Parse(CombatJson.Write(S));json["focusBudget"]=JValue.CreateNull();Assert.Throws<Newtonsoft.Json.JsonSerializationException>(()=>CombatJson.Read<CombatState>(json.ToString()));
        }
        [TestCase(false)] [TestCase(true)] public void SixDistinctReactionEventsLimitBothManualAndAutomaticSlowtimeWithoutUsingChargeOrFocus(bool manual)
        {
            Run(CommandKind.SetReactionSettings,fill:c=>c.reactionSettings=new ReactionSettings{autoMode=manual?ReactionAutoMode.Manual:ReactionAutoMode.AllLegal});S.focusBudget.secondsRemaining=0;int charges=S.charges;
            for(int i=0;i<6;i++){S.Actor("mage").hp=S.Actor("mage").maxHp;var w=Offer("mage",12,6);if(manual)Begin(w,"shield");Assert.That(S.reactionOpportunityRoots.Count,Is.EqualTo(i+1));Run(CommandKind.DeclineReaction);session=new CombatSession(S.Clone(),session.Config);}
            S.Actor("mage").hp=S.Actor("mage").maxHp;S.random.backgroundCombat=BackgroundFor(12);var final=session.ResolveIncoming("seventh","enemy-1","mage",6,false,12,"seventh-root");Assert.That(final.success&&final.damageApplied);Assert.That(S.activePendingEvent,Is.Null);Assert.That(S.incomingEvents.All(e=>e.damageApplied));Assert.That(S.reactionOpportunityRoots.Count,Is.EqualTo(6));Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.focusBudget.secondsRemaining,Is.Zero);
        }
        [Test] public void PaidReactionTimeoutDefaultsToDeclineAndRetainsPaymentAndPriorFocusContext()
        {
            Run(CommandKind.EnterFocus);session.Tick(1);var w=Offer("fighter",12,8);Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);float budget=S.focusBudget.secondsRemaining;int charge=S.charges;session.Tick(2.5f);
            Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(S.Actor("fighter").hp,Is.EqualTo(22));Assert.That(S.Actor("fighter").reactionRemaining,Is.GreaterThan(0));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(2));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(budget));Assert.That(S.charges,Is.EqualTo(charge));
        }
        [TestCase(20,false)] [TestCase(60,false)] [TestCase(2.5f,true)] public void OldLongOrPausedDecisionSettingsCannotBypassTheReactionLimit(float seconds,bool pause)
        {var c=C(CommandKind.SetReactionSettings);c.reactionSettings=new ReactionSettings{decisionSeconds=seconds,pauseDecision=pause};string before=CombatJson.Write(S);Assert.That(session.Execute(c).reason,Is.EqualTo("reaction_settings_invalid"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
    }
    public sealed class P46FocusProfileTests:ProfileTestFixture
    {
        [Test] public void BudgetCrossesEncountersAndModesWithoutRefillAndRetryRestoresEntireStart()
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);profile.Tick(2);Core(CommandKind.ExitFocus);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(13));Win();Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(13));
            StartChallenge();Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(15));Travel("R1");Activate();Core(CommandKind.EnterFocus);profile.Tick(3);Core(CommandKind.ExitFocus);Run(ProfileCommandKind.SettlementOnce,c=>c.selectionId="abandon");Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(13));
            Travel("R2");Travel("R3");Activate();var start=CombatJson.Write(profile.ActiveEncounter.encounterStartSnapshot);Core(CommandKind.EnterFocus);profile.Tick(1);Core(CommandKind.ConfirmRoll);Run(ProfileCommandKind.RetryStoryEncounter);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(13));Assert.That(CombatJson.Write(profile.ActiveEncounter.encounterStartSnapshot),Is.EqualTo(start));Assert.That(CState.pendingRoll,Is.Null);
        }
        [Test] public void ExistingFocusRelicChangesOnlyLegalRecoveryMultiplierAndNoFreeSeconds()
        {
            First(false);CState.focusBudget.secondsRemaining=3;Assert.That(profile.PendingReward.candidates.Contains("FOCUS"));Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="FOCUS");Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(3));Assert.That(CState.focusBudget.recoveryMultiplier,Is.EqualTo(1.25f));Travel("R2");Travel("R3");Activate();Assert.That(CState.focusBudget.recoveryMultiplier,Is.EqualTo(1.25f));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(3));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void FocusDeadlineFailurePreservesPaidResultBudgetWorldAndFile(CombatWriteStage stage)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P46Focus",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try{profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);profile.Store.Save(profile.State);Travel("R1");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);Core(CommandKind.RevealRoll);profile.Tick(14);Assert.That(profile.SaveCurrent("before-budget-deadline").success);string before=CombatJson.Write(profile.State),combat=CombatJson.Write(CState),disk=File.ReadAllText(path);
                profile.Store.WriteFault=point=>{if(point==stage)throw new IOException("focus-deadline-fault");};Assert.That(profile.Tick(2),Is.Zero);Assert.That(profile.LastTickError,Does.Contain("focus-deadline-fault"));Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(CombatJson.Write(CState),Is.EqualTo(combat));profile.Tick(2);Assert.That(CState.pendingRoll,Is.Null);Assert.That(CState.charges,Is.EqualTo(1));Assert.That(CState.focusBudget.secondsRemaining,Is.Zero);}
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        [TestCase(false)] [TestCase(true)] public void OldP4MissingBudgetLoadsWithoutChangingConfigHashesResourcesOrSourceFileAndCanSave(bool paidReaction)
        {
            profile.State.story.build.relicSlots[0]="FOCUS";
            string dir=Path.Combine(Path.GetTempPath(),"P46Migration",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"profile.json");
            try{profile=new ProfileSession(profile.State,BaseConfig,profile.Config,path);profile.Store.Save(profile.State);Travel("R1");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);if(paidReaction){Core(CommandKind.RevealRoll);var w=Offer();Core(CommandKind.BeginReaction,c=>{c.reactionWindowId=w.id;c.reactionId="parry";});Core(CommandKind.ConfirmReactionRoll);}string ticket=CombatJson.Write(CState.pendingRoll),rng=CombatJson.Write(CState.random),configHash=profile.State.baseCombatConfigHash;
                var envelope=JObject.Parse(File.ReadAllText(path));var payload=JObject.Parse((string)envelope["payload"]);
                foreach(var obj in payload.Descendants().OfType<JObject>().Where(o=>o["rulesProfileId"]!=null).ToList()){obj.Remove("focusBudget");obj.Remove("reactionOpportunityRoots");obj["reactionSettings"]["decisionSeconds"]=20;obj["reactionSettings"]["pauseDecision"]=true;}
                envelope["payload"]=payload.ToString(Newtonsoft.Json.Formatting.None);envelope["integrity"]=CombatStore.Digest((string)envelope["payload"]);File.WriteAllText(path,envelope.ToString(Newtonsoft.Json.Formatting.None));byte[] old=File.ReadAllBytes(path);
                profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(File.ReadAllBytes(path),Is.EqualTo(old));Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(configHash));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(8));Assert.That(CState.focusBudget.recoveryMultiplier,Is.EqualTo(1.25f));Assert.That(CState.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));Assert.That(CState.reactionSettings.pauseDecision,Is.False);Assert.That(CombatJson.Write(CState.pendingRoll),Is.EqualTo(ticket));Assert.That(CombatJson.Write(CState.random),Is.EqualTo(rng));Assert.That(CState.charges,Is.EqualTo(1));Assert.That(profile.SaveCurrent("save-migrated-p4").success);profile=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(CState.charges,Is.EqualTo(1));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(8));}
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
    }
}
