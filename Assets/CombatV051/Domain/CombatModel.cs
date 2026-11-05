using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    public enum CombatMode { RealtimeCombat, TacticalFocus, RollFreeze, PostRollDecision, QueuedExecution, CombatPaused, ReactionPrompt }
    public enum CommandKind { EnterFocus, ExitFocus, ConfirmRoll, RevealRoll, Reroll, Modify, Precision, Commit, Cancel, BasicAttack, StartAim, CancelAim, StartGuard, EndGuard, StartDodge, AwardFact, StartQueued, ResolveQueued, CancelQueued, Pause, Resume, EndEncounter, IncomingAttack, IncomingSave, SwitchActive, SelectCommandActor, BeginReaction, ConfirmReactionRoll, CommitReaction, DeclineReaction, FinalizeIncoming, IncomingBatch, SetReactionSettings, IssueBasicOrder, CancelBasicOrder, CompleteBasicOrder, StartPreparation, CancelPreparation, StartBasicAttack, ResolveBasicAttack, SelectSmiteOvercharge, RequestBindRadiance, ResolveBindRadiance, RefillPracticeResources, ChoosePracticeUpgrade, RequestHeroAction, ResolveHeroAction, ResolveHeroTick, RequestV12Beam, ResolveV12Beam, V13FateStart, V13FateReveal, V13FateAccept, V13FateReplace, V13FateEffects, V13NodeHit, V13PillarHit }
    public enum ActorRole { Fighter, Rogue, Mage, Enemy, Paladin }
    public enum FormationPreference { Front, Mid, Back }
    public enum CommandAuthority { Player, AllyAI, EnemyAI, System }

    [Serializable] public sealed partial class CombatCommand
    {
        public string id, actorId="fighter", targetId="enemy-1", actionId="attack", ticketId, queueId, factId, rootId, templateId;
        public CommandKind kind;
        public long expectedRevision=-1;
        public bool flag, qualified,lineOfSight=true;
        public float amount, x, z;
        public int dc=12;
        public CommandAuthority authority;
        public string reactionId,reactionWindowId,incomingId;
        public bool parryable=true;
    }
    [Serializable] public sealed partial class CombatResult
    {
        public bool success, hit, critical, duplicate;
        public string reason, ticketId, queueId, executionId;
        public long revision;
        public int total, damage, chargeSpent;
        public List<int> rawFaces=new List<int>();
        public string incomingId,reactionWindowId,disposition;
        public bool deferred,damageApplied;
        public int prospectiveDamage;
        public CombatResult Clone() { return CombatJson.Clone(this); }
    }
    [Serializable] public sealed class CommandReceipt
    {
        public string id, fingerprint;
        public CombatResult result;
    }
    [Serializable] public sealed partial class ActorState
    {
        public string id, heldBy, attackGroupId, attackTargetId;
        public bool isEnemy;
        public float x,z,heightY;
        public int hp=12,maxHp=12,armorClass=14,attackBonus=5,saveBonus=3,proficiency=2,strengthModifier=3;
        public float majorRemaining,bonusRemaining,reactionRemaining,aimRemaining,guardRemaining,guardElapsed,dodgeRemaining,dodgeCooldown,windupRemaining;
        public bool aimReady,aimPreparing;
        public int inspiration=2,superiority=2,spellSlots=1,items=1,heldSpellSlots,heldItems;
        public int aiState,attackSequence;
        public float aiRemaining,lockedX,lockedZ;
        public float breakValue=3,breakMaximum=3,staggerRemaining;
        public string concentrationId;
        public ActorRole role;
        public FormationPreference formation;
        public int dexterityModifier,intelligenceModifier,wisdomModifier,charismaModifier,constitutionModifier,dexSaveBonus,wisSaveBonus;
        public string reactionHeldBy,aiTargetId,aiPolicyId="conservative-p1";
        public int heldSuperiority;
        public float aiMoveX,aiMoveZ,aiDecisionRemaining;
        public List<StatusState> statuses=new List<StatusState>();
        [JsonIgnore] public bool Alive { get { return hp>0; } }
    }
    [Serializable] public sealed partial class StatusState
    {
        public string id,sourceId,concentrationId;
        public float remaining,tickRemaining;
        public int stacks=1;
        public int armorClassBonus;
    }
    [Serializable] public sealed class TelegraphState
    {
        public string id,ownerId,targetId,shape="Circle";
        public float x,z,directionX,directionZ,radius=2,length=4,angle=70,remaining,total;
        public int damage=3,dc=12;
        public bool resolved;
    }
    [Serializable] public sealed class ProjectileState
    {
        public string id,ownerId,rootId;
        public float x,z,heightY,directionX,directionZ,speed,remaining;
        public int damage;
        public bool resolved;
    }
    [Serializable] public sealed class WorldState
    {
        public Dictionary<string,float> numbers=new Dictionary<string,float>();
        public Dictionary<string,string> strings=new Dictionary<string,string>();
        public List<TelegraphState> telegraphs=new List<TelegraphState>();
        public List<ProjectileState> projectiles=new List<ProjectileState>();
    }
    [Serializable] public sealed partial class RollTicket
    {
        public string id,actorId,targetId,actionId,stage="Rolling",queueId,rollOwnerId,rollType;
        public ActionSpec spec;
        public List<int> rawFaces=new List<int>();
        public List<string> modifierSources=new List<string>(),paidResourceIds=new List<string>();
        public int selectedFace,total,targetNumber,precisionBonus,baseBonus,flatBonus;
        public bool hit,critical,advantage,disadvantage,rerollUsed,precisionUsed,modifyUsed;
        public int chargeSpent=1;
        public string createdRulesVersion="v051-p0-1";
        public ulong rngBefore,rngAfter;
        public string purpose="Tactical",reactionWindowId,incomingId,chargePolicy="SharedCharge";
    }
    [Serializable] public sealed class QueuedAction
    {
        public string id,ticketId,actorId,targetId,actionId,stage="Queued",reason,executionId;
        public float windupRemaining;
        public bool resourcesSpent,effectApplied;
    }
    [Serializable] public sealed partial class MomentumLedger
    {
        public float passiveEarned,totalEarned,lastBasicHit=-999;
        public List<string> roots=new List<string>();
        public List<TemplateReward> templates=new List<TemplateReward>();
    }
    [Serializable] public sealed class TemplateReward
    {
        public string id;
        public float lastTime=-999;
        public int repeats;
    }
    [Serializable] public sealed partial class CombatState
    {
        public int schemaVersion=2;
        public string contentVersion="actioncrpg-v0.5.1-p0",rulesVersion="v051-p0-1",profileId,modeInstanceId,encounterId="p0-encounter-1",activeActorId="fighter";
        public long revision,sequence;
        public bool inCombat=true,encounterResolved;
        public CombatMode mode=CombatMode.RealtimeCombat,resumeMode=CombatMode.RealtimeCombat;
        public int charges=2,chargeCap=2;
        public float momentum,simulationSeconds,realCombatSeconds;
        public string pendingRollId;
        public List<ActorState> actors=new List<ActorState>(),enemies=new List<ActorState>();
        public List<RollTicket> rolls=new List<RollTicket>();
        public List<QueuedAction> queue=new List<QueuedAction>();
        public List<CommandReceipt> receipts=new List<CommandReceipt>();
        public List<string> incomingRoots=new List<string>(),rewardIds=new List<string>(),worldFlags=new List<string>();
        public MomentumLedger momentumLedger=new MomentumLedger();
        public RandomStreamsState random;
        public WorldState world=new WorldState();
        public string rulesProfileId="p0-single",commandActorId="fighter",activeReactionWindowId;
        public List<PendingIncoming> incomingEvents=new List<PendingIncoming>();
        public List<ReactionWindow> reactionWindows=new List<ReactionWindow>();
        public DecisionResumeContext decisionResume;
        [JsonIgnore] public bool IsPartyP1 { get { return rulesProfileId=="p1-party"; } }
        [JsonIgnore] public ReactionWindow activeReaction { get { return reactionWindows.Find(w=>w.id==activeReactionWindowId); } }
        [JsonIgnore] public RollTicket pendingRoll { get { return rolls.Find(r=>r.id==pendingRollId); } }
        public ActorState Actor(string id) { return actors.Find(a=>a.id==id)??enemies.Find(a=>a.id==id); }
        public CombatState Clone() { return CombatJson.Clone(this); }
        public static CombatState Create(string profileId,string modeInstanceId,ulong seed)
        {
            return new CombatState {profileId=profileId,modeInstanceId=modeInstanceId,random=RandomStreams.Create(seed),
                actors=new List<ActorState>{new ActorState{id="fighter",x=-3,z=0}},
                enemies=new List<ActorState>{new ActorState{id="enemy-1",role=ActorRole.Enemy,isEnemy=true,x=2,z=0,hp=30,maxHp=30,armorClass=13,attackBonus=3,saveBonus=2,inspiration=0,superiority=0,spellSlots=0,items=0}}};
        }
    }
    public static class CombatJson
    {
        public static readonly JsonSerializerSettings Settings=new JsonSerializerSettings {ObjectCreationHandling=ObjectCreationHandling.Replace,TypeNameHandling=TypeNameHandling.None,MissingMemberHandling=MissingMemberHandling.Error};
        public static string Write<T>(T value)
        {
            string encoded;
            return CombatTypedJson.TryWrite(value,Settings,out encoded)?encoded:JsonConvert.SerializeObject(value,Formatting.None,Settings);
        }
        public static T Read<T>(string text) { return JsonConvert.DeserializeObject<T>(text,Settings); }
        public static T Clone<T>(T value)
        {
            using(DomainTiming.Measure("Domain.Clone"))
            {
                object copy;
                if(CombatDeepClone.TryClone(value,out copy))return (T)copy;
                // Non-domain callers (for example presentation preferences) retain their codec contract.
                return Read<T>(Write(value));
            }
        }
    }
}
