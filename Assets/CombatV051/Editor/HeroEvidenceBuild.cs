using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CombatV051.Editor
{
    /// <summary>Explicit one-shot authored-asset bake. Never loads or saves a gameplay scene.</summary>
    public static class HeroEvidenceBuild
    {
        [Serializable] sealed class Result
        {
            public string classification="ACTUAL_EDITOR_HERO_BAKE_AND_ISOLATED_ASSET_PREVIEW_NOT_PLAYER",engine,utc;
            public bool succeeded;public int prefabCount,previewCount;public string[] prefabs;
            public int humanPlayers,humanRuns;public bool combatValidated;
        }
        public static void BakeAndPreview()
        {
            try
            {
                if(Application.unityVersion!="2022.3.62f3c1")throw new InvalidOperationException("The recorded compatibility editor is required.");
                const string heroes="Assets/CombatV051/Resources/Heroes",manifest="Reports/MAIN_CHARACTER_BAKE.json",previews="Reports/MAIN_CHARACTER_PREVIEWS";
                if(Directory.Exists(heroes)||File.Exists(heroes+".meta")||File.Exists(manifest)||Directory.Exists(previews))
                    throw new InvalidOperationException("Existing hero assets/evidence are protected; this one-shot bake never overwrites an earlier bake.");
                var args=Environment.GetCommandLineArgs();int argument=Array.IndexOf(args,"-p6-hero-output");
                if(argument<0||argument+1>=args.Length)throw new InvalidOperationException("Explicit fresh -p6-hero-output is required.");
                string output=Path.GetFullPath(args[argument+1]);
                if(Directory.Exists(output)||File.Exists(output))throw new InvalidOperationException("Evidence destination already exists.");
                string project=Path.GetFullPath(Path.Combine(Application.dataPath,".."));
                if(output.StartsWith(Path.Combine(project,"Assets")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Evidence may not be written into Assets.");
                HeroModelBuilder.Bake();
                HeroModelBuilder.CapturePreview();
                AssetDatabase.SaveAssets();AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string[] names={"Evan","Mira","Finn"};
                foreach(string name in names)
                {
                    string prefab=heroes+"/"+name+".prefab";
                    if(!AssetDatabase.LoadAssetAtPath<GameObject>(prefab)||!File.Exists(prefab+".meta")||!File.Exists(previews+"/"+name+".png"))
                        throw new InvalidOperationException("Bake did not produce the expected Prefab/GUID/preview: "+name);
                }
                if(!File.Exists(manifest))throw new InvalidOperationException("Actual bake manifest is missing.");
                Directory.CreateDirectory(output);
                File.Copy(manifest,Path.Combine(output,"MAIN_CHARACTER_BAKE.json"),false);
                foreach(string name in names)File.Copy(previews+"/"+name+".png",Path.Combine(output,name+".png"),false);
                File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(new Result{engine=Application.unityVersion,utc=DateTime.UtcNow.ToString("o"),succeeded=true,prefabCount=3,previewCount=3,prefabs=names.Select(n=>heroes+"/"+n+".prefab").ToArray()},true));
                Debug.Log("P6_HERO_EVIDENCE complete. Isolated Editor asset renders only; combat, Player and human validation remain separate.");
                EditorApplication.Exit(0);
            }
            catch(Exception error){Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
