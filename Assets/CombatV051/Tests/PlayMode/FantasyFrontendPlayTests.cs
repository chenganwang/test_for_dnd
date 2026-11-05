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
    public sealed class FantasyFrontendPlayTests
    {
        BattleWorld world;Mouse mouse;Keyboard keyboard;string directory;
        InputSettings.BackgroundBehavior background;InputSettings.UpdateMode update;
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
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-Fantasy-Frontend",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("Fantasy UI real pointer fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));
            yield return null;yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=update;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editor;
#endif
            Time.timeScale=1;string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-Fantasy-Frontend"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase))Directory.Delete(directory,true);
        }
        Button Find(string id)=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
        FantasyFrontendArt Art(string id)=>Find(id).GetComponentInChildren<FantasyFrontendArt>();
        Vector2 Center(Button button){Canvas.ForceUpdateCanvases();var r=(RectTransform)button.transform;return RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(r.rect.center));}
        void Pointer(Vector2 point,bool down)=>InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=down?(ushort)1:(ushort)0});
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();yield return null;var b=Find(id);Assert.That(b.interactable,Is.True,id);
            var p=Center(b);var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},rays);
            Assert.That(rays.Count>0&&(rays[0].gameObject==b.gameObject||rays[0].gameObject.transform.IsChildOf(b.transform)),Is.True,id+" must remain the actual top raycast target.");
            int clicks=0;UnityEngine.Events.UnityAction observe=()=>clicks++;b.onClick.AddListener(observe);
            Pointer(p,false);yield return null;Pointer(p,true);yield return null;Pointer(p,false);yield return null;yield return null;
            b.onClick.RemoveListener(observe);Assert.That(clicks,Is.EqualTo(1));
        }
        string Colours(FantasyFrontendArt graphic)=>string.Join(";",graphic.canvasRenderer.GetMesh().colors32.Select(c=>c.r+","+c.g+","+c.b+","+c.a));
        [UnityTest] public IEnumerator ActualHoverPressedArtSevenCharactersAndPreparationPreserveProfileAndControlPool()
        {
            var forest=world.Hud.Canvas.GetComponentsInChildren<FantasyFrontendArt>().Single(a=>a.Surface==FantasySurface.Forest);
            Assert.That(forest.raycastTarget,Is.False);Assert.That(forest.canvasRenderer.GetMesh().vertexCount,Is.GreaterThan(500),"The forest must be rendered geometry, not one recoloured default rectangle.");
            var main=Find("front-campaign");var art=Art(main.name);string normal=Colours(art);
            // V12 uses a .15s eased transition. Two fast batch frames can still
            // quantize to the same 8-bit vertex colour near its zero-slope start.
            Pointer(Center(main),false);yield return new WaitForSecondsRealtime(.17f);Canvas.ForceUpdateCanvases();string hover=Colours(art);Assert.That(hover,Is.Not.EqualTo(normal));
            Pointer(Center(main),true);yield return null;Canvas.ForceUpdateCanvases();Assert.That(Colours(art),Is.Not.EqualTo(hover));
            // Cancel the press by leaving before release; no menu command is invoked.
            Pointer(new Vector2(8,8),true);yield return null;Pointer(new Vector2(8,8),false);yield return null;Assert.That(world.FrontPage,Is.EqualTo("main"));
            int pooled=world.Hud.Canvas.GetComponentsInChildren<FantasyFrontendButton>(true).Length;
            yield return Click("front-characters");var draft=world.Profile.V11PreparationDraft;
            foreach(var id in draft.teamIds)Assert.That(Find("front-role:"+id).GetComponentsInChildren<Text>().Any(t=>t.text=="核心"||t.text.StartsWith("队")),Is.True);
            string original=CombatJson.Write(world.Profile.State.meta.v11);
            var ids=new HashSet<string>();
            foreach(var character in V11Catalog.Characters)
            {
                yield return Click("front-role:"+character.id);
                ids.Add(world.V11CurrentCharacter);
                Assert.That(world.Hud.Canvas.GetComponentsInChildren<FantasyFrontendArt>().Count(a=>a.Surface==FantasySurface.Node),Is.EqualTo(character.id==CombatSession.V12Finn?5:3));
                yield return Click("front-characters");
            }
            Assert.That(ids,Is.EquivalentTo(V11Catalog.Characters.Select(c=>c.id)));Assert.That(CombatJson.Write(world.Profile.State.meta.v11),Is.EqualTo(original));
            yield return Click("front-explore");yield return Click("front-map-ashen");
            Assert.That(world.FrontPage,Is.EqualTo("prepare"));Assert.That(Find("front-depart").interactable,Is.True);
            Assert.That(world.Hud.Canvas.GetComponentsInChildren<FantasyFrontendButton>(true).Length,Is.EqualTo(pooled),"Navigating pages must reuse the existing UI pool.");
        }
        [UnityTest] public IEnumerator NodeInsufficientPrerequisiteAvailableUnlockedSelectedAreDistinctAndIdleArtworkDoesNotRebuild()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            const string passive="front-node:char_story_evan/passive",active1="front-node:char_story_evan/active1",active2="front-node:char_story_evan/active2";
            Assert.That(Art(passive).NodeState,Is.EqualTo(FantasyNodeState.Available));Assert.That(Art(passive).Selected,Is.True);
            Assert.That(Art(active1).NodeState,Is.EqualTo(FantasyNodeState.Unlocked));Assert.That(Art(active2).NodeState,Is.EqualTo(FantasyNodeState.Prerequisite));
            // Explicit isolated presentation fixture. It is not a purchase or a
            // real Player reward; the authoritative quote still decides the state.
            world.Profile.State.meta.v11.points=0;world.Hud.RefreshView();yield return null;
            Assert.That(Art(passive).NodeState,Is.EqualTo(FantasyNodeState.Insufficient));Assert.That(Art(active2).NodeState,Is.EqualTo(FantasyNodeState.Prerequisite));
            Assert.That(Find("front-node-confirm").interactable,Is.False);
            yield return Click(active1);Assert.That(Art(active1).Selected,Is.True);Assert.That(Art(passive).Selected,Is.False);
            Pointer(new Vector2(8,8),false);yield return new WaitForSecondsRealtime(.25f);Canvas.ForceUpdateCanvases();
            var graphics=world.Hud.Canvas.GetComponentsInChildren<FantasyFrontendArt>();var builds=graphics.Select(g=>g.MeshBuildCount).ToArray();
            for(int i=0;i<6;i++){world.Hud.RefreshView();yield return null;}
            Assert.That(graphics.Select(g=>g.MeshBuildCount),Is.EqualTo(builds),"Static artwork must not generate new meshes merely because the HUD refreshed.");
        }
        [UnityTest] public IEnumerator AllTwentyOneSkillDescriptionsKeepTheirCostsReadableAndOverflowScrollsWithActualWheelInput()
        {
            yield return Click("front-characters");yield return Click("front-role:char_story_evan");
            string profile=CombatJson.Write(world.Profile.State.meta.v11);var visited=new HashSet<string>();int overflowCount=0;
            for(int character=0;character<7;character++)
            {
                foreach(var node in V11Catalog.NodesFor(world.V11CurrentCharacter))
                {
                    yield return Click("front-node:"+node.id);Canvas.ForceUpdateCanvases();
                    var scroll=world.Hud.Canvas.GetComponentsInChildren<ScrollRect>().Single(s=>s.name=="Skill folio viewport");
                    var body=scroll.content.GetComponent<Text>();visited.Add(node.id);
                    Assert.That(body.fontSize,Is.EqualTo(16),"Long copy must not shrink the whole tree or its description type.");
                    Assert.That(body.text,Does.Contain(node.description),node.id);
                    bool main=V11Catalog.FindCharacter(node.characterId).category=="Main";
                    if(main)Assert.That(world.FrontBody,Does.Contain("新探索双主动默认开放；永久节点另存"));
                    if(main&&node.kind=="Active")
                    {
                        Assert.That(body.text,Does.Contain("V12探索已开放，无需购买即可使用。"),node.id);
                        Assert.That(body.text,Does.Contain("不增加本版技能效果"),node.id);
                        Assert.That(body.text,Does.Contain("已开始的冒险保持原快照"),node.id);
                        Assert.That(Find("front-node:"+node.id).GetComponentInChildren<Text>().text,Does.Contain("永久 · "),node.id);
                        Assert.That(Find("front-node-confirm").GetComponentInChildren<Text>().text,Does.Contain("永久"),node.id);
                    }
                    else
                    {
                        Assert.That(body.text,Does.Not.Contain("不增加本版技能效果"),"Passive purchases still change runtime; companion rules stay unchanged: "+node.id);
                        Assert.That(body.text,Does.Not.Contain("V12探索已开放，无需购买即可使用。"),node.id);
                    }
                    Assert.That(body.text,Does.Contain("前置："),node.id);
                    string cost="消耗："+node.cost+" Demo 点";Assert.That(body.text,Does.Contain(cost),node.id);
                    Assert.That(scroll.content.rect.height,Is.GreaterThanOrEqualTo(body.preferredHeight-.5f),node.id+" must retain every generated line.");
                    if(body.preferredHeight<=scroll.viewport.rect.height+.5f)
                    {
                        Assert.That(scroll.content.anchoredPosition.y,Is.EqualTo(0).Within(.5f),node.id);
                        continue;
                    }
                    overflowCount++;Assert.That(scroll.vertical,Is.True);Assert.That(scroll.horizontal,Is.False);
                    Assert.That(scroll.viewport.GetComponent<RectMask2D>(),Is.Not.Null);Assert.That(scroll.verticalScrollbar.gameObject.activeInHierarchy,Is.True);
                    Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(1).Within(.01f),"Selecting a different description resets only that folio to its heading.");
                    var point=RectTransformUtility.WorldToScreenPoint(null,scroll.viewport.TransformPoint(scroll.viewport.rect.center));
                    Pointer(point,false);yield return null;
                    var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},rays);
                    Assert.That(rays.First().gameObject,Is.EqualTo(scroll.gameObject),"The paper must receive real scroll/drag input.");
                    for(int step=0;step<8&&scroll.verticalNormalizedPosition>.005f;step++)
                    {
                        InputSystem.QueueDeltaStateEvent(mouse.scroll,new Vector2(0,-120));yield return null;yield return null;
                    }
                    Canvas.ForceUpdateCanvases();Assert.That(scroll.verticalNormalizedPosition,Is.LessThan(.01f),node.id+" must allow reaching its fee/footer with the mouse wheel.");
                    var contentBottom=scroll.viewport.InverseTransformPoint(scroll.content.TransformPoint(new Vector3(0,scroll.content.rect.yMin,0)));
                    Assert.That(contentBottom.y,Is.EqualTo(scroll.viewport.rect.yMin).Within(1),"The final lines must enter the actual clipped viewport.");
                    // At the bottom, the complete fee and following status must
                    // fit in view, measured from the rendered generator's lines.
                    int feeIndex=body.text.IndexOf(cost,StringComparison.Ordinal);var lines=body.cachedTextGenerator.lines;
                    int feeLine=0;for(int line=0;line<lines.Count;line++)if(lines[line].startCharIdx<=feeIndex)feeLine=line;
                    var last=lines[lines.Count-1];float feeAndFooterHeight=(lines[feeLine].topY-last.topY+last.height)/body.pixelsPerUnit;
                    Assert.That(feeAndFooterHeight,Is.LessThanOrEqualTo(scroll.viewport.rect.height),node.id+" fee/status must be simultaneously visible when scrolled down.");
                    float position=scroll.verticalNormalizedPosition;world.Hud.RefreshView();yield return null;
                    Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(position).Within(.01f),"An ordinary HUD refresh must not pull a reader back to the top.");
                }
                yield return Click("front-tree-next");
            }
            Assert.That(visited.Count,Is.EqualTo(21));Assert.That(overflowCount,Is.GreaterThan(0),"The actual long catalog descriptions must exercise the overflow path.");
            Assert.That(visited.Contains("char_story_finn/passive"),Is.True);Assert.That(CombatJson.Write(world.Profile.State.meta.v11),Is.EqualTo(profile));
        }
    }
}
