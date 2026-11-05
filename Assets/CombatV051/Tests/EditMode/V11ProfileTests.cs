using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Rule fixtures use explicit positions/deaths; they are not natural Player evidence.
    public sealed class V11ProfileTests:ProfileTestFixture
    {
        string folder,path;CombatConfig legacy,p5,p6;DungeonConfig dungeon;
        const string Paladin=P6CharacterCatalog.PaladinId,Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn";
        [SetUp] public void V11Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCrpgV11Profile",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();dungeon=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v11-profile",53,p6,p5,legacy,dungeon,path);seq=0;
        }
        [TearDown] public void V11Cleanup(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static string Json(object value){return JsonConvert.SerializeObject(value,Formatting.None,CombatJson.Settings);}
        void Ensure(){profile.SetNextV12Rules(null);var r=profile.EnsureV11Progress("v11-ensure");Assert.That(r.success,Is.True,r.reason);}
        void Reload(){profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon);profile.SetNextV12Rules(null);}
        void StartV11(){Ensure();Run(ProfileCommandKind.StartDemoExploration,c=>c.seed=79);Assert.That(profile.ActiveUsesV11);}
        void Unlock(string actor,string node){var r=profile.UnlockV11Node(actor,actor+"/"+node,"unlock-"+actor+"-"+node);Assert.That(r.success,Is.True,r.reason);}
        void Draft(params string[] ids)
        {var draft=profile.V11PreparationDraft;draft.teamIds=ids.ToList();draft.defaultActiveActorId=Paladin;draft.coreId=Paladin;var r=profile.SaveV11Preparation(draft,"draft-"+(++seq));Assert.That(r.success,Is.True,r.reason);}

        [Test] public void CatalogHasThreeMainFourRetainedCompanionsAndExactlyOnePlusTwoEach()
        {
            Assert.That(V11Catalog.Characters.Where(c=>c.category=="Main").Select(c=>c.id),Is.EqualTo(new[]{Evan,Mira,Finn}));
            Assert.That(V11Catalog.Characters.Where(c=>c.category=="Companion").Select(c=>c.id),Is.EquivalentTo(new[]{"fighter","mage","rogue",Paladin}));
            foreach(var c in V11Catalog.Characters){var nodes=V11Catalog.NodesFor(c.id);Assert.That(nodes.Count(n=>n.kind=="Passive"),Is.EqualTo(1));Assert.That(nodes.Count(n=>n.kind=="Active"),Is.EqualTo(2));}
            // V12 section 5.1 adds the three existing protagonists as legal cores.
            Assert.That(V11Catalog.Characters.Count(c=>c.coreSupported),Is.EqualTo(4));
            V11Catalog.FindCharacter(Evan).actionIds.Clear();Assert.That(V11Catalog.FindCharacter(Evan).actionIds.Count,Is.EqualTo(2));
        }
        [Test] public void CatalogJsonIsActuallyMappedIntoDomainAndInvalidDependenciesHaveLocatableErrors()
        {
            string root=TestContext.CurrentContext.TestDirectory,source=null;
            for(int i=0;i<12&&root!=null;i++,root=Directory.GetParent(root)?.FullName)
            {string candidate=Path.Combine(root,"Assets","CombatV051","Resources","V11Characters.json");if(File.Exists(candidate)){source=candidate;break;}}
            Assert.That(source,Is.Not.Null,"source fixture not located");
            Assert.That(JToken.DeepEquals(JToken.Parse(File.ReadAllText(source)),JToken.Parse(V11Catalog.SourceJson)),Is.True);
            var data=V11Catalog.Load(V11Catalog.SourceJson);var node=data.nodes.First(n=>n.id==Evan+"/passive");node.prerequisiteIds.Add(Evan+"/active2");
            Assert.That(Assert.Throws<InvalidOperationException>(()=>V11Catalog.Validate(data)).Message,Does.Contain("cycle"));
            data=V11Catalog.Load(V11Catalog.SourceJson);data.nodes.First(n=>n.id==Evan+"/active2").prerequisiteIds[0]=Mira+"/passive";
            Assert.That(Assert.Throws<InvalidOperationException>(()=>V11Catalog.Validate(data)).Message,Does.Contain("cross_character"));
        }
        [Test] public void NullExtensionsPreservePreviousConfigAndProfileBytesAndNoForcedMigrationOnLoad()
        {
            string before=File.ReadAllText(path),state=Json(profile.State),config=Json(p6);
            Assert.That(JObject.Parse(state)["meta"]["v11"],Is.Null);Assert.That(JObject.Parse(state)["story"]["v11Departure"],Is.Null);Assert.That(JObject.Parse(config)["v11"],Is.Null);Reload();
            Assert.That(Json(profile.State),Is.EqualTo(state));Assert.That(File.ReadAllText(path),Is.EqualTo(before));Assert.That(Json(p6),Is.EqualTo(config));Assert.That(profile.ActiveMode.v11Departure,Is.Null);
        }
        [Test] public void OnceOnlyAllowanceAndUnlockBlockThenUnlockPersistWithoutDoubleDebit()
        {
            Ensure();Assert.That(profile.V11Progress.points,Is.EqualTo(6));var locked=profile.QuoteV11Node(Evan,Evan+"/active2");Assert.That(locked.canUnlock,Is.False);Assert.That(locked.reason,Does.StartWith("v11_prerequisite_required"));
            string before=File.ReadAllText(path);Assert.That(profile.UnlockV11Node(Evan,Evan+"/active2","too-soon").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(before));
            Unlock(Evan,"passive");Unlock(Evan,"active2");Assert.That(profile.V11Progress.points,Is.EqualTo(4));Reload();
            Assert.That(profile.QuoteV11Node(Evan,Evan+"/active2").status,Is.EqualTo("Unlocked"));Assert.That(profile.EnsureV11Progress("ensure-again").success);
            Assert.That(profile.UnlockV11Node(Evan,Evan+"/active2","unlock-again").success);Assert.That(profile.V11Progress.points,Is.EqualTo(4));
            var repeat=profile.UnlockV11Node(Evan,Evan+"/active2","unlock-again");Assert.That(repeat.success&&repeat.duplicate);
            Assert.That(profile.UnlockV11Node(Mira,Evan+"/passive","cross-owner").success,Is.False);
        }
        [Test] public void AllSixNewUnlocksSpendOnlyDemoAllowanceAndDoNotSpendOldResearch()
        {
            Ensure();profile.State.meta.research=7;Run(ProfileCommandKind.SaveCurrent);
            foreach(string id in new[]{Evan,Mira,Finn}){Unlock(id,"passive");Unlock(id,"active2");}
            Assert.That(profile.V11Progress.points,Is.Zero);Assert.That(profile.State.meta.research,Is.EqualTo(7));Assert.That(profile.V11Progress.unlockedNodeIds.Count,Is.EqualTo(21));Reload();Assert.That(profile.V11Progress.unlockedNodeIds.Count,Is.EqualTo(21));
        }
        [Test] public void DraftRejectsDuplicateAndUnknownAndRemovedCoreRequiresExplicitReselection()
        {
            Ensure();var d=profile.V11PreparationDraft;d.teamIds[1]=Paladin;Assert.That(profile.SaveV11Preparation(d,"dup").reason,Is.EqualTo("v11_three_distinct_characters_required"));
            d=profile.V11PreparationDraft;d.teamIds[1]="missing-old-id";Assert.That(profile.SaveV11Preparation(d,"unknown").reason,Is.EqualTo("v11_unknown_character:missing-old-id"));
            d=new V11PreparationDraft{teamIds=new System.Collections.Generic.List<string>{Evan,Mira,Finn},defaultActiveActorId=Evan,coreId=null};
            Assert.That(profile.SaveV11Preparation(d,"remove-core").success);Reload();Assert.That(profile.V11PreparationDraft.coreId,Is.Null);Assert.That(profile.QuoteV11Departure().reason,Is.EqualTo("v11_core_required"));
            Assert.That(profile.Execute(P(ProfileCommandKind.StartDemoExploration)).success,Is.False);d.coreId=Evan;Assert.That(profile.SaveV11Preparation(d,"unsupported-core").success);Assert.That(profile.QuoteV11Departure().reason,Is.EqualTo("v11_core_pool_unavailable"));
        }
        [Test] public void ExistingPaidP6RunKeepsExactTicketActorResourcesAndSnapshotAfterMetaMigration()
        {
            Travel("R1");Activate();var a=CState.Actor(Paladin);var e=CState.enemies[0];e.x=a.x+1;e.z=a.z;Core(CommandKind.EnterFocus,c=>c.actorId=Paladin);
            Core(CommandKind.ConfirmRoll,c=>{c.actorId=Paladin;c.actionId=CombatSession.P6SmiteId;c.targetId=e.id;});
            string combat=Json(CState),departure=Json(M.p6Departure),preparation=Json(profile.State.meta.p6Preparation),legacyPreparation=Json(profile.State.meta.lastPreparation);
            Ensure();Unlock(Evan,"passive");Draft(Paladin,Evan,"fighter");
            Assert.That(Json(CState),Is.EqualTo(combat));Assert.That(Json(M.p6Departure),Is.EqualTo(departure));Assert.That(Json(profile.State.meta.p6Preparation),Is.EqualTo(preparation));Assert.That(Json(profile.State.meta.lastPreparation),Is.EqualTo(legacyPreparation));
            Reload();Assert.That(Json(CState),Is.EqualTo(combat));Assert.That(profile.ActiveUsesV11,Is.False);
        }
        [Test] public void SameRoleCharactersUseIndependentSnapshotsAndNoPassiveReapplyOnContinueOrNewRoom()
        {
            Ensure();Draft(Paladin,Evan,"mage");Run(ProfileCommandKind.StartDemoExploration);Assert.That(CState.actors.Select(a=>a.id),Is.EqualTo(new[]{Paladin,Evan,"mage"}));
            Assert.That(CState.Actor(Evan).intelligenceModifier,Is.EqualTo(3));Assert.That(CState.Actor("mage").intelligenceModifier,Is.EqualTo(4));
            int hp=CState.Actor(Paladin).maxHp;CState.Actor(Paladin).hp=5;Run(ProfileCommandKind.SaveCurrent);Unlock(Evan,"passive");
            Assert.That(profile.ActiveCombat.Config.v11.actors.Find(a=>a.actorId==Evan).passiveUnlocked,Is.False);Reload();Assert.That(CState.Actor(Paladin).hp,Is.EqualTo(5));Assert.That(CState.Actor(Paladin).maxHp,Is.EqualTo(hp));
            Travel("R1");Activate();Assert.That(CState.Actor(Paladin).hp,Is.EqualTo(5));Assert.That(CState.Actor(Paladin).maxHp,Is.EqualTo(hp));Assert.That(CState.Actor(Evan).intelligenceModifier,Is.EqualTo(3));
        }
        [Test] public void NaturalRewardRngOfferIsSavedClaimedExactlyOnceAndNewRunClearsBuffs()
        {
            StartV11();First(false);var offer=profile.PendingReward;Assert.That(offer.candidates.Count,Is.EqualTo(3));Assert.That(offer.candidates.Any(id=>V11BuffCatalog.Find(id).coreActorId==Paladin));
            string offered=Json(offer),rng=Json(M.random.contentReward);Reload();Assert.That(Json(profile.PendingReward),Is.EqualTo(offered));Assert.That(Json(M.random.contentReward),Is.EqualTo(rng));
            string pick=profile.PendingReward.candidates[0];var c=P(ProfileCommandKind.ClaimReward,x=>x.selectionId=pick);Assert.That(profile.Execute(c).success);Assert.That(profile.Execute(c).duplicate);Assert.That(M.v11Build.buffs.Single().stacks,Is.EqualTo(1));
            string buffs=Json(M.v11Build);Reload();Assert.That(Json(M.v11Build),Is.EqualTo(buffs));Assert.That(profile.ActiveCombat.Config.v11.buffs.Single().id,Is.EqualTo(pick));
            Run(ProfileCommandKind.SettlementOnce,x=>x.selectionId="abandon");Run(ProfileCommandKind.StartDemoExploration);Assert.That(M.v11Build.buffs,Is.Empty);Assert.That(profile.ActiveCombat.Config.v11.buffs,Is.Empty);
        }
        [Test] public void UnsupportedOrPrerequisiteOnlyBuffsAreNeverOfferedAndClaimRechecksEligibility()
        {
            Ensure();Draft(Paladin,Mira,"rogue");Run(ProfileCommandKind.StartDemoExploration);var eligible=V11BuffCatalog.Eligible(profile.ActiveCombat.Config.v11).Select(d=>d.id).ToArray();
            Assert.That(eligible,Does.Not.Contain("core_radiant_resonance"));Assert.That(eligible,Does.Not.Contain("core_full_dice"));
            First(false);string before=Json(M.v11Build);Assert.That(profile.Execute(P(ProfileCommandKind.ClaimReward,c=>c.selectionId="core_full_dice")).success,Is.False);Assert.That(Json(M.v11Build),Is.EqualTo(before));
        }
        [Test] public void UnknownNodeProgressIsRetainedAndReportedWithoutGrantingOrClearing()
        {
            Ensure();profile.State.meta.v11.unlockedNodeIds.Add("future-character/kept-node");Run(ProfileCommandKind.SaveCurrent);Reload();
            Assert.That(profile.V11UnknownNodeIds,Does.Contain("future-character/kept-node"));Assert.That(profile.V11Progress.unlockedNodeIds,Does.Contain("future-character/kept-node"));Assert.That(profile.QuoteV11Node("future-character","future-character/kept-node").status,Is.EqualTo("Invalid"));
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void NodePointAndProgressCommitRollsBackAcrossAllWriteFailures(CombatWriteStage stage)
        {
            Ensure();string old=Json(profile.State),disk=File.ReadAllText(path);profile.Store.WriteFault=at=>{if(at==stage)throw new IOException("v11-fault-"+stage);};
            var fail=profile.UnlockV11Node(Evan,Evan+"/passive","atomic-unlock");Assert.That(fail.success,Is.False);Assert.That(fail.reason,Does.Contain("v11-fault"));Assert.That(Json(profile.State),Is.EqualTo(old));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            Reload();var ok=profile.UnlockV11Node(Evan,Evan+"/passive","atomic-unlock");Assert.That(ok.success,Is.True,ok.reason);Assert.That(profile.V11Progress.points,Is.EqualTo(5));Reload();Assert.That(profile.UnlockV11Node(Evan,Evan+"/passive","atomic-unlock").duplicate);Assert.That(profile.V11Progress.points,Is.EqualTo(5));
        }
        [TestCase("P4")] [TestCase("P5")] [TestCase("P6")]
        public void EachExistingPaidRulesetRemainsByteExactWhenV11MetaIsInitializedAndReloaded(string ruleset)
        {
            string oldPath=Path.Combine(folder,"historical-"+ruleset+".json");
            profile=ruleset=="P4"?ProfileSession.CreateNew("historical",53,legacy,dungeon,oldPath):ruleset=="P5"?ProfileSession.CreateNewCompatible("historical",53,p5,legacy,dungeon,oldPath):ProfileSession.CreateNewCompatible("historical",53,p6,p5,legacy,dungeon,oldPath);
            Travel("R1");Activate();string actorId=ruleset=="P6"?Paladin:"fighter";var actor=CState.Actor(actorId);var enemy=CState.enemies[0];enemy.x=actor.x+1;enemy.z=actor.z;
            actor.hp=7;actor.inspiration=0;CState.Actor("mage").spellSlots=0;
            Core(CommandKind.EnterFocus,c=>c.actorId=actorId);Core(CommandKind.ConfirmRoll,c=>{c.actorId=actorId;c.targetId=enemy.id;c.actionId=ruleset=="P6"?CombatSession.P6SmiteId:"attack";});
            string oldCombat=Json(CState),oldMode=Json(M),oldRules=Json(profile.ActiveCombat.Config);profile=ProfileSession.LoadCompatible(oldPath,p6,p5,legacy,dungeon);
            Assert.That(Json(CState),Is.EqualTo(oldCombat));Assert.That(Json(M),Is.EqualTo(oldMode));Ensure();Unlock(Evan,"passive");Draft(Paladin,Evan,"fighter");
            profile=ProfileSession.LoadCompatible(oldPath,p6,p5,legacy,dungeon);
            Assert.That(Json(CState),Is.EqualTo(oldCombat));Assert.That(Json(M),Is.EqualTo(oldMode));Assert.That(Json(profile.ActiveCombat.Config),Is.EqualTo(oldRules));Assert.That(CState.actors.All(a=>a.v11==null));
        }
        [Test] public void StorySwitchAndTreeChangesCannotRewriteOrRefillTheDepartedChallenge()
        {
            StartV11();First(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId=profile.PendingReward.candidates[0]);
            CState.Actor(Paladin).hp=7;CState.Actor("mage").spellSlots=0;CState.Actor("rogue").inspiration=0;Run(ProfileCommandKind.SaveCurrent);
            string challenge=Json(M),combat=Json(CState),rules=Json(profile.ActiveCombat.Config);
            Run(ProfileCommandKind.ContinueStory);Assert.That(profile.ActiveUsesV11,Is.False);Assert.That(CState.actors.All(a=>a.v11==null));Unlock(Mira,"passive");Draft(Paladin,Mira,Finn);Run(ProfileCommandKind.ContinueChallenge);
            Assert.That(Json(M),Is.EqualTo(challenge));Assert.That(Json(CState),Is.EqualTo(combat));Assert.That(Json(profile.ActiveCombat.Config),Is.EqualTo(rules));Reload();Assert.That(Json(M),Is.EqualTo(challenge));
        }
        [TestCase("core_full_dice")] [TestCase("core_radiant_resonance")]
        public void RestoredOfferedRewardWithMissingPrerequisiteOrMissingRelevantTeammateIsRejected(string illegalCandidate)
        {
            Ensure();Draft(Paladin,Mira,"rogue");Run(ProfileCommandKind.StartDemoExploration);First(false);
            string disk=File.ReadAllText(path);M.pendingReward.candidates.Clear();M.pendingReward.candidates.Add(illegalCandidate);
            var result=profile.SaveCurrent("invalid-restored-offer");Assert.That(result.success,Is.False,"restored offer must reject unusable candidate "+illegalCandidate);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void RestoredAlreadyClaimedRewardCannotBeReopenedAndPaidUnderAnotherCommandId()
        {
            StartV11();First(false);string id=profile.PendingReward.id,pick=profile.PendingReward.candidates[0];Run(ProfileCommandKind.ClaimReward,c=>c.selectionId=pick);
            string disk=File.ReadAllText(path);M.pendingReward.stage="Offered";M.pendingReward.chosenId=null;
            var result=profile.SaveCurrent("reopened-reward");Assert.That(result.success,Is.False,"claimed offer cannot be reopened in a restorable profile");Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
    }
}
