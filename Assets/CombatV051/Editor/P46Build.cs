using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace CombatV051.Editor
{
    public static class P46Build
    {
        public static void Run()
        {
            try
            {
                var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-p46-output");
                if(i<0||i+1>=args.Length)throw new Exception("Explicit isolated P46 output required");
                string path=Path.GetFullPath(args[i+1]);string root=Path.GetFullPath(Path.Combine(Application.dataPath,"../Builds"))+Path.DirectorySeparatorChar;
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||Directory.Exists(path))throw new Exception("Use a fresh build directory inside this project's Builds");
                CombatPrototypeBuilder.BuildPrototype();Directory.CreateDirectory(path);
                var manifest=new StringBuilder();
                string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
                using(var sha=SHA256.Create())
                    foreach(var input in new[]{"Assets","ProjectSettings","Packages"}.SelectMany(folder=>Directory.GetFiles(Path.Combine(project,folder),"*",SearchOption.AllDirectories)).OrderBy(f=>f,StringComparer.Ordinal))
                    {string hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(input))).Replace("-","").ToLowerInvariant();manifest.Append(hash).Append("  ").Append(input.Substring(project.Length+1).Replace('\\','/')).AppendLine();}
                File.WriteAllText(Path.Combine(path,"P46_BUILD_SOURCE_SHA256.txt"),manifest.ToString());
                var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/CombatV051/Generated/CombatP0.unity"},locationPathName=Path.Combine(path,"ActionCRPG.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
                File.WriteAllText(Path.Combine(path,"P46_BUILD_SOURCE.txt"),"Actual engine: "+Application.unityVersion+"\nSource project: "+Path.GetFullPath(Path.Combine(Application.dataPath,".."))+"\nResult: "+result.summary.result+"\nErrors: "+result.summary.totalErrors+"\nProfiler development build; not final release acceptance.\n");
                EditorApplication.Exit(result.summary.result==BuildResult.Succeeded?0:1);
            }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
        }
    }
}
