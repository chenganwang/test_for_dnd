using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Deterministic rule fixtures. Geometry evidence is explicit; these tests do
    // not claim actual Unity movement, collision, presentation or human play.
    public sealed class P5DomainTests
    {
        CombatSession session;int sequence;
        CombatState S=>session.State;ActorState Fighter=>S.Actor("fighter");ActorState Rogue=>S.Actor("rogue");ActorState Mage=>S.Actor("mage");ActorState Enemy=>S.enemies[0];
        [SetUp] public void Setup()
        {
            var config=new CombatConfig{passiveMomentumPerSecond=0,p5=new P5Config()};var state=CombatState.CreatePartyP1("p5-tests","run",17,config);
            foreach(var a in state.actors){a.x=0;a.z=0;a.lockedX=1;a.lockedZ=0;}
            state.enemies[0].x=1;state.enemies[0].z=0;state.enemies[0].lockedX=-1;state.enemies[0].lockedZ=0;state.enemies[0].hp=state.enemies[0].maxHp=500;state.enemies[0].breakValue=state.enemies[0].breakMaximum=10;
            CombatSession.InitializeP5State(state,config);session=new CombatSession(state,config);sequence=0;
        }
        CombatCommand C(CommandKind kind,string actor="fighter",string action="shove",string target=null)=>new CombatCommand{id="p5/"+(++sequence),kind=kind,actorId=actor,actionId=action,targetId=target??Enemy.id};
        CombatResult Run(CombatCommand command){var result=session.Execute(command);Assert.That(result.success,Is.True,result.reason);return result;}
        CombatResult Run(CommandKind kind)=>Run(C(kind));
        SetupToken Token(string kind="off_balance",string owner="mage",bool neutral=false)
        {
            string id="fixture-token/"+(++sequence);var token=new SetupToken{id=id,ownerId=neutral?null:owner,targetId=Enemy.id,remaining=8,p5=new P5OpportunityData{kind=kind,sourceActionId=id+"/producer",encounterAttemptId=S.encounterId,neutral=neutral}};
            if(kind=="bound"){token.p5.sourceConditionId=id+"/condition";token.remaining=4;Enemy.statuses.Add(new StatusState{id="p5-slow",sourceId=owner,remaining=4,p5=new P5ConditionData{instanceId=token.p5.sourceConditionId,sourceActionId=id+"/producer",moveMultiplier=.7f}});}
            S.p5.opportunities.Add(token);return token;
        }
        string Confirm(string action="shove",int natural=15,string actor="fighter",string token=null)
        {
            if(S.mode!=CombatMode.TacticalFocus)Run(CommandKind.EnterFocus);S.random.visibleTactical=CombatRuleTests.StreamFor(natural);
            var c=C(CommandKind.ConfirmRoll,actor,action);c.p5=new P5CommandData{opportunityId=token};return Run(c).ticketId;
        }
        string Queue(string action="shove",int natural=15,string actor="fighter",string token=null)
        {Confirm(action,natural,actor,token);Run(CommandKind.RevealRoll);string id=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);return id;}
        CombatResult Resolve(string queue,P5CommandData geometry=null)
        {
            var start=C(CommandKind.StartQueued);start.queueId=queue;Run(start);session.Tick(.5f,true,0);var resolve=C(CommandKind.ResolveQueued);resolve.queueId=queue;resolve.p5=geometry;return Run(resolve);
        }
        CombatCommand BeginFollowup(string token,string actor="fighter")
        {var c=C(CommandKind.StartBasicAttack,actor,"basic_followup");c.p5=new P5CommandData{opportunityId=token};return c;}
        CombatResult Contact(CombatCommand start,int natural=12,P5CommandData geometry=null)
        {Run(start);session.Tick(.13f,true,0);S.random.backgroundCombat=CombatRuleTests.StreamFor(natural);var c=C(CommandKind.ResolveBasicAttack,start.actorId);c.orderId=start.id;c.authority=CommandAuthority.System;c.p5=geometry;return Run(c);}
        CombatCommand Prepare(string actor,string target=null)
        {var c=C(CommandKind.StartPreparation,actor,target:target??Enemy.id);c.replaceExisting=true;c.qualified=true;return c;}

        [Test] public void LegacyConfigAndCommandsRemainNullAndP5OverridesDoNotMutateTheirLists()
        {
            var legacy=new CombatConfig();string before=CombatJson.Write(legacy);var modern=CombatJson.Clone(legacy);modern.p5=new P5Config();
            Assert.That(legacy.Action("shove").rollType,Is.EqualTo("Save"));Assert.That(modern.Action("shove").rollType,Is.EqualTo("Check"));Assert.That(modern.actions.Single(a=>a.id=="shove").rollType,Is.EqualTo("Save"));
            Assert.That(CombatJson.Write(legacy),Is.EqualTo(before));Assert.That(CombatJson.Write(new CombatCommand{id="old"}),Does.Not.Contain("\"p5\""));
        }
        [TestCase(1,100,true)] [TestCase(20,-100,false)]
        public void ShoveCheckBelongsToFighterWithoutAttackNaturalOverrides(int face,int strength,bool succeeds)
        {Fighter.strengthModifier=strength;Confirm(natural:face);Assert.That(S.pendingRoll.rollType,Is.EqualTo("Check"));Assert.That(S.pendingRoll.rollOwnerId,Is.EqualTo(Fighter.id));Assert.That(S.pendingRoll.hit,Is.EqualTo(succeeds));Assert.That(S.pendingRoll.critical,Is.False);}
        [Test] public void OwnCheckCanUseOwnInspirationButCannotModifyEnemyConSave()
        {Confirm();Run(CommandKind.RevealRoll);int inspiration=Fighter.inspiration;Run(CommandKind.Modify);Assert.That(Fighter.inspiration,Is.EqualTo(inspiration-1));Run(CommandKind.Cancel);Confirm("control",15,"mage");Run(CommandKind.RevealRoll);string before=CombatJson.Write(S);Assert.That(session.Execute(C(CommandKind.Modify)).reason,Is.EqualTo("cannot_modify_enemy_save"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));}
        [Test] public void PreviewIsPureAndExplicitEmptySelectionDoesNotConsumeAvailableOpportunity()
        {var token=Token();string before=CombatJson.Write(S);var c=C(CommandKind.ConfirmRoll);c.p5=new P5CommandData{opportunityId=token.id};Assert.That(session.QuoteP5(c).legal);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Confirm();Assert.That(S.p5.opportunities.Single().consumed,Is.False);}
        [TestCase("owner")] [TestCase("expired")] [TestCase("wrong-target")] [TestCase("los")] [TestCase("range")] [TestCase("slot")]
        public void IllegalConfirmationDoesNotSpendAnyResourceOrRandomState(string failure)
        {
            var token=Token(owner:failure=="owner"?"fighter":"mage");if(failure=="expired")token.remaining=0;if(failure=="wrong-target"){var other=CombatJson.Clone(Enemy);other.id="other-enemy";S.enemies.Add(other);token.targetId=other.id;}
            Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);c.p5=new P5CommandData{opportunityId=token.id};if(failure=="los")c.lineOfSight=false;if(failure=="range")Enemy.x=50;
            if(failure=="slot"){c.actorId="mage";c.actionId="control";token.ownerId="fighter";Mage.spellSlots=0;}
            string before=CombatJson.Write(S);var r=session.Execute(c);Assert.That(r.success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void OnlyFirstActorConsumesAndDuplicateIdCannotPayOrRollTwice()
        {
            var token=Token();Run(CommandKind.EnterFocus);var c=C(CommandKind.ConfirmRoll);c.p5=new P5CommandData{opportunityId=token.id};Run(c);string after=CombatJson.Write(S);Assert.That(session.Execute(c).duplicate);Assert.That(CombatJson.Write(S),Is.EqualTo(after));
            Run(CommandKind.RevealRoll);Run(CommandKind.Commit);var second=C(CommandKind.ConfirmRoll,"rogue","sneak");second.p5=new P5CommandData{opportunityId=token.id};int charges=S.charges;Assert.That(session.Execute(second).reason,Is.EqualTo("opportunity_already_used"));Assert.That(S.charges,Is.EqualTo(charges));
        }
        [Test] public void ConsumingBoundDoesNotDeleteConditionAndLockedBenefitSurvivesItsExpiry()
        {
            var token=Token("bound");Enemy.statuses[0].remaining=.25f;token.remaining=.25f;string q=Queue(token:token.id);Assert.That(Enemy.statuses,Has.Count.EqualTo(1));Assert.That(S.rolls.Last().p5.pushDistance,Is.EqualTo(3));
            var result=Resolve(q);Assert.That(result.hit,Is.True);Assert.That(Enemy.x,Is.EqualTo(4).Within(.001));Assert.That(Enemy.statuses,Is.Empty);Assert.That(S.rolls.Last().p5.effective);Assert.That(S.p5.opportunities.Single().consumed);
        }
        [Test] public void BoundExpiresWithItsSpecificConditionAndLeavesOtherOpportunityAlone()
        {var bound=Token("bound");Token("off_balance","rogue");Enemy.statuses.Clear();Assert.That(session.GetOpportunityChoices("fighter",Enemy.id,"shove").Count,Is.EqualTo(1));session.Tick(.1f,true,0);Assert.That(S.p5.opportunities.Single(t=>t.id==bound.id).p5.status,Is.EqualTo("Expired"));Assert.That(S.p5.opportunities.Single(t=>t.p5.kind=="off_balance").p5.status,Is.EqualTo("Available"));}
        [Test] public void PaidCancelKeepsChargeAndOpportunitySpentButReleasesOnlyHeldSlot()
        {var token=Token("exposed","fighter");int slots=Mage.spellSlots;Confirm("control",10,"mage",token.id);Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(Mage.heldSpellSlots,Is.EqualTo(1));Run(CommandKind.RevealRoll);Run(CommandKind.Cancel);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(Mage.heldSpellSlots,Is.Zero);}
        [Test] public void FreezeStopsOpportunityConditionPreparationAndStartDeadline()
        {var token=Token("bound");Confirm(token:token.id);string before=CombatJson.Write(S.p5);float condition=Enemy.statuses[0].remaining;session.Tick(20);Assert.That(CombatJson.Write(S.p5),Is.EqualTo(before));Assert.That(Enemy.statuses[0].remaining,Is.EqualTo(condition));Assert.That(S.simulationSeconds,Is.Zero);Run(CommandKind.RevealRoll);Assert.That(S.pendingRoll.p5.startDeadlineSimulationTime,Is.EqualTo(2));}
        [Test] public void TwoSimulationSecondsExpirePaidResultAndDoNotRefundIt()
        {var token=Token();Confirm(token:token.id);Run(CommandKind.RevealRoll);session.Tick(19,true,0);Assert.That(S.pendingRoll,Is.Not.Null);session.Tick(1.01f,true,0);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.rolls.Last().stage,Is.EqualTo("Canceled"));Assert.That(Fighter.heldBy,Is.Null);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);}
        [Test] public void QueueCannotStoreHighFaceOutsideRangeAndRerollDoesNotResetDeadline()
        {Confirm();Run(CommandKind.RevealRoll);session.Tick(10,true,0);float deadline=S.pendingRoll.p5.startDeadlineSimulationTime;Run(CommandKind.Reroll);session.Tick(50);Run(CommandKind.RevealRoll);Assert.That(S.pendingRoll.p5.startDeadlineSimulationTime,Is.EqualTo(deadline));string queue=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);Enemy.x=99;session.Tick(1.01f,true,0);Assert.That(S.queue.Single(q=>q.id==queue).stage,Is.EqualTo("Canceled"));Assert.That(S.charges,Is.EqualTo(1));}
        [Test] public void ZeroChargeFreeFollowupConsumesAtStartUsesBackgroundRollAndSameMajor()
        {
            S.charges=0;var token=Token();var start=BeginFollowup(token.id);ulong visible=S.random.visibleTactical.drawCount;Run(start);Assert.That(S.p5.opportunities.Single().consumed);Assert.That(S.random.backgroundCombat.drawCount,Is.Zero);
            Assert.That(session.Execute(C(CommandKind.StartBasicAttack)).success,Is.False);session.Tick(.13f,true,0);S.random.backgroundCombat=CombatRuleTests.StreamFor(12);var contact=C(CommandKind.ResolveBasicAttack);contact.orderId=start.id;contact.authority=CommandAuthority.System;
            var result=Run(contact);Assert.That(result.damage,Is.GreaterThan(0));Assert.That(S.charges,Is.Zero);Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(Fighter.majorRemaining,Is.EqualTo(session.Config.majorRecovery));Assert.That(S.momentum,Is.EqualTo(10));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Assert.That(session.Execute(contact).duplicate);Assert.That(S.momentum,Is.EqualTo(10));
        }
        [Test] public void MissedFreeFollowupStillSpendsOpportunityAndGivesNoReward()
        {S.charges=0;var token=Token();float prior=Enemy.breakValue;var hit=Contact(BeginFollowup(token.id),1);Assert.That(hit.damage,Is.Zero);Assert.That(S.p5.opportunities.Single().consumed);Assert.That(Enemy.breakValue,Is.EqualTo(prior));Assert.That(S.momentum,Is.Zero);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);}
        [Test] public void DefaultAiBasicDoesNotConsumeOrGetOpportunityAdvantage()
        {S.charges=0;Token();var c=C(CommandKind.StartBasicAttack);c.authority=CommandAuthority.AllyAI;c.flag=true;var result=Contact(c);Assert.That(S.p5.opportunities.Single().consumed,Is.False);Assert.That(result.rawFaces,Has.Count.EqualTo(1));Assert.That(S.momentum,Is.EqualTo(1));}
        [Test] public void AiCannotSelectFollowupButExactPlayerOrderAuthorizesSystemStart()
        {
            var token=Token();var auto=BeginFollowup(token.id);auto.authority=CommandAuthority.AllyAI;auto.flag=true;Assert.That(session.Execute(auto).reason,Is.EqualTo("ai_opportunity_forbidden"));
            var order=C(CommandKind.IssueBasicOrder,action:"Followup");order.p5=new P5CommandData{opportunityId=token.id};Run(order);Assert.That(S.p5.opportunities.Single().consumed,Is.False);
            var start=BeginFollowup(token.id);start.authority=CommandAuthority.System;start.orderId="wrong";Assert.That(session.Execute(start).reason,Is.EqualTo("followup_order_authorization_missing"));start.orderId=order.id;Run(start);Assert.That(S.p5.opportunities.Single().consumed);Assert.That(Fighter.basicOrder.stage,Is.EqualTo("Finished"));
        }
        [TestCase(CommandAuthority.Player)] [TestCase(CommandAuthority.AllyAI)]
        public void OrdinaryAttackCannotStealAnAuthorizedPendingFollowup(CommandAuthority authority)
        {var token=Token();var order=C(CommandKind.IssueBasicOrder,action:"Followup");order.p5=new P5CommandData{opportunityId=token.id};Run(order);var basic=C(CommandKind.StartBasicAttack);basic.authority=authority;string before=CombatJson.Write(S);Assert.That(session.Execute(basic).reason,Is.EqualTo("followup_order_pending"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(S.p5.opportunities.Single().consumed,Is.False);}
        [Test] public void OwnOpportunityOnlyLeavesNormalBasicAndNeutralConsumptionDoesNotEarnSynergy()
        {var own=Token(owner:"fighter");Assert.That(session.Execute(BeginFollowup(own.id)).reason,Is.EqualTo("opportunity_requires_other_actor"));S.p5.opportunities.Clear();var neutral=Token(neutral:true);Contact(BeginFollowup(neutral.id));Assert.That(S.momentumLedger.setupEarned,Is.Zero);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);}
        [Test] public void PaidOrderCanTakeOverCompletedBasicRecoveryButNeverUnfinishedContact()
        {var basic=C(CommandKind.StartBasicAttack);Run(basic);Run(CommandKind.EnterFocus);Assert.That(session.Quote("fighter",Enemy.id,"shove").legal,Is.False);session.Tick(1.3f,true,0);var contact=C(CommandKind.ResolveBasicAttack);contact.orderId=basic.id;contact.authority=CommandAuthority.System;Run(contact);Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));Assert.That(session.P5MajorAvailable(Fighter));Assert.That(session.Quote("fighter",Enemy.id,"shove").legal);Confirm();Assert.That(Fighter.majorRemaining,Is.Zero);Assert.That(Fighter.heldBy,Is.Not.Null);}
        [TestCase(5)] [TestCase(6)] public void ShoveNearMissOnlyBreaksOnceWithoutMovementOrResourceReward(int face)
        {S.charges=1;S.focusBudget.secondsRemaining=1;Enemy.breakValue=1;var token=Token();float x=Enemy.x;var result=Resolve(Queue(natural:face,token:token.id));Assert.That(result.hit,Is.False);Assert.That(Enemy.x,Is.EqualTo(x));Assert.That(Enemy.breakValue,Is.Zero);Assert.That(S.momentum,Is.Zero);Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Assert.That(S.rolls.Last().p5.weakEffect);}
        [Test] public void BlockedShoveIsNotEffectiveSynergyAndUninterruptibleEvidenceKeepsWindup()
        {S.charges=1;var token=Token("bound");Enemy.aiState=1;Enemy.attackGroupId="danger";string queue=Queue(token:token.id);var result=Resolve(queue,new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,x=Enemy.x,z=Enemy.z,hasDisplacement=true,allowInterrupt=false}}});Assert.That(S.rolls.Last().p5.effective,Is.False);Assert.That(S.momentumLedger.setupEarned,Is.Zero);Assert.That(Enemy.aiState,Is.EqualTo(1));Assert.That(Enemy.attackGroupId,Is.EqualTo("danger"));}
        [Test] public void SneakUsesNearbyAllyWithoutRearButExposedAloneCannotCreateEligibility()
        {Assert.That(session.Quote("rogue",Enemy.id,"sneak").legal);Fighter.x=20;Mage.x=20;var token=Token("exposed","fighter");var c=C(CommandKind.ConfirmRoll,"rogue","sneak");c.p5=new P5CommandData{opportunityId=token.id};Assert.That(session.QuoteP5(c).reason,Is.EqualTo("sneak_requires_advantage_or_ally"));token.p5.kind="off_balance";Assert.That(session.QuoteP5(c).legal);Rogue.statuses.Add(new StatusState{id="suppressed",remaining=5});Assert.That(session.QuoteP5(c).reason,Is.EqualTo("sneak_requires_advantage_or_ally"));}
        [Test] public void CanceledAdvantageStillAllowsNearbyAllySneakAndRollsOneDie()
        {var token=Token(owner:"fighter");Rogue.statuses.Add(new StatusState{id="suppressed",remaining=5});Confirm("sneak",12,"rogue",token.id);Assert.That(S.pendingRoll.advantage&&S.pendingRoll.disadvantage);Assert.That(S.pendingRoll.rawFaces,Has.Count.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);}
        [Test] public void ExposedIgnoresOnlyExplicitFrontalShieldAndMeleeDoesNotGainCoverAc()
        {var token=Token("exposed","fighter");var c=C(CommandKind.ConfirmRoll,"rogue","sneak");c.p5=new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,coverAcBonus=2,frontalShieldAcBonus=2}}};int ac=Enemy.armorClass;Assert.That(session.QuoteP5(c).targetNumber,Is.EqualTo(ac+2));c.p5.opportunityId=token.id;Assert.That(session.QuoteP5(c).targetNumber,Is.EqualTo(ac));}
        [Test] public void FinalBlowSynergyPaysOnceMergesSameRootAndHonorsEncounterBudget()
        {S.charges=1;S.focusBudget.secondsRemaining=1;S.momentumLedger.tacticalAwarded=95;Enemy.hp=1;var token=Token(owner:"fighter");var result=Resolve(Queue("sneak",15,"rogue",token.id));Assert.That(Enemy.Alive,Is.False);Assert.That(result.damage,Is.GreaterThan(0));Assert.That(S.momentumLedger.setupEarned,Is.EqualTo(5));Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(100));Assert.That(S.momentumLedger.p4Roots.Count(x=>x.id==S.rolls.Last().id),Is.EqualTo(1));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(.75f));}
        [Test] public void FullChargeFinalFollowupDiscardsWithoutPendingInventory()
        {Enemy.hp=1;var token=Token("exposed");Contact(BeginFollowup(token.id));Assert.That(Enemy.Alive,Is.False);Assert.That(S.charges,Is.EqualTo(2));Assert.That(S.momentum,Is.Zero);Assert.That(S.momentumLedger.fullCapDiscarded,Is.EqualTo(10));Assert.That(S.p5.opportunities.Single().consumed);}

        [Test] public void FighterBraceRequiresRealCommittedContactProducesOnceAndEventuallyReleasesOrder()
        {
            S.world.strings["enemyTemplate/"+Enemy.id]="guard";var prepare=Prepare("fighter","fighter");Run(prepare);session.Tick(.79f,true,0);Assert.That(Fighter.guardRemaining,Is.Zero);session.Tick(.02f,true,0);Assert.That(Fighter.guardRemaining,Is.GreaterThan(1.9f));Assert.That(S.p5.opportunities,Is.Empty);
            Enemy.aiState=1;Enemy.attackGroupId="guard-heavy";var contact=C(CommandKind.IncomingAttack,Enemy.id,target:"fighter");contact.rootId="guard-heavy";contact.amount=10;contact.qualified=true;contact.authority=CommandAuthority.System;Run(contact);
            Assert.That(S.p5.opportunities.Single().p5.kind,Is.EqualTo("off_balance"));Assert.That(S.p5.opportunities.Single().ownerId,Is.EqualTo("fighter"));Assert.That(Fighter.p5.preparation.used);Assert.That(session.Execute(contact).duplicate);Assert.That(S.p5.opportunities,Has.Count.EqualTo(1));session.Tick(2.1f,true,0);Assert.That(Fighter.basicOrder.stage,Is.EqualTo("Finished"));Assert.That(Fighter.guardRemaining,Is.Zero);
        }
        [Test] public void BraceButtonOrUncommittedOrdinaryAttackCannotManufactureOpportunity()
        {Run(Prepare("fighter","fighter"));session.Tick(.81f,true,0);S.world.strings["enemyTemplate/"+Enemy.id]="guard";var c=C(CommandKind.IncomingAttack,Enemy.id,target:"fighter");c.rootId="not-committed";c.amount=5;c.qualified=true;c.authority=CommandAuthority.System;Run(c);Assert.That(S.p5.opportunities,Is.Empty);}
        [Test] public void ReactionOriginContactCannotReturnAFreeBraceOpportunity()
        {Run(Prepare("fighter","fighter"));session.Tick(.81f,true,0);S.world.strings["enemyTemplate/"+Enemy.id]="guard";Enemy.aiState=1;Enemy.attackGroupId="reaction-origin";var c=C(CommandKind.IncomingAttack,Enemy.id,target:"fighter");c.rootId=Enemy.attackGroupId;c.amount=5;c.qualified=true;c.fromReaction=true;c.authority=CommandAuthority.System;Run(c);Assert.That(S.p5.opportunities,Is.Empty);Assert.That(S.p5.productionRoots,Is.Empty);}
        [Test] public void RepeatedSameKindContactDoesNotRefreshOrReplaceExistingOpportunity()
        {
            S.world.strings["enemyTemplate/"+Enemy.id]="guard";
            for(int i=0;i<2;i++)
            {
                Run(Prepare("fighter","fighter"));session.Tick(.81f,true,0);Enemy.aiState=1;Enemy.attackGroupId="committed/"+i;
                var c=C(CommandKind.IncomingAttack,Enemy.id,target:"fighter");c.rootId=Enemy.attackGroupId;c.amount=5;c.qualified=true;c.authority=CommandAuthority.System;Run(c);
                if(i==0)session.Tick(5.2f,true,0);
            }
            Assert.That(S.p5.opportunities,Has.Count.EqualTo(1));Assert.That(S.p5.productionRoots,Has.Count.EqualTo(2));Assert.That(S.p5.opportunities[0].remaining,Is.EqualTo(1.99f).Within(.001));Assert.That(S.p5.opportunities[0].ownerId,Is.EqualTo("fighter"));
        }
        [Test] public void RogueArrivalOnlyArmsAndActualCommittedRearHitProducesExposed()
        {
            Rogue.x=2;Enemy.aiState=1;Enemy.attackGroupId="facing-lock";var prepare=Prepare("rogue");prepare.x=2;prepare.z=0;Run(prepare);var arrival=C(CommandKind.CompleteBasicOrder,"rogue");arrival.orderId=prepare.id;arrival.authority=CommandAuthority.System;Run(arrival);Assert.That(S.p5.opportunities,Is.Empty);
            Contact(C(CommandKind.StartBasicAttack,"rogue"));Assert.That(S.p5.opportunities.Single().p5.kind,Is.EqualTo("exposed"));Assert.That(S.p5.opportunities.Single().ownerId,Is.EqualTo("rogue"));
        }
        [Test] public void MageWithNoChargeOrSlotsProducesBoundOnlyAfterActualFrostHit()
        {
            S.charges=0;Mage.spellSlots=0;Run(Prepare("mage"));session.Tick(.81f,true,0);Assert.That(S.p5.opportunities,Is.Empty);Assert.That(Mage.p5.preparation.ready);var start=C(CommandKind.StartBasicAttack,"mage");Run(start);session.Tick(.13f,true,0);
            S.random.backgroundCombat=FrostStream(12);var contact=C(CommandKind.ResolveBasicAttack,"mage");contact.orderId=start.id;contact.authority=CommandAuthority.System;var result=Run(contact);
            Assert.That(result.damage,Is.GreaterThan(0));Assert.That(S.p5.opportunities.Single().p5.kind,Is.EqualTo("bound"));Assert.That(Enemy.statuses.Single(x=>x.p5!=null).p5.instanceId,Is.EqualTo(S.p5.opportunities.Single().p5.sourceConditionId));Assert.That(session.P5MovementMultiplier(Enemy.id),Is.EqualTo(.7f));Assert.That(Mage.spellSlots,Is.Zero);Assert.That(S.charges,Is.Zero);Assert.That(S.random.visibleTactical.drawCount,Is.Zero);
        }
        static RngState FrostStream(int natural)
        {for(ulong seed=0;seed<10000;seed++){var stream=RandomStreams.Create(seed).backgroundCombat;var test=CombatJson.Clone(stream);RandomStreams.NextInt(test,1,9);if(RandomStreams.D20(test)==natural)return stream;}throw new Exception("fixture_seed_missing");}
        [Test] public void FailedFrostDoesNotCreateConditionOrOpportunity()
        {Mage.spellSlots=0;Run(Prepare("mage"));session.Tick(.81f,true,0);var start=C(CommandKind.StartBasicAttack,"mage");Run(start);session.Tick(.13f,true,0);S.random.backgroundCombat=FrostStream(1);var contact=C(CommandKind.ResolveBasicAttack,"mage");contact.orderId=start.id;contact.authority=CommandAuthority.System;Assert.That(Run(contact).damage,Is.Zero);Assert.That(S.p5.opportunities,Is.Empty);Assert.That(Enemy.statuses,Is.Empty);}
        [Test] public void ExistingSpecializationsPreserveTheirPreparationRatiosUnderP5()
        {session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="mage",specializationId="M_INSIGHT",studySeconds=.8f});session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="rogue",specializationId="R_FLANK",flankWindowSeconds=6});Run(Prepare("mage"));Assert.That(Mage.p5.preparation.remaining,Is.EqualTo(.64f).Within(.0001));session.Tick(.65f,true,0);Assert.That(Mage.p5.preparation.ready);Rogue.x=2;Enemy.aiState=1;Enemy.attackGroupId="specialized-facing";var prep=Prepare("rogue");prep.x=2;Run(prep);Assert.That(Rogue.p5.preparation.remaining,Is.EqualTo(6));var arrival=C(CommandKind.CompleteBasicOrder,"rogue");arrival.orderId=prep.id;arrival.authority=CommandAuthority.System;Run(arrival);Assert.That(Rogue.p5.preparation.remaining,Is.EqualTo(6));}
        [TestCase(false,2)] [TestCase(false,0)] [TestCase(true,2)]
        public void IncomingUsesActualRangedCoverEvidenceButSavesNeverGainAc(bool savingThrow,int cover)
        {Enemy.attackBonus=0;Fighter.armorClass=10;S.random.backgroundCombat=CombatRuleTests.StreamFor(12);var c=C(savingThrow?CommandKind.IncomingSave:CommandKind.IncomingAttack,Enemy.id,target:"fighter");c.rootId="cover-evidence";c.authority=CommandAuthority.System;c.qualified=true;c.amount=1;c.p5=new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId="fighter",coverAcBonus=cover}}};Run(c);Assert.That(S.incomingEvents.Single().effectiveArmorClass,Is.EqualTo(10+(savingThrow?0:cover)));}
        [Test] public void ThunderLocksPerTargetConSavesAndConsumesOnlyOneMainOpportunity()
        {
            Fighter.x=2;Fighter.z=.2f;Rogue.x=-3;var token=Token("exposed","fighter");Confirm("control",20,"mage",token.id);var ticket=S.pendingRoll;Assert.That(ticket.rollOwnerId,Is.EqualTo(Enemy.id));Assert.That(ticket.rollType,Is.EqualTo("Save"));Assert.That(ticket.hit,Is.False);Assert.That(ticket.p5.targets.Any(x=>x.targetId=="fighter"));Assert.That(ticket.p5.targets.Count(x=>x.targetId==Enemy.id),Is.EqualTo(1));Assert.That(S.p5.opportunities.Count(x=>x.consumed),Is.EqualTo(1));Assert.That(ticket.p5.savePenalty,Is.EqualTo(2));
            Run(CommandKind.RevealRoll);string queue=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);int before=Enemy.hp;Resolve(queue);Assert.That(before-Enemy.hp,Is.EqualTo(S.rolls.Last().p5.damage/2));Assert.That(Enemy.x,Is.EqualTo(1));Assert.That(S.rolls.Last().p5.rewarded,Is.False);
        }
        [Test] public void ThunderRechecksActualAreaAndNewEntrantGetsOnlyBaseBackgroundSave()
        {
            Rogue.x=-4;Fighter.x=-4;var token=Token("bound","fighter");Confirm("control",1,"mage",token.id);Run(CommandKind.RevealRoll);string queue=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);var start=C(CommandKind.StartQueued);start.queueId=queue;Run(start);session.Tick(.5f,true,0);Enemy.x=5;Rogue.x=1;Rogue.z=.5f;
            int enemyHp=Enemy.hp,rogueHp=Rogue.hp;ulong background=S.random.backgroundCombat.drawCount;var resolve=C(CommandKind.ResolveQueued);resolve.queueId=queue;Run(resolve);
            Assert.That(Enemy.hp,Is.EqualTo(enemyHp));Assert.That(Rogue.hp,Is.LessThan(rogueHp));Assert.That(S.random.backgroundCombat.drawCount,Is.GreaterThan(background));Assert.That(S.rolls.Last().p5.targets.Single(x=>x.targetId=="rogue").bonus,Is.EqualTo(Rogue.constitutionModifier));Assert.That(S.rolls.Last().p5.rewarded,Is.False);
        }
        [Test] public void BlockedPrimaryDoesNotBlockVisibleSecondaryThunderContact()
        {Fighter.x=1;Fighter.z=.2f;Confirm("control",1,"mage");Run(CommandKind.RevealRoll);string queue=Run(CommandKind.Commit).queueId;Run(CommandKind.ExitFocus);int enemy=Enemy.hp,fighter=Fighter.hp;var evidence=new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,lineOfSight=false},new P5TargetEvidence{targetId="fighter",lineOfSight=true,hasDisplacement=true,x=Fighter.x,z=Fighter.z}}};var start=C(CommandKind.StartQueued);start.queueId=queue;start.lineOfSight=false;start.p5=evidence;Run(start);session.Tick(.5f,true,0);var resolve=C(CommandKind.ResolveQueued);resolve.queueId=queue;resolve.lineOfSight=false;resolve.p5=evidence;Run(resolve);Assert.That(Enemy.hp,Is.EqualTo(enemy));Assert.That(Fighter.hp,Is.LessThan(fighter));}
        [Test] public void ImpactSurfaceSpendsAtMostTwoUsesAndRootCannotApplyTwice()
        {var token=Token("bound");S.charges=1;int hp=Enemy.hp;string q=Queue(token:token.id);var result=Resolve(q,new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,x=2,z=0,hasDisplacement=true,actualDistance=1,impactSurfaceId="fixture-surface",impactRegistered=true}}});Assert.That(result.p5Effects.Count(x=>x.kind=="impact"),Is.EqualTo(1));Assert.That(Enemy.hp,Is.LessThan(hp));Assert.That(S.p5.impactSurfaces.Single().uses,Is.EqualTo(1));var repeat=C(CommandKind.ResolveQueued);repeat.queueId=q;Assert.That(session.Execute(repeat).success,Is.False);Assert.That(S.p5.impactSurfaces.Single().uses,Is.EqualTo(1));}
        [Test] public void ThirdDistinctCollisionOnSameNodeCannotRollOrDamageAgain()
        {
            for(int i=0;i<3;i++)
            {
                // Isolated rule fixture: restore position/available Charge between separate
                // actual commands, while preserving the node ledger and random stream.
                Enemy.x=1;Enemy.z=0;S.charges=1;string queue=Queue();int hp=Enemy.hp;ulong draws=S.random.backgroundCombat.drawCount;
                var r=Resolve(queue,new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,x=2,z=0,hasDisplacement=true,actualDistance=1,impactSurfaceId="node",impactRegistered=true}}});
                Assert.That(r.p5Effects.Any(x=>x.kind=="impact"),Is.EqualTo(i<2));if(i==2){Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));}session.Tick(5,true,0);
            }
            Assert.That(S.p5.impactSurfaces.Single().uses,Is.EqualTo(2));Assert.That(S.p5.impactSurfaces.Single().roots,Has.Count.EqualTo(2));
        }
        [Test] public void RegisteredContactBelowMinimumDistanceDoesNotConsumeNode()
        {string queue=Queue();int hp=Enemy.hp;ulong draws=S.random.backgroundCombat.drawCount;var r=Resolve(queue,new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,x=1.59f,z=0,hasDisplacement=true,actualDistance=.59f,impactSurfaceId="node",impactRegistered=true}}});Assert.That(r.p5Effects.All(x=>x.kind!="impact"));Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));Assert.That(S.p5.impactSurfaces,Is.Empty);}
        [Test] public void ExpiredPaidResultCannotReturnAfterSuspendingReactionCloses()
        {
            var token=Token();string ticket=Confirm(token:token.id);Run(CommandKind.RevealRoll);session.Tick(18,true,0);Enemy.attackBonus=0;Mage.armorClass=10;S.random.backgroundCombat=CombatRuleTests.StreamFor(12);
            var incoming=C(CommandKind.IncomingAttack,Enemy.id,target:"mage");incoming.authority=CommandAuthority.System;incoming.rootId="suspending-incoming";incoming.qualified=true;incoming.amount=8;Run(incoming);Assert.That(S.mode,Is.EqualTo(CombatMode.ReactionPrompt));Assert.That(S.decisionResume.suspendedTicketId,Is.EqualTo(ticket));
            session.Tick(3,true,0);Assert.That(S.rolls.Single(x=>x.id==ticket).stage,Is.EqualTo("Canceled"));Run(CommandKind.DeclineReaction);Assert.That(S.mode,Is.EqualTo(CombatMode.TacticalFocus));Assert.That(S.pendingRoll,Is.Null);Assert.That(Fighter.heldBy,Is.Null);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);
        }
        [TestCase(CombatWriteStage.BeforeWrite)] [TestCase(CombatWriteStage.AfterFlush)] [TestCase(CombatWriteStage.BeforeReplace)] [TestCase(CombatWriteStage.AfterReplace)] [TestCase(CombatWriteStage.AfterVerify)]
        public void PaidStartDeadlineFailureRollsBackMemoryDiskAndReplaysExactlyOnce(CombatWriteStage fault)
        {
            string folder=Path.Combine(Path.GetTempPath(),"P5DeadlineRules",Guid.NewGuid().ToString("N")),path=Path.Combine(folder,"save.json");
            try
            {
                session=new CombatSession(S,session.Config,path);Assert.That(session.Save().success);var token=Token();Confirm(token:token.id);Run(CommandKind.RevealRoll);session.Tick(19,true,0);Assert.That(session.Save().success);
                string state=CombatJson.Write(S),disk=File.ReadAllText(path);session.Store.WriteFault=stage=>{if(stage==fault)throw new IOException("p5-start-deadline-fault");};Assert.That(session.Tick(2,true,0),Is.Zero);Assert.That(session.LastTickError,Does.Contain("p5-start-deadline-fault"));Assert.That(CombatJson.Write(S),Is.EqualTo(state));Assert.That(File.ReadAllText(path),Is.EqualTo(disk));
                session=CombatSession.Load(path,session.Config,"p1-party");Assert.That(CombatJson.Write(S),Is.EqualTo(state));session.Tick(2,true,0);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.rolls.Single().stage,Is.EqualTo("Canceled"));Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);ulong draws=S.random.visibleTactical.drawCount;session.Tick(2,true,0);Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(S.charges,Is.EqualTo(1));
            }
            finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
        }
        [Test] public void ConsumedContextAndAllP5TimersRoundTripExactly()
        {var token=Token("bound");Confirm(token:token.id);string before=CombatJson.Write(S);var restored=CombatJson.Read<CombatState>(before);Assert.That(CombatJson.Write(restored),Is.EqualTo(before));Assert.That(CombatJson.Write(S.Clone()),Is.EqualTo(before));session=new CombatSession(restored,session.Config);Assert.That(S.pendingRoll.p5.opportunityId,Is.EqualTo(token.id));session.Tick(20);Assert.That(S.p5.opportunities.Single().remaining,Is.EqualTo(4));}

        [Test] public void P5EdgeBreakWindowLastsThreeSecondsThenRejectsAnotherBreakForFive()
        {
            // Explicit rule fixture: one-point Break bar and authored opportunities.
            // Every decrement still passes actual basic start/contact/background d20.
            Enemy.breakValue=Enemy.breakMaximum=1;
            CombatResult Strike(string actor){var token=Token(owner:actor=="mage"?"rogue":"mage");return Contact(BeginFollowup(token.id,actor));}
            Strike("fighter");Assert.That(Enemy.breakValue,Is.Zero);Assert.That(Enemy.staggerRemaining,Is.EqualTo(3));Assert.That(Enemy.p5.breakRecovery,Is.EqualTo(8));
            session.Tick(1,true,0);Strike("mage");Assert.That(Enemy.staggerRemaining,Is.EqualTo(1.87f).Within(.0001));Assert.That(Enemy.p5.breakRecovery,Is.EqualTo(6.87f).Within(.0001));
            session.Tick(Enemy.staggerRemaining,true,0);Assert.That(Enemy.staggerRemaining,Is.Zero);Assert.That(Enemy.breakValue,Is.EqualTo(1));Assert.That(Enemy.p5.breakRecovery,Is.EqualTo(5).Within(.0001));
            int health=Enemy.hp;Strike("rogue");Assert.That(Enemy.hp,Is.LessThan(health),"Recovery must not make ordinary damage immune.");Assert.That(Enemy.breakValue,Is.EqualTo(1));Assert.That(Enemy.staggerRemaining,Is.Zero);
            session.Tick(Enemy.p5.breakRecovery-.2f,true,0);Strike("fighter");Assert.That(Enemy.breakValue,Is.EqualTo(1));Assert.That(Enemy.staggerRemaining,Is.Zero);Assert.That(Enemy.p5.breakRecovery,Is.GreaterThan(0));
            session.Tick(Enemy.p5.breakRecovery+.001f,true,0);Strike("mage");Assert.That(Enemy.breakValue,Is.Zero);Assert.That(Enemy.staggerRemaining,Is.EqualTo(3));Assert.That(Enemy.p5.breakRecovery,Is.EqualTo(8));Assert.That(S.focusBudget.secondsRecovered,Is.Zero,"Free followup does not mint Focus merely by causing Break.");
        }
        [Test] public void P5EdgeActualComboImpactEliteDeathInterruptAndBreakShareHighestRootReward()
        {
            // Fixed hp/registered threat/spatial sweep are isolated rule fixtures,
            // not a claim that an ordinary Player run naturally reached this setup.
            S.charges=1;S.focusBudget.secondsRemaining=1;Enemy.hp=1;Enemy.breakValue=1;Enemy.aiState=1;Enemy.attackGroupId="fixture-elite-windup";
            S.world.strings["enemyTemplate/"+Enemy.id]="elite";S.momentumLedger.registeredHighThreats.Add(Enemy.id);S.momentumLedger.registeredDangerousThreats.Add(Enemy.attackGroupId);
            var token=Token("bound");var result=Resolve(Queue(natural:20,token:token.id),new P5CommandData{targets=new List<P5TargetEvidence>{new P5TargetEvidence{targetId=Enemy.id,x=2,z=0,hasDisplacement=true,actualDistance=1,impactSurfaceId="fixture-elite-impact",impactRegistered=true}}});
            var ticket=S.rolls.Single();Assert.That(Enemy.Alive,Is.False);Assert.That(Enemy.breakValue,Is.Zero);Assert.That(Enemy.aiState,Is.EqualTo(2));Assert.That(result.p5Effects.Any(e=>e.kind=="impact"&&e.damage>0));Assert.That(ticket.p5.effective&&ticket.p5.rewarded);
            Assert.That(S.momentum,Is.EqualTo(40));Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(40));Assert.That(S.momentumLedger.p4Roots,Has.Count.EqualTo(1));Assert.That(S.momentumLedger.p4Roots.Single().id,Is.EqualTo(ticket.id));Assert.That(S.momentumLedger.p4Roots.Single().highest,Is.EqualTo(40));Assert.That(S.momentumLedger.paidChains.Single().awarded,Is.EqualTo(40));
            Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(1.5f));Assert.That(S.focusBudget.rewardEvents,Has.Count.EqualTo(1));Assert.That(S.focusBudget.rewardEvents.Single().highestOffered,Is.EqualTo(1.5f));Assert.That(S.focusBudget.rewardEvents.Single().credited,Is.EqualTo(1.5f));Assert.That(S.focusBudget.recoveryScopes.Single().consumed,Is.EqualTo(1.5f));
        }
        [Test] public void P5EdgePaidComboAndRegisteredDerivedRootsCannotReturnMoreThanSixty()
        {
            S.charges=1;var token=Token();Resolve(Queue(token:token.id));string paid=S.rolls.Single().id;Assert.That(S.momentum,Is.EqualTo(30));
            // Existing registered world-fact interface represents two subsequent
            // effects attributed to this same paid action, without adding a relic.
            CombatCommand last=null;
            for(int i=0;i<2;i++)
            {
                string root="fixture-derived-environment/"+i;S.momentumLedger.registeredObjectives.Add(root);var c=C(CommandKind.AwardFact,action:"environment");c.authority=CommandAuthority.System;c.rootId=c.factId=root;c.qualified=true;c.paidChainId=paid;Run(c);last=c;
            }
            Assert.That(S.momentum,Is.EqualTo(60));Assert.That(S.momentumLedger.tacticalAwarded,Is.EqualTo(60));Assert.That(S.momentumLedger.paidChains.Single().id,Is.EqualTo(paid));Assert.That(S.momentumLedger.paidChains.Single().awarded,Is.EqualTo(60));string before=CombatJson.Write(S);Assert.That(session.Execute(last).duplicate);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
            last.id="fixture-refund-loop";last.refundOrigin=true;Assert.That(session.Execute(last).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void P5EdgeFullFocusRecordsDiscardedComboWithoutShowingRecoveryOrBankingIt()
        {
            S.charges=1;S.focusBudget.secondsRemaining=S.focusBudget.maximumSeconds;var token=Token();string queue=Queue(token:token.id);Resolve(queue);
            var replay=C(CommandKind.ResolveQueued);replay.id=S.receipts.Last().id;replay.queueId=queue;
            Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(20));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);Assert.That(S.focusBudget.secondsDiscarded,Is.EqualTo(.75f));Assert.That(S.focusBudget.rewardEvents.Single().credited,Is.Zero);Assert.That(S.focusBudget.rewardEvents.Single().quotaConsumed,Is.EqualTo(.75f));Assert.That(S.focusBudget.lastRecoverySequence,Is.Zero);Assert.That(S.focusBudget.lastRecoverySeconds,Is.Zero);Assert.That(S.focusBudget.lastRecoveryKind,Is.Null);
            session=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),session.Config);Run(CommandKind.EnterFocus);session.Tick(0,true,.5f);Run(CommandKind.ExitFocus);float remaining=S.focusBudget.secondsRemaining;Assert.That(remaining,Is.LessThan(20));string before=CombatJson.Write(S);Assert.That(session.Execute(replay).duplicate);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(remaining));Assert.That(S.focusBudget.secondsRecovered,Is.Zero);
        }
        [Test] public void P5EdgeSimultaneousFocusAndPaidStartDeadlinesReleaseOnlyUnspentHoldsOnce()
        {
            S.focusBudget.secondsRemaining=.1f;var token=Token("exposed","fighter");int slots=Mage.spellSlots;Confirm("control",10,"mage",token.id);Run(CommandKind.RevealRoll);session.Tick(19,true,0);Assert.That(Mage.heldSpellSlots,Is.EqualTo(1));ulong visible=S.random.visibleTactical.drawCount,background=S.random.backgroundCombat.drawCount;
            session.Tick(1.1f,true,.2f);Assert.That(session.LastTickError,Is.Null);Assert.That(S.pendingRoll,Is.Null);Assert.That(S.rolls.Single().stage,Is.EqualTo("Canceled"));Assert.That(Mage.heldBy,Is.Null);Assert.That(Mage.heldSpellSlots,Is.Zero);Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);Assert.That(S.mode,Is.EqualTo(CombatMode.RealtimeCombat));Assert.That(S.focusBudget.secondsRemaining,Is.Zero);Assert.That(S.focusBudget.secondsSpent,Is.EqualTo(.1f).Within(.0001));
            session=new CombatSession(S.Clone(),session.Config);session.Tick(3,true,3);Assert.That(S.charges,Is.EqualTo(1));Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(visible));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(background));Assert.That(S.queue,Is.Empty);
        }
        [Test] public void P5EdgeActionAlreadyStartedSurvivesFocusAndStartDeadlineWithoutRefund()
        {
            S.focusBudget.secondsRemaining=.1f;var token=Token();string queue=Queue(token:token.id);var start=C(CommandKind.StartQueued);start.queueId=queue;Run(start);Run(CommandKind.EnterFocus);session.Tick(21,true,.2f);
            Assert.That(session.LastTickError,Is.Null);Assert.That(S.simulationSeconds,Is.GreaterThan(2));Assert.That(S.queue.Single().stage,Is.EqualTo("Executing"));Assert.That(S.rolls.Single().stage,Is.EqualTo("Executing"));Assert.That(S.focusBudget.secondsRemaining,Is.Zero);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.p5.opportunities.Single().consumed);
            var contact=C(CommandKind.ResolveQueued);contact.queueId=queue;Run(contact);Assert.That(S.queue.Single().stage,Is.EqualTo("Resolved"));Assert.That(Fighter.heldBy,Is.Null);string before=CombatJson.Write(S);Assert.That(session.Execute(contact).duplicate);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
    }
}
