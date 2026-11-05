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
    // Real Unity warning/contact and wall geometry, with explicit fixture positions.
    // This is not an unassisted Player route or a human playtest.
    public sealed class V14EvadeBridgePlayTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        BattleWorld world;string directory;GameObject wall;int sequence;
        CombatState S=>world.State;ActorState Mira=>S.Actor(M);
        string Id()=>"v14-evade-engine/"+(++sequence);
        void Sync(){world.SendMessage("SynchronizeViews");Physics.SyncTransforms();}
        void Place(string id,float x,float z){var actor=S.Actor(id);actor.x=x;actor.z=z;}
        IEnumerator Start(PartyCombatMode mode)
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-EvadeEngine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V14 evade world evidence fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");var draft=world.Profile.V11PreparationDraft;
            draft.teamIds=new List<string>{E,M,F};draft.coreId=M;draft.defaultActiveActorId=M;draft.partyCombatMode=mode;draft.finnTree="growth";
            Assert.That(world.Profile.SaveV11Preparation(draft,Id()).success,Is.True);world.ChooseFrontend("depart");if(world.Training)world.FinishP6Training();world.enabled=false;
            var begin=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true,roomId="V12_R1"});Assert.That(begin.success,Is.True,begin.reason);
            foreach(var a in S.actors){a.reactionRemaining=100;a.x=-3;a.z=4;}Place(M,2,0);
            foreach(var enemy in S.enemies){enemy.aiState=2;enemy.aiRemaining=100;enemy.x=8;enemy.z=8;}
            world.Session.Tick(.01f);Sync();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(wall)UnityEngine.Object.Destroy(wall);if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string permitted=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-EvadeEngine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Directory.Exists(directory)&&Path.GetFullPath(directory).StartsWith(permitted,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        string BeginBoss(int sequenceIndex=0)
        {
            var boss=S.enemies[0];boss.x=0;boss.z=0;boss.hp=boss.maxHp=230;boss.aiState=2;boss.aiRemaining=0;boss.attackTargetId=S.activeActorId;
            S.v13.boss=new V13Boss{actorId=boss.id,stage="Recovery",remaining=0,sequence=sequenceIndex};Sync();world.AdvanceDungeonEnemies(.01f);
            Assert.That(S.v13.boss.stage,Is.EqualTo("Warning"));return S.v13.boss.root;
        }
        IEnumerator EscapingSweep(PartyCombatMode mode)
        {
            yield return Start(mode);string root=BeginBoss();int hp=Mira.hp;string random=CombatJson.Write(S.random);
            Assert.That(S.world.strings.Any(x=>x.Key.StartsWith("v14/evade/")&&x.Value=="watch"),Is.True);
            Place(M,-3,3);Sync();world.AdvanceDungeonEnemies(1.2f);
            Assert.That(Mira.hp,Is.EqualTo(hp));Assert.That(Mira.v14Core?.dodgeRoot,Is.EqualTo(root));Assert.That(Mira.v14Core.dodgeUntil,Is.GreaterThan(S.simulationSeconds));
            Assert.That(S.incomingEvents.Any(e=>e.defenderId==M&&e.rootId.StartsWith(root)),Is.False);Assert.That(CombatJson.Write(S.random),Is.EqualTo(random));
            Assert.That(Mira.v14Core.encounters.Single(e=>e.encounterId==S.encounterId).dodgeRoots.Count(x=>x==root),Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator AssistSweepEscapedThroughRealContactPathAwardsCoreQualification(){yield return EscapingSweep(PartyCombatMode.CoreAndAssist);}
        [UnityTest] public IEnumerator TrioSweepEscapedThroughRealContactPathAwardsCoreQualification(){yield return EscapingSweep(PartyCombatMode.PersistentTrio);}
        [UnityTest] public IEnumerator InitiallyOutsideWarningCannotEarnThroughAnEmptyDodge()
        {
            yield return Start(PartyCombatMode.PersistentTrio);Place(M,-2,2);Place(E,2,0);Assert.That(world.SwitchActive(E).success,Is.True);BeginBoss();
            var dodge=world.Command(CommandKind.StartDodge,actor:M,direction:Vector3.back);Assert.That(dodge.success,Is.True,dodge.reason);Place(M,-2,-2);Sync();world.Session.Tick(.4f);world.AdvanceDungeonEnemies(1.2f);
            Assert.That(Mira.v14Core?.dodgeRoot,Is.Null);Assert.That(Mira.v12.readinessRemaining,Is.Zero);
        }
        [UnityTest] public IEnumerator WallBlockedDodgeThatDoesNotEscapeCoverageDoesNotEarnQualification()
        {
            yield return Start(PartyCombatMode.CoreAndAssist);BeginBoss();wall=BattleVisual.Box("actual wall blocking dodge",new Vector3(2.65f,1,0),new Vector3(.2f,2,3),Color.gray,null,8,true);Sync();
            var dodge=world.Command(CommandKind.StartDodge,actor:M,direction:Vector3.right);Assert.That(dodge.success,Is.True,dodge.reason);float x=Mira.x,z=Mira.z;
            typeof(BattleWorld).GetMethod("MovePlayer",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(world,new object[]{.2f});
            Assert.That(Vector2.Distance(new Vector2(x,z),new Vector2(Mira.x,Mira.z)),Is.LessThanOrEqualTo(.4f));world.Session.Tick(.4f);Sync();world.AdvanceDungeonEnemies(1.2f);
            Assert.That(Mira.v14Core?.dodgeRoot,Is.Null);
        }
        [UnityTest] public IEnumerator ChargeDoesNotAwardOutsideQualificationUntilActualTravelEnds()
        {
            yield return Start(PartyCombatMode.PersistentTrio);string root=BeginBoss(1);Place(M,2,4);Sync();world.AdvanceDungeonEnemies(1.3f);Assert.That(S.v13.boss.stage,Is.EqualTo("Charge"));
            world.AdvanceDungeonEnemies(.03f);Assert.That(S.v13.boss.stage,Is.EqualTo("Charge"));Assert.That(Mira.v14Core?.dodgeRoot,Is.Null);
            for(int n=0;n<20&&S.v13.boss.stage=="Charge";n++)world.AdvanceDungeonEnemies(.1f);
            Assert.That(S.v13.boss.stage,Is.EqualTo("Recovery"));Assert.That(Mira.v14Core?.dodgeRoot,Is.EqualTo(root));
        }
        [UnityTest] public IEnumerator RegisteredOrdinaryContactWithEmptyContactSetStillCommitsAvoidance()
        {
            yield return Start(PartyCombatMode.CoreAndAssist);S.v13.boss=null;var enemy=S.enemies[0];enemy.x=0;enemy.z=0;enemy.aiState=0;enemy.aiRemaining=0;
            if(!S.momentumLedger.registeredHighThreats.Contains(enemy.id))S.momentumLedger.registeredHighThreats.Add(enemy.id);Place(M,1.5f,0);Sync();world.AdvanceDungeonEnemies(.01f);
            enemy=S.Actor(enemy.id);Assert.That(enemy.aiState,Is.EqualTo(1));string root=enemy.attackGroupId;Assert.That(S.momentumLedger.registeredDangerousThreats,Does.Contain(root));Place(M,-4,4);Sync();
            world.AdvanceDungeonEnemies(3);Assert.That(Mira.v14Core?.dodgeRoot,Is.EqualTo(root));Assert.That(S.incomingEvents.Any(e=>e.rootId==root),Is.False);
        }
    }
}
