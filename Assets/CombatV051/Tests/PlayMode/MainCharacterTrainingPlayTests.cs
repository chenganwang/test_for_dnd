using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Isolated training/profile contracts. Deliberate HP/resource fixtures below
    // distinguish original adventure state from the disposable arena; no natural
    // combat victory or micro-heal result is claimed by these tests.
    public sealed class MainCharacterTrainingPlayTests
    {
        const string Finn="char_story_finn",Evan="char_story_evan",Mira="char_story_mira";
        BattleWorld world;Keyboard keyboard;Mouse mouse;string directory,path;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode update;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editor;
#endif
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;update=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editor=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-Main-Training",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            world=new GameObject("Main training isolation fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(path);
            world.ChooseFrontend("characters");Assert.That(world.Profile,Is.Not.Null);yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            Time.timeScale=1;string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-Main-Training"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        string TutorialPath=>Path.Combine(directory,"presentation","p6-smite-tutorial.txt");
        static void Good(ProfileResult result)=>Assert.That(result.success,Is.True,result.reason);
        ProfileState Disk()=>CombatJson.Read<ProfileState>(CombatJson.Read<ProfileEnvelope>(File.ReadAllText(path)).payload);
        void BuyFinnPassive()
        {
            int points=world.Profile.V11Progress.points;
            Good(world.Profile.UnlockV11Node(Finn,Finn+"/passive","test-paid-finn-passive"));
            Assert.That(world.Profile.V11Progress.points,Is.EqualTo(points-1));
            Assert.That(Disk().meta.v11.unlockedNodeIds,Does.Contain(Finn+"/passive"));
        }
        [UnityTest] public IEnumerator PaidFinnPassiveAndV12TwoActionTrialReachCorelessThreeCharacterArena()
        {
            BuyFinnPassive();var profile=world.Profile;var progress=profile.V11Progress;string meta=CombatJson.Write(profile.State.meta);
            world.StartMainCharacterTraining();world.enabled=false;
            Assert.That(world.MainCharacterTraining,Is.True);Assert.That(world.Profile,Is.Null);Assert.That(world.State.p6.training,Is.True);
            Assert.That(world.State.actors.Select(a=>a.id),Is.EqualTo(new[]{Evan,Mira,Finn}));
            Assert.That(world.State.activeActorId,Is.EqualTo(Evan));Assert.That(world.State.commandActorId,Is.EqualTo(Evan));
            Assert.That(world.Session.Config.v11.coreActorId,Is.Null);Assert.That(world.Session.Config.v11.buffs,Is.Empty);
            Assert.That(world.State.actors.Any(a=>a.id=="char_demo_paladin"),Is.False);
            foreach(var loadout in world.Session.Config.v11.actors)
            {
                var definition=V11Catalog.FindCharacter(loadout.actorId);var nodes=V11Catalog.NodesFor(loadout.actorId);
                Assert.That(loadout.passiveUnlocked,Is.EqualTo(loadout.actorId==Finn));
                // V12 3.5 grants the two actions for this run; the permanent purchase is unchanged.
                Assert.That(loadout.unlockedActionIds,Is.EquivalentTo(nodes.Where(n=>n.kind=="Active").Select(n=>n.abilityId)));
                Assert.That(progress.unlockedNodeIds,Does.Not.Contain(loadout.actorId+"/active2"));
                Assert.That(CombatJson.Write(loadout.finnSustain),Is.EqualTo(CombatJson.Write(definition.finnSustain)));
            }
            world.EndTraining();Assert.That(world.Profile,Is.SameAs(profile));Assert.That(world.MainCharacterTraining,Is.False);
            Assert.That(CombatJson.Write(world.Profile.State.meta),Is.EqualTo(meta));Assert.That(File.Exists(TutorialPath),Is.False);
            yield return null;
        }
        [UnityTest] public IEnumerator TrainingFinishRestoresOriginalAdventureResourcesBuildAndNodesWithoutCompletingPaladinTutorial()
        {
            BuyFinnPassive();Good(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.StartDemoExploration,selectionId="BASE"}));
            world.enabled=false;var originalProfile=world.Profile;
            // Explicit original-adventure fixture: make healing/resource replacement visible.
            foreach(var actor in world.State.actors){actor.hp=Mathf.Max(1,actor.maxHp-3);actor.spellSlots=Mathf.Max(0,actor.spellSlots-1);actor.inspiration=0;}
            Good(world.Profile.SaveCurrent("original-adventure-fixture"));
            string actors=CombatJson.Write(world.State.actors),random=CombatJson.Write(world.State.random),meta=CombatJson.Write(world.Profile.State.meta),
                departure=CombatJson.Write(world.Profile.ActiveMode.v11Departure),build=CombatJson.Write(world.Profile.ActiveMode.v11Build),runId=world.Profile.ActiveMode.id;
            int charges=world.State.charges;float momentum=world.State.momentum;string focus=CombatJson.Write(world.State.focusBudget);
            world.StartMainCharacterTraining();Assert.That(world.MainCharacterTraining,Is.True);string saved=File.ReadAllText(path);
            world.State.Actor(Finn).hp=1;world.State.charges=0;world.RefillP6Practice();
            Assert.That(File.ReadAllText(path),Is.EqualTo(saved),"Training refill may not save into the suspended adventure.");
            world.FinishP6Training();Assert.That(world.Training,Is.False);Assert.That(world.Menu,Is.True);Assert.That(world.Profile,Is.SameAs(originalProfile));
            Assert.That(CombatJson.Write(world.State.actors),Is.EqualTo(actors));Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(world.State.momentum,Is.EqualTo(momentum));
            Assert.That(CombatJson.Write(world.State.focusBudget),Is.EqualTo(focus));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(random));
            Assert.That(world.Profile.ActiveMode.id,Is.EqualTo(runId));Assert.That(CombatJson.Write(world.Profile.ActiveMode.v11Departure),Is.EqualTo(departure));Assert.That(CombatJson.Write(world.Profile.ActiveMode.v11Build),Is.EqualTo(build));
            Assert.That(CombatJson.Write(world.Profile.State.meta),Is.EqualTo(meta));Assert.That(CombatJson.Write(Disk().ActiveMode.explorationCombat.actors),Is.EqualTo(actors));
            Assert.That(File.Exists(TutorialPath),Is.False,"Main-character Finish must not write skipped/completed for the separate Paladin tutorial.");
            // A fresh formal departure must still offer that tutorial. Existing run
            // remains protected, so verify using a second fresh isolated profile.
            world.InitializeP6FrontendForTests(Path.Combine(directory,"second-profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");world.ChooseFrontend("depart");
            Assert.That(world.Training,Is.True);Assert.That(world.MainCharacterTraining,Is.False);Assert.That(world.State.Actor("char_demo_paladin"),Is.Not.Null);
            yield return null;
        }
        [UnityTest] public IEnumerator PaladinApiWithoutPaladinStillRejectsButV12QOpensCurrentHeroWithoutPayment()
        {
            world.StartMainCharacterTraining();Assert.That(world.MainCharacterTraining,Is.True);
            string before=CombatJson.Write(world.State),actor=world.SelectedActor,action=world.SelectedAction,target=world.SelectedTarget;
            var result=world.OpenSmite();Assert.That(result.success,Is.False);Assert.That(result.reason,Is.EqualTo("paladin_not_in_party"));
            Assert.That(world.Message,Does.Contain("没有圣武士"),"Check the synchronous rejection hint before legitimate simulation can replace the shared message.");
            Assert.That(CombatJson.Write(world.State),Is.EqualTo(before));Assert.That(world.SelectedActor,Is.EqualTo(actor));Assert.That(world.SelectedAction,Is.EqualTo(action));Assert.That(world.SelectedTarget,Is.EqualTo(target));
            int charges=world.State.charges,rolls=world.State.rolls.Count;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
            // V12 Q selects this protagonist's key action. It does not roll or charge merely on entering Focus.
            Assert.That(world.State.pendingRoll,Is.Null);Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(world.State.rolls.Count,Is.EqualTo(rolls));
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(world.SelectedActor,Is.EqualTo(actor));Assert.That(world.SelectedAction,Is.EqualTo("v12_evan_final"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
