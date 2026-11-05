using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using CombatV051.Domain;
using CombatV051.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CombatV051.Editor
{
    /// <summary>Creates the owned v051 entry scene without changing archived v04 assets or metadata.</summary>
    public static class CombatPrototypeBuilder
    {
        public const string GeneratedFolder = "Assets/CombatV051/Generated";
        public const string ScenePath = GeneratedFolder + "/CombatP0.unity";
        private const string MarkerPath = GeneratedFolder + "/GENERATED_BY_COMBAT_V051.txt";
        private const string MarkerText = "Combat v051 generated prototype assets. Preserve authored assets and all existing GUIDs.";
        private const string RendererPath = GeneratedFolder + "/CombatForwardRenderer.asset";
        private const string PipelinePath = GeneratedFolder + "/CombatForwardURP.asset";
        private const string GlobalSettingsPath = GeneratedFolder + "/CombatURPGlobalSettings.asset";
        private const string MaterialPath = GeneratedFolder + "/Resources/CombatP0Unlit.mat";
        private const string TransparentMaterialPath = GeneratedFolder + "/Resources/CombatP0TransparentUnlit.mat";
        private const string OutputMarker = "COMBAT_V051_BUILD_OUTPUT.txt";
        private const string OutputMarkerText = "Combat v051 Windows build output; complete directory required; not the v04 baseline.";
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [MenuItem("Combat v051/Validate Content")]
        public static void ValidateContent()
        {
            LoadConfigResource("CombatConfig");
            var dungeonResource = LoadConfigResource("DungeonConfig");
            var dungeonObject = JObject.Parse(dungeonResource.text);
            foreach (var array in new[] { "rooms", "passages", "enemies", "events", "rules" })
                Require(dungeonObject[array] is JArray && ((JArray)dungeonObject[array]).Count > 0,
                    "DungeonConfig requires an explicit nonempty " + array + " array; implicit C# defaults do not validate authored resources.");
            ValidateDungeon(CombatJson.Read<DungeonConfig>(dungeonResource.text));
            Require(!AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                    assembly.GetName().Name == "ActionDice.Runtime" || assembly.GetName().Name == "ActionDice.Domain"),
                "The v04 combat runtime is still loaded. Archive its sources outside Assets before building v051.");
            WriteReport("V051_ENGINE_ContentValidation.txt",
                "PASS: Actual CombatConfig and DungeonConfig Resources loaded; Dungeon IDs, references, geometry and enemy patterns checked.\n" +
                "CombatConfig SHA256: " + ResourceHash("CombatConfig") + "\n" +
                "DungeonConfig SHA256: " + ResourceHash("DungeonConfig") + "\n" +
                "Competing v04 combat assemblies are absent.\n" +
                "Parameter semantics require separate rule/engine tests. Author text may remain explicitly placeholder.\n" +
                "Editor version: " + Application.unityVersion + "\n");
        }

        private static TextAsset LoadConfigResource(string name)
        {
            var path = "Assets/CombatV051/Resources/" + name + ".json";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Require(asset != null, "Missing actual resource " + path + ".");
            Require(JObject.Parse(asset.text).Properties().Any(), name + " must be a nonempty JSON object.");
            var candidates = AssetDatabase.GetAllAssetPaths().Where(candidate =>
            {
                var normalized = candidate.Replace('\\', '/');
                var marker = normalized.LastIndexOf("/Resources/", StringComparison.Ordinal);
                return marker >= 0 && !AssetDatabase.IsValidFolder(candidate) &&
                    Path.ChangeExtension(normalized.Substring(marker + 11), null) == name;
            }).ToArray();
            Require(candidates.Length == 1 && candidates[0] == path,
                "Resources key " + name + " is ambiguous; preserve assets and resolve duplicate resource keys explicitly.");
            Require(Resources.Load<TextAsset>(name) == asset, "Runtime Resources.Load does not resolve " + path + ".");
            return asset;
        }

        private static string ResourceHash(string name)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(Absolute("Assets/CombatV051/Resources/" + name + ".json")))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        private static HashSet<string> UniqueIds(IEnumerable<string> values, string kind)
        {
            Require(values != null, "Missing " + kind + " collection.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in values)
                Require(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "Empty or duplicate " + kind + " ID: " + id);
            return ids;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateDungeon(DungeonConfig config)
        {
            Require(config != null && !string.IsNullOrWhiteSpace(config.contentVersion), "DungeonConfig contentVersion is required.");
            Require(config.rooms != null && config.passages != null && config.enemies != null && config.events != null && config.rules != null,
                "DungeonConfig collections cannot be null.");
            Require(config.rooms.All(item => item != null) && config.passages.All(item => item != null) &&
                config.enemies.All(item => item != null) && config.events.All(item => item != null) && config.rules.All(item => item != null),
                "DungeonConfig collection entries cannot be null.");
            var rooms = UniqueIds(config.rooms.Select(item => item.id), "room");
            var enemies = UniqueIds(config.enemies.Select(item => item.id), "enemy template");
            var events = UniqueIds(config.events.Select(item => item.id), "event");
            UniqueIds(config.passages.Select(item => item.id), "passage");
            UniqueIds(config.rules.Select(item => item.id), "build rule");
            Require(Finite(config.restHealFraction) && config.restHealFraction >= 0 && config.restHealFraction <= 1,
                "restHealFraction must be within 0..1.");
            foreach (var room in config.rooms)
            {
                Require(Finite(room.x) && Finite(room.z) && Finite(room.width) && Finite(room.depth) && room.width > 0 && room.depth > 0,
                    "Invalid room geometry: " + room.id);
                Require(room.enemies != null && room.eventIds != null && room.enemies.All(item => item != null), "Invalid room collections: " + room.id);
                UniqueIds(room.enemies.Select(item => item.id), "spawn in " + room.id);
                foreach (var spawn in room.enemies)
                    Require(enemies.Contains(spawn.templateId) && Finite(spawn.x) && Finite(spawn.z), "Unknown enemy template or invalid spawn: " + room.id + "/" + spawn.id);
                foreach (var id in room.eventIds)
                    Require(events.Contains(id) && config.Event(id).roomId == room.id, "Unknown event or room mismatch: " + room.id + "/" + id);
            }
            foreach (var passage in config.passages)
                Require(rooms.Contains(passage.roomA) && rooms.Contains(passage.roomB) && passage.roomA != passage.roomB &&
                    Finite(passage.x) && Finite(passage.z) && Finite(passage.width) && passage.width > 0, "Invalid passage: " + passage.id);
            foreach (var item in config.events)
                Require(rooms.Contains(item.roomId) && config.Room(item.roomId).eventIds.Contains(item.id) &&
                    Finite(item.x) && Finite(item.z) && Finite(item.range) && item.range > 0 && item.dc > 0,
                    "Invalid event location, range, DC or room reference: " + item.id);
            foreach (var enemy in config.enemies)
            {
                Require(enemy.hp > 0 && enemy.armorClass > 0 && Finite(enemy.breakMaximum) && enemy.breakMaximum > 0 &&
                    Finite(enemy.speed) && enemy.speed >= 0 && enemy.patterns != null && enemy.patterns.Count > 0 && enemy.patterns.All(item => item != null),
                    "Invalid enemy template: " + enemy.id);
                UniqueIds(enemy.patterns.Select(item => item.id), "pattern in " + enemy.id);
                foreach (var pattern in enemy.patterns)
                    Require((pattern.shape == "Cone" || pattern.shape == "Circle") && Finite(pattern.windup) && pattern.windup > 0 &&
                        Finite(pattern.recovery) && pattern.recovery >= 0 && Finite(pattern.size) && pattern.size > 0 &&
                        Finite(pattern.radius) && pattern.radius > 0 && Finite(pattern.angle) && pattern.angle > 0 && pattern.angle <= 360 &&
                        Finite(pattern.phaseThreshold) && pattern.phaseThreshold > 0 && pattern.phaseThreshold <= 1 && pattern.damage >= 0,
                        "Invalid enemy telegraph pattern: " + enemy.id + "/" + pattern.id);
            }
            foreach (var rule in config.rules)
                Require((rule.kind == "Law" || rule.kind == "Relic") && Finite(rule.aimMultiplier) && rule.aimMultiplier > 0 &&
                    Finite(rule.reactionMultiplier) && rule.reactionMultiplier > 0, "Invalid build rule: " + rule.id);
        }

        [MenuItem("Combat v051/Build Prototype")]
        public static void BuildPrototype()
        {
            ValidateContent();
            EnsureOwnership();
            var pipeline = EnsureForwardPipeline();
            ConfigurePlayer(pipeline);

            if (!File.Exists(Absolute(ScenePath)))
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    throw new OperationCanceledException("Generation cancelled to preserve unsaved scenes.");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Combat v051 Entry").AddComponent<BattleWorld>();
                Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save owned CombatP0 scene.");
            }
            else
            {
                Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null,
                    "Existing owned scene cannot be imported; its contents and metadata were preserved.");
            }
            ValidateEntryScene();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteReport("V051_ENGINE_PrototypeGeneration.txt",
                "PASS: Owned BattleWorld scene, Universal Forward renderer and Input System prepared.\n" +
                "Scene: " + ScenePath + "\nScene GUID: " + AssetDatabase.AssetPathToGUID(ScenePath) + "\n" +
                "Camera, XZ space and gameplay are created by the runtime and need actual PlayMode/Player validation.\n" +
                "Editor version: " + Application.unityVersion + "\n" +
                "Scene generation is not gameplay, build, human-playtest or target Unity6 verification.\n");
        }

        [MenuItem("Combat v051/Build Windows")]
        public static void BuildWindows()
        {
            try
            {
                BuildPrototype();
                Require(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64),
                    "Windows64 build support is unavailable in this actual editor.");
                var outputDirectory = Path.Combine(ProjectRoot, "Builds", "WindowsV051");
                var marker = Path.Combine(outputDirectory, OutputMarker);
                if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
                    Require(File.Exists(marker) && File.ReadAllText(marker).Trim() == OutputMarkerText,
                        "Existing WindowsV051 output has no matching ownership marker; refusing to overwrite it.");
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(marker, OutputMarkerText + "\n");
                var output = Path.Combine(outputDirectory, "ActionCRPG.exe");
                var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath }, locationPathName = output,
                    target = BuildTarget.StandaloneWindows64, targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                });
                var success = build.summary.result == BuildResult.Succeeded && File.Exists(output);
                var report = "Result: " + build.summary.result + "\n" +
                    "Editor version: " + Application.unityVersion + "\n" +
                    "Target: Windows64 / Mono / Direct3D11\n" +
                    "Prototype: v051 P3 presentation on unchanged P2 dungeon; P0 and P1 retained as explicit regression fixtures\n" +
                    "Scene: " + ScenePath + " (existing shared bootstrap, GUID preserved)\n" +
                    "Scene GUID: " + AssetDatabase.AssetPathToGUID(ScenePath) + "\n" +
                    "Executable: " + output + "\nBytes: " + build.summary.totalSize + "\n" +
                    "Errors: " + build.summary.totalErrors + "\nWarnings: " + build.summary.totalWarnings + "\n" +
                    "Duration: " + build.summary.totalTime + "\nExit code: " + (success ? 0 : 1) + "\n";
                foreach (var step in build.steps)
                    foreach (var message in step.messages)
                        if (message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                            report += message.type + " [" + step.name + "]: " + message.content + "\n";
                WriteReport("V051_ENGINE_WindowsBuild.txt", report);
                if (!success) throw new BuildFailedException("No successful v051 Windows executable was produced.");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                WriteReport("V051_ENGINE_WindowsBuild-failure.txt", "Editor version: " + Application.unityVersion +
                    "\nExit code: 1\n" + error + "\n");
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        private static void EnsureOwnership()
        {
            var directory = Absolute(GeneratedFolder);
            var marker = Absolute(MarkerPath);
            if (File.Exists(marker))
            {
                Require(File.ReadAllText(marker).Trim() == MarkerText, "Unrecognized Generated ownership marker.");
                return;
            }
            Require(!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any(),
                "Generated contains files without a matching marker; author files were left untouched.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(marker, MarkerText + "\n");
            AssetDatabase.ImportAsset(GeneratedFolder, ImportAssetOptions.ImportRecursive);
        }

        private static UniversalRenderPipelineAsset EnsureForwardPipeline()
        {
            EnsureOwnedGlobalSettings();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                Require(!File.Exists(Absolute(RendererPath)), "Unexpected renderer asset type; preserved existing file.");
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.name = "Combat Forward Renderer";
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
#if !UNITY_6000_0_OR_NEWER
            // URP14's CreateInstance leaves its ShaderResources null until an explicit reload.
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
#endif
            renderer.renderingMode = RenderingMode.Forward;
            if(renderer.postProcessData==null)renderer.postProcessData=AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            Require(renderer.postProcessData!=null,"Installed URP post process data missing; no network dependency requested.");
            const string impactProfilePath="Assets/CombatV051/Resources/P46ImpactVolume.asset";
            var impactProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(impactProfilePath);
            if(impactProfile==null)
            {
                Require(!File.Exists(Absolute(impactProfilePath)),"Unexpected impact volume asset preserved.");
                impactProfile=ScriptableObject.CreateInstance<VolumeProfile>();impactProfile.name="P46 Impact Monochrome";
                AssetDatabase.CreateAsset(impactProfile,impactProfilePath);var adjustment=impactProfile.Add<ColorAdjustments>(true);adjustment.saturation.Override(-100);
                AssetDatabase.AddObjectToAsset(adjustment,impactProfile);EditorUtility.SetDirty(impactProfile);
            }
            EditorUtility.SetDirty(renderer);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                Require(!File.Exists(Absolute(PipelinePath)), "Unexpected pipeline asset type; preserved existing file.");
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Combat Forward URP";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var serialized = new SerializedObject(pipeline);
            var list = serialized.FindProperty("m_RendererDataList");
            Require(list != null && list.isArray, "URP renderer-list serialization contract is unavailable.");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            var index = serialized.FindProperty("m_DefaultRendererIndex");
            if (index != null) index.intValue = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            pipeline.supportsHDR = false;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1;
            EditorUtility.SetDirty(pipeline);

            // Runtime geometric objects use this shader; Resources retains it in the actual Player build.
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Require(!File.Exists(Absolute(MaterialPath)), "Unexpected unlit material asset; preserved existing file.");
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                Require(shader != null, "URP Unlit shader is unavailable.");
                Directory.CreateDirectory(Absolute(GeneratedFolder + "/Resources"));
                AssetDatabase.Refresh();
                material = new Material(shader) { name = "Combat P0 Unlit" };
                material.SetColor("_BaseColor", Color.white);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var transparent = AssetDatabase.LoadAssetAtPath<Material>(TransparentMaterialPath);
            if (transparent == null)
            {
                Require(!File.Exists(Absolute(TransparentMaterialPath)), "Unexpected transparent material asset; preserved existing file.");
                transparent = new Material(material) { name = "Combat P0 Transparent Unlit" };
                transparent.SetColor("_BaseColor", new Color(1, 1, 1, .5f));
                transparent.SetFloat("_Surface", 1);
                transparent.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                transparent.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                transparent.SetInt("_ZWrite", 0);
                transparent.SetOverrideTag("RenderType", "Transparent");
                transparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                transparent.renderQueue = (int)RenderQueue.Transparent;
                AssetDatabase.CreateAsset(transparent, TransparentMaterialPath);
            }
            return pipeline;
        }

        private static void EnsureOwnedGlobalSettings()
        {
            // The URP14 concrete type is internal. Use its discovered type without invoking internal methods.
            var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings");
            Require(type != null && typeof(RenderPipelineGlobalSettings).IsAssignableFrom(type),
                "URP global-settings type is unavailable.");
            var global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(GlobalSettingsPath);
            if (global == null)
            {
                Require(!File.Exists(Absolute(GlobalSettingsPath)), "Unexpected global settings asset; existing file preserved.");
                global = (RenderPipelineGlobalSettings)ScriptableObject.CreateInstance(type);
                global.name = "Combat URP Global Settings";
                AssetDatabase.CreateAsset(global, GlobalSettingsPath);
            }
#if UNITY_6000_0_OR_NEWER
            UnityEditor.Rendering.EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset(typeof(UniversalRenderPipeline), global);
#else
            GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(global);
#endif
        }

        private static void ConfigurePlayer(UniversalRenderPipelineAsset pipeline)
        {
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var quality = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(quality, false);
            PlayerSettings.companyName = "AuthorPlaceholder";
            PlayerSettings.productName = "Action CRPG v051 Prototype";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            Require(settings != null, "Cannot load PlayerSettings.");
            var serialized = new SerializedObject(settings);
            var input = serialized.FindProperty("activeInputHandler");
            Require(input != null, "PlayerSettings.activeInputHandler is unavailable.");
            input.intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateEntryScene()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var count = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<BattleWorld>(true).Length);
                Require(count == 1, "Owned entry scene must contain exactly one BattleWorld; existing scene was not regenerated.");
            }
            finally { if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true); }
        }

        private static string Absolute(string path) => Path.GetFullPath(Path.Combine(ProjectRoot, path));
        private static void WriteReport(string name, string body)
        {
            var directory = Path.Combine(ProjectRoot, "Reports");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name), DateTime.UtcNow.ToString("o") + "\n" + body);
            Debug.Log(body);
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
