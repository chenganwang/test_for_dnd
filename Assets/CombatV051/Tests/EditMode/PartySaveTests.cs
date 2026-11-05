using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class PartySaveTests : PartyTestFixture
    {
        string directory,path;
        [SetUp] public void SetupSave()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatV051PartyRules",Guid.NewGuid().ToString("N"));path=Path.Combine(directory,"P1","party-campaign.json");
            session=new CombatSession(S,session.Config,path);Assert.That(session.Save().success);
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        void RoundTrip()
        {
            Assert.That(session.Save().success);string before=CombatJson.Write(S);session=CombatSession.Load(path,session.Config,"p1-party");Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void FullPartyWorldAndRngSnapshotRestoresInIndependentP1Envelope()
        {
            S.Actor("mage").aiTargetId="enemy-1";S.Actor("rogue").aiMoveX=7;S.Actor("rogue").aiMoveZ=3;S.Actor("rogue").dexSaveBonus=5;
            S.Actor("mage").statuses.Add(new StatusState{id="shield",remaining=1.3f,armorClassBonus=5,sourceId="test"});S.world.telegraphs.Add(new TelegraphState{id="danger",x=2,z=3,remaining=.7f});S.world.projectiles.Add(new ProjectileState{id="bolt",x=-1,z=3,remaining=2,speed=5});S.world.strings["selectedSkill"]="control";S.world.numbers["cameraX"]=3;
            RandomStreams.D20(S.random.classResource);RandomStreams.D20(S.random.eventDice);Assert.That(session.SwitchActive("active","rogue").success);RoundTrip();
            var e=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path));Assert.That(e.schemaVersion,Is.EqualTo(3));Assert.That(S.activeActorId,Is.EqualTo("rogue"));Assert.That(S.Actor("mage").aiTargetId,Is.EqualTo("enemy-1"));
        }
        [Test] public void OfferedAndChoosingShieldRestoreOriginalIncomingDiceAndUnspentHolds()
        {
            var w=Offer("mage");ulong draws=S.random.backgroundCombat.drawCount;RoundTrip();w=S.reactionWindows.Single();Begin(w,"shield");RoundTrip();
            Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(2));Assert.That(S.Actor("mage").heldSpellSlots,Is.EqualTo(1));Assert.That(session.QuoteReaction(w.id,"mage","shield").legal);Run(CommandKind.CommitReaction,"mage");RoundTrip();
            Assert.That(S.Actor("mage").hp,Is.EqualTo(22));Assert.That(S.Actor("mage").spellSlots,Is.EqualTo(1));Assert.That(S.Actor("mage").statuses.Single(x=>x.id=="shield").armorClassBonus,Is.EqualTo(5));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));
        }
        [Test] public void SuspendedMajorParryRollPaidEditsAndResumeRestoreExactlyWithoutRedraw()
        {
            Run(CommandKind.EnterFocus);Run(CommandKind.ConfirmRoll);Run(CommandKind.RevealRoll);string original=S.pendingRollId,originalRaw=CombatJson.Write(S.pendingRoll.rawFaces);var w=Offer();Begin(w,"parry");RoundTrip();
            Run(CommandKind.ConfirmReactionRoll);RoundTrip();Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Run(CommandKind.RevealRoll);Run(CommandKind.Modify);Run(CommandKind.Reroll);RoundTrip();Assert.That(S.Actor("fighter").inspiration,Is.Zero);Assert.That(S.decisionResume.suspendedTicketId,Is.EqualTo(original));
            Run(CommandKind.RevealRoll);Run(CommandKind.Cancel);RoundTrip();Assert.That(S.pendingRollId,Is.EqualTo(original));Assert.That(CombatJson.Write(S.pendingRoll.rawFaces),Is.EqualTo(originalRaw));Assert.That(S.Actor("fighter").heldBy,Is.EqualTo(original));Assert.That(S.Actor("fighter").superiority,Is.EqualTo(2));Assert.That(S.Actor("fighter").heldSuperiority,Is.Zero);
        }
        [Test] public void ParryCommitAndDuplicateCommandRestoreSinglePaymentAndDamage()
        {
            var w=Offer();Begin(w,"parry");S.random.visibleTactical=CombatRuleTests.StreamFor(1);Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);var c=C(CommandKind.Commit);var r=session.Execute(c);Assert.That(r.success);int hp=S.Actor("fighter").hp;RoundTrip();Assert.That(session.Execute(c).duplicate);Assert.That(S.Actor("fighter").superiority,Is.EqualTo(1));Assert.That(S.Actor("fighter").hp,Is.EqualTo(hp));Assert.That(hp,Is.EqualTo(26));
        }
        static IEnumerable<TestCaseData> FaultCases()
        {
            foreach(string operation in new[]{"offer","begin","confirm","modify","reroll","shield-commit","parry-commit","decline"})foreach(CombatWriteStage stage in Enum.GetValues(typeof(CombatWriteStage)))yield return new TestCaseData(operation,stage).SetName("P1Atomic_"+operation+"_"+stage);
        }
        [TestCaseSource(nameof(FaultCases))] public void EveryReactionTransactionFailureRestoresWholeStateAndDisk(string operation,CombatWriteStage stage)
        {
            CombatCommand command;
            if(operation=="offer")
            {
                S.Actor("fighter").armorClass=10;S.Actor("enemy-1").attackBonus=0;S.random.backgroundCombat=BackgroundFor(12);command=C(CommandKind.IncomingAttack,"enemy-1");command.targetId="fighter";command.amount=4;command.rootId="fault-hit";command.qualified=true;command.authority=CommandAuthority.System;
            }
            else
            {
                bool shield=operation=="shield-commit";var w=Offer(shield?"mage":"fighter");
                if(operation=="begin"){command=C(CommandKind.BeginReaction);command.reactionWindowId=w.id;command.reactionId="parry";}
                else
                {
                    Begin(w,shield?"shield":"parry");
                    if(operation=="confirm")command=C(CommandKind.ConfirmReactionRoll);
                    else if(shield)command=C(CommandKind.CommitReaction,"mage");
                    else if(operation=="decline")command=C(CommandKind.DeclineReaction);
                    else{Run(CommandKind.ConfirmReactionRoll);Run(CommandKind.RevealRoll);command=C(operation=="modify"?CommandKind.Modify:operation=="reroll"?CommandKind.Reroll:CommandKind.Commit);}
                    command.reactionWindowId=w.id;
                }
            }
            Assert.That(session.Save().success);string before=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=point=>{if(point==stage)throw new IOException("p1-injected-"+stage);};
            var result=session.Execute(command);Assert.That(result.success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));session=CombatSession.Load(path,session.Config,"p1-party");Assert.That(CombatJson.Write(S),Is.EqualTo(before));
            Assert.That(session.Execute(command).success);Assert.That(session.Execute(command).duplicate);CombatSession.Validate(S);
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void DeadlineFailureReturnsZeroAndDiagnosticWithoutApplyingDamageOrAdvancingSnapshot(CombatWriteStage stage)
        {
            Offer();string before=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=point=>{if(point==stage)throw new IOException("deadline-injected-"+stage);};
            Assert.That(session.Tick(6),Is.Zero);Assert.That(session.LastTickError,Does.Contain("deadline-injected"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));session.Store.WriteFault=null;
            Assert.That(session.Tick(6),Is.EqualTo(6));Assert.That(session.LastTickError,Is.Null);Assert.That(S.Actor("fighter").hp,Is.EqualTo(26));RoundTrip();Assert.That(S.Actor("fighter").hp,Is.EqualTo(26));
        }
        [Test] public void ReactionCommandReplayCannotChangeWindowOwnerOrReactionKind()
        {
            var w=Offer();var c=C(CommandKind.BeginReaction);c.reactionWindowId=w.id;c.reactionId="parry";Assert.That(session.Execute(c).success);RoundTrip();string before=CombatJson.Write(S);c.actorId="mage";c.reactionId="shield";Assert.That(session.Execute(c).reason,Is.EqualTo("command_payload_mismatch"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void P1RefusesP0MainBeforeConsideringP1BackupAndRetainsAllLegacyBytes()
        {
            string p0Path=Path.Combine(directory,"old-p0.json");var old=new CombatSession(CombatState.Create("old","old-run",19),session.Config,p0Path);Assert.That(old.Save().success);Assert.That(old.Execute(new CombatCommand{id="old-focus",kind=CommandKind.EnterFocus}).success);File.WriteAllText(p0Path+".tmp","legacy-temp");
            var files=new[]{p0Path,p0Path+".bak",p0Path+".tmp"};var bytes=files.Select(File.ReadAllBytes).ToArray();
            Assert.Throws<IOException>(()=>CombatSession.Load(p0Path,session.Config,"p1-party"));var wrong=new CombatSession(S.Clone(),session.Config,p0Path);Assert.That(wrong.Save().success,Is.False);
            for(int i=0;i<files.Length;i++)Assert.That(File.ReadAllBytes(files[i]),Is.EqualTo(bytes[i]));
            // A valid P1 backup must never silently replace a valid wrong-profile main.
            File.Copy(path,p0Path+".bak",true);string main=File.ReadAllText(p0Path);Assert.Throws<IOException>(()=>CombatSession.Load(p0Path,session.Config,"p1-party"));Assert.That(File.ReadAllText(p0Path),Is.EqualTo(main));
        }
        [Test] public void P1StoreRefusesWrongCandidateAndP0LoaderRefusesP1WithoutRewriting()
        {
            string disk=File.ReadAllText(path);Assert.Throws<InvalidDataException>(()=>session.Store.Save(CombatState.Create("party-tests","party-run",42)));Assert.Throws<IOException>(()=>CombatSession.Load(path,session.Config));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void CorruptMainFallsBackToSameProfileSnapshotAndKeepsOriginalRoll()
        {
            var w=Offer();Begin(w,"parry");Run(CommandKind.ConfirmReactionRoll);string rolling=CombatJson.Write(S);Run(CommandKind.RevealRoll);File.WriteAllText(path,"corrupt-main");session=CombatSession.Load(path,session.Config,"p1-party");Assert.That(session.Store.UsedBackup);Assert.That(CombatJson.Write(S),Is.EqualTo(rolling));Run(CommandKind.RevealRoll);RoundTrip();Assert.That(S.pendingRoll.stage,Is.EqualTo("AwaitDecision"));
        }
        [Test] public void TamperedHoldOrMissingIncomingLinkIsRejectedOnLoadWithoutRepairingFile()
        {
            var w=Offer();Begin(w,"parry");var envelope=CombatJson.Read<CombatEnvelope>(File.ReadAllText(path));var state=CombatJson.Read<CombatState>(envelope.payload);state.Actor("fighter").heldSuperiority=0;envelope.payload=CombatJson.Write(state);envelope.integrity=CombatStore.Digest(envelope.payload);string bad=CombatJson.Write(envelope);File.WriteAllText(path,bad);File.WriteAllText(path+".bak",bad);Assert.Throws<IOException>(()=>CombatSession.Load(path,session.Config,"p1-party"));Assert.That(File.ReadAllText(path),Is.EqualTo(bad));
        }
    }
}
