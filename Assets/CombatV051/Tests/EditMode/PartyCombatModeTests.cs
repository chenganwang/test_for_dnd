using System;
using System.Collections.Generic;
using System.Reflection;
using CombatV051.Domain;
using Newtonsoft.Json;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class PartyCombatModeTests
    {
        const string Original="{\"teamIds\":[\"char_demo_paladin\",\"mage\",\"rogue\"],\"coreId\":\"char_demo_paladin\",\"defaultActiveActorId\":\"char_demo_paladin\"}";
        ProfileSession Profile()
        {
            var legacy=new CombatConfig();var p5=CombatJson.Clone(legacy);p5.p5=new P5Config();var p6=CombatJson.Clone(p5);p6.p6=new P6Config();
            var profile=ProfileSession.CreateNewCompatible("party-mode",51,p6,p5,legacy,new DungeonConfig());Assert.That(profile.EnsureV11Progress("progress").success);return profile;
        }
        [Test] public void MissingFieldAndExplicitZeroKeepOriginalDraftBytes()
        {
            var draft=CombatJson.Read<V11PreparationDraft>(Original);Assert.That(draft.partyCombatMode,Is.EqualTo(PartyCombatMode.PersistentTrio));
            Assert.That(CombatJson.Write(draft),Is.EqualTo(Original));draft.partyCombatMode=PartyCombatMode.PersistentTrio;Assert.That(JsonConvert.SerializeObject(draft,Formatting.None,CombatJson.Settings),Is.EqualTo(Original));
            Assert.That((int)PartyCombatMode.PersistentTrio,Is.Zero);Assert.That((int)PartyCombatMode.CoreAndAssist,Is.EqualTo(1));
        }
        [TestCase("v11-departure-1")][TestCase("v12-departure-1")]
        public void ExistingDepartureHashProjectionIsByteIdentical(string version)
        {
            var d=new V11DepartureSnapshot{version=version,catalogHash="historical-catalog",preparation=CombatJson.Read<V11PreparationDraft>(Original),actors=new List<V11ActorLoadout>(),v12=version=="v12-departure-1"?V12Defaults.Create():null};
            var p=d.preparation;var oldPreparation=new {p.teamIds,p.coreId,p.defaultActiveActorId};
            string old=version=="v11-departure-1"?CombatJson.Write(new{d.version,d.catalogHash,preparation=oldPreparation,d.actors}):CombatJson.Write(new{d.version,d.catalogHash,preparation=oldPreparation,d.actors,d.v12});
            var hash=typeof(ProfileSession).GetMethod("V11DepartureHash",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.That(hash.Invoke(null,new object[]{d}),Is.EqualTo(CombatStore.Digest(old)));
        }
        [TestCase(19,"party_combat_mode_invalid")]
        public void UnsupportedPreparationRequestIsRejectedWithoutChangingProgressOrRunningTrioInstead(int mode,string reason)
        {
            var profile=Profile();string before=CombatJson.Write(profile.State);var draft=profile.V11PreparationDraft;draft.partyCombatMode=(PartyCombatMode)mode;
            Assert.That(profile.QuoteV11Departure(draft).reason,Is.EqualTo(reason));Assert.That(profile.SaveV11Preparation(draft,"unsupported").reason,Is.EqualTo(reason));Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));
            Assert.That(profile.ActivePartyCombatMode,Is.EqualTo(PartyCombatMode.PersistentTrio));
        }
        [Test] public void UnsupportedRestoredPreparationIsRejectedInsteadOfReinterpreted()
        {
            var profile=Profile();profile.State.meta.v11.preparation.partyCombatMode=PartyCombatMode.CoreAndAssist;
            Assert.DoesNotThrow(()=>profile.Validate(profile.State));
        }
        [Test] public void GeneratedCloneMustRetainUnsupportedModeUntilValidationRejectsIt()
        {
            var profile=Profile();var draft=profile.V11PreparationDraft;draft.partyCombatMode=PartyCombatMode.CoreAndAssist;
            var clone=CombatJson.Clone(draft);Assert.That(clone.partyCombatMode,Is.EqualTo(PartyCombatMode.CoreAndAssist),"Regenerate DTO clone; dropping the mode would silently reinterpret the request.");
            Assert.That(profile.SaveV11Preparation(clone,"save-cloned-assist").success,Is.True);
        }
        [Test] public void NewDepartureAndSkillReservationKeepExistingIdsAndThreeEntities()
        {
            var profile=Profile();Assert.That(profile.Execute(new ProfileCommand{id="depart",kind=ProfileCommandKind.StartDemoExploration}).success);
            Assert.That(profile.ActivePartyCombatMode,Is.EqualTo(PartyCombatMode.PersistentTrio));Assert.That(profile.ActiveCombat.State.actors.Count,Is.EqualTo(3));
            Assert.That(profile.ActiveMode.v11Departure.preparation.teamIds,Is.EqualTo(new[]{P6CharacterCatalog.PaladinId,"mage","rogue"}));
            foreach(var actor in profile.ActiveCombat.Config.v11.actors)foreach(string id in actor.unlockedActionIds)Assert.That(PartyCombatModePolicy.SkillUsageFor(id),Is.EqualTo(SkillUsageMode.Both));
        }
    }
}
