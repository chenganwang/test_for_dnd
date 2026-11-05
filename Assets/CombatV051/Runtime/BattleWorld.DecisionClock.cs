using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        bool decisionClockRunning;
        long decisionTimestamp;
        CombatMode decisionClockMode;
        string decisionClockInstance,decisionClockEncounter,decisionClockEvent;
        void SuspendDecisionClock(){decisionClockRunning=false;}
        bool DecisionClockAvailable => Session!=null&&!Menu&&!DungeonModal&&(IgnoreApplicationFocus||UnityEngine.Application.isFocused)&&State.mode!=CombatMode.RollFreeze&&State.mode!=CombatMode.CombatPaused;
        string DecisionEventId()
        {
            if(State.activeReactionEventId!=null)return State.activeReactionEventId;
            // Manual offered windows have a UI deadline too. A newly arrived offer must
            // not inherit elapsed time from the preceding realtime segment.
            PendingThreatEvent oldest=null;
            foreach(var group in State.pendingThreatEvents)
                if(group.stage=="Offered"&&(oldest==null||group.arrivalSequence<oldest.arrivalSequence))oldest=group;
            return oldest?.id;
        }
        bool MatchesDecisionSegment(string eventId)
        {return decisionClockRunning&&decisionClockMode==State.mode&&decisionClockInstance==State.modeInstanceId&&decisionClockEncounter==State.encounterId&&decisionClockEvent==eventId;}
        void BeginDecisionSegment(string eventId,long now)
        {
            decisionClockRunning=true;decisionTimestamp=now;decisionClockMode=State.mode;
            decisionClockInstance=State.modeInstanceId;decisionClockEncounter=State.encounterId;decisionClockEvent=eventId;
        }
        void SynchronizeDecisionClockBoundary()
        {
            if(!DecisionClockAvailable){SuspendDecisionClock();return;}
            string eventId=DecisionEventId();
            if(!MatchesDecisionSegment(eventId))BeginDecisionSegment(eventId,System.Diagnostics.Stopwatch.GetTimestamp());
        }
        float SampleDecisionDelta()
        {
            if(!DecisionClockAvailable){SuspendDecisionClock();return 0;}
            long now=System.Diagnostics.Stopwatch.GetTimestamp();string eventId=DecisionEventId();
            if(!MatchesDecisionSegment(eventId)){BeginDecisionSegment(eventId,now);return 0;}
            float delta=(float)((now-decisionTimestamp)/(double)System.Diagnostics.Stopwatch.Frequency);decisionTimestamp=now;
            return delta>0?delta:0;
        }
    }
}
