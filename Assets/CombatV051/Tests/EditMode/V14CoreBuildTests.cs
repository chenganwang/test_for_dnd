using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Deterministic rules fixtures. These results are not natural Player or human evidence.
    public sealed class V14CoreBuildTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        CombatConfig config;CombatSession game;int serial;CombatState S=>game.State;ActorState Enemy=>S.enemies[0];
        void Make(string core,params string[] buffs)
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig{build=new V12BuildConfig()},v13=new V13Rules(),v14=new V14Rules(),
                v11=new V11CombatRules{version="v12-character-1",v12BuildVersion="v12-build-1",v14BuildVersion=V14BuildCatalog.Version,v13FinnTree="growth",v14FinnGrowthAction="v14_finn_detonate",coreActorId=core}};
            foreach(string id in new[]{E,M,F})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==E?"v11_evan_insight":id==M?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,
                unlockedActionIds=id==E?new List<string>{"v12_evan_mark","v13_evan_correct"}:id==M?new List<string>{"v12_mira_chase","v12_mira_break"}:new List<string>{"v12_finn_seed","v14_finn_detonate"}});
            config.v11.buffs.AddRange(buffs.Select(id=>new V11BuffStack{id=id,stacks=1}));
            var s=CombatState.CreatePartyForPreset("v14-core-test","run",71,config);foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=1000;
            game=new CombatSession(s,config);serial=0;
        }
        CombatCommand Cmd(CommandKind kind,string actor=M,string action="v12_mira_break")=>new CombatCommand{id="v14-core/"+(++serial),kind=kind,actorId=actor,targetId=Enemy.id,actionId=action,qualified=true};
        CombatResult Good(CombatCommand c){var r=game.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Dice(int first,int? second=null)
        {
            for(ulong n=1;n<200000;n++){var stream=RandomStreams.Stream(n,"v14-fixture");var copy=CombatJson.Clone(stream);if(RandomStreams.D20(copy)==first&&(!second.HasValue||RandomStreams.D20(copy)==second.Value)){S.random.visibleTactical=stream;return;}}
            Assert.Fail("deterministic seed not found");
        }
        void Roll(int raw=9,bool advantage=false)
        {
            Enemy.armorClass=S.Actor(M).attackBonus+10;if(advantage)Enemy.staggerRemaining=1;
            Dice(raw,advantage?(int?)1:null);Good(Cmd(CommandKind.EnterFocus));Good(Cmd(CommandKind.ConfirmRoll));Good(Cmd(CommandKind.RevealRoll));
        }
        CombatResult ExecuteRoll()
        {
            Good(Cmd(CommandKind.Commit));Good(Cmd(CommandKind.ExitFocus));string queue=S.rolls.Last().queueId;
            var start=Cmd(CommandKind.StartQueued);start.queueId=queue;Good(start);game.Tick(1);
            var finish=Cmd(CommandKind.ResolveQueued);finish.queueId=queue;finish.authority=CommandAuthority.System;return Good(finish);
        }
        void Dodge(string root="real-threat")
        {
            S.momentumLedger.registeredDangerousThreats.Add(root);Good(Cmd(CommandKind.StartDodge));
            Good(new CombatCommand{id="dodge-contact/"+(++serial),kind=CommandKind.IncomingAttack,actorId=Enemy.id,targetId=M,rootId=root,amount=5,qualified=true,authority=CommandAuthority.System});
            game.Tick(.4f);
        }
        [Test] public void FirstCorrectionWorksWithZeroInspirationAndKeepsRawFace()
        {
            Make(E,"v14_evan_firstproof");Roll();S.Actor(E).inspiration=0;int raw=S.pendingRoll.selectedFace,old=S.pendingRoll.total;
            Assert.That(game.QuoteV14Correction().free,Is.True);Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));
            Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(raw));Assert.That(S.pendingRoll.total,Is.EqualTo(old+2));Assert.That(S.Actor(E).inspiration,Is.Zero);
            Assert.That(S.Actor(E).reactionRemaining,Is.GreaterThan(0));Assert.That(game.V14FirstCorrectionFree(S),Is.False);
        }
        [Test] public void FreeCorrectionPaymentSurvivesCancelAndSaveWithoutAnotherDiscount()
        {
            Make(E,"v14_evan_firstproof");Roll();Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));Good(Cmd(CommandKind.Cancel));
            string json=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(json),CombatJson.Clone(config));
            Assert.That(game.V14FirstCorrectionFree(S),Is.False);Assert.That(S.Actor(E).v14Core.encounters.Single().freeCorrectionUsed,Is.True);
            string oldEncounter=S.encounterId;S.encounterId="second-encounter";Assert.That(game.V14FirstCorrectionFree(S),Is.True);S.encounterId=oldEncounter;Assert.That(game.V14FirstCorrectionFree(S),Is.False);
        }
        [Test] public void FirstCorrectionDoesNotMakeOutOfCombatPreparationsFree()
        {Make(E,"v14_evan_firstproof");S.inCombat=false;S.Actor(E).inspiration=0;Assert.That(game.V14FirstCorrectionFree(S),Is.False);Assert.That(game.QuoteV14Correction(M).legal,Is.False);}
        [Test] public void PlusTwoCannotTurnNaturalOneIntoHitOrEighteenIntoCritical()
        {
            Make(E);Roll(1);Enemy.armorClass=1;Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));Assert.That(S.pendingRoll.hit,Is.False);Assert.That(S.pendingRoll.critical,Is.False);
            Make(E);Roll(18);Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(18));Assert.That(S.pendingRoll.critical,Is.False);
        }
        [Test] public void ReviewRerollsWholeAdvantageGroupAndForcesWorseRawWithoutTouchingFate()
        {
            Make(E,"v14_evan_review");Roll(20,true);Assert.That(S.pendingRoll.rawFaces.Count,Is.EqualTo(2));string fate=CombatJson.Write(S.v13.fateRng);Dice(1,2);
            var c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.v14CorrectionChoice="reroll";Good(c);
            Assert.That(S.pendingRoll.rawFaces,Is.EqualTo(new[]{1,2}));Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(2));Assert.That(S.pendingRoll.hit,Is.False);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));
            Assert.That(CombatJson.Write(S.v13.fateRng),Is.EqualTo(fate));Assert.That(S.Actor(E).inspiration,Is.EqualTo(1));Good(Cmd(CommandKind.RevealRoll));
            Assert.That(game.Execute(Cmd(CommandKind.Modify)).reason,Is.EqualTo("manual_intervention_already_used"));Assert.That(game.Execute(Cmd(CommandKind.Reroll)).reason,Is.EqualTo("manual_intervention_already_used"));
            Assert.That(game.Execute(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct")).success,Is.False);
        }
        [Test] public void ReviewStandaloneIsNotAFreeRerollAndUnownedReviewIsRejected()
        {
            Make(E,"v14_evan_review");Roll();S.Actor(E).inspiration=0;var c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.v14CorrectionChoice="reroll";Assert.That(game.Execute(c).reason,Is.EqualTo("correction_resource_or_reaction_unavailable"));
            Make(E);Roll();c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.v14CorrectionChoice="reroll";Assert.That(game.Execute(c).reason,Is.EqualTo("review_not_owned"));
        }
        [Test] public void PreparedCorrectionOnlyAttachesToItsChosenLegalPartyRoll()
        {
            Make(E,"v14_evan_firstproof","v14_evan_settle");var c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.targetId=M;Good(c);game.Tick(1.3f);Roll();
            Assert.That(S.pendingRoll.v14Correction,Is.Not.Null);Assert.That(S.pendingRoll.flatBonus,Is.EqualTo(2));Assert.That(S.pendingRoll.v14Correction.beforeSuccess,Is.False);
            Assert.That(S.pendingRoll.v14Correction.afterSuccess,Is.True);Assert.That(S.Actor(E).inspiration,Is.EqualTo(2));
        }
        [Test] public void SettleStandaloneRequiresActualEffectAfterChangedFailureAndAwardsOnce()
        {
            Make(E,"v14_evan_settle");Roll();Assert.That(S.pendingRoll.hit,Is.False);Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));
            Assert.That(S.p6.resourceEvents.Any(x=>x.kind=="v14-settle"),Is.False);var result=ExecuteRoll();Assert.That(result.damage,Is.GreaterThan(0));
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Count,Is.EqualTo(1));var receipt=S.p6.resourceEvents.Single(x=>x.kind=="v14-settle");Assert.That(receipt.qualifiedAmount,Is.EqualTo(30));
            var ticket=S.rolls.Last();Assert.That(ticket.v14Correction.actualEffect&&ticket.v14Correction.settled,Is.True);Assert.That(S.focusBudget.p6.rewards.Single(x=>x.id.EndsWith("/"+ticket.id)).offered,Is.GreaterThanOrEqualTo(2));
        }
        [Test] public void SettleDoesNotPayForAlreadySuccessfulRollOrFullyAbsorbedHit()
        {
            Make(E,"v14_evan_settle");Roll(18);Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));ExecuteRoll();Assert.That(S.p6.resourceEvents.Any(x=>x.kind=="v14-settle"),Is.False);
            Make(E,"v14_evan_settle");Roll();Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));Enemy.p6.absorptionShield=1000;ExecuteRoll();Assert.That(S.p6.resourceEvents.Any(x=>x.kind=="v14-settle"),Is.False);
        }
        [Test] public void SettleHasTwoEncounterReceiptsAndDoesNotResetAfterSave()
        {
            Make(E,"v14_evan_settle");
            for(int i=0;i<3;i++)
            {
                S.charges=2;S.momentum=0;S.Actor(E).inspiration=2;S.Actor(E).reactionRemaining=0;S.Actor(M).majorRemaining=0;S.focusBudget.p6.reopenCooldownRemainingSim=0;game.Member(S,M).actionUntil=0;
                Roll();Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));ExecuteRoll();
            }
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Count,Is.EqualTo(2));Assert.That(S.p6.resourceEvents.Count(x=>x.kind=="v14-settle"),Is.EqualTo(2));
            string saved=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(saved),CombatJson.Clone(config));Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Count,Is.EqualTo(2));
        }
        [Test] public void SettleTakesMaximumOfExistingSameRootRewardsInsteadOfAdding()
        {
            Make(E,"v14_evan_settle");Roll();Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));string root=S.pendingRoll.id;
            S.p6.resourceEvents.Add(new P6ResourceEvent{root=root,kind="existing-fixture",qualifiedAmount=40,discardedAmount=40});CombatSession.AwardP6FocusRecovery(S,root,"combo");
            ExecuteRoll();Assert.That(S.p6.resourceEvents.Single(x=>x.root==root).qualifiedAmount,Is.EqualTo(40));Assert.That(S.focusBudget.p6.rewards.Single(x=>x.id.EndsWith("/"+root)).offered,Is.EqualTo(3));
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Count,Is.EqualTo(1));
        }
        [Test] public void DefensiveCorrectionSettlementSharesTheActualThreatRoot()
        {
            Make(E,"v14_evan_settle");string root="registered-parry-threat";S.momentumLedger.registeredDangerousThreats.Add(root);S.Actor(E).armorClass=10;Enemy.attackBonus=0;Dice(14);S.random.backgroundCombat=CombatJson.Clone(S.random.visibleTactical);
            Good(new CombatCommand{id="enemy-parry-contact",kind=CommandKind.IncomingAttack,actorId=Enemy.id,targetId=E,rootId=root,amount=8,qualified=true,parryable=true,authority=CommandAuthority.System});
            var quote=game.GetReactionChoices().First(q=>q.actorId==M&&q.reactionId=="parry");var begin=Cmd(CommandKind.BeginReaction);begin.reactionWindowId=quote.windowId;begin.reactionId="parry";Good(begin);
            Dice(quote.targetNumber-quote.bonus-1);Good(Cmd(CommandKind.ConfirmReactionRoll));Good(Cmd(CommandKind.RevealRoll));Assert.That(S.pendingRoll.hit,Is.False);
            Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));string ticket=S.pendingRoll.id;Good(Cmd(CommandKind.CommitReaction));
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Single(),Is.EqualTo(root));Assert.That(S.p6.resourceEvents.Count(x=>x.root==root),Is.EqualTo(1));
            Assert.That(S.p6.resourceEvents.Single(x=>x.root==root).qualifiedAmount,Is.GreaterThanOrEqualTo(30));Assert.That(S.focusBudget.p6.rewards.Count(x=>x.id.EndsWith("/"+root)),Is.EqualTo(1));
            Assert.That(S.focusBudget.p6.rewards.Any(x=>x.id.EndsWith("/"+ticket)),Is.False);
        }
        [Test] public void EmptyDodgeAndSafeTravelNeverGenerateAdvancedMiraQualification()
        {
            Make(M,"v14_mira_opening","v14_mira_breakflow","v14_mira_press");Good(Cmd(CommandKind.StartDodge));game.Tick(.4f);S.Actor(M).v12.readinessRemaining=4;
            Assert.That(game.V14MiraDamageMultiplier(S,S.Actor(M),config.BasicFor(S.Actor(M)).id,true),Is.EqualTo(1));Assert.That(S.Actor(M).v14Core?.dodgeRoot,Is.Null);
        }
        CombatCommand PreparedChase(PartyCombatMode mode,int raw=9,int shield=0)
        {
            Make(E,"v14_evan_settle");config.v14.partyCombatMode=mode;S.v14.partyCombatMode=mode;
            Enemy.armorClass=S.Actor(M).attackBonus+10;Enemy.p6.absorptionShield=shield;
            var prepare=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");prepare.targetId=M;Good(prepare);game.Tick(1.3f);
            Dice(raw);S.random.backgroundCombat=CombatJson.Clone(S.random.visibleTactical);
            var chase=Cmd(CommandKind.RequestHeroAction,M,"v12_mira_chase");chase.x=0;chase.z=0;Good(chase);game.Tick(config.v12.chaseWindup+.01f);
            var resolve=Cmd(CommandKind.ResolveHeroAction,M,"v12_mira_chase");resolve.orderId=chase.id;resolve.authority=CommandAuthority.System;return resolve;
        }
        [TestCase(PartyCombatMode.PersistentTrio)][TestCase(PartyCombatMode.CoreAndAssist)]
        public void PreparedChaseRealBackgroundRollSettlesOnceWithoutPaidRollHistory(PartyCombatMode mode)
        {
            var resolve=PreparedChase(mode);int beforeRolls=S.rolls.Count;ulong beforeRng=S.random.backgroundCombat.drawCount;var result=Good(resolve);
            Assert.That(result.rawFaces,Is.EqualTo(new[]{9}));Assert.That(result.total,Is.EqualTo(S.Actor(M).attackBonus+11));Assert.That(result.damage,Is.GreaterThan(0));
            Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(beforeRng+1));Assert.That(S.rolls.Count,Is.EqualTo(beforeRolls));
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots,Is.EqualTo(new[]{resolve.orderId}));
            Assert.That(S.p6.resourceEvents.Single(e=>e.root==resolve.orderId).qualifiedAmount,Is.EqualTo(30));
            Assert.That(S.focusBudget.p6.rewards.Single(e=>e.id.EndsWith("/"+resolve.orderId)).offered,Is.EqualTo(2));
            game=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),CombatJson.Clone(config));
            Assert.That(game.Execute(resolve).duplicate,Is.True);Assert.That(S.p6.resourceEvents.Count(e=>e.root==resolve.orderId),Is.EqualTo(1));
            Assert.That(S.Actor(E).v14Core.encounters.Single().settlementRoots.Count,Is.EqualTo(1));
        }
        [TestCase(1,0)][TestCase(18,0)][TestCase(9,1000)]
        public void PreparedChaseDoesNotSettleMissAlreadySuccessOrAbsorbedDamage(int raw,int shield)
        {
            var resolve=PreparedChase(PartyCombatMode.CoreAndAssist,raw,shield);Good(resolve);
            Assert.That(S.p6.resourceEvents.Any(e=>e.kind=="v14-settle"),Is.False);
        }
        [Test] public void LogicalChaseRechecksCurrentCoreAnchorBeforeActualEffect()
        {
            var resolve=PreparedChase(PartyCombatMode.CoreAndAssist);string random=CombatJson.Write(S.random);int hp=Enemy.hp;
            S.Actor(E).x=Enemy.x-config.v12.chaseRange-.01f;var result=Good(resolve);
            Assert.That(result.reason,Is.EqualTo("hero_target_moved_or_blocked"));Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(CombatJson.Write(S.random),Is.EqualTo(random));
            Assert.That(S.p6.resourceEvents.Any(e=>e.kind=="v14-settle"),Is.False);Assert.That(S.Actor(M).v12.intent.stage,Is.EqualTo("Canceled"));
        }
        [TestCase("v14_mira_opening",1.75f,1.25f,1f)][TestCase("v14_mira_press",2f,1.5f,1.5f)]
        public void MiraEachDamageCardIndependentlyReplacesRatherThanMultiplies(string card,float basic,float chase,float heavy)
        {
            Make(M,card);Dodge();var a=S.Actor(M);
            Assert.That(game.V14MiraDamageMultiplier(S,a,config.BasicFor(a).id,true),Is.EqualTo(basic));Assert.That(game.V14MiraDamageMultiplier(S,a,"v12_mira_chase",true),Is.EqualTo(chase));Assert.That(game.V14MiraDamageMultiplier(S,a,"v12_mira_break",true),Is.EqualTo(heavy));
            Assert.That(game.V14MiraDamageMultiplier(S,a,config.BasicFor(a).id,false),Is.EqualTo(1));
        }
        [Test] public void MiraMissAndAiCannotConsumeQualificationButOneRealManualHitDoes()
        {
            Make(M,"v14_mira_press");Dodge();var a=S.Actor(M);string action=config.BasicFor(a).id;
            game.V14MiraConsume(S,a,Enemy,"miss",action,0,true);Assert.That(game.V14MiraDamageMultiplier(S,a,action,true),Is.EqualTo(2));
            game.V14MiraConsume(S,a,Enemy,"ai",action,9,false);Assert.That(game.V14MiraDamageMultiplier(S,a,action,true),Is.EqualTo(2));
            game.V14MiraConsume(S,a,Enemy,"manual",action,9,true);Assert.That(game.V14MiraDamageMultiplier(S,a,action,true),Is.EqualTo(1));Assert.That(a.v12.readinessRemaining,Is.Zero);
        }
        [Test] public void MiraRealBasicContactUsesOneReplacementAndOneGlobalScale()
        {
            Make(M,"v14_mira_opening","v14_mira_press");Dodge();Enemy.armorClass=1;Dice(10);S.random.backgroundCombat=CombatJson.Clone(S.random.visibleTactical);
            var start=Cmd(CommandKind.StartBasicAttack);Good(start);game.Tick(.3f);var end=Cmd(CommandKind.ResolveBasicAttack);end.authority=CommandAuthority.System;end.orderId=start.id;var result=Good(end);
            Assert.That(result.damage,Is.GreaterThan(0));var damage=S.v14DamageEvents.Single(x=>x.sourceId==M&&x.id.StartsWith(start.id+"/"));
            Assert.That(damage.sourceDamage,Is.EqualTo(config.BasicFor(S.Actor(M)).damage*2*1.1).Within(.000001));Assert.That(S.Actor(M).v14Core.dodgeRoot,Is.Null);
            Assert.That(S.v12.events.Count(x=>x.kind=="v14-counter"&&x.rootId==start.id),Is.EqualTo(1));Assert.That(game.Execute(end).duplicate,Is.True);
        }
        [Test] public void MiraGroupCannotIssueAgainAfterConsumptionAndCooldownRefundHasFourSecondLimit()
        {
            Make(M,"v14_mira_press");Dodge();var a=S.Actor(M);a.v12.basicCooldown=6;game.V14MiraConsume(S,a,Enemy,"first-counter","v12_mira_chase",7,true);Assert.That(a.v12.basicCooldown,Is.EqualTo(4));
            game.V14ObserveMiraDodge(S,new PendingIncoming{rootId="real-threat",defenderId=M,attackerId=Enemy.id,reason="avoided_by_dodge",baseDamage=5});Assert.That(a.v14Core.dodgeRoot,Is.Null);
            a.dodgeCooldown=0;Dodge("other-threat");a=S.Actor(M);a.v12.basicCooldown=6;game.V14MiraConsume(S,a,Enemy,"second-counter","v12_mira_chase",7,true);Assert.That(a.v12.basicCooldown,Is.EqualTo(6));
        }
        [Test] public void MiraQualificationAndConsumedGroupRemainCorrectAfterJsonContinue()
        {
            Make(M,"v14_mira_press");Dodge();string saved=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(saved),CombatJson.Clone(config));var a=S.Actor(M);
            Assert.That(game.V14MiraDamageMultiplier(S,a,"v12_mira_chase",true),Is.EqualTo(1.5));game.V14MiraConsume(S,a,Enemy,"saved-counter","v12_mira_chase",7,true);
            saved=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(saved),CombatJson.Clone(config));a=S.Actor(M);
            game.V14ObserveMiraDodge(S,new PendingIncoming{rootId="real-threat",defenderId=M,attackerId=Enemy.id,reason="avoided_by_dodge",baseDamage=5});Assert.That(a.v14Core.dodgeRoot,Is.Null);
        }
        [Test] public void BreakflowWorksAloneAndPressNeverGrantsUnownedExtraBreak()
        {
            Make(M,"v14_mira_breakflow");Dodge();float before=Enemy.breakValue;game.V14MiraConsume(S,S.Actor(M),Enemy,"break","v12_mira_break",9,true);Assert.That(Enemy.breakValue,Is.LessThan(before));
            Make(M,"v14_mira_press");Dodge();before=Enemy.breakValue;S.Actor(M).v12.basicCooldown=6;game.V14MiraConsume(S,S.Actor(M),Enemy,"press","v12_mira_break",9,true);Assert.That(Enemy.breakValue,Is.EqualTo(before));Assert.That(S.Actor(M).v12.basicCooldown,Is.EqualTo(4));
        }
        [Test] public void R4OnlyBasicCounterAndHeavyCounterCannotSpendTwoCopiesOfOneDodge()
        {
            Make(M,"v14_mira_breakflow");Dodge();var a=S.Actor(M);float before=Enemy.breakValue;
            game.V14MiraConsume(S,a,Enemy,"baseline-counter",config.BasicFor(a).id,9,true);Assert.That(a.v14Core.dodgeRoot,Is.Null);
            game.V14MiraConsume(S,a,Enemy,"late-heavy","v12_mira_break",9,true);Assert.That(Enemy.breakValue,Is.EqualTo(before));
        }
        [Test] public void MiraQualificationExpiresAndDoesNotCrossEncounter()
        {
            Make(M,"v14_mira_press");Dodge();game.Tick(4);var a=S.Actor(M);Assert.That(game.V14MiraDamageMultiplier(S,a,"v12_mira_chase",true),Is.EqualTo(1));
            a.dodgeCooldown=0;Dodge("second");S.encounterId="other-room";Assert.That(game.V14MiraDamageMultiplier(S,a,"v12_mira_chase",true),Is.EqualTo(1));
        }
        [Test] public void NewCardsDoNotLeakToLegacyOrNonCoreAndOldDefinitionsStillExist()
        {
            Make(E);Assert.That(V11BuffCatalog.Eligible(config.v11).Count(d=>d.id.StartsWith("v14_")),Is.EqualTo(3));Assert.That(V11BuffCatalog.Eligible(config.v11).Any(d=>d.id.StartsWith("v12_evan_")),Is.False);
            var legacy=CombatJson.Clone(config.v11);legacy.v14BuildVersion=null;Assert.That(V11BuffCatalog.Eligible(legacy).Any(d=>d.id.StartsWith("v14_")),Is.False);Assert.That(V11BuffCatalog.Find("v12_evan_coauthor"),Is.Not.Null);
            Assert.That(game.V14CoreBuff("v14_evan_firstproof",M),Is.False);
        }
        [Test] public void GroveAndGuardFilterOnlyIncompatibleFinnRewards()
        {
            Make(F);config.v11.v14FinnGrowthAction="v12_finn_grove";config.v11.actors.Find(a=>a.actorId==F).unlockedActionIds=new List<string>{"v12_finn_seed","v12_finn_grove"};
            Assert.That(V11BuffCatalog.Eligible(config.v11).Count(d=>d.id.StartsWith("v14_finn_")),Is.EqualTo(2));Assert.That(V14BuildCatalog.Guaranteed(config.v11,"V12_R5"),Is.Null);
            config.v11.v13FinnTree="guard";Assert.That(V11BuffCatalog.Eligible(config.v11).Any(d=>d.id.StartsWith("v14_finn_")),Is.False);Assert.That(V11BuffCatalog.Eligible(config.v11).Any(d=>d.id=="v13_finn_shelter"),Is.True);
        }
    }
}
