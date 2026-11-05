using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class P6Config
    {
        public string rulesVersion="p6-smite-070-1",presetId="preset_greatsword_smite";
        public ActorTemplate paladinTemplate=new ActorTemplate{role=ActorRole.Paladin,hp=36,armorClass=15,attackBonus=5,strengthModifier=3,formation=FormationPreference.Front,actionIds=new List<string>{"ability_smite_recharge"},basic=new BasicAttackSpec{id="basic_greatsword",range=2.5f,damage=10,ranged=false}};
        public P6FocusConfig focus=new P6FocusConfig();
        public float passiveMomentumPerSimulationSecond=10,guardMomentum=25,guardInterval=4,markMomentum=20,missMomentum=50;
        public int capacity=2,maximumCapacity=3,newEncounterFloor=2,markRewardsPerEncounter=2;
        public float basicInterval=1.1f,basicWindup=.12f,basicRange=2.5f,basicAngle=90;
        public int weaponDice=2,weaponSides=6,smiteRadiantDice=3,overchargeRadiantDice=2,radiantSides=8,smiteCriticalRaw=19;
        public float smiteRange=2.5f,smiteWindup=.35f,smiteRecovery=.4f,queuedStartSeconds=1;
        public float bindRange=7,bindWindup=.75f,bindCooldown=16,markSeconds=8,radiantMultiplier=1.5f;
        public float rootNormalSeconds=1.2f,rootEliteSeconds=.6f,bossSlowSeconds=2,bossSlowMultiplier=.65f;
        public ActionSpec SmiteSpec(){return new ActionSpec{id="ability_smite_recharge",rollType="Attack",range=smiteRange,windup=smiteWindup,ranged=false,damage=0};}
        public ActionSpec BindSpec(){return new ActionSpec{id="ability_bind_radiance",rollType="Save",saveAbility="Wisdom",range=bindRange,windup=bindWindup,ranged=true,damage=0};}
    }
    [Serializable] public sealed class P6CombatState
    {
        public string rulesVersion="p6-smite-070-1",presetId="preset_greatsword_smite",encounterId;
        public RngState damageRandom;
        public bool training,trainingBlessingEnabled,trainingBlessingUsed;
        public string trainingUpgradeId;
        public float lastGuardSimulationTime=-999;
        public int markRewardCount;
        public List<P6ResourceEvent> resourceEvents=new List<P6ResourceEvent>();
        public List<P6DamageResult> damageResults=new List<P6DamageResult>();
        public List<P6BindResult> bindResults=new List<P6BindResult>();
    }
    [Serializable] public sealed class P6ActorState
    {
        public string characterId,classId,weaponId;
        public float bindCooldown;
        public P6BindIntent bindIntent;
        public bool basicRecovery;
        // Explicit bounded typed mitigation; defaults preserve the existing actors.
        public float physicalDamageMultiplier=1,radiantDamageMultiplier=1;
        public int absorptionShield;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public int v13FateShield;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public float v13FateShieldUntil;
    }
    [Serializable] public sealed class P6BindIntent
    {
        public string id,targetId,stage="Casting",reason;
        public float remaining;
        public int startingHp;
    }
    [Serializable] public sealed class P6RadiantMark
    {
        public string id,casterId,rootId;
        public float multiplier=1.5f;
    }
    [Serializable] public sealed class P6SmiteContext
    {
        public string markId,criticalSource;
        public bool preacceptBaseResult,acceptBaseOnTimeout=true,overchargeSelected,overchargeHeld,overchargePaid,executed,missCompensated;
        public int criticalMinimumRaw=19;
        public float committedAt=-1,startDeadline=-1;
        public P6DamageResult damage;
    }
    [Serializable] public sealed class P6BasicContext
    {
        public float directionX,directionZ=1;
        public List<string> resolvedTargetIds=new List<string>();
    }
    [Serializable] public sealed class P6CommandData
    {
        public bool preacceptBaseResult,acceptBaseOnTimeout=true;
        public float directionX,directionZ;
        public List<P6TargetEvidence> targets;
    }
    [Serializable] public sealed class P6TargetEvidence
    {
        public string targetId;
        public bool lineOfSight=true,inArea=true;
    }
    [Serializable] public sealed class P6ResourceEvent
    {
        public string root,kind;
        public float qualifiedAmount,creditedAmount,discardedAmount;
    }
    [Serializable] public sealed partial class P6DamageResult
    {
        public string id,rootId,actorId,targetId,actionId,criticalSource,markId;
        public List<int> attackFaces=new List<int>(),weaponFaces=new List<int>(),radiantFaces=new List<int>();
        public int selectedRaw,total,targetAc,strengthBonus,physicalBeforeMitigation,radiantBeforeMitigation,physicalApplied,radiantApplied,absorbed,totalDamage;
        public float radiantMultiplier=1;
        public bool hit,critical,overcharged,markConsumed;
    }
    [Serializable] public sealed class P6BindResult
    {
        public string id,casterId,targetId,markId;
        public int raw,total,dc;
        public bool savePassed,markApplied;
        public string controlKind;
        public float controlSeconds;
    }
    [Serializable] public sealed class P6ActionResult
    {
        public List<P6DamageResult> damages=new List<P6DamageResult>();
        public P6BindResult bind;
        public bool overchargeHeld,overchargePaid,directExecution;
        public string bindIntentId;
    }
    [Serializable] public sealed class P6SmiteQuote
    {
        public ActionQuote attack;
        public int availableCharges;
        public bool interactiveAllowed,directAllowed,canOvercharge;
        public float markRemaining;
        public double criticalProbability;
        public string reason;
    }
    public sealed partial class CombatConfig
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6Config p6;}
    public sealed partial class CombatState
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6CombatState p6;}
    public sealed partial class ActorState
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6ActorState p6;}
    public sealed partial class StatusState
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6RadiantMark p6;}
    public sealed partial class RollTicket
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6SmiteContext p6;}
    public sealed partial class BasicAttackIntent
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6BasicContext p6;}
    public sealed partial class CombatCommand
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6CommandData p6;}
    public sealed partial class CombatResult
    {[JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6ActionResult p6;}
}

