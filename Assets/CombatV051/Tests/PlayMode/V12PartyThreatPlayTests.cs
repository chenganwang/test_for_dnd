using System;
using System.Collections;
using System.IO;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Engine fixture: explicit positions and a held warning isolate the actual party steering
    // and command bridge. This is not natural Player or balance evidence; no dice are selected.
    public sealed class V12PartyThreatPlayTests
    {
        const string Evan="char_story_evan",Mira="char_story_mira",Finn="char_story_finn";
        BattleWorld world;
        string directory;
        static void Place(ActorState actor,float x,float z){actor.x=x;actor.z=z;actor.heightY=0;}

        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-PartyThreat",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            world=new GameObject("V12 party warning priority fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.StartMainCharacterTraining();
            world.enabled=false;
            world.Controls.Map.Disable();
            if(!world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            Assert.That(world.SwitchActive(Mira).success,Is.True);
            Assert.That(world.V12Enabled,Is.True);
            Assert.That(world.Profile,Is.Null,"Use the existing training arena, not dungeon routing.");
            yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-PartyThreat"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }

        [UnityTest] public IEnumerator EvanEvadesBeforeStartingFreeMark(){yield return VerifyPriority(Evan);}
        [UnityTest] public IEnumerator FinnEvadesBeforeStartingFreeSeed(){yield return VerifyPriority(Finn);}

        IEnumerator VerifyPriority(string actorId)
        {
            foreach(var actor in world.State.actors)
            {
                Place(actor,-6,-6);
                actor.majorRemaining=actor.guardRemaining=actor.dodgeRemaining=actor.windupRemaining=0;
                actor.aimPreparing=false;actor.basicAttackIntent=null;
                actor.staggerRemaining=actor.id==actorId?0:100;
            }
            foreach(var enemy in world.State.enemies){Place(enemy,10,10);enemy.aiState=2;enemy.aiRemaining=100;}
            var member=world.State.Actor(actorId);Place(member,-4,-4);
            var target=world.State.enemies[0];Place(target,-1,-4);
            target.attackSequence=3;target.attackGroupId="v12-party-priority/circle";target.attackTargetId=actorId;
            target.lockedX=member.x;target.lockedZ=member.z;target.aiState=1;target.aiRemaining=1.4f;
            Physics.SyncTransforms();
            var warning=world.EnemyShape(target);
            string action=BattleWorld.V12BasicAction(actorId);
            Assert.That(world.Session.Quote(actorId,target.id,action).legal,Is.True,"Preparation must be legal, not skipped because of cooldown/range.");
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(member),BattleWorld.Position(target)),Is.True);
            Assert.That(BattleGeometry.Contains(warning,BattleWorld.Position(member)),Is.True);
            Assert.That(CombatSession.HasPlayerOrder(member),Is.False);
            int charges=world.State.charges;
            ulong draws=world.State.random.backgroundCombat.drawCount,visible=world.State.random.visibleTactical.drawCount;
            Vector3 start=BattleWorld.Position(member);

            world.AdvanceParty(.1f);
            member=world.State.Actor(actorId);
            Assert.That(member.aiState,Is.EqualTo(3),"Existing danger takes priority over a new autonomous preparation.");
            Assert.That(BattleGeometry.DistanceXZ(start,BattleWorld.Position(member)),Is.GreaterThan(.05f),"The real wall-swept actor must move.");
            Assert.That(member.v12.intent,Is.Null);
            Assert.That(member.v12.basicCooldown,Is.Zero);
            for(int i=0;i<20&&BattleGeometry.Contains(warning,BattleWorld.Position(member));i++)
            {world.AdvanceParty(.1f);member=world.State.Actor(actorId);}
            Assert.That(BattleGeometry.Contains(warning,BattleWorld.Position(member)),Is.False,"AI must reach a safe point, not only label its intention evade.");
            Assert.That(member.v12.intent,Is.Null);
            Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));
            Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible));

            // Explicit fixture end of danger: the same free action should remain available.
            target=world.State.Actor(target.id);target.aiState=2;target.aiRemaining=100;
            Assert.That(world.Session.Quote(actorId,target.id,action).legal,Is.True);
            world.AdvanceParty(.1f);
            member=world.State.Actor(actorId);
            Assert.That(member.v12.intent,Is.Not.Null,world.Message);
            Assert.That(member.v12.intent.actionId,Is.EqualTo(action));
            Assert.That(CombatSession.V12ActionActive(member),Is.True);
            Assert.That(world.State.charges,Is.EqualTo(charges));
            yield return null;
        }
    }
}
