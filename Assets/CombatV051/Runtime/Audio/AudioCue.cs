using System;

namespace CombatV051.Runtime
{
    public enum AudioCue
    {
        UiHover, UiConfirm, UiCancel, TacticalEnter, TacticalExit,
        DiceRoll, DiceLand, Success, Failure, Critical, ActionDiceEarn,
        BasicAttack, Hit, Block, Parry, Dodge, PerfectDodge,
        SpellCast, SpellImpact, EnemyTelegraph, EnemyHit, Break,
        Door, BreakableDoor, Interact, Footstep, ObjectiveUpdate,
        DiceThrow, DiceBounce, NaturalOne, MenuHover, MenuSelect, MenuConfirm, MenuBack, MapSelect, CharacterSelect, FocusRecovered
    }
    public enum BattleMusicState { Exploration, Combat }
    public enum BattleMixState { Realtime, Focus, RollFreeze, Paused }

    [Serializable]
    public sealed class AudioPreferences
    {
        public int schema = 1;
        public float master = .8f, music = .65f, sfx = .85f;
    }

    [Serializable]
    public sealed class BattleAudioConfig
    {
        public string exploration = "Audio/Music/exploration", combat = "Audio/Music/combat";
        public float crossfadeSeconds = 2, focusDuck = .55f, freezeDuck = .2f, pauseDuck = .4f;
        public float focusLowPass = 4200, freezeLowPass = 1600, normalLowPass = 22000;
        public float musicGain = .48f, ambienceGain = .06f, worldGain = .65f, uiGain = .6f;
        public float footstepDistance = 1.1f;
    }
}
