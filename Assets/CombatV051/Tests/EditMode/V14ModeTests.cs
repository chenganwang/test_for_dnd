using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Controlled profile/command fixtures: explicit room travel and defeated enemies.
    // These are not natural Player routes, rendering checks, or human playtests.
    public sealed class V14ModeTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn,P=P6CharacterCatalog.PaladinId;
        ProfileSession profile;CombatConfig legacy,p5,p6;DungeonConfig map;string folder,path;int sequence;
        CombatState S=>profile.ActiveCombat.State;
        CombatSession Game=>profile.ActiveCombat;
        ModeState RunState=>profile.ActiveMode;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Modes",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();map=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v14-mode-fixture",181,p6,p5,legacy,map,path);Good(profile.EnsureV11Progress(Id()));
        }
        [TearDown] public void Cleanup()
        {
            string permitted=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Modes"))+Path.DirectorySeparatorChar;
            if(Directory.Exists(folder)&&Path.GetFullPath(folder).StartsWith(permitted,StringComparison.OrdinalIgnoreCase))Directory.Delete(folder,true);
        }
        string Id()=>"v14-modes/"+(++sequence);
        static void Good(ProfileResult result)=>Assert.That(result.success,Is.True,result.reason);
        static void Good(CombatResult result)=>Assert.That(result.success,Is.True,result.reason);
        void Command(ProfileCommandKind kind,string room=null,string selection=null)=>Good(profile.Execute(new ProfileCommand{id=Id(),kind=kind,roomId=room,selectionId=selection,qualified=true}));
        V11PreparationDraft Draft(string core,PartyCombatMode mode,string tree="growth",string slot=null)=>new V11PreparationDraft{
            teamIds=core==P?new List<string>{P,E,F}:new List<string>{E,M,F},coreId=core,defaultActiveActorId=core,partyCombatMode=mode,finnTree=tree,finnGrowthAction=slot};
        void Start(string core,PartyCombatMode mode,string tree="growth",string slot=null,int version=14)
        {
            if(version==14)profile.EnableV14Departures();else if(version==13)profile.EnableV13Departures();
            var draft=Draft(core,mode,tree,slot);Assert.That(profile.QuoteV11Departure(draft).legal,Is.True);
            Good(profile.SaveV11Preparation(draft,Id()));Command(ProfileCommandKind.StartDemoExploration);
        }
        void Reload(){profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,map);}
        void Switch(string actor)=>Good(profile.ExecuteCombat(new CombatCommand{id=Id(),kind=CommandKind.SwitchActive,actorId=actor}));
        void Travel(string room)
        {
            var definition=profile.ActiveDungeonConfig.Room(room);
            foreach(var actor in S.actors){actor.x=definition.x;actor.z=definition.z;}
            Command(ProfileCommandKind.EnterRoom,room);
        }
        void Win()
        {
            if(!S.inCombat)Command(ProfileCommandKind.ActivateEncounter);
            foreach(var enemy in S.enemies)enemy.hp=0;
            Command(ProfileCommandKind.ResolveEncounter);
        }
        string[] Eligible()=>V11BuffCatalog.Eligible(Game.Config.v11).Select(card=>card.id).OrderBy(id=>id,StringComparer.Ordinal).ToArray();

        [TestCase(E,PartyCombatMode.PersistentTrio)][TestCase(E,PartyCombatMode.CoreAndAssist)]
        [TestCase(M,PartyCombatMode.PersistentTrio)][TestCase(M,PartyCombatMode.CoreAndAssist)]
        [TestCase(F,PartyCombatMode.PersistentTrio)][TestCase(F,PartyCombatMode.CoreAndAssist)]
        [TestCase(P,PartyCombatMode.PersistentTrio)][TestCase(P,PartyCombatMode.CoreAndAssist)]
        public void NormalDepartureSnapshotsCurrentRulesForEveryCoreAndBothModes(string core,PartyCombatMode mode)
        {
            Start(core,mode);
            Assert.That(profile.ActivePartyCombatMode,Is.EqualTo(mode));Assert.That(S.v14.partyCombatMode,Is.EqualTo(mode));
            Assert.That(Game.Config.v14.partyCombatMode,Is.EqualTo(mode));Assert.That(Game.Config.v14.version,Is.EqualTo("v14-dual-builds-1"));
            Assert.That(RunState.v11Departure.v14.partyCombatMode,Is.EqualTo(mode));Assert.That(RunState.v11Departure.preparation.coreId,Is.EqualTo(core));
            Assert.That(Game.Config.v11.coreActorId,Is.EqualTo(core));Assert.That(S.v13.coreId,Is.EqualTo(core));
            Assert.That(Game.Config.v14.playerDamageScale,Is.EqualTo(1.10));Assert.That(Game.Config.v14.finnPeriodicScale,Is.EqualTo(2.0));
            Assert.That(S.actors.Count(a=>CombatSession.V13Physical(S,a)),Is.EqualTo(mode==PartyCombatMode.PersistentTrio?3:1));
            Assert.That(Game.Config.v11.actors.Single(a=>a.actorId==E).unlockedActionIds,Does.Contain("v13_evan_correct"));
            Assert.That(Game.Config.v11.actors.Single(a=>a.actorId==F).unlockedActionIds,Is.EquivalentTo(new[]{"v12_finn_seed","v14_finn_detonate"}));
            string snapshot=CombatJson.Write(RunState.v11Departure),config=CombatJson.Write(Game.Config);Good(profile.SaveCurrent(Id()));Reload();
            Assert.That(CombatJson.Write(RunState.v11Departure),Is.EqualTo(snapshot));Assert.That(CombatJson.Write(Game.Config),Is.EqualTo(config));
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void NextPreparationCannotChangeOngoingModeCoreTreeOrPreparedSnapshot(PartyCombatMode mode)
        {
            Start(E,mode);string snapshot=CombatJson.Write(RunState.v11Departure);string[] pool=Eligible();
            Good(profile.SaveV11Preparation(Draft(F,mode==PartyCombatMode.PersistentTrio?PartyCombatMode.CoreAndAssist:PartyCombatMode.PersistentTrio,"guard"),Id()));
            Good(profile.SaveCurrent(Id()));Reload();profile.EnableV14Departures();
            Assert.That(CombatJson.Write(RunState.v11Departure),Is.EqualTo(snapshot));Assert.That(profile.ActivePartyCombatMode,Is.EqualTo(mode));
            Assert.That(Game.Config.v11.coreActorId,Is.EqualTo(E));Assert.That(S.v13.coreId,Is.EqualTo(E));Assert.That(Game.Config.v13.finnTree,Is.EqualTo("growth"));
            Assert.That(Eligible(),Is.EqualTo(pool));Assert.That(profile.V11PreparationDraft.coreId,Is.EqualTo(F));Assert.That(profile.V11PreparationDraft.finnTree,Is.EqualTo("guard"));
        }

        [Test] public void TrioSwitchChangesOnlyOperatedActorAndKeepsRewardCoreAcrossDiskContinue()
        {
            Start(E,PartyCombatMode.PersistentTrio);Command(ProfileCommandKind.ActivateEncounter);
            string snapshot=CombatJson.Write(RunState.v11Departure);string[] pool=Eligible();Switch(M);
            Assert.That(S.activeActorId,Is.EqualTo(M));Assert.That(S.v13.coreId,Is.EqualTo(E));Assert.That(Eligible(),Is.EqualTo(pool));
            Good(profile.SaveCurrent(Id()));Reload();Assert.That(S.activeActorId,Is.EqualTo(M));Assert.That(CombatJson.Write(RunState.v11Departure),Is.EqualTo(snapshot));
            Win();Assert.That(profile.PendingReward.candidates,Does.Contain("v14_evan_firstproof"));
            Assert.That(profile.PendingReward.candidates.Where(id=>V11BuffCatalog.Find(id).pool=="Core").All(id=>V11BuffCatalog.Find(id).coreActorId==E),Is.True);
        }

        [Test] public void AssistModeRejectsBodyCommandsAndCoreSwitchWithoutResourceOrSnapshotDrift()
        {
            Start(M,PartyCombatMode.CoreAndAssist);Command(ProfileCommandKind.ActivateEncounter);string snapshot=CombatJson.Write(RunState.v11Departure);
            int charges=S.charges;float focus=S.focusBudget.secondsRemaining;
            var result=profile.ExecuteCombat(new CombatCommand{id=Id(),kind=CommandKind.SwitchActive,actorId=E});Assert.That(result.success,Is.False);Assert.That(result.reason,Is.EqualTo("core_locked_for_run"));
            result=profile.ExecuteCombat(new CombatCommand{id=Id(),kind=CommandKind.StartBasicAttack,actorId=E,targetId=S.enemies[0].id});Assert.That(result.success,Is.False);Assert.That(result.reason,Is.EqualTo("logical_member_has_no_body"));
            Assert.That(S.actors.Count,Is.EqualTo(3),"Logical identity/resource owners remain available");Assert.That(S.actors.Count(a=>CombatSession.V13Physical(S,a)),Is.EqualTo(1));
            Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(focus));Assert.That(CombatJson.Write(RunState.v11Departure),Is.EqualTo(snapshot));
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void CastingOriginUsesActualCasterOnlyWhenItHasABody(PartyCombatMode mode)
        {
            Start(M,mode);Command(ProfileCommandKind.ActivateEncounter);var evan=S.Actor(E);var core=S.Actor(M);var enemy=S.enemies[0];
            core.x=0;core.z=0;evan.x=20;evan.z=0;enemy.x=21;enemy.z=0;
            var command=new CombatCommand{id=Id(),kind=CommandKind.RequestHeroAction,actorId=E,targetId=enemy.id,actionId="v12_evan_mark",lineOfSight=true};
            Assert.That(Game.QuoteHero(command).legal,Is.EqualTo(mode==PartyCombatMode.PersistentTrio));
            Assert.That(Game.V13Anchor(S,evan).id,Is.EqualTo(mode==PartyCombatMode.PersistentTrio?E:M));
            command.lineOfSight=false;Assert.That(Game.QuoteHero(command).legal,Is.False,"Neither deployment permits casting through an actual blocked ray");
        }

        [TestCase(PartyCombatMode.PersistentTrio,true)][TestCase(PartyCombatMode.CoreAndAssist,false)]
        public void DefeatUsesPhysicalPartyRatherThanLivingOffFieldData(PartyCombatMode mode,bool survives)
        {
            Start(M,mode);S.Actor(M).hp=0;Assert.That(CombatSession.V13PartyAlive(S),Is.EqualTo(survives));
            S.Actor(E).hp=S.Actor(F).hp=0;Assert.That(CombatSession.V13PartyAlive(S),Is.False);
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void GrowthDefaultsToSeedAndDetonationWhileGroveRemainsAnExclusiveDepartureChoice(PartyCombatMode mode)
        {
            Start(F,mode);Assert.That(Game.HeroBasic(F),Is.EqualTo("v12_finn_seed"));Assert.That(Game.HeroKey(F),Is.EqualTo("v14_finn_detonate"));
            Assert.That(Game.Config.v12.seedCooldown,Is.EqualTo(4));Assert.That(Game.Config.v11.actors.Single(a=>a.actorId==F).unlockedActionIds,Has.Count.EqualTo(2));
            Assert.That(Game.Config.AllowsAction(S.Actor(F),"v12_finn_grove"),Is.False);
            var next=profile.V11PreparationDraft;next.finnGrowthAction="v12_finn_grove";Good(profile.SaveV11Preparation(next,Id()));
            Assert.That(Game.HeroKey(F),Is.EqualTo("v14_finn_detonate"));Assert.That(Game.Config.AllowsAction(S.Actor(F),"v12_finn_grove"),Is.False);
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void ExplicitGroveKeepsItsTwoLegalAbilitiesAndFiltersDetonationOnlyReward(PartyCombatMode mode)
        {
            Start(F,mode,"growth","v12_finn_grove");Assert.That(Game.HeroKey(F),Is.EqualTo("v12_finn_grove"));
            Assert.That(Game.Config.v11.actors.Single(a=>a.actorId==F).unlockedActionIds,Is.EquivalentTo(new[]{"v12_finn_seed","v12_finn_grove"}));
            Assert.That(V14BuildCatalog.Available(Game.Config.v11,"v14_finn_bloom"),Is.False);
            Assert.That(V14BuildCatalog.Available(Game.Config.v11,"v14_finn_quickgrowth"),Is.True);
            Assert.That(V14BuildCatalog.Available(Game.Config.v11,"v14_finn_densegrowth"),Is.True);
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void GuardIgnoresTheUnusedGrowthSlotWithoutMixingTrees(PartyCombatMode mode)
        {
            Start(F,mode,"guard","v14_finn_detonate");Assert.That(Game.HeroBasic(F),Is.EqualTo("v13_finn_shield"));Assert.That(Game.HeroKey(F),Is.EqualTo("v13_finn_prayer"));
            Assert.That(Game.Config.v11.actors.Single(a=>a.actorId==F).unlockedActionIds,Is.EquivalentTo(new[]{"v13_finn_shield","v13_finn_prayer"}));
            Assert.That(V11BuffCatalog.Eligible(Game.Config.v11).All(card=>!card.id.StartsWith("v14_finn_",StringComparison.Ordinal)),Is.True);
            Assert.That(Game.Member(S,F).prayers,Is.EqualTo(Game.Config.v13.prayerCapacity));
        }

        [TestCase(E,PartyCombatMode.PersistentTrio)][TestCase(E,PartyCombatMode.CoreAndAssist)]
        [TestCase(M,PartyCombatMode.PersistentTrio)][TestCase(M,PartyCombatMode.CoreAndAssist)]
        [TestCase(F,PartyCombatMode.PersistentTrio)][TestCase(F,PartyCombatMode.CoreAndAssist)]
        [TestCase(P,PartyCombatMode.PersistentTrio)][TestCase(P,PartyCombatMode.CoreAndAssist)]
        public void SameModeAndOneCoreReachFourExistingRewardPointsAndTheOriginalBoss(string core,PartyCombatMode mode)
        {
            Start(core,mode);string departure=CombatJson.Write(RunState.v11Departure);
            foreach(string room in new[]{"V12_R1","V12_R2","V12_R4","V12_R5"})
            {
                if(room=="V12_R4")Travel("V12_R3");
                if(room!="V12_R1")Travel(room);
                if(room!="V12_R5")Win();
                var offer=profile.PendingReward;Assert.That(offer,Is.Not.Null);Assert.That(offer.candidates.Count,Is.InRange(1,3));
                string guaranteed=V14BuildCatalog.Guaranteed(Game.Config.v11,room);
                if(guaranteed!=null)Assert.That(offer.candidates,Does.Contain(guaranteed));
                Assert.That(offer.candidates.Where(id=>V11BuffCatalog.Find(id).pool=="Core").All(id=>V11BuffCatalog.Find(id).coreActorId==core),Is.True);
                Command(ProfileCommandKind.ClaimReward,selection:guaranteed??offer.candidates[0]);
            }
            Assert.That(RunState.claimedRewardIds.Count,Is.EqualTo(4));Assert.That(CombatJson.Write(RunState.v11Departure),Is.EqualTo(departure));
            Good(profile.SaveCurrent(Id()));Reload();Travel("V12_R6");Command(ProfileCommandKind.ActivateEncounter);
            Assert.That(S.v14.partyCombatMode,Is.EqualTo(mode));Assert.That(S.v13.coreId,Is.EqualTo(core));Assert.That(Game.Config.v11.coreActorId,Is.EqualTo(core));
            Assert.That(S.v13.boss,Is.Not.Null);Assert.That(S.enemies.Count,Is.EqualTo(1));Assert.That(S.enemies[0].maxHp,Is.EqualTo(230));
            Assert.That(S.actors.Count(a=>CombatSession.V13Physical(S,a)),Is.EqualTo(mode==PartyCombatMode.PersistentTrio?3:1));
        }

        [TestCase(12)][TestCase(13)]
        public void EnablingV14DoesNotRewriteExistingLegacyOrV13SnapshotHashOrDisk(int version)
        {
            Start(E,version==13?PartyCombatMode.CoreAndAssist:PartyCombatMode.PersistentTrio,version:version);
            Good(profile.SaveCurrent(Id()));string disk=File.ReadAllText(path),mode=CombatJson.Write(RunState),config=CombatJson.Write(Game.Config),hash=RunState.v11Departure.hash;
            Reload();profile.EnableV14Departures();Assert.That(Game.Config.v14,Is.Null);Assert.That(S.v14,Is.Null);
            Assert.That(RunState.v11Departure.hash,Is.EqualTo(hash));Assert.That(CombatJson.Write(RunState),Is.EqualTo(mode));Assert.That(CombatJson.Write(Game.Config),Is.EqualTo(config));
            Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Assert.That(Game.HeroKey(F),Is.EqualTo("v12_finn_grove"));
        }

        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void HealingTerrainVisitsEveryLivingPhysicalAllyAtMostOncePerTick(PartyCombatMode mode)
        {
            Start(M,mode);Command(ProfileCommandKind.ActivateEncounter);
            foreach(var actor in S.actors){actor.x=0;actor.z=0;actor.hp=actor.maxHp-3;actor.v12.groveHealingRemainder=.9;}
            var before=S.actors.ToDictionary(actor=>actor.id,actor=>actor.hp);
            S.v13.terrain.Add(new V13Terrain{id="fixture-healing-ground",kind="heal",x=0,z=0,remaining=8,nextTick=0});
            Good(profile.ExecuteCombat(new CombatCommand{id=Id(),kind=CommandKind.V13FateEffects,authority=CommandAuthority.System}));
            foreach(var actor in S.actors)Assert.That(actor.hp,Is.EqualTo(before[actor.id]+(mode==PartyCombatMode.PersistentTrio||actor.id==M?1:0)));
            var paid=S.actors.Select(actor=>actor.hp).ToArray();Good(profile.ExecuteCombat(new CombatCommand{id=Id(),kind=CommandKind.V13FateEffects,authority=CommandAuthority.System}));
            Assert.That(S.actors.Select(actor=>actor.hp).ToArray(),Is.EqualTo(paid));
        }

        [Test] public void UnknownGrowthSlotIsRejectedWithoutOverwritingAuthorPreparation()
        {
            profile.EnableV14Departures();string before=CombatJson.Write(profile.V11PreparationDraft),disk=File.ReadAllText(path);
            var draft=Draft(F,PartyCombatMode.PersistentTrio,slot:"author-future-ability");var result=profile.SaveV11Preparation(draft,Id());
            Assert.That(result.success,Is.False);Assert.That(result.reason,Is.EqualTo("v14_growth_action_unknown"));Assert.That(CombatJson.Write(profile.V11PreparationDraft),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
    }
}
