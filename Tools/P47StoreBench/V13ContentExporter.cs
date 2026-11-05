using System.Security.Cryptography;
using CombatV051.Domain;
using Newtonsoft.Json;

// Exports compiled production definitions. Does not launch the game or mutate saves.
static class V13ContentExporter
{
    public static void Run(string destination,string project)
    {
        destination=Path.GetFullPath(destination);project=Path.GetFullPath(project);
        if(Directory.Exists(destination))throw new InvalidOperationException("Preserve previous content export");
        Directory.CreateDirectory(destination);
        var table=Enumerable.Range(1,100).GroupBy(CombatSession.FateIndex).Select(g=>new {index=g.Key,min=g.Min(),max=g.Max(),name=CombatSession.FateName(g.Min()),effect=CombatSession.FateDetail(g.Min())}).ToArray();
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(destination,name),JsonConvert.SerializeObject(value,Formatting.Indented,CombatJson.Settings));
        Write("V13Rules.json",new V13Rules());Write("D100Table.json",table);
        Write("FinnTrees.json",new{growth=V13Trees.For(CombatSession.V12Finn,"growth"),guard=V13Trees.For(CombatSession.V12Finn,"guard")});
        Write("ActorSkillMapping.json",new{coreMode="CoreAndAssist",evan=new[]{"v12_evan_mark","v13_evan_correct"},mira=new[]{"v12_mira_chase","v12_mira_break"},finnGrowth=new[]{"v12_finn_seed","v12_finn_grove"},finnGuard=new[]{"v13_finn_shield","v13_finn_prayer"},legacyEvanSecond="v12_evan_final",mainDiceLawSlotConsumed=false});
        Write("BuildRewards.json",V11BuffCatalog.All);
        Write("NewRunRewardPools.json",new[]{CombatSession.V12Evan,CombatSession.V12Mira,CombatSession.V12Finn,P6CharacterCatalog.PaladinId}.SelectMany(core=>new[]{"growth","guard"}.Select(tree=>new{core,tree,ids=V11BuffCatalog.All.Where(d=>V11BuffCatalog.AvailableInRules(new V11CombatRules{version="v12-character-1",v12BuildVersion="v12-build-1",coreActorId=core,v13FinnTree=tree},d.id)).Select(d=>d.id)})));
        var files=new[]{"Domain/V13Model.cs","Domain/V13Fate.cs","Domain/V13Rules.cs","Domain/V13Boss.cs","Domain/V13Trees.cs","Domain/ProfileSession.cs","Domain/V11Catalogs.cs","Domain/V12Actions.cs","Runtime/BattleWorld.V13Boss.cs"};
        Write("V13_CONTENT_PROVENANCE.json",new{classification="COMPILED_PRODUCTION_DEFINITIONS_NOT_PLAYTEST",runtimeAuthority="UnityProject/Assets/CombatV051/Domain and Runtime",files=files.Select(name=>new{path="Assets/CombatV051/"+name,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(project,"Assets/CombatV051",name)))).ToLowerInvariant()})});
        Console.WriteLine("Exported production V13 definitions to "+destination);
    }
}
