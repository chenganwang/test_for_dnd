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
    // Isolated engine/UI checks. No author profile, injected combat resource,
    // chosen die result or human-fun claim is involved.
    public sealed class V12FrontendMotionPlayTests
    {
        BattleWorld world;Mouse mouse;Keyboard keyboard;string directory;
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
            mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Frontend-Motion",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);CreateWorld();yield return null;yield return null;
        }
        void CreateWorld(){world=new GameObject("V12 UI real pointer fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));}
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            Time.timeScale=1;string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Frontend-Motion"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        Button Find(string id)=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
        V12FrontendTransition Transition=>world.Hud.Canvas.GetComponentInChildren<V12FrontendTransition>(true);
        Vector2 Center(Button b){Canvas.ForceUpdateCanvases();var r=(RectTransform)b.transform;return RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center));}
        void Pointer(Vector2 point,bool down=false)=>InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=down?(ushort)1:(ushort)0});
        IEnumerator Click(string id,int expected=1)
        {
            var b=Find(id);var p=Center(b);
            int calls=0;UnityEngine.Events.UnityAction listener=()=>calls++;b.onClick.AddListener(listener);
            // Test the actual pointer's frame, after Unity assigns native canvas
            // depths to controls just re-enabled by the previous mouse-up. This
            // reuses the existing move yield; it adds no navigation delay.
            Pointer(p);yield return null;Canvas.ForceUpdateCanvases();
            var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},rays);
            Assert.That(rays.Count,Is.GreaterThan(0));Assert.That(rays[0].gameObject==b.gameObject||rays[0].gameObject.transform.IsChildOf(b.transform),Is.True,"Live button "+id+" at "+p+" hit "+string.Join(", ",rays.Select(r=>r.gameObject.name+" depth="+r.depth)));
            Pointer(p,true);yield return null;Pointer(p);yield return null;
            b.onClick.RemoveListener(listener);Assert.That(calls,Is.EqualTo(expected),id);
        }
        [UnityTest] public IEnumerator HoverUsesUnscaledVisualOffsetWhileHitRectStaysFixedAndReducedMotionSnaps()
        {
            var button=Find("front-campaign");var skin=button.GetComponent<FantasyFrontendButton>();var r=(RectTransform)button.transform;
            var hitPosition=r.anchoredPosition;var size=r.sizeDelta;Pointer(Center(button));yield return new WaitForSecondsRealtime(.23f);
            Assert.That(skin.VisualOffset.y,Is.EqualTo(4).Within(.01f));Assert.That(r.anchoredPosition,Is.EqualTo(hitPosition));Assert.That(r.sizeDelta,Is.EqualTo(size));Assert.That(skin.Animating,Is.False);
            Pointer(new Vector2(8,8));yield return null;Pointer(Center(button));yield return null;Pointer(new Vector2(8,8));yield return new WaitForSecondsRealtime(.23f);
            Assert.That(skin.VisualOffset,Is.EqualTo(Vector2.zero),"Repeated enter/exit must use an absolute offset, never accumulate it.");
            Pointer(Center(button));yield return null;world.ConfigureReduceMotion(true);yield return null;
            Assert.That(skin.VisualOffset,Is.EqualTo(Vector2.zero));Assert.That(skin.Animating,Is.False);Assert.That(world.ReduceMotion,Is.True);
            yield return Click("front-campaign");Assert.That(world.FrontPage,Is.EqualTo("campaign"));Assert.That(Transition.Animating,Is.False);Assert.That(Transition.LiveAlpha,Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator RapidBackAndDifferentNodesStayClickableAndSingleSnapshotPoolSettles()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            var transition=Transition;Assert.That(transition.SnapshotCount,Is.GreaterThan(0));
            Assert.That(transition.SnapshotRoot.GetComponentsInChildren<Button>(true),Is.Empty);Assert.That(transition.SnapshotRoot.GetComponentsInChildren<Graphic>(true),Is.Empty);
            var group=transition.SnapshotRoot.GetComponent<CanvasGroup>();Assert.That(group.blocksRaycasts,Is.False);Assert.That(group.interactable,Is.False);
            yield return Click("front-node:char_story_evan/active1");Assert.That(world.V11SelectedNode,Is.EqualTo("char_story_evan/active1"));
            yield return Click("front-node:char_story_evan/active2");Assert.That(world.V11SelectedNode,Is.EqualTo("char_story_evan/active2"));
            yield return Click("front-characters");Assert.That(world.FrontPage,Is.EqualTo("characters"));
            yield return Click("front-role:char_story_mira");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_mira"));
            yield return Click("front-characters");yield return Click("front-main");Assert.That(world.FrontPage,Is.EqualTo("main"));
            int count=transition.SnapshotCount,pool=transition.PooledDrawingCount;yield return new WaitForSecondsRealtime(.24f);
            Assert.That(transition.Animating,Is.False);Assert.That(transition.LiveAlpha,Is.EqualTo(1));Assert.That(transition.SnapshotRoot.activeSelf,Is.False);
            for(int i=0;i<5;i++){world.Hud.RefreshView();yield return null;}
            Assert.That(transition.SnapshotCount,Is.EqualTo(count),"An ordinary HUD refresh must not capture again.");Assert.That(transition.PooledDrawingCount,Is.EqualTo(pool));
            Assert.That(world.Hud.Canvas.GetComponentsInChildren<CanvasGroup>(true).Count(g=>g.name=="V12 outgoing page drawing"),Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator SameCommandDoubleClickDoesNotNavigateTwiceButReboundCommandRemainsUsable()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            var next=(FantasyFrontendCommandButton)Find("front-tree-next");
            yield return Click("front-tree-next");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_mira"));
            yield return Click("front-tree-next",0);Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_mira"));Assert.That(next.SuppressedDoubleClicks,Is.GreaterThan(0));
            yield return Click("front-tree-prev");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_evan"));
            yield return Click("front-characters");yield return Click("front-role:char_story_finn");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_finn"));
        }
        [UnityTest] public IEnumerator UnlockPulseRequiresSuccessfulPersistentPurchaseAndStopsOnPageChange()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            var lines=world.Hud.Canvas.GetComponentInChildren<V11TreeConnections>();int points=world.Profile.V11Progress.points;
            yield return Click("front-node:char_story_evan/active2");yield return Click("front-node-confirm",0);Assert.That(lines.UnlockPulseCount,Is.EqualTo(0));Assert.That(world.Profile.V11Progress.points,Is.EqualTo(points));
            yield return Click("front-node:char_story_evan/passive");var node=Find("front-node:char_story_evan/passive").GetComponent<FantasyFrontendButton>();int pulses=node.UnlockPulseCount;
            yield return Click("front-node-confirm");Assert.That(lines.UnlockPulseCount,Is.EqualTo(1));Assert.That(node.UnlockPulseCount,Is.EqualTo(pulses+1));Assert.That(world.Profile.V11Progress.points,Is.EqualTo(points-1));
            yield return Click("front-node-confirm",0);Assert.That(world.Profile.V11Progress.points,Is.EqualTo(points-1));Assert.That(lines.UnlockPulseCount,Is.EqualTo(1));
            yield return Click("front-characters");Assert.That(lines.Animating,Is.False);
            UnityEngine.Object.Destroy(world.gameObject);yield return null;CreateWorld();yield return null;
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            Assert.That(world.Profile.QuoteV11Node("char_story_evan","char_story_evan/passive").status,Is.EqualTo("Unlocked"));Assert.That(world.Profile.V11Progress.points,Is.EqualTo(points-1));
            Assert.That(world.Hud.Canvas.GetComponentInChildren<V11TreeConnections>().UnlockPulseCount,Is.EqualTo(0),"Loading an old unlock never replays a transaction pulse.");
        }
        [UnityTest] public IEnumerator ReducedMotionSettingsUseExistingFilePersistAndPreserveOldPreferences()
        {
            Assert.That(CombatJson.Read<P4PlayerPreferences>("{\"schema\":1,\"focusScale\":0.1,\"quickDice\":false}").reduceMotion,Is.False,"Old settings without the optional field keep normal presentation.");
            // Main-menu launch intentionally loads profiles lazily. Open the
            // real roster once before comparing an existing progress snapshot.
            yield return Click("front-characters");Assert.That(world.Profile,Is.Not.Null);yield return Click("front-main");
            world.ConfigureImpactSettings(FeedbackIntensity.Low,FeedbackIntensity.Off);world.ConfigureDiceAnimationSpeed(DiceAnimationSpeed.Fast);
            var before=CombatJson.Write(world.Profile.State.meta.v11);yield return Click("v11-settings");yield return Click("reduce-motion");
            Assert.That(world.ReduceMotion,Is.True);var saved=CombatJson.Read<P4PlayerPreferences>(File.ReadAllText(Path.Combine(directory,"p4-player-settings.json")));
            Assert.That(saved.reduceMotion,Is.True);Assert.That(saved.screenShake,Is.EqualTo("Low"));Assert.That(saved.hitStopIntensity,Is.EqualTo("Off"));Assert.That(saved.diceAnimationSpeed,Is.EqualTo("Fast"));Assert.That(CombatJson.Write(world.Profile.State.meta.v11),Is.EqualTo(before));
            UnityEngine.Object.Destroy(world.gameObject);yield return null;CreateWorld();yield return null;Assert.That(world.ReduceMotion,Is.True);
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");Assert.That(Transition.Animating,Is.False);Assert.That(Transition.PooledDrawingCount,Is.EqualTo(0));
        }
    }
}
