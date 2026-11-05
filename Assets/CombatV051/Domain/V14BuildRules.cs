using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public static class V14BuildCatalog
    {
        public const string Version="v14-core-builds-1";
        public static bool Enabled(V11CombatRules rules)=>rules?.v14BuildVersion==Version;
        public static bool Available(V11CombatRules rules,string id)
        {
            if(!Enabled(rules))return false;
            var d=V11BuffCatalog.Find(id);if(d==null||d.coreActorId!=rules.coreActorId)return false;
            var core=rules.actors.Find(a=>a.actorId==rules.coreActorId);if(core==null)return false;
            if(id.StartsWith("v14_evan_",StringComparison.Ordinal))return core.unlockedActionIds.Contains("v13_evan_correct");
            if(id.StartsWith("v14_mira_",StringComparison.Ordinal))return core.unlockedActionIds.Contains("v12_mira_chase")&&core.unlockedActionIds.Contains("v12_mira_break");
            if(id.StartsWith("v14_finn_",StringComparison.Ordinal))return rules.v13FinnTree=="growth"&&core.unlockedActionIds.Contains("v12_finn_seed")&&
                (id!="v14_finn_bloom"||rules.v14FinnGrowthAction=="v14_finn_detonate"&&core.unlockedActionIds.Contains("v14_finn_detonate"));
            return false;
        }
        public static string Guaranteed(V11CombatRules rules,string roomTemplate)
        {
            if(!Enabled(rules)||roomTemplate=="V12_R2")return null;
            int stage=roomTemplate=="V12_R1"?0:roomTemplate=="V12_R4"?1:roomTemplate=="V12_R5"?2:-1;
            if(stage<0)return null;
            string[] ids=rules.coreActorId==CombatSession.V12Evan?new[]{"v14_evan_firstproof","v14_evan_review","v14_evan_settle"}:
                rules.coreActorId==CombatSession.V12Mira?new[]{"v14_mira_opening","v14_mira_breakflow","v14_mira_press"}:
                rules.coreActorId==CombatSession.V12Finn&&rules.v13FinnTree=="growth"?new[]{"v14_finn_quickgrowth","v14_finn_densegrowth","v14_finn_bloom"}:null;
            string id=ids?[stage];return id!=null&&Available(rules,id)?id:null;
        }
        public static bool UsesNewRoomOffers(V11CombatRules rules)=>Enabled(rules)&&rules.coreActorId!=P6CharacterCatalog.PaladinId&&
            !(rules.coreActorId==CombatSession.V12Finn&&rules.v13FinnTree=="guard");
    }
    public sealed partial class ProfileSession
    {
        static List<string> V14RewardCandidates(ModeState mode,RoomState room,V11CombatRules rules)
        {
            Require(new[]{"V12_R1","V12_R2","V12_R4","V12_R5"}.Contains(room.templateId),"v14_reward_room_invalid");
            var generic=V11BuffCatalog.Eligible(rules).Where(d=>d.pool=="Generic").Select(d=>d.id).ToList();ShuffleRewardPool(mode,generic);
            string guaranteed=V14BuildCatalog.Guaranteed(rules,room.templateId);var selected=new List<string>();if(guaranteed!=null&&V11BuffCatalog.Eligible(rules).Any(d=>d.id==guaranteed))selected.Add(guaranteed);
            selected.AddRange(generic.Take(3-selected.Count));return selected;
        }
    }
}
