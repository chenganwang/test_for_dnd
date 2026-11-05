using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    public enum ProfileModeKind { Story, Challenge }
    public enum ProfileCommandKind { StartStory, UnlockChallenge, StartChallenge, ContinueStory, ContinueChallenge, EnterRoom, ActivateEncounter, ResolveEncounter, OpenEvent, RollEvent, RerollEvent, ConfirmEvent, UseEventAlternative, CloseEvent, ShortRest, OpenGate, OpenShortcut, UseEnvironment, ClaimReward, SkipReward, RetryStoryEncounter, SettlementOnce, SetFocusScale, SaveCurrent, TrainParty, SavePreparation, UnlockSpecialization, ApplyStoryPreparation, StartDemoExploration, SaveP6Preparation, EnsureV11Progress, UnlockV11Node, SaveV11Preparation }
    [Serializable] public sealed class ProfileCommand
    {
        public string id,modeInstanceId,roomId,roomInstanceId,eventId,actorId="fighter",gateId,selectionId,rewardId,encounterId,sourceFactId;
        public ProfileCommandKind kind;
        public long expectedRevision=-1,expectedBindingRevision=-1;
        public int replacementSlot=-1;
        public bool qualified;
        public float amount;
        public ulong seed;
        public PartyPreparation preparation;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V11PreparationDraft v11Preparation;
    }
    [Serializable] public sealed class ProfileResult
    {
        public bool success,duplicate;
        public string reason,encounterId,eventId,rewardId,settlementId;
        public long revision;
    }
    [Serializable] public sealed class ProfileReceipt { public string id,fingerprint;public ProfileResult result; }
    [Serializable] public sealed class BuildState { public string primaryLawId="BASE";public List<string> relicSlots=new List<string>{null,null}; }
    [Serializable] public sealed partial class ProfileMeta
    {
        public bool challengeUnlocked;
        public int research,trainingLevel;
        public List<string> unlockedLaws=new List<string>{"BASE","COMMAND"},settledRunIds=new List<string>(),unlocks=new List<string>();
        public SettlementState lastSettlement;
        public PartyPreparation lastPreparation=PartyPreparation.Default();
        public List<string> unlockedSpecializations=new List<string>{"F_GUARD","R_FLANK","M_INSIGHT"};
    }
    [Serializable] public sealed class SettlementState { public string runId;public ProfileModeKind kind;public bool victory;public int earned;public float elapsedRealSeconds; }
    [Serializable] public sealed class RewardOffer
    {
        public string id,roomId,stage="Offered",chosenId;
        public List<string> candidates=new List<string>();
    }
    [Serializable] public sealed class RoomState
    {
        public string id,templateId,stage="Unvisited",encounterId;
        public bool visited,resolved;
        public List<string> eventIds=new List<string>();
        public List<EnemySpawn> enemySpawns=new List<EnemySpawn>();
    }
    [Serializable] public sealed class EncounterRecord
    {
        public string id,roomId,stage="Active",attemptId,configHash;
        public int attemptNumber,initialCharge=2,chargeCap=2;
        public ulong seed;
        public CombatConfig resolvedConfig;
        public CombatState encounterStartSnapshot,currentCombat;
    }
    [Serializable] public sealed class SafeEventState
    {
        public string id,templateId,roomId,stage="Unopened",actorId,ability,worldEffect;
        public int dc,bonus,selectedFace,total,rollCount;
        public bool advantage,disadvantage,success,rerollUsed,usedAlternative;
        public List<int> rawFaces=new List<int>();
    }
    [Serializable] public sealed class EventQuote
    {
        public bool legal,advantage,disadvantage;
        public string reason,eventId,actorId,ability;
        public int dc,bonus,inspirationCost=1;
        public double successProbability;
    }
    [Serializable] public sealed partial class ModeState
    {
        public ProfileModeKind kind;
        public string id,stage="Exploring",currentRoomId,activeEncounterId,activeEventId,planHash;
        public ulong seed;
        public int runEarnings,departureTraining;
        public float elapsedRealSeconds,explicitPauseSeconds,focusLostSeconds;
        public PreparationSnapshot departurePreparation;
        public BuildState build=new BuildState();
        public CombatState explorationCombat;
        public List<string> abilities=new List<string>(),departureAbilitySnapshot=new List<string>(),departureUnlockPoolSnapshot=new List<string>(),flags=new List<string>(),openedGateIds=new List<string>(),usedRestIds=new List<string>(),claimedRewardIds=new List<string>(),environmentFacts=new List<string>();
        public List<RoomState> rooms=new List<RoomState>();
        public List<EncounterRecord> encounters=new List<EncounterRecord>();
        public List<SafeEventState> events=new List<SafeEventState>();
        public RewardOffer pendingReward;
        public RandomStreamsState random;
        public RoomState Room(string id){return rooms.Find(r=>r.id==id||r.templateId==id);}
        public bool HasFlag(string flag){return flags.Contains(flag);}
        public bool HasAbility(string ability){return kind==ProfileModeKind.Challenge?departureAbilitySnapshot.Contains(ability)||(HasFlag("DEMO_EXPLORATION")&&Room("R3")?.resolved==true&&abilities.Contains(ability)):abilities.Contains(ability);}
    }
    [Serializable] public sealed class ProfileState
    {
        public int schemaVersion=5;
        public string contentVersion="actioncrpg-v0.5.2-p4",profileId,profileInstanceId,configHash,baseCombatConfigHash;
        public string migrationSourceDigest,migrationPolicy;
        public long revision,bindingRevision,modeSequence;
        public ulong seed;
        public float focusScale=.15f;
        public ProfileModeKind activeMode=ProfileModeKind.Story;
        public ProfileMeta meta=new ProfileMeta();
        public ModeState story,challenge;
        public List<ProfileReceipt> receipts=new List<ProfileReceipt>();
        [JsonIgnore] public ModeState ActiveMode {get{return activeMode==ProfileModeKind.Challenge?challenge:story;}}
        public ProfileState Clone(){return CombatJson.Clone(this);}
    }
}
