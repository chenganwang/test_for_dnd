using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Actual engine geometry fixtures with explicit positions/time/resources. They
    // are not natural routes, balance measurements, or human playtest evidence.
    public sealed class V14ModeRuntimeTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        BattleWorld world;string directory;GameObject wall;int sequence;
        CombatState S=>world.State;
        void Sync(){world.SendMessage("SynchronizeViews");Physics.SyncTransforms();}
        string Id()=>"v14-runtime/"+(++sequence);
        IEnumerator Start(PartyCombatMode mode)
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V14 mode engine fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");
            var draft=world.Profile.V11PreparationDraft;draft.teamIds=new List<string>{E,M,F};draft.coreId=M;draft.defaultActiveActorId=M;draft.partyCombatMode=mode;draft.finnTree="growth";
            Assert.That(world.Profile.SaveV11Preparation(draft,Id()).success,Is.True);world.ChooseFrontend("depart");
            if(world.Training)world.FinishP6Training();world.enabled=false;
            Assert.That(world.V14Enabled,Is.True,world.Message);Assert.That(S.v14.partyCombatMode,Is.EqualTo(mode));
            S.Actor(M).x=0;S.Actor(M).z=0;
            var start=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true,roomId="V12_R1"});Assert.That(start.success,Is.True,start.reason);
            foreach(var actor in S.actors){actor.reactionRemaining=100;actor.x=0;actor.z=0;}
            foreach(var enemy in S.enemies){enemy.aiState=2;enemy.aiRemaining=100;enemy.x=8;enemy.z=8;}
            world.Session.Tick(.01f);Sync();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string permitted=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Directory.Exists(directory)&&Path.GetFullPath(directory).StartsWith(permitted,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        ActorState Boss(string stage,string move,float remaining=0)
        {
            var boss=S.enemies[0];boss.x=0;boss.z=0;boss.hp=boss.maxHp=230;boss.aiState=1;boss.aiRemaining=remaining;boss.attackTargetId=M;
            S.v13.boss=new V13Boss{actorId=boss.id,stage=stage,move=move,remaining=remaining,dx=1,dz=0,root=Id(),coreStartX=3,coreStartZ=0};
            return boss;
        }
        void Position(string id,float x,float z){var actor=S.Actor(id);actor.x=x;actor.z=z;}
        void RepeatContact(string bossId,AreaShape shape,int damage,bool save,string root)
        {
            var method=typeof(BattleWorld).GetMethod("V13BossContact",BindingFlags.NonPublic|BindingFlags.Instance);Assert.That(method,Is.Not.Null);
            method.Invoke(world,new object[]{S.Actor(bossId),shape,damage,save,root});
        }

        [UnityTest] public IEnumerator TrioViewsHaveThreeBodiesAndSwitchPreservesTheCore()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Sync();var views=world.transform.Find("Actor views");
            foreach(string id in new[]{E,M,F})Assert.That(views.Find(id).gameObject.activeSelf,Is.True,id);
            string hash=world.Profile.ActiveMode.v11Departure.hash;var switched=world.SwitchActive(E);Assert.That(switched.success,Is.True,switched.reason);
            Assert.That(S.activeActorId,Is.EqualTo(E));Assert.That(S.v13.coreId,Is.EqualTo(M));Assert.That(world.Profile.ActiveMode.v11Departure.hash,Is.EqualTo(hash));
        }
        [UnityTest] public IEnumerator AssistViewsKeepOneBodyAndCannotSwitchToLogicalMembers()
        {
            yield return Start(PartyCombatMode.CoreAndAssist);Sync();var views=world.transform.Find("Actor views");
            Assert.That(views.Find(M).gameObject.activeSelf,Is.True);Assert.That(views.Find(E).gameObject.activeSelf,Is.False);Assert.That(views.Find(F).gameObject.activeSelf,Is.False);
            Assert.That(world.SwitchActive(E).reason,Is.EqualTo("core_locked_for_run"));
        }
        [UnityTest] public IEnumerator TrioSweepBatchesEveryPhysicalContactUnderOneReactionRoot()
        {
            yield return Start(PartyCombatMode.PersistentTrio);var boss=Boss("Warning","Sweep");string bossId=boss.id,root=S.v13.boss.root+"/first";
            Position(M,2,-.4f);Position(E,2,0);Position(F,2,.4f);Sync();world.AdvanceDungeonEnemies(.02f);
            var hits=S.incomingEvents.Where(e=>e.rootId==root).ToArray();Assert.That(hits.Select(e=>e.defenderId),Is.EquivalentTo(new[]{E,M,F}));
            Assert.That(hits.Select(e=>e.reactionEventId).Distinct().Count(),Is.EqualTo(1));Assert.That(hits.All(e=>e.baseDamage==6),Is.True);
            Assert.That(S.pendingThreatEvents.Count(e=>e.rootId==root),Is.EqualTo(1));int[] hp=S.actors.Select(a=>a.hp).ToArray();
            RepeatContact(bossId,AreaShape.Cone(Vector3.zero,Vector3.right,4,50),6,false,root);
            Assert.That(S.incomingEvents.Count(e=>e.rootId==root),Is.EqualTo(3));Assert.That(S.actors.Select(a=>a.hp).ToArray(),Is.EqualTo(hp));
        }
        [UnityTest] public IEnumerator TrioSweepExcludesBodiesBehindAnActualWall()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Boss("Warning","Sweep");string root=S.v13.boss.root+"/first";
            Position(M,2,-1.3f);Position(E,2,0);Position(F,-3,3);
            wall=BattleVisual.Box("V14 fixture line-of-effect wall",new Vector3(1,1,0),new Vector3(.25f,2,.45f),Color.gray,null,8,true);Sync();
            world.AdvanceDungeonEnemies(.02f);var hits=S.incomingEvents.Where(e=>e.rootId==root).Select(e=>e.defenderId).ToArray();
            Assert.That(hits,Does.Contain(M));Assert.That(hits,Does.Not.Contain(E));Assert.That(hits,Does.Not.Contain(F));
        }
        [UnityTest] public IEnumerator TrioChargeCanHitNonOperatedBodiesOnlyOncePerActor()
        {
            yield return Start(PartyCombatMode.PersistentTrio);var boss=Boss("Charge","Charge",1);boss.x=-1;string bossId=boss.id,root=S.v13.boss.root+"/charge";
            Position(M,0,5);Position(E,.2f,.25f);Position(F,.6f,-.25f);Sync();world.AdvanceDungeonEnemies(.1f);
            Assert.That(S.incomingEvents.Where(e=>e.rootId==root).Select(e=>e.defenderId),Is.EquivalentTo(new[]{E,F}));
            int[] hp=S.actors.Select(a=>a.hp).ToArray();RepeatContact(bossId,AreaShape.Circle(BattleWorld.Position(S.Actor(bossId)),1.2f),8,false,root);
            Assert.That(S.incomingEvents.Count(e=>e.rootId==root),Is.EqualTo(2));Assert.That(S.actors.Select(a=>a.hp).ToArray(),Is.EqualTo(hp));
        }
        [UnityTest] public IEnumerator TrioRitualBothBandsAreOnePulseAndDoNotRequireTheActiveActorInside()
        {
            yield return Start(PartyCombatMode.PersistentTrio);var boss=Boss("Pulse","Ritual");boss.z=6;S.v13.boss.phase=2;S.v13.boss.nodeA=S.v13.boss.nodeB=true;string root=S.v13.boss.root+"/pulse";
            Position(M,0,5);Position(E,-4,2);Position(F,4,2);Sync();int evanHp=S.Actor(E).hp,finnHp=S.Actor(F).hp,miraHp=S.Actor(M).hp;
            world.AdvanceDungeonEnemies(.02f);var hits=S.incomingEvents.Where(e=>e.rootId==root).ToArray();
            Assert.That(hits.Select(e=>e.defenderId),Is.EquivalentTo(new[]{E,F}));Assert.That(hits.Select(e=>e.reactionEventId).Distinct().Count(),Is.EqualTo(1));
            Assert.That(hits.All(e=>e.damageApplied&&e.sourceKind=="Save"&&e.baseDamage==6),Is.True);
            Assert.That(S.Actor(E).hp,Is.LessThan(evanHp));Assert.That(S.Actor(F).hp,Is.LessThan(finnHp));Assert.That(S.Actor(M).hp,Is.EqualTo(miraHp));
        }
        [UnityTest] public IEnumerator SwitchingOperatedActorDoesNotRedirectAnExistingBossWarning()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Boss("Warning","Sweep",1);
            Position(M,3,0);Position(E,0,3);Position(F,-6,-6);Sync();var switched=world.SwitchActive(E);Assert.That(switched.success,Is.True,switched.reason);
            world.AdvanceDungeonEnemies(.05f);Assert.That(S.v13.boss.dx,Is.EqualTo(1).Within(.001f));Assert.That(S.v13.boss.dz,Is.EqualTo(0).Within(.001f));
            S.v13.boss.remaining=.5f;Position(M,0,-3);world.AdvanceDungeonEnemies(.05f);
            Assert.That(S.v13.boss.dx,Is.EqualTo(1).Within(.001f));Assert.That(S.v13.boss.dz,Is.EqualTo(0).Within(.001f));
        }
        [UnityTest] public IEnumerator FallenOperatedCoreDoesNotStopBossAgainstLivingTrioMembers()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Boss("Warning","Sweep");string root=S.v13.boss.root+"/first";
            Position(E,2,0);Position(F,2,.3f);S.Actor(M).hp=0;Sync();world.AdvanceDungeonEnemies(.02f);
            Assert.That(S.incomingEvents.Where(e=>e.rootId==root).Select(e=>e.defenderId),Is.EquivalentTo(new[]{E,F}));
            Assert.That(S.v13.boss.stage,Is.EqualTo("Recovery"));
        }
        [UnityTest] public IEnumerator TrioHealingGroundUsesLivingBodiesInItsActualRadius()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Position(M,6,0);Position(E,0,0);Position(F,0,0);
            S.Actor(M).hp-=3;S.Actor(E).hp-=3;S.Actor(F).hp=0;S.Actor(M).v12.groveHealingRemainder=S.Actor(E).v12.groveHealingRemainder=.9;
            int miraHp=S.Actor(M).hp,evanHp=S.Actor(E).hp;S.v13.terrain.Add(new V13Terrain{id=Id(),kind="heal",x=0,z=0,remaining=8,nextTick=0});Sync();
            var command=new CombatCommand{id=Id(),kind=CommandKind.V13FateEffects,authority=CommandAuthority.System};var result=world.ExecuteWorldCommand(command);Assert.That(result.success,Is.True,result.reason);
            Assert.That(S.Actor(E).hp,Is.EqualTo(evanHp+1));Assert.That(S.Actor(M).hp,Is.EqualTo(miraHp));Assert.That(S.Actor(F).hp,Is.Zero);
            result=world.ExecuteWorldCommand(command);Assert.That(result.duplicate,Is.True);Assert.That(S.Actor(E).hp,Is.EqualTo(evanHp+1));
        }
    }
}
