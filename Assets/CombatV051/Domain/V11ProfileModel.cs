using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V11PreparationDraft
    {
        public List<string> teamIds=new List<string>{P6CharacterCatalog.PaladinId,"mage","rogue"};
        public string coreId=P6CharacterCatalog.PaladinId,defaultActiveActorId=P6CharacterCatalog.PaladinId;
        // The departure already snapshots this preparation. No parallel mode field in combat/state.
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public PartyCombatMode partyCombatMode;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string finnTree;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string finnGrowthAction;
    }
    [Serializable] public sealed class V11ProgressState
    {
        public string version="v11-tree-1";
        public bool demoAllowanceClaimed;
        public int points;
        public List<string> unlockedNodeIds=new List<string>();
        public V11PreparationDraft preparation=new V11PreparationDraft();
    }
    [Serializable] public sealed class V11DepartureSnapshot
    {
        public string version="v11-departure-1",catalogHash,hash;
        public V11PreparationDraft preparation;
        public List<V11ActorLoadout> actors=new List<V11ActorLoadout>();
        // Only new V12 departures have these rules. Missing means the exact V11 snapshot.
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12CombatConfig v12;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V13Rules v13;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14Rules v14;
    }
    [Serializable] public sealed class V11BuildState
    {public List<V11BuffStack> buffs=new List<V11BuffStack>();}
    [Serializable] public sealed class V11NodeQuote
    {public string status,reason;public bool canUnlock;public int cost,remainingPoints;}
    [Serializable] public sealed class V11DepartureQuote
    {public bool legal;public string reason;}
    public sealed partial class ProfileMeta
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11ProgressState v11;}
    public sealed partial class ModeState
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11DepartureSnapshot v11Departure;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11BuildState v11Build;
    }
}
