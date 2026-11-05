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
    // Real UI pointer dispatch against an isolated profile; no combat outcome fixture.
    public sealed class V11FrontendPlayTests
    {
        BattleWorld world;Mouse mouse;Keyboard keyboard;string directory,path;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;updateMode=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Frontend",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            CreateWorld();yield return null;
        }
        void CreateWorld(){world=new GameObject("V11 real frontend fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(path);}
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Frontend"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        Button Find(string id)=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
        IEnumerator Click(string id,bool enabled=true)
        {
            world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();var button=Find(id);Assert.That(button.interactable,Is.EqualTo(enabled),id+"; "+world.Message);
            var rect=(RectTransform)button.transform;Vector2 point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var results=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},results);
            Assert.That(results.Count>0&&(results[0].gameObject==button.gameObject||results[0].gameObject.transform.IsChildOf(button.transform)),Is.True,id+" blocked by "+string.Join(",",results.Select(r=>r.gameObject.name)));
            int calls=0;UnityEngine.Events.UnityAction callback=()=>calls++;button.onClick.AddListener(callback);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=point});yield return null;yield return null;
            button.onClick.RemoveListener(callback);Assert.That(calls,Is.EqualTo(enabled?1:0),"Must use actual pointer dispatch, not Invoke.");
        }
        void AssertVisibleBounds(Button button)
        {
            var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);
            foreach(var corner in corners){Vector2 p=RectTransformUtility.WorldToScreenPoint(null,corner);Assert.That(p.x,Is.InRange(-1f,Screen.width+1f),button.name);Assert.That(p.y,Is.InRange(-1f,Screen.height+1f),button.name);}
        }
        ProfileState Disk()=>CombatJson.Read<ProfileState>(CombatJson.Read<ProfileEnvelope>(File.ReadAllText(path)).payload);

        [UnityTest] public IEnumerator MainRosterAndTreeUseRealButtonsDistinctBlackMeshesAndVisibleBounds()
        {
            Assert.That(world.FrontChoices.Select(c=>c.label),Is.EqualTo(new[]{"战役","探索","角色"}));
            var menu=new[]{Find("front-campaign"),Find("front-explore"),Find("front-characters")};
            foreach(var b in menu)AssertVisibleBounds(b);
            Assert.That(menu.Select(b=>((RectTransform)b.transform).anchoredPosition.x).Distinct().Count(),Is.EqualTo(1),"Main entries must be vertically aligned.");
            yield return Click("front-characters");Assert.That(world.FrontPage,Is.EqualTo("characters"));Assert.That(world.Training,Is.False);
            var roleButtons=world.Hud.Canvas.GetComponentsInChildren<Button>().Where(b=>b.name.StartsWith("front-role:")).ToArray();Assert.That(roleButtons.Length,Is.EqualTo(7));
            var signatures=new HashSet<string>();
            foreach(var b in roleButtons)
            {
                AssertVisibleBounds(b);var art=b.GetComponentInChildren<FrontendSilhouette>();Assert.That(art,Is.Not.Null);Assert.That(art.raycastTarget,Is.False);
                var mesh=art.canvasRenderer.GetMesh();
                bool main=b.name.StartsWith("front-role:char_story_");Assert.That(art.HasReferenceSprite,Is.EqualTo(main),"Three main roles must use supplied references; companions retain their established fallback contour.");
                if(main)Assert.That(mesh.vertexCount,Is.EqualTo(4));else Assert.That(mesh.vertexCount,Is.GreaterThan(40));
                Assert.That(mesh.colors32.All(c=>c.r==0&&c.g==0&&c.b==0&&c.a==255),Is.True,"Silhouette must be uniformly black, retaining only contour.");
                signatures.Add((art.ReferenceResourcePath??"")+"|"+string.Join(";",mesh.vertices.Select(p=>p.x.ToString("F3")+","+p.y.ToString("F3"))));
            }
            Assert.That(signatures.Count,Is.EqualTo(7),"Do not repeat one generic silhouette.");
            yield return Click("front-role:char_story_evan");Assert.That(world.FrontPage,Is.EqualTo("character-tree"));
            yield return Click("front-tree-next");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_mira"));
            yield return Click("front-tree-prev");Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_evan"));
            foreach(var b in world.Hud.Canvas.GetComponentsInChildren<Button>().Where(b=>b.gameObject.activeInHierarchy))AssertVisibleBounds(b);
            yield return Click("front-characters");Assert.That(world.FrontPage,Is.EqualTo("characters"));Assert.That(world.V11CurrentCharacter,Is.EqualTo("char_story_evan"));
        }
        [UnityTest] public IEnumerator PrerequisitePurchaseIsBlockedThenChargedOnceAndSurvivesFullFrontendRestart()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            int before=world.Profile.V11Progress.points;
            yield return Click("front-node:char_story_evan/active2");yield return Click("front-node-confirm",false);Assert.That(world.Profile.V11Progress.points,Is.EqualTo(before));
            yield return Click("front-node:char_story_evan/passive");yield return Click("front-node-confirm");
            Assert.That(world.Profile.V11Progress.points,Is.EqualTo(before-1));yield return Click("front-node-confirm",false);Assert.That(world.Profile.V11Progress.points,Is.EqualTo(before-1));
            yield return Click("front-node:char_story_evan/active2");yield return Click("front-node-confirm");Assert.That(world.Profile.V11Progress.points,Is.EqualTo(before-2));
            Assert.That(Disk().meta.v11.unlockedNodeIds,Does.Contain("char_story_evan/active2"));
            UnityEngine.Object.Destroy(world.gameObject);yield return null;CreateWorld();yield return null;
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");yield return Click("front-node:char_story_evan/active2");
            Assert.That(Find("front-node-confirm").interactable,Is.False);Assert.That(world.Profile.V11Progress.points,Is.EqualTo(before-2));Assert.That(world.Profile.QuoteV11Node("char_story_evan","char_story_evan/active2").status,Is.EqualTo("Unlocked"));
        }
        [UnityTest] public IEnumerator RemovingCoreBlocksDepartureUntilExplicitlyReassignedAndDraftPersists()
        {
            yield return Click("front-explore");yield return Click("front-map-ashen");
            yield return Click("front-slot:0");yield return Click("front-member:char_story_mira");
            Assert.That(world.Profile.V11PreparationDraft.coreId,Is.Null.Or.Empty);Assert.That(Find("front-depart").interactable,Is.False);
            Assert.That(Find("front-member:mage").interactable,Is.False,"Already selected in another slot cannot be duplicated.");
            yield return Click("front-member:char_demo_paladin");Assert.That(Find("front-depart").interactable,Is.False,"Returning a character cannot silently reassign the core.");
            yield return Click("front-core:char_demo_paladin");Assert.That(Find("front-depart").interactable,Is.True);
            yield return Click("front-slot:1");yield return Click("front-member:char_story_evan");
            var draft=Disk().meta.v11.preparation;Assert.That(draft.teamIds,Is.EqualTo(new[]{"char_demo_paladin","char_story_evan","rogue"}));Assert.That(draft.coreId,Is.EqualTo("char_demo_paladin"));
            Assert.That(world.Profile.State.challenge,Is.Null,"Preparing never starts a run or changes battle state.");
        }
    }
}
