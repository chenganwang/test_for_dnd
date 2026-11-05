using System.Collections.Generic;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        readonly Dictionary<string,float> dodgeMotionAtTickStart=new Dictionary<string,float>(3);
        int dodgeMotionFrame=-1;
        void CaptureDodgeMotionWindow()
        {
            dodgeMotionAtTickStart.Clear();dodgeMotionFrame=Time.frameCount;
            foreach(var actor in State.actors)
                if(actor.dodgeRemaining>0)dodgeMotionAtTickStart.Add(actor.id,actor.dodgeRemaining);
        }
        float DodgeMovementSeconds(ActorState actor,float dt)
        {
            // TickCore expires timers before movement. The final (possibly slow) frame
            // still owns the part of the dodge that existed at its start. Clip distance
            // to that interval: never turn a long frame into zero motion or a huge leap.
            float remaining=actor.dodgeRemaining;
            if(dodgeMotionFrame==Time.frameCount&&dodgeMotionAtTickStart.TryGetValue(actor.id,out var before))remaining=before;
            return Mathf.Min(dt,remaining);
        }
    }
}
