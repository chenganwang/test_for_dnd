using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CombatV051.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace CombatV051.Editor
{
    /// <summary>Owns only Resources/Audio. Internal mixer creation is isolated here and fails closed if Unity changes it.</summary>
    public static class P3AudioBuilder
    {
        const string Root = "Assets/CombatV051/Resources/Audio";
        const string MixerPath = Root + "/P3AudioMixer.mixer";
        const string MarkerPath = Root + "/P3AudioOwned.txt";
        const string Marker = "Owned P3 audio assets. Preserve existing GUIDs; no author assets may be overwritten.";
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [MenuItem("Combat v051/P3/Prepare Audio")]
        public static void PrepareAudio()
        {
            Directory.CreateDirectory(Root);
            if (File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() != Marker) throw new InvalidOperationException("Audio ownership marker differs.");
            if (File.Exists(MixerPath) && !File.Exists(MarkerPath)) throw new InvalidOperationException("Refusing to replace an unowned AudioMixer.");
            if (!File.Exists(MarkerPath)) File.WriteAllText(MarkerPath, Marker);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            if (AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath) == null)
            {
                if (File.Exists(MixerPath)) throw new InvalidOperationException("An existing mixer failed import; preserve the file and investigate.");
                CreateMixer();
            }
            foreach (string path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                if (!path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null) throw new InvalidOperationException("No real AudioImporter: " + path);
                bool music = path.Contains("/Music/");
                var samples = importer.defaultSampleSettings;
                samples.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
                samples.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                samples.quality = .65f; samples.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                samples.preloadAudioData = !music;
                importer.defaultSampleSettings = samples;
                importer.forceToMono = !music; importer.loadInBackground = music;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            ValidateAudio();
        }

        static void CreateMixer()
        {
            var editorAssembly = typeof(UnityEditor.Editor).Assembly;
            Type controllerType = editorAssembly.GetType("UnityEditor.Audio.AudioMixerController", true);
            Type groupType = editorAssembly.GetType("UnityEditor.Audio.AudioMixerGroupController", true);
            Type exposedType = editorAssembly.GetType("UnityEditor.Audio.ExposedAudioParameter", true);
            var create = controllerType.GetMethod("CreateMixerControllerAtPath", Members, null, new[] { typeof(string) }, null);
            if (create == null) throw new MissingMethodException("Unity AudioMixerController creation API unavailable; no substitute fake mixer was generated.");
            var controller = (AudioMixer)create.Invoke(null, new object[] { MixerPath });
            controller.name = "P3 Audio"; controller.updateMode = AudioMixerUpdateMode.UnscaledTime;
            object master = Property(controllerType, "masterGroup").GetValue(controller);
            var createGroup = controllerType.GetMethod("CreateNewGroup", Members, null, new[] { typeof(string), typeof(bool) }, null);
            var addChild = controllerType.GetMethod("AddChildToParent", Members, null, new[] { groupType, groupType }, null);
            if (createGroup == null || addChild == null) throw new MissingMethodException("Unity mixer group creation API unavailable.");
            object music = createGroup.Invoke(controller, new object[] { "Music", false });
            object sfx = createGroup.Invoke(controller, new object[] { "SFX", false });
            object ui = createGroup.Invoke(controller, new object[] { "UI", false });
            object ambience = createGroup.Invoke(controller, new object[] { "Ambience", false });
            addChild.Invoke(controller, new[] { music, master }); addChild.Invoke(controller, new[] { sfx, master });
            addChild.Invoke(controller, new[] { ui, sfx }); addChild.Invoke(controller, new[] { ambience, sfx });
            var volumeGuid = groupType.GetMethod("GetGUIDForVolume", Members, null, Type.EmptyTypes, null);
            if (volumeGuid == null) throw new MissingMethodException("Unity mixer volume GUID API unavailable.");
            var parameters = Array.CreateInstance(exposedType, 3);
            object[] groups = { master, music, sfx };
            string[] names = { "MasterVolume", "MusicVolume", "SfxVolume" };
            for (int i = 0; i < groups.Length; i++)
            {
                object parameter = Activator.CreateInstance(exposedType);
                exposedType.GetField("guid", Members).SetValue(parameter, volumeGuid.Invoke(groups[i], null));
                exposedType.GetField("name", Members).SetValue(parameter, names[i]);
                parameters.SetValue(parameter, i);
            }
            Property(controllerType, "exposedParameters").SetValue(controller, parameters);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(MixerPath)) EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceSynchronousImport);
        }
        static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Members) ?? throw new MissingMemberException(type.FullName, name);

        [MenuItem("Combat v051/P3/Validate Audio")]
        public static void ValidateAudio()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null) throw new InvalidOperationException("Missing real P3 mixer. Run PrepareAudio.");
            foreach (var name in new[] { "Master", "Music", "SFX", "UI", "Ambience" })
                if (mixer.FindMatchingGroups(name).Count(g => g.name == name) != 1) throw new InvalidOperationException("Missing/duplicate actual mixer group " + name);
            foreach (var name in new[] { "MasterVolume", "MusicVolume", "SfxVolume" })
                if (!mixer.GetFloat(name, out _)) throw new InvalidOperationException("Missing exposed mixer parameter " + name);
            var notes = new List<string> { "P3 audio resource/import validation", "Unity=" + Application.unityVersion, "Mixer=" + MixerPath, "MixerGUID=" + AssetDatabase.AssetPathToGUID(MixerPath), "Routing=Master/{Music,SFX/{UI,Ambience}}", "This validates imported references, not actual listening or audible device output." };
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                string path = Root + "/SFX/" + cue + ".wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null || clip.length <= .01f) throw new InvalidOperationException("Missing/empty cue " + cue);
                if (Resources.Load<AudioClip>("Audio/SFX/" + cue) != clip) throw new InvalidOperationException("Resource cue resolution differs " + cue);
                notes.Add(cue + "=" + clip.length.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (string name in new[] { "exploration", "combat" })
            {
                var clip = Resources.Load<AudioClip>("Audio/Music/" + name);
                if (clip == null || clip.length < 90) throw new InvalidOperationException("Music loop shorter than 90 seconds or missing: " + name);
                notes.Add(name + "=" + clip.length.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (Resources.Load<AudioClip>("Audio/Ambience/room-air") == null) throw new InvalidOperationException("Missing actual ambience clip.");
            Directory.CreateDirectory("Reports");
            File.WriteAllLines("Reports/P3_AUDIO_ENGINE_VALIDATION.txt", notes);
            Debug.Log("P3_AUDIO_ASSETS_VALIDATED: five actual Mixer groups, all configured cues, two loops and ambience. Human listening remains separate.");
        }
    }
}
