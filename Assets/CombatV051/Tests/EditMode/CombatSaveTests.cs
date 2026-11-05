using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class CombatSaveTests
    {
        string directory,path;CombatSession session;int sequence;
        CombatState S {get{return session.State;}}
        [SetUp] public void Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatV051RuleTests",Guid.NewGuid().ToString("N"));path=Path.Combine(directory,"campaign-v051.json");
            session=new CombatSession(CombatState.Create("save-profile","campaign-p0",75),new CombatConfig{passiveMomentumPerSecond=0},path);Assert.That(session.Save().success);sequence=0;
        }
        [TearDown] public void Teardown(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        CombatCommand C(CommandKind kind){return new CombatCommand{id="save-cmd-"+(++sequence),kind=kind};}
        CombatResult Run(CommandKind kind){var r=session.Execute(C(kind));Assert.That(r.success,Is.True,r.reason);return r;}
        void Roll(){Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);}
        void Reload(){session=CombatSession.Load(path,session.Config);}
        [Test] public void ConfirmPersistentlyFreezesSameRawFacesChargeAndRandomState()
        {
            Roll();string before=CombatJson.Write(S);Reload();Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.pendingRoll,Is.SameAs(S.rolls[0]));
            ulong draws=S.random.visibleTactical.drawCount;Run(CommandKind.RevealRoll);Reload();Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));
        }
        [Test] public void PaidRerollAndModifierRestoreNewResultsAndDoNotRefund()
        {
            Roll();Run(CommandKind.RevealRoll);Run(CommandKind.Modify);Run(CommandKind.Precision);var c=C(CommandKind.Reroll);Assert.That(session.Execute(c).success);string before=CombatJson.Write(S);Reload();Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(S.Actor("fighter").inspiration,Is.Zero);Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(session.Execute(c).duplicate);Run(CommandKind.RevealRoll);Run(CommandKind.Cancel);Reload();Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.Actor("fighter").inspiration,Is.Zero);Assert.That(S.Actor("fighter").heldBy,Is.Null);
        }
        [Test] public void CompleteWorldSnapshotIncludesAIPositionsTelegraphsProjectilesStatusesAndAllStreams()
        {
            var f=S.Actor("fighter");var e=S.Actor("enemy-1");f.x=3;f.z=-4;f.heightY=.2f;f.hp=7;f.majorRemaining=.4f;f.dodgeRemaining=.1f;f.statuses.Add(new StatusState{id="test-buff",remaining=3,tickRemaining=.4f});
            e.aiState=1;e.aiRemaining=.7f;e.attackGroupId="charge-9";e.attackTargetId=f.id;e.lockedX=1;e.lockedZ=.5f;e.x=5;e.z=-3;e.hp=17;
            S.world.telegraphs.Add(new TelegraphState{id="t",remaining=.7f,x=2,z=4,shape="Cone"});S.world.projectiles.Add(new ProjectileState{id="p",remaining=2,x=1,z=1,directionX=1,speed=8});S.world.numbers["camera-x"]=4;S.world.strings["scene"]="p0";
            S.charges=1;S.momentum=67;S.momentumLedger.roots.Add("root");RandomStreams.D20(S.random.backgroundCombat);RandomStreams.D20(S.random.classResource);RandomStreams.D20(S.random.eventDice);
            Assert.That(session.Save().success);string before=CombatJson.Write(S);Reload();Assert.That(CombatJson.Write(S),Is.EqualTo(before));
            var clone=S.Clone();clone.world.telegraphs[0].remaining=0;clone.Actor("fighter").statuses[0].remaining=0;Assert.That(S.world.telegraphs[0].remaining,Is.EqualTo(.7f));Assert.That(S.Actor("fighter").statuses[0].remaining,Is.EqualTo(3));
        }
        [Test] public void QueueAndPaidExecutionResumeExactlyOnce()
        {
            session.Config.actions[0].spellCost=1;S.random.visibleTactical=CombatRuleTests.StreamFor(20);Roll();Run(CommandKind.RevealRoll);string q=Run(CommandKind.Commit).queueId;Reload();Assert.That(S.queue.Count,Is.EqualTo(1));Assert.That(S.Actor("fighter").spellSlots,Is.EqualTo(1));Run(CommandKind.ExitFocus);
            var start=C(CommandKind.StartQueued);start.queueId=q;Assert.That(session.Execute(start).success);Reload();Assert.That(S.Actor("fighter").spellSlots,Is.Zero);Assert.That(session.Execute(start).duplicate);session.Tick(1);var finish=C(CommandKind.ResolveQueued);finish.queueId=q;Assert.That(session.Execute(finish).success);int hp=S.Actor("enemy-1").hp;Reload();Assert.That(session.Execute(finish).duplicate);Assert.That(S.Actor("enemy-1").hp,Is.EqualTo(hp));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void ConfirmFailureNeverPublishesChargeRollOrNewDisk(CombatWriteStage stage)
        {
            Run(CommandKind.EnterFocus);string memory=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=s=>{if(s==stage)throw new IOException("injected-"+stage);};var r=session.Execute(C(CommandKind.ConfirmRoll));Assert.That(r.success,Is.False);Assert.That(r.ticketId,Is.Null);Assert.That(CombatJson.Write(S),Is.EqualTo(memory));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();Assert.That(S.charges,Is.EqualTo(2));Assert.That(S.pendingRoll,Is.Null);
        }
        [Test] public void RerollFailureKeepsPreviousFaceInspirationAndRng()
        {
            Roll();Run(CommandKind.RevealRoll);string before=CombatJson.Write(S);session.Store.WriteFault=s=>{if(s==CombatWriteStage.BeforeReplace)throw new IOException("blocked-reroll");};Assert.That(session.Execute(C(CommandKind.Reroll)).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Reload();Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void ExecutionSaveFailureDoesNotDamageOrSpendLongTermResources()
        {
            session.Config.actions[0].spellCost=1;Roll();Run(CommandKind.RevealRoll);string q=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);string before=CombatJson.Write(S);session.Store.WriteFault=s=>{if(s==CombatWriteStage.AfterFlush)throw new IOException("blocked-execute");};var c=C(CommandKind.StartQueued);c.queueId=q;Assert.That(session.Execute(c).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(S.Actor("fighter").spellSlots,Is.EqualTo(1));
        }
        [Test] public void TwoLiveSessionsCannotOverwriteEachOthersCommittedAuthority()
        {
            var other=CombatSession.Load(path,session.Config);Run(CommandKind.EnterFocus);string disk=File.ReadAllText(path);var r=other.Execute(new CombatCommand{id="other-focus",kind=CommandKind.EnterFocus});Assert.That(r.success,Is.False);Assert.That(r.reason,Is.EqualTo("stale_save_authority"));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void CorruptMainRecoversBackupAndNextWriteKeepsBackupValid()
        {
            Run(CommandKind.EnterFocus);string backup=File.ReadAllText(path+".bak");File.WriteAllText(path,"{broken");Reload();Assert.That(session.Store.UsedBackup);Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Run(CommandKind.EnterFocus);Assert.That(File.ReadAllText(path+".bak"),Is.EqualTo(backup));
        }
        [Test] public void ValidJsonTamperingCannotReplaceConfirmedAuthority()
        {
            Roll();var envelope=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path));var state=CombatJson.Read<CombatState>(envelope.payload);state.charges=2;envelope.payload=CombatJson.Write(state);File.WriteAllText(path,CombatJson.Write(envelope));Reload();Assert.That(session.Store.UsedBackup);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));
        }
        [Test] public void BothCorruptFilesRemainAndDoNotCreateNewProfile(){File.WriteAllText(path,"bad");File.WriteAllText(path+".bak","bad-backup");Assert.Throws<IOException>(()=>CombatSession.Load(path,session.Config));Assert.That(File.ReadAllText(path),Is.EqualTo("bad"));Assert.That(File.ReadAllText(path+".bak"),Is.EqualTo("bad-backup"));}
        [Test] public void UncommittedTemporaryFileDoesNotOverrideMain(){Run(CommandKind.EnterFocus);string known=File.ReadAllText(path);File.WriteAllText(path+".tmp","bad-temporary");Reload();Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(File.ReadAllText(path),Is.EqualTo(known));}
        [Test] public void LegacyVersionIsRejectedAndNeverSilentlyMigrated()
        {
            var e=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path));e.schemaVersion=1;e.contentVersion="actiondice-v0.4-m2";File.WriteAllText(path,CombatJson.Write(e));string old=File.ReadAllText(path);Assert.Throws<IOException>(()=>CombatSession.Load(path,session.Config));var fresh=new CombatSession(CombatState.Create("save-profile","campaign-p0",1),session.Config,path);Assert.That(fresh.Save().success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(old));
        }
        [Test] public void NewSessionMustLoadExistingSaveInsteadOfOverwritingIt(){var fresh=new CombatSession(S.Clone(),session.Config,path);string before=File.ReadAllText(path);Assert.That(fresh.Save().reason,Does.Contain("load_existing_save_first"));Assert.That(File.ReadAllText(path),Is.EqualTo(before));}
        [Test] public void ProfilesAndModesCannotWriteIntoAnothersSnapshot(){var wrong=S.Clone();wrong.profileId="someone-else";Assert.Throws<InvalidDataException>(()=>session.Store.Save(wrong));wrong=S.Clone();wrong.modeInstanceId="run-other";Assert.Throws<InvalidDataException>(()=>session.Store.Save(wrong));}
        [Test] public void EndedEncounterAndRewardReceiptsSurviveReloadWithoutNewCharge(){S.Actor("enemy-1").hp=0;S.rewardIds.Add("reward-p0");Run(CommandKind.EndEncounter);Reload();Assert.That(S.encounterResolved);Assert.That(S.momentum,Is.Zero);Assert.That(S.rewardIds,Is.EqualTo(new[]{"reward-p0"}));Assert.That(session.Execute(C(CommandKind.EnterFocus)).success,Is.False);}
        [Test] public void DodgeDirectionAndProtectionRestoreFromTheSameSuccessfulTransaction(){var c=C(CommandKind.StartDodge);c.x=6;c.z=8;Assert.That(session.Execute(c).success);Reload();Assert.That(S.Actor("fighter").lockedX,Is.EqualTo(.6f).Within(.0001));Assert.That(S.Actor("fighter").lockedZ,Is.EqualTo(.8f).Within(.0001));Assert.That(S.Actor("fighter").dodgeRemaining,Is.EqualTo(session.Config.dodgeSeconds));Assert.That(session.Execute(c).duplicate);}
        [Test] public void RejectedDodgeLeavesCommittedDirectionAndDiskUnchanged(){var a=S.Actor("fighter");a.windupRemaining=.4f;a.lockedX=1;a.lockedZ=0;Assert.That(session.Save().success);string memory=CombatJson.Write(S),disk=File.ReadAllText(path);var c=C(CommandKind.StartDodge);c.x=-4;c.z=5;Assert.That(session.Execute(c).reason,Is.EqualTo("actor_busy"));Assert.That(CombatJson.Write(S),Is.EqualTo(memory));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));}
        [Test] public void FailedDodgeSaveDoesNotGrantProtectionOrPublishDirection(){string before=CombatJson.Write(S);session.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeReplace)throw new IOException("dodge-write-failure");};var c=C(CommandKind.StartDodge);c.x=3;c.z=4;Assert.That(session.Execute(c).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Reload();Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
    }
}
