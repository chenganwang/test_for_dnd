using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V11ActorLoadout
    { public string actorId,passiveId;public bool passiveUnlocked;public List<string> unlockedActionIds=new List<string>();
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11FinnSustainConfig finnSustain;
    }
    [Serializable] public sealed class V11FinnSustainConfig
    {
        public double maxHpFraction=.005;
        public float intervalSeconds=.5f;
        // Null means self. An explicit living party member can be configured, never all allies.
        public string targetActorId;
    }
    [Serializable] public sealed class V11FinnSustainState
    { public double fractionalHp;public float lastSimulationTime;public string lastRoot; }
    [Serializable] public sealed class V11BuffStack
    { public string id;public int stacks; }
    [Serializable] public sealed partial class V11CombatRules
    {
        public string version="v11-character-1",coreActorId;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string v12BuildVersion;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string v13FinnTree;
        public List<V11ActorLoadout> actors=new List<V11ActorLoadout>();
        public List<V11BuffStack> buffs=new List<V11BuffStack>();
    }
    [Serializable] public sealed class V11ActorState
    { public bool passiveApplied;public int shieldGranted;public float shieldRemaining;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11FinnSustainState finnSustain;
    }
    [Serializable] public sealed class V11ActionContext
    { public bool customAction;public int guardRadiantDice;public float startDeadline; }
    [Serializable] public sealed class V11EffectResult
    { public string actorId,targetId,kind;public int amount;public float seconds; }
    [Serializable] public sealed class V11ActionResult
    { public List<V11EffectResult> effects=new List<V11EffectResult>(); }
    [Serializable] public sealed class V11DamageBreakdown
    { public int radiantDiceBonus,fullDiceBonus,guardDiceBonus,smiteAttackBonus;public List<string> buffIds=new List<string>(); }
    public sealed partial class CombatConfig
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11CombatRules v11;
        public bool AllowsAction(ActorState actor,string actionId)
        {
            if(actor==null||actor.isEnemy)return false;
            return v11==null?Template(actor.role)?.actionIds.Contains(actionId)==true:v11.actors.Find(x=>x.actorId==actor.id)?.unlockedActionIds.Contains(actionId)==true;
        }
    }
    public sealed partial class ActorState
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11ActorState v11; }
    public sealed partial class RollTicket
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11ActionContext v11; }
    public sealed partial class P6DamageResult
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11DamageBreakdown v11; }
    public sealed partial class CombatResult
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11ActionResult v11; }
}
