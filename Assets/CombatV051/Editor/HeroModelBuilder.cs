using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CombatV051.Editor
{
    /// <summary>Original authored low-poly forms, baked into three independent Prefabs. Only owns the new Heroes directory.</summary>
    public static class HeroModelBuilder
    {
        const string Folder = "Assets/CombatV051/Resources/Heroes";
        static readonly Color SkinE = Hex("DAB591"), SkinM = Hex("D1A780"), SkinF = Hex("E9DCCD"), Dark = Hex("283537"), Leather = Hex("704E36"), Gold = Hex("CDA969"), Steel = Hex("B2CDCF");
        static Material shared;
        static string currentName;
        [Serializable] public sealed class Manifest { public string origin = "Original authored procedural 3D geometry; no external model source"; public string unity; public List<Entry> heroes = new List<Entry>(); }
        [Serializable] public sealed class Entry { public string id, prefab, weaponAttachment, hitAttachment; public int meshCount, renderers, triangles, vertices, materialSlots; public Vector3 boundsMin, boundsMax; public bool colliders, shadows; }

        [MenuItem("Action CRPG/Art/Bake Main Character Prefabs")]
        public static void Bake()
        {
            Directory.CreateDirectory(Folder + "/Meshes"); AssetDatabase.Refresh();
            Shader shader = Shader.Find("ActionCRPG/Hero Facets");
            if (!shader) throw new InvalidOperationException("Hero Facets shader missing; import source before Bake.");
            string materialPath = Folder + "/HeroPalette.mat";
            shared = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!shared) { shared = new Material(shader) { name = "Heroes shared vertex-color facets" }; AssetDatabase.CreateAsset(shared, materialPath); }
            else { shared.shader = shader; EditorUtility.SetDirty(shared); }
            var manifest = new Manifest { unity = Application.unityVersion };
            foreach (string name in new[] { "Evan", "Mira", "Finn" })
            {
                currentName = name; GameObject root = Build(name);
                try
                {
                    string path = Folder + "/" + name + ".prefab";
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    var rig = root.GetComponent<HeroVisual>(); var renderers = root.GetComponentsInChildren<MeshRenderer>(); var filters = root.GetComponentsInChildren<MeshFilter>();
                    var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    manifest.heroes.Add(new Entry { id = rig.CharacterId, prefab = path, weaponAttachment = PathTo(rig.WeaponSocket, root.transform), hitAttachment = PathTo(rig.HitSocket, root.transform), meshCount = filters.Length, renderers = renderers.Length, triangles = filters.Sum(f => f.sharedMesh.triangles.Length / 3), vertices = filters.Sum(f => f.sharedMesh.vertexCount), materialSlots = renderers.Sum(r => r.sharedMaterials.Length), boundsMin = bounds.min, boundsMax = bounds.max, colliders = root.GetComponentsInChildren<Collider>().Length != 0, shadows = renderers.Any(r => r.shadowCastingMode != ShadowCastingMode.Off || r.receiveShadows) });
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Reports"); File.WriteAllText("Reports/MAIN_CHARACTER_BAKE.json", JsonUtility.ToJson(manifest, true));
            Debug.Log("MAIN_CHARACTER_BAKE complete: three Prefabs, one shared material. Manifest Reports/MAIN_CHARACTER_BAKE.json");
        }
        /// <summary>Repair only Mira's three existing meshes. Does not save a Prefab, touch material/other heroes, or create missing assets.</summary>
        [MenuItem("Action CRPG/Art/Rebake Mira Shoulder and Fringe Geometry Only")]
        public static void RebakeMiraGeometry()
        {
            foreach (string key in new[] { "FaceHair", "UpperArm0", "UpperArm1" })
                if (!AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Meshes/Mira-" + key + ".asset"))
                    throw new InvalidOperationException("Scoped Mira repair requires its existing mesh: " + key);
            shared = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/HeroPalette.mat");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Mira.prefab");
            if (!shared || !prefab) throw new InvalidOperationException("Scoped Mira repair requires existing palette and Prefab.");
            currentName = "Mira"; var temporaryScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var temporary = new GameObject("Mira scoped geometry repair"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(temporary, temporaryScene);
            try
            {
                BuildHead(temporary.transform, false, true, SkinM);
                BuildUpperArm(temporary.transform, 0, false, true, SkinM, Hex("557362"));
                BuildUpperArm(temporary.transform, 1, false, true, SkinM, Hex("557362"));
            }
            finally { Object.DestroyImmediate(temporary); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(temporaryScene); }
            AssetDatabase.SaveAssets();
            var auditScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, auditScene);
            try
            {
                var rig = root.GetComponent<HeroVisual>(); var renderers = root.GetComponentsInChildren<MeshRenderer>(); var filters = root.GetComponentsInChildren<MeshFilter>();
                var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var manifest = new Manifest { unity = Application.unityVersion, origin = "Mira scoped repair: FaceHair and UpperArm0/1 existing mesh contents only; unchanged asset GUIDs/Prefab/material" };
                manifest.heroes.Add(new Entry { id = rig.CharacterId, prefab = Folder + "/Mira.prefab", weaponAttachment = PathTo(rig.WeaponSocket, root.transform), hitAttachment = PathTo(rig.HitSocket, root.transform), meshCount = filters.Length, renderers = renderers.Length, triangles = filters.Sum(f => f.sharedMesh.triangles.Length / 3), vertices = filters.Sum(f => f.sharedMesh.vertexCount), materialSlots = renderers.Sum(r => r.sharedMaterials.Length), boundsMin = bounds.min, boundsMax = bounds.max, colliders = root.GetComponentsInChildren<Collider>().Length != 0, shadows = renderers.Any(r => r.shadowCastingMode != ShadowCastingMode.Off || r.receiveShadows) });
                Directory.CreateDirectory("Reports"); File.WriteAllText("Reports/MAIN_CHARACTER_MIRA_FIX.json", JsonUtility.ToJson(manifest, true));
            }
            finally { Object.DestroyImmediate(root); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(auditScene); }
            Debug.Log("MIRA_GEOMETRY_REPAIR complete: only existing FaceHair / UpperArm0 / UpperArm1 mesh contents updated.");
        }
        public static void ReviseMiraAndPreview()
        {
            MiraPreviewDirectory(); // Reject missing/old output before changing any asset.
            RebakeMiraGeometry();
            CaptureMiraPreview();
        }
        static string MiraPreviewDirectory()
        {
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-p6-mira-preview");
            if (at < 0 || at + 1 >= args.Length || args[at + 1].StartsWith("-", StringComparison.Ordinal))
                throw new InvalidOperationException("Specify -p6-mira-preview <freshDir>; previous preview evidence will never be overwritten.");
            string output = Path.GetFullPath(args[at + 1]);
            if (Directory.Exists(output) || File.Exists(output)) throw new InvalidOperationException("Refusing existing preview path: " + output);
            return output;
        }
        public static void CaptureMiraPreview()
        {
            string output = MiraPreviewDirectory();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Mira.prefab");
            if (!prefab) throw new InvalidOperationException("Mira Prefab missing; cannot capture.");
            Directory.CreateDirectory(output);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Mira isolated repair preview camera"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.cameraType = CameraType.Preview; camera.orthographic = true; camera.orthographicSize = 1.20f;
            camera.backgroundColor = Hex("233838"); camera.clearFlags = CameraClearFlags.SolidColor; camera.transform.rotation = Quaternion.Euler(55,45,0);
            camera.transform.position = new Vector3(0,.95f,0) - camera.transform.forward * 5; camera.nearClipPlane = .05f; camera.farClipPlane = 20; camera.scene = scene;
            var target = new RenderTexture(768,896,24); camera.targetTexture = target; var previous = RenderTexture.active;
            try
            {
                var hero = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                hero.transform.rotation = Quaternion.Euler(0,180,0); hero.GetComponent<HeroVisual>().Apply(true,false,false,null,1,0);
                camera.Render(); RenderTexture.active = target; var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                try { image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(output,"Mira.png"),image.EncodeToPNG()); }
                finally { Object.DestroyImmediate(image); }
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
            Debug.Log("MIRA_REPAIR_PREVIEW " + output + ": isolated asset render only; no gameplay scene loaded or saved.");
        }
        static string PathTo(Transform leaf, Transform root) { string path = leaf.name; while (leaf.parent && leaf.parent != root) { leaf = leaf.parent; path = leaf.name + "/" + path; } return path; }
        static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var c); return c; }
        static Transform Bone(string name, Transform parent, Vector3 position) { var b = new GameObject(name).transform; b.SetParent(parent, false); b.localPosition = position; return b; }
        static void Mesh(Transform joint, Facets geometry, string key)
        {
            var mesh = geometry.Build(currentName + " " + key); string path = Folder + "/Meshes/" + currentName + "-" + key + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                // CopySerialized updated the saved CPU data but retained stale native vertex buffers in the
                // same Editor process (FIX_01 image: old shoulder cap, new shorter index count, truncated tail).
                // Update the existing Mesh through its native setters; keep its asset object/GUID/name.
                existing.Clear(false); existing.subMeshCount = 1;
                existing.SetVertices(mesh.vertices); existing.SetColors(mesh.colors); existing.SetNormals(mesh.normals);
                existing.SetTriangles(mesh.triangles, 0, false); existing.bounds = mesh.bounds;
                existing.UploadMeshData(false); EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); mesh = existing;
            }
            else AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(key, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(joint, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = shared; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        static GameObject Build(string name)
        {
            bool evan = name == "Evan", mira = name == "Mira", finn = name == "Finn";
            Color skin = evan ? SkinE : mira ? SkinM : SkinF, cloth = evan ? Hex("46697A") : mira ? Hex("557362") : Hex("E6E7D8");
            Color trim = finn ? Hex("809C7B") : Gold;
            float hipY = mira ? .85f : .89f, shoulder = finn ? .265f : .30f;
            var root = new GameObject(name + " authored low-poly hero"); var bones = new Transform[11];
            bones[0] = Bone("Hips", root.transform, new Vector3(0, hipY, 0));
            bones[1] = Bone("Chest", bones[0], new Vector3(0, .05f, 0));
            bones[2] = Bone("Head", bones[1], new Vector3(0, .51f, .005f));
            bones[3] = Bone("UpperArm_L", bones[1], new Vector3(-shoulder, .38f, 0));
            bones[4] = Bone("Forearm_L", bones[3], new Vector3(-.03f, -.27f, .005f));
            bones[5] = Bone("UpperArm_R", bones[1], new Vector3(shoulder, .38f, 0));
            bones[6] = Bone("Forearm_R", bones[5], new Vector3(.03f, -.27f, .005f));
            bones[7] = Bone("Thigh_L", bones[0], new Vector3(-.13f, -.04f, 0));
            bones[8] = Bone("Shin_L", bones[7], new Vector3(0, -.36f, 0));
            bones[9] = Bone("Thigh_R", bones[0], new Vector3(.13f, -.04f, 0));
            bones[10] = Bone("Shin_R", bones[9], new Vector3(0, -.36f, 0));
            for (int i = 0; i < 2; i++) BuildLeg(bones[7 + i * 2], bones[8 + i * 2], i, hipY, evan, mira, skin, cloth);
            var hips = new Facets();
            hips.Loft(new[] { new Vector3(0, -.14f, 0), Vector3.zero, new Vector3(0, .10f, 0) }, new[] { new Vector2(.20f, .115f), new Vector2(.215f, .135f), new Vector2(.175f, .12f) }, evan ? Hex("434B50") : mira ? Hex("414D43") : trim);
            if (finn)
            {
                // Four independently shaped linen panels leave bare lower legs and feet visible.
                hips.Plate(new[] { new Vector2(-.18f,.07f),new Vector2(.18f,.07f),new Vector2(.22f,-.24f),new Vector2(.05f,-.31f),new Vector2(-.20f,-.24f) }, new Vector3(0,0,.13f), .035f, cloth);
                hips.Plate(new[] { new Vector2(-.16f,.07f),new Vector2(.16f,.07f),new Vector2(.19f,-.26f),new Vector2(-.08f,-.32f),new Vector2(-.21f,-.22f) }, new Vector3(0,0,-.13f), .035f, cloth);
            }
            if (evan)
            {
                hips.Plate(new[] { new Vector2(-.20f,.08f),new Vector2(-.055f,.08f),new Vector2(-.065f,-.24f),new Vector2(-.25f,-.27f) }, new Vector3(0,0,.10f), .055f, cloth);
                hips.Plate(new[] { new Vector2(.055f,.08f),new Vector2(.20f,.08f),new Vector2(.24f,-.20f),new Vector2(.07f,-.24f) }, new Vector3(0,0,.10f), .055f, cloth);
                hips.Box(new Vector3(0,-.08f,-.14f),new Vector3(.43f,.27f,.065f),cloth);
            }
            hips.Box(new Vector3(0,.09f,0),new Vector3(.365f,.052f,.28f),finn?trim:Leather);
            hips.Box(new Vector3(.02f,.09f,.15f),new Vector3(.064f,.070f,.025f),Gold); Mesh(bones[0],hips,"PelvisClothes");
            BuildChest(bones[1], evan,mira,skin,cloth,trim); BuildHead(bones[2],evan,mira,skin);
            for (int i=0;i<2;i++) BuildArm(bones[3+i*2],bones[4+i*2],i,evan,mira,skin,cloth,trim);
            var weapon = Bone("WeaponSocket", bones[6], new Vector3(0,-.27f,.02f));
            BuildWeapon(weapon,evan,mira);
            if (mira) BuildShield(Bone("ShieldSocket",bones[4],new Vector3(-.055f,-.16f,.095f)));
            if (evan) BuildNotebook(Bone("NotebookSocket",bones[4],new Vector3(0,-.21f,.055f)));
            var hit = Bone("HitSocket",bones[1],new Vector3(0,.23f,.12f));
            root.AddComponent<HeroVisual>().Configure("char_story_"+name.ToLowerInvariant(),bones,weapon,hit);
            return root;
        }
        static void BuildLeg(Transform thigh, Transform shin, int side, float hipY, bool evan, bool mira, Color skin, Color cloth)
        {
            var top=new Facets();
            top.Loft(new[]{new Vector3(0,.03f,0),new Vector3(0,-.14f,.012f),new Vector3(0,-.36f,0)},new[]{new Vector2(.105f,.108f),new Vector2(.090f,.09f),new Vector2(.063f,.064f)},evan?Hex("475157"):skin);
            if(mira)top.Loft(new[]{new Vector3(0,.04f,0),new Vector3(0,-.15f,0)},new[]{new Vector2(.124f,.128f),new Vector2(.116f,.118f)},Hex("455044"));
            if(evan)top.Box(new Vector3(0,-.29f,.056f),new Vector3(.10f,.10f,.04f),Leather);
            Mesh(thigh,top,"Thigh"+side);
            var lower=new Facets(); float footY=-(hipY-.40f)+.065f;
            lower.Loft(new[]{Vector3.zero,new Vector3(0,-.10f,-.01f),new Vector3(0,footY+.035f,0)},new[]{new Vector2(.065f,.07f),new Vector2(.079f,.08f),new Vector2(.050f,.05f)},evan||mira?Leather:skin);
            lower.Ellipsoid(new Vector3(0,footY,.075f),new Vector3(.077f,.064f,.16f),evan||mira?Dark:skin,8,4);
            if(evan||mira)
            {
                lower.Loft(new[]{new Vector3(0,-.04f,0),new Vector3(0,-.095f,0)},new[]{new Vector2(.081f,.084f),new Vector2(.083f,.086f)},mira?Hex("9C7B52"):Leather);
                lower.Box(new Vector3(.057f,-.075f,.045f),new Vector3(.032f,.035f,.05f),Gold);
                lower.Box(new Vector3(0,footY-.048f,.075f),new Vector3(.15f,.035f,.27f),Hex("27322F"));
            }
            else
            {
                // Narrow wreath around each ankle, flowers at the front/outer side; never a body halo.
                lower.Loft(new[]{new Vector3(0,footY+.13f,0),new Vector3(0,footY+.10f,0)},new[]{new Vector2(.059f,.06f),new Vector2(.06f,.061f)},Hex("73946C"));
                for(int f=0;f<3;f++)
                {
                    var p=new Vector3((f-1)*.04f,footY+.12f,.052f);
                    for(int k=0;k<5;k++){float a=k*Mathf.PI*2/5;lower.Ellipsoid(p+new Vector3(Mathf.Cos(a)*.017f,Mathf.Sin(a)*.017f,0),new Vector3(.014f,.014f,.009f),Hex("EEE9D5"),5,3);}
                    lower.Ellipsoid(p+Vector3.forward*.006f,new Vector3(.008f,.008f,.008f),Gold,5,3);
                }
                for(int t=0;t<3;t++)lower.Ellipsoid(new Vector3((t-1)*.035f,footY-.003f,.197f),new Vector3(.021f,.033f,.035f),skin,5,3);
            }
            Mesh(shin,lower,"ShinFoot"+side);
        }
        static void BuildChest(Transform bone,bool evan,bool mira,Color skin,Color cloth,Color trim)
        {
            var b=new Facets();
            b.Loft(new[]{new Vector3(0,.035f,0),new Vector3(0,.18f,0),new Vector3(0,.36f,0),new Vector3(0,.43f,0)},new[]{new Vector2(.18f,.128f),new Vector2(mira?.21f:.195f,.135f),new Vector2(evan?.25f:mira?.245f:.218f,.14f),new Vector2(.155f,.115f)},cloth);
            b.Loft(new[]{new Vector3(0,.40f,0),new Vector3(0,.54f,0)},new[]{new Vector2(.080f,.075f),new Vector2(.075f,.072f)},skin);
            if(evan)
            {
                b.Plate(new[]{new Vector2(-.07f,.05f),new Vector2(.09f,.05f),new Vector2(.10f,.36f),new Vector2(0,.42f),new Vector2(-.10f,.36f)},new Vector3(0,0,.14f),.028f,Hex("D6CEC0"));
                b.Plate(new[]{new Vector2(-.19f,.36f),new Vector2(-.09f,.42f),new Vector2(-.03f,.29f),new Vector2(-.10f,.17f)},new Vector3(0,0,.16f),.028f,Hex("6D8A92"));
                b.Plate(new[]{new Vector2(.19f,.36f),new Vector2(.09f,.42f),new Vector2(.03f,.29f),new Vector2(.10f,.17f)},new Vector3(0,0,.16f),.028f,Hex("6D8A92"));
                b.Beam(new Vector3(-.19f,.40f,.17f),new Vector3(.19f,.03f,.17f),.035f,.020f,Leather);
                b.Box(new Vector3(.245f,-.02f,-.025f),new Vector3(.22f,.23f,.13f),Leather);
                b.Box(new Vector3(.245f,.075f,.009f),new Vector3(.225f,.045f,.16f),Hex("9D7850"));
                b.Box(new Vector3(.245f,-.025f,.057f),new Vector3(.03f,.13f,.02f),Gold);
                b.Box(new Vector3(-.13f,.22f,.157f),new Vector3(.10f,.095f,.03f),cloth);
                for(int i=0;i<3;i++)b.Ellipsoid(new Vector3(.027f,.10f+i*.07f,.177f),Vector3.one*.015f,Gold,5,3);
            }
            else if(mira)
            {
                b.Plate(new[]{new Vector2(-.17f,.12f),new Vector2(.17f,.12f),new Vector2(.21f,.32f),new Vector2(.09f,.40f),new Vector2(-.09f,.40f),new Vector2(-.21f,.32f)},new Vector3(0,0,.135f),.05f,Leather);
                b.Box(new Vector3(0,.07f,.135f),new Vector3(.37f,.072f,.038f),Hex("B58952"));
                for(int i=0;i<3;i++)b.Beam(new Vector3(-.045f,.16f+i*.06f,.183f),new Vector3(.045f,.18f+i*.06f,.183f),.009f,.009f,Gold);
                b.Box(new Vector3(-.11f,.35f,.17f),new Vector3(.07f,.038f,.027f),Steel);
            }
            else
            {
                b.Plate(new[]{new Vector2(-.17f,.40f),new Vector2(0,.43f),new Vector2(.17f,.40f),new Vector2(.07f,.28f),new Vector2(0,.25f),new Vector2(-.07f,.28f)},new Vector3(0,0,.145f),.018f,trim);
                b.Beam(new Vector3(-.15f,.16f,.145f),new Vector3(.16f,.03f,.145f),.022f,.014f,trim);
                b.Ellipsoid(new Vector3(0,.32f,.175f),new Vector3(.024f,.038f,.013f),Hex("D3B66C"),5,3);
                b.Plate(new[]{new Vector2(-.10f,.35f),new Vector2(.13f,.35f),new Vector2(.16f,-.05f),new Vector2(.04f,-.13f),new Vector2(-.12f,-.06f)},new Vector3(0,0,-.15f),.025f,Hex("D1DECF"));
            }
            Mesh(bone,b,"TorsoOutfit");
        }
        static void BuildHead(Transform bone,bool evan,bool mira,Color skin)
        {
            var b=new Facets(); bool finn=!evan&&!mira; Color hair=evan?Hex("49382D"):mira?Hex("804731"):Hex("DCE3E2");
            b.Loft(new[]{new Vector3(0,.015f,.005f),new Vector3(0,.085f,.022f),new Vector3(0,.205f,0),new Vector3(0,.32f,-.005f)},new[]{new Vector2(.075f,.085f),new Vector2(.128f,.115f),new Vector2(.151f,.126f),new Vector2(.123f,.106f)},skin);
            for(int s=-1;s<=1;s+=2)
            {
                if(finn)b.Plate(new[]{new Vector2(s*.12f,.20f),new Vector2(s*.25f,.255f),new Vector2(s*.165f,.12f)},new Vector3(0,0,-.005f),.055f,skin);
                else b.Ellipsoid(new Vector3(s*.151f,.16f,0),new Vector3(.033f,.059f,.048f),skin,6,4);
                b.Box(new Vector3(s*.066f,.186f,.118f),new Vector3(.065f,.024f,.009f),Hex("F5EBDD"));
                b.Box(new Vector3(s*.063f,.184f,.125f),new Vector3(.024f,.023f,.008f),finn?Hex("97BEBB"):evan?Hex("538490"):Hex("6D6143"));
                b.Box(new Vector3(s*.063f,.186f,.13f),new Vector3(.009f,.018f,.003f),Dark);
                b.Beam(new Vector3(s*.038f,.222f,.12f),new Vector3(s*.095f,.228f,.111f),.009f,.007f,hair);
            }
            b.Ellipsoid(new Vector3(0,.153f,.13f),new Vector3(.023f,.034f,.026f),skin,5,3);
            b.Box(new Vector3(0,.095f,.115f),new Vector3(.047f,.006f,.006f),Hex("A17C69"));
            // Sculpted hair cap with volumetric swept locks, not a hat or a recolored shared head.
            b.Loft(new[]{new Vector3(0,.205f,-.037f),new Vector3(0,.292f,-.021f),new Vector3(evan?-.023f:0,.36f,-.008f),new Vector3(evan?-.037f:0,.39f,-.003f)},new[]{new Vector2(.155f,.112f),new Vector2(.172f,.146f),new Vector2(.119f,.104f),new Vector2(.02f,.026f)},hair,9);
            for(int i=0;!mira && i<5;i++)
            {
                float x=(i-2)*.055f; float tip=finn?.19f+(i%2)*.027f:evan?.16f+i*.02f:.24f+Mathf.Abs(i-2)*.02f;
                b.Beam(new Vector3(x-.025f,.32f,.096f),new Vector3(x+.017f,tip,.143f),.047f,.006f,hair,5);
            }
            if(evan)
            {
                b.Beam(new Vector3(-.11f,.32f,-.065f),new Vector3(-.21f,.29f,-.075f),.059f,.005f,hair,5);
                b.Beam(new Vector3(.10f,.31f,-.074f),new Vector3(.18f,.34f,-.08f),.06f,.006f,hair,5);
            }
            if(mira)
            {
                // One closed swept fringe replaces overlapping hair locks and the coplanar/intersecting headband.
                b.Plate(new[]{new Vector2(-.145f,.28f),new Vector2(-.115f,.34f),new Vector2(.13f,.32f),new Vector2(.15f,.26f),new Vector2(.07f,.235f),new Vector2(-.045f,.245f)},new Vector3(0,0,.128f),.044f,hair);
                b.Ellipsoid(new Vector3(.035f,.27f,-.19f),new Vector3(.077f,.075f,.068f),Gold,7,4);
                b.Beam(new Vector3(.035f,.27f,-.19f),new Vector3(.08f,.14f,-.32f),.093f,.067f,hair,7);
                b.Beam(new Vector3(.08f,.14f,-.32f),new Vector3(.13f,.045f,-.35f),.065f,.008f,hair,6);
            }
            if(finn)
            {
                for(int s=-1;s<=1;s+=2)b.Beam(new Vector3(s*.14f,.29f,-.055f),new Vector3(s*.17f,.12f,-.065f),.057f,.005f,hair,5);
                b.Beam(new Vector3(.075f,.365f,-.01f),new Vector3(.14f,.397f,-.025f),.042f,.003f,Hex("F1F0E9"),5);
            }
            Mesh(bone,b,"FaceHair");
        }
        static void BuildUpperArm(Transform upper,int side,bool evan,bool mira,Color skin,Color cloth)
        {
            // Mira's skin cap is inside the sleeve, separated by 9 cm vertically from its visible top cap.
            // Both caps previously shared y=.045 and produced depth-fighting in the actual 55-degree preview.
            var a=new Facets(); a.Loft(new[]{new Vector3(0,mira?-.045f:.045f,0),new Vector3(0,-.10f,0),new Vector3(side==0?-.03f:.03f,-.27f,.005f)},new[]{new Vector2(mira?.09f:.104f,mira?.093f:.109f),new Vector2(.087f,.089f),new Vector2(.060f,.062f)},mira?skin:cloth);
            if(mira)a.Loft(new[]{new Vector3(0,.045f,0),new Vector3(0,-.055f,0)},new[]{new Vector2(.118f,.121f),new Vector2(.105f,.108f)},cloth);
            Mesh(upper,a,"UpperArm"+side);
        }
        static void BuildArm(Transform upper,Transform fore,int side,bool evan,bool mira,Color skin,Color cloth,Color trim)
        {
            BuildUpperArm(upper,side,evan,mira,skin,cloth);
            var b=new Facets(); b.Loft(new[]{Vector3.zero,new Vector3(0,-.11f,0),new Vector3(0,-.225f,0)},new[]{new Vector2(.061f,.063f),new Vector2(.064f,.065f),new Vector2(.043f,.046f)},evan?cloth:skin);
            b.Loft(new[]{new Vector3(0,-.17f,0),new Vector3(0,-.225f,0)},new[]{new Vector2(.061f,.064f),new Vector2(.057f,.060f)},mira?Leather:trim);
            b.Ellipsoid(new Vector3(0,-.272f,.006f),new Vector3(.050f,.061f,.048f),skin,7,4);
            b.Ellipsoid(new Vector3(side==0?.037f:-.037f,-.247f,.031f),new Vector3(.020f,.035f,.022f),skin,5,3);
            Mesh(fore,b,"ForearmHand"+side);
        }
        static void BuildWeapon(Transform socket,bool evan,bool mira)
        {
            var b=new Facets();
            if(mira)
            {
                b.Beam(new Vector3(0,-.09f,0),new Vector3(0,.095f,0),.030f,.030f,Leather);
                b.Ellipsoid(new Vector3(0,-.115f,0),new Vector3(.048f,.040f,.033f),Gold,6,3);
                b.Box(new Vector3(0,.10f,0),new Vector3(.23f,.045f,.067f),Gold);
                b.Plate(new[]{new Vector2(-.05f,.12f),new Vector2(.05f,.12f),new Vector2(.045f,.73f),new Vector2(0,.84f),new Vector2(-.045f,.73f)},Vector3.zero,.040f,Steel);
                b.Plate(new[]{new Vector2(0,.13f),new Vector2(.044f,.14f),new Vector2(.035f,.72f),new Vector2(0,.81f)},new Vector3(0,0,.024f),.006f,Hex("E6EEE1"));
            }
            else if(evan)
            {
                b.Beam(new Vector3(0,-.11f,0),new Vector3(.025f,.42f,.02f),.025f,.018f,Leather);
                b.Loft(new[]{new Vector3(.023f,.37f,.018f),new Vector3(.025f,.42f,.02f)},new[]{new Vector2(.052f,.044f),new Vector2(.06f,.046f)},Gold,6);
                b.Ellipsoid(new Vector3(.025f,.49f,.02f),new Vector3(.065f,.108f,.052f),Hex("77BBB9"),5,4);
                b.Beam(new Vector3(.025f,.41f,.02f),new Vector3(.095f,.50f,.02f),.018f,.011f,Gold,5);
                b.Beam(new Vector3(.025f,.41f,.02f),new Vector3(-.045f,.50f,.02f),.018f,.011f,Gold,5);
            }
            else
            {
                b.Beam(new Vector3(0,-.10f,0),new Vector3(-.015f,.43f,0),.024f,.011f,Hex("9EAC87"),6);
                b.Beam(new Vector3(-.014f,.29f,0),new Vector3(.11f,.42f,0),.013f,.004f,Hex("8FA281"),5);
                b.Beam(new Vector3(-.014f,.34f,0),new Vector3(-.10f,.48f,0),.012f,.004f,Hex("8FA281"),5);
                b.Ellipsoid(new Vector3(.087f,.405f,0),new Vector3(.062f,.025f,.028f),Hex("89AA83"),5,3);
                b.Ellipsoid(new Vector3(-.09f,.46f,0),new Vector3(.033f,.055f,.027f),Hex("C8D9BA"),5,3);
                b.Ellipsoid(new Vector3(-.015f,.465f,0),new Vector3(.031f,.045f,.027f),Hex("E2DDD0"),6,4);
            }
            Mesh(socket,b,mira?"Sword":evan?"CastingWand":"LivingSprig");
        }
        static void BuildShield(Transform socket)
        {
            var b=new Facets(); var outline=new[]{new Vector2(-.17f,.18f),new Vector2(.17f,.18f),new Vector2(.18f,-.08f),new Vector2(0,-.29f),new Vector2(-.18f,-.08f)};
            b.Plate(outline,Vector3.zero,.08f,Gold);
            b.Plate(outline.Select(p=>p*.86f).ToArray(),new Vector3(0,0,.05f),.04f,Hex("415F55"));
            b.Ellipsoid(new Vector3(0,-.015f,.09f),new Vector3(.064f,.081f,.055f),Steel,7,4);
            Mesh(socket,b,"KiteShield");
        }
        static void BuildNotebook(Transform socket)
        {
            var b=new Facets(); b.Box(new Vector3(0,0,.02f),new Vector3(.19f,.245f,.042f),Hex("DED6BC"));
            b.Box(new Vector3(0,0,-.01f),new Vector3(.21f,.26f,.017f),Leather);b.Box(new Vector3(0,0,.052f),new Vector3(.21f,.26f,.018f),Hex("916646"));
            b.Box(new Vector3(-.098f,0,.021f),new Vector3(.023f,.26f,.07f),Leather);
            b.Box(new Vector3(.055f,0,.065f),new Vector3(.021f,.27f,.015f),Gold);
            b.Box(new Vector3(0,-.15f,.033f),new Vector3(.025f,.065f,.012f),Hex("688F94"));
            Mesh(socket,b,"FieldNotebook");
        }

        // All forms are closed 3D volumes. Facet color is baked once into vertices, so all parts share one material.
        sealed class Facets
        {
            readonly List<Vector3> vertices=new List<Vector3>();readonly List<Color> colors=new List<Color>();readonly List<int> indices=new List<int>();
            void Tri(Vector3 a,Vector3 b,Vector3 c,Color color)
            {
                var normal=Vector3.Cross(b-a,c-a).normalized;float light=.84f+.16f*Mathf.Clamp(Vector3.Dot(normal,new Vector3(-.4f,.8f,.45f).normalized),-1,1);
                Color shade=new Color(color.r*light,color.g*light,color.b*light,1);int n=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);colors.Add(shade);colors.Add(shade);colors.Add(shade);indices.Add(n);indices.Add(n+1);indices.Add(n+2);
            }
            void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color){Tri(a,b,c,color);Tri(a,c,d,color);}
            public void Loft(Vector3[] centers,Vector2[] widths,Color color,int sides=8)
            {
                var rings=new Vector3[centers.Length,sides];
                for(int r=0;r<centers.Length;r++)for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;rings[r,i]=centers[r]+new Vector3(Mathf.Cos(a)*widths[r].x,0,Mathf.Sin(a)*widths[r].y);}
                bool up=centers[centers.Length-1].y>=centers[0].y;
                for(int r=0;r<centers.Length-1;r++)for(int i=0;i<sides;i++){int j=(i+1)%sides;if(up)Quad(rings[r,i],rings[r+1,i],rings[r+1,j],rings[r,j],color);else Quad(rings[r,j],rings[r+1,j],rings[r+1,i],rings[r,i],color);}
                int last=centers.Length-1;
                for(int i=0;i<sides;i++){int j=(i+1)%sides;if(up){Tri(centers[0],rings[0,i],rings[0,j],color);Tri(centers[last],rings[last,j],rings[last,i],color);}else{Tri(centers[0],rings[0,j],rings[0,i],color);Tri(centers[last],rings[last,i],rings[last,j],color);}}
            }
            public void Ellipsoid(Vector3 center,Vector3 radius,Color color,int sides=8,int rows=5)
            {
                var points=new Vector3[rows+1];var widths=new Vector2[rows+1];
                for(int r=0;r<=rows;r++){float a=-Mathf.PI*.5f+r*Mathf.PI/rows;points[r]=center+Vector3.up*(Mathf.Sin(a)*radius.y);widths[r]=new Vector2(Mathf.Max(.005f,Mathf.Cos(a))*radius.x,Mathf.Max(.005f,Mathf.Cos(a))*radius.z);}
                Loft(points,widths,color,sides);
            }
            public void Beam(Vector3 a,Vector3 b,float r0,float r1,Color color,int sides=6)
            {
                Vector3 axis=(b-a).normalized,side=Vector3.Cross(axis,Mathf.Abs(axis.y)>.95f?Vector3.forward:Vector3.up).normalized,other=Vector3.Cross(axis,side);
                for(int i=0;i<sides;i++){float x=i*Mathf.PI*2/sides,y=(i+1)*Mathf.PI*2/sides;var p=side*Mathf.Cos(x)+other*Mathf.Sin(x);var q=side*Mathf.Cos(y)+other*Mathf.Sin(y);Quad(a+p*r0,a+q*r0,b+q*r1,b+p*r1,color);Tri(a,a+q*r0,a+p*r0,color);Tri(b,b+p*r1,b+q*r1,color);}
            }
            public void Box(Vector3 c,Vector3 size,Color color)
            {
                Vector3 h=size*.5f;var p=new Vector3[8];for(int i=0;i<8;i++)p[i]=c+new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z);
                Quad(p[0],p[2],p[3],p[1],color);Quad(p[4],p[5],p[7],p[6],color);Quad(p[0],p[4],p[6],p[2],color);Quad(p[1],p[3],p[7],p[5],color);Quad(p[2],p[6],p[7],p[3],color);Quad(p[0],p[1],p[5],p[4],color);
            }
            public void Plate(Vector2[] polygon,Vector3 center,float thickness,Color color)
            {
                float area=0;for(int i=0;i<polygon.Length;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Length];area+=a.x*b.y-b.x*a.y;}
                if(area<0)Array.Reverse(polygon);
                Vector2 middle=Vector2.zero;foreach(var p in polygon)middle+=p;middle/=polygon.Length;
                Vector3 front=center+Vector3.forward*thickness*.5f,back=center-Vector3.forward*thickness*.5f;
                for(int i=0;i<polygon.Length;i++){var a=(Vector3)polygon[i];var b=(Vector3)polygon[(i+1)%polygon.Length];Tri(front+(Vector3)middle,front+a,front+b,color);Tri(back+(Vector3)middle,back+b,back+a,color);Quad(front+a,back+a,back+b,front+b,color);}
            }
            public Mesh Build(string name){var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
        }

        /// <summary>Isolated asset preview only. Never loads/saves/changes the gameplay scene or camera.</summary>
        [MenuItem("Action CRPG/Art/Capture Main Character Asset Previews")]
        public static void CapturePreview()
        {
            string output="Reports/MAIN_CHARACTER_PREVIEWS";Directory.CreateDirectory(output);
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var cameraObject=new GameObject("Hero isolated preview camera");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>();camera.cameraType=CameraType.Preview;camera.orthographic=true;camera.orthographicSize=1.20f;camera.backgroundColor=Hex("233838");camera.clearFlags=CameraClearFlags.SolidColor;
            camera.transform.rotation=Quaternion.Euler(55,45,0);camera.transform.position=new Vector3(0,.95f,0)-camera.transform.forward*5;camera.nearClipPlane=.05f;camera.farClipPlane=20;camera.scene=scene;
            var target=new RenderTexture(768,896,24);camera.targetTexture=target;var previous=RenderTexture.active;
            try
            {
                foreach(string name in new[]{"Evan","Mira","Finn"})
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/"+name+".prefab");if(!prefab)throw new InvalidOperationException("Bake Prefab first: "+name);
                    var hero=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
                    try
                    {
                        // The battle-angle camera sees the front/side of the preview subject; the saved Prefab still faces +Z.
                        hero.transform.rotation=Quaternion.Euler(0,180,0);
                        hero.GetComponent<HeroVisual>().Apply(true,false,false,null,1,0);
                        camera.Render();RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                        try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}finally{Object.DestroyImmediate(image);}
                    }
                    finally{Object.DestroyImmediate(hero);}
                }
            }
            finally{RenderTexture.active=previous;camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);}
            Debug.Log("MAIN_CHARACTER_PREVIEWS are isolated Editor asset renders, not combat/Player/human evidence.");
        }
    }
}
