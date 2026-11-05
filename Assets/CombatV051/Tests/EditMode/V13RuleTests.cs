using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // TEST DOMAIN: explicit dice seeds / HP / time. Never natural play or human evidence.
    public sealed partial class V13RuleTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn;
        CombatConfig config;CombatSession game;int serial;
        CombatState S=>game.State;ActorState Enemy=>S.enemies[0];
        [SetUp] public void Setup()=>Make("growth",M);
        void Make(string tree,string core)
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig(),v13=new V13Rules{finnTree=tree},v11=new V11CombatRules{version="v12-character-1",coreActorId=core}};
            foreach(string id in new[]{E,M,F})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==E?"v11_evan_insight":id==M?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,finnSustain=id==F?new V11FinnSustainConfig():null,unlockedActionIds=id==E?new List<string>{"v12_evan_mark","v13_evan_correct"}:id==F&&tree=="guard"?new List<string>{"v13_finn_shield","v13_finn_prayer"}:new List<string>{CombatSession.V12BasicAction(id),CombatSession.V12KeyAction(id)}});
            var s=CombatState.CreatePartyForPreset("v13-test","run",41,config);foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=1000;game=new CombatSession(s,config);serial=0;
        }
        CombatCommand Cmd(CommandKind kind,string actor=M,string action="v12_mira_break")=>new CombatCommand{id="v13-test/"+(++serial),kind=kind,actorId=actor,targetId=Enemy.id,actionId=action,qualified=true};
        CombatResult Good(CombatCommand c){var r=game.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Cast(string actor,string action,string target=null)
        {var c=Cmd(CommandKind.RequestHeroAction,actor,action);if(target!=null)c.targetId=target;Good(c);game.Tick(1);var end=Cmd(CommandKind.ResolveHeroAction,actor,action);end.orderId=c.id;end.authority=CommandAuthority.System;Good(end);}
        void PrepareFate(int raw)
        {
            S.v13.fate=new V13FateEvent{id="fate-test/"+(++serial),encounterId=S.encounterId,trigger="V12_R4",warning=0};
            for(ulong seed=1;seed<100000;seed++){var rng=RandomStreams.Stream(seed,"v13/fate/1");if(RandomStreams.NextInt(CombatJson.Clone(rng),1,101)==raw){S.v13.fateRng=rng;break;}}
            var c=Cmd(CommandKind.V13FateStart);c.authority=CommandAuthority.System;Good(c);Assert.That(S.v13.fate.original,Is.EqualTo(raw));
            c=Cmd(CommandKind.V13FateReveal);c.authority=CommandAuthority.System;Good(c);
        }
        [Test] public void OnePhysicalBodyAndLockedCore()
        {Assert.That(S.actors.Count(a=>CombatSession.V13Physical(S,a)),Is.EqualTo(1));Assert.That(game.Execute(Cmd(CommandKind.SwitchActive,E)).reason,Is.EqualTo("core_locked_for_run"));Assert.That(game.Execute(Cmd(CommandKind.StartBasicAttack,E)).reason,Is.EqualTo("logical_member_has_no_body"));S.Actor(M).hp=0;Assert.That(CombatSession.V13PartyAlive(S),Is.False);}
        [Test] public void AnchoredRangeAndWallRejectWithoutSpending()
        {S.Actor(E).x=Enemy.x=30;var c=Cmd(CommandKind.RequestHeroAction,E,"v12_evan_mark");Assert.That(game.Execute(c).success,Is.False);Enemy.x=1;c.lineOfSight=false;Assert.That(game.Execute(c).reason,Is.EqualTo("line_of_sight_blocked"));Assert.That(S.Actor(E).v12.basicCooldown,Is.Zero);}
        [Test] public void MarkSameCooldownFromQuickAndTacticalAndSourceSurvivesAppearance()
        {Cast(E,"v12_evan_mark");Assert.That(Enemy.statuses.Any(x=>x.v12?.family=="opening"),Is.True);Good(Cmd(CommandKind.EnterFocus));Assert.That(game.Execute(Cmd(CommandKind.RequestHeroAction,E,"v12_evan_mark")).success,Is.False);Assert.That(S.charges,Is.EqualTo(2));game.Tick(2);Assert.That(Enemy.statuses.Any(x=>x.v12?.family=="opening"),Is.True);}
        [TestCase("growth",E)][TestCase("growth",M)][TestCase("growth",F)][TestCase("guard",F)]
        public void AllCoreConfigurationsPreserveExactlyTwoPreparedSkills(string tree,string core)
        {Make(tree,core);Assert.That(S.activeActorId,Is.EqualTo(core));Assert.That(config.v11.actors.All(a=>a.unlockedActionIds.Count==2),Is.True);Assert.That(config.v11.actors.Find(x=>x.actorId==F).unlockedActionIds.Contains("v13_finn_shield"),Is.EqualTo(tree=="guard"));}
        [Test] public void GuardTreePaysFinitePrayerAndHealsOnlyCore()
        {Make("guard",M);S.Actor(M).hp=8;int before=S.Actor(M).hp;Cast(F,"v13_finn_prayer",M);Assert.That(S.Actor(M).hp,Is.GreaterThan(before));Assert.That(game.Member(S,F).prayers,Is.EqualTo(1));Assert.That(S.charges,Is.EqualTo(1));game.Tick(2);var c=Cmd(CommandKind.RequestHeroAction,F,"v13_finn_shield");c.targetId=E;Assert.That(game.Execute(c).reason,Is.EqualTo("physical_ally_required"));}
        [Test] public void GuardShieldSharesCooldownAndDoesNotStack()
        {Make("guard",M);Cast(F,"v13_finn_shield",M);int shield=S.Actor(M).p6.absorptionShield;Assert.That(shield,Is.GreaterThan(0));var c=Cmd(CommandKind.RequestHeroAction,F,"v13_finn_shield");c.targetId=M;Assert.That(game.Execute(c).success,Is.False);Assert.That(S.Actor(M).p6.absorptionShield,Is.EqualTo(shield));}
        [Test] public void QuickAndTacticalPaidAttackHaveIdenticalDiceCostsAndDamage()
        {
            // Controlled comparison uses identical raw sequences in the two documented streams.
            S.random.backgroundCombat=CombatJson.Clone(S.random.visibleTactical);var origin=S.Clone();var rules=CombatJson.Clone(config);
            var quick=Cmd(CommandKind.ConfirmRoll);quick.p6=new P6CommandData{preacceptBaseResult=true};Good(quick);var qt=S.rolls.Last();var start=Cmd(CommandKind.StartQueued);start.queueId=qt.queueId;Good(start);game.Tick(1);var end=Cmd(CommandKind.ResolveQueued);end.queueId=qt.queueId;end.authority=CommandAuthority.System;var qr=Good(end);var qs=S.Clone();
            game=new CombatSession(origin,rules);Good(Cmd(CommandKind.EnterFocus));Good(Cmd(CommandKind.ConfirmRoll));Good(Cmd(CommandKind.RevealRoll));Good(Cmd(CommandKind.Commit));Good(Cmd(CommandKind.ExitFocus));var tt=S.rolls.Last();start=Cmd(CommandKind.StartQueued);start.queueId=tt.queueId;Good(start);game.Tick(1);end=Cmd(CommandKind.ResolveQueued);end.queueId=tt.queueId;end.authority=CommandAuthority.System;var tr=Good(end);
            Assert.That(tr.rawFaces,Is.EqualTo(qr.rawFaces));Assert.That(tr.damage,Is.EqualTo(qr.damage));Assert.That(S.charges,Is.EqualTo(qs.charges));Assert.That(S.Actor(M).majorRemaining,Is.EqualTo(qs.Actor(M).majorRemaining));Assert.That(S.v13.echo.raw,Is.EqualTo(qs.v13.echo.raw));
        }
        [Test] public void CorrectionConsumesEvanResourceOnceAndNeverChangesNaturalFace()
        {Good(Cmd(CommandKind.EnterFocus));Good(Cmd(CommandKind.ConfirmRoll));Good(Cmd(CommandKind.RevealRoll));int face=S.pendingRoll.selectedFace,total=S.pendingRoll.total;Good(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct"));Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(face));Assert.That(S.pendingRoll.total,Is.EqualTo(total+2));Assert.That(S.Actor(E).inspiration,Is.EqualTo(1));Assert.That(game.Execute(Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct")).success,Is.False);}
        [TestCase(1,1)][TestCase(8,1)][TestCase(9,2)][TestCase(16,2)][TestCase(17,3)][TestCase(24,3)][TestCase(25,4)][TestCase(34,4)][TestCase(35,5)][TestCase(44,5)][TestCase(45,6)][TestCase(54,6)][TestCase(55,7)][TestCase(64,7)][TestCase(65,8)][TestCase(74,8)][TestCase(75,9)][TestCase(82,9)][TestCase(83,10)][TestCase(90,10)][TestCase(91,11)][TestCase(96,11)][TestCase(97,12)][TestCase(100,12)]
        public void AllD100TableBoundaries(int raw,int index)=>Assert.That(CombatSession.FateIndex(raw),Is.EqualTo(index));
        [Test] public void EveryEchoMapsToOneOfTwentyValues(){for(int raw=1;raw<=20;raw++)Assert.That(CombatSession.FateEchoValue(raw),Is.EqualTo(raw*5));}
        [TestCase(1)][TestCase(9)][TestCase(17)][TestCase(25)][TestCase(35)][TestCase(45)][TestCase(55)][TestCase(65)][TestCase(75)][TestCase(83)][TestCase(91)][TestCase(100)]
        public void EveryFateEffectIsAppliedOnceAndRoundTrips(int raw)
        {
            PrepareFate(raw);var c=Cmd(CommandKind.V13FateAccept);c.qualified=false;Good(c);Assert.That(S.v13.fate.stage,Is.EqualTo("Applied"));Assert.That(S.v13.fate.effectId,Is.Not.Null);string applied=CombatJson.Write(S);Assert.That(game.Execute(c).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(applied));game=new CombatSession(CombatJson.Read<CombatState>(applied),CombatJson.Clone(config));Assert.That(S.v13.fate.final,Is.EqualTo(raw));
            if(raw==25||raw==65)Assert.That(S.v13.fate.fallback,Is.Not.Null);if(raw==35)Assert.That(S.Actor(E).p6.absorptionShield,Is.Zero);if(raw==55)Assert.That(Enemy.statuses.Any(x=>x.id=="v13-fate-opening"),Is.True);if(raw==75)Assert.That(S.actors.All(a=>a.statuses.Any(x=>x.id=="v13-inspiration")),Is.True);if(raw==91)Assert.That(S.v13.terrain.Any(t=>t.kind=="lightning"),Is.True);
        }
        [Test] public void FateUsesIndependentRngAndEchoReplacementIsSinglePayment()
        {var before=CombatJson.Write(S.random);S.v13.echo=new V13Echo{id="executed-own-roll",actorId=M,raw=14};PrepareFate(1);Assert.That(CombatJson.Write(S.random),Is.EqualTo(before));int inspiration=S.Actor(M).inspiration;Good(Cmd(CommandKind.V13FateReplace));Assert.That(S.v13.fate.final,Is.EqualTo(70));Assert.That(S.v13.echo,Is.Null);Assert.That(S.Actor(M).inspiration,Is.EqualTo(inspiration-1));Assert.That(game.Execute(Cmd(CommandKind.V13FateReplace)).success,Is.False);}
        [Test] public void FateBlocksNewOrdersAndPausesFocusBudget()
        {PrepareFate(17);float focus=S.focusBudget.secondsRemaining,sim=S.simulationSeconds;Assert.That(game.Execute(Cmd(CommandKind.EnterFocus)).reason,Is.EqualTo("fate_decision_only"));game.Tick(5,true);Assert.That(S.simulationSeconds-sim,Is.EqualTo(.6).Within(.001));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(focus));Assert.That(S.v13.fate.decision,Is.EqualTo(1));}
        [Test] public void EntryTriggersDoNotRepeatAndTimeBudgetIsIndependent()
        {CombatSession.V13EnterEncounter(S,config,"V12_R4");Assert.That(S.v13.fate,Is.Not.Null);CombatSession.V13ClearEncounter(S);CombatSession.V13EnterEncounter(S,config,"V12_R4");Assert.That(S.v13.fate,Is.Null);CombatSession.V13EnterEncounter(S,config,"V12_R6");Assert.That(S.v13.fate.trigger,Is.EqualTo("V12_R6"));}
        [Test] public void BossPillarsHaveOneRealCollisionReceiptAndNoHpLock()
        {S.v13.boss=new V13Boss{actorId=Enemy.id,stage="Charge"};Enemy.x=-4;Enemy.z=0;int hp=Enemy.hp;var c=Cmd(CommandKind.V13PillarHit,Enemy.id);c.targetId="pillar-A";c.authority=CommandAuthority.System;Good(c);Assert.That(Enemy.hp,Is.EqualTo(hp-12));Assert.That(S.v13.boss.pillarA,Is.True);Assert.That(game.Execute(c).duplicate,Is.True);Assert.That(game.Execute(Cmd(CommandKind.V13PillarHit,Enemy.id)).success,Is.False);}
        [Test] public void NodesDoNotRollOrRewardAndEachRemovesOneBand()
        {S.v13.boss=new V13Boss{actorId=Enemy.id,stage="Ritual",root="ritual",nodeA=true,nodeB=true};S.Actor(M).x=-4;S.Actor(M).z=3;var rng=CombatJson.Write(S.random);var c=Cmd(CommandKind.V13NodeHit,M,config.BasicFor(S.Actor(M)).id);c.targetId="v13-node-A";Good(c);Assert.That(S.v13.boss.nodeA,Is.False);Assert.That(S.v13.boss.nodeB,Is.True);Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));game.Tick(1.3f);S.Actor(M).x=4;c=Cmd(CommandKind.V13NodeHit,M,config.BasicFor(S.Actor(M)).id);c.targetId="v13-node-B";Good(c);Assert.That(S.v13.boss.stage,Is.EqualTo("Recovery"));}
    }
}
