using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CombatV051.Tests
{
    public sealed class P4FrontendPlayTests
    {
        BattleWorld world;
        string folder;
        [UnitySetUp] public IEnumerator SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "ActionCRPG-P4-Frontend", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            world = new GameObject("P4 live front-end scene").AddComponent<BattleWorld>();
            world.InitializeFrontendForTests(Path.Combine(folder, "profile.json"));
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (world) UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            // Evidence and files are deliberately retained in the task-specific temp folder.
        }
        IEnumerator Click(string choice)
        {
            world.Hud.RefreshView();
            var button = world.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == "front-" + choice && b.gameObject.activeInHierarchy);
            Assert.That(button.gameObject.activeInHierarchy && button.interactable, Is.True, choice);
            button.onClick.Invoke();
            yield return new WaitForSecondsRealtime(.23f);
        }
        [UnityTest] public IEnumerator OrdinaryExploreMapPreparationDepartureAndDiskContinuePreserveParty()
        {
            Assert.That(world.Menu, Is.True); Assert.That(Time.timeScale, Is.Zero);
            Assert.That(world.FrontChoices.Select(c=>c.id),Is.EquivalentTo(new[]{"campaign","explore","characters"}));
            yield return Click("explore");yield return Click("map-ashen");
            Assert.That(world.FrontPage, Is.EqualTo("prepare"), world.Message); Assert.That(world.Menu, Is.True);
            yield return Click("active");yield return Click("depart");
            Assert.That(world.Menu, Is.False); Assert.That(world.State.actors.Count, Is.EqualTo(3));
            Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Challenge));
            Assert.That(world.Profile.State.story.rooms.Where(r=>r.templateId!="R0").All(r=>!r.visited),Is.True);
            Assert.That(world.State.activeActorId, Is.EqualTo("rogue"));
            Assert.That(world.Profile.ActiveRoom.templateId, Is.EqualTo("R0"));
            Assert.That(world.BattleCamera.orthographic, Is.True);
            world.enabled=false; // Freeze simulation while comparing exact persisted DTOs across the menu animation.
            world.Suspend();
            string before = CombatJson.Write(world.Profile.ActiveCombat.State);
            yield return Click("explore");yield return Click("continue-exploration");
            Assert.That(CombatJson.Write(world.Profile.ActiveCombat.State), Is.EqualTo(before));
            Assert.That(world.Menu, Is.False);
        }
        [UnityTest] public IEnumerator TrainingExitDoesNotChangeProfileOrGrantRewards()
        {
            yield return Click("characters");
            Assert.That(world.Profile, Is.Not.Null, world.Message);
            string before = CombatJson.Write(world.Profile.State);
            string disk = File.ReadAllText(world.SavePath);
            yield return Click("main");yield return Click("help");yield return Click("training");
            Assert.That(world.Training, Is.True); Assert.That(world.Profile, Is.Null);
            Assert.That(world.State.actors.Count, Is.EqualTo(3));
            world.State.actors[0].hp = 1; // Fixture damage only inside isolated training session.
            world.Suspend(); yield return null;
            Assert.That(world.Training, Is.False); Assert.That(world.Menu, Is.True);
            // Starting practice may save a revision, but no progression/domain result is transferable.
            var original = CombatJson.Read<ProfileState>(before);
            Assert.That(CombatJson.Write(world.Profile.State.meta), Is.EqualTo(CombatJson.Write(original.meta)));
            Assert.That(CombatJson.Write(world.Profile.State.story.explorationCombat.actors), Is.EqualTo(CombatJson.Write(original.story.explorationCombat.actors)));
            Assert.That(world.Profile.State.meta.research, Is.Zero);
        }
        [UnityTest] public IEnumerator SettingsCanOpenAtTitleAndReturnWithoutStartingAdventure()
        {
            yield return Click("settings");
            Assert.That(world.FrontSettings, Is.True); Assert.That(world.Menu, Is.True);
            var resume = world.Hud.GetComponentsInChildren<Button>(true).Single(b => b.name == "resume");
            Assert.That(resume.gameObject.activeInHierarchy, Is.True);
            resume.onClick.Invoke(); yield return null;
            Assert.That(world.FrontSettings, Is.False); Assert.That(world.Menu, Is.True);
            Assert.That(world.Profile, Is.Null);
        }
        [UnityTest] public IEnumerator FailedLoadIsVisibleAndNeverOverwrittenByExploreMapButton()
        {
            File.WriteAllText(world.SavePath, "this is a damaged profile");
            yield return Click("explore");yield return Click("map-ashen");
            Assert.That(world.Menu, Is.True); Assert.That(world.Profile, Is.Null);
            Assert.That(File.ReadAllText(world.SavePath), Is.EqualTo("this is a damaged profile"));
            Assert.That(world.Message, Does.Contain("失败"));
        }
        [UnityTest] public IEnumerator P46LegacyReactionSettingsNormalizeWithoutDeletingOriginalAndNewDefaultPersists()
        {
            string settings=Path.Combine(folder,"p4-player-settings.json");
            var old=new P4PlayerPreferences{reactions=new ReactionSettings{decisionSeconds=20,pauseDecision=true}};
            string original=CombatJson.Write(old);File.WriteAllText(settings,original);
            world.InitializeFrontendForTests(Path.Combine(folder,"profile.json"));yield return null;
            Assert.That(world.ReactionPreferences.decisionSeconds,Is.EqualTo(2.5f));
            Assert.That(world.ReactionPreferences.pauseDecision,Is.False);Assert.That(File.ReadAllText(settings),Is.EqualTo(original));
            world.CycleReactionAssistance();world.InitializeFrontendForTests(Path.Combine(folder,"profile.json"));yield return null;
            Assert.That(world.ReactionPreferences.timeoutAcceptPaidRoll,Is.True);
            Assert.That(world.ReactionPreferences.decisionSeconds,Is.EqualTo(2.5f));
        }
    }
}
