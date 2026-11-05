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
    /// <summary>Explicit placement/clear fixtures reach real configured encounters. Actual components, hit commands and files perform the assertions.</summary>
    public sealed class DungeonEnemyTests
    {
        BattleWorld world;
        string directory, file;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(),"ActionCrpgV051-DungeonEnemies",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory); file = Path.Combine(directory,"profile.json");
            world = new GameObject("Actual dungeon enemy test world").AddComponent<BattleWorld>();
            world.InitializeDungeonForTests(file); world.Controls.Map.Disable();
            Assert.That(world.Profile, Is.Not.Null, world.Message);
            world.ChooseDungeonOption("close");
            world.enabled = false; // AdvanceDungeonEnemies below uses bounded simulation steps; no competing automatic enemy tick.
            Assert.That(world.DungeonModal, Is.False);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null; yield return null;
            string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCrpgV051-DungeonEnemies")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) && Path.GetFullPath(directory).StartsWith(safeRoot,StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory,true);
            Time.timeScale = 1;
        }

        ActorState Fighter => world.State.Actor("fighter");
        ActorState Rogue => world.State.Actor("rogue");
        ActorState Mage => world.State.Actor("mage");
        ActorState Enemy(string template) => world.State.enemies.Single(e => world.Profile.EnemyTemplateFor(e.id)?.id == template);
        static void Place(ActorState actor, Vector3 p) { actor.x = p.x; actor.z = p.z; actor.heightY = p.y; }
        void Action(ProfileCommand command)
        {
            var result = world.ProfileAction(command); Assert.That(result.success, Is.True, command.kind + ": " + result.reason);
        }
        void Enter(string room)
        {
            var geometry = world.DungeonConfig.Room(room); int index = 0;
            // This test fixture positions the complete party safely inside. Traversal itself is covered by DungeonMapTests.
            foreach (var actor in world.State.actors) { Place(actor,new Vector3(geometry.x + index * .6f,0,geometry.z - index * .5f)); index++; }
            Action(new ProfileCommand { kind=ProfileCommandKind.EnterRoom,roomId=room,qualified=true });
            if (!world.Profile.ActiveRoom.resolved && !string.IsNullOrEmpty(geometry.encounterKind))
                Action(new ProfileCommand { kind=ProfileCommandKind.ActivateEncounter,roomId=room,qualified=true });
        }
        void ClearFixture()
        {
            // Direct health-zero is solely an earlier-room setup fixture, never evidence of player combat completion.
            foreach (var enemy in world.State.enemies) enemy.hp = 0;
            Action(new ProfileCommand {kind=ProfileCommandKind.ResolveEncounter,encounterId=world.Profile.ActiveEncounter.id});
            if (world.Profile.PendingReward != null)
                Action(new ProfileCommand {kind=ProfileCommandKind.SkipReward,rewardId=world.Profile.PendingReward.id});
        }
        void Reach(string room)
        {
            Enter("R1"); if (room == "R1") { PrepareActors(); return; } ClearFixture();
            Enter("R2"); Enter("R3"); ClearFixture(); Enter("R2");
            Place(Fighter,new Vector3(48,0,8)); Action(new ProfileCommand {kind=ProfileCommandKind.OpenGate,gateId="P24_SMASH",qualified=true});
            Enter("R4"); if (room == "R4") { PrepareActors(); return; } ClearFixture(); Enter("R5");
            if (room == "R5") { PrepareActors(); return; } ClearFixture(); Enter("R6"); Enter("R7"); PrepareActors();
        }
        void PrepareActors()
        {
            foreach (var actor in world.State.actors)
            {
                actor.majorRemaining=0; actor.guardRemaining=0; actor.dodgeRemaining=0; actor.windupRemaining=0;
                actor.aimPreparing=false; actor.staggerRemaining=0; actor.reactionRemaining=100;
            }
            foreach (var enemy in world.State.enemies) { enemy.aiState=0; enemy.aiRemaining=0; }
        }
        string Shape(ActorState enemy)
        {
            AreaShape shape=world.DungeonEnemyShape(enemy);
            return CombatJson.Write(new {shape.Kind,x=shape.Origin.x,y=shape.Origin.y,z=shape.Origin.z,dx=shape.Direction.x,dz=shape.Direction.z,shape.Radius,shape.HalfAngleDegrees});
        }
        string Precious() => CombatJson.Write(new { world.State.charges, actors=world.State.actors.Select(a=>new {a.id,a.inspiration,a.superiority,a.spellSlots,a.items}).ToArray() });

        void LongR4WallFixture()
        {
            Reach("R4"); Enemy("guard").hp=0; // Isolate the scout left alive in the actual failed Player run.
            // Preserve the failed save's float values. Rounding z to 24.71 moves the .36m
            // capsule microscopically into the cover; a sweep cannot resolve an initial overlap.
            Place(Enemy("scout"),new Vector3(51.6243973f,0,24.7100029f));
            Place(Fighter,new Vector3(51.58934f,0,22.6950932f));
            Place(Rogue,new Vector3(52.6679878f,0,23.2899284f));
            Place(Mage,new Vector3(51.61815f,0,19.115942f));
            foreach(var actor in world.State.actors)actor.majorRemaining=100;
            Physics.SyncTransforms();
            Assert.That(world.DungeonMap.transform.GetComponentsInChildren<Collider>().Count(c=>c.enabled&&
                (c.name=="R4 left low cover"||c.name=="R4 right low cover"||c.name=="Cover R4_CENTER")),Is.EqualTo(3),"Use all three real contiguous R4 cover pieces, not a short replacement wall.");
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(Enemy("scout")),BattleWorld.Position(Mage)),Is.False);
            Vector3 origin=BattleWorld.Position(Enemy("scout"));
            Assert.That(Physics.CheckCapsule(origin+Vector3.up*.36f,origin+Vector3.up*1.24f,.36f,1<<8,QueryTriggerInteraction.Ignore),Is.False,"The exact saved starting point must be outside walls at the actual .36m movement radius.");
            AssertCollisionBoundedStep(BattleWorld.Position(Enemy("scout")),BattleWorld.Position(Enemy("scout")),0,0);
        }

        static void AssertCollisionBoundedStep(Vector3 before,Vector3 after,float speed,float dt)
        {
            Assert.That(BattleGeometry.DistanceXZ(before,after),Is.LessThanOrEqualTo(speed*dt+.002f),"AI must walk at its configured speed, never teleport to a waypoint.");
            var overlaps=Physics.OverlapCapsule(after+Vector3.up*.36f,after+Vector3.up*1.24f,.35f,1<<8,QueryTriggerInteraction.Ignore);
            string detail="before="+before.ToString("F7")+" after="+after.ToString("F7")+" overlaps="+string.Join("; ",overlaps.Select(c=>c.name+" center="+c.bounds.center.ToString("F7")+" size="+c.bounds.size.ToString("F7")));
            Assert.That(overlaps,Is.Empty,"The actor capsule must remain outside the original dungeon walls. "+detail);
        }

        [UnityTest]
        public IEnumerator R4InitialCapsuleOverlapIsRejectedInsteadOfTreatingAMissedSweepAsAWalkablePath()
        {
            LongR4WallFixture();var scout=Enemy("scout");scout.z=24.71f; // Explicitly recreate the rounded, invalid starting capsule.
            Vector3 before=BattleWorld.Position(scout);string rng=CombatJson.Write(world.State.random);
            Assert.That(Physics.CheckCapsule(before+Vector3.up*.36f,before+Vector3.up*1.24f,.36f,1<<8,QueryTriggerInteraction.Ignore),Is.True);
            Assert.That(world.TryOrderPath(before,BattleWorld.Position(Mage),out var path),Is.False,"A cast that begins overlapping cover is not proof of a clear route.");
            for(int i=0;i<20;i++)world.AdvanceDungeonEnemies(.05f);
            Assert.That(BattleWorld.Position(scout),Is.EqualTo(before),"Do not move farther into the wall or teleport to a valid point; keep the invalid-start recovery limitation explicit.");
            AssertCollisionBoundedStep(before,BattleWorld.Position(scout),0,0);
            Assert.That(scout.aiState,Is.EqualTo(0));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng));yield return null;
        }

        [UnityTest]
        public IEnumerator R4ScoutActuallyWalksAroundTheCompleteLongWallAndAcquiresALegalAttack()
        {
            LongR4WallFixture();string id=Enemy("scout").id;
            float speed=world.Profile.EnemyTemplateFor(id).speed,sideDetour=0;int steps=0;
            string precious=Precious(),rng=CombatJson.Write(world.State.random);
            for(;steps<400&&world.State.Actor(id).aiState!=1;steps++)
            {
                Vector3 before=BattleWorld.Position(world.State.Actor(id));world.AdvanceDungeonEnemies(.05f);
                Vector3 after=BattleWorld.Position(world.State.Actor(id));AssertCollisionBoundedStep(before,after,speed,.05f);
                sideDetour=Mathf.Max(sideDetour,Mathf.Abs(after.x-51.6244f));
            }
            var scout=world.State.Actor(id);var defender=world.State.Actor(scout.attackTargetId);
            Assert.That(scout.aiState,Is.EqualTo(1),"The former 180-second deadlock must end through actual movement into an attack windup.");
            Assert.That(sideDetour,Is.GreaterThan(7),"The scout must pass a real end of the 16-metre cover wall.");
            Assert.That(defender,Is.Not.Null);Assert.That(world.HasLineOfEffect(BattleWorld.Position(scout),BattleWorld.Position(defender)),Is.True);
            Assert.That(BattleGeometry.DistanceXZ(BattleWorld.Position(scout),BattleWorld.Position(defender)),Is.LessThanOrEqualTo(world.Profile.EnemyTemplateFor(id).patterns[0].size));
            Assert.That(world.AiPathSearchCount,Is.InRange(1,6),"A stable goal reuses a path rather than searching each movement step.");
            Assert.That(steps,Is.GreaterThan(world.AiPathSearchCount*5));
            Assert.That(Precious(),Is.EqualTo(precious));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng),"Walking and acquiring a warning do not roll attack dice.");yield return null;
        }

        [UnityTest]
        public IEnumerator R4RangedAllyWalksToRealLineOfSightWhileAPlayerHoldOrderKeepsPriority()
        {
            LongR4WallFixture();world.SelectedActor="rogue";
            Assert.That(world.IssueHoldOrder().success,Is.True);world.SelectedActor="fighter";
            Vector3 held=BattleWorld.Position(Rogue);string holdId=Rogue.basicOrder.id;
            string precious=Precious(),rng=CombatJson.Write(world.State.random);float detour=0;
            var scout=Enemy("scout");float range=world.Session.Config.BasicFor(Mage).range;
            bool canFire=false;
            for(int i=0;i<400&&!canFire;i++)
            {
                Vector3 before=BattleWorld.Position(Mage);world.AdvanceParty(.05f);Vector3 after=BattleWorld.Position(Mage);
                AssertCollisionBoundedStep(before,after,world.Presentation.moveSpeed,.05f);
                detour=Mathf.Max(detour,Mathf.Abs(after.x-51.6182f));
                Assert.That(BattleWorld.Position(Rogue),Is.EqualTo(held));Assert.That(Rogue.basicOrder.id,Is.EqualTo(holdId));
                canFire=world.HasLineOfEffect(after,BattleWorld.Position(scout))&&BattleGeometry.DistanceXZ(after,BattleWorld.Position(scout))<=range;
            }
            Assert.That(canFire,Is.True,"An in-range but occluded formation point cannot permanently park a ranged ally behind cover.");
            Assert.That(detour,Is.GreaterThan(7));Assert.That(world.AiPathSearchCount,Is.InRange(1,6));
            for(int i=0;i<20;i++)world.AdvanceParty(.05f);
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(Mage),BattleWorld.Position(scout)),Is.True,"Do not return to the obstructed formation point after reaching a firing position.");
            Assert.That(BattleWorld.Position(Rogue),Is.EqualTo(held));Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Hold));
            Assert.That(Precious(),Is.EqualTo(precious));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng));yield return null;
        }

        [UnityTest]
        public IEnumerator TheConfiguredConeUsesTheFullAngleContractAndActualContactAndWallChecksBeforeBackgroundDice()
        {
            Reach("R1"); var enemy=Enemy("guard"); string id=enemy.id;
            Place(enemy,new Vector3(28,0,-3)); Place(Fighter,new Vector3(30,0,-3)); Place(Rogue,new Vector3(30,0,-2.7f)); Place(Mage,new Vector3(28,0,-5.5f));
            var wall=BattleVisual.Box("Actual ray-blocking fixture",new Vector3(29,1,-2.85f),new Vector3(.15f,2,.15f),Color.gray,world.transform,8);
            Physics.SyncTransforms(); string precious=Precious(); ulong visible=world.State.random.visibleTactical.drawCount, background=world.State.random.backgroundCombat.drawCount;
            world.AdvanceDungeonEnemies(.01f);
            enemy=world.State.Actor(id); var pattern=world.Profile.EnemyTemplateFor(id).patterns[0];
            Assert.That(enemy.aiState,Is.EqualTo(1)); Assert.That(world.State.world.strings["enemyPattern/"+id],Is.EqualTo(pattern.id));
            AreaShape shape=world.DungeonEnemyShape(enemy);
            Assert.That(shape.Kind,Is.EqualTo(AreaKind.Cone)); Assert.That(shape.HalfAngleDegrees,Is.EqualTo(pattern.angle*.5f));
            Assert.That(BattleGeometry.Contains(shape,shape.Origin+Quaternion.AngleAxis(shape.HalfAngleDegrees-1,Vector3.up)*Vector3.right*2),Is.True);
            Assert.That(BattleGeometry.Contains(shape,shape.Origin+Quaternion.AngleAxis(shape.HalfAngleDegrees+1,Vector3.up)*Vector3.right*2),Is.False);
            Assert.That(BattleGeometry.Contains(shape,BattleWorld.Position(Rogue)),Is.True);
            Assert.That(world.HasLineOfEffect(BattleWorld.Position(enemy),BattleWorld.Position(Rogue)),Is.False);
            enemy.aiRemaining=0; world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.incomingEvents.Count,Is.EqualTo(1));
            var incoming=world.State.incomingEvents.Single(); Assert.That(incoming.defenderId,Is.EqualTo("fighter")); Assert.That(incoming.sourceKind,Is.EqualTo("Attack"));
            Assert.That(incoming.baseDamage,Is.EqualTo(pattern.damage)); Assert.That(incoming.rawFaces.Count,Is.EqualTo(1));
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(background+1));
            Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible)); Assert.That(Precious(),Is.EqualTo(precious));
            ulong after=world.State.random.backgroundCombat.drawCount; world.AdvanceDungeonEnemies(.1f);
            Assert.That(world.State.incomingEvents.Count,Is.EqualTo(1)); Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(after));
            UnityEngine.Object.Destroy(wall); yield return null;
        }

        [UnityTest]
        public IEnumerator EliteGroundWarningLocksItsWorldPointAndUsesConfiguredSaveDcHalfDamageAndNoParry()
        {
            Reach("R5"); var elite=Enemy("elite"); string id=elite.id;
            Place(elite,new Vector3(29,0,23)); Place(Fighter,new Vector3(29,0,20)); Place(Rogue,new Vector3(30.8f,0,20)); Place(Mage,new Vector3(34,0,18));
            var eliteTemplate=world.Profile.EnemyTemplateFor(id); var circlePattern=eliteTemplate.patterns.First(p=>p.shape=="Circle");
            elite.hp=Mathf.Max(1,Mathf.FloorToInt(elite.maxHp*Mathf.Min(1,circlePattern.phaseThreshold)));
            var eligible=eliteTemplate.patterns.Where(p=>(float)elite.hp/elite.maxHp<=p.phaseThreshold+.0001f).ToArray();
            elite.attackSequence=Array.IndexOf(eligible,circlePattern); // Select the real deterministic Circle turn, including a configured phase gate.
            Fighter.dexSaveBonus=100; Rogue.dexSaveBonus=-100; Physics.SyncTransforms();
            int fighterHp=Fighter.hp,rogueHp=Rogue.hp; string precious=Precious(); ulong visible=world.State.random.visibleTactical.drawCount,background=world.State.random.backgroundCombat.drawCount;
            world.AdvanceDungeonEnemies(.01f);
            elite=world.State.Actor(id); string patternId=world.State.world.strings["enemyPattern/"+id];
            var pattern=world.Profile.EnemyTemplateFor(id).patterns.Single(p=>p.id==patternId); Assert.That(pattern.shape,Is.EqualTo("Circle"));
            AreaShape shape=world.DungeonEnemyShape(elite); Vector3 originalPoint=shape.Origin;
            Place(Fighter,BattleWorld.Position(Fighter)+Vector3.back*.6f);
            Assert.That(world.DungeonEnemyShape(elite).Origin,Is.EqualTo(originalPoint),"The ground warning cannot track the defender after windup begins.");
            Assert.That(shape.Radius,Is.EqualTo(pattern.radius));
            elite.aiRemaining=0; world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.incomingEvents.Count,Is.EqualTo(2));
            foreach(var incoming in world.State.incomingEvents) { Assert.That(incoming.sourceKind,Is.EqualTo("Save")); Assert.That(incoming.dc,Is.EqualTo(pattern.dc)); Assert.That(incoming.parryable,Is.False); }
            Assert.That(Fighter.hp,Is.EqualTo(fighterHp-pattern.damage/2)); Assert.That(Rogue.hp,Is.EqualTo(rogueHp-pattern.damage));
            Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(background+2)); Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(visible));
            Assert.That(Precious(),Is.EqualTo(precious)); yield return null;
        }

        [UnityTest]
        public IEnumerator APartlyElapsedWarningAndItsPatternSurviveFreezeAndARealFileReloadWithoutAnotherIncomingRoll()
        {
            Reach("R1"); var enemy=Enemy("guard"); string id=enemy.id;
            Place(enemy,new Vector3(28,0,-3)); Place(Fighter,new Vector3(30,0,-3)); Place(Rogue,new Vector3(20,0,-6)); Place(Mage,new Vector3(20,0,-7));
            world.AdvanceDungeonEnemies(.01f); world.AdvanceDungeonEnemies(.2f);
            string shape=Shape(world.State.Actor(id)),pattern=world.State.world.strings["enemyPattern/"+id]; float remaining=world.State.Actor(id).aiRemaining;
            world.SelectedActor="fighter"; world.SelectedTarget=id; world.SelectedAction="attack";
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True); Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True,world.Message);
            string rng=CombatJson.Write(world.State.random),ticket=world.State.pendingRoll.id;
            Vector3 camera=world.BattleCamera.transform.position; Quaternion orientation=world.BattleCamera.transform.rotation;
            world.AdvanceDungeonEnemies(10);
            Assert.That(Shape(world.State.Actor(id)),Is.EqualTo(shape)); Assert.That(world.State.Actor(id).aiRemaining,Is.EqualTo(remaining));
            Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng)); yield return null;
            Assert.That(world.BattleCamera.transform.position,Is.EqualTo(camera)); Assert.That(world.BattleCamera.transform.rotation,Is.EqualTo(orientation));
            world.Suspend(); UnityEngine.Object.Destroy(world.gameObject); yield return null;
            world=new GameObject("Fresh warning reload world").AddComponent<BattleWorld>();
            world.InitializeDungeonForTests(file,startNew:false); world.Controls.Map.Disable(); world.enabled=false; world.ContinueSaved();
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze),world.Message); Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));
            Assert.That(world.State.world.strings["enemyPattern/"+id],Is.EqualTo(pattern)); Assert.That(Shape(world.State.Actor(id)),Is.EqualTo(shape));
            Assert.That(world.State.Actor(id).aiRemaining,Is.EqualTo(remaining)); Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng));
            Assert.That(world.Command(CommandKind.RevealRoll).success,Is.True); Assert.That(world.Command(CommandKind.Cancel).success,Is.True);
            world.State.Actor(id).aiRemaining=0; world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.incomingEvents.Count,Is.EqualTo(1)); string incomingId=world.State.incomingEvents[0].id;
            int hp=Fighter.hp; ulong drawn=world.State.random.backgroundCombat.drawCount; world.Suspend(); world.ContinueSaved();
            world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.incomingEvents.Count,Is.EqualTo(1)); Assert.That(world.State.incomingEvents[0].id,Is.EqualTo(incomingId));
            Assert.That(Fighter.hp,Is.EqualTo(hp)); Assert.That(world.State.random.backgroundCombat.drawCount,Is.EqualTo(drawn)); yield return null;
        }

        [UnityTest]
        public IEnumerator TheMechanismStaysStationaryAtFiniteRangeAndBossHalfHealthChangesOnlyFuturePatternsInTheSameEncounter()
        {
            Reach("R7"); var boss=Enemy("boss"); var mechanism=Enemy("mechanism"); string bossId=boss.id,mechanismId=mechanism.id;
            boss.aiState=2; boss.aiRemaining=100;
            Place(Fighter,new Vector3(-15,0,28)); Place(Rogue,new Vector3(-16,0,29)); Place(Mage,new Vector3(-15,0,29));
            Vector3 mechanismPosition=BattleWorld.Position(mechanism); string rng=CombatJson.Write(world.State.random);
            world.AdvanceDungeonEnemies(1);
            Assert.That(BattleWorld.Position(world.State.Actor(mechanismId)),Is.EqualTo(mechanismPosition)); Assert.That(world.State.Actor(mechanismId).aiState,Is.EqualTo(0));
            Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng));
            Place(Fighter,mechanismPosition+Vector3.right*5); world.AdvanceDungeonEnemies(.01f);
            mechanism=world.State.Actor(mechanismId); Assert.That(mechanism.aiState,Is.EqualTo(1)); Assert.That(world.DungeonEnemyShape(mechanism).Kind,Is.EqualTo(AreaKind.Circle));
            Assert.That(BattleWorld.Position(mechanism),Is.EqualTo(mechanismPosition)); mechanism.aiRemaining=100;
            boss=world.State.Actor(bossId); Place(boss,new Vector3(-29,0,24)); Place(Fighter,new Vector3(-26,0,24)); boss.aiState=0; boss.aiRemaining=0;
            string encounter=world.State.encounterId; int records=world.Profile.ActiveMode.encounters.Count,charges=world.State.charges;
            world.AdvanceDungeonEnemies(.01f); string first=world.State.world.strings["enemyPattern/"+bossId]; string firstShape=Shape(world.State.Actor(bossId));
            boss=world.State.Actor(bossId); boss.hp=Mathf.FloorToInt(boss.maxHp*.4f); int remainingHp=boss.hp;
            world.AdvanceDungeonEnemies(.01f);
            Assert.That(world.State.world.strings["enemyPattern/"+bossId],Is.EqualTo(first)); Assert.That(Shape(world.State.Actor(bossId)),Is.EqualTo(firstShape));
            for(int swing=0;swing<2;swing++)
            {
                world.State.Actor(bossId).aiRemaining=0; world.AdvanceDungeonEnemies(.01f);
                // This fixture advances simulation explicitly, but a real Boss contact can
                // start a cosmetic hold on the real clock. Let that hold finish before
                // advancing recovery; passing dt=2 must not bypass the runtime's stop gate.
                float holdDeadline=Time.realtimeSinceStartup+1;
                while(world.ImpactFeedback.Holding&&Time.realtimeSinceStartup<holdDeadline)yield return null;
                Assert.That(world.ImpactFeedback.Holding,Is.False,"The short cosmetic hold must release before the next Boss pattern.");
                world.AdvanceDungeonEnemies(2);
            }
            boss=world.State.Actor(bossId); string phase=world.State.world.strings["enemyPattern/"+bossId];
            Assert.That(phase,Is.EqualTo("boss-phase2")); Assert.That(world.DungeonEnemyShape(boss).HalfAngleDegrees,Is.EqualTo(45));
            Assert.That(world.State.encounterId,Is.EqualTo(encounter)); Assert.That(world.Profile.ActiveMode.encounters.Count,Is.EqualTo(records));
            Assert.That(world.State.charges,Is.EqualTo(charges)); Assert.That(boss.hp,Is.EqualTo(remainingHp));
            Assert.That(BattleWorld.Position(world.State.Actor(mechanismId)),Is.EqualTo(mechanismPosition)); yield return null;
        }
    }
}
