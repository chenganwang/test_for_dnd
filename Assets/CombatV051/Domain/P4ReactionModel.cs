using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    public enum ReactionAutoMode { ImportantLegal, AllLegal, Manual }
    [Serializable] public sealed class ReactionSettings
    {
        public ReactionAutoMode autoMode=ReactionAutoMode.ImportantLegal;
        public float decisionSeconds=FocusBudgetRules.ReactionDecisionSeconds;
        public bool pauseDecision,timeoutAcceptPaidRoll;
    }
    [Serializable] public sealed class IncomingContact
    {
        public string targetId;
        public int damage,dc=12;
        public bool savingThrow,parryable=true;
        // Null is the pure-domain geometry fixture; Runtime supplies actual LOS-qualified allies.
        public List<string> reactionEligibleActorIds;
    }
    [Serializable] public sealed class PendingThreatEvent
    {
        public string id,rootId,stage="Offered",triggerStage="ConfirmedContact";
        public List<string> incomingIds=new List<string>(),respondedDefenderIds=new List<string>();
        public float remainingUiSeconds;
        public long arrivalSequence;
        public bool important,lethal,autoOpened,fromReaction;
    }
    [Serializable] public sealed class DeferredIncomingBatch
    {
        public string commandId,actorId,rootId;
        public bool fromReaction;
        public List<IncomingContact> contacts=new List<IncomingContact>();
    }
    public sealed partial class CombatCommand
    {
        public string reactionEventId;
        public bool fromReaction;
        public List<IncomingContact> incomingContacts;
        public ReactionSettings reactionSettings;
    }
    public sealed partial class CombatResult { public string reactionEventId; }
    public sealed partial class CombatState
    {
        public List<PendingThreatEvent> pendingThreatEvents=new List<PendingThreatEvent>();
        public List<DeferredIncomingBatch> deferredIncomingBatches=new List<DeferredIncomingBatch>();
        public string activeReactionEventId;
        public ReactionSettings reactionSettings=new ReactionSettings();
        [JsonIgnore] public PendingThreatEvent activePendingEvent {get{return pendingThreatEvents.Find(e=>e.id==activeReactionEventId);}}
    }
    public sealed partial class CombatConfig
    {
        public float importantReactionDamageFraction=.2f,reactionFocusScale=.1f,allyShieldRange=5;
    }
}
