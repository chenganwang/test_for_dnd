using System;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Explicit close-range/high-HP rule fixtures. No claim of Unity spatial or natural Player evidence.
    public sealed class P6AtomicTests:ProfileTestFixture
    {
        string directory,path;CombatConfig legacy,p5,p6;DungeonConfig dungeon;
        const string Paladin="char_demo_paladin";
        [SetUp] public void SetupFiles()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCrpgP6Atomic",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            legacy=new CombatConfig();p5=CombatJson.Clone(legacy);p5.p5=new P5Config();p6=CombatJson.Clone(p5);p6.p6=new P6Config();dungeon=new DungeonConfig();
            profile=ProfileSession.CreateNewCompatible("p6-atomic",41,p6,p5,legacy,dungeon,path);seq=0;Travel("R1");Activate();
            foreach(var a in CState.actors){a.x=CState.enemies[0].x-1;a.z=CState.enemies[0].z;}foreach(var enemy in CState.enemies){enemy.hp=enemy.maxHp=1000;enemy.armorClass=10;enemy.wisSaveBonus=100;}
        }
        [TearDown] public void CleanupFiles(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        CombatCommand Command(CommandKind kind)=>new CombatCommand{id="p6-atomic/"+(++seq),kind=kind,actorId=Paladin,targetId=CState.enemies[0].id,actionId=CombatSession.P6SmiteId};
        CombatResult Do(CombatCommand c){var r=profile.ExecuteCombat(c);Assert.That(r.success,Is.True,r.reason);return r;}
        CombatResult Do(CommandKind kind)=>Do(Command(kind));
        void Reload(){profile=ProfileSession.LoadCompatible(path,p6,p5,legacy,dungeon);}
        void PaidWithMark()
        {
            var bind=Command(CommandKind.RequestBindRadiance);bind.actorId="mage";Do(bind);profile.Tick(.76f);
            var resolve=Command(CommandKind.ResolveBindRadiance);resolve.actorId="mage";resolve.rootId=bind.id;resolve.authority=CommandAuthority.System;Do(resolve);
            // Seed-search selects a naturally rolled 20 for a deterministic accepted-cost fixture.
            for(ulong seed=1;seed<10000;seed++){var streams=RandomStreams.Create(seed);if(RandomStreams.D20(RandomStreams.Create(seed).visibleTactical)==20){CState.random.visibleTactical=streams.visibleTactical;break;}}
            Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll);Do(CommandKind.RevealRoll);Assert.That(CState.pendingRoll.hit,Is.True);
        }
        CombatCommand Stage(string operation)
        {
            PaidWithMark();var select=Command(CommandKind.SelectSmiteOvercharge);select.flag=true;if(operation=="reserve")return select;Do(select);Do(CommandKind.Commit);Do(CommandKind.ExitFocus);
            var start=Command(CommandKind.StartQueued);start.queueId=CState.queue.Last().id;if(operation=="start")return start;Do(start);profile.Tick(.36f);
            var finish=Command(CommandKind.ResolveQueued);finish.authority=CommandAuthority.System;finish.queueId=CState.queue.Last().id;return finish;
        }
        [TestCase("reserve",CombatWriteStage.BeforeWrite)][TestCase("reserve",CombatWriteStage.AfterFlush)][TestCase("reserve",CombatWriteStage.BeforeReplace)][TestCase("reserve",CombatWriteStage.AfterReplace)][TestCase("reserve",CombatWriteStage.AfterVerify)]
        [TestCase("start",CombatWriteStage.BeforeWrite)][TestCase("start",CombatWriteStage.AfterFlush)][TestCase("start",CombatWriteStage.BeforeReplace)][TestCase("start",CombatWriteStage.AfterReplace)][TestCase("start",CombatWriteStage.AfterVerify)]
        [TestCase("damage",CombatWriteStage.BeforeWrite)][TestCase("damage",CombatWriteStage.AfterFlush)][TestCase("damage",CombatWriteStage.BeforeReplace)][TestCase("damage",CombatWriteStage.AfterReplace)][TestCase("damage",CombatWriteStage.AfterVerify)]
        public void SharedHoldSecondPaymentAndPositiveRadiantRewardsRollbackTogetherAcrossAllFileStages(string operation,CombatWriteStage stage)
        {
            var command=Stage(operation);Assert.That(profile.SaveCurrent("checkpoint/"+operation).success,Is.True);
            string outer=CombatJson.Write(profile.State),inner=CombatJson.Write(CState),main=File.ReadAllText(path),backup=File.ReadAllText(path+".bak");bool reached=false;
            profile.Store.WriteFault=at=>{if(at==stage){reached=true;throw new IOException("p6-atomic-"+operation+"-"+stage);}};
            var failed=profile.ExecuteCombat(command);Assert.That(reached,Is.True);Assert.That(failed.success,Is.False);Assert.That(failed.reason,Does.Contain("p6-atomic-"));Assert.That(CombatJson.Write(profile.State),Is.EqualTo(outer));Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));Assert.That(File.ReadAllText(path),Is.EqualTo(main));
            // Existing Store policy may rotate the previous authoritative main into
            // backup before rolling back. It must never retain the failed candidate.
            string restoredBackup=File.ReadAllText(path+".bak");Assert.That(restoredBackup==backup||restoredBackup==main,Is.True,"Backup must be an original valid snapshot, never the uncommitted candidate.");
            Reload();Assert.That(CombatJson.Write(CState),Is.EqualTo(inner));var success=Do(command);
            if(operation=="reserve"){Assert.That(CState.charges,Is.EqualTo(1));Assert.That(CState.pendingRoll.p6.overchargeHeld,Is.True);}
            else if(operation=="start"){Assert.That(CState.charges,Is.Zero);Assert.That(CState.rolls.Last().p6.overchargePaid,Is.True);}
            else{Assert.That(success.p6.damages.Single().markConsumed,Is.True);Assert.That(CState.p6.markRewardCount,Is.EqualTo(1));Assert.That(CState.focusBudget.p6.rewards.Count(x=>x.category=="combo"),Is.EqualTo(1));}
            string after=CombatJson.Write(CState);Reload();Assert.That(CombatJson.Write(CState),Is.EqualTo(after));Assert.That(Do(command).duplicate,Is.True);Assert.That(CombatJson.Write(CState),Is.EqualTo(after));
        }
        [Test] public void ProfileResolvePublishesFormalClearEightOnlyOnceAndPreservesItAfterReload()
        {
            // Enemy defeat is an explicit profile-boundary fixture, not a simulated kill.
            foreach(var enemy in CState.enemies)enemy.hp=0;float before=CState.focusBudget.secondsRemaining;
            var command=P(ProfileCommandKind.ResolveEncounter);var result=profile.Execute(command);Assert.That(result.success,Is.True,result.reason);Assert.That(CState.focusBudget.secondsRemaining,Is.EqualTo(before+8));Assert.That(CState.focusBudget.p6.rewards.Count(x=>x.category=="clear"),Is.EqualTo(1));
            string after=CombatJson.Write(CState);Reload();Assert.That(CombatJson.Write(CState),Is.EqualTo(after));Assert.That(profile.Execute(command).duplicate,Is.True);Assert.That(CombatJson.Write(CState),Is.EqualTo(after));Assert.That(profile.Execute(P(ProfileCommandKind.ResolveEncounter)).success,Is.False);
        }
        [TestCase("Normal",8)][TestCase("Elite",8)][TestCase("Boss",0)]
        public void ActualEndEncounterCommandCreditsOnlyNormalOrEliteAndReceiptReplayIsIdempotent(string kind,int expected)
        {
            foreach(var enemy in CState.enemies)enemy.hp=0;CState.momentumLedger.encounterKind=kind;float before=CState.focusBudget.secondsRemaining;
            // EndEncounter is the inner reducer invoked by outer Resolve, not an
            // independently publishable profile command (that would violate binding).
            var core=new CombatSession(CState.Clone(),profile.ActiveCombat.Config);var command=Command(CommandKind.EndEncounter);command.authority=CommandAuthority.System;var ended=core.Execute(command);Assert.That(ended.success,Is.True,ended.reason);Assert.That(core.State.focusBudget.secondsRemaining-before,Is.EqualTo(expected));string after=CombatJson.Write(core.State);Assert.That(core.Execute(command).duplicate,Is.True);Assert.That(CombatJson.Write(core.State),Is.EqualTo(after));
        }
        [Test] public void ResolvingAndEnteringNextEncounterPreservesMageCooldownAndOnlyExplorationSimulationReducesIt()
        {
            var bind=Command(CommandKind.RequestBindRadiance);bind.actorId="mage";Do(bind);profile.Tick(.76f);var resolve=Command(CommandKind.ResolveBindRadiance);resolve.actorId="mage";resolve.rootId=bind.id;resolve.authority=CommandAuthority.System;Do(resolve);
            float cooldown=CState.Actor("mage").p6.bindCooldown;foreach(var enemy in CState.enemies)enemy.hp=0;Run(ProfileCommandKind.ResolveEncounter);Assert.That(CState.Actor("mage").p6.bindCooldown,Is.EqualTo(cooldown));
            profile.Tick(2);Assert.That(CState.Actor("mage").p6.bindCooldown,Is.EqualTo(cooldown-2).Within(.001));Travel("R2");Travel("R3");Activate();Assert.That(CState.Actor("mage").p6.bindCooldown,Is.EqualTo(cooldown-2).Within(.001));Reload();Assert.That(CState.Actor("mage").p6.bindCooldown,Is.EqualTo(cooldown-2).Within(.001));
        }
    }
}
