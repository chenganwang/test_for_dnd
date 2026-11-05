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
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CombatV051.Tests
{
    // Actual menu Update/LateUpdate, Resources, settings and durable profile integration.
    // Isolated engine fixtures; no authored profile, natural combat or playability claims.
    public sealed class P6PreferencesPlayTests
    {
        BattleWorld world;
        Keyboard keyboard;
        Mouse mouse;
        string directory,path;
        int reactionCommits;
        InputSettings.BackgroundBehavior background;
        InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,PrivateInstance).GetValue(target);
        void ApplyPreferences()=>typeof(BattleWorld).GetMethod("ApplyP4Preferences",PrivateInstance).Invoke(world,null);
        void Rebind()=>typeof(BattleWorld).GetMethod("RebindPresentation",PrivateInstance).Invoke(world,null);
        string ReactionLabel()=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name=="reaction-assistance").GetComponentInChildren<Text>(true).text;
        string Gameplay()=>CombatJson.Write(new {world.State.actors,world.State.enemies,world.State.charges,
            world.State.momentum,world.State.momentumLedger,world.State.focusBudget,world.State.p6,
            world.State.simulationSeconds,world.State.random,world.State.rolls,world.State.queue});

        [UnitySetUp] public IEnumerator Setup()
        {
            // NUnit reuses the fixture instance across cases; count only this world's commits.
            reactionCommits=0;
            background=InputSystem.settings.backgroundBehavior;updateMode=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P6-Preferences",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            world=new GameObject("P6 preferences durable menu fixture").AddComponent<BattleWorld>();
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P6-Preferences"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))
                Directory.Delete(directory,true);
        }
        IEnumerator OpenProfile(bool p6)
        {
            if(p6)world.InitializeP6FrontendForTests(path);else world.InitializeFrontendForTests(path);
            world.ChooseFrontend("characters");
            Assert.That(world.Profile,Is.Not.Null,world.Message);Assert.That(world.Menu,Is.True);
            Assert.That(world.P6Enabled,Is.EqualTo(p6));
            yield return null;yield return null;
            world.CommandPublished+=(command,result)=>{if(command.kind==CommandKind.SetReactionSettings)reactionCommits++;};
        }
        IEnumerator StableMenuFrames(int frames=30)
        {
            // The engine really executes Update/LateUpdate between these calls. The
            // explicit apply additionally exercises re-entry from other settings paths.
            for(int i=0;i<frames;i++){world.ApplyTime();ApplyPreferences();yield return null;}
        }

        [UnityTest] public IEnumerator P6RawTwoPointFiveBecomesEffectiveSixOnceAndMenuFramesPreserveProfile()
        {
            yield return OpenProfile(true);
            Assert.That(reactionCommits,Is.Zero,"The new world's preference observation starts empty.");
            // Legal historical state, committed through the existing authority. No
            // direct mutation of the profile, resource balances or random stream.
            var raw=world.Profile.ExecuteCombat(new CombatCommand{id="fixture-raw-reaction",kind=CommandKind.SetReactionSettings,reactionSettings=new ReactionSettings()});
            Assert.That(raw.success,Is.True,raw.reason);Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));
            long before=world.Profile.State.revision;string gameplay=Gameplay(),meta=CombatJson.Write(world.Profile.State.meta);
            Assert.That(reactionCommits,Is.Zero,"The historical raw-settings fixture uses the domain authority, not the observed UI settings path.");
            world.ConfigureReduceMotion(true);
            Assert.That(reactionCommits,Is.EqualTo(1));Assert.That(world.Profile.State.revision,Is.EqualTo(before+1));
            Assert.That(world.ReactionPreferences.decisionSeconds,Is.EqualTo(2.5f));
            Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(6f));
            world.ChooseFrontend("settings");world.Hud.RefreshView();
            Assert.That(ReactionLabel(),Does.StartWith("反应 6秒"),"The settings label must describe the live deadline, not the raw preference.");
            Assert.That(CombatJson.Read<P4PlayerPreferences>(File.ReadAllText(Path.Combine(directory,"p4-player-settings.json"))).reactions.decisionSeconds,Is.EqualTo(2.5f));
            long revision=world.Profile.State.revision,combatRevision=world.State.revision;
            string disk=File.ReadAllText(path);
            yield return StableMenuFrames();
            Assert.That(reactionCommits,Is.EqualTo(1),"Unchanged effective settings cannot append new command receipts.");
            Assert.That(world.Profile.State.revision,Is.EqualTo(revision));Assert.That(world.State.revision,Is.EqualTo(combatRevision));
            Assert.That(File.ReadAllText(path),Is.EqualTo(disk),"Ordinary menu frames must not rewrite the durable profile.");
            Assert.That(Gameplay(),Is.EqualTo(gameplay));Assert.That(CombatJson.Write(world.Profile.State.meta),Is.EqualTo(meta));
        }

        [UnityTest] public IEnumerator RealP6OptionChangesCommitOnceAndPausedChangeWaitsForResume()
        {
            yield return OpenProfile(true);
            world.ConfigureReduceMotion(true);reactionCommits=0;
            string gameplay=Gameplay();long before=world.Profile.State.revision;
            var original=world.State.reactionSettings.autoMode;
            world.CycleReactionMode();
            Assert.That(reactionCommits,Is.EqualTo(1));Assert.That(world.Profile.State.revision,Is.EqualTo(before+1));
            Assert.That(world.State.reactionSettings.autoMode,Is.Not.EqualTo(original));
            Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(world.ReactionPreferences.autoMode));
            yield return StableMenuFrames(12);
            Assert.That(reactionCommits,Is.EqualTo(1));
            world.CycleReactionAssistance();
            Assert.That(reactionCommits,Is.EqualTo(2));Assert.That(world.State.reactionSettings.timeoutAcceptPaidRoll,Is.True);
            Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(6f));
            var pause=world.ExecuteWorldCommand(new CombatCommand{id="fixture-pause",kind=CommandKind.Pause});
            Assert.That(pause.success,Is.True,pause.reason);long pausedRevision=world.Profile.State.revision;
            var pausedMode=world.State.reactionSettings.autoMode;
            world.CycleReactionMode();yield return StableMenuFrames(12);
            Assert.That(reactionCommits,Is.EqualTo(2));Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(pausedMode));
            Assert.That(world.Profile.State.revision,Is.EqualTo(pausedRevision));
            var resume=world.ExecuteWorldCommand(new CombatCommand{id="fixture-resume",kind=CommandKind.Resume});
            Assert.That(resume.success,Is.True,resume.reason);
            Assert.That(reactionCommits,Is.EqualTo(3));Assert.That(world.Profile.State.revision,Is.EqualTo(pausedRevision+2),"Resume plus exactly one pending preference change.");
            Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(world.ReactionPreferences.autoMode));
            long resumedRevision=world.Profile.State.revision;yield return StableMenuFrames(12);
            Assert.That(reactionCommits,Is.EqualTo(3));Assert.That(world.Profile.State.revision,Is.EqualTo(resumedRevision));
            Assert.That(Gameplay(),Is.EqualTo(gameplay));
        }

        [UnityTest] public IEnumerator P6BindingKeepsObservationAcrossFramesAndRebindsOnActualModeChange()
        {
            yield return OpenProfile(true);
            world.ConfigureReduceMotion(true);
            Assert.That(Field<string>(world,"tutorialInstance"),Is.EqualTo(world.Profile.State.profileInstanceId));
            Assert.That(Field<string>(world,"tutorialModeId"),Is.EqualTo(world.Profile.ActiveMode.id));
            Assert.That(world.Tutorial,Is.Null,"Binding P6 does not start the legacy tutorial.");
            var observed=Field<HashSet<string>>(world,"observedIncoming");
            var audioObserved=Field<HashSet<string>>(world.Audio,"incomingApplied");
            observed.Add("fixture-seen-contact");audioObserved.Add("fixture-seen-audio");
            long revision=world.Profile.State.revision;string disk=File.ReadAllText(path);
            yield return StableMenuFrames();
            Assert.That(observed.Contains("fixture-seen-contact"),Is.True,"Stable LateUpdate must not clear combat observer de-duplication.");
            Assert.That(audioObserved.Contains("fixture-seen-audio"),Is.True,"Stable LateUpdate must not reinitialize audio observation.");
            Assert.That(world.Profile.State.revision,Is.EqualTo(revision));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
            Rebind(); // Explicit caller still deliberately resets its observation baseline.
            Assert.That(observed.Contains("fixture-seen-contact"),Is.False);Assert.That(audioObserved.Contains("fixture-seen-audio"),Is.False);
            string story=world.Profile.ActiveMode.id;
            var start=world.ProfileAction(new ProfileCommand{id="fixture-start-demo",kind=ProfileCommandKind.StartDemoExploration,selectionId="BASE"});
            Assert.That(start.success,Is.True,start.reason);Assert.That(world.Profile.ActiveMode.id,Is.Not.EqualTo(story));
            Assert.That(Field<string>(world,"tutorialModeId"),Is.EqualTo(world.Profile.ActiveMode.id));
            Assert.That(world.Tutorial,Is.Null);
            observed.Add("fixture-new-mode-contact");audioObserved.Add("fixture-new-mode-audio");
            revision=world.Profile.State.revision;yield return StableMenuFrames();
            Assert.That(observed.Contains("fixture-new-mode-contact"),Is.True);Assert.That(audioObserved.Contains("fixture-new-mode-audio"),Is.True);
            Assert.That(world.Profile.State.revision,Is.EqualTo(revision));
        }

        [UnityTest] public IEnumerator NonP6SettingsKeepTwoPointFiveAndCommitOnlyRealChanges()
        {
            yield return OpenProfile(false);
            world.ConfigureReduceMotion(true);
            Assert.That(reactionCommits,Is.Zero);Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));
            world.ChooseFrontend("settings");world.Hud.RefreshView();Assert.That(ReactionLabel(),Does.StartWith("反应 2.5秒"));
            string gameplay=Gameplay();long revision=world.Profile.State.revision;
            world.CycleReactionMode();Assert.That(reactionCommits,Is.EqualTo(1));
            Assert.That(world.Profile.State.revision,Is.EqualTo(revision+1));
            string disk=File.ReadAllText(path);revision=world.Profile.State.revision;
            yield return StableMenuFrames();
            Assert.That(reactionCommits,Is.EqualTo(1));Assert.That(world.Profile.State.revision,Is.EqualTo(revision));
            Assert.That(File.ReadAllText(path),Is.EqualTo(disk));Assert.That(Gameplay(),Is.EqualTo(gameplay));
            Assert.That(world.State.reactionSettings.decisionSeconds,Is.EqualTo(2.5f));
            Assert.That(world.State.reactionSettings.autoMode,Is.EqualTo(world.ReactionPreferences.autoMode));
        }
    }
}
