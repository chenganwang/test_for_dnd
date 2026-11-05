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
    // Deterministic snapshot/transaction fixtures, not real Player or balance evidence.
    public sealed class V12ProfileTests
    {
        const string Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn",Paladin=P6CharacterCatalog.PaladinId;
        string folder,path;CombatConfig legacy,p5,p6;DungeonConfig dungeon;ProfileSession profile;int sequence;
        static string Json(object value)=>JsonConvert.SerializeObject(value,Formatting.None,CombatJson.Settings);
        [SetUp] public void SetUp()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCrpgV12Profile",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();dungeon=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v12-profile",181,p6,p5,legacy,dungeon,path);
            AssertOk(profile.EnsureV11Progress("progress"));
        }
        [TearDown] public void TearDown(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static void AssertOk(ProfileResult result)=>Assert.That(result.success,Is.True,result.reason);
        void Run(ProfileCommandKind kind,string selection=null)=>AssertOk(profile.Execute(new ProfileCommand{id="v12-test-"+(++sequence),kind=kind,selectionId=selection}));
        void Reload()=>profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon);
        void Draft(string core,params string[] team)=>AssertOk(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=team.ToList(),coreId=core,defaultActiveActorId=team[0]},"draft-"+(++sequence)));
        void Unlock(string actor)=>AssertOk(profile.UnlockV11Node(actor,actor+"/passive","unlock-"+actor));

        [Test] public void SameCharacterNodeAndPassiveIdentitiesHaveVersionedActionDefinitions()
        {
            Assert.That(V11Catalog.Characters.Count,Is.EqualTo(7));
            Assert.That(V11Catalog.Characters.Where(c=>c.coreSupported).Select(c=>c.id),Is.EquivalentTo(new[]{Evan,Mira,Finn,Paladin}));
            foreach(string id in new[]{Evan,Mira,Finn})
            {
                var old=V11Catalog.FindCharacterForRules(id,"v11-character-1");var current=V11Catalog.FindCharacterForRules(id,"v12-character-1");
                Assert.That(current.id,Is.EqualTo(old.id));Assert.That(current.passiveId,Is.EqualTo(old.passiveId));Assert.That(current.weaponId,Is.EqualTo(old.weaponId));
                Assert.That(current.actionIds.Count,Is.EqualTo(2));Assert.That(current.actionIds.All(x=>x.StartsWith("v12_")),Is.True);
                Assert.That(V11Catalog.NodesFor(id).Select(n=>n.id),Is.EquivalentTo(new[]{id+"/passive",id+"/active1",id+"/active2"}));
            }
            Assert.That(V11Catalog.FindCharacterForRules(Evan,"v11-character-1").actionIds,Is.EqualTo(new[]{CombatSession.P6BindId,"v11_support_ward"}));
            Assert.That(V11Catalog.FindCharacter(Paladin).actionIds,Is.EqualTo(V11Catalog.FindCharacterForRules(Paladin,"v11-character-1").actionIds));
        }

        [Test] public void NewDepartureGrantsBothActionsWithoutPurchasingPermanentNodesOrChangingPoints()
        {
            Draft(Mira,Evan,Mira,Finn);string progress=Json(profile.State.meta.v11);
            Run(ProfileCommandKind.StartDemoExploration);
            Assert.That(profile.ActiveMode.v11Departure.version,Is.EqualTo("v12-departure-1"));Assert.That(profile.ActiveCombat.Config.v11.version,Is.EqualTo("v12-character-1"));
            Assert.That(profile.ActiveCombat.Config.v12,Is.Not.Null);Assert.That(Json(profile.State.meta.v11),Is.EqualTo(progress));
            foreach(var actor in profile.ActiveCombat.Config.v11.actors)
            {Assert.That(actor.unlockedActionIds,Is.EqualTo(V11Catalog.FindCharacter(actor.actorId).actionIds));Assert.That(actor.passiveUnlocked,Is.False);}
            Assert.That(profile.QuoteV11Node(Evan,Evan+"/active2").reason,Does.StartWith("v11_prerequisite_required"));
            string before=Json(profile.ActiveMode);Reload();Assert.That(Json(profile.ActiveMode),Is.EqualTo(before));
        }

        [Test] public void PurchasedPassiveAndUnknownNodeRightsPersistAndCannotDebitTwice()
        {
            Unlock(Evan);profile.State.meta.v11.unlockedNodeIds.Add("future-author-node/retain");AssertOk(profile.SaveCurrent("keep-unknown"));Draft(Evan,Evan,Mira,Finn);
            int points=profile.V11Progress.points;Run(ProfileCommandKind.StartDemoExploration);Assert.That(profile.ActiveCombat.Config.v11.actors[0].passiveUnlocked,Is.True);
            string departed=Json(profile.ActiveMode);Reload();Unlock(Evan);Assert.That(profile.V11Progress.points,Is.EqualTo(points));Assert.That(profile.V11UnknownNodeIds,Does.Contain("future-author-node/retain"));Assert.That(Json(profile.ActiveMode),Is.EqualTo(departed));
        }

        [Test] public void ExistingV11DepartureContinuesExactOldActionsResourcesAndNullNewRules()
        {
            profile.SetNextV12Rules(null);Unlock(Evan);Draft(Paladin,Paladin,Evan,Finn);Run(ProfileCommandKind.StartDemoExploration);
            Assert.That(profile.ActiveCombat.Config.v11.version,Is.EqualTo("v11-character-1"));Assert.That(profile.ActiveCombat.Config.v12,Is.Null);
            profile.ActiveCombat.State.Actor(Paladin).hp=7;profile.ActiveCombat.State.Actor(Evan).spellSlots=0;AssertOk(profile.SaveCurrent("old-wounded"));
            string state=Json(profile.ActiveMode),combat=Json(profile.ActiveCombat.State),config=Json(profile.ActiveCombat.Config),disk=File.ReadAllText(path);
            Reload();Assert.That(Json(profile.ActiveMode),Is.EqualTo(state));Assert.That(Json(profile.ActiveCombat.State),Is.EqualTo(combat));Assert.That(Json(profile.ActiveCombat.Config),Is.EqualTo(config));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            Assert.That(profile.ActiveCombat.Config.v11.actors.Find(a=>a.actorId==Evan).unlockedActionIds,Is.EqualTo(new[]{CombatSession.P6BindId}));
            Assert.That(profile.ActiveCombat.State.Actor(Evan).intelligenceModifier,Is.EqualTo(4));
            Run(ProfileCommandKind.SettlementOnce,"abandon");Run(ProfileCommandKind.StartDemoExploration);
            Assert.That(profile.ActiveCombat.Config.v11.version,Is.EqualTo("v12-character-1"));Assert.That(profile.ActiveCombat.Config.v11.actors.Find(a=>a.actorId==Evan).unlockedActionIds,Does.Contain("v12_evan_final"));
        }

        [Test] public void FutureParameterEditChangesNextDepartureOnlyAndRestoredSnapshotIsIndependent()
        {
            Draft(Finn,Evan,Mira,Finn);var config=V12Defaults.Create();config.markSeconds=7;profile.SetNextV12Rules(config);config.markSeconds=99;
            Run(ProfileCommandKind.StartDemoExploration);Assert.That(profile.ActiveCombat.Config.v12.markSeconds,Is.EqualTo(7));
            var next=V12Defaults.Create();next.markSeconds=8;profile.SetNextV12Rules(next);Assert.That(profile.ActiveCombat.Config.v12.markSeconds,Is.EqualTo(7));
            string state=Json(profile.ActiveMode);Reload();Assert.That(Json(profile.ActiveMode),Is.EqualTo(state));Assert.That(profile.ActiveCombat.Config.v12.markSeconds,Is.EqualTo(7));
        }

        [Test] public void MutatedDepartureRulesCannotOverwriteTheLastValidSave()
        {
            Draft(Mira,Evan,Mira,Finn);Run(ProfileCommandKind.StartDemoExploration);string disk=File.ReadAllText(path);
            profile.ActiveMode.v11Departure.v12.markSeconds=88;
            Assert.That(profile.SaveCurrent("mutated-departure").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();Assert.That(profile.ActiveMode.v11Departure.v12.markSeconds,Is.EqualTo(6));
        }

        [Test] public void MappedParameterResourceMatchesTheActualDomainInput()
        {
            string root=TestContext.CurrentContext.TestDirectory,source=null;
            for(int i=0;i<12&&root!=null;i++,root=Directory.GetParent(root)?.FullName){string p=Path.Combine(root,"Assets","CombatV051","Resources","V12CombatConfig.json");if(File.Exists(p)){source=p;break;}}
            Assert.That(source,Is.Not.Null);Assert.That(JToken.DeepEquals(JToken.Parse(File.ReadAllText(source)),JToken.Parse(V12Defaults.SourceJson)),Is.True);
            Assert.That(V12Defaults.Create().parasiteSeconds,Is.EqualTo(6));Assert.That(V12Defaults.Create().groveMaximumEnemies,Is.EqualTo(4));
        }
    }
}
