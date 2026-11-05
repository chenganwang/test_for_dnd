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
    // Engine fixtures: explicit positions, time and health; never human/natural balance evidence.
    public sealed class V13RuntimeTests
    {
        BattleWorld world;string directory;GameObject wall;
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        CombatState S=>world.State;
        void Sync()=>world.SendMessage("SynchronizeViews");
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V13-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V13 engine fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");
            world.Profile.EnableV14Departures(false); // Exercise the original V13 snapshot, not the latest new-run adapter.
            var draft=world.Profile.V11PreparationDraft;draft.teamIds=new System.Collections.Generic.List<string>{E,M,F};draft.coreId=M;draft.defaultActiveActorId=M;
            Assert.That(world.Profile.SaveV11Preparation(draft,"fixture-prep").success,Is.True);world.ChooseFrontend("depart");
            if(world.Training)world.FinishP6Training();
            Assert.That(world.V13Enabled,Is.True,world.Message);world.enabled=false;
            var room=world.Profile.ActiveDungeonConfig.Room("V12_R1");S.Actor(M).x=room.x;S.Actor(M).z=room.z;
            Assert.That(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true,roomId="V12_R1"}).success,Is.True,world.Message);
            foreach(var a in S.enemies){a.aiState=2;a.aiRemaining=100;a.x=3;a.z=0;}
            S.Actor(M).x=0;S.Actor(M).z=0;world.SelectedTarget=S.enemies[0].id;world.Session.Tick(.01f);Sync();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V13-Engine"))+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        [UnityTest] public IEnumerator NewRunHasOneVisibleBodyAndNoBackgroundAllyAttacks()
        {
            var root=world.transform.Find("Actor views");Assert.That(root.Find(M).gameObject.activeSelf,Is.True);
            Assert.That(root.Find(E).gameObject.activeSelf,Is.False);Assert.That(root.Find(F).gameObject.activeSelf,Is.False);
            string before=CombatJson.Write(new{S.actors,S.random});world.AdvanceParty(30);Assert.That(CombatJson.Write(new{S.actors,S.random}),Is.EqualTo(before));
            Assert.That(SwitchToEvan().reason,Is.EqualTo("core_locked_for_run"));yield return null;
        }
        CombatResult SwitchToEvan()=>world.SwitchActive(E);
        [UnityTest] public IEnumerator FinishedQueueModeStillStartsDueFateAndReleasesBossWait()
        {
            S.mode=CombatMode.QueuedExecution;S.v13.fate=new V13FateEvent{id="fixture-queue-fate",encounterId=S.encounterId,trigger="V12_R4",warning=0};
            world.SendMessage("AdvanceV13Fate",.02f);Assert.That(S.v13.fate.stage,Is.EqualTo("Rolled"));Assert.That(S.v13.fateCount,Is.EqualTo(1));yield return null;
        }
        [UnityTest] public IEnumerator DefeatedCoreShowsRecoveryMenuDespiteLivingLogicalMembers()
        {
            S.Actor(M).hp=0;Assert.That(S.Actor(E).Alive&&S.Actor(F).Alive,Is.True);
            world.ResolveDungeonEncounter();Assert.That(world.DungeonModal,Is.True);Assert.That(world.DungeonChoices.Count,Is.GreaterThan(0));yield return null;
        }
        [UnityTest] public IEnumerator SingleCoreGetsSharedGapAfterAWarningEvenWhenItMisses()
        {
            var a=S.enemies[0];a.x=1;a.z=0;a.aiState=0;a.aiRemaining=0;
            world.AdvanceDungeonEnemies(.01f);Assert.That(a.aiState,Is.EqualTo(1));
            // Move the fixture core outside the already locked cone; this is a controlled geometry test.
            S.Actor(M).x=-8;S.Actor(M).z=-8;a.aiRemaining=.01f;
            world.AdvanceDungeonEnemies(.02f);
            Assert.That(S.v13.enemyRestUntil-S.simulationSeconds,Is.EqualTo(world.Session.Config.v13.threatGap).Within(.001f));
            var other=S.enemies[1];other.aiState=0;other.aiRemaining=0;other.x=-7;other.z=-8;
            world.AdvanceDungeonEnemies(.01f);Assert.That(other.aiState,Is.Not.EqualTo(1));yield return null;
        }
        [UnityTest] public IEnumerator ActualWallBlocksAssistFromCoreWithoutStartingCooldown()
        {
            wall=BattleVisual.Box("fixture wall",new Vector3(1.5f,1,0),new Vector3(.4f,2,4),Color.gray,null,8,true);Physics.SyncTransforms();
            var r=world.RequestAssist(0);Assert.That(r.success,Is.False);Assert.That(S.Actor(E).v12.basicCooldown,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator AssistAppearanceEndsButMarkSourceRemains()
        {
            var r=world.RequestAssist(0);Assert.That(r.success,Is.True,r.reason);world.Session.Tick(.36f);
            var i=S.Actor(E).v12.intent;r=world.ExecuteWorldCommand(new CombatCommand{id="fixture-resolve-mark",kind=CommandKind.ResolveHeroAction,actorId=E,targetId=i.targetId,actionId=i.actionId,orderId=i.id,authority=CommandAuthority.System});
            Assert.That(r.success,Is.True,r.reason);Sync();Assert.That(world.transform.Find("Actor views").Find(E).gameObject.activeSelf,Is.True);
            world.Session.Tick(1.2f);Sync();Assert.That(world.transform.Find("Actor views").Find(E).gameObject.activeSelf,Is.False);Assert.That(S.enemies[0].statuses.Any(x=>x.sourceId==E&&x.v12?.family=="opening"),Is.True);yield return null;
        }
        [UnityTest] public IEnumerator ActualBossPillarCollisionConsumesPillarOnce()
        {
            var boss=S.enemies[0];boss.x=-6;boss.z=0;boss.hp=boss.maxHp=230;boss.aiRemaining=0;
            S.v13.boss=new V13Boss{actorId=boss.id,stage="Charge",move="Charge",remaining=1,dx=1,dz=0,root="fixture-charge"};
            S.Actor(M).x=0;S.Actor(M).z=4;Sync();Physics.SyncTransforms();
            for(int i=0;i<30&&S.v13.boss.stage=="Charge";i++)world.AdvanceDungeonEnemies(.02f);
            Assert.That(S.v13.boss.pillarA,Is.True);Assert.That(S.enemies[0].hp,Is.EqualTo(218));Assert.That(S.v13.boss.stage,Is.EqualTo("Recovery"));yield return null;
        }
        [UnityTest] public IEnumerator BossNodeAndBandUseSameRegisteredGeometry()
        {
            var boss=S.enemies[0];S.v13.boss=new V13Boss{actorId=boss.id,stage="Ritual",root="fixture-ritual",nodeA=true,nodeB=true};S.Actor(M).x=-4;S.Actor(M).z=3;
            world.Session.Tick(.01f);Sync();var r=world.V13AttackNode(M,"v13-node-A");Assert.That(r.success,Is.True,r.reason);Assert.That(S.v13.boss.nodeA,Is.False);Assert.That(S.v13.boss.nodeB,Is.True);
            var shape=AreaShape.Line(world.V13NodePosition("v13-node-B"),Vector3.back,6,2);
            Assert.That(BattleGeometry.Contains(shape,new Vector3(4.99f,0,0)),Is.True);Assert.That(BattleGeometry.Contains(shape,new Vector3(5.01f,0,0)),Is.False);yield return null;
        }
        [UnityTest] public IEnumerator FateTerrainRestoresColliderAndExpiresWithoutBlockingExistingExit()
        {
            S.v13.terrain.Add(new V13Terrain{id="fixture-terrain",kind="cover",x=-6,z=-4,warning=1.2f,remaining=12});
            Assert.That(world.Profile.SaveCurrent("fixture-terrain-disk").success,Is.True);
            UnityEngine.Object.Destroy(world.gameObject);yield return null;
            world=new GameObject("V13 restored terrain fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("continue-exploration");world.enabled=false;Sync();
            Assert.That(S.v13.terrain.Single().id,Is.EqualTo("fixture-terrain"));
            var cover=world.GetComponentsInChildren<Collider>(true).Single(c=>c.name=="潮汐浮石 / fixture-terrain");Assert.That(cover.enabled,Is.False);
            world.Session.Tick(1.3f);Sync();Assert.That(cover.enabled,Is.True);
            Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(-6,0,-6),Vector3.forward*4,.36f,1<<8).z,Is.LessThan(-4));
            world.Session.Tick(12.1f);world.ExecuteWorldCommand(new CombatCommand{id="fixture-expire",kind=CommandKind.V13FateEffects,authority=CommandAuthority.System});Sync();Assert.That(cover.gameObject.activeSelf,Is.False);yield return null;
        }
    }
}

