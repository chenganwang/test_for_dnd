using System;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V14Rules
    {
        public string version="v14-dual-builds-1";
        public PartyCombatMode partyCombatMode=PartyCombatMode.CoreAndAssist;
        public string finnGrowthAction="v14_finn_detonate";
        public double playerDamageScale=1.10, finnPeriodicScale=2.0;
    }
    [Serializable] public sealed class V14CombatState
    {
        public string version="v14-dual-builds-1";
        public PartyCombatMode partyCombatMode=PartyCombatMode.CoreAndAssist;
    }
    public sealed partial class CombatConfig { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14Rules v14; }
    public sealed partial class CombatState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14CombatState v14; }
}
