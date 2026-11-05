using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CombatV051.Tests
{
    public sealed class P47FocusFeedbackTests
    {
        BattleWorld world;
        string directory,path;
        ActorState Fighter=>world.State.Actor("fighter");
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P47FocusFeedback",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            world=new GameObject("P47 real last-elite Focus feedback").AddComponent<BattleWorld>();world.InitializeDungeonForTests(path);world.Controls.Map.Disable();world.enabled=false;world.ChooseDungeonOption("close");
            world.ConfigureImpactSettings(FeedbackIntensity.Normal,FeedbackIntensity.Normal);
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string safeRoot=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P47FocusFeedback"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(safeRoot,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        static void Place(ActorState actor,float x,float z){actor.x=x;actor.z=z;actor.heightY=0;}
        void ProfileCommand(ProfileCommand command)
        {var result=world.ProfileAction(command);Assert.That(result.success,Is.True,command.kind+": "+result.reason);}
        void Enter(string id)
        {
            var room=world.DungeonConfig.Room(id);int index=0;
            foreach(var actor in world.State.actors){Place(actor,room.x+index*.6f,room.z-index*.5f);index++;}
            ProfileCommand(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId=id,qualified=true});
            if(!world.Profile.ActiveRoom.resolved&&!string.IsNullOrEmpty(room.encounterKind))ProfileCommand(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId=id,qualified=true});
        }
        void ClearEarlierRoomFixture()
        {
            // Earlier-room HP fixtures only reach the shipped R5. They are not evidence of
            // playing those battles. The asserted final elite death below is a real d20 hit.
            foreach(var enemy in world.State.enemies)enemy.hp=0;
            ProfileCommand(new ProfileCommand{kind=ProfileCommandKind.ResolveEncounter});
            if(world.Profile.PendingReward!=null)ProfileCommand(new ProfileCommand{kind=ProfileCommandKind.SkipReward});
        }
        static RngState NaturalTwelve()
        {
            for(ulong seed=1;seed<10000;seed++){var rng=RandomStreams.Create(seed).backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(rng))==12)return rng;}
            throw new InvalidOperationException("No real PCG natural-12 fixture found.");
        }
        [UnityTest] public IEnumerator ActualLastEliteHitShowsOnePointFiveFocusAcrossImmediateRoomClear()
        {
            Enter("R1");ClearEarlierRoomFixture();Enter("R2");Enter("R3");ClearEarlierRoomFixture();Enter("R2");Place(Fighter,48,8);
            ProfileCommand(new ProfileCommand{kind=ProfileCommandKind.OpenGate,gateId="P24_SMASH",qualified=true});Enter("R4");ClearEarlierRoomFixture();Enter("R5");
            var elite=world.State.enemies.Single();string eliteId=elite.id;
            Assert.That(world.Profile.EnemyTemplateFor(eliteId).id,Is.EqualTo("elite"));Assert.That(world.State.momentumLedger.registeredHighThreats,Does.Contain(eliteId));
            // Explicit one-HP and positions fixture. No callback receives a fake death or
            // recovery amount: StartBasicAttack/ResolveBasicAttack draw and apply the hit.
            elite.hp=1;elite.aiState=2;elite.aiRemaining=100;Place(elite,27.5f,16);Place(Fighter,26,16);
            foreach(var actor in world.State.actors){actor.majorRemaining=100;actor.basicAttackIntent=null;actor.windupRemaining=actor.guardRemaining=actor.dodgeRemaining=actor.staggerRemaining=0;}
            Fighter.majorRemaining=0;world.SelectedActor="fighter";world.SelectedAction="attack";world.SelectedTarget=eliteId;world.State.focusBudget.secondsRemaining=1;world.State.random.backgroundCombat=NaturalTwelve();
            world.RefreshEngagementEvidence();Physics.SyncTransforms();Assert.That(world.HasLineOfEffect(BattleWorld.Position(Fighter),BattleWorld.Position(elite)),Is.True);
            world.ImpactFeedback.ResetObservation();world.Hud.RefreshView();world.Hud.FocusFeedback.Refresh();
            int count=world.Hud.FocusFeedback.RestorationCount,audio=world.Audio.PlayedCount(AudioCue.FocusRecovered);float recovered=world.State.focusBudget.secondsRecovered;
            const string root="p47-real-last-elite";
            var start=world.ExecuteWorldCommand(new CombatCommand{id=root,kind=CommandKind.StartBasicAttack,actorId="fighter",targetId=eliteId});Assert.That(start.success,Is.True,start.reason);
            world.Session.Tick(world.Session.Config.basicContactSeconds+.01f,true,0);
            var contact=world.ExecuteWorldCommand(new CombatCommand{id=root+"/contact",kind=CommandKind.ResolveBasicAttack,actorId="fighter",targetId=eliteId,orderId=root,authority=CommandAuthority.System});
            Assert.That(contact.success&&contact.hit&&!contact.critical,Is.True,contact.reason);Assert.That(world.State.Actor(eliteId).hp,Is.Zero);Assert.That(world.ImpactFeedback.Holding,Is.False,"A noncritical elite kill must not accidentally hide this same-frame boundary behind hit-stop.");
            Assert.That(world.State.focusBudget.secondsRecovered-recovered,Is.EqualTo(1.5f));Assert.That(world.State.focusBudget.secondsRemaining,Is.EqualTo(2.5f));
            // No yield and no manual feedback refresh between the real hit and the profile
            // transition. This reproduces the LateUpdate-only observer's lost last-hit gain.
            world.ResolveDungeonEncounter();world.Hud.FocusFeedback.Refresh();world.Hud.RefreshView();
            Assert.That(world.Profile.ActiveRoom.resolved,Is.True);Assert.That(world.State.inCombat,Is.False);
            Assert.That(world.Hud.FocusFeedback.RestorationCount,Is.EqualTo(count+1));Assert.That(world.Hud.FocusFeedback.LastDisplayedRecovery,Is.EqualTo(1.5f));Assert.That(world.Audio.PlayedCount(AudioCue.FocusRecovered),Is.EqualTo(audio+1));
            Assert.That(world.Hud.FocusFeedback.RecoveryVisible,Is.True);Assert.That(world.Hud.FocusFeedback.gameObject.activeInHierarchy,Is.True,"Clear-room rebind must retain the short visible recovery tail.");
            Assert.That(world.Hud.FocusFeedback.GetComponentsInChildren<Text>(true).Any(t=>t.text=="+1.5s"&&t.gameObject.activeInHierarchy),Is.True);
            yield return null;
            world.Hud.FocusFeedback.Refresh();Assert.That(world.Hud.FocusFeedback.RestorationCount,Is.EqualTo(count+1));Assert.That(world.Audio.PlayedCount(AudioCue.FocusRecovered),Is.EqualTo(audio+1));
            var loaded=ProfileSession.Load(path,world.Profile.BaseCombatConfig,world.Profile.Config);Assert.That(loaded.ActiveCombat.State.focusBudget.secondsRemaining,Is.EqualTo(2.5f));Assert.That(loaded.ActiveCombat.State.focusBudget.secondsRecovered-recovered,Is.EqualTo(1.5f));
        }
    }
}
