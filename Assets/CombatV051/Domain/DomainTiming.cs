using System;
using System.Diagnostics;
using System.Text;

namespace CombatV051.Domain
{
    // Opt-in, inclusive wall time. A null callback does not read the clock or allocate.
    // Observability must never turn a successful durable transaction into a failure.
    public static class DomainTiming
    {
        public static Action<string,double> Sample;
        // Opt-in metadata for an encoded save attempt, not a durable-commit receipt.
        // Only scalar values reach the observer; it cannot mutate the candidate DTO.
        public static Action<ProfileSaveMetrics> ProfileSave;
        public struct ProfileSaveMetrics
        {
            public string profileId,modeInstanceId,roomId,roomTemplateId,encounterId,combatMode;
            public long profileRevision,combatRevision;
            public int payloadBytes,encodedBytes,profileReceipts,currentCombatReceipts,
                currentCombatIncomingEvents,currentCombatPendingThreatEvents,currentCombatReactionWindows,
                currentCombatFocusRewardEvents,currentCombatFocusRewardRoots,currentCombatFocusRecoveryScopes;
        }
        public static void RecordProfileSave(ProfileState candidate,string payload,int encodedBytes)
        {
            var sample=ProfileSave;if(sample==null)return;
            try
            {
                var mode=candidate.ActiveMode;CombatState combat=mode?.explorationCombat;
                if(mode!=null&&mode.activeEncounterId!=null)
                    foreach(var encounter in mode.encounters)
                        if(encounter.id==mode.activeEncounterId){combat=encounter.currentCombat;break;}
                var room=mode?.Room(mode.currentRoomId);
                sample(new ProfileSaveMetrics
                {
                    profileId=candidate.profileId,profileRevision=candidate.revision,
                    modeInstanceId=mode?.id,roomId=mode?.currentRoomId,roomTemplateId=room?.templateId,
                    encounterId=combat?.encounterId,combatMode=combat?.mode.ToString(),combatRevision=combat?.revision??-1,
                    payloadBytes=Encoding.UTF8.GetByteCount(payload),encodedBytes=encodedBytes,
                    profileReceipts=candidate.receipts?.Count??0,currentCombatReceipts=combat?.receipts?.Count??0,
                    currentCombatIncomingEvents=combat?.incomingEvents?.Count??0,
                    currentCombatPendingThreatEvents=combat?.pendingThreatEvents?.Count??0,
                    currentCombatReactionWindows=combat?.reactionWindows?.Count??0,
                    currentCombatFocusRewardEvents=combat?.focusBudget?.rewardEvents?.Count??0,
                    currentCombatFocusRewardRoots=combat?.focusBudget?.rewardRoots?.Count??0,
                    currentCombatFocusRecoveryScopes=combat?.focusBudget?.recoveryScopes?.Count??0
                });
            }
            catch{/* Metadata collection and callbacks have no gameplay authority. */}
        }
        public static Scope Measure(string name)
        {
            var sample=Sample;
            return sample==null?default(Scope):new Scope(name,sample);
        }
        public struct Scope:IDisposable
        {
            readonly string name;
            readonly Action<string,double> sample;
            readonly long started;
            internal Scope(string name,Action<string,double> sample)
            {this.name=name;this.sample=sample;started=Stopwatch.GetTimestamp();}
            public void Dispose()
            {
                if(sample==null)return;
                double milliseconds=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
                try{sample(name,milliseconds);}catch{/* A profiler callback has no gameplay authority. */}
            }
        }
    }
}
