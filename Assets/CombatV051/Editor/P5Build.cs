using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CombatV051.Editor
{
    /// <summary>Build the existing scene. Never invokes a generator or changes project configuration.</summary>
    public static class P5Build
    {
        const string Scene = "Assets/CombatV051/Generated/CombatP0.unity";
        const string SceneGuid = "9e611541f8fd69549be638e227e377b7";
        public static void Run()
        {
            string output = null;
            try
            {
                string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var args = Environment.GetCommandLineArgs(); int argument = Array.IndexOf(args, "-p5-output");
                if (argument < 0 || argument + 1 >= args.Length) throw new InvalidOperationException("Explicit fresh -p5-output required.");
                output = Path.GetFullPath(args[argument + 1]);
                if (!output.StartsWith(Path.Combine(project, "Builds") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Directory.Exists(output))
                    throw new InvalidOperationException("Build output must be new and inside this project's Builds.");
                if (!File.Exists(Path.Combine(project, Scene)) || AssetDatabase.AssetPathToGUID(Scene) != SceneGuid)
                    throw new InvalidOperationException("Existing entry scene/GUID differs. No scene was created or replaced.");
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                    throw new InvalidOperationException("Windows build support unavailable.");
                if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone) != ScriptingImplementation.Mono2x)
                    throw new InvalidOperationException("Expected the existing Mono setting; this build does not rewrite settings.");
                var before = HashInputs(project);
                Directory.CreateDirectory(output);
                WriteHashes(Path.Combine(output, "P5_BUILD_SOURCE_SHA256.txt"), before);
                bool development = Array.IndexOf(args, "-p5-development") >= 0;
                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene },
                    locationPathName = Path.Combine(output, "ActionCRPG.exe"), target = BuildTarget.StandaloneWindows64,
                    options = development ? BuildOptions.Development : BuildOptions.None });
                var after = HashInputs(project);
                var changed = before.Keys.Where(path => !after.ContainsKey(path) || after[path] != before[path]).ToArray();
                File.WriteAllText(Path.Combine(output, "P5_BUILD_SOURCE.txt"),
                    "Actual engine: " + Application.unityVersion + "\nSource project: " + project + "\nScene: " + Scene + "\nScene GUID: " + SceneGuid +
                    "\nResult: " + result.summary.result + "\nErrors: " + result.summary.totalErrors + "\nWarnings: " + result.summary.totalWarnings +
                    "\nBuild options: " + (development ? "Development" : "Release") + "\nExisting input changes: " + changed.Length +
                    "\n" + string.Join("\n", changed) + "\nNo generator invoked. No human playtest implied.\n");
                WriteHashes(Path.Combine(output, "P5_BUILD_SOURCE_AFTER_SHA256.txt"), after);
                EditorApplication.Exit(result.summary.result == BuildResult.Succeeded && changed.Length == 0 ? 0 : 1);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        static SortedDictionary<string, string> HashInputs(string project)
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            using (var digest = SHA256.Create())
                foreach (string directory in new[] { "Assets", "Packages", "ProjectSettings" })
                    foreach (string path in Directory.GetFiles(Path.Combine(project, directory), "*", SearchOption.AllDirectories))
                        hashes[path.Substring(project.Length + 1).Replace('\\', '/')] = BitConverter.ToString(digest.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            return hashes;
        }
        static void WriteHashes(string path, SortedDictionary<string, string> hashes)
        {
            var text = new StringBuilder();
            foreach (var pair in hashes) text.Append(pair.Value).Append("  ").AppendLine(pair.Key);
            File.WriteAllText(path, text.ToString());
        }
    }
}
