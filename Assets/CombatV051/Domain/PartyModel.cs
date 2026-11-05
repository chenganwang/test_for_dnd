using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class PendingIncoming
    {
        public string id,rootId,attackerId,defenderId,sourceKind,stage="Recorded",reactionWindowId,reason,reactionEventId,guardActorId;
        public List<string> reactionEligibleActorIds;
        public List<int> rawFaces=new List<int>();
        public int selectedFace,total,baseArmorClass,effectiveArmorClass,dc,baseDamage,prospectiveDamage,finalDamage;
        public bool hitBeforeReaction,critical,parryable,damageApplied,guarded;
        public long arrivalSequence;
        public float createdSimulationTime;
    }
    [Serializable] public sealed class ReactionWindow
    {
        public string id,incomingId,defenderId,stage="Offered",reactionId,ticketId,reactorId,reactionEventId,paymentId;
        public float remaining;
        public bool attempted,resourcesSpent,reactionSpent;
        public ReactionSpec spec;
    }
    [Serializable] public sealed class DecisionResumeContext
    {
        public CombatMode priorMode;
        public string suspendedTicketId,priorCommandActorId,reactionWindowId,reactionEventId;
    }
    [Serializable] public sealed class ReactionQuote
    {
        public bool legal,requiresD20;
        public string reason,windowId,actorId,reactionId,eventId,defenderId,incomingId;
        public float range,damageReduction;
        public int incomingTotal,currentArmorClass,afterArmorClass,spellCost,superiorityCost,prospectiveDamage,bonus,targetNumber;
        public double successProbability;
    }
    public sealed partial class CombatState
    {
        public static CombatState CreatePartyP1(string profileId,string modeInstanceId,ulong seed,CombatConfig config=null)
        {
            config=config??new CombatConfig();var s=Create(profileId,modeInstanceId,seed);
            s.schemaVersion=3;s.contentVersion="actioncrpg-v0.5.1-p1";s.rulesVersion="v051-p1-1";s.rulesProfileId="p1-party";s.encounterId="p1-encounter-1";
            s.actors=new List<ActorState>{config.Template(ActorRole.Fighter).Create("fighter",-3,0),config.Template(ActorRole.Rogue).Create("rogue",-4,-2),config.Template(ActorRole.Mage).Create("mage",-5,2)};
            s.enemies[0].hp=s.enemies[0].maxHp=config.partyEnemyHealth;s.enemies[0].lockedZ=-1;
            return s;
        }
    }
}
