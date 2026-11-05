using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V12CombatConfig
    {
        public string version="v12-heroes-1";
        public float markSeconds=6,markMultiplier=1.2f,markRange=7,markWindup=.35f,markCooldown=8;
        public float echoCoefficient=.45f,echoInterval=2.5f,finalCoefficient=2.5f,finalRange=7,finalWindup=.4f;
        public float chaseRange=4,chaseWindup=.25f,chaseCooldown=6,chaseCoefficient=1.2f,chaseMinimumTravel=.35f,chaseTimeout=2;
        public float readinessSeconds=4,readinessMultiplier=1.5f,breakCoefficient=3,breakRange=2.6f,breakWindup=.55f;
        public int breakDamage=2;
        public float seedRange=7,seedWindup=.45f,seedCooldown=8,parasiteSeconds=6,parasiteInterval=1,parasiteCoefficient=.35f;
        public float groveRange=6,groveWindup=.65f,groveRadius=2.5f,groveSeconds=6,groveHealFraction=.01f,entangleMultiplier=.65f,bossEntangleMultiplier=.85f;
        public int groveMaximumEnemies=4;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12BeamConfig beam;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12BuildConfig build;
    }
    [Serializable] public sealed class V12HeroIntent
    {
        public string id,actionId,targetId,stage,reason;
        public float remaining,startX,startZ,destinationX,destinationZ,deadline;
        public int startingHp;
    }
    [Serializable] public sealed class V12ActorState
    {
        public V12HeroIntent intent;
        public float basicCooldown,echoCooldown,readinessRemaining;
        public string lastDodgeRoot;
        public double groveHealingRemainder;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public double buildHealingRemainder;
    }
    [Serializable] public sealed class V12StatusData
    {
        public string family,rootId;
        public float expiresAt,nextTick,multiplier=1;
        public int tickIndex,generation;
        public double damageRemainder;
    }
    [Serializable] public sealed class V12Grove
    {
        public string id,sourceId;
        public float x,z,radius,expiresAt,nextTick;
        public int tickIndex;
        public List<string> touchedEnemies=new List<string>();
    }
    [Serializable] public sealed class V12Effect
    {
        public string id,rootId,sourceId,targetId,actionId,kind;
        public int amount;
        public float simulationTime;
    }
    [Serializable] public sealed class V12CombatState
    {
        public List<V12Grove> groves=new List<V12Grove>();
        public List<V12Effect> events=new List<V12Effect>();
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12BeamState beam;
    }
    [Serializable] public sealed class V12ActionContext { public float startDeadline; }
    [Serializable] public sealed class V12ActionResult { public List<V12Effect> effects=new List<V12Effect>(); }
    public sealed partial class CombatConfig { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12CombatConfig v12; }
    public sealed partial class ActorState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12ActorState v12; }
    public sealed partial class StatusState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12StatusData v12; }
    public sealed partial class CombatState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12CombatState v12; }
    public sealed partial class RollTicket { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12ActionContext v12; }
    public sealed partial class CombatResult { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V12ActionResult v12; }
}
