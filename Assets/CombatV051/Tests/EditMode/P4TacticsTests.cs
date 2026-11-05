using System;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class P4TacticsTests
    {
        CombatSession session;int sequence;
        CombatState S=>session.State;ActorState Mage=>S.Actor("mage");ActorState Rogue=>S.Actor("rogue");ActorState Fighter=>S.Actor("fighter");ActorState Enemy=>S.Actor("enemy-1");
        [SetUp] public void Setup(){var config=new CombatConfig{passiveMomentumPerSecond=0};var state=CombatState.CreatePartyP1("tactics","p4",19,config);state.charges=0;foreach(var a in state.actors){a.x=0;a.z=0;}state.enemies[0].x=1;state.enemies[0].hp=state.enemies[0].maxHp=1000;state.enemies[0].lockedX=2;state.enemies[0].lockedZ=0;session=new CombatSession(state,config);sequence=0;}
        CombatCommand Order(string actor,BasicOrderKind kind,bool replace=false)=>new CombatCommand{id="order-"+(++sequence),kind=CommandKind.IssueBasicOrder,actorId=actor,actionId=kind.ToString(),targetId=kind==BasicOrderKind.Prepare&&actor=="fighter"?"rogue":"enemy-1",qualified=true,x=-.5f,z=0,replaceExisting=replace};
        CombatResult Execute(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Observe(string category="Attack"){var c=Order("mage",BasicOrderKind.Prepare);c.preparationActionCategory=category;Execute(c);session.Tick(1.01f);Assert.That(Mage.setupToken,Is.Not.Null,session.LastTickError);}
        [TestCase(BasicOrderKind.Move)] [TestCase(BasicOrderKind.Hold)] [TestCase(BasicOrderKind.FocusTarget)] [TestCase(BasicOrderKind.Follow)] [TestCase(BasicOrderKind.Prepare)]
        public void ZeroChargeOrdersDoNotRollOrConsumeResources(BasicOrderKind kind)
        {
            string rng=CombatJson.Write(S.random);int slots=Mage.spellSlots;var c=Order("mage",kind);Execute(c);Assert.That(S.charges,Is.Zero);Assert.That(S.momentum,Is.Zero);Assert.That(Mage.spellSlots,Is.EqualTo(slots));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));Assert.That(session.Execute(c).duplicate);
        }
        [Test] public void PersistentOrderReplacementIsExplicitAndAtomic()
        {
            Execute(Order("rogue",BasicOrderKind.Hold));string before=CombatJson.Write(S);Assert.That(session.Execute(Order("rogue",BasicOrderKind.Move)).reason,Is.EqualTo("basic_order_replacement_required"));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Execute(Order("rogue",BasicOrderKind.Move,true));Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Move));
        }
        [Test] public void MovementDoesNotTeleportAndArrivalBecomesPersistentHold()
        {
            var c=Order("rogue",BasicOrderKind.Move);c.x=3;Execute(c);session.Tick(.5f);Assert.That(Rogue.x,Is.Zero);var arrival=new CombatCommand{id="arrive",kind=CommandKind.CompleteBasicOrder,actorId="rogue",orderId=c.id,authority=CommandAuthority.System};Assert.That(session.Execute(arrival).reason,Is.EqualTo("order_not_arrived"));Rogue.x=3;Execute(arrival);Assert.That(CombatSession.HasPlayerOrder(Rogue));Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Hold));
        }
        [Test] public void MagePreparationIsTargetAndCategoryBoundAndRevealsKnownInformation()
        {
            Observe("Save");Assert.That(S.knownEnemyInfo,Does.Contain(Enemy.id));Assert.That(Mage.setupToken.actionCategory,Is.EqualTo("Save"));Assert.That(session.Quote("mage",Enemy.id,"control").disadvantage);Assert.That(session.Quote("fighter",Enemy.id,"attack").advantage,Is.False);Assert.That(S.momentum,Is.Zero);Assert.That(Mage.preparationCooldown,Is.GreaterThan(4.9f));session.Tick(4.01f);Assert.That(session.Quote("mage",Enemy.id,"control").disadvantage,Is.False);
        }
        [TestCase("move")] [TestCase("damage")] [TestCase("stagger")] [TestCase("dead-target")]
        public void InterruptedPreparationCannotCreateTokenOrIncome(string reason)
        {
            Execute(Order("mage",BasicOrderKind.Prepare));session.Tick(.3f);if(reason=="move")Mage.x=.1f;if(reason=="damage")Mage.hp--;if(reason=="stagger")Mage.staggerRemaining=1;if(reason=="dead-target")Enemy.hp=0;session.Tick(1);Assert.That(Mage.setupToken,Is.Null);Assert.That(Mage.basicOrder.stage,Is.EqualTo("Canceled"));Assert.That(S.momentum,Is.Zero);
        }
        [Test] public void MageCannotSpamPrepareAndRepeatedKnowledgeGivesNoMomentum()
        {
            Observe();Assert.That(session.Execute(Order("mage",BasicOrderKind.Prepare)).reason,Is.EqualTo("preparation_unavailable"));session.Tick(6);Execute(Order("mage",BasicOrderKind.Prepare));session.Tick(1.1f);Assert.That(S.knownEnemyInfo.Count,Is.EqualTo(1));Assert.That(S.momentum,Is.Zero);
        }
        [Test] public void OwnSetupDoesNotPayTeamworkWhileAnotherActorsActualHitPaysOnce()
        {
            Observe();S.random.backgroundCombat=CombatRuleTests.StreamFor(20);var c=new CombatCommand{id="ally-hit",kind=CommandKind.BasicAttack,actorId="fighter"};var hit=Execute(c);Assert.That(hit.rawFaces.Count,Is.EqualTo(2));Assert.That(S.momentum,Is.EqualTo(30));Assert.That(Mage.setupToken.consumed);Assert.That(session.Execute(c).duplicate);Assert.That(S.momentum,Is.EqualTo(30));
        }
        [Test] public void OwnPreparedHitOnlyHasBasicIncome(){Observe();S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Execute(new CombatCommand{id="own",kind=CommandKind.BasicAttack,actorId="mage"});Assert.That(S.momentum,Is.EqualTo(1));Assert.That(S.momentumLedger.setupEarned,Is.Zero);}
        [Test] public void ConfirmConsumesSetupOnceAndPaidResolutionPaysOnlyActualEffect()
        {
            Observe();S.charges=1;Execute(new CombatCommand{id="focus",kind=CommandKind.EnterFocus});S.random.visibleTactical=CombatRuleTests.StreamFor(20);Execute(new CombatCommand{id="roll",kind=CommandKind.ConfirmRoll});Assert.That(Mage.setupToken.consumed);Assert.That(S.momentum,Is.Zero);Execute(new CombatCommand{id="reveal",kind=CommandKind.RevealRoll});var q=Execute(new CombatCommand{id="commit",kind=CommandKind.Commit}).queueId;Execute(new CombatCommand{id="exit",kind=CommandKind.ExitFocus});Execute(new CombatCommand{id="start",kind=CommandKind.StartQueued,queueId=q});session.Tick(1);Execute(new CombatCommand{id="resolve",kind=CommandKind.ResolveQueued,queueId=q});Assert.That(S.momentum,Is.EqualTo(30));Assert.That(S.setupUses.Single().rewarded);Assert.That(S.momentumLedger.paidChains.Single().awarded,Is.EqualTo(30));
        }
        [Test] public void RogueRequiresRealArrivalRearAndCommittedFacingWindow()
        {
            var c=Order("rogue",BasicOrderKind.Prepare);Execute(c);var arrive=new CombatCommand{id="arrive",kind=CommandKind.CompleteBasicOrder,actorId="rogue",orderId=c.id,authority=CommandAuthority.System};Assert.That(session.Execute(arrive).reason,Is.EqualTo("order_not_arrived"));Rogue.x=c.x;Execute(arrive);Assert.That(Rogue.setupToken,Is.Null);Assert.That(Rogue.basicOrder.stage,Is.EqualTo("WaitingWindow"));Enemy.aiState=1;Enemy.attackGroupId="committed";arrive.id="committed-arrival";Execute(arrive);Assert.That(Rogue.setupToken.remaining,Is.EqualTo(4));Assert.That(Rogue.setupToken.threatId,Is.EqualTo("committed"));Assert.That(Rogue.basicOrder.kind,Is.EqualTo(BasicOrderKind.Hold));Assert.That(S.momentum,Is.Zero);
        }
        [Test] public void FighterPreparationOnlyArmsGuardAfterOneSimulatedSecond()
        {
            Execute(Order("fighter",BasicOrderKind.Prepare));session.Tick(.9f);Assert.That(Fighter.guardRemaining,Is.Zero);session.Tick(.11f);Assert.That(Fighter.guardRemaining,Is.GreaterThan(0));Assert.That(Fighter.basicOrder.protectTargetId,Is.EqualTo("rogue"));Assert.That(S.momentum,Is.Zero);
        }
        [TestCase("front",true)] [TestCase("back",false)] [TestCase("far",false)] [TestCase("height",false)] [TestCase("blocked",false)] [TestCase("save",false)] [TestCase("unparryable",false)]
        public void PreparedGuardOnlyProtectsItsActualLegalAllyAndFrontalAttack(string condition,bool protectedHit)
        {
            Execute(Order("fighter",BasicOrderKind.Prepare));session.Tick(1.01f);if(condition=="back")Enemy.x=-1;if(condition=="far")Rogue.z=4;if(condition=="height")Rogue.heightY=2;if(condition=="blocked")Fighter.basicOrder.protectionLineOfSight=false;
            foreach(var a in S.actors){a.reactionRemaining=10;}S.random.backgroundCombat=CombatRuleTests.StreamFor(20);int hp=Rogue.hp;var result=session.ResolveIncoming("threat",Enemy.id,Rogue.id,4,condition=="save",30,"actual-root",condition!="unparryable");Assert.That(result.success,Is.True,result.reason);
            if(protectedHit){Assert.That(result.reason,Is.EqualTo("protected_by_preparation"));Assert.That(Rogue.hp,Is.EqualTo(hp));Assert.That(S.momentum,Is.EqualTo(30));Assert.That(Fighter.superiority,Is.EqualTo(2));}
            else{Assert.That(result.damage,Is.GreaterThan(0));Assert.That(S.momentum,Is.Zero);}
        }
        [Test] public void PreparedFighterMovementCancelsGuardInsteadOfLeavingResidualProtection()
        {
            Execute(Order("fighter",BasicOrderKind.Prepare));session.Tick(1.01f);Fighter.x=.2f;session.Tick(.01f);Assert.That(Fighter.basicOrder.stage,Is.EqualTo("Canceled"));Assert.That(Fighter.guardRemaining,Is.Zero);
        }
        [Test] public void PerActorSpecializationChangesOnlyItsPreparation()
        {
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="mage",studySeconds=.8f});Execute(Order("mage",BasicOrderKind.Prepare));session.Tick(.81f);Assert.That(Mage.setupToken,Is.Not.Null);Assert.That(session.Config.RulesFor("fighter").studySeconds,Is.EqualTo(1));
        }
        void PrepareRogueWindow(bool extended=false)
        {
            if(extended)session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="rogue",flankWindowSeconds=6});
            var c=Order("rogue",BasicOrderKind.Prepare);Execute(c);Rogue.x=c.x;Enemy.aiState=1;Enemy.attackGroupId="rear-window";
            Execute(new CombatCommand{id="arrived-window",kind=CommandKind.CompleteBasicOrder,actorId="rogue",orderId=c.id,authority=CommandAuthority.System});
        }
        [Test] public void RogueFlankSpecializationHasSixSecondWindowInsteadOfFour(){PrepareRogueWindow(true);Assert.That(Rogue.setupToken.remaining,Is.EqualTo(6));session.Tick(4.5f);Assert.That(Rogue.setupToken.remaining,Is.EqualTo(1.5f).Within(.001));Assert.That(session.Quote("fighter",Enemy.id,"attack").advantage);Assert.That(session.Config.RulesFor("fighter").flankWindowSeconds,Is.EqualTo(4));}
        [Test] public void RogueDistractExtendsOneExistingExposureAfterRealCooperativeHit()
        {
            session.Config.actorOverrides.Add(new ActorRuleOverride{actorId="rogue",firstExposeExtension=true});PrepareRogueWindow();Enemy.staggerRemaining=2;S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Execute(new CombatCommand{id="use-rogue",kind=CommandKind.BasicAttack});Assert.That(Enemy.staggerRemaining,Is.EqualTo(3));Assert.That(Rogue.p4ExposeUsed);Assert.That(S.momentum,Is.EqualTo(30));
            Fighter.majorRemaining=0;S.random.backgroundCombat=CombatRuleTests.StreamFor(20);Execute(new CombatCommand{id="second-hit",kind=CommandKind.BasicAttack});Assert.That(Enemy.staggerRemaining,Is.EqualTo(3));
        }
        [TestCase(false,0)] [TestCase(true,40)] public void OnlyRegisteredDangerousWindupGetsInterruptIncome(bool registered,int expected)
        {
            S.charges=1;Enemy.saveBonus=-50;Enemy.aiState=1;Enemy.attackGroupId="danger-root";if(registered)S.momentumLedger.registeredDangerousThreats.Add("danger-root");
            Execute(new CombatCommand{id="focus",kind=CommandKind.EnterFocus});Execute(new CombatCommand{id="roll",kind=CommandKind.ConfirmRoll,actionId="shove"});Execute(new CombatCommand{id="reveal",kind=CommandKind.RevealRoll});string q=Execute(new CombatCommand{id="commit",kind=CommandKind.Commit}).queueId;Execute(new CombatCommand{id="exit",kind=CommandKind.ExitFocus});Execute(new CombatCommand{id="start",kind=CommandKind.StartQueued,queueId=q});session.Tick(1);Execute(new CombatCommand{id="resolve",kind=CommandKind.ResolveQueued,queueId=q});Assert.That(S.momentum,Is.EqualTo(expected));Assert.That(Enemy.aiState,Is.Zero);
        }
        [Test] public void BasicAttackHasSeparateWindupContactAndRecoveryWithNoEarlyRoll()
        {
            int hp=Enemy.hp;ulong draws=S.random.backgroundCombat.drawCount;Execute(new CombatCommand{id="begin",kind=CommandKind.StartBasicAttack});Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));var contact=new CombatCommand{id="contact",kind=CommandKind.ResolveBasicAttack,authority=CommandAuthority.System,orderId="begin"};Assert.That(session.Execute(contact).reason,Is.EqualTo("basic_contact_not_ready"));session.Tick(.13f);S.random.backgroundCombat=CombatRuleTests.StreamFor(20);var hit=Execute(contact);Assert.That(hit.damage,Is.GreaterThan(0));Assert.That(Fighter.majorRemaining,Is.EqualTo(session.Config.majorRecovery));Assert.That(session.Execute(contact).duplicate);Assert.That(Enemy.hp,Is.EqualTo(hp-hit.damage));
        }
        [Test] public void ContactRechecksLineOfSightAndCannotHitThroughNewWall()
        {
            Execute(new CombatCommand{id="begin",kind=CommandKind.StartBasicAttack});session.Tick(.13f);int hp=Enemy.hp;ulong draws=S.random.backgroundCombat.drawCount;var r=Execute(new CombatCommand{id="contact",kind=CommandKind.ResolveBasicAttack,authority=CommandAuthority.System,orderId="begin",lineOfSight=false});Assert.That(r.reason,Is.EqualTo("basic_swing_missed"));Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draws));Assert.That(Fighter.majorRemaining,Is.GreaterThan(0));
        }
        [Test] public void PreparationAndIntentSurviveSerializationWithoutProgressingDuringFreeze()
        {
            Execute(Order("mage",BasicOrderKind.Prepare));Execute(new CombatCommand{id="begin",kind=CommandKind.StartBasicAttack});S.charges=1;Execute(new CombatCommand{id="focus",kind=CommandKind.EnterFocus});Execute(new CombatCommand{id="freeze-rogue",kind=CommandKind.ConfirmRoll,actorId="rogue",actionId="sneak"});session=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),session.Config);float timer=Mage.basicOrder.remaining;
            Assert.That(session.Tick(10),Is.Zero);Assert.That(Mage.basicOrder.remaining,Is.EqualTo(timer));Assert.That(Fighter.basicAttackIntent.remaining,Is.EqualTo(.12f));
        }
        [Test] public void EncounterResetClearsPreparedStateAndUseHistory(){Observe();Execute(Order("fighter",BasicOrderKind.Hold));CombatSession.ResetP4PreparationState(S);Assert.That(S.actors.All(a=>a.basicOrder==null&&a.setupToken==null&&a.basicAttackIntent==null&&a.preparationCooldown==0));Assert.That(S.knownEnemyInfo,Is.Empty);Assert.That(S.setupUses,Is.Empty);}
    }
}
