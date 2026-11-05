using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    public sealed class P3AudioTests
    {
        BattleWorld world;
        BattleAudio audio;
        string directory;
        [UnitySetUp] public IEnumerator Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "V051-P3-Audio", Guid.NewGuid().ToString("N"));
            world = new GameObject("P3 actual audio test world").AddComponent<BattleWorld>();
            world.InitializePartyForTests(Path.Combine(directory, "party.json"));
            world.Controls.Map.Disable();
            world.State.enemies[0].x = 1.5f; world.State.enemies[0].z = 0; world.State.enemies[0].aiRemaining = 100;
            foreach (var actor in world.State.actors) if (actor.id != "fighter") actor.majorRemaining = 100;
            audio = world.GetComponent<BattleAudio>() ?? world.gameObject.AddComponent<BattleAudio>();
            audio.Initialize(world, Path.Combine(directory, "audio.json"));
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (world != null) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            Time.timeScale = 1;
        }
        [UnityTest] public IEnumerator ActualMixerHasFiveGroupsAndEverySourceHasExplicitNonMasterRoute()
        {
            Assert.That(audio.MixerReady, Is.True, "Run P3AudioBuilder.PrepareAudio before PlayMode: fake group names are not routing.");
            CollectionAssert.AreEquivalent(new[] { "Master", "Music", "SFX", "UI", "Ambience" }, audio.Mixer.FindMatchingGroups("").Select(g => g.name));
            Assert.That(audio.Mixer.updateMode, Is.EqualTo(AudioMixerUpdateMode.UnscaledTime));
            foreach (var source in audio.GetComponentsInChildren<AudioSource>())
            {
                Assert.That(source.outputAudioMixerGroup, Is.Not.Null, source.name);
                Assert.That(source.outputAudioMixerGroup.name, Is.Not.EqualTo("Master"), source.name);
            }
            foreach (string parameter in new[] { "MasterVolume", "MusicVolume", "SfxVolume" }) Assert.That(audio.Mixer.GetFloat(parameter, out _), Is.True, parameter);
            yield return null;
        }
        [UnityTest] public IEnumerator EveryRequiredCueHasAnImportedClipAndBothLoopsExceedNinetySeconds()
        {
            Assert.That(audio.ClipCount, Is.EqualTo(Enum.GetValues(typeof(AudioCue)).Length),"All original P3 and added P47 cues must be real preloaded assets.");
            foreach (AudioCue cue in Enum.GetValues(typeof(AudioCue)))
            {
                Assert.That(audio.HasClip(cue), Is.True, cue.ToString());
                Assert.That(audio.ClipFor(cue).length, Is.GreaterThan(.02f));
            }
            foreach (string music in new[] { "exploration", "combat" }) Assert.That(Resources.Load<AudioClip>("Audio/Music/" + music).length, Is.GreaterThan(90));
            yield return null;
        }
        [UnityTest] public IEnumerator MissingClipOrUnknownCueDoesNotThrowOrStartAVoice()
        {
            var catalog = (Dictionary<AudioCue, AudioClip>)typeof(BattleAudio).GetField("clips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(audio);
            catalog.Remove(AudioCue.Interact); // Explicit missing-reference fixture, not an unknown-name-only test.
            int voices = audio.PlayedCount(AudioCue.Interact);
            Assert.DoesNotThrow(() => Assert.That(audio.PlayCue(AudioCue.Interact), Is.False));
            Assert.That(audio.PlayCue("not-a-real-cue"), Is.False);
            Assert.That(audio.PlayedCount(AudioCue.Interact), Is.EqualTo(voices));
            Assert.That(audio.MissingClipRequests, Is.GreaterThan(0));
            yield return null;
        }
        [UnityTest] public IEnumerator ThreeVolumesPersistOutsideGameplayProfileAndDriveMixerParameters()
        {
            audio.SetVolumes(.4f, .3f, .2f);
            Assert.That(File.Exists(audio.SettingsPath), Is.True);
            Assert.That(audio.LastSettingsError, Is.Null);
            audio.Mixer.GetFloat("MasterVolume", out float db);
            Assert.That(db, Is.EqualTo(BattleAudio.ToDecibels(.4f)).Within(.01f));
            var holder = new GameObject("Second audio preferences reader");
            var restored = holder.AddComponent<BattleAudio>(); restored.Initialize(null, audio.SettingsPath);
            Assert.That(restored.MasterVolume, Is.EqualTo(.4f)); Assert.That(restored.MusicVolume, Is.EqualTo(.3f)); Assert.That(restored.SfxVolume, Is.EqualTo(.2f));
            UnityEngine.Object.Destroy(holder);
            yield return null;
        }
        [UnityTest] public IEnumerator InvalidPreferencesFallbackAndZeroVolumeIsFiniteSilence()
        {
            string other = Path.Combine(directory, "corrupt.json"); Directory.CreateDirectory(directory); File.WriteAllText(other, "broken{");
            audio.Initialize(world, other);
            Assert.That(audio.MasterVolume, Is.EqualTo(.8f));
            audio.SetVolumes(0, float.NaN, float.PositiveInfinity, false);
            Assert.That(audio.MasterVolume, Is.Zero); Assert.That(audio.MusicVolume, Is.EqualTo(.5f));
            Assert.That(BattleAudio.ToDecibels(0), Is.EqualTo(-80));
            yield return null;
        }
        [UnityTest] public IEnumerator RepeatedCommandsAndResetBaselineDoNotReplayEarnOrHistoricalHits()
        {
            var command = new CombatCommand { id = "audio-owned-dodge", kind = CommandKind.StartDodge, actorId = "fighter" };
            audio.ObserveCommand(command, new CombatResult { success = true });
            int count = audio.PlayedCount(AudioCue.Dodge);
            audio.ObserveCommand(command, new CombatResult { success = true, duplicate = true });
            audio.ObserveCommand(command, new CombatResult { success = true });
            Assert.That(audio.PlayedCount(AudioCue.Dodge), Is.EqualTo(count));
            int earns = audio.PlayedCount(AudioCue.ActionDiceEarn), hits = audio.PlayedCount(AudioCue.EnemyHit);
            world.State.charges = 2; world.State.enemies[0].hp -= 1;
            audio.ResetObservationBaseline();
            yield return null;
            Assert.That(audio.PlayedCount(AudioCue.ActionDiceEarn), Is.EqualTo(earns));
            Assert.That(audio.PlayedCount(AudioCue.EnemyHit), Is.EqualTo(hits));
        }
        [UnityTest] public IEnumerator RealFocusAndFreezeDuckWithoutRestartingLoopsAndUiDiceStillPlay()
        {
            int starts = audio.MusicStartCount;
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(audio.CurrentMixState, Is.EqualTo(BattleMixState.Focus));
            float focusDuck = audio.MusicDuck;
            Assert.That(focusDuck, Is.LessThan(1));
            world.Presentation.rollAnimationSeconds = 2;
            Assert.That(world.ConfirmCurrentDecision().success, Is.True, world.Message);
            yield return new WaitForSecondsRealtime(.12f);
            Assert.That(audio.CurrentMixState, Is.EqualTo(BattleMixState.RollFreeze));
            Assert.That(audio.WorldVoicesPaused, Is.True);
            Assert.That(audio.MusicDuck, Is.LessThan(focusDuck));
            Assert.That(audio.PlayCue(AudioCue.Hit, sourceId: "freeze-world-prohibited"), Is.False);
            Assert.That(audio.PlayCue(AudioCue.DiceLand, sourceId: "freeze-ui-still-enabled"), Is.True);
            Assert.That(audio.MusicStartCount, Is.EqualTo(starts));
            Assert.That(world.Command(CommandKind.RevealRoll).success, Is.True);
            yield return new WaitForSecondsRealtime(.1f);
            Assert.That(audio.CurrentMixState, Is.EqualTo(BattleMixState.Focus));
            Assert.That(audio.WorldVoicesPaused, Is.False);
            Assert.That(audio.MusicStartCount, Is.EqualTo(starts));
        }
        [UnityTest] public IEnumerator MusicCrossfadeContinuesOnUnscaledClockWithoutReplayingEitherTrack()
        {
            int starts = audio.MusicStartCount;
            Assert.That(world.Command(CommandKind.EnterFocus).success, Is.True);
            float before = audio.MusicBlend;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.That(audio.MusicBlend, Is.GreaterThan(before + .06f), "Music fade uses real time while world is in bullet time.");
            Assert.That(audio.CurrentMusicState, Is.EqualTo(BattleMusicState.Combat));
            world.Menu = true; world.ApplyTime();
            yield return null;
            before = audio.MusicBlend;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(audio.CurrentMusicState, Is.EqualTo(BattleMusicState.Exploration));
            Assert.That(audio.MusicBlend, Is.LessThan(before));
            Assert.That(audio.MusicStartCount, Is.EqualTo(starts));
        }
    }
}
