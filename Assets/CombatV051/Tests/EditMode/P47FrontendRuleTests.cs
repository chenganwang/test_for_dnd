using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P47FrontendRuleTests:ProfileTestFixture
    {
        [Test] public void FreshDemoExplorationDoesNotForgeStoryProgressOrBypassTheLegacyChallengeGate()
        {
            string story=CombatJson.Write(profile.State.story);
            Assert.That(profile.Execute(P(ProfileCommandKind.StartChallenge)).reason,Is.EqualTo("challenge_locked"));
            var start=P(ProfileCommandKind.StartDemoExploration,c=>c.selectionId="BASE");
            Assert.That(profile.Execute(start).success,Is.True);
            Assert.That(M.kind,Is.EqualTo(ProfileModeKind.Challenge));Assert.That(M.HasFlag("DEMO_EXPLORATION"),Is.True);
            Assert.That(profile.State.meta.challengeUnlocked,Is.False);
            Assert.That(profile.State.meta.research,Is.Zero);
            Assert.That(CombatJson.Write(profile.State.story),Is.EqualTo(story));
            string run=CombatJson.Write(M);Assert.That(profile.Execute(start).duplicate,Is.True);
            Assert.That(CombatJson.Write(M),Is.EqualTo(run));
            Assert.That(profile.Execute(P(ProfileCommandKind.StartDemoExploration)).reason,Is.EqualTo("challenge_already_active"));
            Assert.That(CombatJson.Write(M),Is.EqualTo(run));
        }
        [Test] public void DemoDepartureClonesPreparationAndCannotOverwriteAnyPaidHistoricalStoryResult()
        {
            Travel("R1");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);
            string paid=CombatJson.Write(profile.State);int face=CState.pendingRoll.selectedFace;
            Assert.That(profile.Execute(P(ProfileCommandKind.StartDemoExploration)).reason,Is.EqualTo("safe_state_required"));
            Assert.That(CombatJson.Write(profile.State),Is.EqualTo(paid));Assert.That(CState.pendingRoll.selectedFace,Is.EqualTo(face));
            SetupProfile();var draft=profile.PreparationDraft;draft.defaultActiveActorId="mage";
            Assert.That(profile.SavePreparation(draft,"demo-draft").success);
            Run(ProfileCommandKind.StartDemoExploration,c=>c.selectionId="COMMAND");
            string departure=CombatJson.Write(M.departurePreparation);
            draft.defaultActiveActorId="rogue";Assert.That(profile.SavePreparation(draft,"future-draft").success);
            Assert.That(CState.activeActorId,Is.EqualTo("mage"));Assert.That(CombatJson.Write(M.departurePreparation),Is.EqualTo(departure));
            Assert.That(M.departurePreparation.preparation.primaryLawId,Is.EqualTo("COMMAND"));
        }
        [Test] public void DemoTutorialUnlockRequiresRealRoomProgressAndTheCompletedTutorialBridge()
        {
            string story=CombatJson.Write(profile.State.story);
            Run(ProfileCommandKind.StartDemoExploration);
            Assert.That(profile.Execute(P(ProfileCommandKind.UnlockChallenge,c=>c.qualified=true)).reason,Is.EqualTo("tutorial_encounter_incomplete"));
            First();Assert.That(profile.State.meta.challengeUnlocked,Is.False);
            Travel("R2");
            Assert.That(profile.Execute(P(ProfileCommandKind.UnlockChallenge)).reason,Is.EqualTo("tutorial_encounter_incomplete"));
            // The Runtime only supplies this observation after its bound sidecar reports Completed.
            Run(ProfileCommandKind.UnlockChallenge,c=>c.qualified=true);
            Assert.That(profile.State.meta.challengeUnlocked,Is.True);
            Assert.That(CombatJson.Write(profile.State.story),Is.EqualTo(story));
            Assert.That(profile.State.meta.unlockedSpecializations.Count,Is.EqualTo(3),"Exploration does not award Story-only free milestones.");
            Assert.That(profile.State.meta.research,Is.Zero);
        }
        [Test] public void ActualDemoThirdEncounterGrantsOnlyThisRunsBreakAbilityWithoutChangingDepartureOrStory()
        {
            string story=CombatJson.Write(profile.State.story);Run(ProfileCommandKind.StartDemoExploration);
            string departure=CombatJson.Write(M.departureAbilitySnapshot),plan=M.planHash;
            First();Third();Assert.That(M.abilities,Does.Contain("SMASH"));Assert.That(M.HasAbility("SMASH"),Is.True);
            Assert.That(CombatJson.Write(M.departureAbilitySnapshot),Is.EqualTo(departure));Assert.That(M.planHash,Is.EqualTo(plan));
            Assert.That(CombatJson.Write(profile.State.story),Is.EqualTo(story));Assert.That(profile.State.meta.research,Is.Zero);
            Travel("R2");Position(48,11);Run(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});
            Assert.That(profile.IsPassageOpen("P24_SMASH"),Is.True);
        }
    }
}
