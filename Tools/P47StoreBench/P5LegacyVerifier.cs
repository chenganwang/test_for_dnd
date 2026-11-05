using System.Security.Cryptography;
using CombatV051.Domain;
using Newtonsoft.Json;

internal static class P5LegacyVerifier
{
    internal static void Run(string root,string output,IEnumerable<string> sources)
    {
        root=Path.GetFullPath(root);
        var legacy=CombatJson.Read<CombatConfig>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/CombatConfig.json")));
        var p5=CombatJson.Clone(legacy);p5.p5=new P5Config();
        var dungeon=CombatJson.Read<DungeonConfig>(File.ReadAllText(Path.Combine(root,"Assets/CombatV051/Resources/DungeonConfig.json")));
        var rows=new List<object>();bool all=true;
        foreach(string relative in sources)
        {
            // Caller supplies explicit sealed evidence files, never default player paths.
            string source=Path.GetFullPath(Path.Combine(root,relative));
            string allowed=Path.GetFullPath(Path.Combine(root,"Reports"))+Path.DirectorySeparatorChar;
            if(!source.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Only explicit Reports fixtures are permitted.");
            byte[] original=File.ReadAllBytes(source);string sourceHash=Convert.ToHexString(SHA256.HashData(original));
            string directory=Path.Combine(Path.GetTempPath(),"ActionCrpgP5Legacy",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);string path=Path.Combine(directory,"profile.json");
            try
            {
                File.WriteAllBytes(path,original);var envelope=CombatJson.Read<ProfileEnvelope>(System.Text.Encoding.UTF8.GetString(original));
                var loaded=ProfileSession.LoadCompatible(path,p5,legacy,dungeon);
                string encoded=CombatJson.Write(loaded.State);
                bool jsonExact=encoded==envelope.payload;
                string oracle=JsonConvert.SerializeObject(CombatJson.Read<ProfileState>(envelope.payload),Formatting.None,CombatJson.Settings);
                bool currentReaderOracleExact=encoded==oracle;
                int firstDifference=0;while(firstDifference<Math.Min(encoded.Length,envelope.payload.Length)&&encoded[firstDifference]==envelope.payload[firstDifference])firstDifference++;
                string oldExcerpt=envelope.payload.Substring(Math.Max(0,firstDifference-50),Math.Min(200,envelope.payload.Length-Math.Max(0,firstDifference-50)));
                string newExcerpt=encoded.Substring(Math.Max(0,firstDifference-50),Math.Min(200,encoded.Length-Math.Max(0,firstDifference-50)));
                bool fileExact=File.ReadAllBytes(path).SequenceEqual(original);
                bool sourceExact=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))==sourceHash;
                bool pass=currentReaderOracleExact&&fileExact&&sourceExact&&!loaded.ActiveUsesP5&&loaded.State.baseCombatConfigHash==envelopePayloadHash(envelope);
                rows.Add(new {path=relative,sourceSha256=sourceHash,passed=pass,jsonExact,currentReaderOracleExact,firstDifference,oldExcerpt,newExcerpt,isolatedFileUnchanged=fileExact,sealedSourceUnchanged=sourceExact,revision=loaded.State.revision,baseConfigHash=loaded.State.baseCombatConfigHash,activeMode=loaded.State.activeMode.ToString(),activeConfigLegacy=!loaded.ActiveUsesP5,pendingTicketId=loaded.ActiveCombat.State.pendingRollId});all&=pass;
            }
            catch(Exception ex){all=false;rows.Add(new {path=relative,sourceSha256=sourceHash,passed=false,error=ex.ToString()});}
            finally{Directory.Delete(directory,true);}
        }
        var report=new{classification="DOTNET_LEGACY_FIXTURE_COMPATIBILITY_NOT_UNITY_PLAYER_OR_HUMAN",utc=DateTime.UtcNow.ToString("o"),passed=all,ordinarySavesRead=false,fixtureCopiesOnly=true,comparison="Read-only file bytes must remain exact. Loaded DTO must equal direct original Newtonsoft after the same read. Raw reencoded payload text is separately reported: Mono and .NET may format identical float32 values differently; raw text is not claimed equal.",legacyResourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"Assets/CombatV051/Resources/CombatConfig.json")))) ,results=rows};
        File.WriteAllText(output,JsonConvert.SerializeObject(report,Formatting.Indented));Console.WriteLine(JsonConvert.SerializeObject(report,Formatting.Indented));if(!all)Environment.ExitCode=2;
    }
    static string envelopePayloadHash(ProfileEnvelope e)=>CombatJson.Read<ProfileState>(e.payload).baseCombatConfigHash;
}
