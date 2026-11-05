using System;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public enum DiceAnimationSpeed { Standard, Fast, UltraFast }

    public sealed partial class BattleWorld
    {
        DiceAnimationSpeed diceAnimationSpeed;
        bool automaticRollClockRunning;
        long automaticRollTimestamp;
        int automaticRollFrame;
        string automaticRollTicket;
        ulong automaticRollDraw;
        void SuspendRollAnimationClock() { automaticRollClockRunning=false; }
        void AdvanceAutomaticRollAnimation()
        {
            var ticket=State.pendingRoll;
            if(State.mode!=CombatMode.RollFreeze||ticket==null){SuspendRollAnimationClock();return;}
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            // Establish a fresh baseline only after Confirm/Resume has returned. The current
            // Unity delta belongs to a frame that may include synchronous persistence work.
            if(!automaticRollClockRunning||automaticRollTicket!=ticket.id||automaticRollDraw!=ticket.rngAfter||rollingTicket!=ticket.id)
            {
                automaticRollClockRunning=true;automaticRollTimestamp=now;automaticRollFrame=Time.frameCount;
                automaticRollTicket=ticket.id;automaticRollDraw=ticket.rngAfter;
                AdvanceRollPresentation(0);return;
            }
            if(automaticRollFrame==Time.frameCount)return;
            float elapsed=(float)((now-automaticRollTimestamp)/(double)System.Diagnostics.Stopwatch.Frequency);
            automaticRollTimestamp=now;automaticRollFrame=Time.frameCount;
            AdvanceRollPresentation(Mathf.Max(0,elapsed));
        }
        public DiceAnimationSpeed DiceSpeed => diceAnimationSpeed;
        // Existing integrations can still select the original two speed options.
        public bool QuickDice
        {
            get => diceAnimationSpeed != DiceAnimationSpeed.Standard;
            set => diceAnimationSpeed = value ? DiceAnimationSpeed.Fast : DiceAnimationSpeed.Standard;
        }
        public string DiceSpeedLabel => DiceSpeed == DiceAnimationSpeed.Standard ? "标准" : DiceSpeed == DiceAnimationSpeed.Fast ? "快速" : "极快";
        public float CurrentRollDuration
        {
            get
            {
                bool pair = Session != null && State.pendingRoll != null && State.pendingRoll.rawFaces.Count == 2;
                float single = DiceSpeed == DiceAnimationSpeed.Standard ? Presentation.rollAnimationSeconds : DiceSpeed == DiceAnimationSpeed.Fast ? Presentation.quickRollSeconds : Presentation.ultraRollSeconds;
                float extra = DiceSpeed == DiceAnimationSpeed.Standard ? Presentation.dualRollExtraSeconds : DiceSpeed == DiceAnimationSpeed.Fast ? Presentation.quickDualRollExtraSeconds : Presentation.ultraDualRollExtraSeconds;
                return Mathf.Max(.05f, single + (pair ? extra : 0));
            }
        }
        public void SetDiceAnimationSpeed(DiceAnimationSpeed speed)
        {
            if (!Enum.IsDefined(typeof(DiceAnimationSpeed), speed)) throw new ArgumentOutOfRangeException(nameof(speed));
            var previous = diceAnimationSpeed;
            bool hadSpeed = State.world.numbers.TryGetValue("diceAnimationSpeed", out float previousSpeed);
            bool hadQuick = State.world.numbers.TryGetValue("quickDice", out float previousQuick);
            diceAnimationSpeed = speed;
            State.world.numbers["diceAnimationSpeed"] = (int)speed;
            State.world.numbers["quickDice"] = QuickDice ? 1 : 0;
            var saved = Session.Save();
            if (saved.success) return;
            diceAnimationSpeed = previous;
            if (hadSpeed) State.world.numbers["diceAnimationSpeed"] = previousSpeed; else State.world.numbers.Remove("diceAnimationSpeed");
            if (hadQuick) State.world.numbers["quickDice"] = previousQuick; else State.world.numbers.Remove("quickDice");
            Message = "设置未保存：" + saved.reason;
        }
        void RestoreDiceAnimationPreference()
        {
            if (State.world.numbers.TryGetValue("diceAnimationSpeed", out float speed) && speed >= 0 && speed <= 2 && speed == Mathf.Floor(speed))
                diceAnimationSpeed = (DiceAnimationSpeed)(int)speed;
            else QuickDice = State.world.numbers.TryGetValue("quickDice", out float quick) && quick > .5f;
        }
    }
}
