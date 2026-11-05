using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V13Rules
    {
        public string version="v13-core-assist-1",finnTree="growth";
        public float actionSeconds=1.2f,inputDebounce=.35f,echoInterval=3,chaseRewardTravel=1,threatGap=.85f,coreHealthScale=1.6f;
        public float shieldFraction=.06f,shieldSeconds=6,shieldCooldown=10,prayerFraction=.16f;
        public int prayerCapacity=2;
        public float fateInterval=120,fateWarning=5,fateDecision=6,fateRecovery=.9f;
    }
    [Serializable] public sealed class V13Member
    {
        public string actorId,correctionTarget,lastAvoidedRoot;
        public float actionUntil,inputUntil,appearanceUntil,correctionUntil,assistRewardUntil;
        public int prayers=2;
        public List<string> prayerSpent=new List<string>(),refunded=new List<string>();
    }
    [Serializable] public sealed class V13Echo { public string id,actorId;public int raw; }
    [Serializable] public sealed class V13FateEvent
    {
        public string id,encounterId,trigger,stage="Scheduled",tableVersion="v13-tides-1",echoId,paymentId,effectId,fallback;
        public ulong rngIndex;
        public int original,final,echoRaw;
        public float warning,decision=6,age;
        public List<string> targets=new List<string>();
    }
    [Serializable] public sealed class V13Terrain
    {
        public string id,kind;
        public float x,z,warning,remaining,nextTick;
    }
    [Serializable] public sealed class V13Boss
    {
        public string actorId,stage="Recovery",move,root;
        public int phase=1,sequence,sweeps;
        public float remaining=1.5f,dx,dz,travel,breakImmunity,arenaX,arenaZ,coreStartX,coreStartZ;
        public bool secondSweep,pillarA,pillarB,nodeA,nodeB;
    }
    [Serializable] public sealed class V13CombatState
    {
        public string version="v13-core-assist-1",coreId;
        public List<V13Member> members=new List<V13Member>();
        public RngState fateRng;
        public V13Echo echo;
        public V13FateEvent fate;
        public List<V13FateEvent> history=new List<V13FateEvent>();
        public List<string> entryTriggers=new List<string>(),receipts=new List<string>();
        public List<V13Terrain> terrain=new List<V13Terrain>();
        public int fateCount;
        public bool timeUsed;
        public float combatSinceFate,enemyRestUntil,ruleUntil;
        public int activeRule;
        public V13Boss boss;
    }
    public sealed partial class CombatConfig { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V13Rules v13; }
    public sealed partial class CombatState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V13CombatState v13; }
}
