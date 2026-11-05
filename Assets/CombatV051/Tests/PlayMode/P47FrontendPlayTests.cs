using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace CombatV051.Tests
{
    public sealed class P47FrontendPlayTests
    {
        BattleWorld world;Keyboard keyboard;Mouse mouse;string folder,file;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode update;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editor;
#endif
        [UnitySetUp]public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;update=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editor=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);
            folder=Path.Combine(Path.GetTempPath(),"ActionCRPG-P47-Frontend",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);file=Path.Combine(folder,"profile.json");
            world=new GameObject("Actual P47 frontend").AddComponent<BattleWorld>();world.InitializeFrontendForTests(file);
            yield return null;yield return null;
        }
        [UnityTearDown]public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            Time.timeScale=1; // Keep task-specific files for failure diagnosis; no user saves are touched.
        }
        Button Find(string id)=>world.Hud.GetComponentsInChildren<Button>(true).Single(b=>b.gameObject.activeInHierarchy&&b.name=="front-"+id);
        Vector2 Center(Button b)
        {Canvas.ForceUpdateCanvases();var r=(RectTransform)b.transform;return RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center));}
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();
            // Menu transitions reactivate pooled Graphics in this same frame. Wait for
            // their canvas depth to be rebuilt before a real screen-space pointer query,
            // matching the Player input harness rather than invoking the button directly.
            yield return null;
            var b=Find(id);Assert.That(b.interactable,Is.True,id);Vector2 point=Center(b);
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Assert.That(hits.Count>0&&(hits[0].gameObject==b.gameObject||hits[0].gameObject.transform.IsChildOf(b.transform)),Is.True,
                "Raycast blocked "+id+" at "+point+" screen "+Screen.width+"x"+Screen.height+
                " targetDepth="+b.targetGraphic.depth+" hits="+string.Join(" | ",hits.Select(h=>h.gameObject.name+" depth="+h.depth+" sort="+h.sortingOrder)));
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point}.WithButton(MouseButton.Left));yield return null;yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
            float timeout=Time.realtimeSinceStartup+.4f;while(b&&b.gameObject.activeInHierarchy&&b.GetComponent<P47FrontendCard>().Confirming&&Time.realtimeSinceStartup<timeout)yield return null;
            yield return null;
        }
        IEnumerator Key(Key key,float hold=.03f)
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return new WaitForSecondsRealtime(hold);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
        }
        IEnumerator Depart()
        {yield return Click("explore");yield return Click("map-ashen");yield return Click("depart");Assert.That(world.Menu,Is.False,world.Message);}

        [UnityTest]public IEnumerator ThreePrimaryCardsHaveActualHoverAndCampaignBackNeverStartsOrWritesAnAdventure()
        {
            Assert.That(world.FrontChoices.Select(c=>c.id),Is.EqualTo(new[]{"campaign","explore","characters"}));
            Assert.That(File.Exists(file),Is.False);var b=Find("campaign");var card=b.GetComponent<P47FrontendCard>();
            InputSystem.QueueStateEvent(mouse,new MouseState{position=Center(b)});yield return new WaitForSecondsRealtime(.24f);
            Assert.That(card.Hovered,Is.True);Assert.That(card.Highlight,Is.GreaterThan(.85f));
            Assert.That(b.transform.localScale.x,Is.GreaterThan(1).And.LessThan(1.02f));
            yield return Click("campaign");Assert.That(world.FrontPage,Is.EqualTo("campaign"));Assert.That(world.FrontBody,Does.Contain("正式剧情开发中"));
            Assert.That(world.Profile,Is.Null);Assert.That(File.Exists(file),Is.False);
            yield return Key(UnityEngine.InputSystem.Key.Escape);Assert.That(world.FrontPage,Is.EqualTo("main"));
            yield return Click("settings");Assert.That(world.FrontSettings,Is.True);
            yield return Key(UnityEngine.InputSystem.Key.Escape);Assert.That(world.FrontSettings,Is.False);Assert.That(world.Profile,Is.Null);
        }
        [UnityTest]public IEnumerator FreshMapPreparationStartsRealExplorationAndTeachesActualMovementWithoutStoryProgress()
        {
            yield return Click("explore");Assert.That(world.FrontChoices.Count(c=>c.id.StartsWith("map-")),Is.EqualTo(1));
            Assert.That(world.FrontChoices.Single(c=>c.id=="map-ashen").label,Does.Contain("灰烬遗迹"));
            yield return Click("map-ashen");Assert.That(world.FrontPage,Is.EqualTo("prepare"));
            string story=CombatJson.Write(world.Profile.State.story);
            yield return Click("active");
            Assert.That(CombatJson.Write(world.Profile.State.story),Is.EqualTo(story),"Editing a meta draft must not capture hidden Story presentation.");
            var savedEnvelope=CombatJson.Read<ProfileEnvelope>(File.ReadAllText(file));
            Assert.That(CombatJson.Write(CombatJson.Read<ProfileState>(savedEnvelope.payload).story),Is.EqualTo(story),"The persisted Story must also remain unchanged.");
            yield return Click("depart");
            Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Challenge));Assert.That(world.State.activeActorId,Is.EqualTo("rogue"));
            Assert.That(CombatJson.Write(world.Profile.State.story),Is.EqualTo(story));Assert.That(world.Profile.State.meta.challengeUnlocked,Is.False);
            Assert.That(world.Tutorial.State.storyModeId,Is.EqualTo(world.Profile.ActiveMode.id));
            Assert.That(world.Tutorial.State.status,Is.EqualTo("Active"));Assert.That(world.DungeonModal,Is.False);
            world.Hud.RefreshView();Assert.That(world.Hud.VisibleTutorialText,Does.Contain("WASD"),"Closing preparation must immediately refresh the already-bound tutorial view.");
            var start=BattleWorld.Position(world.ActiveActor);yield return Key(UnityEngine.InputSystem.Key.W,.72f);
            Assert.That(BattleGeometry.DistanceXZ(start,BattleWorld.Position(world.ActiveActor)),Is.GreaterThan(2));
            Assert.That(world.Tutorial.State.facts.Any(f=>f.key=="move.distance"&&f.value>=2),Is.True);
            Assert.That(CombatJson.Write(world.Profile.State.story),Is.EqualTo(story));
        }
        [UnityTest]public IEnumerator ContinuingExplorationPreservesPaidRollAndDepartureDespiteBrowsingCharacters()
        {
            yield return Depart();world.enabled=false;
            var room=world.DungeonConfig.Room("R1");int n=0;foreach(var actor in world.State.actors){actor.x=room.x+n++*.5f;actor.z=room.z;}
            Assert.That(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true}).success,Is.True);
            Assert.That(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true}).success,Is.True);
            var enemy=world.State.enemies.First();var fighter=world.State.Actor("fighter");enemy.x=fighter.x+2;enemy.z=fighter.z;
            world.SelectedActor="fighter";world.SelectedTarget=enemy.id;world.SelectedAction="attack";
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True,world.Message);
            string ticket=CombatJson.Write(world.State.pendingRoll),departure=CombatJson.Write(world.Profile.ActiveMode.departurePreparation);int charges=world.State.charges;
            world.Suspend();yield return Click("characters");yield return Click("main");yield return Click("explore");yield return Click("map-ashen");
            Assert.That(world.FrontPage,Is.EqualTo("explore-run"));Assert.That(world.FrontChoices.Single(c=>c.id=="new-exploration").enabled,Is.False);
            world.ChooseFrontend("new-exploration");Assert.That(world.FrontPage,Is.EqualTo("explore-run"));
            yield return Click("continue-exploration");
            Assert.That(CombatJson.Write(world.State.pendingRoll),Is.EqualTo(ticket));Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(CombatJson.Write(world.Profile.ActiveMode.departurePreparation),Is.EqualTo(departure));Assert.That(world.Menu,Is.False);
        }
        [UnityTest]public IEnumerator CharacterCardsBuyRealGrowthOnceAndApplyItOnlyToANewDeparture()
        {
            yield return Click("characters");Assert.That(world.FrontChoices.Count(c=>c.id.StartsWith("role:")),Is.EqualTo(3));
            foreach(string id in new[]{"fighter","rogue","mage"})Assert.That(world.FrontActorInfo(id),Does.Contain("初始装备"));
            world.Profile.State.meta.research=3;Assert.That(world.Profile.SaveCurrent("fixture-earned-research").success,Is.True);world.Hud.RefreshView();
            yield return Click("role:fighter");yield return Click("unlock:F_ADEPT");
            Assert.That(world.Profile.State.meta.unlockedSpecializations,Does.Contain("F_ADEPT"));Assert.That(world.Profile.State.meta.research,Is.Zero);
            world.ChooseFrontend("unlock:F_ADEPT");Assert.That(world.Profile.State.meta.research,Is.Zero);
            yield return Click("explore");yield return Click("map-ashen");yield return Click("cycle:fighter");
            Assert.That(world.Profile.PreparationDraft.actors.Single(a=>a.actorId=="fighter").specializationId,Is.EqualTo("F_ADEPT"));
            yield return Click("depart");Assert.That(world.Profile.ActiveMode.departurePreparation.preparation.actors.Single(a=>a.actorId=="fighter").specializationId,Is.EqualTo("F_ADEPT"));
            Assert.That(world.Profile.ActiveCombat.Config.RulesFor("fighter").nearMissCorrection,Is.True);
        }
        [UnityTest]public IEnumerator CampaignAndFailedExplorationLoadNeverReplaceDamagedProfile()
        {
            const string damaged="damaged-but-preserved";File.WriteAllText(file,damaged);
            yield return Click("campaign");Assert.That(File.ReadAllText(file),Is.EqualTo(damaged));
            yield return Click("main");yield return Click("explore");yield return Click("map-ashen");
            Assert.That(world.Profile,Is.Null);Assert.That(world.Menu,Is.True);Assert.That(File.ReadAllText(file),Is.EqualTo(damaged));
            Assert.That(world.Message,Does.Contain("失败"));
        }
        [UnityTest]public IEnumerator PaidHistoricalStoryResumesOnlyThroughSecondaryHelpWithoutResettingItsDiceOrParty()
        {
            world.InitializeDungeonForTests(file);world.enabled=false;
            var room=world.DungeonConfig.Room("R1");int n=0;foreach(var a in world.State.actors){a.x=room.x+n++*.5f;a.z=room.z;}
            Assert.That(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true}).success,Is.True);
            Assert.That(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true}).success,Is.True);
            var enemy=world.State.enemies.First();var fighter=world.State.Actor("fighter");enemy.x=fighter.x+2;enemy.z=fighter.z;
            world.SelectedActor="fighter";world.SelectedAction="attack";world.SelectedTarget=enemy.id;
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True,world.Message);
            string ticket=CombatJson.Write(world.State.pendingRoll),actors=CombatJson.Write(world.State.actors);int charges=world.State.charges;
            world.Suspend();yield return Click("campaign");
            Assert.That(world.FrontPage,Is.EqualTo("campaign"));Assert.That(world.Menu,Is.True);Assert.That(world.FrontBody,Does.Contain("正式剧情开发中"));
            yield return Click("main");yield return Click("help");yield return Click("continue-historical");
            Assert.That(world.Menu,Is.False);Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Story));
            Assert.That(CombatJson.Write(world.State.pendingRoll),Is.EqualTo(ticket));Assert.That(CombatJson.Write(world.State.actors),Is.EqualTo(actors));
            Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(world.Profile.State.challenge,Is.Null);
        }
    }
}
