using System.Security.Cryptography;
using CombatV051.Domain;
using Newtonsoft.Json;

// Uses production factories and actual departure transactions; no Player evidence is claimed.
static class V14ContentExporter
{
    public static void Run(string destination,string project)
    {
        destination=Path.GetFullPath(destination);project=Path.GetFullPath(project);
        if(Directory.Exists(destination))throw new InvalidOperationException("Preserve previous export");
        Directory.CreateDirectory(destination);
        void Write(string name,object data)=>File.WriteAllText(Path.Combine(destination,name),JsonConvert.SerializeObject(data,Formatting.Indented,CombatJson.Settings));
        Write("V14Rules.json",new V14Rules());Write("CoreCards.json",V11BuffCatalog.All.Where(x=>x.id.StartsWith("v14_")));
        var rows=new List<object>();
        foreach(var core in new[]{CombatSession.V12Evan,CombatSession.V12Mira,CombatSession.V12Finn,P6CharacterCatalog.PaladinId})
        foreach(var mode in new[]{PartyCombatMode.PersistentTrio,PartyCombatMode.CoreAndAssist})
        foreach(var slot in core==CombatSession.V12Finn?new[]{"v14_finn_detonate","v12_finn_grove","guard"}:new[]{"v14_finn_detonate"})
        {
            var legacy=new CombatConfig();var p5=CombatJson.Clone(legacy);p5.p5=new P5Config();var p6=CombatJson.Clone(p5);p6.p6=new P6Config();
            var profile=ProfileSession.CreateNewCompatible("v14-content",181,p6,p5,legacy,new DungeonConfig(),null);
            void Check(ProfileResult r){if(!r.success)throw new InvalidOperationException(r.reason);}
            Check(profile.EnsureV11Progress("progress"));profile.EnableV14Departures();
            Check(profile.SaveV11Preparation(new V11PreparationDraft{teamIds=core==P6CharacterCatalog.PaladinId?new List<string>{core,"mage",CombatSession.V12Finn}:new List<string>{CombatSession.V12Evan,CombatSession.V12Mira,CombatSession.V12Finn},coreId=core,defaultActiveActorId=core,partyCombatMode=mode,finnTree=slot=="guard"?"guard":"growth",finnGrowthAction=slot=="guard"?"v14_finn_detonate":slot},"draft"));
            Check(profile.Execute(new ProfileCommand{id="depart",kind=ProfileCommandKind.StartDemoExploration}));
            var config=profile.ActiveCombat.Config;
            rows.Add(new{core,mode=mode.ToString(),slot,departure=profile.ActiveMode.v11Departure,config.v14,config.v12,config.v11,guaranteed=new[]{"V12_R1","V12_R2","V12_R4","V12_R5"}.Select(room=>new{room,card=V14BuildCatalog.Guaranteed(config.v11,room)}),available=V11BuffCatalog.Eligible(config.v11).Select(x=>x.id)});
        }
        Write("ActualDepartureMappings.json",rows);
        var source=Directory.GetFiles(Path.Combine(project,"Assets/CombatV051/Domain"),"V14*.cs").Concat(new[]{"V11Profile.cs","V11Catalogs.cs","V12Actions.cs","V12Effects.cs","V12ProfileRewards.cs"}.Select(x=>Path.Combine(project,"Assets/CombatV051/Domain",x)));
        Write("V14_CONTENT_PROVENANCE.json",new{classification="COMPILED_PRODUCTION_DEPARTURES_NOT_PLAYTEST",files=source.Select(p=>new{path=Path.GetRelativePath(project,p).Replace('\\','/'),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()})});
        Console.WriteLine("Exported actual V14 definitions: "+destination);
    }
}
