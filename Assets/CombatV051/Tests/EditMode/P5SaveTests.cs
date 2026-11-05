using System;
using System.IO;
using System.Linq;
using System.Text;
using CombatV051.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Isolated real files and explicit actor/token fixtures. These are rule/storage
    // tests; they do not claim Player input, spatial contact, or human play evidence.
    public sealed class P5SaveTests : ProfileTestFixture
    {
        string directory,path;
        CombatConfig legacy,p5;
        DungeonConfig dungeon;
        [SetUp] public void SetupP5Files()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCrpgP5Save",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();dungeon=new DungeonConfig();
        }
        [TearDown] public void CleanupP5Files(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        static string Oracle(object value){return JsonConvert.SerializeObject(value,Formatting.None,CombatJson.Settings);}
        static string Hash(object value){return CombatStore.Digest(Oracle(value));}
        static void WriteOriginal(string destination,ProfileState state)
        {
            string payload=Oracle(state);var envelope=new ProfileEnvelope{profileId=state.profileId,profileInstanceId=state.profileInstanceId,revision=state.revision,payload=payload,integrity=CombatStore.Digest(payload)};
            File.WriteAllText(destination,Oracle(envelope),new UTF8Encoding(false));
        }
        void P5Profile(){profile=ProfileSession.CreateNewCompatible("profile-tests",41,p5,legacy,dungeon,path);seq=0;}
        void PaidFixture(bool useP5)
        {
            if(useP5)P5Profile();
            Travel("R1");Activate();var actor=CState.Actor("fighter");var enemy=CState.enemies[0];enemy.x=actor.x+1;enemy.z=actor.z;
            Core(CommandKind.EnterFocus);
        }
        SetupToken Opportunity(string kind="off_balance")
        {
            var target=CState.enemies[0];var token=new SetupToken{id="fixture-opportunity",ownerId="mage",targetId=target.id,remaining=3.25f,
                p5=new P5OpportunityData{kind=kind,sourceActionId="fixture-source",encounterAttemptId=CState.encounterId,sourceConditionId=kind=="bound"?"fixture-condition":null}};
            if(kind=="bound")target.statuses.Add(new StatusState{id="p5-frost",remaining=3.25f,p5=new P5ConditionData{instanceId="fixture-condition",sourceActionId="fixture-source",moveMultiplier=.7f}});
            CState.p5.opportunities.Add(token);return token;
        }
        CombatCommand PaidCommand(string opportunity=null){return new CombatCommand{id="p5-paid-once",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=CState.enemies[0].id,actionId="shove",p5=new P5CommandData{opportunityId=opportunity}};}

        [Test] public void NullExtensionsKeepLegacyJsonConfigAndCommandFingerprints()
        {
            var command=new CombatCommand{id="legacy-id",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId="enemy",actionId="attack"};
            foreach(object value in new object[]{legacy,command,profile.State,CState})
            {
                string encoded=CombatJson.Write(value);Assert.That(encoded,Is.EqualTo(Oracle(value)));
                Assert.That(encoded,Does.Not.Contain("\"p5\""));Assert.That(encoded,Does.Not.Contain("departureP5Rules"));
                Assert.That(CombatStore.Digest(encoded),Is.EqualTo(Hash(value)));
            }
            Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(Hash(legacy)));
        }
        [Test] public void LegacyPaidOriginalNewtonsoftFileContinuesWithoutRewritingOrRepricing()
        {
            PaidFixture(false);var command=new CombatCommand{id="legacy-paid-once",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=CState.enemies[0].id,actionId="attack"};
            var paid=profile.ExecuteCombat(command);Assert.That(paid.success,Is.True,paid.reason);string before=Oracle(profile.State),combat=Oracle(CState);
            WriteOriginal(path,profile.State);byte[] disk=File.ReadAllBytes(path);
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);
            Assert.That(Oracle(profile.State),Is.EqualTo(before));Assert.That(Oracle(CState),Is.EqualTo(combat));Assert.That(File.ReadAllBytes(path),Is.EqualTo(disk));
            Assert.That(profile.ActiveUsesP5,Is.False);Assert.That(M.departureP5Rules,Is.Null);Assert.That(profile.ActiveEncounter.resolvedConfig.p5,Is.Null);
            var duplicate=profile.ExecuteCombat(command);Assert.That(duplicate.success&&duplicate.duplicate,Is.True);Assert.That(duplicate.rawFaces,Is.EqualTo(paid.rawFaces));Assert.That(Oracle(CState),Is.EqualTo(combat));
        }
        [Test] public void CompatibleLoaderPrefersValidLegacyMainOverNewConfigBackup()
        {
            WriteOriginal(path,profile.State);var newer=ProfileSession.CreateNew("profile-tests",41,p5,dungeon);newer.State.profileInstanceId=profile.State.profileInstanceId;newer.State.revision=99;
            WriteOriginal(path+".bak",newer.State);string main=File.ReadAllText(path);
            var loaded=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);
            Assert.That(loaded.Store.UsedBackup,Is.False);Assert.That(loaded.ActiveUsesP5,Is.False);Assert.That(loaded.State.revision,Is.EqualTo(profile.State.revision));Assert.That(File.ReadAllText(path),Is.EqualTo(main));
        }
        [Test] public void CorruptMainRecoversExactMatchingBackupConfigWithoutMutatingEitherFile()
        {
            var newer=ProfileSession.CreateNew("profile-tests",41,p5,dungeon);WriteOriginal(path+".bak",newer.State);File.WriteAllText(path,"corrupt");string backup=File.ReadAllText(path+".bak");
            var loaded=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);
            Assert.That(loaded.Store.UsedBackup,Is.True);Assert.That(loaded.ActiveUsesP5,Is.True);Assert.That(Oracle(loaded.State),Is.EqualTo(Oracle(newer.State)));Assert.That(File.ReadAllText(path),Is.EqualTo("corrupt"));Assert.That(File.ReadAllText(path+".bak"),Is.EqualTo(backup));
        }
        [Test] public void CompatibleLoaderDoesNotAcceptForeignBackupAfterInvalidMain()
        {
            WriteOriginal(path,profile.State);var e=JObject.Parse(File.ReadAllText(path));e["integrity"]="bad";File.WriteAllText(path,e.ToString(Formatting.None));
            var other=ProfileSession.CreateNew("other",41,p5,dungeon);WriteOriginal(path+".bak",other.State);
            Assert.Throws<IOException>(()=>ProfileSession.LoadCompatible(path,p5,legacy,dungeon));
        }
        [Test] public void NewChallengeCopiesP5WhileOldStoryAndOuterHashStayUnchanged()
        {
            First();Travel("R0");WriteOriginal(path,profile.State);profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);string story=Oracle(profile.State.story),baseHash=profile.State.baseCombatConfigHash;
            // Mutation of the caller's future defaults cannot mutate this session's copy.
            p5.p5.comboMomentum=21;StartChallenge();Assert.That(M.departureP5Rules.comboMomentum,Is.EqualTo(30));Assert.That(profile.ActiveUsesP5,Is.True);
            Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(baseHash));Assert.That(Oracle(profile.State.story),Is.EqualTo(story));Assert.That(profile.State.story.departureP5Rules,Is.Null);
            string departure=Oracle(M.departureP5Rules);profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);Assert.That(Oracle(M.departureP5Rules),Is.EqualTo(departure));Assert.That(profile.ActiveCombat.Config.p5.comboMomentum,Is.EqualTo(30));
            Run(ProfileCommandKind.ContinueStory);Assert.That(profile.ActiveUsesP5,Is.False);
        }
        [Test] public void ExistingOldChallengeRetainsPreparationResourcesAndConfigThenNewDepartureUsesP5()
        {
            First();StartChallenge();CState.Actor("mage").spellSlots=0;CState.Actor("fighter").inspiration=0;Run(ProfileCommandKind.SaveCurrent);string mode=Oracle(M);WriteOriginal(path,profile.State);
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);Assert.That(Oracle(M),Is.EqualTo(mode));Assert.That(profile.ActiveUsesP5,Is.False);
            // A completed mode fixture isolates the departure-copy rule from full dungeon play.
            profile.State.challenge.stage="Settled";profile.State.activeMode=ProfileModeKind.Story;profile=new ProfileSession(profile.State,legacy,dungeon);
            WriteOriginal(path,profile.State);profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);StartChallenge();Assert.That(M.departureP5Rules,Is.Not.Null);Assert.That(profile.ActiveUsesP5,Is.True);
        }
        [Test] public void ChangedLegacyBaseOrTamperedDepartureIsRejectedWithoutWriting()
        {
            var changed=CombatJson.Clone(p5);changed.basicDamage++;
            Assert.Throws<InvalidOperationException>(()=>ProfileSession.CreateNewCompatible("bad",1,changed,legacy,dungeon,path));Assert.That(File.Exists(path),Is.False);
            P5Profile();string before=File.ReadAllText(path);M.departureP5Rules.comboMomentum=29;
            var result=profile.SaveCurrent("tampered");Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("departure_p5_rules_changed"));Assert.That(File.ReadAllText(path),Is.EqualTo(before));
        }
        [Test] public void P5ConditionOpportunityDeadlineAndPaidContextRoundTripWithoutLoadInitialization()
        {
            PaidFixture(true);var token=Opportunity("bound");var command=PaidCommand(token.id);var paid=profile.ExecuteCombat(command);Assert.That(paid.success,Is.True,paid.reason);
            string before=Oracle(CState),start=Oracle(profile.ActiveEncounter.encounterStartSnapshot);ulong draws=CState.random.visibleTactical.drawCount;
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);Assert.That(Oracle(CState),Is.EqualTo(before));Assert.That(Oracle(profile.ActiveEncounter.encounterStartSnapshot),Is.EqualTo(start));
            Assert.That(CState.pendingRoll.p5.startDeadlineSimulationTime,Is.EqualTo(2));Assert.That(CState.p5.opportunities.Single().consumed,Is.True);Assert.That(CState.enemies[0].statuses.Single(x=>x.p5!=null).remaining,Is.EqualTo(3.25f));
            profile.Tick(.1f);Assert.That(CState.p5.opportunities.Single().remaining,Is.EqualTo(3.25f));Assert.That(CState.random.visibleTactical.drawCount,Is.EqualTo(draws));
            var duplicate=profile.ExecuteCombat(command);Assert.That(duplicate.success&&duplicate.duplicate,Is.True);Assert.That(duplicate.rawFaces,Is.EqualTo(paid.rawFaces));
        }
        [Test] public void P5SafeEventAwaitDecisionRetainsExactD20AndSeparateRngWithoutCombatResources()
        {
            P5Profile();First();Travel("R2");AtEvent("EVENT_A");Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});
            string pending=Oracle(profile.ActiveSafeEvent),random=Oracle(M.random),combat=Oracle(CState);int charges=CState.charges;float momentum=CState.momentum;
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);
            Assert.That(Oracle(profile.ActiveSafeEvent),Is.EqualTo(pending));Assert.That(Oracle(M.random),Is.EqualTo(random));Assert.That(Oracle(CState),Is.EqualTo(combat));
            Assert.That(CState.charges,Is.EqualTo(charges));Assert.That(CState.momentum,Is.EqualTo(momentum));Assert.That(CState.p5.opportunities,Is.Empty);
            Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");AtEvent("EVENT_A");Assert.That(Oracle(profile.ActiveSafeEvent),Is.EqualTo(pending));
        }
        [Test] public void P5WholeEncounterRetryDiscardsFailedAttemptTokensConditionsAndPayment()
        {
            PaidFixture(true);string start=Oracle(profile.ActiveEncounter.encounterStartSnapshot);Opportunity("bound");var paid=profile.ExecuteCombat(PaidCommand("fixture-opportunity"));Assert.That(paid.success,Is.True,paid.reason);CState.Actor("fighter").hp=1;
            Run(ProfileCommandKind.RetryStoryEncounter);var expected=CombatJson.Read<CombatState>(start);expected.encounterId=profile.ActiveEncounter.attemptId;expected.receipts.Clear();
            Assert.That(Oracle(CState),Is.EqualTo(Oracle(expected)));Assert.That(CState.p5.opportunities,Is.Empty);Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.pendingRoll,Is.Null);
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void EffectiveComboRewardAndPhysicalResultRollbackTogetherThenReloadCannotAwardTwice(CombatWriteStage stage)
        {
            PaidFixture(true);Opportunity();
            // Deterministic pure-rule roll fixture; no claim of actual Player RNG/input.
            for(ulong seed=1;seed<10000;seed++){var stream=RandomStreams.Create(seed).visibleTactical;if(RandomStreams.D20(CombatJson.Clone(stream))==18){CState.random.visibleTactical=stream;break;}}
            var paid=profile.ExecuteCombat(PaidCommand("fixture-opportunity"));Assert.That(paid.success,Is.True,paid.reason);Assert.That(CState.pendingRoll.hit,Is.True);
            Core(CommandKind.RevealRoll);Core(CommandKind.Commit);Core(CommandKind.ExitFocus);string queue=CState.queue.Single().id;
            Core(CommandKind.StartQueued,c=>c.queueId=queue);profile.Tick(.3f);Assert.That(profile.LastTickError,Is.Null);Run(ProfileCommandKind.SaveCurrent);
            string outer=Oracle(profile.State),inner=Oracle(CState),disk=File.ReadAllText(path);float x=CState.enemies[0].x;
            var command=new CombatCommand{id="p5-effect-once",kind=CommandKind.ResolveQueued,queueId=queue,actorId="fighter",authority=CommandAuthority.System};
            profile.Store.WriteFault=at=>{if(at==stage)throw new IOException("p5-effect-injected-"+stage);};var failed=profile.ExecuteCombat(command);
            Assert.That(failed.success,Is.False);Assert.That(failed.reason,Does.Contain("p5-effect-injected-"+stage));Assert.That(Oracle(profile.State),Is.EqualTo(outer));Assert.That(Oracle(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);var success=profile.ExecuteCombat(command);Assert.That(success.success,Is.True,success.reason);
            Assert.That(CState.enemies[0].x,Is.GreaterThan(x));Assert.That(CState.momentumLedger.setupEarned,Is.EqualTo(30));Assert.That(CState.focusBudget.rewardEvents.Single(e=>e.category=="combo").credited,Is.EqualTo(.75f));
            string after=Oracle(CState);profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);var duplicate=profile.ExecuteCombat(command);
            Assert.That(duplicate.success&&duplicate.duplicate,Is.True);Assert.That(Oracle(CState),Is.EqualTo(after));Assert.That(CState.p5.opportunities.Single().consumed,Is.True);
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void PaidOpportunityAndRngAreAtomicAcrossEveryOuterFileFailure(CombatWriteStage stage)
        {
            PaidFixture(true);Opportunity();Run(ProfileCommandKind.SaveCurrent);string outer=Oracle(profile.State),inner=Oracle(CState),disk=File.ReadAllText(path);var command=PaidCommand("fixture-opportunity");
            profile.Store.WriteFault=at=>{if(at==stage)throw new IOException("p5-injected-"+stage);};var fail=profile.ExecuteCombat(command);
            Assert.That(fail.success,Is.False);Assert.That(fail.reason,Does.Contain("p5-injected-"+stage));Assert.That(Oracle(profile.State),Is.EqualTo(outer));Assert.That(Oracle(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            profile=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);var success=profile.ExecuteCombat(command);Assert.That(success.success,Is.True,success.reason);Assert.That(CState.charges,Is.EqualTo(1));Assert.That(CState.p5.opportunities.Single().consumed,Is.True);
            string paid=Oracle(CState);var repeat=profile.ExecuteCombat(command);Assert.That(repeat.success&&repeat.duplicate,Is.True);Assert.That(Oracle(CState),Is.EqualTo(paid));
        }
    }
}
