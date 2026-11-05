using System;
using System.IO;
using System.Linq;
using System.Text;
using CombatV051.Domain;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace CombatV051.Editor
{
    // Read-only oracle verification of an actual earlier Player profile, in the same
    // Unity Mono runtime. .NET 8 formats some floats differently from this engine.
    public static class V12JsonCompatibilityCheck
    {
        public static void Run()
        {
            int exit=1;
            try
            {
                var args=Environment.GetCommandLineArgs();
                string Arg(string name){int i=Array.IndexOf(args,name);if(i<0||i+1>=args.Length)throw new ArgumentException(name);return Path.GetFullPath(args[i+1]);}
                string input=Arg("-v12-json-profile"),output=Arg("-v12-json-report");
                if(File.Exists(output))throw new IOException("Preserve existing evidence");
                byte[] original=File.ReadAllBytes(input);
                var envelope=CombatJson.Read<ProfileEnvelope>(Encoding.UTF8.GetString(original).TrimStart('\uFEFF'));
                if(CombatStore.Digest(envelope.payload)!=envelope.integrity)throw new InvalidDataException("Original profile integrity mismatch");
                var state=CombatJson.Read<ProfileState>(envelope.payload);
                bool fast=CombatTypedJson.TryWrite(state,CombatJson.Settings,out var typed);
                string oracle=JsonConvert.SerializeObject(state,Formatting.None,CombatJson.Settings);
                bool exactOracle=fast&&typed==oracle,exactOriginal=fast&&typed==envelope.payload;
                bool unchanged=original.SequenceEqual(File.ReadAllBytes(input));
                var result=new{classification="ACTUAL_UNITY_EDITOR_READ_ONLY_LEGACY_JSON_NOT_PLAYER",engine=Application.unityVersion,
                    utc=DateTime.UtcNow.ToString("o"),input,fastPath=fast,exactOracleBytes=exactOracle,exactOriginalPayloadBytes=exactOriginal,
                    originalUnchanged=unchanged,originalIntegrity=envelope.integrity,payloadBytes=Encoding.UTF8.GetByteCount(envelope.payload)};
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write))
                using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(JsonConvert.SerializeObject(result,Formatting.Indented));
                if(!fast||!exactOracle||!exactOriginal||!unchanged)throw new InvalidDataException("Legacy byte compatibility failed; evidence retained");
                Debug.Log("V12 actual prior Player profile JSON matches original bytes and Newtonsoft oracle.");exit=0;
            }
            catch(Exception error){Debug.LogException(error);}
            EditorApplication.Exit(exit);
        }
    }
}
