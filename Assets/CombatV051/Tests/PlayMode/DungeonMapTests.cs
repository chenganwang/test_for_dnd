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
    public sealed class DungeonMapTests
    {
        GameObject owner;
        Camera camera;
        DungeonMap map;
        BattleWorld world;
        string directory;

        // Fixed layout contract fixture. These are geometry inputs, not duplicated progression/ability rules.
        static DungeonRoomGeometry[] Rooms() => new[]
        {
            Room("R0",0,0),Room("R1",26,0),Room("R2",52,0),Room("R3",78,0),
            Room("R4",52,22),Room("R5",26,22),Room("R6",0,22),Room("R7",-26,22)
        };
        static DungeonRoomGeometry Room(string id, float x, float z) => new DungeonRoomGeometry { id = id, center = new Vector3(x,0,z) };
        static DungeonPassageGeometry[] Passages() => new[]
        {
            Passage("P01","R0","R1",13,0),Passage("P12","R1","R2",39,0),Passage("P23","R2","R3",65,0),
            Passage("P24_SMASH","R2","R4",48,11),Passage("P24_OUTER","R2","R4",56,11),
            Passage("P45","R4","R5",39,22),Passage("P56","R5","R6",13,22),Passage("P67","R6","R7",-13,22),
            Passage("P60_SHORTCUT","R6","R0",0,11)
        };
        static DungeonPassageGeometry Passage(string id, string a, string b, float x, float z) =>
            new DungeonPassageGeometry { id = id, roomA = a, roomB = b, center = new Vector3(x,0,z) };

        [UnitySetUp]
        public IEnumerator Setup()
        {
            owner = new GameObject("Actual dungeon geometry test");
            camera = new GameObject("Fixed dungeon test camera").AddComponent<Camera>(); camera.transform.SetParent(owner.transform);
            camera.transform.rotation = Quaternion.Euler(55,45,0); camera.orthographic = true;
            map = owner.AddComponent<DungeonMap>(); map.Initialize(camera, Rooms(), Passages());
            Physics.SyncTransforms(); yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            if (owner) UnityEngine.Object.Destroy(owner);
            yield return null; yield return null;
            string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCrpgV051-DungeonMap")) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(directory) && Path.GetFullPath(directory).StartsWith(safeRoot,StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory,true);
            Time.timeScale = 1;
        }

        [UnityTest]
        public IEnumerator EightPhysicalFloorsHaveIndependentCutawaysAndEveryClosedDoorRetainsItsLayerEightCollider()
        {
            Assert.That(owner.GetComponentsInChildren<FrontWallCutaway>().Length, Is.EqualTo(8));
            foreach (var room in Rooms())
            {
                var floor = owner.GetComponentsInChildren<Transform>().Single(t => t.name == "Floor " + room.id);
                Assert.That(floor.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(floor.GetComponent<Renderer>().bounds.size.x, Is.EqualTo(24).Within(.001));
                Assert.That(floor.GetComponent<Renderer>().bounds.size.z, Is.EqualTo(20).Within(.001));
                Assert.That(map.RoomAt(room.center), Is.EqualTo(room.id));
                Assert.That(map.RoomAt(room.center + Vector3.right * 11.8f, .5f), Is.Null, "Positive room margin must mean safely inside.");
            }
            foreach (var cutaway in owner.GetComponentsInChildren<FrontWallCutaway>()) cutaway.Refresh();
            Assert.That(owner.GetComponentsInChildren<Renderer>().Any(r => r.name.StartsWith("Wall ") && !r.enabled), Is.True);
            foreach (var passage in Passages())
            {
                var door = owner.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Door " + passage.id);
                Assert.That(door.gameObject.layer, Is.EqualTo(8));
                Assert.That(door.GetComponent<BoxCollider>().enabled, Is.True);
                Assert.That(door.gameObject.activeInHierarchy, Is.True);
                var a = Rooms().Single(r => r.id == passage.roomA); var b = Rooms().Single(r => r.id == passage.roomB);
                Vector3 direction = (b.center - a.center).normalized;
                Vector3 start = passage.center - direction * 3;
                Vector3 stopped = BattleGeometry.MoveAgainstWalls(start,direction * 6,.36f,1 << 8);
                Assert.That(BattleGeometry.DistanceXZ(start,stopped), Is.LessThan(3), "Closed physical doorway: " + passage.id);
                Assert.That(map.RoomAt(passage.center), Is.Null, "A corridor is not an entered room.");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheOuterAbilityAlternativeAndTheInsideShortcutUseRealDoorsAndInvalidateCachedRoutesInBothDirections()
        {
            Assert.That(map.HasRoute("R2","R4"), Is.False); // Cache the blocked route before changing a door.
            map.SetPassage("P24_OUTER",true);
            Assert.That(map.IsPassageOpen("P24_SMASH"), Is.False);
            Assert.That(map.HasRoute("R2","R4"), Is.True);
            Assert.That(map.HasRoute("R4","R2"), Is.True);
            Vector3 outerStart = new Vector3(56,0,8);
            Assert.That(BattleGeometry.MoveAgainstWalls(outerStart,Vector3.forward * 6,.36f,1 << 8), Is.EqualTo(new Vector3(56,0,14)));
            Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(48,0,8),Vector3.forward * 6,.36f,1 << 8).z, Is.LessThan(11));
            map.SetPassage("P24_OUTER",false);
            Assert.That(map.HasRoute("R2","R4"), Is.False);
            Assert.That(map.NextWaypoint(new Vector3(52,0,0),new Vector3(52,0,22)), Is.EqualTo(new Vector3(52,0,0)), "A locked route must not request walking through its gate.");
            Assert.That(map.HasRoute("R6","R0"), Is.False);
            map.SetPassage("P60_SHORTCUT",true);
            Assert.That(map.HasRoute("R6","R0") && map.HasRoute("R0","R6"), Is.True);
            Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(0,0,8),Vector3.forward * 6,.36f,1 << 8), Is.EqualTo(new Vector3(0,0,14)));
            Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(0,0,14),Vector3.back * 6,.36f,1 << 8), Is.EqualTo(new Vector3(0,0,8)));
            map.SetPassage("P60_SHORTCUT",false);
            Assert.That(map.HasRoute("R6","R0"), Is.False);
            Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(0,0,14),Vector3.back * 6,.36f,1 << 8).z, Is.GreaterThan(11));
            yield return null;
        }

        [UnityTest]
        public IEnumerator EventAndEnvironmentCoverChangesMoveActualCollisionWhileTheOutsideFailurePathRemainsWalkable()
        {
            Vector3 middle = new Vector3(52,0,21), outside = new Vector3(42,0,21);
            Assert.That(BattleGeometry.MoveAgainstWalls(middle,Vector3.forward * 6,.36f,1 << 8).z, Is.LessThan(24));
            Assert.That(BattleGeometry.MoveAgainstWalls(outside,Vector3.forward * 6,.36f,1 << 8), Is.EqualTo(outside + Vector3.forward * 6));
            map.SetCover("R4_CENTER",true);
            Assert.That(BattleGeometry.MoveAgainstWalls(middle,Vector3.forward * 6,.36f,1 << 8), Is.EqualTo(middle + Vector3.forward * 6));
            Assert.That(BattleGeometry.MoveAgainstWalls(outside,Vector3.forward * 6,.36f,1 << 8), Is.EqualTo(outside + Vector3.forward * 6));
            map.SetCover("R4_CENTER",false);
            Assert.That(BattleGeometry.MoveAgainstWalls(middle,Vector3.forward * 6,.36f,1 << 8).z, Is.LessThan(24));
            Vector3 elite = new Vector3(26,0,21);
            Assert.That(BattleGeometry.MoveAgainstWalls(elite,Vector3.forward * 5,.36f,1 << 8).z, Is.LessThan(24));
            map.SetCover("R5_SHIFT",true);
            Assert.That(BattleGeometry.MoveAgainstWalls(elite,Vector3.forward * 5,.36f,1 << 8), Is.EqualTo(elite + Vector3.forward * 5));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeRealPartyActorsWalkTheOpenRoomGraphAndShortcutWithoutTeleportingOrTurningTheCamera()
        {
            directory = Path.Combine(Path.GetTempPath(),"ActionCrpgV051-DungeonMap",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            world = new GameObject("Actual party using the dungeon graph").AddComponent<BattleWorld>();
            world.InitializePartyForTests(Path.Combine(directory,"profile.json")); world.Controls.Map.Disable();
            // Explicit fixture replaces only old arena collision. Domain resource policy stays in BattleWorld/Session.
            foreach (var collider in world.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            world.State.inCombat = false; world.State.enemies.Clear(); world.State.encounterResolved = false;
            int index = 0;
            foreach (var actor in world.State.actors) { actor.x = -index; actor.z = -index; actor.majorRemaining = 0; actor.guardRemaining = 0; index++; }
            map.Initialize(world.BattleCamera,Rooms(),Passages());
            foreach (var passage in Passages().Where(p => p.id != "P24_SMASH" && p.id != "P60_SHORTCUT")) map.SetPassage(passage.id,true);
            world.PartyNavigation = map.NextWaypoint; Physics.SyncTransforms();
            string rng = CombatJson.Write(world.State.random);
            string resources = CombatJson.Write(world.State.actors.Select(a => new { a.id,a.inspiration,a.superiority,a.spellSlots,a.items }).ToArray());
            Quaternion orientation = world.BattleCamera.transform.rotation;
            var goals = new[] { new Vector3(26,0,0),new Vector3(52,0,0),new Vector3(78,0,0),new Vector3(52,0,0),new Vector3(52,0,20),new Vector3(26,0,22),new Vector3(0,0,22),new Vector3(-26,0,22),new Vector3(0,0,22),Vector3.zero };
            foreach (Vector3 goal in goals)
            {
                if (goal == Vector3.zero) map.SetPassage("P60_SHORTCUT",true);
                string goalRoom = map.RoomAt(goal); bool arrived = false;
                // Accelerated, bounded simulation fixture: every step still uses actual PartyWalk/MoveActor and Physics capsule casts.
                // This proves route/collision behavior, not a real-time 6–8 minute player run.
                for (int step = 0; step < 650; step++)
                {
                    var before = world.State.actors.ToDictionary(a => a.id,a => BattleWorld.Position(a));
                    Vector3 next = map.NextWaypoint(BattleWorld.Position(world.ActiveActor),goal);
                    Vector3 delta = BattleGeometry.Ground(next - BattleWorld.Position(world.ActiveActor));
                    world.MoveActor(world.ActiveActor,delta.normalized * Mathf.Min(delta.magnitude,world.Presentation.moveSpeed * .1f));
                    world.AdvanceParty(.1f); world.Rig.Step(.1f);
                    foreach (var actor in world.State.actors)
                        Assert.That(BattleGeometry.DistanceXZ(before[actor.id],BattleWorld.Position(actor)), Is.LessThanOrEqualTo(world.Presentation.moveSpeed * .1f + .001f), "No teleport: " + actor.id);
                    Assert.That(Quaternion.Angle(world.BattleCamera.transform.rotation,orientation), Is.LessThan(.001f));
                    if (BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),goal) < .25f &&
                        world.State.actors.All(a => map.RoomAt(BattleWorld.Position(a),.8f) == goalRoom && BattleGeometry.DistanceXZ(BattleWorld.Position(a),goal) < 4))
                    { arrived = true; break; }
                    if (step % 8 == 0) yield return null;
                }
                Assert.That(arrived, Is.True, "All three actual actors must clear the doorway into " + goalRoom + ": " + CombatJson.Write(world.State.actors.Select(a => new { a.id,a.x,a.z,a.aiMoveX,a.aiMoveZ }).ToArray()));
            }
            Assert.That(map.IsPassageOpen("P24_SMASH"), Is.False, "The physical outside route was used throughout.");
            Assert.That(CombatJson.Write(world.State.random), Is.EqualTo(rng));
            Assert.That(CombatJson.Write(world.State.actors.Select(a => new { a.id,a.inspiration,a.superiority,a.spellSlots,a.items }).ToArray()), Is.EqualTo(resources));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RebuildingAndDisposingTheMapDropsOldCollisionAndDestroysOnlyItsGeneratedMaterials()
        {
            for (int cycle = 0; cycle < 5; cycle++)
            {
                var oldMaterial = owner.GetComponentsInChildren<Renderer>().First(r => r.name == "Floor R0").sharedMaterial;
                var oldFloor = owner.GetComponentsInChildren<Collider>().First(c => c.name == "Floor R0");
                map.Dispose();
                Assert.That(oldFloor.gameObject.activeInHierarchy, Is.False, "Stale collision is disabled immediately, before delayed Unity destruction.");
                yield return null; yield return null;
                Assert.That(oldFloor == null, Is.True); Assert.That(oldMaterial == null, Is.True);
                map.Initialize(camera,Rooms(),Passages());
                map.SetPassage("P01",true); yield return null;
                Assert.That(owner.GetComponentsInChildren<FrontWallCutaway>().Length, Is.EqualTo(8));
                Assert.That(map.HasRoute("R0","R1"), Is.True); Assert.That(map.HasRoute("R0","R7"), Is.False);
                Assert.That(BattleGeometry.MoveAgainstWalls(new Vector3(10,0,0),Vector3.right * 6,.36f,1 << 8), Is.EqualTo(new Vector3(16,0,0)));
            }
        }
    }
}
