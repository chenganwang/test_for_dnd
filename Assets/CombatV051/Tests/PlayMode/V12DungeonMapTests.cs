using System.Collections;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Real colliders/render hierarchy, not a Player victory or enjoyment verdict.
    public sealed class V12DungeonMapTests
    {
        GameObject owner;Camera camera;DungeonMap map;ArtEnvironment art;
        void Build(DungeonConfig config,V12MapObstacle[] obstacles=null)
        {
            map.Initialize(camera,config.rooms.Select(r=>new DungeonRoomGeometry{id=r.id,center=new Vector3(r.x,0,r.z),width=r.width,depth=r.depth}).ToArray(),
                config.passages.Select(p=>new DungeonPassageGeometry{id=p.id,roomA=p.roomA,roomB=p.roomB,center=new Vector3(p.x,0,p.z),width=p.width}).ToArray(),obstacles);
            Physics.SyncTransforms();
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            owner=new GameObject("V12 six room geometry fixture");camera=new GameObject("Fixed camera").AddComponent<Camera>();camera.transform.SetParent(owner.transform);camera.orthographic=true;camera.transform.rotation=Quaternion.Euler(55,45,0);
            map=owner.AddComponent<DungeonMap>();art=owner.AddComponent<ArtEnvironment>();art.Initialize(owner.AddComponent<ArtResources>());yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup(){if(owner)Object.Destroy(owner);yield return null;yield return null;Time.timeScale=1;}
        [UnityTest] public IEnumerator SixPhysicalFloorsMiddleObstacleBothSidesAndShortcutHaveRealColliders()
        {
            var data=V12DungeonDefaults.Create();Build(data.dungeon,data.obstacles.ToArray());yield return null;
            Assert.That(owner.GetComponentsInChildren<FrontWallCutaway>().Length,Is.EqualTo(6));Assert.That(owner.GetComponentsInChildren<P5LowCover>().Length,Is.EqualTo(1));
            foreach(var room in data.dungeon.rooms)Assert.That(map.RoomAt(new Vector3(room.x,0,room.z)),Is.EqualTo(room.id));
            Vector3 start=new Vector3(21,0,0);Assert.That(BattleGeometry.MoveAgainstWalls(start,Vector3.right*10,.36f,1<<8).x,Is.LessThan(25));
            foreach(float z in new[]{-6f,6f}){start=new Vector3(21,0,z);Assert.That(BattleGeometry.MoveAgainstWalls(start,Vector3.right*9,.36f,1<<8),Is.EqualTo(start+Vector3.right*9));}
            start=new Vector3(48,0,3);Assert.That(BattleGeometry.MoveAgainstWalls(start,Vector3.right*3,.36f,1<<8).x,Is.LessThan(51));
            map.SetV12WorldFlags(new[]{"V12_LIBRARY_READ"});Assert.That(BattleGeometry.MoveAgainstWalls(start,Vector3.right*3,.36f,1<<8),Is.EqualTo(start+Vector3.right*3));
            map.SetV12WorldFlags(new string[0]);Assert.That(BattleGeometry.MoveAgainstWalls(start,Vector3.right*3,.36f,1<<8).x,Is.LessThan(51));
            foreach(var passage in data.dungeon.passages){map.SetPassage(passage.id,true);Assert.That(map.HasRoute(passage.roomA,passage.roomB));}
            Assert.That(map.HasRoute("V12_R1","V12_R6"));yield return null;
        }
        [UnityTest] public IEnumerator SwitchingHistoricalEightRoomAndNewSixRoomDropsOldCollisionAndArtImmediately()
        {
            var old=new DungeonConfig();var current=V12DungeonDefaults.Create();Build(old);art.AddLandmark("old-hub",Vector3.zero,"rest");
            var oldDoor=owner.GetComponentsInChildren<Collider>(true).Single(c=>c.name=="Door P60_SHORTCUT");var oldMarker=art.Landmarks["old-hub"];
            art.ResetMapPresentation();Build(current.dungeon,current.obstacles.ToArray());
            Assert.That(oldDoor.gameObject.activeInHierarchy,Is.False);Assert.That(oldMarker.gameObject.activeInHierarchy,Is.False);Assert.That(art.Landmarks.Count,Is.Zero);
            Assert.That(owner.GetComponentsInChildren<Collider>().Any(c=>c.name=="Door P60_SHORTCUT"),Is.False);Assert.That(map.RoomAt(Vector3.zero),Is.EqualTo("V12_R1"));yield return null;
            var middle=owner.GetComponentsInChildren<Collider>().Single(c=>c.name==current.obstacles.First(o=>o.roomId=="V12_R2"&&!o.lowCover).id);art.AddLandmark("new-read",new Vector3(47,0,5),"event");var newMarker=art.Landmarks["new-read"];
            art.ResetMapPresentation();Build(old);Assert.That(middle.gameObject.activeInHierarchy,Is.False);Assert.That(newMarker.gameObject.activeInHierarchy,Is.False);Assert.That(map.RoomAt(Vector3.zero),Is.EqualTo("R0"));
            Assert.That(owner.GetComponentsInChildren<FrontWallCutaway>().Length,Is.EqualTo(8));Assert.That(owner.GetComponentsInChildren<P5LowCover>(),Is.Empty);yield return null;
            Assert.That(oldDoor==null&&oldMarker==null&&middle==null&&newMarker==null,Is.True);
        }
    }
}
