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
    public sealed partial class P46DicePresentationTests
    {
        BattleWorld world;
        string directory,file;
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCrpg-P46Dice",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);file=Path.Combine(directory,"profile.json");
            world=new GameObject("P46 actual dice world").AddComponent<BattleWorld>();world.InitializeForTests(file);world.IgnoreApplicationFocus=true;
            world.enabled=false;world.Controls.Map.Disable();
            world.ActiveActor.x=0;world.ActiveActor.z=0;world.State.enemies[0].x=2;world.State.enemies[0].z=0;
            world.State.enemies[0].aiRemaining=100;world.State.enemies[0].aiState=2;world.QuickDice=false;
            yield return null;
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCrpg-P46Dice"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        RollTicket Confirm(string kind="single",string action="attack")
        {
            if(kind=="advantage")world.ActiveActor.aimReady=true;
            if(kind=="disadvantage")world.ActiveActor.statuses.Add(new StatusState{id="suppressed",sourceId="test",remaining=20});
            world.SelectedAction=action;
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True,world.Message);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True,world.Message);
            world.Hud.DicePresentation.RefreshPresentation();Canvas.ForceUpdateCanvases();
            return world.State.pendingRoll;
        }
        [TestCase("single",1,2f)] [TestCase("advantage",2,2f)] [TestCase("disadvantage",2,2f)]
        public void ActualRollRotatesGeometryThenLandsOnSavedFaceWithoutTouchingRng(string kind,int count,float duration)
        {
            var ticket=Confirm(kind);var layer=world.Hud.DicePresentation;
            Assert.That(ticket.rawFaces.Count,Is.EqualTo(count));Assert.That(world.CurrentRollDuration,Is.EqualTo(duration).Within(.001));
            string state=CombatJson.Write(world.State);var die=layer.DieAt(0);Quaternion pose=die.Pose;
            var firstVertices=die.canvasRenderer.GetMesh().vertices;Assert.That(firstVertices.Length,Is.GreaterThan(30));
            world.AdvanceRollAnimation(duration*.3f);layer.RefreshPresentation();Canvas.ForceUpdateCanvases();var secondVertices=die.canvasRenderer.GetMesh().vertices;
            Assert.That(Quaternion.Angle(pose,die.Pose),Is.GreaterThan(1));Assert.That(firstVertices.SequenceEqual(secondVertices),Is.False,"D20 must rotate actual geometry, not only replace a number.");
            world.AdvanceRollAnimation(duration*.68f);layer.RefreshPresentation();Canvas.ForceUpdateCanvases();
            Assert.That(Time.timeScale,Is.Zero);Assert.That(layer.BreakdownVisible,Is.True);Assert.That(layer.VisibleDiceCount,Is.EqualTo(count));
            Assert.That(die.FaceCount,Is.EqualTo(20));Assert.That(die.AllocatedFaceLabels,Is.EqualTo(20));
            Assert.That(die.FrontFaceNormal.z,Is.LessThan(-.999f));Assert.That(die.DisplayedRaw,Is.EqualTo(ticket.rawFaces[0]));
            Assert.That(CombatJson.Write(world.State),Is.EqualTo(state),"Presentation must not consume RNG, budget, resources or world simulation.");
            Assert.That(layer.SelectedDieIndex,Is.EqualTo(ticket.rawFaces.IndexOf(ticket.selectedFace)));
            if(count==2){Assert.That(layer.DieAt(layer.SelectedDieIndex).Emphasis,Is.EqualTo(1));Assert.That(layer.DieAt(1-layer.SelectedDieIndex).Emphasis,Is.LessThan(.4f));}
        }
        [UnityTest] public IEnumerator ActualUpdateAutomaticallyReturnsToSlowtimeAndKeepsOriginalCommitControls()
        {
            var ticket=Confirm();var raw=ticket.rawFaces.ToArray();var rng=CombatJson.Write(world.State.random);float simulation=world.State.simulationSeconds;
            world.enabled=true;float end=Time.realtimeSinceStartup+3;
            while(world.State.mode==CombatMode.RollFreeze&&Time.realtimeSinceStartup<end){Assert.That(world.State.simulationSeconds,Is.EqualTo(simulation));yield return null;}
            world.enabled=false;Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(Time.timeScale,Is.EqualTo(world.Session.Config.focusScale));
            var layer=world.Hud.DicePresentation;layer.RefreshPresentation();Assert.That(layer.BreakdownVisible,Is.True);Assert.That(world.State.pendingRoll.rawFaces,Is.EqualTo(raw));Assert.That(CombatJson.Write(world.State.random),Is.EqualTo(rng));
            Assert.That(layer.Canvas,Is.Not.SameAs(world.Hud.Canvas));Assert.That(layer.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget),Is.True);
            foreach(string id in new[]{"commit","cancel","reroll","modify"})Assert.That(world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id).gameObject.activeInHierarchy,Is.True,id);
            Assert.That(world.ConfirmCurrentDecision().success,Is.True,world.Message);
        }
        [UnityTest] public IEnumerator StandardSingleAutomaticRollRetainsAtLeastOnePointEightVisibleSecondsAfterStalledConfirmFrame()
        { yield return MeasureAutomaticDuration("single",1.8); }
        [UnityTest] public IEnumerator StandardAdvantageAutomaticRollRetainsAtLeastOneVisibleSecondAfterStalledConfirmFrame()
        { yield return MeasureAutomaticDuration("advantage",1.8); }
        [UnityTest] public IEnumerator StandardDisadvantageAutomaticRollRetainsAtLeastOneVisibleSecondAfterStalledConfirmFrame()
        { yield return MeasureAutomaticDuration("disadvantage",1.8); }
        IEnumerator MeasureAutomaticDuration(string kind,double minimum)
        {
            // Explicitly model a synchronous disk stall before the first rendered roll frame.
            // The following Unity delta includes this old work; animation must exclude it.
            System.Threading.Thread.Sleep(350);
            Confirm(kind);var clock=System.Diagnostics.Stopwatch.StartNew();world.enabled=true;
            yield return null;
            Assert.That(world.RollElapsed,Is.LessThan(.1f),"The first automatic sample must establish the visible-frame baseline, not import the previous Unity frame delta.");
            while(world.State.mode==CombatMode.RollFreeze&&clock.Elapsed.TotalSeconds<5)yield return null;
            world.enabled=false;
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));
            TestContext.WriteLine(kind+" actual visible seconds="+clock.Elapsed.TotalSeconds+" configured="+world.CurrentRollDuration);
            Assert.That(clock.Elapsed.TotalSeconds,Is.GreaterThanOrEqualTo(minimum));
        }
        [UnityTest] public IEnumerator AutomaticClockExcludesPauseResumeAndManualAdvancementIsNotDoubleCounted()
        {
            Confirm();world.AdvanceRollAnimation(.25f);world.enabled=true;yield return null;
            Assert.That(world.RollElapsed,Is.EqualTo(.25f).Within(.02f),"A manual fixture sample must not also become automatic delta.");
            Assert.That(world.Command(CommandKind.Pause).success,Is.True);float before=world.RollElapsed;
            yield return new WaitForSecondsRealtime(.35f);Assert.That(world.RollElapsed,Is.EqualTo(before));
            Assert.That(world.Command(CommandKind.Resume).success,Is.True);yield return null;
            Assert.That(world.RollElapsed,Is.EqualTo(before).Within(.02f),"Resume must establish a new baseline after its persistence cost.");
            world.Menu=true;before=world.RollElapsed;yield return new WaitForSecondsRealtime(.3f);Assert.That(world.RollElapsed,Is.EqualTo(before));
            world.Menu=false;yield return null;Assert.That(world.RollElapsed,Is.EqualTo(before).Within(.02f));world.enabled=false;
        }
        [UnityTest] public IEnumerator LandingAudioTracksGeometryOncePerDrawAndPauseCannotCauseFalseLanding()
        {
            var ticket=Confirm();var audio=world.Audio;Assert.That(audio.MixerReady,Is.True);
            int lands=audio.PlayedCount(AudioCue.DiceLand),outcomes=OutcomeSounds(audio);
            float duration=world.CurrentRollDuration;
            world.AdvanceRollAnimation(duration*(DicePresentationLayer.SettleFraction-.01f));Assert.That(audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(lands));
            world.Command(CommandKind.Pause);yield return null;Assert.That(audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(lands));
            world.Command(CommandKind.Resume);world.AdvanceRollAnimation(duration*.02f);Assert.That(audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(lands+1));
            Assert.That(OutcomeSounds(audio),Is.EqualTo(outcomes));
            world.AdvanceRollAnimation(duration*(DicePresentationLayer.BreakdownFraction-DicePresentationLayer.SettleFraction));Assert.That(OutcomeSounds(audio),Is.EqualTo(outcomes+1));
            audio.ObserveRollAnimation(world.State.pendingRoll,.99f);audio.ResetObservationBaseline();audio.ObserveRollAnimation(world.State.pendingRoll,.99f);
            Assert.That(audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(lands+1));
            world.AdvanceRollAnimation(duration*.2f);ulong draw=ticket.rngAfter;string id=ticket.id;
            Assert.That(world.Command(CommandKind.Reroll).success,Is.True);Assert.That(world.State.pendingRoll.id,Is.EqualTo(id));Assert.That(world.State.pendingRoll.rngAfter,Is.GreaterThan(draw));
            yield return new WaitForSecondsRealtime(.03f);world.AdvanceRollAnimation(duration*(DicePresentationLayer.SettleFraction+.01f));
            Assert.That(audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(lands+2));
        }
        [UnityTest] public IEnumerator FocusBudgetUsesFifteenActualDecisionSecondsAndSameModeSelectionsCannotRefreshIt()
        {
            world.InitializePartyForTests(file);world.Controls.Map.Disable();world.enabled=false;
            foreach(var actor in world.State.actors){actor.majorRemaining=100;actor.x=0;actor.z=0;}
            foreach(var enemy in world.State.enemies){enemy.aiState=2;enemy.aiRemaining=100;enemy.x=4;enemy.z=0;}
            System.Threading.Thread.Sleep(350);Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            var clock=System.Diagnostics.Stopwatch.StartNew();world.enabled=true;bool selected=false;
            while(world.State.mode==CombatMode.TacticalFocus&&clock.Elapsed.TotalSeconds<18)
            {
                if(!selected&&clock.Elapsed.TotalSeconds>.25)
                {
                    float before=world.State.focusBudget.secondsRemaining;
                    Assert.That(world.SelectCommandActor("mage").success,Is.True);Assert.That(world.SelectCommandActor("fighter").success,Is.True);
                    Assert.That(world.State.focusBudget.secondsRemaining,Is.EqualTo(before));selected=true;
                }
                yield return null;
            }
            world.enabled=false;Assert.That(selected,Is.True);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(world.State.focusBudget.secondsRemaining,Is.Zero);
            TestContext.WriteLine("Focus actual seconds="+clock.Elapsed.TotalSeconds);
            Assert.That(clock.Elapsed.TotalSeconds,Is.GreaterThanOrEqualTo(14.95).And.LessThan(16.5));
        }
        [UnityTest] public IEnumerator FocusLossCallbackStopsDecisionClockEvenWhenPauseWriteFailsAndUpdateDoesNotRun()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);world.enabled=true;yield return null;world.enabled=false;
            float remaining=world.State.focusBudget.secondsRemaining;bool faultReached=false;
            world.Session.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeWrite){faultReached=true;throw new IOException("focus-loss-pause-write-fault");}};
            world.IgnoreApplicationFocus=false;world.SendMessage("OnApplicationFocus",false);
            Assert.That(faultReached,Is.True);Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus),"The failed Pause must preserve the authoritative original mode.");
            yield return new WaitForSecondsRealtime(.3f);Assert.That(world.State.focusBudget.secondsRemaining,Is.EqualTo(remaining));
            world.Session.Store.WriteFault=null;world.IgnoreApplicationFocus=true;world.enabled=true;yield return null;world.enabled=false;
            Assert.That(world.State.focusBudget.secondsRemaining,Is.EqualTo(remaining).Within(.02f),"The first restored Update must not debit the unavailable interval.");
        }
        [UnityTest] public IEnumerator FocusLossCallbackStopsRollClockEvenWhenPauseWriteFailsAndUpdateDoesNotRun()
        {
            Confirm();world.enabled=true;yield return null;world.enabled=false;
            float elapsed=world.RollElapsed;string ticket=world.State.pendingRoll.id;int[] raw=world.State.pendingRoll.rawFaces.ToArray();bool faultReached=false;
            world.Session.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeWrite){faultReached=true;throw new IOException("roll-loss-pause-write-fault");}};
            world.IgnoreApplicationFocus=false;world.SendMessage("OnApplicationFocus",false);
            Assert.That(faultReached,Is.True);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));
            yield return new WaitForSecondsRealtime(.3f);Assert.That(world.RollElapsed,Is.EqualTo(elapsed));
            world.Session.Store.WriteFault=null;world.IgnoreApplicationFocus=true;world.enabled=true;yield return null;world.enabled=false;
            Assert.That(world.RollElapsed,Is.EqualTo(elapsed).Within(.02f));Assert.That(world.State.pendingRoll.id,Is.EqualTo(ticket));Assert.That(world.State.pendingRoll.rawFaces,Is.EqualTo(raw));
        }
        [Test] public void SaveBreakdownUsesEnemySaveMeaningAndEditsUpdateTheSamePresentationPool()
        {
            var ticket=Confirm(action:"shove");world.AdvanceRollAnimation(world.CurrentRollDuration*.86f);var layer=world.Hud.DicePresentation;layer.RefreshPresentation();
            Assert.That(layer.BreakdownText,Does.Contain("DC  "+ticket.targetNumber));
            Assert.That(layer.BreakdownText,Does.Contain(ticket.hit?"敌方豁免失败 · 技能生效":"敌方豁免成功 · 技能抵抗"));
            int objects=layer.GetComponentsInChildren<Transform>(true).Length;
            for(int i=0;i<100;i++)layer.RefreshPresentation();
            Assert.That(layer.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
        }
        [Test] public void StandardFixtureDurationAndQuickDiceCompatibilityRemainEffective()
        {
            Confirm();world.Presentation.rollAnimationSeconds=20;Assert.That(world.CurrentRollDuration,Is.EqualTo(20));
            world.AdvanceRollAnimation(3);Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));
            world.QuickDice=true;Assert.That(world.DiceSpeed,Is.EqualTo(DiceAnimationSpeed.Fast));Assert.That(world.CurrentRollDuration,Is.EqualTo(world.Presentation.quickRollSeconds));
            world.QuickDice=false;Assert.That(world.DiceSpeed,Is.EqualTo(DiceAnimationSpeed.Standard));
        }
        [Test] public void UltraFastPersistsAndLegacyQuickOnlyPreferenceMigratesToFast()
        {
            world.ConfigureDiceAnimationSpeed(DiceAnimationSpeed.UltraFast);
            Assert.That(world.DiceSpeed,Is.EqualTo(DiceAnimationSpeed.UltraFast));Assert.That(world.CurrentRollDuration,Is.EqualTo(.3f).Within(.001));
            world.InitializeForTests(file,false);world.ContinueSaved();Assert.That(world.DiceSpeed,Is.EqualTo(DiceAnimationSpeed.UltraFast));
            File.WriteAllText(Path.Combine(directory,"p4-player-settings.json"),"{\"schema\":1,\"focusScale\":0.1,\"quickDice\":true,\"reactions\":{}}");
            world.InitializeForTests(file,false);world.ContinueSaved();Assert.That(world.DiceSpeed,Is.EqualTo(DiceAnimationSpeed.Fast));Assert.That(world.QuickDice,Is.True);
        }
        [Test] public void RerollRestartsTheSamePooledDiceAndDisplaysTheNewSavedRaw()
        {
            Confirm();world.AdvanceRollAnimation(world.CurrentRollDuration);Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));
            var layer=world.Hud.DicePresentation;layer.RefreshPresentation();var original=layer.DieAt(0);int labels=layer.GetComponentsInChildren<Text>(true).Length;
            Assert.That(world.Command(CommandKind.Modify).success,Is.True,world.Message);layer.RefreshPresentation();Assert.That(layer.BreakdownText,Does.Contain("骰后修正 +2"));
            world.State.Actor("fighter").inspiration=1;
            Assert.That(world.Command(CommandKind.Reroll).success,Is.True,world.Message);layer.RefreshPresentation();
            Assert.That(world.RollElapsed,Is.Zero);Assert.That(layer.Settled,Is.False);Assert.That(layer.DieAt(0),Is.SameAs(original));
            world.AdvanceRollAnimation(world.CurrentRollDuration*.86f);layer.RefreshPresentation();Assert.That(original.DisplayedRaw,Is.EqualTo(world.State.pendingRoll.rawFaces[0]));Assert.That(layer.GetComponentsInChildren<Text>(true).Length,Is.EqualTo(labels));
        }
    }
}
