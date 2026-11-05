using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Controlled engine integration fixtures: explicit buffs/positions/HP. No manual LOS links
    // or parasite plans. All attachment and detonation commands use ExecuteWorldCommand.
    // These are not natural route, reward acquisition, performance or human-play evidence.
    public sealed class V14DotRuntimeTests
    {
        const string Finn=CombatSession.V12Finn;
        BattleWorld world;GameObject wall;string directory;int sequence;
        CombatState S=>world.State;
        ActorState Primary=>S.enemies[0];ActorState Side=>S.enemies[1];
        static void Place(ActorState a,float x,float z){a.x=x;a.z=z;a.heightY=0;}
        static void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Dot-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V14 dot command geometry fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartMainCharacterTraining();world.enabled=false;world.Controls.Map.Disable();
            if(world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            var config=world.Session.Config;config.v11.coreActorId=Finn;config.v11.buffs.Clear();config.v11.v13FinnTree="growth";config.v11.v14BuildVersion=V14BuildCatalog.Version;config.v11.v14FinnGrowthAction=CombatSession.V14DetonateId;
            config.v13=new V13Rules();config.v14=new V14Rules();config.v12.seedCooldown=4;
            config.v11.actors.Find(a=>a.actorId==Finn).unlockedActionIds=new List<string>{"v12_finn_seed",CombatSession.V14DetonateId};
            CombatSession.InitializeV13State(S,config);S.activeActorId=S.commandActorId=Finn;
            foreach(var actor in S.actors){Place(actor,-4,-4);actor.majorRemaining=actor.guardRemaining=actor.dodgeRemaining=actor.windupRemaining=actor.staggerRemaining=0;actor.aimPreparing=false;actor.basicAttackIntent=null;}
            if(S.enemies.Count<2)Extra("fixture-secondary",10,10);
            foreach(var enemy in S.enemies){Place(enemy,10,10);enemy.aiState=2;enemy.aiRemaining=100;enemy.hp=enemy.maxHp=500;enemy.statuses.Clear();}
            Place(Primary,-1,-4);Place(Side,-1,-1.8f);world.SendMessage("SynchronizeViews");Physics.SyncTransforms();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Dot-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        ActorState Extra(string id,float x,float z)
        {var enemy=CombatJson.Clone(Primary);enemy.id=id;enemy.statuses.Clear();enemy.v14DamageCarries=null;enemy.hp=enemy.maxHp=500;Place(enemy,x,z);S.enemies.Add(enemy);return enemy;}
        void Mode(PartyCombatMode mode){world.Session.Config.v14.partyCombatMode=mode;S.v14.partyCombatMode=mode;}
        void Buff(string id)=>world.Session.Config.v11.buffs.Add(new V11BuffStack{id=id,stacks=1});
        void Ready()
        {float delay=Math.Max(S.Actor(Finn).majorRemaining,world.Session.Member(S,Finn).actionUntil-S.simulationSeconds);if(delay>0)world.Session.Tick(delay+.001f);}
        CombatCommand Cast(string action,string target)
        {
            Ready();Good(world.RequestHeroAction(Finn,action,target,CommandAuthority.Player));var start=S.Actor(Finn).v12.intent.id;
            world.Session.Tick(S.Actor(Finn).v12.intent.remaining+.001f);var command=new CombatCommand{id="fixture/resolve/"+(++sequence),kind=CommandKind.ResolveHeroAction,actorId=Finn,actionId=action,targetId=target,orderId=start,authority=CommandAuthority.System};
            Assert.That(command.v12,Is.Null,"Fixture must not inject spatial evidence");Good(world.ExecuteWorldCommand(command));return command;
        }
        IEnumerator SeedGeometry(PartyCombatMode mode)
        {
            Mode(mode);Buff("v14_finn_quickgrowth");
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="V14 actual secondary LOS blocker";wall.layer=8;wall.transform.position=new Vector3(-2.5f,1,-2.9f);wall.transform.localScale=new Vector3(.8f,2,.3f);Physics.SyncTransforms();
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(S.Actor(Finn)),BattleWorld.Position(Primary)),Is.True);
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(S.Actor(Finn)),BattleWorld.Position(Side)),Is.False);
            var blocked=Cast("v12_finn_seed",Primary.id);Assert.That(blocked.v12,Is.Not.Null);Assert.That(blocked.v12.links.Any(l=>l.fromId==Finn&&l.toId==Side.id&&!l.lineOfSight),Is.True);
            Assert.That(Primary.statuses.Any(s=>s.v14Dot!=null),Is.True);Assert.That(Side.statuses.Any(s=>s.v14Dot!=null),Is.False);
            UnityEngine.Object.Destroy(wall);yield return null;Physics.SyncTransforms();world.Session.Tick(4.01f);
            var clear=Cast("v12_finn_seed",Primary.id);Assert.That(clear.v12.links.Any(l=>l.fromId==Finn&&l.toId==Side.id&&l.lineOfSight),Is.True);
            Assert.That(Side.statuses.Count(s=>s.v14Dot!=null&&s.sourceId==Finn),Is.EqualTo(1));Assert.That(S.enemies.Count(e=>e.statuses.Any(s=>s.v14Dot!=null)),Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator AssistQuickgrowthUsesActualCommandTimeLineEvidence(){yield return SeedGeometry(PartyCombatMode.CoreAndAssist);}
        [UnityTest] public IEnumerator TrioQuickgrowthUsesActualCommandTimeLineEvidence(){yield return SeedGeometry(PartyCombatMode.PersistentTrio);}
        IEnumerator BloomGeometry(PartyCombatMode mode)
        {
            Mode(mode);Buff("v14_finn_quickgrowth");Buff("v14_finn_bloom");var third=Extra("fixture-third",10,10);
            Cast("v12_finn_seed",Primary.id);Assert.That(Side.statuses.Any(s=>s.v14Dot!=null),Is.True);world.Session.Tick(4.01f);
            Place(S.Actor(third.id),-2,-4.8f);Cast("v12_finn_seed",third.id);var uninfected=Extra("fixture-uninfected",-.5f,-4.3f);
            Assert.That(S.enemies.Count(e=>e.statuses.Any(s=>s.v14Dot!=null)),Is.EqualTo(3));int charges=S.charges;
            var command=Cast(CombatSession.V14DetonateId,Primary.id);Assert.That(command.v12,Is.Not.Null);Assert.That(S.charges,Is.EqualTo(charges-1));
            var detonation=S.v14Detonations.Single();Assert.That(detonation.targets.Count,Is.EqualTo(3));Assert.That(detonation.targets.All(t=>t.remainingTicks>0&&t.coefficient==1.25),Is.True);
            Assert.That(S.Actor(uninfected.id).hp,Is.EqualTo(500));Assert.That(S.enemies.All(e=>!e.statuses.Any(s=>s.v14Dot!=null)),Is.True);yield return null;
        }
        [UnityTest] public IEnumerator AssistBloomConsumesThreeNaturallyAttachedPlansThroughWorldCommands(){yield return BloomGeometry(PartyCombatMode.CoreAndAssist);}
        [UnityTest] public IEnumerator TrioBloomConsumesThreeNaturallyAttachedPlansThroughWorldCommands(){yield return BloomGeometry(PartyCombatMode.PersistentTrio);}
    }
}
