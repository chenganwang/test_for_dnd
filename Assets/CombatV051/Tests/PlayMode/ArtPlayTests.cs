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
    /// <summary>Procedural art checks, not a human readability or frame-rate verdict.</summary>
    public sealed class ArtPlayTests
    {
        GameObject owner;
        BattleWorld world;
        string directory;
        [UnitySetUp]
        public IEnumerator Setup(){owner=new GameObject("P3 art test owner");yield return null;}
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);
            if(owner)UnityEngine.Object.Destroy(owner);
            yield return null;yield return null;
            string safe=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCrpgV051-Art"))+Path.DirectorySeparatorChar;
            if(!string.IsNullOrEmpty(directory)&&Path.GetFullPath(directory).StartsWith(safe,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
            Time.timeScale=1;
        }
        ArtResources Palette()=>owner.GetComponent<ArtResources>()??owner.AddComponent<ArtResources>();
        [UnityTest]
        public IEnumerator RoleAndEnemySilhouettesHaveActualDistinctWeaponsWithoutAddingColliders()
        {
            var palette=Palette();
            foreach(string kind in new[]{"fighter","rogue","mage","guard","scout","elite","boss","mechanism"})
            {
                var root=new GameObject(kind).transform;root.SetParent(owner.transform);root.position=Vector3.up*.8f;root.localScale=new Vector3(.75f,.8f,.75f);
                ActorRole role=kind=="fighter"?ActorRole.Fighter:kind=="rogue"?ActorRole.Rogue:kind=="mage"?ActorRole.Mage:ActorRole.Enemy;
                var view=ArtActorView.Attach(root,palette,role,kind);
                var actor=new ActorState{id=kind,role=role,isEnemy=role==ActorRole.Enemy};
                view.Apply(actor,kind=="fighter",kind=="guard",false,false,2);
                string[] parts=root.GetComponentsInChildren<Transform>(true).Select(t=>t.name).ToArray();
                Assert.That(parts,Does.Contain(kind=="fighter"?"Sword blade":kind=="rogue"||kind=="scout"?"Right dagger":kind=="mage"?"Staff shaft":kind=="mechanism"?"Projector muzzle":kind=="elite"||kind=="boss"?"Broad axe head":"Front shield"));
                Assert.That(root.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh&&f.sharedMesh.vertexCount>0),Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
                Assert.That(root.position,Is.EqualTo(Vector3.up*.8f));Assert.That(root.localScale,Is.EqualTo(new Vector3(.75f,.8f,.75f)));
                Assert.That(view.VisualRoot.position.y,Is.EqualTo(0).Within(.0001f));
                if(kind=="fighter")Assert.That(view.ActiveRingVisible,Is.True);
                if(kind=="guard")Assert.That(view.TargetRingVisible,Is.True);
            }
            Assert.That(palette.MaterialCount,Is.LessThan(70),"Palette is shared across actors instead of material-per-part.");
            yield return null;
        }
        [UnityTest]
        public IEnumerator FilledCircleConeLineAndRingUseTheRealShapeAndKeepTheRingHole()
        {
            var origin=new Vector3(3,0,7);
            foreach(var shape in new[]{AreaShape.Circle(origin,4),AreaShape.Cone(origin,new Vector3(1,0,1),4,40),AreaShape.Line(origin,new Vector3(1,0,1),6,2),AreaShape.Ring(origin,2,4)})
            {
                Mesh mesh=AreaFillOverlay.CreateMesh(shape);var v=mesh.vertices;var t=mesh.triangles;
                Assert.That(t.Length,Is.GreaterThan(0));
                for(int i=0;i<t.Length;i+=3)
                {
                    var centroid=(v[t[i]]+v[t[i+1]]+v[t[i+2]])/3;
                    Assert.That(BattleGeometry.Contains(shape,centroid),Is.True,shape.Kind+" drew fill outside the authoritative geometry.");
                    if(shape.Kind==AreaKind.Ring)Assert.That(BattleGeometry.DistanceXZ(origin,centroid),Is.GreaterThan(2));
                }
                UnityEngine.Object.Destroy(mesh);
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator DecorationKeepsDoorCollisionLineOfSightAndCutawayVisibility()
        {
            var camera=new GameObject("camera").AddComponent<Camera>();camera.transform.SetParent(owner.transform);camera.transform.rotation=Quaternion.Euler(55,45,0);
            var map=owner.AddComponent<DungeonMap>();map.Initialize(camera,new[]{new DungeonRoomGeometry{id="a",center=Vector3.zero},new DungeonRoomGeometry{id="b",center=new Vector3(26,0,0)}},new[]{new DungeonPassageGeometry{id="ab",roomA="a",roomB="b",center=new Vector3(13,0,0),width=6}});
            Physics.SyncTransforms();
            var colliders=owner.GetComponentsInChildren<Collider>(true);
            var before=colliders.Select(c=>new{c,center=c.bounds.center,size=c.bounds.size,enabled=c.enabled,layer=c.gameObject.layer}).ToArray();
            var environment=owner.AddComponent<ArtEnvironment>();environment.Initialize(Palette());environment.Decorate(owner.transform);
            Physics.SyncTransforms();
            Assert.That(owner.GetComponentsInChildren<Collider>(true).Length,Is.EqualTo(colliders.Length));
            foreach(var item in before){Assert.That(item.c.bounds.center,Is.EqualTo(item.center));Assert.That(item.c.bounds.size,Is.EqualTo(item.size));Assert.That(item.c.enabled,Is.EqualTo(item.enabled));Assert.That(item.c.gameObject.layer,Is.EqualTo(item.layer));}
            Assert.That(Physics.Linecast(new Vector3(11,.55f,0),new Vector3(15,.55f,0),1<<8),Is.True);
            foreach(var cutaway in owner.GetComponentsInChildren<FrontWallCutaway>())cutaway.Refresh();
            foreach(var sync in owner.GetComponentsInChildren<ArtSurfaceVisibility>(true))
            {sync.Refresh();Assert.That(sync.Decorations.All(r=>r.enabled==sync.Source.enabled),Is.True);}
            map.SetPassage("ab",true);Physics.SyncTransforms();
            Assert.That(Physics.Linecast(new Vector3(11,.55f,0),new Vector3(15,.55f,0),1<<8),Is.False);
            Assert.That(map.HasRoute("a","b"),Is.True);
            var door=owner.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Door ab");Assert.That(door.GetComponentsInChildren<Renderer>(true).All(r=>!r.gameObject.activeInHierarchy),Is.True);
            yield return null;
        }
        [UnityTest]
        public IEnumerator ArtRefreshAndCueDedupDoNotWriteGameplayAndFreezeEveryNewWorldAccent()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCrpgV051-Art",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("Art integrated actual world").AddComponent<BattleWorld>();world.InitializePartyForTests(Path.Combine(directory,"profile.json"));world.Controls.Map.Disable();world.enabled=false;
            world.RefreshArtPresentation();
            string dto=CombatJson.Write(world.State);Vector3 camera=world.BattleCamera.transform.position;Quaternion rotation=world.BattleCamera.transform.rotation;
            world.RefreshArtPresentation();world.NotifyArtCue("parry","fighter",null,"same-real-cue");world.NotifyArtCue("parry","fighter",null,"same-real-cue");
            Assert.That(world.ArtEffects.ActiveCount,Is.EqualTo(1));Assert.That(CombatJson.Write(world.State),Is.EqualTo(dto));
            Assert.That(world.Command(CommandKind.Pause).success,Is.True,world.Message);world.RefreshArtPresentation();
            // Canvas layout may settle during pause; this assertion covers world art, not unscaled UI layout.
            var transforms=world.GetComponentsInChildren<Transform>(true).Where(t=>!(t is RectTransform)&&(t.name.StartsWith("P3")||t.GetComponentInParent<ArtActorView>()!=null)).Select(t=>new{t,p=t.localPosition,r=t.localRotation,s=t.localScale}).ToArray();
            string frozen=CombatJson.Write(world.State);
            yield return null;yield return null;
            world.RefreshArtPresentation();
            foreach(var item in transforms)if(item.t){Assert.That(item.t.localPosition,Is.EqualTo(item.p));Assert.That(item.t.localRotation,Is.EqualTo(item.r));Assert.That(item.t.localScale,Is.EqualTo(item.s));}
            Assert.That(CombatJson.Write(world.State),Is.EqualTo(frozen));Assert.That(world.BattleCamera.transform.position,Is.EqualTo(camera));Assert.That(world.BattleCamera.transform.rotation,Is.EqualTo(rotation));
            world.ResetArtObservation();Assert.That(world.ArtEffects.ActiveCount,Is.Zero);
        }
        [UnityTest]
        public IEnumerator PaletteModeChangesOnlyEnvironmentBrightnessAndResourcesDoNotAccumulateAfterCues()
        {
            var palette=Palette();Material stone=palette.Get(ArtResources.Stone,ArtTone.Environment),danger=palette.Get(ArtResources.Danger,ArtTone.Danger);
            string prop=stone.HasProperty("_BaseColor")?"_BaseColor":"_Color";Color normal=stone.GetColor(prop),dangerNormal=danger.GetColor(prop);
            palette.SetMode(true,false);Assert.That(stone.GetColor(prop).grayscale,Is.LessThan(normal.grayscale));Color focus=stone.GetColor(prop);
            palette.SetMode(false,true);Assert.That(stone.GetColor(prop).grayscale,Is.LessThan(focus.grayscale));Assert.That(danger.GetColor(prop),Is.EqualTo(dangerNormal));
            palette.SetMode(false,false);Assert.That(stone.GetColor(prop),Is.EqualTo(normal));
            var effects=owner.AddComponent<ArtCueEffects>();effects.Initialize(palette);int before=palette.MeshCount;
            for(int i=0;i<20;i++){effects.Play("parry",Vector3.zero,i);effects.Refresh(i+1,false);}
            Assert.That(effects.ActiveCount,Is.Zero);Assert.That(palette.MeshCount,Is.EqualTo(before),"P47 prewarms the parry ring/accent pair; first combat use must allocate no mesh.");
            int warm=palette.MeshCount;for(int i=20;i<80;i++){effects.Play("parry",Vector3.zero,i);effects.Refresh(i+1,false);}Assert.That(palette.MeshCount,Is.EqualTo(warm),"Repeated cues may not keep allocating pooled meshes.");yield return null;
        }
    }
}
