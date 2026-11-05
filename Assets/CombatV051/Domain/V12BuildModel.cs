using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V12BuildConfig
    {
        public string version="v12-build-1";
        public float coauthorRange=3,coauthorMultiplier=.6f,coauthorUpgradeMultiplier=.85f;
        public float conclusionMultiplier=1.3f,conclusionUpgradeMultiplier=1.45f;
        public float sweepRange=2,sweepUpgradeRange=2.8f,sweepAngle=90,sweepMultiplier=.6f,sweepUpgradeMultiplier=.75f;
        public int sweepTargets=2,spreadTargets=2;
        public float huntCooldownReduction=3,huntUpgradeRange=5.5f,spreadRange=3,spreadUpgradeRange=4.5f,bloomHealFraction=.02f;
        public float shieldFraction=.05f,shieldSeconds=6,dodgeCooldownMultiplier=.85f,minimumDodgeCooldown=.5f;
        public float focusCapacity=15,focusRecovery=15,edgeMomentum=25,healingMultiplier=1.15f,explorationFocus=2;
        public int explorationLimit=2;
    }
    [Serializable] public sealed class V12LineEvidence { public string fromId,toId;public bool lineOfSight; }
    [Serializable] public sealed class V12CommandData { public List<V12LineEvidence> links=new List<V12LineEvidence>(); }
    public sealed partial class CombatCommand { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12CommandData v12; }
}
