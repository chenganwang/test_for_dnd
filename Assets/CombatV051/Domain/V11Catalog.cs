using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V11CharacterDefinition
    {
        public string id,displayName,category,silhouetteId,passiveId,weaponId;
        public ActorRole role;
        public bool coreSupported;
        [Newtonsoft.Json.JsonProperty(DefaultValueHandling=Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public V11FinnSustainConfig finnSustain;
        public List<string> actionIds=new List<string>();
    }
    [Serializable] public sealed class V11NodeDefinition
    {
        public string id,characterId,displayName,description,kind,abilityId;
        public List<string> prerequisiteIds=new List<string>();
        public int cost;
        public bool initialUnlocked;
    }
    [Serializable] public sealed class V11CatalogData
    {
        public string version="v11-catalog-1";
        public int demoAllowance=6;
        public List<V11CharacterDefinition> characters=new List<V11CharacterDefinition>();
        public List<V11NodeDefinition> nodes=new List<V11NodeDefinition>();
    }
    public static class V11Catalog
    {
        static readonly V11CatalogData Data=Load(V11CatalogJson.Text);
        // A versioned input snapshot in the same catalog, not another registry/service.
        // Existing departures validate against the definitions they actually bought.
        static readonly V11CatalogData LegacyData=Load(V11LegacyCatalogJson.Text);
        public static string SourceJson {get{return V11CatalogJson.Text;}}
        public static string Hash {get{return CombatStore.Digest(V11CatalogJson.Text);}}
        public static int DemoAllowance {get{return Data.demoAllowance;}}
        public static IReadOnlyList<V11CharacterDefinition> Characters {get{return CombatJson.Clone(Data.characters);}}
        public static V11CharacterDefinition FindCharacter(string id){return CombatJson.Clone(Data.characters.Find(c=>c.id==id));}
        public static V11CharacterDefinition FindCharacterForRules(string id,string rulesVersion)
        {return CombatJson.Clone((rulesVersion=="v11-character-1"?LegacyData:Data).characters.Find(c=>c.id==id));}
        public static bool SupportsCoreForRules(string id,string rulesVersion)
        {return (rulesVersion=="v11-character-1"?LegacyData:Data).characters.Any(c=>c.id==id&&c.coreSupported);}
        public static string LegacyHash=>CombatStore.Digest(V11LegacyCatalogJson.Text);
        public static bool RoleMatches(string id,ActorRole role)
        {for(int i=0;i<Data.characters.Count;i++)if(Data.characters[i].id==id)return Data.characters[i].role==role;return false;}
        public static bool CharacterExists(string id)
        {for(int i=0;i<Data.characters.Count;i++)if(Data.characters[i].id==id)return true;return false;}
        public static bool SupportsCore(string id)
        {for(int i=0;i<Data.characters.Count;i++)if(Data.characters[i].id==id)return Data.characters[i].coreSupported;return false;}
        public static string SavedPrerequisiteIssue(List<string> unlocked)
        {
            for(int i=0;i<Data.nodes.Count;i++)
            {var node=Data.nodes[i];if(!unlocked.Contains(node.id))continue;for(int j=0;j<node.prerequisiteIds.Count;j++)if(!unlocked.Contains(node.prerequisiteIds[j]))return node.id;}
            foreach(var n in V13Trees.Extra())if(unlocked.Contains(n.id)&&n.prerequisiteIds.Any(id=>!unlocked.Contains(id)))return n.id;
            return null;
        }
        public static IReadOnlyList<V11NodeDefinition> NodesFor(string characterId){return CombatJson.Clone(Data.nodes.Where(n=>n.characterId==characterId).ToList());}
        public static V11NodeDefinition FindNode(string id){return CombatJson.Clone(Data.nodes.Find(n=>n.id==id)??V13Trees.Find(id));}
        public static V11CatalogData Load(string json)
        {var value=CombatJson.Read<V11CatalogData>(json);Validate(value);return value;}
        public static void Validate(V11CatalogData value)
        {
            if(value==null||(value.version!="v11-catalog-1"&&value.version!="v12-catalog-1")||value.characters==null||value.nodes==null||value.demoAllowance<0)throw new InvalidOperationException("v11_catalog_invalid");
            if(value.characters.Count!=7||value.characters.Any(c=>c==null||string.IsNullOrWhiteSpace(c.id))||value.characters.Select(c=>c.id).Distinct().Count()!=7)throw new InvalidOperationException("v11_catalog_character_ids");
            if(value.characters.Count(c=>c.category=="Main")!=3||value.characters.Count(c=>c.category=="Companion")!=4)throw new InvalidOperationException("v11_catalog_categories");
            if(value.nodes.Any(n=>n==null||string.IsNullOrWhiteSpace(n.id))||value.nodes.Select(n=>n.id).Distinct().Count()!=value.nodes.Count)throw new InvalidOperationException("v11_catalog_node_ids");
            foreach(var c in value.characters)
            {
                if(c.role==ActorRole.Enemy||string.IsNullOrWhiteSpace(c.displayName)||string.IsNullOrWhiteSpace(c.silhouetteId)||string.IsNullOrWhiteSpace(c.weaponId)||c.actionIds==null||c.actionIds.Count!=2||c.actionIds.Distinct().Count()!=2)throw new InvalidOperationException("v11_character_invalid:"+c.id);
                var nodes=value.nodes.Where(n=>n.characterId==c.id).ToList();
                if(nodes.Count!=3||nodes.Count(n=>n.kind=="Passive"&&n.abilityId==c.passiveId)!=1||nodes.Count(n=>n.kind=="Active")!=2||!nodes.Where(n=>n.kind=="Active").Select(n=>n.abilityId).OrderBy(x=>x).SequenceEqual(c.actionIds.OrderBy(x=>x)))throw new InvalidOperationException("v11_character_suite_invalid:"+c.id);
            }
            foreach(var n in value.nodes)
            {
                if(!value.characters.Any(c=>c.id==n.characterId)||n.cost<0||n.prerequisiteIds==null||n.prerequisiteIds.Distinct().Count()!=n.prerequisiteIds.Count)throw new InvalidOperationException("v11_node_invalid:"+n.id);
                foreach(var id in n.prerequisiteIds)
                {var prerequisite=value.nodes.Find(p=>p.id==id);if(prerequisite==null)throw new InvalidOperationException("v11_node_missing_prerequisite:"+n.id+":"+id);if(prerequisite.characterId!=n.characterId)throw new InvalidOperationException("v11_node_cross_character:"+n.id+":"+id);}
                Visit(value,n,new HashSet<string>(),new HashSet<string>());
                if(n.initialUnlocked&&n.prerequisiteIds.Any(id=>!value.nodes.Find(p=>p.id==id).initialUnlocked))throw new InvalidOperationException("v11_initial_prerequisite_locked:"+n.id);
            }
        }
        static void Visit(V11CatalogData data,V11NodeDefinition node,HashSet<string> active,HashSet<string> visited)
        {if(visited.Contains(node.id))return;if(!active.Add(node.id))throw new InvalidOperationException("v11_node_cycle:"+node.id);foreach(var id in node.prerequisiteIds){var next=data.nodes.Find(n=>n.id==id);if(next==null)throw new InvalidOperationException("v11_node_missing_prerequisite:"+node.id+":"+id);Visit(data,next,active,visited);}active.Remove(node.id);visited.Add(node.id);}
    }
}
