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
    // Explicit placements and deaths are rule fixtures. These tests do not claim
    // real engine contacts, natural encounter balance, or player-input validation.
    public sealed class P6ProfileTests:ProfileTestFixture
    {
        string directory,path;
        CombatConfig legacy,p5,p6;
        DungeonConfig dungeon;
        const string Paladin=P6CharacterCatalog.PaladinId;
        [SetUp] public void SetupFiles()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCrpgP6Profile",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();dungeon=new DungeonConfig();
        }
        [TearDown] public void CleanupFiles(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        static string Oracle(object value){return JsonConvert.SerializeObject(value,Formatting.None,CombatJson.Settings);}
        static string Hash(object value){return CombatStore.Digest(Oracle(value));}
        static void WriteOriginal(string destination,ProfileState state)
        {
            string payload=Oracle(state);var envelope=new ProfileEnvelope{profileId=state.profileId,profileInstanceId=state.profileInstanceId,revision=state.revision,payload=payload,integrity=CombatStore.Digest(payload)};
            File.WriteAllText(destination,Oracle(envelope),new UTF8Encoding(false));
        }
        void Fresh(){profile=ProfileSession.CreateNewCompatible("profile-tests",41,p6,p5,legacy,dungeon,path);seq=0;}
        void Reload(){profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon);}
        CombatCommand Smite(){return new CombatCommand{id="p6-paid-once",kind=CommandKind.ConfirmRoll,actorId=Paladin,targetId=CState.enemies[0].id,actionId="ability_smite_recharge"};}
        void PaidSetup()
        {
            Fresh();Travel("R1");Activate();var actor=CState.Actor(Paladin);var target=CState.enemies[0];target.x=actor.x+1;target.z=actor.z;
            Core(CommandKind.EnterFocus,c=>c.actorId=Paladin);
        }

        [Test] public void PaladinCatalogIsIndependentAndOldRoleNumbersAndFactoryRemainStable()
        {
            Assert.That((int)ActorRole.Fighter,Is.EqualTo(0));Assert.That((int)ActorRole.Rogue,Is.EqualTo(1));Assert.That((int)ActorRole.Mage,Is.EqualTo(2));Assert.That((int)ActorRole.Enemy,Is.EqualTo(3));Assert.That((int)ActorRole.Paladin,Is.EqualTo(4));
            string before=Oracle(legacy);var old=CombatState.CreatePartyP1("old","old",41,legacy);
            Assert.That(old.actors.Select(a=>a.id),Is.EqualTo(new[]{"fighter","rogue","mage"}));Assert.That(old.Actor("fighter").role,Is.EqualTo(ActorRole.Fighter));Assert.That(old.p6,Is.Null);
            var current=CombatState.CreatePartyForPreset("new","new",41,p6);
            Assert.That(current.actors.Select(a=>a.id),Is.EqualTo(new[]{Paladin,"mage","rogue"}));Assert.That(current.Actor(Paladin).role,Is.EqualTo(ActorRole.Paladin));Assert.That(current.activeActorId,Is.EqualTo(Paladin));Assert.That(current.Actor("fighter"),Is.Null);
            Assert.That(p6.Template(ActorRole.Fighter).role,Is.EqualTo(ActorRole.Fighter));Assert.That(p6.BasicFor(current.Actor(Paladin)).range,Is.EqualTo(2.5f));Assert.That(Oracle(legacy),Is.EqualTo(before));
            p6.p6.basicRange=3.25f;var basic=p6.BasicFor(current.Actor(Paladin));Assert.That(basic.id,Is.EqualTo("basic_greatsword"));Assert.That(basic.range,Is.EqualTo(3.25f));Assert.That(p6.p6.paladinTemplate.basic.range,Is.EqualTo(2.5f));Assert.That(p6.BasicFor(old.Actor("fighter")).range,Is.EqualTo(legacy.basicRange));
            P6CharacterCatalog.Find(Paladin).actionIds.Clear();Assert.That(P6CharacterCatalog.Find(Paladin).actionIds.Single(),Is.EqualTo("ability_smite_recharge"));
        }
        [Test] public void P6DraftPersistsSeparatelyAndDepartureIsImmutableAndNoActorResourceRefillOccurs()
        {
            Fresh();Assert.That(profile.ActiveUsesP6);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(90));Assert.That(profile.State.focusScale,Is.EqualTo(.12f));Assert.That(M.p6Departure.recoveryPolicy,Is.EqualTo("ExactState"));
            string departure=Oracle(M.departurePreparation),old=Oracle(profile.State.meta.lastPreparation),rules=Oracle(M.p6Departure);CState.Actor("mage").spellSlots=0;CState.Actor(Paladin).inspiration=0;
            var draft=profile.P6PreparationDraft;draft.defaultActiveActorId="mage";var result=profile.SaveP6Preparation(draft,"save-p6-draft");Assert.That(result.success,Is.True,result.reason);
            Assert.That(Oracle(M.departurePreparation),Is.EqualTo(departure));Assert.That(Oracle(M.p6Departure),Is.EqualTo(rules));Assert.That(Oracle(profile.State.meta.lastPreparation),Is.EqualTo(old));Assert.That(CState.Actor("mage").spellSlots,Is.Zero);Assert.That(CState.Actor(Paladin).inspiration,Is.Zero);
            draft.actors.Clear();Assert.That(profile.P6PreparationDraft.actors.Count,Is.EqualTo(3));Reload();Assert.That(profile.P6PreparationDraft.defaultActiveActorId,Is.EqualTo("mage"));Assert.That(CState.activeActorId,Is.EqualTo(Paladin));
            var again=profile.SaveP6Preparation(profile.P6PreparationDraft,"save-p6-draft");Assert.That(again.success&&again.duplicate,Is.True);
        }
        [Test] public void UnsupportedP6WeaponOrFighterSubstitutionCannotChangeDraftOrFile()
        {
            Fresh();string before=File.ReadAllText(path);var draft=profile.P6PreparationDraft;draft.actors[0].weaponId="fighter-sword-shield";
            var result=profile.SaveP6Preparation(draft,"invalid-weapon");Assert.That(result.success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(before));
            result=profile.SaveP6Preparation(PartyPreparation.Default(),"invalid-party");Assert.That(result.success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(before));
        }
        [TestCase(false)] [TestCase(true)] public void LegacyAndP5PaidFilesContinueWithExactResultsResourcesAndFocus(bool p5Mode)
        {
            if(p5Mode)profile=ProfileSession.CreateNewCompatible("profile-tests",41,p5,legacy,dungeon);
            Travel("R1");Activate();var actor=CState.Actor("fighter");var enemy=CState.enemies[0];enemy.x=actor.x+1;enemy.z=actor.z;Core(CommandKind.EnterFocus);
            var paid=new CombatCommand{id="old-paid",kind=CommandKind.ConfirmRoll,actorId="fighter",targetId=enemy.id,actionId="attack"};var result=profile.ExecuteCombat(paid);Assert.That(result.success,Is.True,result.reason);
            string before=Oracle(profile.State),combat=Oracle(CState);WriteOriginal(path,profile.State);byte[] disk=File.ReadAllBytes(path);Reload();
            Assert.That(Oracle(profile.State),Is.EqualTo(before));Assert.That(Oracle(CState),Is.EqualTo(combat));Assert.That(File.ReadAllBytes(path),Is.EqualTo(disk));Assert.That(profile.ActiveUsesP6,Is.False);Assert.That(CState.focusBudget.p6,Is.Null);Assert.That(CState.focusBudget.secondsRemaining,Is.LessThan(90));
            var repeat=profile.ExecuteCombat(paid);Assert.That(repeat.success&&repeat.duplicate,Is.True);Assert.That(Oracle(CState),Is.EqualTo(combat));Assert.That(M.p6Departure,Is.Null);
        }
        [Test] public void HistoricalP2ImportRetainsFighterAndResourcesThenCompatibleRebindEnablesNewP6Departure()
        {
            profile=ProfileSession.CreateNew("p2-import",41,legacy,dungeon);
            CState.Actor("fighter").hp=7;CState.Actor("mage").spellSlots=0;Run(ProfileCommandKind.SaveCurrent);
            string actors=Oracle(CState.actors);float focus=CState.focusBudget.secondsRemaining;
            // Historical envelope/plan shape fixture, matching the retained P4 migration
            // tests. This is not a read of the author's ordinary Player profile.
            var payload=JObject.Parse(Oracle(profile.State));payload["schemaVersion"]=4;payload["contentVersion"]="actioncrpg-v0.5.1-p2";
            payload.Remove("migrationSourceDigest");payload.Remove("migrationPolicy");((JObject)payload["meta"]).Remove("lastPreparation");((JObject)payload["meta"]).Remove("unlockedSpecializations");
            foreach(string key in new[]{"story","challenge"})
            {
                var mode=payload[key] as JObject;if(mode==null)continue;mode.Remove("departurePreparation");mode.Remove("explicitPauseSeconds");mode.Remove("focusLostSeconds");
                var plan=new JObject{["version"]="dungeon-v051-p2-1",["rooms"]=new JArray(((JArray)mode["rooms"]).Select(r=>new JObject{["id"]=r["id"].DeepClone(),["templateId"]=r["templateId"].DeepClone(),["enemySpawns"]=r["enemySpawns"].DeepClone()})),["abilities"]=mode["departureAbilitySnapshot"].DeepClone(),["departureTraining"]=mode["departureTraining"].DeepClone()};
                mode["planHash"]=CombatStore.Digest(plan.ToString(Formatting.None));
            }
            string body=payload.ToString(Formatting.None);var envelope=new JObject{["schemaVersion"]=4,["contentVersion"]="actioncrpg-v0.5.1-p2",["integrityAlgorithm"]="sha256-utf8-payload-v1",["profileId"]=profile.State.profileId,["profileInstanceId"]=profile.State.profileInstanceId,["revision"]=profile.State.revision,["payload"]=body,["integrity"]=CombatStore.Digest(body)};
            string old=Path.Combine(directory,"legacy-p2.json"),before=envelope.ToString(Formatting.None);File.WriteAllText(old,before);
            var imported=ProfileSession.LoadLegacyIntoP4(old,path,legacy,dungeon,true);
            Assert.That(imported.ActiveUsesP6,Is.False);Assert.That(Oracle(imported.ActiveCombat.State.actors),Is.EqualTo(actors));
            profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon);
            Assert.That(profile.ActiveUsesP6,Is.False);Assert.That(Oracle(CState.actors),Is.EqualTo(actors));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(focus));Assert.That(CState.focusBudget.p6,Is.Null);
            string retainedStory=Oracle(profile.State.story);Run(ProfileCommandKind.StartDemoExploration,c=>c.selectionId="BASE");
            Assert.That(profile.ActiveUsesP6,Is.True);Assert.That(CState.actors.Select(a=>a.id),Is.EqualTo(new[]{Paladin,"mage","rogue"}));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(90));
            Assert.That(Oracle(profile.State.story),Is.EqualTo(retainedStory));Assert.That(File.ReadAllText(old),Is.EqualTo(before));Assert.That(File.ReadAllText(path+".legacy-v051.json"),Is.EqualTo(before));
            Reload();Assert.That(profile.ActiveUsesP6,Is.True);Assert.That(profile.State.story.explorationCombat.Actor("fighter").hp,Is.EqualTo(7));
        }
        [Test] public void ExistingP5ChallengeKeepsSnapshotWhileLaterDepartureAdoptsP6Only()
        {
            profile=ProfileSession.CreateNewCompatible("profile-tests",41,p5,legacy,dungeon);First();StartChallenge();CState.Actor("mage").spellSlots=0;Run(ProfileCommandKind.SaveCurrent);
            string old=Oracle(M);WriteOriginal(path,profile.State);Reload();Assert.That(Oracle(M),Is.EqualTo(old));Assert.That(profile.ActiveUsesP6,Is.False);
            Run(ProfileCommandKind.SettlementOnce,c=>c.selectionId="abandon");string story=Oracle(profile.State.story),baseHash=profile.State.baseCombatConfigHash;
            var draft=profile.P6PreparationDraft;draft.defaultActiveActorId="mage";Assert.That(profile.SaveP6Preparation(draft,"next-prep").success);
            p6.p6.smiteRadiantDice=4;StartChallenge();Assert.That(profile.ActiveUsesP6);Assert.That(CState.activeActorId,Is.EqualTo("mage"));Assert.That(M.p6Departure.rules.smiteRadiantDice,Is.EqualTo(3));Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(baseHash));Assert.That(Oracle(profile.State.story),Is.EqualTo(story));
            Reload();Assert.That(M.p6Departure.rules.smiteRadiantDice,Is.EqualTo(3));Assert.That(CState.Actor("mage").spellSlots,Is.EqualTo(p6.Template(ActorRole.Mage).spellSlots));
        }
        [Test] public void P6NewEncounterFloorsTwoCarriesThreeAndRevisitOrLoadDoesNotRefill()
        {
            Fresh();First();Travel("R0");StartChallenge("COMMAND");Travel("R1");Activate();Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.chargeCap,Is.EqualTo(3));
            CState.charges=3;CState.momentum=0;CState.focusBudget.secondsRemaining=17;CState.focusBudget.secondsSpent=73;Win();Assert.That(CState.charges,Is.EqualTo(3));Assert.That(profile.PendingReward,Is.Null);float focus=CState.focusBudget.secondsRemaining;
            Travel("R0");Travel("R1");Assert.That(CState.charges,Is.EqualTo(3));Assert.That(profile.Execute(P(ProfileCommandKind.ActivateEncounter,c=>c.qualified=true)).success,Is.False);Reload();Assert.That(CState.charges,Is.EqualTo(3));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(focus));
            Travel("R2");Travel("R3");Activate();Assert.That(CState.charges,Is.EqualTo(3));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(focus));CState.charges=0;Win();float after=CState.focusBudget.secondsRemaining;Travel("R2");Travel("R4");Activate();Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(after));
        }
        [Test] public void P6SafeEventPendingResultAndIndependentRngRestoreExactly()
        {
            Fresh();First();Travel("R2");AtEvent("EVENT_A");Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});string before=Oracle(profile.State),combat=Oracle(CState);Reload();
            Assert.That(Oracle(profile.State),Is.EqualTo(before));Assert.That(Oracle(CState),Is.EqualTo(combat));Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");var result=Oracle(profile.ActiveMode.events.First(e=>e.templateId=="EVENT_A"));AtEvent("EVENT_A");Assert.That(Oracle(profile.ActiveSafeEvent),Is.EqualTo(result));
        }
        [Test] public void P6PaidRollingTicketRestoresExactStateAndIndependentCloneDoesNotLoseExtensions()
        {
            PaidSetup();var command=Smite();var result=profile.ExecuteCombat(command);Assert.That(result.success,Is.True,result.reason);string before=Oracle(CState);var copied=CState.Clone();
            Assert.That(CombatJson.Write(copied),Is.EqualTo(before));Assert.That(copied.p6,Is.Not.SameAs(CState.p6));Assert.That(copied.pendingRoll.p6,Is.Not.SameAs(CState.pendingRoll.p6));Assert.That(copied.focusBudget.p6,Is.Not.SameAs(CState.focusBudget.p6));
            copied.p6.damageRandom.state++;copied.pendingRoll.p6.criticalMinimumRaw=17;copied.focusBudget.p6.rules.visitSeconds=4;Assert.That(Oracle(CState),Is.EqualTo(before));Reload();Assert.That(Oracle(CState),Is.EqualTo(before));Assert.That(profile.ExecuteCombat(command).duplicate);Assert.That(Oracle(CState),Is.EqualTo(before));
        }
        [Test] public void P6DepartureTamperingCannotOverwriteExistingAuthoritativeFile()
        {
            Fresh();string disk=File.ReadAllText(path);M.p6Departure.rules.smiteRadiantDice++;
            var result=profile.SaveCurrent("tamper");Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("departure_p6_rules_changed"));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void CompatibleLoaderKeepsValidOldMainInsteadOfP6BackupAndRejectsChangedBase()
        {
            WriteOriginal(path,profile.State);var next=ProfileSession.CreateNewCompatible("profile-tests",41,p6,p5,legacy,dungeon);next.State.profileInstanceId=profile.State.profileInstanceId;next.State.revision=99;WriteOriginal(path+".bak",next.State);Reload();Assert.That(profile.Store.UsedBackup,Is.False);Assert.That(profile.ActiveUsesP6,Is.False);
            p6.basicDamage++;Assert.Throws<InvalidOperationException>(()=>ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon));
        }
        [Test] public void NullP6FieldsKeepOldSerializationAndConfigHashUnchanged()
        {
            foreach(object value in new object[]{legacy,p5,profile.State,CState,new CombatCommand{id="old",kind=CommandKind.ConfirmRoll}})
            {string encoded=CombatJson.Write(value);Assert.That(encoded,Is.EqualTo(Oracle(value)));Assert.That(encoded,Does.Not.Contain("\"p6\""));Assert.That(encoded,Does.Not.Contain("p6Departure"));Assert.That(encoded,Does.Not.Contain("p6Preparation"));}
            Assert.That(profile.State.baseCombatConfigHash,Is.EqualTo(Hash(legacy)));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void P6PaidTicketFocusRngAndAllOuterStateRollbackAcrossFiveFileFailures(CombatWriteStage stage)
        {
            PaidSetup();Run(ProfileCommandKind.SaveCurrent);string outer=Oracle(profile.State),inner=Oracle(CState),disk=File.ReadAllText(path);var command=Smite();
            profile.Store.WriteFault=at=>{if(at==stage)throw new IOException("p6-injected-"+stage);};var failed=profile.ExecuteCombat(command);
            Assert.That(failed.success,Is.False);Assert.That(failed.reason,Does.Contain("p6-injected-"+stage));Assert.That(Oracle(profile.State),Is.EqualTo(outer));Assert.That(Oracle(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            Reload();var success=profile.ExecuteCombat(command);Assert.That(success.success,Is.True,success.reason);Assert.That(CState.charges,Is.EqualTo(1));string paid=Oracle(CState);Reload();Assert.That(profile.ExecuteCombat(command).duplicate);Assert.That(Oracle(CState),Is.EqualTo(paid));
        }
    }
}
