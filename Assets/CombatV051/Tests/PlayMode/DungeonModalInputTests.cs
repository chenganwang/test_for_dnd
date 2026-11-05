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
    // Isolated modal-input fixture: the encounter is explicitly cleared to expose
    // its real reward UI. This test is not evidence of natural combat victory.
    public sealed class DungeonModalInputTests
    {
        BattleWorld world; Mouse mouse; Keyboard keyboard; string directory;
        InputSettings.BackgroundBehavior background; InputSettings.UpdateMode update;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editor;
#endif
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;update=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editor=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-Modal-Input",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("Real reward modal input fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            world.ChooseFrontend("explore");world.ChooseFrontend("map-ashen");
            // Isolate historical P6 R1 reward-modal input; the new six-room default has separate tests.
            var historicalRules=V12Defaults.Create();historicalRules.build=null;historicalRules.beam=null;world.Profile.SetNextV12Rules(historicalRules);
            world.Profile.SetNextV12Dungeon(null);world.ChooseFrontend("depart");
            Assert.That(world.Training,Is.True);world.FinishP6Training();
            world.enabled=false;world.Controls.Map.Disable();
            foreach(var actor in world.State.actors){actor.x=26;actor.z=0;}
            Good(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true}));
            Good(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId="R1",qualified=true}));
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-Modal-Input"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        static void Good(ProfileResult result)=>Assert.That(result.success,Is.True,result.reason);
        Button Choice=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name=="dungeon-choice-0");
        Vector2 Point()
        {
            Canvas.ForceUpdateCanvases();var rect=(RectTransform)Choice.transform;
            return RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        }
        void MouseAt(Vector2 point,bool pressed)=>InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=pressed?(ushort)1:(ushort)0});
        void ShowReward()
        {
            foreach(var enemy in world.State.enemies)enemy.hp=0;
            world.ResolveDungeonEncounter();world.Hud.RefreshView();Canvas.ForceUpdateCanvases();
            Assert.That(world.DungeonModal,Is.True);Assert.That(world.Profile.PendingReward,Is.Not.Null);
            Assert.That(Choice.interactable,Is.False,"A just-opened reward must not accept an in-flight combat click.");
        }
        void AssertUnclaimed()
        {
            Assert.That(world.Profile.PendingReward,Is.Not.Null,"A combat press/release consumed a reward without an intentional new choice.");
            Assert.That(world.Profile.ActiveMode.v11Build.buffs,Is.Empty);
            Assert.That(world.Session.Config.v11.buffs,Is.Empty);
        }
        IEnumerator FreshClickClaimsExactlyOne()
        {
            MouseAt(Point(),false);yield return null;yield return null;
            float until=Time.realtimeSinceStartup+2;
            while(!Choice.interactable&&Time.realtimeSinceStartup<until)yield return null;
            Assert.That(Choice.interactable,Is.True,"Released modal never armed.");
            string expected=world.Profile.PendingReward.candidates[0];var point=Point();
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},hits);
            Assert.That(hits.Count>0&&(hits[0].gameObject==Choice.gameObject||hits[0].gameObject.transform.IsChildOf(Choice.transform)),Is.True);
            MouseAt(point,true);yield return null;MouseAt(point,false);yield return null;yield return null;
            Assert.That(world.Profile.PendingReward,Is.Null);
            Assert.That(world.Profile.ActiveMode.v11Build.buffs.Count,Is.EqualTo(1));
            Assert.That(world.Profile.ActiveMode.v11Build.buffs[0].id,Is.EqualTo(expected));
            Assert.That(world.Profile.ActiveMode.v11Build.buffs[0].stacks,Is.EqualTo(1));
            Assert.That(world.Session.Config.v11.buffs.Single().id,Is.EqualTo(expected));
        }
        [UnityTest] public IEnumerator LateBattlePressAfterRewardAppearsCannotChooseBeforeFreshReleaseAndPress()
        {
            ShowReward();var point=Point();
            // Same race as Player02: the room ended during the attack driver's yields,
            // then its already planned mouse-down arrived on the new reward button.
            MouseAt(point,true);yield return null;yield return null;
            MouseAt(point,false);yield return null;yield return null;
            AssertUnclaimed();yield return FreshClickClaimsExactlyOne();
        }
        [UnityTest] public IEnumerator HoldingAttackAcrossModalOpeningDoesNotArmEvenAfterRealTimeGuardExpires()
        {
            MouseAt(new Vector2(Screen.width*.5f,Screen.height*.5f),true);yield return null;
            ShowReward();var point=Point();MouseAt(point,true);
            yield return new WaitForSecondsRealtime(.3f);world.Hud.RefreshView();
            Assert.That(Choice.interactable,Is.False,"Elapsed guard alone must not arm under a held pointer.");AssertUnclaimed();
            MouseAt(point,false);yield return null;yield return null;AssertUnclaimed();
            yield return FreshClickClaimsExactlyOne();
        }
    }
}
