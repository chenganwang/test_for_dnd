using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    public sealed partial class V11CombatRules
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string v14BuildVersion;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string v14FinnGrowthAction;
    }
    [Serializable] public sealed class V14CoreEncounter
    {
        public string encounterId;
        public bool freeCorrectionUsed;
        public List<string> settlementRoots=new List<string>(),dodgeRoots=new List<string>();
    }
    [Serializable] public sealed class V14CoreState
    {
        public List<V14CoreEncounter> encounters=new List<V14CoreEncounter>();
        public string dodgeEncounter,dodgeRoot,preparedCorrectionEncounter,preparedCorrectionPayment,lastChaseEncounter;
        public float dodgeUntil,lastChaseRefund=-4;
        public List<string> playerHeroRoots=new List<string>();
    }
    [Serializable] public sealed class V14CorrectionRecord
    {
        public string sourceId,encounterId,paymentId,method;
        public List<int> originalFaces=new List<int>();
        public int originalTotal;
        public bool beforeRecorded,beforeSuccess,afterSuccess,actualEffect,settled;
    }
    public sealed partial class ActorState
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14CoreState v14Core; }
    public sealed partial class RollTicket
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14CorrectionRecord v14Correction; }
    public sealed partial class BasicAttackIntent
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public bool v14PlayerCommand; }
    public sealed partial class CombatCommand
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string v14CorrectionChoice; }
    public sealed class V14CorrectionQuote
    { public bool legal,free,canReroll;public string reason; }
}
