using System;
using System.Collections;
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
        static int OutcomeSounds(BattleAudio audio)=>audio.PlayedCount(AudioCue.Success)+audio.PlayedCount(AudioCue.Failure)+audio.PlayedCount(AudioCue.Critical)+audio.PlayedCount(AudioCue.NaturalOne);
        [TestCase(DiceAnimationSpeed.Standard,2f)]
        [TestCase(DiceAnimationSpeed.Fast,.8f)]
        [TestCase(DiceAnimationSpeed.UltraFast,.3f)]
        public void P47EverySpeedKeepsTheD20RawFinalAndDefenseVisible(DiceAnimationSpeed speed,float duration)
        {
            world.SetDiceAnimationSpeed(speed);var ticket=Confirm("advantage");var layer=world.Hud.DicePresentation;
            Assert.That(world.CurrentRollDuration,Is.EqualTo(duration).Within(.001));
            string before=CombatJson.Write(world.State);
            world.AdvanceRollAnimation(duration*.99f);layer.RefreshPresentation();
            Assert.That(Time.timeScale,Is.Zero);Assert.That(layer.BreakdownVisible,Is.True);
            Assert.That(layer.DieAt(layer.SelectedDieIndex).DisplayedRaw,Is.EqualTo(ticket.selectedFace));
            Assert.That(layer.BreakdownText,Does.Contain("最终  "+ticket.total).And.Contain("AC  "+ticket.targetNumber));
            Assert.That(CombatJson.Write(world.State),Is.EqualTo(before));
            world.AdvanceRollAnimation(duration*.02f);layer.RefreshPresentation();
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(layer.BreakdownVisible,Is.True);
        }
        [Test] public void P47FixedNumberedFacesAndBounceTrajectoryActuallySettleWithoutIdleRebuild()
        {
            Confirm();var layer=world.Hud.DicePresentation;var die=layer.DieAt(0);string before=CombatJson.Write(world.State);
            foreach(float bounce in DicePresentationLayer.BounceFractions)
            {Assert.That(D20Graphic.FlightOffset(bounce,0).y,Is.EqualTo(0).Within(.002));Assert.That(D20Graphic.FlightOffset(bounce-.02f,0).y,Is.GreaterThan(.2f));}
            for(int face=1;face<=20;face++)
            {die.SetRawFace(face);die.SetResultAppearance(true);die.SetPose(1,0,1);Assert.That(die.DisplayedRaw,Is.EqualTo(face));Assert.That(die.FrontFaceNormal.z,Is.LessThan(-.999));Assert.That(die.MotionOffset,Is.EqualTo(Vector2.zero));}
            var numbers=die.GetComponentsInChildren<Text>(true).Select(t=>int.Parse(t.text)).OrderBy(n=>n).ToArray();
            Assert.That(numbers,Is.EqualTo(Enumerable.Range(1,20)));Canvas.ForceUpdateCanvases();int count=die.MeshBuildCount;
            for(int i=0;i<50;i++){die.SetPose(1,0,1);Canvas.ForceUpdateCanvases();}
            Assert.That(die.MeshBuildCount,Is.EqualTo(count));Assert.That(CombatJson.Write(world.State),Is.EqualTo(before));
        }
        [TestCase(1)] [TestCase(20)] public void P47NaturalFaceUsesTheActualPaidStreamAndDistinctOutcomeSound(int face)
        {
            for(ulong seed=1;seed<10000;seed++)
            {var rng=RandomStreams.Create(seed).visibleTactical;var copy=CombatJson.Clone(rng);if(RandomStreams.D20(copy)!=face)continue;world.State.random.visibleTactical=rng;break;}
            var ticket=Confirm();Assert.That(ticket.selectedFace,Is.EqualTo(face));
            world.AdvanceRollAnimation(world.CurrentRollDuration*.99f);var layer=world.Hud.DicePresentation;layer.RefreshPresentation();
            Assert.That(layer.DieAt(0).DisplayedRaw,Is.EqualTo(face));Assert.That(layer.DieAt(0).FrontFaceNormal.z,Is.LessThan(-.999));
            Assert.That(world.Audio.PlayedCount(face==1?AudioCue.NaturalOne:AudioCue.Critical),Is.EqualTo(1));
            Assert.That(world.Audio.DiceSoundTimeline.Last().cue,Is.EqualTo(face==1?"NaturalOne":"Critical"));
        }
        [UnityTest] public IEnumerator P47WoodRattleStopsForPauseResumesAndEndsAtTheActualLanding()
        {
            Confirm();float duration=world.CurrentRollDuration;world.AdvanceRollAnimation(duration*.24f);yield return null;
            Assert.That(world.Audio.DiceRattlePlaying,Is.True);Assert.That(world.Audio.HasClip(AudioCue.DiceThrow),Is.True);Assert.That(world.Audio.PlayedCount(AudioCue.DiceBounce),Is.GreaterThan(0));
            Assert.That(world.Command(CommandKind.Pause).success,Is.True);yield return null;
            Assert.That(world.Audio.DiceRattlePaused,Is.True);float elapsed=world.RollElapsed;
            yield return new WaitForSecondsRealtime(.06f);Assert.That(world.RollElapsed,Is.EqualTo(elapsed));
            Assert.That(world.Command(CommandKind.Resume).success,Is.True);yield return null;
            Assert.That(world.Audio.DiceRattlePlaying,Is.True);
            world.AdvanceRollAnimation(duration*.59f);yield return null;
            Assert.That(world.Audio.DiceRattlePlaying,Is.False);Assert.That(world.Audio.PlayedCount(AudioCue.DiceLand),Is.EqualTo(1));
        }
        [Test] public void P47ResultInformationRevealsOnlyInOrderAfterLanding()
        {
            Confirm();var layer=world.Hud.DicePresentation;
            string[] names={"Raw faces","Modifiers","Final total","AC or DC","Outcome"};
            float[] points={DicePresentationLayer.SettleFraction,DicePresentationLayer.ModifiersFraction,DicePresentationLayer.TotalFraction,DicePresentationLayer.DefenseFraction,DicePresentationLayer.BreakdownFraction};
            float previous=0;
            for(int step=0;step<points.Length;step++)
            {
                float next=points[step]+.001f;world.AdvanceRollAnimation(world.CurrentRollDuration*(next-previous));previous=next;layer.RefreshPresentation();
                for(int i=0;i<names.Length;i++)Assert.That(layer.GetComponentsInChildren<Text>(true).Single(t=>t.name==names[i]).gameObject.activeSelf,Is.EqualTo(i<=step),names[i]);
            }
        }
    }
}
