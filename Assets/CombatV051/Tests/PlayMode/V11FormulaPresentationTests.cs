using System;
using System.Collections;
using System.Collections.Generic;
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
    // Explicit isolated rendering fixture: upgrades, close positions and deterministic
    // PCG seed are arranged here. This is not natural reward acquisition or Player play.
    public sealed class V11FormulaPresentationTests
    {
        const string Pal="char_demo_paladin";BattleWorld world;string directory;
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Formula",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V11 formula rendering fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartTraining();world.enabled=false;world.Controls.Map.Disable();
            var rules=new V11CombatRules{coreActorId=Pal};foreach(var a in world.State.actors){var d=V11Catalog.FindCharacterForRules(a.id,"v11-character-1");rules.actors.Add(new V11ActorLoadout{actorId=a.id,passiveId=d.passiveId,unlockedActionIds=new List<string>(d.actionIds)});a.x=0;a.z=0;}
            rules.buffs.AddRange(new[]{new V11BuffStack{id="core_smite_dice",stacks=2},new V11BuffStack{id="core_full_dice",stacks=1},new V11BuffStack{id="core_smite_accuracy",stacks=2},new V11BuffStack{id="core_radiant_resonance",stacks=1}});
            world.Session.Config.v11=rules;CombatSession.InitializeV11State(world.State,world.Session.Config,false);
            var enemy=world.State.enemies[0];enemy.x=1;enemy.z=0;enemy.armorClass=5;enemy.wisSaveBonus=100;enemy.aiState=2;enemy.aiRemaining=100;
            world.SelectedActor=Pal;world.SelectedAction=CombatSession.P6SmiteId;world.SelectedTarget=enemy.id;yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Formula"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);
        void Mark()
        {
            Good(world.ExecuteWorldCommand(new CombatCommand{id="formula-bind",kind=CommandKind.RequestBindRadiance,actorId="mage",targetId=world.SelectedTarget,actionId=CombatSession.P6BindId}));world.Session.Tick(.76f);
            Good(world.ExecuteWorldCommand(new CombatCommand{id="formula-bind-end",kind=CommandKind.ResolveBindRadiance,actorId="mage",targetId=world.SelectedTarget,rootId="formula-bind",authority=CommandAuthority.System}));
        }
        void SeedRaw(int maximum,bool pair)
        {
            for(ulong seed=1;seed<10000;seed++){var streams=RandomStreams.Create(seed);var sample=CombatJson.Clone(streams.visibleTactical);int a=RandomStreams.D20(sample),b=pair?RandomStreams.D20(sample):a;if(Math.Max(a,b)==maximum){world.State.random.visibleTactical=streams.visibleTactical;return;}}
            Assert.Fail("Deterministic presentation seed was not found.");
        }
        [Test] public void ActualHudAndDiceLayerShowBuffDiceAccuracyAndLiveMarkWithoutMutatingRules()
        {
            Mark();SeedRaw(12,true);Good(world.Command(CommandKind.EnterFocus));Good(world.Command(CommandKind.ConfirmRoll));var t=world.State.pendingRoll;Assert.That(t.rawFaces.Count,Is.EqualTo(2));Assert.That(t.selectedFace,Is.EqualTo(12));Assert.That(t.critical,Is.False);
            string state=CombatJson.Write(world.State);world.AdvanceRollAnimation(world.CurrentRollDuration*.99f);world.Hud.DicePresentation.RefreshPresentation();world.Hud.RefreshView();Canvas.ForceUpdateCanvases();
            var layer=world.Hud.DicePresentation;Assert.That(layer.BreakdownText,Does.Contain("5d8光耀").And.Contain("另加 3d8").And.Contain("光耀×1.75").And.Contain("誓锋校准 +2"));Assert.That(layer.DieAt(layer.SelectedDieIndex).DisplayedRaw,Is.EqualTo(t.selectedFace));Assert.That(CombatJson.Write(world.State),Is.EqualTo(state));
            world.AdvanceRollAnimation(world.CurrentRollDuration*.02f);world.Hud.RefreshView();layer.RefreshPresentation();
            var labels=world.Hud.Canvas.GetComponentsInChildren<Text>(true);Assert.That(labels.Any(x=>x.gameObject.activeInHierarchy&&x.text.Contains("原始 12")&&x.text.Contains("总修正 "+(t.total-t.selectedFace))&&x.text.Contains("5d8光耀")&&x.text.Contains("光耀×1.75")),Is.True,"Actual post-roll Text must show current formula, not base 3d8/1.5.");
            var formula=layer.GetComponentsInChildren<Text>(true).Single(x=>x.name=="Smite damage formula");Assert.That(formula.gameObject.activeInHierarchy,Is.True);Assert.That(formula.raycastTarget,Is.False);Assert.That(formula.preferredHeight,Is.LessThanOrEqualTo(formula.rectTransform.rect.height+2));
        }
        [Test] public void CriticalFormulaDoublesOnlyDamageDiceAndExpiredCapturedMarkDoesNotClaimMultiplier()
        {
            Mark();SeedRaw(20,true);Good(world.Command(CommandKind.EnterFocus));Good(world.Command(CommandKind.ConfirmRoll));var t=world.State.pendingRoll;
            string line=SmiteFormulaPresentation.Formula(world.Session.Config,world.State,Pal,t);Assert.That(line,Does.Contain("4d6").And.Contain("10d8光耀").And.Contain("另加 6d8").And.Contain("力量+3").And.Contain("1.75"));
            var mark=world.State.Actor(t.targetId).statuses.Single(x=>x.p6?.id==t.p6.markId);mark.remaining=0;
            line=SmiteFormulaPresentation.Formula(world.Session.Config,world.State,Pal,t);Assert.That(line,Does.Contain("裂光已失效：光耀×1").And.Not.Contain("1.75"));Assert.That(t.selectedFace,Is.EqualTo(20));
        }
        [Test] public void SupportCheckDoesNotDisplayAthleticsOrWeakShoveOutcome()
        {
            SeedRaw(1,false);world.SelectedAction="v11_oath_aegis";world.SelectedTarget=Pal;Good(world.Command(CommandKind.EnterFocus));Good(world.Command(CommandKind.ConfirmRoll));
            world.AdvanceRollAnimation(world.CurrentRollDuration*.99f);var layer=world.Hud.DicePresentation;layer.RefreshPresentation();Assert.That(layer.BreakdownText,Does.Contain("施法检定失败").And.Not.Contain("削韧").And.Not.Contain("运动检定"));
        }
        [Test] public void SmiteAndRadianceAfterSelfTargetedSkillReturnToEnemyWithoutSpendingOrRollingEarly()
        {
            Good(world.Command(CommandKind.EnterFocus));world.SelectV11Action("v11_oath_aegis");Assert.That(world.SelectedTarget,Is.EqualTo(Pal));
            string random=CombatJson.Write(world.State.random);int charges=world.State.charges;
            Good(world.OpenSmite());Assert.That(world.State.Actor(world.SelectedTarget).isEnemy,Is.True);Assert.That(world.SelectedActionQuote().legal,Is.True);
            Assert.That(world.State.charges,Is.EqualTo(charges));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(random));Assert.That(world.State.pendingRoll,Is.Null);
            world.SelectV11Action("v11_oath_aegis");Assert.That(world.SelectedTarget,Is.EqualTo(Pal));Good(world.RequestRadiance());
            Assert.That(world.State.Actor("mage").p6.bindIntent.targetId,Is.EqualTo(world.State.enemies[0].id));Assert.That(world.State.charges,Is.EqualTo(charges));
            Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(random),"Request begins a short cast; its background Save rolls only when it resolves.");
        }
    }
}
