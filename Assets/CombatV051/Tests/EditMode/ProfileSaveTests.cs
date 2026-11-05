using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class ProfileSaveTests:ProfileTestFixture
    {
        string directory,path;
        [SetUp] public void SetupProfileStore()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatV051ProfileRules",Guid.NewGuid().ToString("N"));path=Path.Combine(directory,"P2","campaign.json");
            profile=new ProfileSession(profile.State,profile.BaseCombatConfig,profile.Config,path);profile.Store.Save(profile.State);
        }
        [TearDown] public void CleanupProfile(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        void Reload(){profile=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);}
        void Save(){Assert.That(profile.SaveCurrent("save-"+(++seq)).success);}
        static IEnumerable<TestCaseData> Faults()
        {
            foreach(string operation in new[]{"activate","resolve","event-roll","event-reroll","event-confirm","rest","reward","settle","training","gate","inner-confirm","inner-reroll","inner-parry","deadline"})
                foreach(CombatWriteStage stage in Enum.GetValues(typeof(CombatWriteStage)))yield return new TestCaseData(operation,stage).SetName("P2Atomic_"+operation+"_"+stage);
        }
        [TestCaseSource(nameof(Faults))] public void SoleProfileFileFailureRollsBackOuterInnerRngCostsAndWorld(string operation,CombatWriteStage stage)
        {
            ProfileCommand command=null;CombatCommand inner=null;bool deadline=false;
            if(operation=="activate"){Travel("R1");command=P(ProfileCommandKind.ActivateEncounter,c=>c.qualified=true);}
            else if(operation=="resolve"){Travel("R1");Activate();foreach(var e in CState.enemies)e.hp=0;command=P(ProfileCommandKind.ResolveEncounter);}
            else if(operation.StartsWith("event"))
            {
                First();Travel("R2");AtEvent("EVENT_A");M.random.eventDice=Stream("event",operation=="event-confirm"?20:1);
                if(operation!="event-roll")Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});
                if(operation=="event-reroll")M.random.eventDice=Stream("event",20);
                command=P(operation=="event-roll"?ProfileCommandKind.RollEvent:operation=="event-reroll"?ProfileCommandKind.RerollEvent:ProfileCommandKind.ConfirmEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});
            }
            else if(operation=="rest"){First();Third();Upper();CState.Actor("mage").hp=1;command=P(ProfileCommandKind.ShortRest,c=>{c.selectionId="heal";c.qualified=true;});}
            else if(operation=="reward"){First(false);command=P(ProfileCommandKind.ClaimReward,c=>c.selectionId=profile.PendingReward.candidates[0]);}
            else if(operation=="settle"){First();StartChallenge();First();command=P(ProfileCommandKind.SettlementOnce,c=>c.selectionId="abandon");}
            else if(operation=="training"){profile.State.meta.research=3;Position(0,0);command=P(ProfileCommandKind.TrainParty);}
            else if(operation=="gate"){First();Third();Travel("R2");Position(48,11);command=P(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});}
            else
            {
                Travel("R1");Activate();
                if(operation=="deadline"){Offer();deadline=true;}
                else if(operation=="inner-parry")
                {
                    var w=Offer();Core(CommandKind.BeginReaction,c=>{c.reactionWindowId=w.id;c.reactionId="parry";});Core(CommandKind.ConfirmReactionRoll);Core(CommandKind.RevealRoll);inner=new CombatCommand{id="fault-parry",kind=CommandKind.Commit,actorId="fighter"};
                }
                else
                {
                    Core(CommandKind.EnterFocus);
                    if(operation=="inner-reroll"){Core(CommandKind.ConfirmRoll);Core(CommandKind.RevealRoll);}
                    inner=new CombatCommand{id="fault-core",kind=operation=="inner-confirm"?CommandKind.ConfirmRoll:CommandKind.Reroll,actorId="fighter",targetId=CState.enemies[0].id,actionId="attack"};
                }
            }
            Save();string before=CombatJson.Write(profile.State),combatBefore=CombatJson.Write(CState),disk=File.ReadAllText(path);
            profile.Store.WriteFault=p=>{if(p==stage)throw new IOException("p2-injected-"+stage);};
            if(deadline){Assert.That(profile.Tick(6),Is.Zero);Assert.That(profile.LastTickError,Does.Contain("p2-injected"));}
            else if(inner!=null){var result=profile.ExecuteCombat(inner);Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("p2-injected"));}
            else{var result=profile.Execute(command);Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("p2-injected"));}
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));Assert.That(CombatJson.Write(CState),Is.EqualTo(combatBefore));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));
            if(deadline){Assert.That(profile.Tick(6),Is.EqualTo(6));Assert.That(CState.Actor("fighter").hp,Is.EqualTo(26));}
            else if(inner!=null){Assert.That(profile.ExecuteCombat(inner).success);Assert.That(profile.ExecuteCombat(inner).duplicate);}
            else{Assert.That(profile.Execute(command).success);Assert.That(profile.Execute(command).duplicate);}
            profile.Validate(profile.State);Assert.That(Directory.GetFiles(Path.GetDirectoryName(path),"*.json",SearchOption.AllDirectories).Length,Is.EqualTo(1),"P2 may only write its outer save, never a second combat file");
        }
        [TestCase(false)] [TestCase(true)] public void TwentyActualFileCyclesKeepPaidRollAndCompleteWorldInBothModes(bool challenge)
        {
            if(challenge){First();StartChallenge();}Travel("R1");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);Core(CommandKind.RevealRoll);Core(CommandKind.Modify);Core(CommandKind.Reroll);
            string ticket=CombatJson.Write(CState.pendingRoll),rng=CombatJson.Write(CState.random);int charge=CState.charges,insp=CState.Actor("fighter").inspiration;
            for(int i=0;i<20;i++)
            {
                CState.Actor("mage").x+=.01f;CState.world.numbers["cameraX"]=i;CState.world.strings["enemyPattern/"+CState.enemies[0].id]="strike";Save();string before=CombatJson.Write(profile.State);Reload();Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before),"cycle "+i);Assert.That(CombatJson.Write(CState.pendingRoll),Is.EqualTo(ticket));Assert.That(CombatJson.Write(CState.random),Is.EqualTo(rng));Assert.That(CState.charges,Is.EqualTo(charge));Assert.That(CState.Actor("fighter").inspiration,Is.EqualTo(insp));
            }
            Assert.That(CState.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(CState.world.numbers["cameraX"],Is.EqualTo(19));Assert.That(CState.Actor("fighter").inspiration,Is.Zero);
        }
        [Test] public void TickPartyCloneAndOuterSaveAlwaysCaptureLatestBoundCombatReference()
        {
            Travel("R1");Activate();var w=Offer();var previous=profile.State.ActiveMode.encounters.Single().currentCombat;profile.Tick(.1f);Assert.That(CState,Is.Not.SameAs(previous));CState.Actor("mage").x=27.1f;float remaining=CState.reactionWindows.Single(x=>x.id==w.id).remaining;Save();Reload();Assert.That(CState.Actor("mage").x,Is.EqualTo(27.1f));Assert.That(CState.reactionWindows.Single().remaining,Is.EqualTo(remaining));Assert.That(profile.State.ActiveMode.elapsedRealSeconds,Is.EqualTo(.1f));
        }
        [Test] public void EventResultFutureStreamAndChosenActorRestoreWithoutSecondInitialRoll()
        {
            First();Travel("R2");AtEvent("EVENT_A");M.random.eventDice=Stream("event",1);Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});string result=CombatJson.Write(profile.ActiveSafeEvent),rng=CombatJson.Write(M.random);Save();Reload();Assert.That(CombatJson.Write(profile.ActiveSafeEvent),Is.EqualTo(result));Assert.That(CombatJson.Write(M.random),Is.EqualTo(rng));Assert.That(profile.Execute(P(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";})).reason,Is.EqualTo("event_initial_roll_used"));
            Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");Save();Reload();Run(ProfileCommandKind.OpenEvent,c=>{c.eventId="EVENT_A";c.actorId="fighter";});Assert.That(CombatJson.Write(profile.ActiveSafeEvent),Is.EqualTo(result));
        }
        [Test] public void AlreadyRolledResultCanBeConfirmedAfterOriginalActorFallsWithoutFreeReroll()
        {
            First();Travel("R2");AtEvent("EVENT_A");M.random.eventDice=Stream("event",20);Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");CState.Actor("mage").hp=0;Save();Reload();Assert.That(profile.QuoteEvent("EVENT_A","mage").legal);Run(ProfileCommandKind.OpenEvent,c=>{c.eventId="EVENT_A";c.actorId="fighter";});Assert.That(profile.ActiveSafeEvent.actorId,Is.EqualTo("mage"));Run(ProfileCommandKind.ConfirmEvent,c=>c.eventId="EVENT_A");Assert.That(M.HasFlag("ROUTE_OPEN"));
        }
        [Test] public void SuspendedMajorReactionAndDeadlineResumeAreOneOuterSnapshot()
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);Core(CommandKind.RevealRoll);string original=CState.pendingRollId;var w=Offer();Core(CommandKind.BeginReaction,c=>{c.reactionWindowId=w.id;c.reactionId="parry";});Core(CommandKind.ConfirmReactionRoll);Save();string before=CombatJson.Write(profile.State);Reload();Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));Assert.That(CState.decisionResume.suspendedTicketId,Is.EqualTo(original));Core(CommandKind.RevealRoll);Core(CommandKind.Cancel);Assert.That(CState.pendingRollId,Is.EqualTo(original));Assert.That(CState.Actor("fighter").heldBy,Is.EqualTo(original));
        }
        [Test] public void SameProfileConcurrentWriterFailsCASAndCannotOverwriteNewerWorld()
        {
            var second=ProfileSession.Load(path,profile.BaseCombatConfig,profile.Config);CState.world.numbers["position"]=7;Save();string disk=File.ReadAllText(path);var result=second.SaveCurrent("stale-save");Assert.That(result.success,Is.False);Assert.That(result.reason,Is.EqualTo("stale_profile_authority"));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterReplace)]
        public void OverlappingReadersAndWritersCannotObserveOrOverwriteAnUncommittedProfile(CombatWriteStage stage)
        {
            var second=ProfileSession.Load(path,BaseConfig,profile.Config);long revision=profile.State.revision;
            CState.world.strings["writer"]="A";second.ActiveCombat.State.world.strings["writer"]="B";
            string secondState=CombatJson.Write(second.State),secondCombat=CombatJson.Write(second.ActiveCombat.State);
            ProfileResult competing=null;bool loadBlocked=false,competingReachedWrite=false;
            second.Store.WriteFault=_=>competingReachedWrite=true;
            profile.Store.WriteFault=point=>
            {
                if(point!=stage)return;
                competing=second.SaveCurrent("overlapping-B");
                try{ProfileSession.Load(path,BaseConfig,profile.Config);}catch(IOException){loadBlocked=true;}
            };
            Assert.That(profile.SaveCurrent("owner-A").success);profile.Store.WriteFault=null;
            Assert.That(competing,Is.Not.Null);Assert.That(competing.success,Is.False,"a competing writer must fail before touching the shared temporary file");
            Assert.That(competingReachedWrite,Is.False);Assert.That(loadBlocked,Is.True,"a reader must not adopt a replacement that may still roll back");
            Assert.That(CombatJson.Write(second.State),Is.EqualTo(secondState));Assert.That(CombatJson.Write(second.ActiveCombat.State),Is.EqualTo(secondCombat));
            Assert.That(profile.State.revision,Is.EqualTo(revision+1));string committed=File.ReadAllText(path);
            Assert.That(second.SaveCurrent("released-but-stale-B").reason,Is.EqualTo("stale_profile_authority"));Assert.That(File.ReadAllText(path),Is.EqualTo(committed));
            second=ProfileSession.Load(path,BaseConfig,profile.Config);Assert.That(second.ActiveCombat.State.world.strings["writer"],Is.EqualTo("A"));second.ActiveCombat.State.world.strings["writer"]="B";Assert.That(second.SaveCurrent("fresh-B").success);
            Reload();Assert.That(CState.world.strings["writer"],Is.EqualTo("B"));Assert.That(profile.State.revision,Is.EqualTo(revision+2));Assert.That(File.Exists(path+".lock"),Is.True,"the per-path lock file is persistent and never removed between owners");
        }
        [Test] public void P1MainIsRejectedBeforeValidP2BackupAndAllOldFilesRemainUnchanged()
        {
            string oldPath=Path.Combine(directory,"old-party.json");var p1=new CombatSession(CombatState.CreatePartyP1("old","old-mode",1,BaseConfig),BaseConfig,oldPath);Assert.That(p1.Save().success);File.Copy(path,oldPath+".bak");File.WriteAllText(oldPath+".tmp","old-temporary");var files=new[]{oldPath,oldPath+".bak",oldPath+".tmp"};var bytes=files.Select(File.ReadAllBytes).ToArray();Assert.Throws<IOException>(()=>ProfileSession.Load(oldPath,BaseConfig,profile.Config));Assert.Throws<IOException>(()=>ProfileSession.CreateNew("new",1,BaseConfig,profile.Config,oldPath));for(int i=0;i<files.Length;i++)Assert.That(File.ReadAllBytes(files[i]),Is.EqualTo(bytes[i]));
        }
        [Test] public void CorruptMainRecoversSameProfileBackupAndNeverUsesTemporaryAsAuthority()
        {
            First(false);string expected=CombatJson.Write(profile.State);Run(ProfileCommandKind.SkipReward);File.WriteAllText(path,"incomplete-main");File.WriteAllText(path+".tmp","uncommitted-temporary");Reload();Assert.That(profile.Store.UsedBackup);Assert.That(CombatJson.Write(profile.State),Is.EqualTo(expected));Assert.That(profile.PendingReward,Is.Not.Null);Run(ProfileCommandKind.SkipReward);Reload();Assert.That(profile.PendingReward,Is.Null);
        }
        [Test] public void ConfigChangeOrIntegrityTamperingCannotSilentlyResetOrImportProfile()
        {
            string disk=File.ReadAllText(path);var changed=CombatJson.Clone(profile.Config);changed.enemies[0].hp++;Assert.Throws<IOException>(()=>ProfileSession.Load(path,BaseConfig,changed));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            var e=CombatJson.Read<ProfileEnvelope>(disk);var payload=CombatJson.Read<ProfileState>(e.payload);payload.meta.research=999;e.payload=CombatJson.Write(payload);File.WriteAllText(path,CombatJson.Write(e));Assert.Throws<IOException>(()=>ProfileSession.Load(path,BaseConfig,profile.Config));
        }
        [Test] public void SavedRewardCandidatesTrainingAndDepartureResourcesStayIsolatedOnReload()
        {
            First(false);string candidates=CombatJson.Write(profile.PendingReward);Save();Reload();Assert.That(CombatJson.Write(profile.PendingReward),Is.EqualTo(candidates));Run(ProfileCommandKind.SkipReward);Travel("R0");profile.State.meta.research=3;Run(ProfileCommandKind.TrainParty);StartChallenge();CState.Actor("mage").hp=2;Save();Reload();Assert.That(profile.State.meta.trainingLevel,Is.EqualTo(1));Assert.That(M.departureTraining,Is.EqualTo(1));Assert.That(CState.Actor("mage").hp,Is.EqualTo(2));Assert.That(profile.State.story.explorationCombat.Actor("mage").hp,Is.EqualTo(BaseConfig.Template(ActorRole.Mage).hp+2));
        }
        [Test] public void CorruptPayloadCannotRecoverFromAnotherProfilesValidBackup()
        {
            string other=Path.Combine(directory,"other.json");ProfileSession.CreateNew("another-profile",77,BaseConfig,profile.Config,other);File.Copy(other,path+".bak",true);var e=CombatJson.Read<ProfileEnvelope>(File.ReadAllText(path));e.payload="broken";File.WriteAllText(path,CombatJson.Write(e));string bytes=File.ReadAllText(path);Assert.Throws<IOException>(()=>ProfileSession.Load(path,BaseConfig,profile.Config));Assert.That(File.ReadAllText(path),Is.EqualTo(bytes));
        }
        [Test] public void DeadOriginalEventActorCanConfirmButCannotBuyFailureReroll()
        {
            First();Travel("R2");AtEvent("EVENT_A");M.random.eventDice=Stream("event",1);Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");CState.Actor("mage").hp=0;Save();Reload();Run(ProfileCommandKind.OpenEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});int inspiration=CState.Actor("mage").inspiration;Assert.That(profile.Execute(P(ProfileCommandKind.RerollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";})).reason,Is.EqualTo("inspiration_unavailable"));Assert.That(CState.Actor("mage").inspiration,Is.EqualTo(inspiration));Run(ProfileCommandKind.ConfirmEvent,c=>c.eventId="EVENT_A");Assert.That(M.HasFlag("ROUTE_OPEN"),Is.False);
        }
        [Test] public void FirstCreateFailureRetainsNoCommittedProfileAndRetryCanInitialize()
        {
            string fresh=Path.Combine(directory,"fresh.json");var s=ProfileSession.CreateNew("fresh",91,BaseConfig,profile.Config);var store=new ProfileStore(fresh,s.Validate);store.WriteFault=p=>{if(p==CombatWriteStage.AfterReplace)throw new IOException("first-create-fault");};Assert.Throws<IOException>(()=>store.Save(s.State));Assert.That(File.Exists(fresh),Is.False);store.WriteFault=null;store.Save(s.State);Assert.That(ProfileSession.Load(fresh,BaseConfig,profile.Config).State.profileId,Is.EqualTo("fresh"));
        }
    }
}
