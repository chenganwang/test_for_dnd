using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Controlled profile transactions, not an actual Player completion claim.
    public sealed class V14BuildRewardTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        ProfileSession profile;CombatConfig legacy,p5,p6;DungeonConfig map;string folder,path;int serial;
        CombatState S=>profile.ActiveCombat.State;V11CombatRules Rules=>profile.ActiveCombat.Config.v11;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Rewards",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();map=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v14-rewards",181,p6,p5,legacy,map,path);Good(profile.EnsureV11Progress("progress"));profile.EnableV14Departures();
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static void Good(ProfileResult r)=>Assert.That(r.success,Is.True,r.reason);
        void Run(ProfileCommandKind kind,string room=null,string pick=null)=>Good(profile.Execute(new ProfileCommand{id="v14-rewards/"+(++serial),kind=kind,roomId=room,selectionId=pick,qualified=true}));
        void Start(string core,PartyCombatMode mode,string tree="growth",string second="v14_finn_detonate")
        {
            var team=core==P6CharacterCatalog.PaladinId?new List<string>{core,E,F}:new List<string>{E,M,F};
            Good(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=team,coreId=core,defaultActiveActorId=core,partyCombatMode=mode,finnTree=tree,finnGrowthAction=second},"draft"));Run(ProfileCommandKind.StartDemoExploration);
        }
        void Travel(string room){var r=profile.ActiveDungeonConfig.Room(room);foreach(var a in S.actors){a.x=r.x;a.z=r.z;}Run(ProfileCommandKind.EnterRoom,room);}
        void Win(){Run(ProfileCommandKind.ActivateEncounter);foreach(var enemy in S.enemies)enemy.hp=0;Run(ProfileCommandKind.ResolveEncounter);}
        void Claim(string expected=null)
        {
            var offer=profile.PendingReward;Assert.That(offer.candidates.Count,Is.LessThanOrEqualTo(3));Assert.That(offer.candidates.Count,Is.GreaterThan(0));
            if(expected!=null)Assert.That(offer.candidates[0],Is.EqualTo(expected));else Assert.That(offer.candidates.All(id=>V11BuffCatalog.Find(id).pool=="Generic"),Is.True);
            Run(ProfileCommandKind.ClaimReward,pick:expected??offer.candidates[0]);
        }
        [TestCase(E,PartyCombatMode.CoreAndAssist)][TestCase(M,PartyCombatMode.CoreAndAssist)][TestCase(F,PartyCombatMode.CoreAndAssist)]
        [TestCase(E,PartyCombatMode.PersistentTrio)][TestCase(M,PartyCombatMode.PersistentTrio)][TestCase(F,PartyCombatMode.PersistentTrio)]
        public void ThreeIndependentRouteCardsAreGuaranteedAtTheirActualRoomAndSurviveContinue(string core,PartyCombatMode mode)
        {
            Start(core,mode);var ids=core==E?new[]{"v14_evan_firstproof","v14_evan_review","v14_evan_settle"}:core==M?new[]{"v14_mira_opening","v14_mira_breakflow","v14_mira_press"}:new[]{"v14_finn_quickgrowth","v14_finn_densegrowth","v14_finn_bloom"};
            Win();Claim(ids[0]);Travel("V12_R2");Win();Claim();Travel("V12_R3");Travel("V12_R4");Win();Claim(ids[1]);Travel("V12_R5");Claim(ids[2]);
            Assert.That(profile.ActiveMode.claimedRewardIds.Count,Is.EqualTo(4));Assert.That(Rules.buffs.Count,Is.EqualTo(4));Assert.That(Rules.buffs.Count(b=>b.id.StartsWith("v14_")),Is.EqualTo(3));
            Good(profile.SaveCurrent("before-continue"));string before=CombatJson.Write(profile.ActiveMode);profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,map);Assert.That(CombatJson.Write(profile.ActiveMode),Is.EqualTo(before));
            Assert.That(Rules.coreActorId,Is.EqualTo(core));Assert.That(profile.ActiveCombat.Config.v14.partyCombatMode,Is.EqualTo(mode));Travel("V12_R6");Run(ProfileCommandKind.ActivateEncounter);Assert.That(S.v13.boss,Is.Not.Null);
        }
        [Test] public void SkippingEarlierRouteCardsStillOffersIndependentMiddleAndFinalCards()
        {
            Start(E,PartyCombatMode.CoreAndAssist);Win();Run(ProfileCommandKind.SkipReward);Travel("V12_R2");Win();Run(ProfileCommandKind.SkipReward);Travel("V12_R3");Travel("V12_R4");Win();
            Assert.That(profile.PendingReward.candidates[0],Is.EqualTo("v14_evan_review"));Run(ProfileCommandKind.SkipReward);Travel("V12_R5");Assert.That(profile.PendingReward.candidates[0],Is.EqualTo("v14_evan_settle"));Claim("v14_evan_settle");
            Assert.That(Rules.buffs.Single().id,Is.EqualTo("v14_evan_settle"));
        }
        [Test] public void SwitchingTrioActiveMemberDoesNotChangeRunCoreOrRewardPool()
        {
            Start(M,PartyCombatMode.PersistentTrio);Run(ProfileCommandKind.ActivateEncounter);var r=profile.ActiveCombat.Execute(new CombatCommand{id="switch-to-evan",kind=CommandKind.SwitchActive,actorId=E});Assert.That(r.success,Is.True,r.reason);
            Assert.That(S.activeActorId,Is.EqualTo(E));foreach(var enemy in S.enemies)enemy.hp=0;Run(ProfileCommandKind.ResolveEncounter);Assert.That(Rules.coreActorId,Is.EqualTo(M));Claim("v14_mira_opening");
        }
        [Test] public void GroveKeepsFirstTwoFinnCardsAndUsesGenericFinalSlot()
        {
            Start(F,PartyCombatMode.CoreAndAssist,second:"v12_finn_grove");Win();Claim("v14_finn_quickgrowth");Travel("V12_R2");Win();Claim();Travel("V12_R3");Travel("V12_R4");Win();Claim("v14_finn_densegrowth");Travel("V12_R5");Claim();
            Assert.That(Rules.actors.Find(a=>a.actorId==F).unlockedActionIds,Does.Contain("v12_finn_grove"));Assert.That(Rules.buffs.Any(b=>b.id=="v14_finn_bloom"),Is.False);
        }
        [Test] public void GuardKeepsLegalOldHealingRouteAndDoesNotOfferGrowthCards()
        {
            Start(F,PartyCombatMode.PersistentTrio,"guard");Win();Assert.That(profile.PendingReward.candidates.All(id=>id=="v13_finn_shelter"||id=="v13_finn_prayer"));Run(ProfileCommandKind.ClaimReward,pick:"v13_finn_shelter");
            Travel("V12_R2");Win();Claim();Travel("V12_R3");Travel("V12_R4");Win();Assert.That(profile.PendingReward.candidates[0],Is.EqualTo("v13_finn_shelter_up"));Run(ProfileCommandKind.ClaimReward,pick:"v13_finn_shelter_up");Travel("V12_R5");Claim();
        }
        [TestCase(PartyCombatMode.CoreAndAssist)][TestCase(PartyCombatMode.PersistentTrio)]
        public void PaladinMaintainsOriginalTwoCoreAndTwoGenericRewardMoments(PartyCombatMode mode)
        {
            Start(P6CharacterCatalog.PaladinId,mode);Win();Assert.That(profile.PendingReward.candidates.All(id=>id.StartsWith("core_")));Run(ProfileCommandKind.ClaimReward,pick:profile.PendingReward.candidates[0]);
            Travel("V12_R2");Win();Claim();Travel("V12_R3");Travel("V12_R4");Win();Assert.That(profile.PendingReward.candidates.All(id=>id.StartsWith("core_")));Run(ProfileCommandKind.ClaimReward,pick:profile.PendingReward.candidates[0]);Travel("V12_R5");Claim();
            Assert.That(Rules.buffs.Any(b=>b.id.StartsWith("v14_")),Is.False);
        }
    }
}
