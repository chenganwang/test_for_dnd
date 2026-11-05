using System;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public abstract class ProfileTestFixture
    {
        protected ProfileSession profile;protected int seq;
        protected CombatState CState {get{return profile.ActiveCombat.State;}}
        protected ModeState M {get{return profile.ActiveMode;}}
        protected CombatConfig BaseConfig {get{return profile.BaseCombatConfig;}}
        [SetUp] public virtual void SetupProfile(){profile=ProfileSession.CreateNew("profile-tests",41,new CombatConfig(),new DungeonConfig());seq=0;}
        protected ProfileCommand P(ProfileCommandKind kind,Action<ProfileCommand> fill=null){var c=new ProfileCommand{id="p2-"+(++seq),kind=kind};if(fill!=null)fill(c);return c;}
        protected ProfileResult Run(ProfileCommandKind kind,Action<ProfileCommand> fill=null){var r=profile.Execute(P(kind,fill));Assert.That(r.success,Is.True,r.reason);return r;}
        protected CombatResult Core(CommandKind kind,Action<CombatCommand> fill=null)
        {var c=new CombatCommand{id="p2-core-"+(++seq),kind=kind,actorId="fighter",targetId=CState.enemies.Count>0?CState.enemies[0].id:null};if(fill!=null)fill(c);var r=profile.ExecuteCombat(c);Assert.That(r.success,Is.True,r.reason);return r;}
        protected void Position(float x,float z){foreach(var a in CState.actors){a.x=x;a.z=z;}}
        protected void Travel(string room){var t=profile.Config.Room(room);Position(t.x,t.z);Run(ProfileCommandKind.EnterRoom,c=>{c.roomId=room;c.qualified=true;});}
        protected void Activate(){Run(ProfileCommandKind.ActivateEncounter,c=>c.qualified=true);}
        // Enemy deaths/actor placements are state fixtures; room resolution/payment use production commands.
        protected void Win(bool reward=true){foreach(var e in CState.enemies)e.hp=0;Run(ProfileCommandKind.ResolveEncounter);if(reward&&profile.PendingReward!=null)Run(ProfileCommandKind.SkipReward);}
        protected void First(bool reward=true){Travel("R1");Activate();Win(reward);}
        protected void Third(bool reward=true){Travel("R2");Travel("R3");Activate();Win(reward);}
        protected void Upper(){Travel("R2");if(M.HasAbility("SMASH")){Position(48,11);Run(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});}Travel("R4");Activate();Win();Travel("R5");Activate();Win();Travel("R6");}
        protected void AtEvent(string id,string actor="mage") {var t=profile.Config.Event(id);Position(t.x,t.z);Run(ProfileCommandKind.OpenEvent,c=>{c.eventId=id;c.actorId=actor;});}
        protected static RngState Stream(string stream,int face)
        {for(ulong seed=1;seed<10000;seed++){var rng=RandomStreams.Create(seed);var r=stream=="event"?rng.eventDice:rng.backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(r))==face)return r;}throw new Exception("missing seed");}
        protected void StartChallenge(string law="BASE"){Run(ProfileCommandKind.StartChallenge,c=>{c.selectionId=law;c.seed=133;});}
        protected ReactionWindow Offer(string defender="fighter")
        {
            var enemy=CState.enemies[0];enemy.x=CState.Actor(defender).x+1;enemy.z=CState.Actor(defender).z;enemy.attackBonus=0;CState.Actor(defender).armorClass=10;CState.random.backgroundCombat=Stream("background",12);
            var r=profile.ActiveCombat.ResolveIncoming("outer-incoming-"+(++seq),enemy.id,defender,4,false,12,"root-"+seq);Assert.That(r.success,r.reason);Assert.That(r.deferred,r.reason);return CState.reactionWindows.Single(w=>w.id==r.reactionWindowId);
        }
    }
    public sealed class ProfileRuleTests:ProfileTestFixture
    {
        [Test] public void SafeExplorationHasOneBoundPartyAuthorityAndNoCombatChargeFarm()
        {
            Assert.That(profile.ActiveCombat,Is.Not.Null);Assert.That(CState.actors.Count,Is.EqualTo(3));Assert.That(CState.inCombat,Is.False);Assert.That(CState.enemies,Is.Empty);Assert.That(profile.ActiveRoom.templateId,Is.EqualTo("R0"));
            int charge=CState.charges;profile.Tick(30);Assert.That(CState.charges,Is.EqualTo(charge));Assert.That(CState.momentum,Is.Zero);Assert.That(profile.State.story.elapsedRealSeconds,Is.EqualTo(30));
            Assert.That(profile.ExecuteCombat(new CombatCommand{id="no-safe-aim",kind=CommandKind.StartAim}).success,Is.False);profile.Validate(profile.State);
        }
        [Test] public void EntryRequiresActualInsideFactAndWholeLivingPartyBeforeDoorsLock()
        {
            Assert.That(profile.Execute(P(ProfileCommandKind.EnterRoom,c=>{c.roomId="R1";c.qualified=true;})).reason,Is.EqualTo("not_inside_target_room"));Travel("R1");CState.Actor("mage").x=0;
            Assert.That(profile.Execute(P(ProfileCommandKind.ActivateEncounter,c=>c.qualified=true)).reason,Is.EqualTo("party_not_inside_room"));Position(26,0);Activate();Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.chargeCap,Is.EqualTo(2));Assert.That(profile.IsPassageOpen("P01"),Is.False);Assert.That(profile.EnemyTemplateFor(CState.enemies[0].id).id,Is.EqualTo("guard"));
        }
        [Test] public void RewardGateUnlockAndResolvedRevisitCannotReopenOrRefillEncounter()
        {
            First(false);Assert.That(profile.State.meta.challengeUnlocked);Assert.That(profile.PendingReward.candidates.Count,Is.EqualTo(3));Assert.That(profile.IsPassageOpen("P12"),Is.False);Run(ProfileCommandKind.SkipReward);Travel("R0");Travel("R1");Assert.That(CState.charges,Is.Zero);Assert.That(profile.Execute(P(ProfileCommandKind.ActivateEncounter,c=>c.qualified=true)).success,Is.False);Assert.That(M.encounters.Count,Is.EqualTo(1));
        }
        [Test] public void StoryDefaultBuildReachesAllFiveFixedEncountersGatesShortcutAndSettlementWithoutChallenge()
        {
            First();Third();Assert.That(M.HasAbility("SMASH"));Assert.That(profile.IsPassageOpen("P24_OUTER"),Is.False);Upper();Position(0,13);Run(ProfileCommandKind.OpenShortcut,c=>{c.gateId="P60_SHORTCUT";c.qualified=true;});Assert.That(profile.IsPassageOpen("P60_SHORTCUT"));Travel("R0");Travel("R6");Travel("R7");Activate();Assert.That(CState.enemies.Count,Is.EqualTo(2));Win();var settlement=P(ProfileCommandKind.SettlementOnce,c=>c.selectionId="victory");Assert.That(profile.Execute(settlement).success);Assert.That(profile.Execute(settlement).duplicate);Assert.That(M.stage,Is.EqualTo("Exploring"));Assert.That(M.HasFlag("STORY_COMPLETE"));Assert.That(profile.State.meta.research,Is.Zero);Assert.That(profile.State.challenge,Is.Null);Travel("R6");Assert.That(M.encounters.Count(e=>e.stage=="Resolved"),Is.EqualTo(5));
        }
        [Test] public void ChallengeWithoutDepartureSmashUsesOuterRouteAndCannotBorrowLaterStoryAbility()
        {
            First();StartChallenge();Assert.That(M.departureAbilitySnapshot,Is.Empty);First();Third();Assert.That(M.HasAbility("SMASH"),Is.False);Assert.That(profile.IsPassageOpen("P24_SMASH"),Is.False);Assert.That(profile.IsPassageOpen("P24_OUTER"));string before=CombatJson.Write(profile.State.challenge);Run(ProfileCommandKind.ContinueStory);Third();Assert.That(profile.State.story.HasAbility("SMASH"));Assert.That(CombatJson.Write(profile.State.challenge),Is.EqualTo(before));Run(ProfileCommandKind.ContinueChallenge);Assert.That(M.HasAbility("SMASH"),Is.False);
        }
        [Test] public void ChallengeBuildAppliesOnlyRealNextEncounterHooksAndSharedCap()
        {
            First();StartChallenge("COMMAND");Travel("R1");Activate();Assert.That(CState.chargeCap,Is.EqualTo(3));Assert.That(profile.ActiveCombat.Config.aimSeconds,Is.EqualTo(BaseConfig.aimSeconds*1.25f));Win(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="RESERVE");Travel("R2");Travel("R3");Activate();Assert.That(CState.charges,Is.EqualTo(2));Assert.That(CState.chargeCap,Is.EqualTo(3));Win(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="FOCUS");Travel("R2");Travel("R4");Activate();Assert.That(profile.ActiveCombat.Config.aimSeconds,Is.EqualTo(BaseConfig.aimSeconds*1.25f*.65f).Within(.00001));Assert.That(profile.State.story.build.relicSlots.All(x=>x==null));
        }
        [Test] public void ThirdRewardRequiresExplicitReplacementAndCommittedOfferCannotPayAgain()
        {
            First(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="RESERVE");Third(false);Run(ProfileCommandKind.ClaimReward,c=>c.selectionId="FOCUS");Travel("R2");Position(48,11);Run(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});Travel("R4");Activate();Win();Travel("R5");Activate();Win(false);
            Assert.That(profile.PendingReward.candidates,Is.EquivalentTo(new[]{"REACTION"}));string before=CombatJson.Write(M.build);Assert.That(profile.Execute(P(ProfileCommandKind.ClaimReward,c=>c.selectionId="REACTION")).reason,Is.EqualTo("explicit_replacement_required"));Assert.That(CombatJson.Write(M.build),Is.EqualTo(before));
            var c=P(ProfileCommandKind.ClaimReward,x=>{x.selectionId="REACTION";x.replacementSlot=0;});Assert.That(profile.Execute(c).success);Assert.That(profile.Execute(c).duplicate);Assert.That(M.build.relicSlots,Is.EqualTo(new[]{"REACTION","FOCUS"}));Assert.That(profile.Execute(P(ProfileCommandKind.ClaimReward,x=>x.selectionId="RESERVE")).success,Is.False);Assert.That(profile.ActiveCombat.Config.reactionRecovery,Is.EqualTo(BaseConfig.reactionRecovery*.7f));
        }
        [Test] public void QuotedEventCanChangeActorButPaidResultAndSingleRerollStayWithOriginalActor()
        {
            First();Travel("R2");AtEvent("EVENT_A","fighter");int initialBonus=profile.ActiveSafeEvent.bonus;Run(ProfileCommandKind.OpenEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Assert.That(profile.ActiveSafeEvent.actorId,Is.EqualTo("mage"));Assert.That(profile.ActiveSafeEvent.bonus,Is.Not.EqualTo(initialBonus));
            M.random.eventDice=Stream("event",1);string coreRng=CombatJson.Write(CState.random);int charge=CState.charges;Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Assert.That(profile.ActiveSafeEvent.success,Is.False);string face=CombatJson.Write(profile.ActiveSafeEvent.rawFaces);Run(ProfileCommandKind.CloseEvent,c=>c.eventId="EVENT_A");Run(ProfileCommandKind.OpenEvent,c=>{c.eventId="EVENT_A";c.actorId="fighter";});Assert.That(profile.ActiveSafeEvent.actorId,Is.EqualTo("mage"));Assert.That(CombatJson.Write(profile.ActiveSafeEvent.rawFaces),Is.EqualTo(face));
            Assert.That(profile.Execute(P(ProfileCommandKind.RerollEvent,c=>{c.eventId="EVENT_A";c.actorId="fighter";})).reason,Is.EqualTo("event_actor_locked"));int insp=CState.Actor("mage").inspiration;M.random.eventDice=Stream("event",20);Run(ProfileCommandKind.RerollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Assert.That(CState.Actor("mage").inspiration,Is.EqualTo(insp-1));Assert.That(profile.ActiveSafeEvent.rollCount,Is.EqualTo(2));Assert.That(profile.Execute(P(ProfileCommandKind.RerollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";})).success,Is.False);Run(ProfileCommandKind.ConfirmEvent,c=>c.eventId="EVENT_A");Assert.That(M.HasFlag("ROUTE_OPEN"));Assert.That(CState.charges,Is.EqualTo(charge));Assert.That(CombatJson.Write(CState.random),Is.EqualTo(coreRng));
        }
        [Test] public void EventFailureAlternativeRetainsWorldObstacleAndAllowsProgress()
        {
            First();Travel("R2");AtEvent("EVENT_A");M.random.eventDice=Stream("event",1);Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_A";c.actorId="mage";});Run(ProfileCommandKind.UseEventAlternative,c=>c.eventId="EVENT_A");Assert.That(M.HasFlag("ROUTE_OPEN"),Is.False);Assert.That(profile.Execute(P(ProfileCommandKind.OpenEvent,c=>c.eventId="EVENT_A")).success,Is.False);Travel("R3");Activate();Assert.That(profile.IsInEncounter);
        }
        [Test] public void SecondEventSuccessActuallyRemovesBossMechanismSpawn()
        {
            First();Third();AtEvent("EVENT_B");M.random.eventDice=Stream("event",20);Run(ProfileCommandKind.RollEvent,c=>{c.eventId="EVENT_B";c.actorId="mage";});Run(ProfileCommandKind.ConfirmEvent,c=>c.eventId="EVENT_B");Upper();Travel("R7");Activate();Assert.That(CState.enemies.Count,Is.EqualTo(1));Assert.That(profile.EnemyTemplateFor(CState.enemies[0].id).id,Is.EqualTo("boss"));
        }
        [TestCase("heal")] [TestCase("fighter")] [TestCase("mage")] [TestCase("rogue")]
        public void RestOnceHealsIncludingDownedOrOneCappedRoleResourceWithoutCharge(string choice)
        {
            First();Third();Upper();CState.Actor("fighter").hp=0;CState.Actor("mage").hp=1;CState.Actor("rogue").hp=1;
            if(choice!="heal"){CState.Actor("fighter").hp=1;CState.Actor("fighter").superiority=0;CState.Actor("mage").spellSlots=0;CState.Actor("rogue").inspiration=0;}
            Run(ProfileCommandKind.ShortRest,c=>{c.selectionId=choice=="heal"?"heal":"resource";c.actorId=choice;c.qualified=true;});
            if(choice=="heal")Assert.That(CState.Actor("fighter").hp,Is.EqualTo((int)Math.Ceiling(CState.Actor("fighter").maxHp*.35f)));else{var a=CState.Actor(choice);Assert.That(choice=="fighter"?a.superiority:choice=="mage"?a.spellSlots:a.inspiration,Is.EqualTo(1));}
            Assert.That(CState.charges,Is.Zero);Assert.That(profile.Execute(P(ProfileCommandKind.ShortRest,c=>{c.selectionId="heal";c.qualified=true;})).reason,Is.EqualTo("rest_already_used"));
        }
        [Test] public void FullResourceRestRefusesWithoutConsumingTheOnceChoice()
        {
            First();Third();Upper();Assert.That(profile.Execute(P(ProfileCommandKind.ShortRest,c=>{c.selectionId="resource";c.actorId="mage";c.qualified=true;})).reason,Is.EqualTo("resource_already_full"));Assert.That(M.usedRestIds,Is.Empty);CState.Actor("mage").spellSlots--;Run(ProfileCommandKind.ShortRest,c=>{c.selectionId="resource";c.actorId="mage";c.qualified=true;});Assert.That(CState.Actor("mage").spellSlots,Is.EqualTo(BaseConfig.Template(ActorRole.Mage).spellSlots));
        }
        [Test] public void EnvironmentConsumesBonusOnceAndAddsAtMostOneActualCombatFact()
        {
            First();Third();Travel("R2");Position(48,11);Run(ProfileCommandKind.OpenGate,c=>{c.gateId="P24_SMASH";c.qualified=true;});Travel("R4");Activate();Win();Travel("R5");Activate();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);Core(CommandKind.RevealRoll);Core(CommandKind.Cancel);Position(26,22);float momentum=CState.momentum;Run(ProfileCommandKind.UseEnvironment,c=>{c.selectionId="R5_SHIFT";c.qualified=true;c.sourceFactId="lever-1";});Assert.That(M.HasFlag("COVER_SHIFTED"));Assert.That(CState.Actor("fighter").bonusRemaining,Is.GreaterThan(0));Assert.That(CState.momentum,Is.GreaterThan(momentum));float after=CState.momentum;Assert.That(profile.Execute(P(ProfileCommandKind.UseEnvironment,c=>{c.selectionId="R5_SHIFT";c.qualified=true;c.sourceFactId="lever-2";})).success,Is.False);Assert.That(CState.momentum,Is.EqualTo(after));
        }
        [Test] public void StoryRetryRestoresWholeEncounterStartIncludingPaidRollAndWorldPositions()
        {
            Travel("R1");Activate();var snapshot=profile.ActiveEncounter.encounterStartSnapshot.Clone();Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);int face=CState.pendingRoll.selectedFace;Assert.That(CState.charges,Is.EqualTo(1));CState.Actor("fighter").hp=1;CState.Actor("fighter").x=32;CState.enemies[0].hp=2;RandomStreams.D20(CState.random.backgroundCombat);Run(ProfileCommandKind.RetryStoryEncounter);
            snapshot.encounterId=profile.ActiveEncounter.attemptId;snapshot.receipts.Clear();Assert.That(CombatJson.Write(CState),Is.EqualTo(CombatJson.Write(snapshot)));Assert.That(profile.ActiveEncounter.attemptNumber,Is.EqualTo(1));Core(CommandKind.EnterFocus);Core(CommandKind.ConfirmRoll);Assert.That(CState.pendingRoll.selectedFace,Is.EqualTo(face));
        }
        [Test] public void ChallengeRetryIsForbiddenAndSettlementPaysOnlyCompletedObjectivesOnce()
        {
            First();StartChallenge();First();Travel("R2");Travel("R3");Activate();Assert.That(profile.Execute(P(ProfileCommandKind.RetryStoryEncounter)).reason,Is.EqualTo("challenge_retry_forbidden"));foreach(var a in CState.actors)a.hp=0;var c=P(ProfileCommandKind.SettlementOnce,x=>x.selectionId="defeat");Assert.That(profile.Execute(c).success);Assert.That(profile.Execute(c).duplicate);Assert.That(profile.State.meta.research,Is.EqualTo(1));Assert.That(profile.State.activeMode,Is.EqualTo(ProfileModeKind.Story));Assert.That(profile.State.challenge.stage,Is.EqualTo("Settled"));Run(ProfileCommandKind.ContinueStory);Assert.That(profile.Execute(P(ProfileCommandKind.SettlementOnce,x=>x.selectionId="defeat")).success,Is.False);Assert.That(profile.State.meta.research,Is.EqualTo(1));
        }
        [Test] public void ChallengeVictoryHasFiveObjectivesNineResearchAndLeavesStorySnapshotUntouched()
        {
            First();string story=CombatJson.Write(profile.State.story);StartChallenge();First();Third();Upper();Travel("R7");Activate();Win();Run(ProfileCommandKind.SettlementOnce,c=>c.selectionId="victory");Assert.That(profile.State.meta.research,Is.EqualTo(9));Assert.That(profile.State.meta.lastSettlement.earned,Is.EqualTo(9));Assert.That(CombatJson.Write(profile.State.story),Is.EqualTo(story));Assert.That(profile.State.meta.settledRunIds.Count,Is.EqualTo(1));
        }
        [Test] public void TrainingCostsOnceOnlyChangesStoryAndFutureDepartureAndCannotMutateExistingRun()
        {
            First();Travel("R0");StartChallenge();string old=CombatJson.Write(profile.State.challenge);int baseHp=CState.Actor("fighter").maxHp;Run(ProfileCommandKind.ContinueStory);profile.State.meta.research=3;Position(0,0);var c=P(ProfileCommandKind.TrainParty);Assert.That(profile.Execute(c).success);Assert.That(profile.Execute(c).duplicate);Assert.That(CState.Actor("fighter").maxHp,Is.EqualTo(baseHp+2));Assert.That(profile.State.meta.research,Is.Zero);Assert.That(profile.State.meta.trainingLevel,Is.EqualTo(1));Assert.That(CombatJson.Write(profile.State.challenge),Is.EqualTo(old));Assert.That(profile.Execute(P(ProfileCommandKind.TrainParty)).reason,Is.EqualTo("training_already_complete"));Run(ProfileCommandKind.ContinueChallenge);Assert.That(CState.Actor("fighter").maxHp,Is.EqualTo(baseHp));Run(ProfileCommandKind.SettlementOnce,x=>x.selectionId="abandon");StartChallenge();Assert.That(M.departureTraining,Is.EqualTo(1));Assert.That(CState.Actor("fighter").maxHp,Is.EqualTo(baseHp+2));
        }
        [Test] public void ImmutableDepartureSnapshotAndConfigMutationAreRejectedWithoutPublishing()
        {
            First();StartChallenge();M.departureAbilitySnapshot.Add("SMASH");Assert.That(profile.SaveCurrent("tampered-plan").reason,Is.EqualTo("dungeon_plan_mismatch"));SetupProfile();profile.Config.enemies[0].hp++;Assert.That(profile.SaveCurrent("changed-config").reason,Is.EqualTo("profile_config_changed"));
        }
        [Test] public void ProfileRevisionModeRoomBindingAndPayloadCASRejectStaleCommands()
        {
            long binding=profile.State.bindingRevision;var oldCore=profile.ActiveCombat;Run(ProfileCommandKind.SaveCurrent);Assert.That(profile.ExecuteCombat(new CombatCommand{id="old-core",kind=CommandKind.SwitchActive,actorId="mage"},binding).reason,Is.EqualTo("stale_binding"));Assert.That(oldCore.SwitchActive("old-callback","mage").reason,Is.EqualTo("stale_binding"));
            Assert.That(profile.Execute(P(ProfileCommandKind.SaveCurrent,c=>c.expectedRevision=0)).reason,Is.EqualTo("stale_profile_revision"));Assert.That(profile.Execute(P(ProfileCommandKind.SaveCurrent,c=>c.modeInstanceId="other-mode")).reason,Is.EqualTo("mode_instance_mismatch"));Assert.That(profile.Execute(P(ProfileCommandKind.SaveCurrent,c=>c.roomInstanceId="other-room")).reason,Is.EqualTo("room_instance_mismatch"));var command=P(ProfileCommandKind.SaveCurrent);Assert.That(profile.Execute(command).success);command.kind=ProfileCommandKind.StartStory;Assert.That(profile.Execute(command).reason,Is.EqualTo("command_payload_mismatch"));
        }
        [Test] public void SafeSwitchUsesSameActorDTOAndManualPauseDoesNotCountRunDuration()
        {
            Core(CommandKind.SwitchActive,c=>c.actorId="mage");Assert.That(CState.activeActorId,Is.EqualTo("mage"));Core(CommandKind.Pause);profile.Tick(10);Assert.That(M.elapsedRealSeconds,Is.Zero);Core(CommandKind.Resume);profile.Tick(.5f);Assert.That(M.elapsedRealSeconds,Is.EqualTo(.5f));
        }
        [Test] public void TrainingRefusesInsufficientResearchAndChallengeLocationWithoutCosts()
        {
            Assert.That(profile.Execute(P(ProfileCommandKind.TrainParty)).reason,Is.EqualTo("research_unavailable"));First();StartChallenge();profile.State.meta.research=3;Assert.That(profile.Execute(P(ProfileCommandKind.TrainParty)).reason,Is.EqualTo("training_story_entry_required"));Assert.That(profile.State.meta.research,Is.EqualTo(3));Assert.That(profile.State.meta.trainingLevel,Is.Zero);
        }
    }
}
