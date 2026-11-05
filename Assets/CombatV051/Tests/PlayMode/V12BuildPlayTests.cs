using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Explicit engine fixtures: supplied Buff, positions and a deterministic contact branch.
    // Exercises real colliders/line evidence/command publication, not natural acquisition/fun.
    public sealed class V12BuildPlayTests
    {
        const string Evan=CombatSession.V12Evan,Mira=CombatSession.V12Mira,Finn=CombatSession.V12Finn;
        BattleWorld world;GameObject wall;string directory;int sequence;
        ActorState Primary=>world.State.enemies[0];ActorState Side=>world.State.enemies[1];
        static void Place(ActorState a,float x,float z){a.x=x;a.z=z;a.heightY=0;}
        static void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Build-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V12 actual geometry build fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartMainCharacterTraining();world.enabled=false;world.Controls.Map.Disable();
            if(world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            var config=world.Session.Config;Assert.That(config.v12.build,Is.Not.Null);config.v11.coreActorId=Evan;
            config.v11.buffs.Add(new V11BuffStack{id="v12_evan_coauthor",stacks=1});config.v12.echoCoefficient=4; // Positive effect even at the minimum fixture damage die.
            foreach(var actor in world.State.actors)
            {Place(actor,-8,-8);actor.majorRemaining=actor.guardRemaining=actor.dodgeRemaining=actor.windupRemaining=actor.staggerRemaining=0;actor.aimPreparing=false;actor.basicAttackIntent=null;}
            Place(world.State.Actor(Evan),-4,-4);Place(world.State.Actor(Mira),-2,-4);
            if(world.State.enemies.Count<2){var extra=CombatJson.Clone(Primary);extra.id="v12-engine-secondary";extra.statuses.Clear();world.State.enemies.Add(extra);}
            foreach(var enemy in world.State.enemies){Place(enemy,10,10);enemy.aiState=2;enemy.aiRemaining=100;enemy.hp=enemy.maxHp=500;}
            Place(Primary,-1,-4);Place(Side,-1,-1.8f);Physics.SyncTransforms();
            Good(world.RequestHeroAction(Evan,"v12_evan_mark",Primary.id,CommandAuthority.Player));
            world.Session.Tick(.36f);var intent=world.State.Actor(Evan).v12.intent;
            Good(world.ExecuteWorldCommand(new CombatCommand{id="fixture/mark-resolve",kind=CommandKind.ResolveHeroAction,actorId=Evan,actionId="v12_evan_mark",targetId=Primary.id,orderId=intent.id,authority=CommandAuthority.System}));
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Build-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        CombatResult AllyContact()
        {
            for(ulong seed=1;seed<10000;seed++)
            {var stream=RandomStreams.Create(seed).backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(stream))!=14)continue;world.State.random.backgroundCombat=stream;break;}
            string root="fixture/contact/"+(++sequence);
            return world.ExecuteWorldCommand(new CombatCommand{id=root,kind=CommandKind.IncomingAttack,actorId=Mira,targetId=Primary.id,rootId=root,amount=2,qualified=true,authority=CommandAuthority.System});
        }
        [UnityTest] public IEnumerator ActualWallBlocksSecondaryEchoThenRemovingItRestoresTheSameAuthority()
        {
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Actual V12 secondary line blocker";wall.layer=8; // The existing battle wall collision layer.
            wall.transform.position=new Vector3(-1,1,-2.9f);wall.transform.localScale=new Vector3(1,2,.2f);Physics.SyncTransforms();
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(Primary),BattleWorld.Position(Side)),Is.False);
            int hp=Side.hp;var first=AllyContact();Good(first);
            Assert.That(first.v12.effects.Any(e=>e.kind=="echo"),Is.True);Assert.That(first.v12.effects.Any(e=>e.kind=="splash"),Is.False);Assert.That(Side.hp,Is.EqualTo(hp));
            UnityEngine.Object.Destroy(wall);yield return null;Physics.SyncTransforms();world.Session.Tick(2.6f);
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(Primary),BattleWorld.Position(Side)),Is.True);
            var next=AllyContact();Good(next);Assert.That(next.v12.effects.Count(e=>e.kind=="splash"&&e.targetId==Side.id),Is.EqualTo(1));Assert.That(Side.hp,Is.LessThan(hp));
        }
        [UnityTest] public IEnumerator ActualActiveCharacterSwitchDoesNotMoveCoreFormOrDamageSource()
        {
            Good(world.SwitchActive(Finn));Assert.That(world.State.activeActorId,Is.EqualTo(Finn));Assert.That(world.Session.Config.v11.coreActorId,Is.EqualTo(Evan));
            var result=AllyContact();Good(result);var splash=result.v12.effects.Single(e=>e.kind=="splash");
            Assert.That(splash.sourceId,Is.EqualTo(Evan));Assert.That(world.State.Actor(Finn).v12.echoCooldown,Is.Zero);Assert.That(world.State.Actor(Evan).v12.echoCooldown,Is.GreaterThan(0));
            Assert.That(world.Session.Config.v11.buffs.Single().id,Is.EqualTo("v12_evan_coauthor"));yield return null;
        }
    }
}
