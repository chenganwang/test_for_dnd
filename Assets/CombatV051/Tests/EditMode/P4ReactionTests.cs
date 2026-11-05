using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P4PaidReactionProfileTests : ProfileTestFixture
    {
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void PaidDeadlineUsesOnlyProfileFileAndEveryFailureRestoresAllAuthority(CombatWriteStage fault)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P4PaidProfile",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"p4.json");
            try
            {
                profile=new ProfileSession(profile.State,profile.BaseCombatConfig,profile.Config,path);profile.Store.Save(profile.State);Travel("R1");Activate();Core(CommandKind.SetReactionSettings,c=>c.reactionSettings=new ReactionSettings{timeoutAcceptPaidRoll=true});var w=Offer();
                Core(CommandKind.BeginReaction,c=>{c.reactionWindowId=w.id;c.reactionId="parry";});CState.random.visibleTactical=CombatRuleTests.StreamFor(20);Core(CommandKind.ConfirmReactionRoll);Core(CommandKind.RevealRoll);Core(CommandKind.Modify);profile.Tick(2);
                Assert.That(profile.SaveCurrent("paid-deadline-save").success);string outer=CombatJson.Write(profile.State),inner=CombatJson.Write(CState),disk=File.ReadAllText(path);
                profile.Store.WriteFault=stage=>{if(stage==fault)throw new IOException("p4-paid-profile-fault");};Assert.That(profile.Tick(4),Is.Zero);Assert.That(profile.LastTickError,Does.Contain("p4-paid-profile-fault"));Assert.That(CombatJson.Write(profile.State),Is.EqualTo(outer));Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
                profile=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));profile.Tick(4);Assert.That(CState.Actor("fighter").hp,Is.EqualTo(30));Assert.That(CState.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(CState.Actor("fighter").inspiration,Is.EqualTo(1));Assert.That(CState.activePendingEvent,Is.Null);
                Assert.That(Directory.GetFiles(dir,"*.json",SearchOption.AllDirectories).Length,Is.EqualTo(1));profile.Validate(profile.State);
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
    }
    public sealed class P4ReactionTests : PartyTestFixture
    {
        [TestCase("full-cap")] [TestCase("preparation")]
        public void SaveRejectsInvalidNewP4StateBeforeReplacingAValidFile(string invalid)
        {
            string dir=Path.Combine(Path.GetTempPath(),"P4InvalidSave",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"save.json");
            try
            {
                session=new CombatSession(S,session.Config,path);Assert.That(session.Save().success);string disk=File.ReadAllText(path);
                if(invalid=="full-cap")S.momentum=100;else S.Actor("fighter").preparationCooldown=float.NaN;
                var result=session.Save();Assert.That(result.success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));var loaded=CombatSession.Load(path,session.Config,"p1-party");Assert.That(loaded.State.momentum,Is.Zero);Assert.That(loaded.State.Actor("fighter").preparationCooldown,Is.Zero);
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        void Mode(ReactionAutoMode mode){Run(CommandKind.SetReactionSettings,fill:c=>c.reactionSettings=new ReactionSettings{autoMode=mode});}
        void Select(ReactionQuote quote){Run(CommandKind.BeginReaction,quote.actorId,c=>{c.reactionWindowId=quote.windowId;c.reactionId=quote.reactionId;c.incomingId=quote.incomingId;});}
        CombatResult Batch(int damage=6,bool reactionOrigin=false)
        {
            S.Actor("fighter").armorClass=S.Actor("mage").armorClass=S.Actor("rogue").armorClass=10;S.Actor("enemy-1").attackBonus=0;
            for(ulong seed=1;seed<100000;seed++)
            {var stream=RandomStreams.Create(seed).backgroundCombat;var probe=CombatJson.Clone(stream);if(RandomStreams.D20(probe)==12&&RandomStreams.D20(probe)==12){S.random.backgroundCombat=stream;break;}}
            return Run(CommandKind.IncomingBatch,"enemy-1",c=>{c.authority=CommandAuthority.System;c.rootId="batch/"+sequence;c.qualified=true;c.fromReaction=reactionOrigin;c.incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=damage},new IncomingContact{targetId="mage",damage=damage}};});
        }
        [Test] public void ImportantUsefulThreatAutomaticallyOpensOneGroupBeforeDamage()
        {
            int hp=S.Actor("mage").hp;Offer("mage",12,6);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));Assert.That(S.activePendingEvent.autoOpened);Assert.That(S.activeReaction,Is.Null);
            Assert.That(S.Actor("mage").hp,Is.EqualTo(hp));Assert.That(session.Scale,Is.EqualTo(.1f));Assert.That(session.GetReactionChoices().Count,Is.GreaterThan(0));
        }
        [Test] public void LowDamageIsManualByDefaultAndLethalDamageBypassesImportanceFilter()
        {
            Offer("mage",12,2);Assert.That(S.activePendingEvent,Is.Null);Run(CommandKind.DeclineReaction,fill:c=>c.reactionEventId=S.pendingThreatEvents.Single().id);
            S.Actor("mage").hp=2;Offer("mage",12,2);Assert.That(S.activePendingEvent.lethal);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));
        }
        [TestCase(ReactionAutoMode.AllLegal,true)] [TestCase(ReactionAutoMode.Manual,false)]
        public void AutomaticModeIsExplicitAndSaved(ReactionAutoMode mode,bool automatic)
        {Mode(mode);Offer("mage",12,2);Assert.That(S.activePendingEvent!=null,Is.EqualTo(automatic));var restored=new CombatSession(S.Clone(),session.Config);Assert.That(restored.State.reactionSettings.autoMode,Is.EqualTo(mode));}
        [Test] public void UnaffordableAndUnchangeableThreatNeverOpensAnEmptyMenu()
        {
            S.Actor("fighter").superiority=0;S.Actor("mage").spellSlots=0;S.random.backgroundCombat=BackgroundFor(12);S.Actor("mage").armorClass=10;S.Actor("enemy-1").attackBonus=0;
            var r=session.ResolveIncoming("no-resource","enemy-1","mage",6);Assert.That(r.success&&r.damageApplied);Assert.That(S.activePendingEvent,Is.Null);Assert.That(S.reactionWindows,Is.Empty);
        }
        [Test] public void NaturalTwentyCannotBeQuotedAsAnArmorClassShieldDodge()
        {
            Offer("mage",20,6);Assert.That(session.GetReactionChoices().All(q=>q.reactionId!="shield"));Assert.That(session.GetReactionChoices().Any(q=>q.reactionId=="parry"));
        }
        [Test] public void SameThreatAoEOffersAllDefendersInOneGroupAndOneBudget()
        {
            Batch();Assert.That(S.pendingThreatEvents.Count,Is.EqualTo(1));Assert.That(S.activePendingEvent.incomingIds.Count,Is.EqualTo(2));string group=S.activeReactionEventId;
            var fighter=session.GetReactionChoices().First(q=>q.actorId=="fighter"&&q.defenderId=="fighter");Select(fighter);session.Tick(1);float remaining=S.activePendingEvent.remainingUiSeconds;
            var mage=session.GetReactionChoices().First(q=>q.actorId=="mage"&&q.defenderId=="mage");Select(mage);Assert.That(S.activeReactionEventId,Is.EqualTo(group));Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(remaining));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);
            Run(CommandKind.CommitReaction,"mage");Assert.That(S.activePendingEvent,Is.Not.Null);Assert.That(S.Actor("mage").hp,Is.EqualTo(22));Run(CommandKind.DeclineReaction);Assert.That(S.Actor("fighter").hp,Is.EqualTo(24));Assert.That(S.activePendingEvent,Is.Null);
        }
        [Test] public void D20ConfirmImmediatelySpendsReactionButNoChargeAndCancelNeverRefundsIt()
        {
            var w=Offer("fighter",12,8);Begin(w,"parry");int charge=S.charges,resource=S.Actor("fighter").superiority;
            var command=C(CommandKind.ConfirmReactionRoll);var first=session.Execute(command);Assert.That(first.success,first.reason);Assert.That(S.Actor("fighter").reactionRemaining,Is.EqualTo(session.Config.reactionRecovery));Assert.That(S.activeReaction.reactionSpent);Assert.That(session.Execute(command).duplicate);
            Run(CommandKind.RevealRoll);Run(CommandKind.Cancel);Assert.That(S.Actor("fighter").reactionRemaining,Is.EqualTo(session.Config.reactionRecovery));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(resource));Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.Actor("fighter").hp,Is.EqualTo(22));
        }
        [Test] public void DecisionBudgetUsesVisibleUiSecondsAndExcludesFreezePauseAndFocusLoss()
        {
            var w=Offer("fighter",12,8);session.Tick(1);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(1.5f));session.Tick(20,false);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(1.5f));
            Begin(w,"parry");Run(CommandKind.ConfirmReactionRoll);session.Tick(30);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(1.5f));Run(CommandKind.Pause);session.Tick(20);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(1.5f));Run(CommandKind.Resume);Run(CommandKind.RevealRoll);session.Tick(1);Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(.5f));
        }
        [Test] public void EditsAndRerollsDoNotResetBudgetAndPaidTimeoutAcceptsExistingResult()
        {
            Run(CommandKind.SetReactionSettings,fill:c=>c.reactionSettings=new ReactionSettings{timeoutAcceptPaidRoll=true});
            var w=Offer("fighter",12,8);Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);session.Tick(2);Run(CommandKind.Modify);S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.Reroll);
            Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(.5f));session.Tick(100);Run(CommandKind.RevealRoll);ulong draws=S.random.visibleTactical.drawCount;session.Tick(4);
            Assert.That(S.activePendingEvent,Is.Null);Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(S.Actor("fighter").inspiration,Is.Zero);Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));
        }
        [Test] public void UnpaidTimeoutDeclinesWithoutReactionOrClassResourceCost()
        {var w=Offer("mage",12,6);Begin(w,"shield");session.Tick(6);Assert.That(S.Actor("mage").reactionRemaining,Is.Zero);Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(2));Assert.That(S.Actor("mage").hp,Is.EqualTo(16));}
        [Test] public void AutomaticReactionSuspendsAndRestoresTheExactPaidTacticalDecision()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);var ticket=CombatJson.Write(S.pendingRoll);int charges=S.charges;
            Offer("mage",12,6);Assert.That(S.rolls.Single().stage,Is.EqualTo("SuspendedAwaitDecision"));Run(CommandKind.DeclineReaction);
            Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(CombatJson.Write(S.pendingRoll),Is.EqualTo(ticket));Assert.That(S.charges,Is.EqualTo(charges));
        }
        [Test] public void QueuedTacticalFocusReturnsToFocusAndDoesNotAutoExecute()
        {
            Run(CommandKind.EnterFocus);string queue=Queue("fighter","attack");Offer("mage",12,6);Run(CommandKind.DeclineReaction);Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(S.queue.Single(q=>q.id==queue).stage,Is.EqualTo("Queued"));
        }
        [Test] public void IncomingDuringRollFreezeWaitsBeforeBackgroundDrawAndRestoresOnReveal()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);ulong draws=S.random.backgroundCombat.drawCount;
            var c=C(CommandKind.IncomingBatch,"enemy-1");c.authority=CommandAuthority.System;c.qualified=true;c.rootId="frozen-batch";c.incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="mage",damage=6}};S.Actor("enemy-1").attackBonus=0;S.Actor("mage").armorClass=10;S.random.backgroundCombat=BackgroundFor(12);draws=S.random.backgroundCombat.drawCount;
            Assert.That(session.Execute(c).success);Assert.That(session.Execute(c).duplicate);Assert.That(S.deferredIncomingBatches.Count,Is.EqualTo(1));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            session=new CombatSession(S.Clone(),session.Config);Run(CommandKind.RevealRoll);Assert.That(S.deferredIncomingBatches,Is.Empty);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));Assert.That(S.random.backgroundCombat.drawCount,Is.GreaterThan(draws));
        }
        [Test] public void ReactionOriginCannotGenerateARecursiveWindowOrRerollRoot()
        {var r=Batch(reactionOrigin:true);Assert.That(S.reactionWindows,Is.Empty);Assert.That(S.activePendingEvent,Is.Null);Assert.That(S.incomingEvents.All(e=>e.damageApplied));ulong draws=S.random.backgroundCombat.drawCount;Assert.That(session.ResolveIncoming("again","enemy-1","fighter",6,false,12,S.pendingThreatEvents.Single().rootId).success,Is.False);Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));}
        [Test] public void AllyProtectionChecksActualRangeHeightAndLineOfSightAndReducesRealDamage()
        {
            S.Actor("fighter").x=-4;S.Actor("mage").z=0;var w=Offer("mage",12,6);Assert.That(session.QuoteReaction(w.id,"fighter","parry").legal,Is.False);
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="fighter",specializationId="F_GUARD",protectionRange=4.5f});Assert.That(session.QuoteReaction(w.id,"fighter","parry").legal);
            S.Actor("fighter").heightY=3;Assert.That(session.QuoteReaction(w.id,"fighter","parry").legal,Is.False);S.Actor("fighter").heightY=0;
            var c=C(CommandKind.BeginReaction);c.reactionWindowId=w.id;c.reactionId="parry";c.lineOfSight=false;string before=CombatJson.Write(S);Assert.That(session.Execute(c).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
            Select(session.QuoteReaction(w.id,"fighter","parry"));S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);Run(CommandKind.Commit);Assert.That(S.Actor("mage").hp,Is.EqualTo(19));Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));
        }
        [Test] public void MageWardAllowsPaidShieldOnAllyWithoutChangingOriginalRoll()
        {
            var w=Offer("fighter",12,8);Assert.That(session.QuoteReaction(w.id,"mage","shield").legal,Is.False);session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="mage",specializationId="M_WARD",allyShield=true});var quote=session.QuoteReaction(w.id,"mage","shield");Assert.That(quote.legal);
            ulong draws=S.random.backgroundCombat.drawCount;Select(quote);Run(CommandKind.CommitReaction,"mage");Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));
        }
        [Test] public void ConfirmedDamageSurvivesVictimMovementAndOriginalAttackerDeath()
        {Offer("mage",12,6);S.Actor("mage").x=50;S.Actor("enemy-1").hp=0;Run(CommandKind.DeclineReaction);Assert.That(S.Actor("mage").hp,Is.EqualTo(16));}
        [Test] public void ReactorDeathReleasesUnpaidClassHoldButKeepsPaidReactionAndRestoresContext()
        {
            var w=Offer("mage",12,6);Select(session.QuoteReaction(w.id,"fighter","parry"));Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);
            var r=session.ResolveIncoming("fatal-save","enemy-1","fighter",100,true,100);Assert.That(r.success,r.reason);Assert.That(S.Actor("fighter").hp,Is.Zero);Assert.That(S.Actor("fighter").reactionRemaining,Is.GreaterThan(0));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);Assert.That(S.Actor("mage").hp,Is.EqualTo(16));Assert.That(S.activePendingEvent,Is.Null);CombatSession.Validate(S);
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void PaidTimeoutFailureRestoresEntirePaidTicketBudgetWorldAndDisk(CombatWriteStage fault)
        {
            Run(CommandKind.SetReactionSettings,fill:c=>c.reactionSettings=new ReactionSettings{timeoutAcceptPaidRoll=true});
            string dir=Path.Combine(Path.GetTempPath(),"P4Reaction",Guid.NewGuid().ToString("N")),path=Path.Combine(dir,"save.json");
            try
            {
                session=new CombatSession(S,session.Config,path);Assert.That(session.Save().success);var w=Offer("fighter",12,8);Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(20);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);session.Tick(2);Assert.That(session.Save().success);
                string state=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=stage=>{if(stage==fault)throw new IOException("paid-timeout-injected");};Assert.That(session.Tick(4),Is.Zero);Assert.That(session.LastTickError,Does.Contain("paid-timeout-injected"));Assert.That(CombatJson.Write(S),Is.EqualTo(state));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
                session=CombatSession.Load(path,session.Config,"p1-party");Assert.That(CombatJson.Write(S),Is.EqualTo(state));session.Tick(4);Assert.That(S.Actor("fighter").hp,Is.EqualTo(30));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(S.activePendingEvent,Is.Null);
            }
            finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
        }
        [Test] public void PauseDecisionCannotBypassTheTwoPointFiveSecondDecisionCap()
        {var c=C(CommandKind.SetReactionSettings);c.reactionSettings=new ReactionSettings{pauseDecision=true};Assert.That(session.Execute(c).reason,Is.EqualTo("reaction_settings_invalid"));Offer("mage",12,6);float time=S.simulationSeconds;session.Tick(2);Assert.That(S.simulationSeconds,Is.GreaterThan(time));Assert.That(S.activePendingEvent.remainingUiSeconds,Is.EqualTo(.5f));}
        [Test] public void MageFocusProtectsFirstNonlethalDamageOnlyAndDoesNotGiveHealth()
        {
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="mage",concentrationProtection=true});S.Actor("mage").concentrationId="control-test";S.Actor("enemy-1").statuses.Add(new StatusState{id="restrained",sourceId="mage",concentrationId="control-test",remaining=10});
            S.Actor("mage").dexSaveBonus=-100;Assert.That(session.ResolveIncoming("first","enemy-1","mage",1,true).success);Assert.That(S.Actor("mage").hp,Is.EqualTo(21));Assert.That(S.Actor("mage").concentrationId,Is.EqualTo("control-test"));Assert.That(S.Actor("mage").p4ConcentrationProtectionUsed);
            Assert.That(session.ResolveIncoming("second","enemy-1","mage",1,true).success);Assert.That(S.Actor("mage").concentrationId,Is.Null);Assert.That(S.Actor("enemy-1").statuses,Is.Empty);
        }
        [Test] public void FighterAdeptNearMissPaysOwnedResourceAndUsesNoExtraRandomDraw()
        {
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="fighter",nearMissCorrection=true});S.Actor("enemy-1").armorClass=17;S.random.visibleTactical=CombatRuleTests.StreamFor(10);
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);Assert.That(S.pendingRoll.hit,Is.False);ulong draws=S.random.classResource.drawCount;Run(CommandKind.Precision);Assert.That(S.pendingRoll.hit);Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(S.Actor("fighter").p4NearMissUsed);Assert.That(S.random.classResource.drawCount,Is.EqualTo(draws));
            Run(CommandKind.Cancel);S.random.visibleTactical=CombatRuleTests.StreamFor(10);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);Run(CommandKind.Precision);Assert.That(S.random.classResource.drawCount,Is.GreaterThan(draws));
        }
        [Test] public void FighterPressureAddsBreakOnlyOnFirstLandedShoveAndResetsOnlyForNewEncounter()
        {
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="fighter",firstShoveBonus=true});S.Actor("enemy-1").breakMaximum=S.Actor("enemy-1").breakValue=10;S.random.visibleTactical=CombatRuleTests.StreamFor(1);
            Run(CommandKind.EnterFocus);var id=Queue("fighter","shove");Run(CommandKind.ExitFocus);Run(CommandKind.StartQueued,fill:c=>c.queueId=id);session.Tick(1);Run(CommandKind.ResolveQueued,fill:c=>c.queueId=id);Assert.That(S.Actor("enemy-1").breakValue,Is.EqualTo(10-session.Config.Action("shove").breakDamage-1));Assert.That(S.Actor("fighter").p4FirstShoveUsed);
            var clone=S.Clone();CombatSession.ResetP4ReactionSpecializations(clone);Assert.That(clone.Actor("fighter").p4FirstShoveUsed,Is.False);Assert.That(S.Actor("fighter").p4FirstShoveUsed);
        }
    }
}
