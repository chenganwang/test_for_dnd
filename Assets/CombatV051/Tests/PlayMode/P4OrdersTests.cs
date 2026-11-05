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
    // Explicit deterministic time fixtures exercise the real world executor and Physics capsule sweeps.
    public sealed class P4OrdersTests
    {
        BattleWorld world;string folder;
        ActorState Rogue=>world.State.Actor("rogue");ActorState Fighter=>world.State.Actor("fighter");ActorState Enemy=>world.State.enemies[0];
        [UnitySetUp] public IEnumerator Setup()
        {
            folder=Path.Combine(Path.GetTempPath(),"ActionCrpg-P4Orders",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            world=new GameObject("P4 actual orders fixture").AddComponent<BattleWorld>();world.IgnoreApplicationFocus=true;world.InitializePartyForTests(Path.Combine(folder,"save.json"));world.Controls.Map.Disable();world.enabled=false;
            foreach(var a in world.State.actors){a.x=-4;a.z=-5;a.majorRemaining=0;a.basicAttackIntent=null;a.windupRemaining=0;}
            Rogue.x=0;Enemy.x=4;Enemy.z=-5;Enemy.lockedX=5;Enemy.lockedZ=-5;Enemy.aiRemaining=100;Enemy.hp=Enemy.maxHp=500;
            world.Session.Config.passiveMomentumPerSecond=0;world.State.charges=0;world.SelectedActor="rogue";world.SelectedTarget=Enemy.id;world.SelectedAction="sneak";Physics.SyncTransforms();yield return null;
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            string safe=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCrpg-P4Orders"))+Path.DirectorySeparatorChar;
            if(folder!=null&&Path.GetFullPath(folder).StartsWith(safe,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(folder))Directory.Delete(folder,true);
        }
        GameObject Wall(Vector3 position,Vector3 size)
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Explicit physics wall fixture";wall.layer=8;wall.transform.SetParent(world.transform);wall.transform.position=position;wall.transform.localScale=size;Physics.SyncTransforms();return wall;
        }
        void Step(float dt){float sim=world.Session.Tick(dt);world.AdvanceBasicOrders(sim);}
        [UnityTest] public IEnumerator MoveOrderWalksAroundARealWallThenKeepsTheRequestedPosition()
        {
            Wall(new Vector3(2,1,-5),new Vector3(.5f,2,3));Vector3 start=BattleWorld.Position(Rogue),destination=new Vector3(4,0,-5);Assert.That(world.TryOrderPath(start,destination,out var route));Assert.That(route.Count,Is.GreaterThan(1));
            var order=world.IssueMoveOrder(destination);Assert.That(order.success,Is.True,order.reason);Assert.That(BattleWorld.Position(Rogue),Is.EqualTo(start));float maxDetour=0;
            for(int i=0;i<300&&Rogue.basicOrder.kind!=BasicOrderKind.Hold;i++){Step(.05f);maxDetour=Mathf.Max(maxDetour,Mathf.Abs(Rogue.z+5));}
            Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Hold));Assert.That(maxDetour,Is.GreaterThan(1.6f));Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(Rogue),destination),Is.LessThan(.3f));
            Vector3 arrived=BattleWorld.Position(Rogue);Rogue.majorRemaining=100;for(int i=0;i<10;i++)world.AdvanceParty(.1f);Assert.That(BattleWorld.Position(Rogue),Is.EqualTo(arrived));Assert.That(world.State.charges,Is.Zero);yield return null;
        }
        [UnityTest] public IEnumerator HoldAndFocusTargetPersistAgainstDefaultAi()
        {
            var hold=world.IssueHoldOrder();Assert.That(hold.success);Rogue.majorRemaining=100;Vector3 before=BattleWorld.Position(Rogue);world.AdvanceParty(.5f);Assert.That(BattleWorld.Position(Rogue),Is.EqualTo(before));
            var second=CombatJson.Clone(Enemy);second.id="other-enemy";second.x=.5f;second.z=-5;world.State.enemies.Add(second);
            Assert.That(world.IssueFocusTargetOrder(Enemy.id).reason,Is.EqualTo("basic_order_replacement_required"));Assert.That(world.IssueFocusTargetOrder(Enemy.id,true).success);world.AdvanceParty(.1f);Assert.That(Rogue.aiTargetId,Is.EqualTo(Enemy.id));yield return null;
        }
        [UnityTest] public IEnumerator RearPreviewRejectsWallFilledCandidateArea()
        {
            Wall(new Vector3(2.5f,1,-5),new Vector3(2.6f,2,4));Assert.That(world.TryGetRearDestination(out _,out var reason),Is.False);Assert.That(reason,Is.EqualTo("rear_destination_unreachable"));Assert.That(world.IssuePreparationOrder().success,Is.False);Assert.That(Rogue.setupToken,Is.Null);yield return null;
        }
        [UnityTest] public IEnumerator BasicContactUsesNewWallLineOfEffectAfterItsWindup()
        {
            Fighter.x=0;Fighter.z=-5;Enemy.x=1.5f;world.SelectedActor="fighter";int hp=Enemy.hp;ulong draws=world.State.random.backgroundCombat.drawCount;
            Assert.That(world.BeginBasicAttack("fighter",Enemy.id).success);Wall(new Vector3(.75f,1,-5),new Vector3(.3f,2,3));Step(.2f);
            Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(draws));Assert.That(Fighter.basicAttackIntent.resolved);Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));yield return null;
        }
        [UnityTest] public IEnumerator WallSeparatedEnemyCannotProvidePassiveEngagementIncome()
        {
            foreach(var a in world.State.actors){a.x=0;a.z=-5;}Enemy.x=3;var wall=Wall(new Vector3(1.5f,1,-5),new Vector3(.4f,2,8));world.Session.Config.passiveMomentumPerSecond=4;
            world.RefreshEngagementEvidence();Assert.That(world.State.momentumLedger.disengaged);world.Session.Tick(1);Assert.That(world.State.momentum,Is.Zero);
            wall.GetComponent<Collider>().enabled=false;Physics.SyncTransforms();world.RefreshEngagementEvidence();Assert.That(world.State.momentumLedger.disengaged,Is.False);world.Session.Tick(1);Assert.That(world.State.momentum,Is.EqualTo(4));yield return null;
        }
        [UnityTest] public IEnumerator RogueMobileSpecializationChangesRealPreparationMovementOnlyForRogue()
        {
            Vector3 start=BattleWorld.Position(Rogue);Assert.That(world.IssuePreparationOrder().success);world.AdvanceBasicOrders(.1f);float baseline=BattleGeometry.DistanceXZ(start,BattleWorld.Position(Rogue));Assert.That(baseline,Is.GreaterThan(0));
            Rogue.x=start.x;Rogue.z=start.z;Rogue.basicOrder=null;Rogue.preparationCooldown=0;world.Session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="rogue",preparationMoveMultiplier=.85f});Assert.That(world.IssuePreparationOrder().success);world.AdvanceBasicOrders(.1f);float mobile=BattleGeometry.DistanceXZ(start,BattleWorld.Position(Rogue));
            Assert.That(mobile/baseline,Is.EqualTo(.85f/.55f).Within(.02f));Assert.That(world.Session.Config.RulesFor("fighter").preparationMoveMultiplier,Is.EqualTo(.55f));yield return null;
        }
        [UnityTest] public IEnumerator SavedMoveOrderRestoresDestinationAndCanReconstructItsRoute()
        {
            Wall(new Vector3(2,1,-5),new Vector3(.5f,2,3));Assert.That(world.IssueMoveOrder(new Vector3(4,0,-5)).success);Step(.1f);Assert.That(world.Session.Save().success);
            // The saved command is independent of the ephemeral route cache.
            var saved=CombatSession.Load(world.SavePath,world.Session.Config,"p1-party");Assert.That(saved.State.Actor("rogue").basicOrder.kind,Is.EqualTo(BasicOrderKind.Move));Assert.That(saved.State.Actor("rogue").basicOrder.x,Is.EqualTo(4));
            Assert.That(world.TryOrderPath(BattleWorld.Position(saved.State.Actor("rogue")),new Vector3(4,0,-5),out var route));Assert.That(route.Count,Is.GreaterThan(0));yield return null;
        }
    }
}
