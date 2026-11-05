using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Transaction route fixtures deliberately place the core and defeat enemies.
    public sealed class V13ProfileTests
    {
        ProfileSession profile;CombatConfig legacy,p5,p6;DungeonConfig map;string folder,path;int seq;
        CombatState S=>profile.ActiveCombat.State;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-V13-Profile",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();map=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v13-fixture",181,p6,p5,legacy,map,path);Good(profile.EnsureV11Progress("progress"));
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static void Good(ProfileResult r)=>Assert.That(r.success,Is.True,r.reason);
        void Run(ProfileCommandKind kind,string room=null){Good(profile.Execute(new ProfileCommand{id="v13-profile/"+(++seq),kind=kind,roomId=room,qualified=true}));}
        void Start(string core,string tree,bool modern=true)
        {
            profile.EnableV13Departures(modern);var team=core==P6CharacterCatalog.PaladinId?new List<string>{core,"mage",CombatSession.V12Finn}:new List<string>{CombatSession.V12Evan,CombatSession.V12Mira,CombatSession.V12Finn};
            Good(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=team,coreId=core,defaultActiveActorId=core,finnTree=tree},"draft"));Run(ProfileCommandKind.StartDemoExploration);
        }
        void Reload()=>profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,map);
        void Travel(string room){var r=profile.ActiveDungeonConfig.Room(room);S.Actor(S.activeActorId).x=r.x;S.Actor(S.activeActorId).z=r.z;Run(ProfileCommandKind.EnterRoom,room);}
        void Win(){Run(ProfileCommandKind.ActivateEncounter);foreach(var e in S.enemies)e.hp=0;Run(ProfileCommandKind.ResolveEncounter);if(profile.PendingReward!=null)Run(ProfileCommandKind.SkipReward);}
        [TestCase(CombatSession.V12Evan,"growth")][TestCase(CombatSession.V12Mira,"growth")][TestCase(CombatSession.V12Finn,"growth")][TestCase(CombatSession.V12Finn,"guard")][TestCase(P6CharacterCatalog.PaladinId,"guard")]
        public void EveryCoreAndFinnTreeConnectToExistingSixRoomsAndFourBuildChoices(string core,string tree)
        {
            Start(core,tree);Assert.That(S.v13.coreId,Is.EqualTo(core));Assert.That(profile.ActiveCombat.Config.v13.finnTree,Is.EqualTo(tree));
            int departureHealth=S.Actor(core).maxHp;Assert.That(departureHealth,Is.EqualTo(core==P6CharacterCatalog.PaladinId?64:core==CombatSession.V12Mira?48:36));
            Assert.That(profile.ActiveMode.departurePreparation.preparation.primaryLawId,Is.EqualTo("BASE"));
            Win();Travel("V12_R2");Win();Travel("V12_R3");Travel("V12_R4");Run(ProfileCommandKind.ActivateEncounter);Assert.That(S.v13.fate.trigger,Is.EqualTo("V12_R4"));
            foreach(var e in S.enemies)e.hp=0;Run(ProfileCommandKind.ResolveEncounter);Run(ProfileCommandKind.SkipReward);
            Travel("V12_R5");Run(ProfileCommandKind.SkipReward);Good(profile.SaveCurrent("mid-route"));string saved=CombatJson.Write(profile.ActiveMode);Reload();Assert.That(CombatJson.Write(profile.ActiveMode),Is.EqualTo(saved));
            Travel("V12_R6");Run(ProfileCommandKind.ActivateEncounter);Assert.That(S.enemies.Count,Is.EqualTo(1));Assert.That(S.v13.boss,Is.Not.Null);Assert.That(S.enemies[0].maxHp,Is.EqualTo(230));Assert.That(S.v13.fate.trigger,Is.EqualTo("V12_R6"));
            Assert.That(profile.ActiveMode.claimedRewardIds.Count,Is.EqualTo(4));
            Assert.That(S.Actor(core).maxHp,Is.EqualTo(departureHealth),"Entering rooms or loading must never multiply core vitality again");
        }
        [Test] public void OldPersistentTrioLoadRemainsByteIdenticalAfterEnablingNewDepartures()
        {
            Start(CombatSession.V12Evan,"growth",false);Assert.That(S.v13,Is.Null);Good(profile.SaveCurrent("old-save"));string before=File.ReadAllText(path),old=CombatJson.Write(profile.ActiveMode);
            Reload();profile.EnableV13Departures();Assert.That(S.v13,Is.Null);Assert.That(profile.ActivePartyCombatMode,Is.EqualTo(PartyCombatMode.PersistentTrio));Assert.That(CombatJson.Write(profile.ActiveMode),Is.EqualTo(old));Assert.That(File.ReadAllText(path),Is.EqualTo(before));
        }
        [Test] public void NewEvanCannotReceiveObsoleteFinalDraftRewardsButLegacyPoolRetainsThem()
        {
            Start(CombatSession.V12Evan,"growth");var rules=profile.ActiveCombat.Config.v11;
            Assert.That(V11BuffCatalog.AvailableInRules(rules,"v12_evan_conclusion"),Is.False);
            Assert.That(V11BuffCatalog.AvailableInRules(rules,"v12_evan_conclusion_up"),Is.False);
            Assert.That(V11BuffCatalog.AvailableInRules(rules,"v12_evan_coauthor"),Is.True);
            Assert.That(V11BuffCatalog.All.Count(d=>V11BuffCatalog.AvailableInRules(rules,d.id)),Is.GreaterThanOrEqualTo(4));
            var legacyRules=CombatJson.Clone(rules);legacyRules.v13FinnTree=null;
            Assert.That(V11BuffCatalog.AvailableInRules(legacyRules,"v12_evan_conclusion"),Is.True);
            Assert.That(V11BuffCatalog.AvailableInRules(legacyRules,"v12_evan_conclusion_up"),Is.True);
        }
        [Test] public void ActiveFateAndTerrainBossMechanicsPersistThroughActualAtomicDiskSave()
        {
            Start(CombatSession.V12Mira,"guard");Win();Travel("V12_R2");Win();Travel("V12_R3");Travel("V12_R4");Win();Travel("V12_R5");Run(ProfileCommandKind.SkipReward);Travel("V12_R6");Run(ProfileCommandKind.ActivateEncounter);
            // Controlled persistence fixture, not a natural encounter: freeze a valid
            // decision and partially consumed mechanics without writing any player input.
            var v=S.v13;v.fate.stage="Deciding";v.fate.original=v.fate.final=28;v.fate.decision=3.5f;v.fateCount=1;
            v.echo=new V13Echo{id="fixture-paid-echo",actorId=S.activeActorId,raw=12};v.fate.echoId=v.echo.id;v.fate.echoRaw=12;
            v.terrain.Add(new V13Terrain{id="fixture-prior-cover",kind="cover",x=v.boss.arenaX-6,z=v.boss.arenaZ-4,warning=0,remaining=4});
            v.boss.phase=2;v.boss.stage="Ritual";v.boss.move="Ritual";v.boss.pillarA=true;v.boss.nodeA=false;v.boss.nodeB=true;v.boss.remaining=1.2f;
            string expected=CombatJson.Write(v);Good(profile.SaveCurrent("active-v13-disk"));string bytes=File.ReadAllText(path);Reload();
            Assert.That(CombatJson.Write(S.v13),Is.EqualTo(expected));Assert.That(File.ReadAllText(path),Is.EqualTo(bytes));
            int inspiration=S.Actor(S.activeActorId).inspiration;
            var result=profile.ExecuteCombat(new CombatCommand{id="after-reload-replace",kind=CommandKind.V13FateReplace,actorId=S.activeActorId});Assert.That(result.success,Is.True,result.reason);
            Assert.That(S.v13.fate.final,Is.EqualTo(60));Assert.That(S.Actor(S.activeActorId).inspiration,Is.EqualTo(inspiration-1));
            expected=CombatJson.Write(S.v13);Good(profile.SaveCurrent("after-v13-payment"));Reload();Assert.That(CombatJson.Write(S.v13),Is.EqualTo(expected));
            result=profile.ExecuteCombat(new CombatCommand{id="after-reload-replace",kind=CommandKind.V13FateReplace,actorId=S.activeActorId});Assert.That(result.duplicate,Is.True);Assert.That(S.Actor(S.activeActorId).inspiration,Is.EqualTo(inspiration-1));
        }
        [Test] public void MidRunTreeChangeOnlyAffectsNextDepartureAndRetainsPrayerInventory()
        {
            Start(CombatSession.V12Finn,"guard");string snapshot=CombatJson.Write(profile.ActiveMode.v11Departure);S.v13.members.Single(x=>x.actorId==CombatSession.V12Finn).prayers=1;
            var draft=profile.V11PreparationDraft;draft.finnTree="growth";Good(profile.SaveV11Preparation(draft,"next-tree"));Good(profile.SaveCurrent("saved-tree"));Reload();
            Assert.That(CombatJson.Write(profile.ActiveMode.v11Departure),Is.EqualTo(snapshot));Assert.That(profile.ActiveCombat.Config.v13.finnTree,Is.EqualTo("guard"));Assert.That(S.v13.members.Single(x=>x.actorId==CombatSession.V12Finn).prayers,Is.EqualTo(1));
        }
    }
}
