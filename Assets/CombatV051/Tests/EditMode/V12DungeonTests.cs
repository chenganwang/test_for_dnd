using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Transaction fixtures deliberately place actors/defeat enemies. Player route evidence is separate.
    public sealed class V12DungeonTests
    {
        const string Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn";
        string folder,path;CombatConfig legacy,p5,p6;DungeonConfig oldMap;ProfileSession profile;int seq;
        CombatState S=>profile.ActiveCombat.State;ModeState M=>profile.ActiveMode;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Dungeon",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();oldMap=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v12-dungeon",181,p6,p5,legacy,oldMap,path);Good(profile.EnsureV11Progress("progress"));
            Good(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=new System.Collections.Generic.List<string>{Evan,Mira,Finn},coreId=Evan,defaultActiveActorId=Evan},"draft"));
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static void Good(ProfileResult r)=>Assert.That(r.success,Is.True,r.reason);
        ProfileCommand Cmd(ProfileCommandKind kind,string choice=null)=>new ProfileCommand{id="v12-map-"+(++seq),kind=kind,selectionId=choice,qualified=true};
        void Run(ProfileCommandKind kind,string choice=null)=>Good(profile.Execute(Cmd(kind,choice)));
        void Start()=>Run(ProfileCommandKind.StartDemoExploration);
        void Place(float x,float z){foreach(var a in S.actors){a.x=x;a.z=z;}}
        void Travel(string room){var r=profile.ActiveDungeonConfig.Room(room);Place(r.x,r.z);var c=Cmd(ProfileCommandKind.EnterRoom);c.roomId=room;Good(profile.Execute(c));}
        void Win(bool skip=true){Run(ProfileCommandKind.ActivateEncounter);foreach(var e in S.enemies)e.hp=0;Run(ProfileCommandKind.ResolveEncounter);if(skip&&profile.PendingReward!=null)Run(ProfileCommandKind.SkipReward);}
        void Library(){Start();Win();Travel("V12_R2");Win();Travel("V12_R3");}
        void Open(string id){var e=profile.ActiveDungeonConfig.Event(id);Place(e.x,e.z);var c=Cmd(ProfileCommandKind.OpenEvent);c.eventId=id;c.actorId=Evan;Good(profile.Execute(c));}
        void Event(ProfileCommandKind kind,string id){var c=Cmd(kind);c.eventId=id;c.actorId=Evan;Good(profile.Execute(c));}
        void Reload()=>profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,oldMap);

        [Test] public void NewDepartureSnapshotsSixRoomsThreeSeededSlotsAndNoOldMapMutation()
        {
            string old=CombatJson.Write(profile.Config);Start();Assert.That(profile.UsesV12Dungeon);Assert.That(profile.ActiveRoom.templateId,Is.EqualTo("V12_R1"));
            Assert.That(M.rooms.Select(r=>r.templateId),Is.EqualTo(Enumerable.Range(1,6).Select(i=>"V12_R"+i)));Assert.That(M.v12Run.layoutSlots.Count,Is.EqualTo(3));
            Assert.That(profile.Config.rooms.Count,Is.EqualTo(8));Assert.That(CombatJson.Write(profile.Config),Is.EqualTo(old));Assert.That(profile.ActiveCombat.Config.v12.beam,Is.Not.Null);
            string before=CombatJson.Write(M);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));Assert.That(profile.ActiveDungeonKey,Is.EqualTo(M.v12Run.hash));
        }
        [Test] public void OptionalLibraryPatrolRequiresApproachAndNeverLocksUnattemptedMainRoute()
        {
            Library();Place(45,3);Assert.That(profile.V12EncounterTriggerReached(),Is.False);Assert.That(profile.IsPassageOpen("V12_P34"));
            Assert.That(profile.Execute(Cmd(ProfileCommandKind.ActivateEncounter)).reason,Is.EqualTo("v12_optional_patrol_not_approached"));
            Travel("V12_R4");Assert.That(M.Room("V12_R3").encounterId,Is.Null);Assert.That(M.events.All(e=>e.rollCount==0));
        }
        [Test] public void ApproachedLibraryPatrolCreatesExactlyTwoEnemiesAndNoExtraReward()
        {
            Library();Place(57,-3);Assert.That(profile.V12EncounterTriggerReached());Win();Assert.That(M.Room("V12_R3").resolved);Assert.That(profile.PendingReward,Is.Null);Assert.That(M.claimedRewardIds.Count,Is.EqualTo(2));
            Assert.That(M.encounters.Last().id,Does.Contain("V12_R3"));
        }
        [Test] public void ReadOpensActualShortcutFlagWithoutRollingOrAwardingCombatResources()
        {
            Library();string random=CombatJson.Write(M.random.eventDice);int charges=S.charges;float focus=S.focusBudget.secondsRemaining;Open("V12_READ");Event(ProfileCommandKind.UseEventAlternative,"V12_READ");
            Assert.That(M.HasFlag("V12_LIBRARY_READ"));Assert.That(M.events.Single(e=>e.templateId=="V12_READ").rollCount,Is.Zero);Assert.That(CombatJson.Write(M.random.eventDice),Is.EqualTo(random));Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(focus));
            Assert.That(M.v12Run.obstacles.Single(o=>o.removedByFlag=="V12_LIBRARY_READ").id,Is.Not.Empty);Reload();Assert.That(M.HasFlag("V12_LIBRARY_READ"));
        }
        [Test] public void PaidSafeSearchAndFullStateRestoreKeepSameResultAndEventDoesNotRefillCombat()
        {
            Library();S.Actor(Finn).hp=10;int charges=S.charges;string combatRandom=CombatJson.Write(S.random);Open("V12_SEARCH");Event(ProfileCommandKind.RollEvent,"V12_SEARCH");
            string before=CombatJson.Write(M),faces=CombatJson.Write(profile.ActiveSafeEvent.rawFaces);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));Assert.That(CombatJson.Write(profile.ActiveSafeEvent.rawFaces),Is.EqualTo(faces));
            Event(ProfileCommandKind.ConfirmEvent,"V12_SEARCH");Assert.That(S.charges,Is.EqualTo(charges));Assert.That(CombatJson.Write(S.random),Is.EqualTo(combatRandom));Assert.That(profile.IsPassageOpen("V12_P34"));
            var repeat=Cmd(ProfileCommandKind.OpenEvent);repeat.eventId="V12_SEARCH";repeat.actorId=Evan;Assert.That(profile.Execute(repeat).success,Is.False);
        }
        [Test] public void FourRewardMomentsOneRestAndBossVictoryUseExistingOnceReceipts()
        {
            Library();Travel("V12_R4");Win();Travel("V12_R5");Assert.That(profile.PendingReward.roomId,Does.EndWith("/V12_R5"));Assert.That(profile.IsPassageOpen("V12_P56"),Is.False);
            Run(ProfileCommandKind.SkipReward);Assert.That(M.claimedRewardIds.Count,Is.EqualTo(4));Assert.That(profile.IsPassageOpen("V12_P56"));
            S.Actor(Evan).hp=1;S.Actor(Mira).hp=0;S.Actor(Finn).spellSlots=0;int charges=S.charges;var rest=Cmd(ProfileCommandKind.ShortRest,"v12:V12_R5_REST");Good(profile.Execute(rest));int healed=S.Actor(Evan).hp;
            Assert.That(healed,Is.GreaterThan(1));Assert.That(S.Actor(Mira).hp,Is.Zero);Assert.That(S.Actor(Finn).spellSlots,Is.Zero);Assert.That(S.charges,Is.EqualTo(charges));Assert.That(profile.Execute(rest).duplicate);
            string before=CombatJson.Write(M);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));Assert.That(profile.Execute(Cmd(ProfileCommandKind.ShortRest,"v12:V12_R5_REST")).reason,Is.EqualTo("v12_supply_already_used"));
            Travel("V12_R4");Travel("V12_R5");Assert.That(profile.PendingReward,Is.Null);Assert.That(S.Actor(Evan).hp,Is.EqualTo(healed));Travel("V12_R6");Win();Run(ProfileCommandKind.SettlementOnce,"victory");Assert.That(profile.State.challenge.stage,Is.EqualTo("Settled"));Assert.That(profile.State.challenge.claimedRewardIds.Count,Is.EqualTo(4));Assert.That(profile.State.meta.lastSettlement.victory);
        }
        [Test] public void PreviouslyShippedV12HeroEightRoomDepartureKeepsMapAndNullBeamAfterReload()
        {
            var historicalA=V12Defaults.Create();historicalA.build=null;historicalA.beam=null;profile.SetNextV12Rules(historicalA);profile.SetNextV12Dungeon(null);Start();Assert.That(profile.UsesV12Dungeon,Is.False);Assert.That(M.v11Departure.version,Is.EqualTo("v12-departure-1"));Assert.That(M.rooms.Count,Is.EqualTo(8));Assert.That(profile.ActiveCombat.Config.v12.beam,Is.Null);
            S.Actor(Evan).hp=7;Good(profile.SaveCurrent("a-stage-wounded"));string before=CombatJson.Write(M);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));Assert.That(profile.ActiveRoom.templateId,Is.EqualTo("R0"));Assert.That(profile.ActiveCombat.Config.v12.beam,Is.Null);
        }
        [Test] public void ChangedMapSnapshotCannotOverwriteLastGoodSave()
        {
            Start();string disk=File.ReadAllText(path);M.v12Run.dungeon.Room("V12_R2").width=99;Assert.That(profile.SaveCurrent("bad-map").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();Assert.That(M.v12Run.dungeon.Room("V12_R2").width,Is.EqualTo(24));
        }
        [Test] public void NewMapSourceIsExplicitlyMappedAndOldEnemyHealthIsUnchanged()
        {
            string root=TestContext.CurrentContext.TestDirectory,source=null;for(int i=0;i<12&&root!=null;i++,root=Directory.GetParent(root)?.FullName){string p=Path.Combine(root,"Assets","CombatV051","Resources","V12DungeonConfig.json");if(File.Exists(p)){source=p;break;}}
            Assert.That(source,Is.Not.Null);Assert.That(JToken.DeepEquals(JToken.Parse(File.ReadAllText(source)),JToken.Parse(V12DungeonDefaults.SourceJson)),Is.True);
            var mapped=V12DungeonDefaults.Create().dungeon;Assert.That(mapped.Enemy("boss").hp,Is.EqualTo(oldMap.Enemy("boss").hp));Assert.That(mapped.Enemy("elite").hp,Is.EqualTo(oldMap.Enemy("elite").hp));Assert.That(mapped.Room("V12_R5").enemies,Is.Empty);
        }
    }
}
