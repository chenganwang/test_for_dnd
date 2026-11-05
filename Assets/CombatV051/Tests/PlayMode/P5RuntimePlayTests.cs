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
    // Resources + actual GameWorld Update/InputSystem/UI integration. Combat cases
    // explicitly place actors, suppress enemy attacks and seed opportunity/condition
    // DTOs to isolate the contract. These are NOT natural RNG or human play evidence.
    public sealed class P5RuntimePlayTests
    {
        BattleWorld world; Keyboard keyboard; Mouse mouse; string directory,path;
        InputSettings.BackgroundBehavior background; InputSettings.UpdateMode updateMode;
#if UNITY_EDITOR
        InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        ActorState Fighter=>world.State.Actor("fighter");
        ActorState Mage=>world.State.Actor("mage");
        static void Place(ActorState actor,float x,float z){actor.x=x;actor.z=z;actor.heightY=0;}
        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;updateMode=InputSystem.settings.updateMode;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
#if UNITY_EDITOR
            editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-P5-Runtime",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);path=Path.Combine(directory,"profile.json");
            Assert.That(Resources.Load<TextAsset>("CombatConfig"),Is.Not.Null);Assert.That(Resources.Load<TextAsset>("P5CombatConfig"),Is.Not.Null);
            world=new GameObject("P5 engine contract fixture").AddComponent<BattleWorld>();world.InitializeFrontendForTests(path,true);
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f)});
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
#endif
            Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-P5-Runtime"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        IEnumerator Press(Key key)
        {InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;}
        IEnumerator Click(string id)
        {
            world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
            Assert.That(button.interactable,Is.True,id+"; "+world.Message);
            var rect=(RectTransform)button.transform;Vector2 p=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=p},hits);
            Assert.That(p.x,Is.InRange(0f,(float)Screen.width));Assert.That(p.y,Is.InRange(0f,(float)Screen.height));
            Assert.That(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),Is.True,id+" raycast="+string.Join(",",hits.Select(h=>h.gameObject.name))+"; "+p);
            int clicks=0;button.onClick.AddListener(()=>clicks++); // Observe actual pointer dispatch, never invoke.
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p,buttons=1});yield return null;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=p});yield return null;yield return null;
            Assert.That(clicks,Is.EqualTo(1),id+" actual pointer event");
            var card=button.GetComponent<P47FrontendCard>();if(card!=null)yield return Until(()=>!card.Confirming,2,id+" visual confirmation must complete");
        }
        IEnumerator Until(Func<bool> condition,float seconds,string description,Func<string> diagnostic=null)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&clock.Elapsed.TotalSeconds<seconds)yield return null;
            Assert.That(condition(),Is.True,description+"; "+world.Message+(diagnostic==null?"":"; "+diagnostic()));
        }
        IEnumerator CombatFixture(bool quietAllies=true)
        {
            world.InitializeDungeonForTests(path,"p5-runtime",true,true);world.ChooseDungeonOption("close");
            // R1 deployment fixture; room entry/activation still use the real profile commands.
            foreach(var actor in world.State.actors)Place(actor,26,-2);
            var enter=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",qualified=true});Assert.That(enter.success,Is.True,enter.reason);
            var activate=world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,qualified=true});Assert.That(activate.success,Is.True,activate.reason);
            Place(Fighter,26,-2);Place(world.State.Actor("rogue"),27,-3);Place(Mage,25,-3);
            for(int i=0;i<world.State.enemies.Count;i++){var e=world.State.enemies[i];Place(e,27+i*4,-2);e.aiState=2;e.aiRemaining=100;e.lockedX=e.x+1;e.lockedZ=e.z;}
            if(quietAllies)foreach(var id in world.State.actors.Select(a=>a.id).ToArray())
            {
                world.State.Actor(id).majorRemaining=100;
                var hold=world.ExecuteWorldCommand(new CombatCommand{id="fixture-hold/"+id,kind=CommandKind.IssueBasicOrder,actorId=id,actionId="Hold"});Assert.That(hold.success,Is.True,hold.reason);
            }
            world.SelectedActor="fighter";world.SelectedAction="shove";world.SelectedTarget=world.State.enemies[0].id;world.QuickDice=false;
            Physics.SyncTransforms();yield return null;yield return null;
            Assert.That(world.P5Enabled,Is.True,world.Message);Assert.That(world.State.actors.Select(a=>a.id),Is.EquivalentTo(new[]{"fighter","rogue","mage"}));
            Assert.That(world.State.actors.All(a=>a.Alive&&a.p5!=null),Is.True);Assert.That(world.DungeonMap,Is.Not.Null);Assert.That(world.BattleCamera.orthographic,Is.True);
        }
        SetupToken Token(string id,string kind,string target,float remaining)
        {
            var t=new SetupToken{id=id,ownerId="mage",targetId=target,remaining=remaining,p5=new P5OpportunityData{kind=kind,sourceActionId="fixture/"+id,encounterAttemptId=world.State.encounterId,sourceConditionId=kind=="bound"?"condition/"+id:null}};
            if(kind=="bound")world.State.Actor(target).statuses.Add(new StatusState{id="p5-slow",remaining=remaining,p5=new P5ConditionData{instanceId="condition/"+id,sourceActionId="fixture/"+id,moveMultiplier=.7f}});
            world.State.p5.opportunities.Add(t);return t;
        }
        SetupToken Token(string id)=>world.State.p5.opportunities.Single(t=>t.id==id);
        string WorldTimers()=>CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.p5,world.State.focusBudget,world.State.simulationSeconds,world.State.queue});
        string VisibleText()=>string.Join("\n",world.Hud.GetComponentsInChildren<Text>().Where(t=>t.gameObject.activeInHierarchy).Select(t=>t.text));

        [UnityTest] public IEnumerator ThreeEntryFrontendActualClicksCreateANewP5DepartureAndThreePersonDungeon()
        {
            Assert.That(world.FrontChoices.Select(c=>c.id),Is.EquivalentTo(new[]{"campaign","explore","characters"}));
            yield return Click("front-explore");yield return Click("front-map-ashen");Assert.That(world.FrontPage,Is.EqualTo("prepare"),world.Message);
            yield return Click("front-depart");Assert.That(world.Menu,Is.False,world.Message);Assert.That(world.P5Enabled,Is.True);
            Assert.That(world.Profile.ActiveMode.kind,Is.EqualTo(ProfileModeKind.Challenge));Assert.That(world.Profile.ActiveMode.departureP5Rules,Is.Not.Null);
            Assert.That(world.Profile.BaseCombatConfig.p5,Is.Null,"The original profile base hash remains legacy.");
            Assert.That(world.State.actors.Count,Is.EqualTo(3));Assert.That(world.State.actors.All(a=>a.Alive&&a.p5!=null),Is.True);
            Assert.That(world.Profile.ActiveRoom.templateId,Is.EqualTo("R0"));Assert.That(world.DungeonMap,Is.Not.Null);Assert.That(File.Exists(path),Is.True);
            var envelope=CombatJson.Read<ProfileEnvelope>(File.ReadAllText(path));var disk=CombatJson.Read<ProfileState>(envelope.payload);
            Assert.That(disk.ActiveMode.id,Is.EqualTo(world.Profile.ActiveMode.id));Assert.That(disk.ActiveMode.departureP5RulesHash,Is.EqualTo(world.Profile.ActiveMode.departureP5RulesHash));
        }
        [UnityTest] public IEnumerator ActualFUsesAuthorizedTargetAndDoesNotReplaceExpiredOpportunityOrLetAiTakeIt()
        {
            yield return CombatFixture(false);string target=world.SelectedTarget,other="rogue"; // A different living target must not redirect the selected enemy token.
            // This is a controlled authorization fixture, not a natural combat/balance run.
            // Preserve the selected target and start the expiry clock only after actual F
            // input creates its order. Exercise real AI in its own phase; authorization
            // assertions must not race the presentation hit-stop that intentionally blocks input.
            foreach(var enemy in world.State.enemies)enemy.hp=enemy.maxHp=1000;
            Token("expires","off_balance",target,30);Token("fallback","exposed",target,30);
            Func<string> diagnostic=()=>CombatJson.Write(new{world.State.simulationSeconds,world.State.mode,selected=world.SelectedTarget,
                targetHp=world.State.Actor(target)?.hp,expires=Token("expires"),fallback=Token("fallback"),order=Fighter.basicOrder,
                Fighter.majorRemaining,draws=world.State.random.backgroundCombat.drawCount,impactHolding=world.ImpactFeedback?.Holding});
            Func<bool> settleAiAndAllowInput=()=>
            {
                // Fixture-only suppression of new attacks. Already-started contacts still
                // resolve normally; repeat because their completion sets their normal recovery.
                foreach(var ally in world.State.actors.Where(a=>a.id!="fighter"))ally.majorRemaining=100;
                return world.State.actors.All(a=>a.basicAttackIntent==null||a.basicAttackIntent.resolved)&&
                    world.ImpactFeedback?.Holding!=true&&!world.IsFrozen&&world.State.activePendingEvent==null&&world.State.pendingRoll==null;
            };
            yield return Until(settleAiAndAllowInput,3,"Initial F input must wait for existing contacts and hit-stop",diagnostic);
            Fighter.majorRemaining=100;int charges=world.State.charges;ulong beforeDraws=world.State.random.backgroundCombat.drawCount;
            world.ChooseOpportunity("expires");yield return Press(Key.F);
            Assert.That(Fighter.basicOrder,Is.Not.Null,world.Message+"; "+diagnostic());Assert.That(Fighter.basicOrder.targetId,Is.EqualTo(target),diagnostic());Assert.That(Fighter.basicOrder.p5.opportunityId,Is.EqualTo("expires"),diagnostic());
            Token("expires").remaining=.1f;
            foreach(var ally in world.State.actors.Where(a=>a.id!="fighter"))ally.majorRemaining=0;
            yield return Until(()=>Fighter.basicOrder.stage=="Canceled",3,"Expired authorized token must cancel its order",diagnostic);
            yield return Until(()=>world.State.random.backgroundCombat.drawCount>beforeDraws,3,"Default ally AI must actually attempt ordinary combat",diagnostic);
            yield return Until(settleAiAndAllowInput,3,"Authorized F input must wait for actual AI contacts and hit-stop",diagnostic);
            Assert.That(Token("expires").consumed,Is.False,diagnostic());Assert.That(Token("fallback").consumed,Is.False,"AI and expired orders cannot take a replacement token.; "+diagnostic());
            world.SelectedTarget=other;world.ChooseOpportunity(null);world.ChooseOpportunity("fallback");yield return Press(Key.F);
            Assert.That(Token("fallback").consumed,Is.False,diagnostic());Assert.That(Fighter.basicOrder.stage,Is.EqualTo("Canceled"),diagnostic());
            world.SelectedTarget=target;world.ChooseOpportunity(null);world.ChooseOpportunity("fallback");Fighter.majorRemaining=0;
            yield return Press(Key.F);yield return Until(()=>Token("fallback").consumed,3,"F should consume exactly the selected opportunity at basic start",diagnostic);
            Assert.That(Token("fallback").usedBy,Is.EqualTo("fighter"),diagnostic());Assert.That(Token("fallback").targetId,Is.EqualTo(target),diagnostic());Assert.That(Token("expires").consumed,Is.False,diagnostic());
            Assert.That(world.State.charges,Is.EqualTo(charges),diagnostic());
        }
        [UnityTest] public IEnumerator StandardTwoSecondRollFreezesConditionsOpportunityFocusAiAndCameraThenNaturallyReveals()
        {
            yield return CombatFixture();Fighter.majorRemaining=0;Token("frozen-condition","bound",world.SelectedTarget,4);
            yield return Press(Key.Tab);yield return Click("shove");yield return Click("confirm");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze),world.Message);Assert.That(world.CurrentRollDuration,Is.EqualTo(2).Within(.0001));
            string frozen=WorldTimers();Vector3 camera=world.BattleCamera.transform.position;Quaternion rotation=world.BattleCamera.transform.rotation;
            float animation=world.RollElapsed;int frames=0;var clock=System.Diagnostics.Stopwatch.StartNew();
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.F));
            while(world.State.mode==CombatMode.RollFreeze&&clock.Elapsed.TotalSeconds<6)
            {
                Assert.That(WorldTimers(),Is.EqualTo(frozen),"No world/AI/condition/focus timer advances during dice freeze.");
                Assert.That(world.BattleCamera.transform.position,Is.EqualTo(camera));Assert.That(world.BattleCamera.transform.rotation,Is.EqualTo(rotation));
                frames++;yield return null;
            }
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
            Assert.That(frames,Is.GreaterThan(1));Assert.That(world.RollElapsed,Is.GreaterThan(animation));Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision),world.Message);
            Assert.That(Token("frozen-condition").consumed,Is.False);float remaining=Token("frozen-condition").remaining,focus=world.State.focusBudget.secondsRemaining;
            yield return Until(()=>Token("frozen-condition").remaining<remaining&&world.State.focusBudget.secondsRemaining<focus,2,"World conditions and focus resume after actual reveal");
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));
        }
        [UnityTest] public IEnumerator ShoveCheckAndEnemyConstitutionSaveUiMatchRealThreeMetreNinetyDegreeFriendlyFirePreview()
        {
            yield return CombatFixture();Fighter.majorRemaining=0;yield return Press(Key.Tab);yield return Click("shove");
            var check=world.SelectedActionQuote();Assert.That(check.legal,Is.True,check.reason);Assert.That(check.rollType,Is.EqualTo("Check"));Assert.That(check.rollOwnerId,Is.EqualTo("fighter"));
            Assert.That(VisibleText(),Does.Contain("运动 d20"));
            Place(Mage,26,-3);Place(world.State.Actor(world.SelectedTarget),28,-3);Place(Fighter,27,-3);Place(world.State.Actor("rogue"),26,-5);
            yield return Press(Key.Digit3);Mage.majorRemaining=0;yield return Click("control");
            var save=world.SelectedActionQuote();Assert.That(save.legal,Is.True,save.reason);Assert.That(save.rollType,Is.EqualTo("Save"));Assert.That(save.rollOwnerId,Is.EqualTo(world.SelectedTarget));
            Assert.That(VisibleText(),Does.Contain("敌人豁免 d20"));Assert.That(VisibleText(),Does.Contain("CON"));Assert.That(world.P5ConeWarning,Does.Contain("战士"));
            var previews=world.GetComponentsInChildren<AreaPreview>().Where(p=>p.Shape.Kind==AreaKind.Cone&&p.Shape.Origin==BattleWorld.Position(Mage)&&Mathf.Abs(p.Shape.Radius-3)<.001f).ToArray();
            Assert.That(previews.Length,Is.GreaterThan(0),"Actual rendered mage area preview missing.");
            foreach(var preview in previews){Assert.That(preview.Shape.HalfAngleDegrees,Is.EqualTo(45));Assert.That(BattleGeometry.Contains(preview.Shape,BattleWorld.Position(Fighter)),Is.True);Assert.That(BattleGeometry.Contains(preview.Shape,BattleWorld.Position(world.State.Actor("rogue"))),Is.False);}
            Assert.That(world.State.charges,Is.EqualTo(2));Assert.That(Mage.spellSlots,Is.EqualTo(world.Session.Config.Template(ActorRole.Mage).spellSlots));
        }
    }
}
