using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class TutorialFactDefinition
    {
        public string key;
        public float threshold=1;
        public bool allowSystem,recoverable;
        public List<string> implies=new List<string>();
    }
    [Serializable] public sealed class TutorialStepDefinition
    {
        public string id,prompt,waitingPrompt,highlightKey,targetKey,waitingTargetKey;
        public List<string> requiredFacts=new List<string>(),availableFacts=new List<string>(),retireOnFacts=new List<string>(),retireUnlessFacts=new List<string>();
    }
    [Serializable] public sealed class TutorialPhaseDefinition
    {
        public string id,promptOverride,fallbackPrompt,highlightKey,targetKey;
        public bool waiting;
        public List<string> requiredContextFacts=new List<string>(),excludedContextFacts=new List<string>(),prioritySteps=new List<string>();
    }
    [Serializable] public sealed class TutorialObservation
    {
        public string factKey,sourceId,profileInstanceId,storyModeId;
        public float value=1;
        public bool accepted=true,duplicate,playerInitiated=true;
    }
    [Serializable] public sealed class TutorialFactRecord { public string key,sourceId;public float value; }
    [Serializable] public sealed class TutorialStepProgress { public string id,status="Pending",reason; }
    [Serializable] public sealed class TutorialReceipt { public string id,kind; }
    [Serializable] public sealed class TutorialState
    {
        public int schemaVersion=1,definitionVersion=1,resetEpoch;
        public long revision;
        public string definitionId,profileInstanceId,storyModeId,status="Active";
        public List<TutorialFactRecord> facts=new List<TutorialFactRecord>();
        public List<TutorialStepProgress> steps=new List<TutorialStepProgress>();
        public List<TutorialReceipt> receipts=new List<TutorialReceipt>();
    }
    [Serializable] public sealed class TutorialContext
    {
        public bool storyActive=true,suppress;
        public List<string> availableFacts=new List<string>(),worldFacts=new List<string>();
    }
    [Serializable] public sealed class TutorialResult
    {
        public bool success,changed,duplicate;
        public string reason;
        public long revision;
    }
    [Serializable] public sealed class TutorialView
    {
        public bool visible,waiting,finished,skipped;
        public string stepId,prompt,highlightKey,targetKey;
        public int stepNumber,stepCount;
    }
}
