using System;
using System.IO;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    [Serializable] public sealed class P4PlayerPreferences
    {
        public int schema = 1;
        public float focusScale = .1f;
        public bool quickDice;
        public bool v12FreeAssistance=true;
        public bool reduceMotion;
        public string diceAnimationSpeed;
        public string screenShake="Normal",hitStopIntensity="Normal";
        public ReactionSettings reactions = new ReactionSettings();
    }
    public sealed partial class BattleWorld
    {
        P4PlayerPreferences p4Preferences = new P4PlayerPreferences();
        bool hasP4Preferences;
        bool applyingP4Preferences;
        string PreferencesPath => Path.Combine(Path.GetDirectoryName(SavePath), "p4-player-settings.json");
        public ReactionSettings ReactionPreferences => p4Preferences.reactions;
        static FeedbackIntensity FeedbackSetting(string value)=>Enum.TryParse<FeedbackIntensity>(value,out var parsed)&&Enum.IsDefined(typeof(FeedbackIntensity),parsed)?parsed:FeedbackIntensity.Normal;
        public FeedbackIntensity ShakeSetting=>FeedbackSetting(p4Preferences.screenShake);
        public FeedbackIntensity HitStopSetting=>FeedbackSetting(p4Preferences.hitStopIntensity);
        public bool ReduceMotion=>p4Preferences.reduceMotion;
        public void ConfigureReduceMotion(bool enabled)
        {
            var value=CombatJson.Clone(p4Preferences);value.reduceMotion=enabled;
            if(SaveP4Preferences(value))Hud?.RefreshView();
        }
        public void ConfigureImpactSettings(FeedbackIntensity shake,FeedbackIntensity stop)
        {
            if(!Enum.IsDefined(typeof(FeedbackIntensity),shake)||!Enum.IsDefined(typeof(FeedbackIntensity),stop))throw new ArgumentOutOfRangeException();
            var value=CombatJson.Clone(p4Preferences);value.screenShake=shake.ToString();value.hitStopIntensity=stop.ToString();
            if(SaveP4Preferences(value))ImpactFeedback?.CancelTransient();
        }
        void LoadP4Preferences()
        {
            hasP4Preferences = false; p4Preferences = new P4PlayerPreferences(); QuickDice=false;
            try
            {
                if (!File.Exists(PreferencesPath)) return;
                var value = CombatJson.Read<P4PlayerPreferences>(File.ReadAllText(PreferencesPath));
                if (value.schema != 1 || value.reactions == null || float.IsNaN(value.reactions.decisionSeconds) || value.reactions.decisionSeconds <= 0 || value.reactions.decisionSeconds > 60 ||
                    (value.focusScale != 0 && value.focusScale != .1f && value.focusScale != .15f)) throw new InvalidDataException("invalid_settings");
                if (string.IsNullOrEmpty(value.diceAnimationSpeed)) value.diceAnimationSpeed = value.quickDice ? "Fast" : "Standard";
                if (!Enum.TryParse<DiceAnimationSpeed>(value.diceAnimationSpeed, out var speed) || !Enum.IsDefined(typeof(DiceAnimationSpeed), speed)) throw new InvalidDataException("invalid_dice_speed");
                value.reactions.decisionSeconds = Math.Min(FocusBudgetRules.ReactionDecisionSeconds,value.reactions.decisionSeconds);
                value.reactions.pauseDecision = false;
                p4Preferences = value; hasP4Preferences = true;
            }
            catch (Exception error) { Message = "设置读取失败，采用默认设置并保留原文件：" + error.Message; }
        }
        bool SaveP4Preferences(P4PlayerPreferences candidate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
                string tmp = PreferencesPath + ".tmp";
                File.WriteAllText(tmp, CombatJson.Write(candidate));
                if (File.Exists(PreferencesPath)) File.Replace(tmp, PreferencesPath, PreferencesPath + ".bak");
                else File.Move(tmp, PreferencesPath);
                p4Preferences = candidate; hasP4Preferences = true; ApplyP4Preferences(); return true;
            }
            catch (Exception error) { Message = "设置未保存：" + error.Message; return false; }
        }
        void ApplyP4Preferences()
        {
            if (!hasP4Preferences || Session == null || applyingP4Preferences) return;
            applyingP4Preferences = true;
            try
            {
            if (!P6Enabled && Math.Abs(Session.Config.focusScale - p4Preferences.focusScale) > .001f) SetFocusScale(p4Preferences.focusScale);
            var speed = PreferenceDiceSpeed(p4Preferences);
            if (DiceSpeed != speed) SetDiceAnimationSpeed(speed);
            var reactions = EffectiveReactionPreferences();
            if (State.IsPartyP1 && State.mode!=CombatMode.CombatPaused && CombatJson.Write(State.reactionSettings) != CombatJson.Write(reactions))
                ExecuteWorldCommand(new CombatCommand { id = Guid.NewGuid().ToString("N"), kind = CommandKind.SetReactionSettings, reactionSettings = reactions });
            }
            finally { applyingP4Preferences = false; }
        }
        public void ConfigureFocus(float scale)
        { if(P6Enabled){Message="誓光大剑：决策慢时固定 ×0.12，反应独立。";return;} var value = CombatJson.Clone(p4Preferences); value.focusScale = scale; SaveP4Preferences(value); }
        public void ConfigureQuickDice()
        { ConfigureDiceAnimationSpeed((DiceAnimationSpeed)(((int)DiceSpeed + 1) % 3)); }
        public void ConfigureDiceAnimationSpeed(DiceAnimationSpeed speed)
        {
            if (!Enum.IsDefined(typeof(DiceAnimationSpeed), speed)) throw new ArgumentOutOfRangeException(nameof(speed));
            var value = CombatJson.Clone(p4Preferences); value.diceAnimationSpeed = speed.ToString(); value.quickDice = speed != DiceAnimationSpeed.Standard; SaveP4Preferences(value);
        }
        static DiceAnimationSpeed PreferenceDiceSpeed(P4PlayerPreferences value)
        {
            return Enum.TryParse<DiceAnimationSpeed>(value.diceAnimationSpeed, out var speed) && Enum.IsDefined(typeof(DiceAnimationSpeed), speed) ? speed : value.quickDice ? DiceAnimationSpeed.Fast : DiceAnimationSpeed.Standard;
        }
        public void CycleReactionMode()
        {
            var value = CombatJson.Clone(p4Preferences);
            value.reactions.autoMode = (ReactionAutoMode)(((int)value.reactions.autoMode + 1) % 3);
            SaveP4Preferences(value);
        }
        public void CycleReactionAssistance()
        {
            var value = CombatJson.Clone(p4Preferences);
            value.reactions.pauseDecision = false; value.reactions.decisionSeconds = FocusBudgetRules.ReactionDecisionSeconds;
            value.reactions.timeoutAcceptPaidRoll = !value.reactions.timeoutAcceptPaidRoll;
            SaveP4Preferences(value);
        }
    }
}
