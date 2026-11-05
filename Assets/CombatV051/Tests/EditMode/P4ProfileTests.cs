using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P4ProfileTests:ProfileTestFixture
    {
        string directory,path;
        [SetUp] public void PrepareP4Files()
        {
            directory=Path.Combine(Path.GetTempPath(),"CombatV052ProfileRules",Guid.NewGuid().ToString("N"));
            path=Path.Combine(directory,"P4","profile.json");
            profile=new ProfileSession(profile.State,profile.BaseCombatConfig,profile.Config,path);profile.Store.Save(profile.State);
        }
        [TearDown] public void DeleteP4Files(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        void Save(){Assert.That(profile.SaveCurrent("p4-save-"+(++seq)).success);}
        void Reload(){profile=ProfileSession.Load(path,BaseConfig,profile.Config);}
        PartyPreparation Draft(string actor,string spec)
        {var p=profile.PreparationDraft;p.actors.Single(a=>a.actorId==actor).specializationId=spec;return p;}

        [Test] public void NewProfileHasThreeDistinctDefaultsAndNineConcretePerActorDefinitions()
        {
            Assert.That(profile.State.schemaVersion,Is.EqualTo(5));Assert.That(ProfilePreparationCatalog.All.Count,Is.EqualTo(9));
            foreach(var role in new[]{ActorRole.Fighter,ActorRole.Rogue,ActorRole.Mage})
                Assert.That(ProfilePreparationCatalog.All.Count(d=>d.role==role),Is.EqualTo(3));
            Assert.That(profile.State.meta.unlockedSpecializations,Is.EquivalentTo(new[]{"F_GUARD","R_FLANK","M_INSIGHT"}));
            Assert.That(profile.ActiveCombat.Config.RulesFor("fighter").protectionRange,Is.EqualTo(4.5f));
            Assert.That(profile.ActiveCombat.Config.RulesFor("rogue").flankWindowSeconds,Is.EqualTo(6));
            Assert.That(profile.ActiveCombat.Config.RulesFor("mage").studySeconds,Is.EqualTo(.8f));
            var mutable=ProfilePreparationCatalog.Find("F_GUARD");mutable.effects.protectionRange=99;
            Assert.That(ProfilePreparationCatalog.Find("F_GUARD").effects.protectionRange,Is.EqualTo(4.5f));
        }
        [Test] public void PaidUnlockIsAtomicIdempotentAndDoesNotEquipOrSpendTwice()
        {
            Assert.That(profile.UnlockSpecialization("F_ADEPT","locked").reason,Is.EqualTo("research_unavailable"));
            profile.State.meta.research=3;string before=CombatJson.Write(M.departurePreparation);
            Assert.That(profile.UnlockSpecialization("F_ADEPT","purchase").success);Assert.That(profile.UnlockSpecialization("F_ADEPT","purchase").duplicate);
            Assert.That(profile.UnlockSpecialization("F_ADEPT","already-owned").reason,Is.EqualTo("already_unlocked"));
            Assert.That(profile.State.meta.research,Is.Zero);Assert.That(CombatJson.Write(M.departurePreparation),Is.EqualTo(before));
            Reload();Assert.That(profile.SpecializationUnlocked("F_ADEPT"));Assert.That(profile.State.meta.research,Is.Zero);
        }
        [Test] public void StoryMilestonesUnlockOptionsWithoutRequiringChallengeOrResearch()
        {
            First();Assert.That(profile.State.meta.unlockedSpecializations.Count,Is.EqualTo(6));
            Third();Assert.That(profile.State.meta.unlockedSpecializations.Count,Is.EqualTo(9));Assert.That(profile.State.meta.research,Is.Zero);
        }
        [Test] public void PreparationRejectsLockedWrongRoleDuplicateAndUnimplementedEquipment()
        {
            Assert.That(profile.SavePreparation(Draft("fighter","F_ADEPT"),"locked").reason,Is.EqualTo("specialization_locked"));
            Assert.That(profile.SavePreparation(Draft("fighter","M_INSIGHT"),"wrong-role").reason,Is.EqualTo("invalid_actor_specialization"));
            var draft=profile.PreparationDraft;draft.actors[1].actorId="fighter";
            Assert.That(profile.SavePreparation(draft,"duplicate").reason,Is.EqualTo("invalid_prepared_party"));
            draft=profile.PreparationDraft;draft.actors[0].weaponId="not-implemented";
            Assert.That(profile.SavePreparation(draft,"equipment").reason,Is.EqualTo("unsupported_equipment_or_prepared_actions"));
            draft=profile.PreparationDraft;draft.actors[0].preparedActionIds.Add("unimplemented");
            Assert.That(profile.SavePreparation(draft,"action").success,Is.False);
        }
        [Test] public void ApplyStoryPreparationPreservesAllActorResourcesTimersAndRestReceipts()
        {
            profile.State.meta.research=3;Assert.That(profile.UnlockSpecialization("M_WARD","ward-unlock").success);
            CState.Actor("fighter").hp=7;CState.Actor("fighter").majorRemaining=1.25f;CState.Actor("mage").spellSlots=0;CState.Actor("mage").inspiration=0;
            M.usedRestIds.Add(M.id+"/R6/rest");string actors=CombatJson.Write(CState.actors),rests=CombatJson.Write(M.usedRestIds);
            var draft=Draft("mage","M_WARD");draft.defaultActiveActorId="rogue";
            Assert.That(profile.ApplyStoryPreparation(draft,"apply").success);
            Assert.That(CState.activeActorId,Is.EqualTo("rogue"));Assert.That(CombatJson.Write(CState.actors),Is.EqualTo(actors));Assert.That(CombatJson.Write(M.usedRestIds),Is.EqualTo(rests));
            Assert.That(profile.ActiveCombat.Config.RulesFor("mage").allyShield);Assert.That(profile.ActiveCombat.Config.RulesFor("fighter").allyShield,Is.False);
            Reload();Assert.That(CombatJson.Write(CState.actors),Is.EqualTo(actors));
        }
        [Test] public void NewRunClonesDraftButContinuingRunCannotBorrowLaterDraftOrStoryConfiguration()
        {
            First();Travel("R0");var draft=Draft("rogue","R_MOBILE");draft.primaryLawId="COMMAND";draft.defaultActiveActorId="mage";
            Assert.That(profile.SavePreparation(draft,"draft-run").success);StartChallenge("COMMAND");
            string snapshot=CombatJson.Write(M.departurePreparation);Assert.That(CState.activeActorId,Is.EqualTo("mage"));
            Run(ProfileCommandKind.ContinueStory);Assert.That(profile.ApplyStoryPreparation(PartyPreparation.Default(),"change-story").success);
            Run(ProfileCommandKind.ContinueChallenge);Assert.That(CombatJson.Write(M.departurePreparation),Is.EqualTo(snapshot));
            Assert.That(profile.ActiveCombat.Config.RulesFor("rogue").preparationMoveMultiplier,Is.EqualTo(.85f));
            Assert.That(profile.ApplyStoryPreparation(PartyPreparation.Default(),"deny-run").reason,Is.EqualTo("story_preparation_point_required"));
            Save();Reload();Assert.That(CombatJson.Write(M.departurePreparation),Is.EqualTo(snapshot));
            M.departurePreparation.actorOverrides[0].protectionRange=99;
            Assert.That(profile.SaveCurrent("tamper-snapshot").reason,Is.EqualTo("departure_preparation_changed"));
        }
        [Test] public void EachSelectedSpecializationCompilesOnlyItsOwningActorsRules()
        {
            profile.State.meta.unlockedSpecializations=ProfilePreparationCatalog.All.Select(d=>d.id).ToList();
            foreach(var definition in ProfilePreparationCatalog.All)
            {
                string owner=ProfilePreparationCatalog.ActorId(definition.role);var draft=PartyPreparation.Default();draft.actors.Single(a=>a.actorId==owner).specializationId=definition.id;
                Assert.That(profile.ApplyStoryPreparation(draft,"spec-"+definition.id).success);
                var actual=CombatJson.Clone(profile.ActiveCombat.Config.RulesFor(owner));actual.actorId=null;
                Assert.That(CombatJson.Write(actual),Is.EqualTo(CombatJson.Write(definition.effects)),definition.id);
            }
        }
        [Test] public void NewEncounterAlwaysStartsWithTwoAndReserveChangesActualPreparationCooldown()
        {
            First(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="RESERVE");Travel("R0");
            var draft=profile.PreparationDraft;draft.primaryLawId="COMMAND";Assert.That(profile.ApplyStoryPreparation(draft,"law").success);
            Travel("R1");Travel("R2");Travel("R3");Activate();
            Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.chargeCap,Is.EqualTo(3));Assert.That(CState.momentum,Is.Zero);
            Assert.That(profile.ActiveCombat.Config.preparationRepeatSeconds,Is.EqualTo(5).Within(.00001f));
            Core(CommandKind.StartPreparation,c=>c.targetId="fighter");Assert.That(CState.Actor("fighter").preparationCooldown,Is.EqualTo(5).Within(.00001f));
            Save();Reload();Assert.That(CState.Actor("fighter").preparationCooldown,Is.EqualTo(5).Within(.00001f));
        }
        [Test] public void EncounterRegistrationHasOnlyConcreteThreatAndEnvironmentRootsAndLoadDoesNotReset()
        {
            First();Third();Upper();Travel("R7");Activate();Assert.That(CState.momentumLedger.encounterKind,Is.EqualTo("Boss"));
            Assert.That(CState.momentumLedger.registeredHighThreats,Is.EquivalentTo(CState.enemies.Select(a=>a.id)));
            Assert.That(CState.momentumLedger.registeredObjectives,Is.Empty);CState.momentumLedger.tacticalAwarded=17;Save();Reload();Assert.That(CState.momentumLedger.tacticalAwarded,Is.EqualTo(17));
        }
        [Test] public void ActivityClocksCountReadingOnceAndSeparatePauseAndFocusLossWithoutWorldSimulation()
        {
            profile.RecordActivity(3);profile.RecordActivity(4,explicitPaused:true);profile.RecordActivity(5,focusLost:true);
            profile.Tick(.5f);Assert.That(M.elapsedRealSeconds,Is.EqualTo(3.5f));Core(CommandKind.Pause);profile.Tick(2,false);
            Assert.That(M.elapsedRealSeconds,Is.EqualTo(3.5f));Assert.That(M.explicitPauseSeconds,Is.EqualTo(6));Assert.That(M.focusLostSeconds,Is.EqualTo(5));
            Save();Reload();Assert.That(M.elapsedRealSeconds,Is.EqualTo(3.5f));Assert.That(M.focusLostSeconds,Is.EqualTo(5));
        }
        [TestCase("current-momentum")]
        [TestCase("start-momentum")]
        [TestCase("current-preparation")]
        [TestCase("safe-preparation")]
        [TestCase("inactive-preparation")]
        public void FullProfileSaveValidationRejectsInvalidP4StateWithoutTouchingAnyFile(string location)
        {
            if(location=="inactive-preparation"){First();StartChallenge();Run(ProfileCommandKind.ContinueStory);}
            else if(location!="safe-preparation"){Travel("R1");Activate();}
            Save();string valid=CombatJson.Write(profile.State),disk=File.ReadAllText(path),backup=File.ReadAllText(path+".bak");
            string[] files=Directory.GetFiles(Path.GetDirectoryName(path)).OrderBy(f=>f).ToArray();
            var target=location=="start-momentum"?profile.ActiveEncounter.encounterStartSnapshot:
                location=="inactive-preparation"?profile.State.challenge.explorationCombat:CState;
            if(location.EndsWith("momentum",StringComparison.Ordinal)){target.charges=target.chargeCap;target.momentum=100;}
            else target.Actor("fighter").preparationCooldown=float.NaN;
            string invalidOuter=CombatJson.Write(profile.State),invalidBound=CombatJson.Write(CState);int writes=0;
            profile.Store.WriteFault=_=>writes++;
            var result=profile.SaveCurrent("invalid-p4-save-"+location);
            Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain(location.EndsWith("momentum",StringComparison.Ordinal)?"full_cap_momentum_must_be_zero":"invalid_preparation_cooldown"));
            Assert.That(writes,Is.Zero,"Read-only validation must finish before any write stage.");
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(invalidOuter));Assert.That(CombatJson.Write(CState),Is.EqualTo(invalidBound),"Rejected input is not silently repaired in memory.");
            Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Assert.That(File.ReadAllText(path+".bak"),Is.EqualTo(backup));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)).OrderBy(f=>f),Is.EqualTo(files));
            Reload();Assert.That(CombatJson.Write(profile.State),Is.EqualTo(valid));
        }
        static IEnumerable<TestCaseData> PreparationFailures()
        {foreach(string operation in new[]{"unlock","draft","apply"})foreach(CombatWriteStage stage in Enum.GetValues(typeof(CombatWriteStage)))yield return new TestCaseData(operation,stage);}
        [TestCaseSource(nameof(PreparationFailures))] public void PreparationWritesRollBackUnlockCostDraftActorsAndBinding(string operation,CombatWriteStage stage)
        {
            profile.State.meta.research=3;Save();var draft=profile.PreparationDraft;draft.defaultActiveActorId="rogue";
            var command=P(operation=="unlock"?ProfileCommandKind.UnlockSpecialization:operation=="draft"?ProfileCommandKind.SavePreparation:ProfileCommandKind.ApplyStoryPreparation,c=>{c.selectionId="F_ADEPT";c.preparation=draft;});
            string before=CombatJson.Write(profile.State),core=CombatJson.Write(CState),disk=File.ReadAllText(path);
            profile.Store.WriteFault=s=>{if(s==stage)throw new IOException("p4-injected");};var result=profile.Execute(command);
            Assert.That(result.success,Is.False);Assert.That(result.reason,Does.Contain("p4-injected"));Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));Assert.That(CombatJson.Write(CState),Is.EqualTo(core));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            Reload();Assert.That(profile.Execute(command).success);Assert.That(profile.Execute(command).duplicate);
        }

        // Construct the historical outer shape with historical plan hashes. Actual retained P3
        // Player saves are additionally inspected by the host's migration verification.
        string WriteLegacyFixture(bool corruptPlan=false)
        {
            Save();var payload=JObject.Parse(CombatJson.Write(profile.State));payload["schemaVersion"]=4;payload["contentVersion"]="actioncrpg-v0.5.1-p2";
            payload.Remove("migrationSourceDigest");payload.Remove("migrationPolicy");((JObject)payload["meta"]).Remove("lastPreparation");((JObject)payload["meta"]).Remove("unlockedSpecializations");
            foreach(string key in new[]{"story","challenge"})
            {
                var mode=payload[key] as JObject;if(mode==null)continue;mode.Remove("departurePreparation");mode.Remove("explicitPauseSeconds");mode.Remove("focusLostSeconds");
                var plan=new JObject{["version"]="dungeon-v051-p2-1",["rooms"]=new JArray(((JArray)mode["rooms"]).Select(r=>new JObject{["id"]=r["id"].DeepClone(),["templateId"]=r["templateId"].DeepClone(),["enemySpawns"]=r["enemySpawns"].DeepClone()})),["abilities"]=mode["departureAbilitySnapshot"].DeepClone(),["departureTraining"]=mode["departureTraining"].DeepClone()};
                mode["planHash"]=corruptPlan?"changed-plan":CombatStore.Digest(plan.ToString(Formatting.None));
            }
            string body=payload.ToString(Formatting.None);var envelope=new JObject{["schemaVersion"]=4,["contentVersion"]="actioncrpg-v0.5.1-p2",["integrityAlgorithm"]="sha256-utf8-payload-v1",["profileId"]=profile.State.profileId,["profileInstanceId"]=profile.State.profileInstanceId,["revision"]=profile.State.revision,["payload"]=body,["integrity"]=CombatStore.Digest(body)};
            string legacy=Path.Combine(directory,"P3","profile.json");Directory.CreateDirectory(Path.GetDirectoryName(legacy));File.WriteAllText(legacy,envelope.ToString(Formatting.None));return legacy;
        }
        [Test] public void LegacySafeMigrationRequiresSeparateFileAndCopiesExactResourcesWithoutRefill()
        {
            CState.Actor("fighter").hp=7;CState.Actor("mage").spellSlots=0;CState.Actor("mage").inspiration=0;string actors=CombatJson.Write(CState.actors);
            string old=WriteLegacyFixture(),before=File.ReadAllText(old),target=Path.Combine(directory,"Imported","profile.json");
            Assert.That(ProfileSession.InspectLegacyForP4(old).canMigrate);Assert.Throws<InvalidOperationException>(()=>ProfileSession.LoadLegacyIntoP4(old,old,BaseConfig,profile.Config));
            var migrated=ProfileSession.LoadLegacyIntoP4(old,target,BaseConfig,profile.Config);
            Assert.That(CombatJson.Write(migrated.ActiveCombat.State.actors),Is.EqualTo(actors));Assert.That(File.ReadAllText(old),Is.EqualTo(before));Assert.That(File.ReadAllText(target+".legacy-v051.json"),Is.EqualTo(before));
            Assert.That(migrated.State.migrationPolicy,Is.EqualTo("safe_exact_resources_v051_to_v052"));
            Assert.Throws<InvalidOperationException>(()=>ProfileSession.LoadLegacyIntoP4(old,target,BaseConfig,profile.Config));
        }
        [Test] public void LegacyActiveMigrationRequiresAcknowledgementAndRestoresEntireOriginalStart()
        {
            Travel("R1");Activate();var start=profile.ActiveEncounter.encounterStartSnapshot.Clone();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);CState.Actor("fighter").hp=1;CState.enemies[0].hp=1;CState.Actor("mage").x+=3;RandomStreams.D20(CState.random.backgroundCombat);
            string old=WriteLegacyFixture(),before=File.ReadAllText(old),target=Path.Combine(directory,"Imported","profile.json");
            Assert.That(ProfileSession.InspectLegacyForP4(old).requiresEncounterRollback);Assert.Throws<InvalidOperationException>(()=>ProfileSession.LoadLegacyIntoP4(old,target,BaseConfig,profile.Config));Assert.That(File.Exists(target),Is.False);
            var migrated=ProfileSession.LoadLegacyIntoP4(old,target,BaseConfig,profile.Config,true);start.encounterId=migrated.ActiveEncounter.attemptId;start.receipts.Clear();
            Assert.That(CombatJson.Write(migrated.ActiveCombat.State),Is.EqualTo(CombatJson.Write(start)));Assert.That(File.ReadAllText(old),Is.EqualTo(before));
        }
        [Test] public void LegacyPlanMismatchIsRejectedBeforeAnyDestinationWriteEvenWhenEnvelopeDigestMatches()
        {
            string old=WriteLegacyFixture(true),target=Path.Combine(directory,"Imported","profile.json");
            Assert.That(ProfileSession.InspectLegacyForP4(old).reason,Does.Contain("legacy_dungeon_plan_mismatch"));
            Assert.Throws<IOException>(()=>ProfileSession.LoadLegacyIntoP4(old,target,BaseConfig,profile.Config));Assert.That(File.Exists(target),Is.False);
        }
    }
}
