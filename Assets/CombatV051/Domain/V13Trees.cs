using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public static class V13Trees
    {
        public static V11NodeDefinition Find(string id)=>Extra().FirstOrDefault(n=>n.id==id);
        public static List<V11NodeDefinition> Extra()
        {
            var nodes=new List<V11NodeDefinition>();
            nodes.Add(N("guard/active1","护生","零指挥；6%生命护盾6秒，共享10秒CD。", "Active","v13_finn_shield",0));
            nodes.Add(N("guard/active2","回春祈愿","1指挥＋1祈愿；回复16%生命并解缠绕。每局2次，不能复活。", "Active","v13_finn_prayer",0));
            nodes.Add(N("growth/upgrade1","长芽","寄生持续6→7秒。", "Upgrade","v12_finn_seed",1,"char_story_finn/active1"));
            nodes.Add(N("growth/upgrade2","繁息","森息半径2.5→2.8米。", "Upgrade","v12_finn_grove",1,"char_story_finn/active2"));
            nodes.Add(N("guard/upgrade1","厚生","护生护盾6%→8%，不增加次数。", "Upgrade","v13_finn_shield",1,"v13/finn/guard/active1"));
            nodes.Add(N("guard/upgrade2","深愿","回春治疗16%→20%，祈愿仍为2次。", "Upgrade","v13_finn_prayer",1,"v13/finn/guard/active2"));
            return nodes;
        }
        static V11NodeDefinition N(string suffix,string name,string detail,string kind,string ability,int cost,string prerequisite=null)
        {return new V11NodeDefinition{id="v13/finn/"+suffix,characterId=CombatSession.V12Finn,displayName=name,description=detail,kind=kind,abilityId=ability,cost=cost,initialUnlocked=cost==0,prerequisiteIds=prerequisite==null?new List<string>():new List<string>{prerequisite}};}
        public static IReadOnlyList<V11NodeDefinition> For(string character,string tree)
        {
            if(character!=CombatSession.V12Finn)return V11Catalog.NodesFor(character);
            var old=V11Catalog.NodesFor(character);var result=new List<V11NodeDefinition>{old.First(n=>n.kind=="Passive")};result[0].displayName="生命回响";
            if(tree=="guard")result.AddRange(Extra().Where(n=>n.id.Contains("/guard/")&&n.kind=="Active"));else result.AddRange(old.Where(n=>n.kind=="Active"));
            result.AddRange(Extra().Where(n=>n.id.Contains(tree=="guard"?"/guard/":"/growth/")&&n.kind=="Upgrade"));return result;
        }
    }
}
