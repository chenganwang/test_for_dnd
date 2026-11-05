using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    // P5 is an opt-in extension of the existing combat authority. Null extensions
    // are omitted so legacy command fingerprints and configuration hashes stay exact.
    [Serializable] public sealed class P5Config
    {
        public string version="p5.1";
        public float opportunitySeconds=8,readyStartSeconds=2,prepareSeconds=.8f,prepareCooldown=6,braceSeconds=2,braceMoveMultiplier=.6f,flankAuthorizationSeconds=4;
        public float frostSeconds=4,frostMoveMultiplier=.7f,shoveDistance=2,thunderDistance=2,followupDistance=.8f,retreatDistance=1.5f;
        public float comboMomentum=30,comboFocus=.75f,followupMomentum=10,breakWindowSeconds=3,breakRecoverySeconds=5;
        public int ordinaryStabilityDc=12,eliteStabilityDc=14,bossStabilityDc=16;
        public float shoveCooldown=4,sneakCooldown=5,thunderCooldown=5,thunderAngle=90,bossPushMultiplier=.25f;
        public int impactMaximumUses=2; public float impactMinimumDistance=.6f;
        public int frontalShieldAcBonus=2;public List<string> frontalShieldTemplateIds=new List<string>{"guard"};
        public List<string> braceEligibleTemplateIds=new List<string>{"guard","scout"};
        public List<ActionSpec> actions=new List<ActionSpec>{
            new ActionSpec{id="shove",rollType="Check",range=2.5f,windup=.25f,ranged=false,damage=0,shoveDistance=2,breakDamage=1},
            new ActionSpec{id="sneak",rollType="Attack",range=2.5f,windup=.25f,ranged=false,damage=3,requiresFlanking=false},
            new ActionSpec{id="control",rollType="Save",saveAbility="Constitution",range=3,windup=.4f,ranged=false,damage=0,spellCost=1,shoveDistance=2}
        };
    }
    public sealed partial class CombatConfig
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5Config p5; }
    [Serializable] public sealed class P5OpportunityData
    {
        public string kind,sourceActionId,sourceConditionId,encounterAttemptId,status="Available";
        public float createdSimulationTime;
        public bool neutral;
    }
    public sealed partial class SetupToken
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5OpportunityData p5; }
    [Serializable] public sealed class P5PreparationState
    {
        public string actionId,targetId,sourceRoot,threatId;
        public float remaining;
        public bool ready,used;
    }
    [Serializable] public sealed class P5ActorState
    {
        public P5PreparationState preparation;
        public float shoveCooldown,sneakCooldown,thunderCooldown,breakRecovery;
        public int frontalShieldAcBonus;
        public bool basicRecovery;
    }
    public sealed partial class ActorState
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5ActorState p5; }
    [Serializable] public sealed class P5ConditionData
    { public string instanceId,sourceActionId;public float moveMultiplier=1; }
    public sealed partial class StatusState
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5ConditionData p5; }
    [Serializable] public sealed class P5ImpactUse
    { public string id;public int uses;public List<string> roots=new List<string>(); }
    [Serializable] public sealed class P5CombatState
    {
        public string version="p5.1";
        public List<SetupToken> opportunities=new List<SetupToken>();
        public List<string> productionRoots=new List<string>();
        public List<P5ImpactUse> impactSurfaces=new List<P5ImpactUse>();
    }
    public sealed partial class CombatState
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5CombatState p5; }
    // Runtime supplies actual line-of-effect/cover and swept displacement evidence.
    // Absence of targets is permitted only by pure-domain geometry fixtures.
    [Serializable] public sealed class P5TargetEvidence
    {
        public string targetId,impactSurfaceId;
        public bool lineOfSight=true,inArea=true,hasDisplacement,impactRegistered,allowInterrupt=true;
        public float x,z,actualDistance;
        public int coverAcBonus,frontalShieldAcBonus;
    }
    [Serializable] public sealed class P5CommandData
    {
        public string opportunityId;
        public float directionX,directionZ;
        public List<P5TargetEvidence> targets;
    }
    public sealed partial class CombatCommand
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5CommandData p5; }
    [Serializable] public sealed class P5TargetRoll
    {
        public string targetId;
        public List<int> rawFaces=new List<int>();
        public int selectedFace,total,bonus,targetNumber,damage;
        public bool savePassed,effectApplied,inAreaAtConfirm;
    }
    [Serializable] public sealed class P5ActionContext
    {
        public string root,encounterAttemptId,opportunityId,opportunityKind,ownerId,targetId,sourceConditionId;
        public bool crossActor,neutral,paid,effective,rewarded,weakEffect;
        public float startDeadlineSimulationTime,pushDistance,retreatDistance,directionX,directionZ;
        public int bonusBreak,extraDamageDice,ignoreShieldAc,savePenalty,damage;
        public bool advantage;
        public List<int> damageFaces=new List<int>();
        public List<P5TargetRoll> targets=new List<P5TargetRoll>();
    }
    public sealed partial class RollTicket
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5ActionContext p5; }
    public sealed partial class BasicAttackIntent
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5ActionContext p5; }
    public sealed partial class BasicOrder
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5CommandData p5; }
    [Serializable] public sealed class P5OpportunityQuote
    {
        public bool legal;public string reason,id,kind,ownerId,targetId,description;
        public float remaining;public bool neutral;
    }
    [Serializable] public sealed class P5EffectResult
    {
        public string targetId,kind,root;
        public int damage,breakDamage;
        public float fromX,fromZ,x,z;
        public bool effective,weakEffect;
    }
    public sealed partial class CombatResult
    { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public List<P5EffectResult> p5Effects; }
}
