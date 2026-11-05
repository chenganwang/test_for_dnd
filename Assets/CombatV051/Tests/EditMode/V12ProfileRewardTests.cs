using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Rule/transaction fixtures place actors and resolve enemies explicitly. Not Player or fun evidence.
    public sealed class V12ProfileRewardTests
    {
        const string Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn",Pal=P6CharacterCatalog.PaladinId;
        string folder,path;CombatConfig legacy,p5,p6;DungeonConfig map;ProfileSession profile;int seq;
        CombatState S=>profile.ActiveCombat.State;ModeState M=>profile.ActiveMode;
        [SetUp] public void Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Rewards",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);path=Path.Combine(folder,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();map=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("v12-reward",181,p6,p5,legacy,map,path);Good(profile.EnsureV11Progress("progress"));
            profile.SetNextV12Rules(new V12CombatConfig{build=new V12BuildConfig()});
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
        static void Good(ProfileResult r)=>Assert.That(r.success,Is.True,r.reason);
        ProfileCommand Cmd(ProfileCommandKind k,string selection=null)=>new ProfileCommand{id="v12-reward-"+(++seq),kind=k,selectionId=selection,qualified=true};
        void Run(ProfileCommandKind k,string selection=null)=>Good(profile.Execute(Cmd(k,selection)));
        void Start(string core=Evan)
        {
            Good(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=core==Pal?new List<string>{Pal,Evan,Finn}:new List<string>{Evan,Mira,Finn},coreId=core,defaultActiveActorId=core},"draft-"+(++seq)));
            Run(ProfileCommandKind.StartDemoExploration);
        }
        void Place(float x,float z){foreach(var a in S.actors){a.x=x;a.z=z;}}
        void Travel(string room){var r=profile.ActiveDungeonConfig.Room(room);Place(r.x,r.z);var c=Cmd(ProfileCommandKind.EnterRoom);c.roomId=room;Good(profile.Execute(c));}
        void Win(){Run(ProfileCommandKind.ActivateEncounter);foreach(var e in S.enemies)e.hp=0;Run(ProfileCommandKind.ResolveEncounter);}
        void Choose(string pick=null)=>Run(ProfileCommandKind.ClaimReward,pick??profile.PendingReward.candidates[0]);
        void ToSecondOffer(){Start();Win();Choose();Travel("V12_R2");Win();}
        // A deliberately prepared legal offer isolates atomic payment/recovery from content RNG.
        void PreparedLegalOffer(string id){M.pendingReward.candidates=new List<string>{id};Good(profile.SaveCurrent("prepare-offer-"+(++seq)));}
        void Reload()=>profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,map);
        void Open(string id){var e=profile.ActiveDungeonConfig.Event(id);Place(e.x,e.z);var c=Cmd(ProfileCommandKind.OpenEvent);c.eventId=id;c.actorId=Evan;Good(profile.Execute(c));}

        [Test] public void CatalogAddsExactlyTwelveCoreAndSixGenericWithAnExplicitSnapshotGate()
        {
            Assert.That(V11BuffCatalog.All.Count(x=>x.id.StartsWith("v12_")&&x.pool=="Core"),Is.EqualTo(12));
            Assert.That(V11BuffCatalog.All.Count(x=>x.id.StartsWith("v12_")&&x.pool=="Generic"),Is.EqualTo(6));
            Start();var rules=profile.ActiveCombat.Config.v11;Assert.That(rules.v12BuildVersion,Is.EqualTo("v12-build-1"));
            Assert.That(V11BuffCatalog.Eligible(rules).Count(x=>x.pool=="Core"),Is.EqualTo(2));
            Assert.That(V11BuffCatalog.AvailableInRules(rules,"team_ward"),Is.False);
            var old=CombatJson.Clone(rules);old.v12BuildVersion=null;
            Assert.That(V11BuffCatalog.AvailableInRules(old,"v12_team_steadfast"),Is.False);Assert.That(V11BuffCatalog.AvailableInRules(old,"team_ward"));
        }
        [TestCase(Evan,"v12_evan_coauthor")][TestCase(Mira,"v12_mira_sweep")][TestCase(Finn,"v12_finn_spread")]
        public void FourChoicesOfferOneFormThenGenericThenItsUpgradeThenGeneric(string core,string form)
        {
            Start(core);Win();Assert.That(profile.PendingReward.candidates.Count,Is.EqualTo(2));Assert.That(profile.PendingReward.candidates.All(id=>V11BuffCatalog.Find(id).coreActorId==core));
            Choose(form);Assert.That(V11BuffCatalog.Eligible(profile.ActiveCombat.Config.v11).Count(x=>x.pool=="Core"),Is.EqualTo(1));
            Travel("V12_R2");Win();Assert.That(profile.PendingReward.candidates.Count,Is.EqualTo(3));Assert.That(profile.PendingReward.candidates.All(id=>V11BuffCatalog.Find(id).pool=="Generic"));Choose();
            Travel("V12_R3");Travel("V12_R4");Win();Assert.That(profile.PendingReward.candidates[0],Is.EqualTo(form+"_up"));Assert.That(profile.PendingReward.candidates.Skip(1).All(id=>V11BuffCatalog.Find(id).pool=="Generic"));Choose(form+"_up");
            Travel("V12_R5");Assert.That(profile.PendingReward.candidates.All(id=>V11BuffCatalog.Find(id).pool=="Generic"));Choose();
            Assert.That(M.claimedRewardIds.Count,Is.EqualTo(4));Assert.That(M.v11Build.buffs.Count,Is.EqualTo(4));Assert.That(M.v11Build.buffs.Count(b=>b.id==form||b.id==form+"_up"),Is.EqualTo(2));
            var display=V11BuffCatalog.DisplayBuffs(profile.ActiveCombat.Config.v11);Assert.That(display.Count,Is.EqualTo(3));Assert.That(display.Any(d=>d.id==form+"_up"));Assert.That(display.Any(d=>d.id==form),Is.False);
            Assert.That(M.v11Departure.preparation.coreId,Is.EqualTo(core));string before=CombatJson.Write(M);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));
        }
        [Test] public void SkippingFirstFormNeverOffersAnOrphanUpgradeOrANewBaseAtFourthRoom()
        {
            Start();Win();Run(ProfileCommandKind.SkipReward);Travel("V12_R2");Win();Run(ProfileCommandKind.SkipReward);Travel("V12_R3");Travel("V12_R4");Win();
            Assert.That(profile.PendingReward.candidates.Count,Is.EqualTo(3));Assert.That(profile.PendingReward.candidates.All(id=>V11BuffCatalog.Find(id).pool=="Generic"));
        }
        [Test] public void PaladinKeepsOriginalCorePoolAndUsesOnlyNewGenericAtGenericMoments()
        {
            Start(Pal);Win();Assert.That(profile.PendingReward.candidates.All(id=>id.StartsWith("core_")));Choose();Travel("V12_R2");Win();
            Assert.That(profile.PendingReward.candidates.All(id=>id.StartsWith("v12_team_")));Choose();Travel("V12_R3");Travel("V12_R4");Win();
            Assert.That(profile.PendingReward.candidates.All(id=>id.StartsWith("core_")));Assert.That(profile.PendingReward.candidates.All(id=>!id.Contains("evan")&&!id.Contains("mira")&&!id.Contains("finn")));
        }
        [Test] public void OfferedCardsRngAndOldBSnapshotHashSurviveNewCatalogLoad()
        {
            profile.SetNextV12Rules(new V12CombatConfig());Start(Pal);Win();Assert.That(M.v11Departure.v12.build,Is.Null);
            Assert.That(profile.PendingReward.candidates.All(id=>!id.StartsWith("v12_")));S.Actor(Pal).hp=7;Good(profile.SaveCurrent("old-b"));
            string before=CombatJson.Write(M),hash=M.v11Departure.hash,rng=CombatJson.Write(M.random.contentReward),config=CombatJson.Write(profile.ActiveCombat.Config);
            Reload();profile.SetNextV12Rules(new V12CombatConfig{build=new V12BuildConfig()});
            Assert.That(CombatJson.Write(M),Is.EqualTo(before));Assert.That(M.v11Departure.hash,Is.EqualTo(hash));Assert.That(CombatJson.Write(M.random.contentReward),Is.EqualTo(rng));Assert.That(CombatJson.Write(profile.ActiveCombat.Config),Is.EqualTo(config));
            Assert.That(S.Actor(Pal).hp,Is.EqualTo(7));Choose();Assert.That(M.v11Build.buffs.Single().id,Does.Not.StartWith("v12_"));
        }
        [Test] public void NewOfferIsPersistedAndCannotBeReplacedWithWrongRoomPool()
        {
            Start();Win();string offer=CombatJson.Write(profile.PendingReward),rng=CombatJson.Write(M.random.contentReward);Reload();Assert.That(CombatJson.Write(profile.PendingReward),Is.EqualTo(offer));Assert.That(CombatJson.Write(M.random.contentReward),Is.EqualTo(rng));
            string disk=File.ReadAllText(path);M.pendingReward.candidates=new List<string>{"v12_team_leeway"};Assert.That(profile.SaveCurrent("wrong-room-offer").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
        [Test] public void LeewayClaimUsesOneOriginalAtomicTransactionAndNeverRecoversTwice()
        {
            ToSecondOffer();PreparedLegalOffer("v12_team_leeway");float capacity=S.focusBudget.maximumSeconds,remaining=S.focusBudget.secondsRemaining;
            var command=Cmd(ProfileCommandKind.ClaimReward,"v12_team_leeway");Good(profile.Execute(command));Assert.That(S.focusBudget.maximumSeconds,Is.EqualTo(capacity+15));Assert.That(S.focusBudget.secondsRemaining,Is.GreaterThanOrEqualTo(remaining));
            string before=CombatJson.Write(M);Assert.That(profile.Execute(command).duplicate);Assert.That(CombatJson.Write(M),Is.EqualTo(before));Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(before));
        }
        [TestCase(CombatWriteStage.BeforeReplace)][TestCase(CombatWriteStage.AfterVerify)]
        public void RewardAndImmediateFocusEffectRollBackTogetherOnWriteFailure(CombatWriteStage at)
        {
            ToSecondOffer();PreparedLegalOffer("v12_team_leeway");string before=CombatJson.Write(profile.State),disk=File.ReadAllText(path);var command=Cmd(ProfileCommandKind.ClaimReward,"v12_team_leeway");
            profile.Store.WriteFault=stage=>{if(stage==at)throw new IOException("v12-reward-write-fault");};Assert.That(profile.Execute(command).success,Is.False);
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(before));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));profile.Store.WriteFault=null;Good(profile.Execute(command));
            Assert.That(M.v11Build.buffs.Count(b=>b.id=="v12_team_leeway"),Is.EqualTo(1));
        }
        [Test] public void PathfinderRewardsOnlyCommittedRegisteredExplorationAndKeepsOldResourceRules()
        {
            ToSecondOffer();PreparedLegalOffer("v12_team_pathfinder");Choose();Travel("V12_R3");S.focusBudget.secondsRemaining=50;int charges=S.charges;Open("V12_READ");float before=S.focusBudget.secondsRemaining;
            var read=Cmd(ProfileCommandKind.UseEventAlternative);read.eventId="V12_READ";read.actorId=Evan;Good(profile.Execute(read));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(before+2));Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));
            Assert.That(profile.Execute(read).duplicate);Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(before+2));
            var point=M.v12Run.points.Single(p=>p.id=="V12_R3_CACHE");Place(point.x,point.z);S.Actor(Evan).hp=1;
            Run(ProfileCommandKind.ShortRest,"v12:V12_R3_CACHE");Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(before+4));Assert.That(S.charges,Is.EqualTo(charges));
            string mode=CombatJson.Write(M);Reload();Assert.That(CombatJson.Write(M),Is.EqualTo(mode));Assert.That(profile.Execute(Cmd(ProfileCommandKind.ShortRest,"v12:V12_R3_CACHE")).success,Is.False);
        }
        [Test] public void OppositeFormsAndUpgradeWithoutBaseCannotBeSaved()
        {
            Start();string disk=File.ReadAllText(path);M.v11Build.buffs.Add(new V11BuffStack{id="v12_evan_coauthor_up",stacks=1});Assert.That(profile.SaveCurrent("orphan").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Reload();
            M.v11Build.buffs.Add(new V11BuffStack{id="v12_evan_coauthor",stacks=1});M.v11Build.buffs.Add(new V11BuffStack{id="v12_evan_conclusion",stacks=1});Assert.That(profile.SaveCurrent("two-forms").success,Is.False);Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
        }
    }
}
