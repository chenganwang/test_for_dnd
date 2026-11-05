using System.Collections;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    /// <summary>Actual baked-asset and renderer contracts. These are not Player performance or human aesthetic claims.</summary>
    public sealed class MainCharacterModelTests
    {
        GameObject owner;
        static readonly string[] Ids = { "char_story_evan", "char_story_mira", "char_story_finn" };
        [UnitySetUp] public IEnumerator Setup() { owner = new GameObject("Hero asset fixture"); yield return null; }
        [UnityTearDown] public IEnumerator Cleanup() { Object.Destroy(owner); yield return null; yield return null; }
        ArtActorView Attach(string id)
        {
            var root = new GameObject(id); root.transform.SetParent(owner.transform, false);
            root.transform.position = new Vector3(3,.8f,4); root.transform.localScale = new Vector3(.75f,.8f,.75f);
            root.AddComponent<CapsuleCollider>();
            var palette = owner.GetComponent<ArtResources>() ?? owner.AddComponent<ArtResources>();
            return ArtActorView.Attach(root.transform, palette, id == Ids[1] ? ActorRole.Fighter : ActorRole.Mage, null, id);
        }
        static ActorState Actor(string id) => new ActorState { id = id, role = id == Ids[1] ? ActorRole.Fighter : ActorRole.Mage, x = 3, z = 4, hp = 20, maxHp = 20 };

        [UnityTest]
        public IEnumerator ThreeRealIndependentPrefabsHaveBodiesOutfitsWeaponsAndOneSharedPalette()
        {
            Material palette = null; var heads = new System.Collections.Generic.HashSet<Mesh>(); var totals = new System.Collections.Generic.HashSet<int>();
            foreach (string id in Ids)
            {
                var prefab = Resources.Load<GameObject>(HeroVisual.ResourceKey(id)); Assert.That(prefab, Is.Not.Null, "Run the scoped HeroModelBuilder.Bake; no text-only/fallback acceptance.");
                var instance = Object.Instantiate(prefab, owner.transform); var visual = instance.GetComponent<HeroVisual>();
                Assert.That(visual.CharacterId, Is.EqualTo(id)); Assert.That(visual.JointCount, Is.EqualTo(11));
                Assert.That(visual.WeaponSocket, Is.Not.Null); Assert.That(visual.HitSocket, Is.Not.Null);
                Assert.That(instance.GetComponentsInChildren<Collider>(), Is.Empty);
                var renderers = instance.GetComponentsInChildren<MeshRenderer>(); Assert.That(renderers.Length, Is.InRange(12, 13));
                foreach (var r in renderers)
                {
                    Assert.That(r.sharedMaterials.Length, Is.EqualTo(1)); if (!palette) palette = r.sharedMaterial;
                    Assert.That(r.sharedMaterial, Is.SameAs(palette)); Assert.That(r.sharedMaterial.shader.name, Is.EqualTo("ActionCRPG/Hero Facets"));
                    Assert.That(r.sharedMaterial.shader.isSupported, Is.True); Assert.That(r.receiveShadows, Is.False); Assert.That(r.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));
                }
                var meshes = instance.GetComponentsInChildren<MeshFilter>(); int triangles = meshes.Sum(m => m.sharedMesh.triangles.Length / 3); totals.Add(triangles);
                Assert.That(triangles, Is.InRange(1200, 12000));
                foreach (var f in meshes)
                {
                    var mesh = f.sharedMesh; Assert.That(mesh.subMeshCount, Is.EqualTo(1)); Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
                    Assert.That(mesh.bounds.size.x, Is.GreaterThan(.005f)); Assert.That(mesh.bounds.size.y, Is.GreaterThan(.005f)); Assert.That(mesh.bounds.size.z, Is.GreaterThan(.005f), "All parts have actual volume.");
                }
                heads.Add(meshes.Single(f => f.name == "FaceHair").sharedMesh);
                var names = instance.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();
                Assert.That(names, Does.Contain(id == Ids[1] ? "Sword" : id == Ids[0] ? "CastingWand" : "LivingSprig"));
                if (id == Ids[0]) Assert.That(names, Does.Contain("FieldNotebook"));
                if (id == Ids[1]) Assert.That(names, Does.Contain("KiteShield"));
                var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Assert.That(bounds.min.y, Is.InRange(-.015f,.02f)); Assert.That(bounds.max.y, Is.InRange(1.65f,2.05f));
            }
            Assert.That(heads.Count, Is.EqualTo(3), "Cannot be three color variants of one head."); Assert.That(totals.Count, Is.EqualTo(3), "Each outfit/equipment topology is independently built.");
            yield return null;
        }
        [UnityTest]
        public IEnumerator IdentityAttachmentPreservesOriginalColliderFeetRootSelectionAndRuleState()
        {
            foreach (string id in Ids)
            {
                var view = Attach(id); var root = view.transform; var collider = root.GetComponent<CapsuleCollider>();
                var actor = Actor(id); string before = CombatJson.Write(actor); Vector3 center = collider.center; float radius = collider.radius, height = collider.height;
                view.Apply(actor, true, false, true, false, 1);
                Assert.That(view.Hero, Is.Not.Null); Assert.That(view.Archetype, Is.EqualTo(id)); Assert.That(view.Hero.CharacterId, Is.EqualTo(id));
                Assert.That(root.position, Is.EqualTo(new Vector3(3,.8f,4))); Assert.That(root.localScale, Is.EqualTo(new Vector3(.75f,.8f,.75f)));
                Assert.That(view.VisualRoot.position.y, Is.EqualTo(0).Within(.0001f)); Assert.That(view.ActiveRingVisible, Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1)); Assert.That(collider.center, Is.EqualTo(center)); Assert.That(collider.radius, Is.EqualTo(radius)); Assert.That(collider.height, Is.EqualTo(height));
                Assert.That(CombatJson.Write(actor), Is.EqualTo(before)); Assert.That(ArtActorView.Attach(root, owner.GetComponent<ArtResources>(), actor.role, null, id), Is.SameAs(view));
                Assert.That(root.GetComponentsInChildren<HeroVisual>().Length, Is.EqualTo(1));
            }
            yield return null;
        }
        [UnityTest]
        public IEnumerator SixDisplayStatesUseExistingCuesAndNeverChangeHealthOrWorldTransform()
        {
            var view = Attach(Ids[1]); var actor = Actor(Ids[1]); var root = view.transform; Vector3 originalRoot = root.position;
            view.Apply(actor,true,false,false,false,1); Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Idle));
            actor.x += .1f; view.Apply(actor,true,false,false,false,1.1f); Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Move));
            string before = CombatJson.Write(actor);
            view.Cue("attack",2);view.Apply(actor,true,false,false,false,2.15f);Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Attack));
            Quaternion attack = view.Hero.transform.Find("Hips/Chest/UpperArm_R").localRotation;
            view.Cue("spell",3);view.Apply(actor,true,false,true,false,3.15f);Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Skill));
            Assert.That(view.Hero.transform.Find("Hips/Chest/UpperArm_R").localRotation,Is.Not.EqualTo(attack));
            view.Cue("hit",4);view.Apply(actor,true,false,false,false,4.05f);Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Hit));
            Assert.That(CombatJson.Write(actor),Is.EqualTo(before)); Assert.That(root.position,Is.EqualTo(originalRoot));
            actor.hp=0;string dead=CombatJson.Write(actor);view.Apply(actor,false,false,false,false,5);Assert.That(view.Hero.Pose,Is.EqualTo(HeroPose.Down));
            Assert.That(view.ActiveRingVisible,Is.False);Assert.That(view.Hero.transform.Find("Hips").localPosition.y,Is.LessThan(.25f));Assert.That(CombatJson.Write(actor),Is.EqualTo(dead));
            yield return null;
        }
        [UnityTest]
        public IEnumerator RollFreezeKeepsAllHeroJointsStillAndRefreshDoesNotInstantiateMeshesOrMaterials()
        {
            var view=Attach(Ids[2]);var actor=Actor(Ids[2]);view.Cue("spell",1);view.Apply(actor,true,false,true,false,1.10f);
            var transforms=view.Hero.GetComponentsInChildren<Transform>().Select(t=>new{t,p=t.localPosition,r=t.localRotation,s=t.localScale}).ToArray();
            var meshIds=view.Hero.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh.GetInstanceID()).ToArray();var materialIds=view.Hero.GetComponentsInChildren<MeshRenderer>().Select(r=>r.sharedMaterial.GetInstanceID()).ToArray();
            int count=view.Hero.GetComponentsInChildren<Transform>().Length; string before=CombatJson.Write(actor);
            for(int i=0;i<120;i++)view.Apply(actor,true,false,true,true,1.10f+i*.02f);
            yield return null;yield return null;
            foreach(var t in transforms){Assert.That(t.t.localPosition,Is.EqualTo(t.p));Assert.That(t.t.localRotation,Is.EqualTo(t.r));Assert.That(t.t.localScale,Is.EqualTo(t.s));}
            Assert.That(view.Hero.PoseTime,Is.EqualTo(1.10f));Assert.That(CombatJson.Write(actor),Is.EqualTo(before));
            for(int i=0;i<120;i++)view.Apply(actor,true,false,false,false,4+i*.016f);
            Assert.That(view.Hero.GetComponentsInChildren<Transform>().Length,Is.EqualTo(count));
            Assert.That(view.Hero.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh.GetInstanceID()).ToArray(),Is.EqualTo(meshIds));
            Assert.That(view.Hero.GetComponentsInChildren<MeshRenderer>().Select(r=>r.sharedMaterial.GetInstanceID()).ToArray(),Is.EqualTo(materialIds));
        }
        [UnityTest]
        public IEnumerator MainHeroAdditionLeavesLegacyPaladinOnExistingGreatswordPresentation()
        {
            var root=new GameObject("legacy paladin");root.transform.SetParent(owner.transform,false);root.transform.position=Vector3.up*.8f;
            var view=ArtActorView.Attach(root.transform,owner.AddComponent<ArtResources>(),ActorRole.Paladin,null,"char_demo_paladin");
            Assert.That(view.Hero,Is.Null);Assert.That(view.Archetype,Is.EqualTo("paladin"));Assert.That(root.GetComponentsInChildren<Transform>().Any(t=>t.name=="Oathlight greatsword"),Is.True);
            Assert.That(HeroVisual.ResourceKey("unknown-keep-save"),Is.Null);yield return null;
        }
    }
}
