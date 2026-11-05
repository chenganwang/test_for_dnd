using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.Audio;

namespace CombatV051.Runtime
{
    /// <summary>Read-only presentation observer. Never advances gameplay RNG or changes combat state.</summary>
    public sealed partial class BattleAudio : MonoBehaviour
    {
        sealed class Voice { public AudioSource source; public bool paused; }
        BattleWorld world;
        BattleAudioConfig config = new BattleAudioConfig();
        AudioPreferences preferences = new AudioPreferences();
        AudioMixer mixer;
        AudioMixerGroup musicGroup, sfxGroup, uiGroup, ambienceGroup;
        AudioSource explore, combat, ambience;
        AudioLowPassFilter exploreFilter, combatFilter, ambienceFilter;
        readonly Dictionary<AudioCue, AudioClip> clips = new Dictionary<AudioCue, AudioClip>();
        readonly Dictionary<AudioCue, float> lastCueTime = new Dictionary<AudioCue, float>();
        readonly Dictionary<AudioCue, int> played = new Dictionary<AudioCue, int>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly Queue<string> seenOrder = new Queue<string>();
        readonly List<Voice> voices = new List<Voice>();
        readonly Dictionary<string, int> hitPoints = new Dictionary<string, int>();
        readonly Dictionary<string, float> stagger = new Dictionary<string, float>();
        readonly HashSet<string> incomingApplied = new HashSet<string>();
        string encounter, ticket, activeId;
        int charges;
        CombatMode previousMode;
        Vector3 footPosition;
        float walked, blend, duck = 1, lowPass = 22000;
        bool initialized, started, baseline, worldPaused;
        int nextVoice;
        string animatedTicket,animationCueRoot;
        ulong animatedDraw;
        bool animationRollPlayed,animationLandPlayed,animationOutcomePlayed;

        public float MasterVolume => preferences.master;
        public float MusicVolume => preferences.music;
        public float SfxVolume => preferences.sfx;
        public string SettingsPath { get; private set; }
        public BattleMusicState CurrentMusicState { get; private set; }
        public BattleMixState CurrentMixState { get; private set; }
        public bool MixerReady => mixer != null && musicGroup != null && sfxGroup != null && uiGroup != null && ambienceGroup != null;
        public float MusicBlend => blend;
        public float MusicDuck => duck;
        public bool WorldVoicesPaused => worldPaused;
        public int MusicStartCount { get; private set; }
        public int MissingClipRequests { get; private set; }
        public string LastSettingsError { get; private set; }
        public AudioMixer Mixer => mixer;
        public int ClipCount => clips.Count;
        public double InitializationMilliseconds { get; private set; }
        public int PlayedCount(AudioCue cue) => played.TryGetValue(cue, out int count) ? count : 0;
        public bool HasClip(AudioCue cue) => clips.TryGetValue(cue, out var clip) && clip != null;
        public AudioClip ClipFor(AudioCue cue) => clips.TryGetValue(cue, out var clip) ? clip : null;

        public void Initialize(BattleWorld owner, string settingsPathOverride = null)
        {
            if (initialized)
            {
                world = owner;
                if (settingsPathOverride != null && SettingsPath != settingsPathOverride)
                {
                    SettingsPath = settingsPathOverride; LoadPreferences(); if (started) ApplyVolumes();
                }
                ResetObservationBaseline(); return;
            }
            world = owner;
            long initializationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            SettingsPath = settingsPathOverride ?? Path.Combine(Application.persistentDataPath, "CombatV051", "Presentation", "audio.json");
            var declared = Resources.Load<TextAsset>("Audio/BattleAudioConfig");
            if (declared != null) config = JsonUtility.FromJson<BattleAudioConfig>(declared.text) ?? new BattleAudioConfig();
            LoadPreferences();
            mixer = Resources.Load<AudioMixer>("Audio/P3AudioMixer");
            if (mixer != null)
            {
                mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
                musicGroup = Group("Music"); sfxGroup = Group("SFX"); uiGroup = Group("UI"); ambienceGroup = Group("Ambience");
            }
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                var clip = Resources.Load<AudioClip>("Audio/SFX/" + cue);
                if (clip != null) clips.Add(cue, clip);
            }
            explore = NewSource("Exploration loop", musicGroup, false);
            combat = NewSource("Combat loop", musicGroup, false);
            ambience = NewSource("Dungeon air", ambienceGroup, false);
            explore.loop = combat.loop = ambience.loop = true;
            explore.clip = Resources.Load<AudioClip>(config.exploration);
            combat.clip = Resources.Load<AudioClip>(config.combat);
            ambience.clip = Resources.Load<AudioClip>("Audio/Ambience/room-air");
            exploreFilter = explore.gameObject.AddComponent<AudioLowPassFilter>();
            combatFilter = combat.gameObject.AddComponent<AudioLowPassFilter>();
            ambienceFilter = ambience.gameObject.AddComponent<AudioLowPassFilter>();
            for (int i = 0; i < 20; i++) voices.Add(new Voice { source = NewSource("World voice " + i, sfxGroup, true) });
            for (int i = 0; i < 8; i++) voices.Add(new Voice { source = NewSource("UI voice " + i, uiGroup, false) });
            InitializeDiceSound();
            initialized = true;
            ResetObservationBaseline();
            // Mixer SetFloat must run in Start or later, not in Awake/Initialize.
            if (started) StartPlayback();
            InitializationMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp()-initializationStarted)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }

        AudioMixerGroup Group(string name) => mixer.FindMatchingGroups(name).FirstOrDefault(g => g.name == name);
        AudioSource NewSource(string name, AudioMixerGroup group, bool spatial)
        {
            var child = new GameObject(name); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.outputAudioMixerGroup = group;
            source.volume = 0; source.spatialBlend = spatial ? .35f : 0;
            source.dopplerLevel = 0; source.minDistance = 5; source.maxDistance = 32;
            source.rolloffMode = AudioRolloffMode.Linear; source.ignoreListenerPause = !spatial;
            return source;
        }
        void Start() { started = true; if (initialized) StartPlayback(); }
        void StartPlayback()
        {
            ApplyVolumes();
            if (!MixerReady) return; // Fail silent if the asset is missing; never silently route everything to Master.
            double when = AudioSettings.dspTime + .08;
            if (explore.clip != null && !explore.isPlaying) { explore.PlayScheduled(when); MusicStartCount++; }
            if (combat.clip != null && !combat.isPlaying) { combat.PlayScheduled(when); MusicStartCount++; }
            if (ambience.clip != null && !ambience.isPlaying) ambience.PlayScheduled(when);
        }

        public void SetVolumes(float master, float music, float sfx, bool persist = true)
        {
            preferences.master = SafeVolume(master); preferences.music = SafeVolume(music); preferences.sfx = SafeVolume(sfx);
            if (started) ApplyVolumes();
            if (persist) SavePreferences();
        }
        static float SafeVolume(float value) => float.IsNaN(value) || float.IsInfinity(value) ? .5f : Mathf.Clamp01(value);
        public static float ToDecibels(float linear) => linear <= .0001f ? -80 : Mathf.Log10(Mathf.Clamp01(linear)) * 20;
        void ApplyVolumes()
        {
            if (!MixerReady) return;
            mixer.SetFloat("MasterVolume", ToDecibels(preferences.master));
            mixer.SetFloat("MusicVolume", ToDecibels(preferences.music));
            mixer.SetFloat("SfxVolume", ToDecibels(preferences.sfx));
        }
        void LoadPreferences()
        {
            preferences = new AudioPreferences();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var loaded = JsonUtility.FromJson<AudioPreferences>(File.ReadAllText(SettingsPath));
                    if (loaded != null && loaded.schema == 1) preferences = loaded;
                }
            }
            catch (Exception e) { LastSettingsError = e.GetType().Name; }
            preferences.master = SafeVolume(preferences.master); preferences.music = SafeVolume(preferences.music); preferences.sfx = SafeVolume(preferences.sfx);
        }
        void SavePreferences()
        {
            if (string.IsNullOrEmpty(SettingsPath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SettingsPath)));
                string temp = SettingsPath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(preferences, true));
                if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, null); else File.Move(temp, SettingsPath);
                LastSettingsError = null;
            }
            catch (Exception e) { LastSettingsError = e.GetType().Name; }
        }

        public bool PlayCue(string cue, Vector3 position = default, string sourceId = null)
            => Enum.TryParse(cue, true, out AudioCue parsed) && Enum.IsDefined(typeof(AudioCue), parsed) && PlayCue(parsed, position, sourceId);
        public bool PlayCue(AudioCue cue, Vector3 position = default, string sourceId = null,float gain=1)
        {
            if (!initialized || !HasClip(cue)) { MissingClipRequests++; return false; }
            bool ui = IsUi(cue);
            if (!ui && worldPaused) return false;
            if (!string.IsNullOrEmpty(sourceId) && !Remember(cue + "/" + sourceId)) return false;
            float now = Time.unscaledTime;
            float cooldown = cue == AudioCue.UiHover || cue == AudioCue.MenuHover ? .07f : cue == AudioCue.Footstep ? .12f : .025f;
            if (lastCueTime.TryGetValue(cue, out float last) && now - last < cooldown) return false;
            if (!MixerReady) return false;
            lastCueTime[cue] = now;
            int start = ui ? 20 : 0, count = ui ? 8 : 20;
            var voice = voices.Skip(start).Take(count).FirstOrDefault(v => !v.source.isPlaying && !v.paused) ?? voices[start + (nextVoice++ % count)];
            voice.source.Stop(); voice.paused = false;
            voice.source.transform.position = position;
            voice.source.pitch = 1; // No gameplay random stream and no pitch change with Time.timeScale.
            voice.source.clip = clips[cue]; voice.source.volume = Mathf.Clamp01((ui ? config.uiGain : config.worldGain)*Mathf.Clamp(gain,0,1.5f));
            voice.source.Play();
            played[cue] = PlayedCount(cue) + 1;
            return true;
        }
        public static bool IsUi(AudioCue cue) => (int)cue <= (int)AudioCue.ActionDiceEarn || cue == AudioCue.ObjectiveUpdate || cue==AudioCue.DiceThrow || cue==AudioCue.DiceBounce || cue==AudioCue.NaturalOne || cue==AudioCue.MenuHover || cue==AudioCue.MenuSelect || cue==AudioCue.MenuConfirm || cue==AudioCue.MenuBack || cue==AudioCue.MapSelect || cue==AudioCue.CharacterSelect || cue==AudioCue.FocusRecovered;
        bool Remember(string key)
        {
            if (!seen.Add(key)) return false;
            seenOrder.Enqueue(key);
            if (seenOrder.Count > 4096) seen.Remove(seenOrder.Dequeue());
            return true;
        }

        public void ObserveCommand(CombatCommand command, CombatResult result)
        {
            if (command == null || result == null || !result.success || result.duplicate) return;
            string id = command.id;
            Vector3 pos = world?.Session == null ? Vector3.zero : BattleWorld.Position(world.State.Actor(command.actorId));
            switch (command.kind)
            {
                case CommandKind.BasicAttack:
                case CommandKind.StartBasicAttack:
                    PlayCue(world?.State.Actor(command.actorId)?.role == ActorRole.Mage ? AudioCue.SpellCast : AudioCue.BasicAttack, pos, id);

                    break;
                case CommandKind.ResolveBasicAttack:
                    if(result.reason=="basic_swing_missed") PlayCue(AudioCue.UiCancel,pos,id);
                    break;
                case CommandKind.StartDodge: PlayCue(AudioCue.Dodge, pos, id); break;
                case CommandKind.StartQueued:
                    var role = world?.State.Actor(command.actorId)?.role;
                    PlayCue(role == ActorRole.Mage ? AudioCue.SpellCast : AudioCue.BasicAttack, pos, id); break;
                case CommandKind.SwitchActive: PlayCue(AudioCue.UiConfirm, pos, id); break;
            }
        }

        public void ObserveRollAnimation(RollTicket roll,float progress)
        {
            if(roll==null||!initialized)return;
            if(animatedTicket!=roll.id||animatedDraw!=roll.rngAfter)
            {
                animatedTicket=roll.id;animatedDraw=roll.rngAfter;
                animationCueRoot=roll.id+"/draw/"+roll.rngAfter;
                animationRollPlayed=animationLandPlayed=animationOutcomePlayed=false;
                ResetDiceSoundDraw();
            }
            AdvanceDiceSound(roll,Mathf.Clamp01(progress));
        }

        public void ResetObservationBaseline(bool resetImpact=true)
        {
            if (world?.Session == null) { baseline = false; return; }
            var state = world.State;
            encounter = state.encounterId; previousMode = state.mode; ticket = state.pendingRoll?.id;
            charges = state.charges; activeId = state.activeActorId; footPosition = BattleWorld.Position(world.ActiveActor); walked = 0;
            hitPoints.Clear(); stagger.Clear(); incomingApplied.Clear();
            foreach (var actor in state.actors.Concat(state.enemies)) { hitPoints[actor.id] = actor.hp; stagger[actor.id] = actor.staggerRemaining; }
            foreach (var incoming in state.incomingEvents) if (incoming.damageApplied) incomingApplied.Add(incoming.id);
            foreach (var enemy in state.enemies) if (enemy.aiState == 1 && enemy.attackGroupId != null) Remember("telegraph/" + enemy.attackGroupId);
            baseline = true;if(resetImpact)world?.ImpactFeedback?.ResetObservation();
        }
        void LateUpdate() { using (P46Performance.Measure(9)) LateUpdateMeasured(); }
        void LateUpdateMeasured()
        {
            if (!initialized) return;
            if (world?.Session != null)
            {
                if (!baseline || encounter != world.State.encounterId) ResetObservationBaseline(false);
                ObserveChanges();
                bool inCombat = !world.Menu && world.State.inCombat && !world.State.encounterResolved;
                CurrentMusicState = inCombat ? BattleMusicState.Combat : BattleMusicState.Exploration;
                CurrentMixState = world.Menu || world.DungeonModal || world.State.mode == CombatMode.CombatPaused ? BattleMixState.Paused :
                    world.State.mode == CombatMode.RollFreeze ? BattleMixState.RollFreeze : world.IsFocus ? BattleMixState.Focus : BattleMixState.Realtime;
            }
            UpdateMix(Time.unscaledDeltaTime);
            UpdateDiceSoundPause();
        }
        void ObserveChanges()
        {
            var state = world.State;
            if (state.mode != previousMode)
            {
                if (state.mode == CombatMode.TacticalFocus && previousMode != CombatMode.PostRollDecision) PlayCue(AudioCue.TacticalEnter);
                else if (previousMode == CombatMode.TacticalFocus && (state.mode == CombatMode.QueuedExecution || state.mode == CombatMode.RealtimeCombat)) PlayCue(AudioCue.TacticalExit);
                previousMode = state.mode;
            }
            if (state.charges > charges) PlayCue(AudioCue.ActionDiceEarn, sourceId: state.encounterId + "/earn/" + state.revision + "/" + state.charges);
            charges = state.charges; ticket = state.pendingRoll?.id;
            foreach (var actor in state.actors.Concat(state.enemies))
            {


                hitPoints[actor.id] = actor.hp; stagger[actor.id] = actor.staggerRemaining;
                if (actor.isEnemy && actor.aiState == 1 && actor.attackGroupId != null && Remember("telegraph/" + actor.attackGroupId)) PlayCue(AudioCue.EnemyTelegraph, BattleWorld.Position(actor));
            }
            var current = BattleWorld.Position(world.ActiveActor);
            if (activeId != state.activeActorId) { activeId = state.activeActorId; walked = 0; footPosition = current; }
            float step = Vector3.Distance(footPosition, current); footPosition = current;
            if (!world.IsFrozen && step < 1) walked += step; else walked = 0;
            if (walked >= config.footstepDistance) { walked = 0; PlayCue(AudioCue.Footstep, current); }
        }
        void UpdateMix(float dt)
        {
            bool freezeWorld = CurrentMixState == BattleMixState.RollFreeze || CurrentMixState == BattleMixState.Paused;
            if (worldPaused != freezeWorld)
            {
                worldPaused = freezeWorld;
                for (int i = 0; i < 20; i++)
                {
                    var v = voices[i];
                    if (worldPaused && v.source.isPlaying) { v.source.Pause(); v.paused = true; }
                    else if (!worldPaused && v.paused) { v.source.UnPause(); v.paused = false; }
                }
            }
            blend = Mathf.MoveTowards(blend, CurrentMusicState == BattleMusicState.Combat ? 1 : 0, dt / Mathf.Max(.05f, config.crossfadeSeconds));
            float desiredDuck = CurrentMixState == BattleMixState.RollFreeze ? config.freezeDuck : CurrentMixState == BattleMixState.Focus ? config.focusDuck : CurrentMixState == BattleMixState.Paused ? config.pauseDuck : 1;
            float desiredCutoff = CurrentMixState == BattleMixState.RollFreeze ? config.freezeLowPass : CurrentMixState == BattleMixState.Focus ? config.focusLowPass : config.normalLowPass;
            float factor = 1 - Mathf.Exp(-dt * (CurrentMixState == BattleMixState.RollFreeze ? 35 : 8));
            duck = Mathf.Lerp(duck, desiredDuck, factor); lowPass = Mathf.Lerp(lowPass, desiredCutoff, factor);
            if (explore == null) return;
            explore.volume = Mathf.Cos(blend * Mathf.PI * .5f) * config.musicGain * duck;
            combat.volume = Mathf.Sin(blend * Mathf.PI * .5f) * config.musicGain * duck;
            ambience.volume = config.ambienceGain * duck;
            exploreFilter.cutoffFrequency = combatFilter.cutoffFrequency = ambienceFilter.cutoffFrequency = lowPass;
        }
    }
}
