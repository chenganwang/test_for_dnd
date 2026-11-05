using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CombatV051.Domain;
using Newtonsoft.Json;

internal static class JsonFileVerifier
{
    internal static void Run(string input,string output)
    {
        input=Path.GetFullPath(input);output=Path.GetFullPath(output);
        if(File.Exists(output)||Directory.Exists(output))throw new IOException("Preserve prior verification output");
        byte[] original=File.ReadAllBytes(input);string inputHash=Convert.ToHexString(SHA256.HashData(original));
        var envelope=CombatJson.Read<ProfileEnvelope>(Encoding.UTF8.GetString(original).TrimStart('\uFEFF'));
        if(CombatStore.Digest(envelope.payload)!=envelope.integrity)throw new InvalidDataException("Input profile integrity mismatch");
        var profile=CombatJson.Read<ProfileState>(envelope.payload);
        bool fast=CombatTypedJson.TryWrite(profile,CombatJson.Settings,out string typed);
        string oracle=JsonConvert.SerializeObject(profile,Formatting.None,CombatJson.Settings);
        bool exactOracle=fast&&typed==oracle,exactHistorical=fast&&typed==envelope.payload;
        int difference=0;while(difference<Math.Min(typed?.Length??0,envelope.payload.Length)&&typed[difference]==envelope.payload[difference])difference++;
        string Slice(string value)=>value==null?null:value.Substring(Math.Max(0,difference-60),Math.Min(180,value.Length-Math.Max(0,difference-60)));
        double Measure(Action action){for(int i=0;i<3;i++)action();var clock=Stopwatch.StartNew();for(int i=0;i<20;i++)action();return clock.Elapsed.TotalMilliseconds/20;}
        double typedMs=Measure(()=>CombatTypedJson.TryWrite(profile,CombatJson.Settings,out _));
        double oracleMs=Measure(()=>JsonConvert.SerializeObject(profile,Formatting.None,CombatJson.Settings));
        bool unchanged=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)))==inputHash;
        var report=new{classification="READ_ONLY_ACTUAL_PROFILE_JSON_ORACLE_CHECK_DOTNET_NOT_UNITY",utc=DateTime.UtcNow.ToString("o"),input,inputHash,
            fastPath=fast,exactOracleBytes=exactOracle,exactHistoricalPayloadBytes=exactHistorical,originalUnchanged=unchanged,
            payloadBytes=Encoding.UTF8.GetByteCount(envelope.payload),firstHistoricalDifference=exactHistorical?-1:difference,historicalContext=exactHistorical?null:Slice(envelope.payload),currentContext=exactHistorical?null:Slice(typed),typedMeanMs=typedMs,oracleMeanMs=oracleMs,iterations=20,
            performanceScope="Micro timings only, not Player FPS. No save write, restore, gameplay mutation, or dependency download."};
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write))
        using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(JsonConvert.SerializeObject(report,Formatting.Indented));
        Console.WriteLine(JsonConvert.SerializeObject(report,Formatting.Indented));
        if(!fast||!exactOracle||!exactHistorical||!unchanged)throw new InvalidDataException("JSON compatibility failed; evidence retained");
    }
}
