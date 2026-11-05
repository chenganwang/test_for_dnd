using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Deterministic rule fixtures use explicit positions/HP/seed search. They are not Player/fun evidence.
    public sealed class V12HeroTests
    {
        const string Evan=CombatSession.V12Evan,Mira=CombatSession.V12Mira,Finn=CombatSession.V12Finn;
        CombatConfig config;CombatSession session;int sequence;
        CombatState S=>session.State;ActorState Enemy=>S.enemies[0];
        [SetUp] public void Setup()
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig(),v11=new V11CombatRules{version="v12-character-1",coreActorId=Mira}};
            foreach(string id in new[]{Evan,Mira,Finn})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==Evan?"v11_evan_insight":id==Mira?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,unlockedActionIds=new List<string>{CombatSession.V12BasicAction(id),CombatSession.V12KeyAction(id)},finnSustain=id==Finn?new V11FinnSustainConfig():null});
            var s=CombatState.CreatePartyForPreset("v12-rule","run",41,config);foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=1000;session=new CombatSession(s,config);sequence=0;
        }
        CombatCommand C(CommandKind kind,string actor=Evan,string action=null)=>new CombatCommand{id="v12/"+(++sequence),kind=kind,actorId=actor,targetId=Enemy.id,actionId=action??CombatSession.V12BasicAction(actor),qualified=true};
        CombatResult Good(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Tick(float seconds)
        {session.Tick(seconds);Assert.That(session.LastTickError,Is.Null);if(session.V12TickPending)Good(new CombatCommand{id="v12/tick/"+(++sequence),kind=CommandKind.ResolveHeroTick,authority=CommandAuthority.System});}
        CombatResult Cast(string actor,string action=null,float x=0,float z=0)
        {var c=C(CommandKind.RequestHeroAction,actor,action);c.x=x;c.z=z;Good(c);Tick(S.Actor(actor).v12.intent.remaining+.001f);var end=C(CommandKind.ResolveHeroAction,actor,action);end.orderId=c.id;end.authority=CommandAuthority.System;return Good(end);}
        void SeedBackground(int raw)
        {for(ulong n=1;n<10000;n++){var streams=RandomStreams.Create(n);if(RandomStreams.D20(CombatJson.Clone(streams.backgroundCombat))==raw){S.random.backgroundCombat=streams.backgroundCombat;return;}}Assert.Fail("seed_missing");}
        CombatResult Basic(string actor,int raw=14)
        {var a=S.Actor(actor);if(a.majorRemaining>0)Tick(a.majorRemaining+.001f);SeedBackground(raw);var start=C(CommandKind.StartBasicAttack,actor);Good(start);Tick(config.basicContactSeconds+.001f);var end=C(CommandKind.ResolveBasicAttack,actor);end.orderId=start.id;end.authority=CommandAuthority.System;return Good(end);}
        [Test] public void NewActionsAreThreePairsAndOldIdsRemainAvailable()
        {foreach(var id in new[]{Evan,Mira,Finn})Assert.That(config.v11.actors.Single(x=>x.actorId==id).unlockedActionIds.Count,Is.EqualTo(2));Assert.That(config.Action(CombatSession.P6SmiteId),Is.Not.Null);Assert.That(config.Action(CombatSession.P6BindId),Is.Not.Null);}
        [Test] public void FreeMarkCostsNoChargeAndHasNoVisibleDice()
        {int charge=S.charges;ulong draws=S.random.visibleTactical.drawCount;Cast(Evan);Assert.That(S.charges,Is.EqualTo(charge));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(draws));Assert.That(Enemy.statuses.Single(x=>x.v12?.family=="opening").remaining,Is.EqualTo(6));}
        [Test] public void NewMarkReplacesOnlyItsOwnPreviousTarget()
        {
            Cast(Evan);var second=CombatJson.Clone(Enemy);second.id="other";second.statuses.Clear();S.enemies.Add(second);Tick(8);var cmd=C(CommandKind.RequestHeroAction);cmd.targetId="other";Good(cmd);Tick(.36f);var end=C(CommandKind.ResolveHeroAction);end.orderId=cmd.id;end.authority=CommandAuthority.System;Good(end);Assert.That(S.enemies.SelectMany(x=>x.statuses).Count(x=>x.v12?.family=="opening"),Is.EqualTo(1));Assert.That(S.Actor("other").statuses.Any(x=>x.v12?.family=="opening"),Is.True);
        }
        [Test] public void MarkClearsWhenItsLivingSourceIsLost()
        {Cast(Evan);S.Actor(Evan).hp=0;Tick(.01f);Assert.That(Enemy.statuses.Any(x=>x.v12?.family=="opening"),Is.False);}
        [Test] public void AllyDirectContactTriggersOneNonRecursiveAnnotation()
        {config.Template(ActorRole.Mage).basic.damage=10;Cast(Evan);Basic(Mira);Assert.That(S.v12.events.Count(x=>x.kind=="echo"),Is.EqualTo(1));Basic(Mira);Assert.That(S.v12.events.Count(x=>x.kind=="echo"),Is.EqualTo(1));}
        [Test] public void EvanOwnDamageAndDotCannotTriggerAnnotation()
        {Cast(Evan);Basic(Evan);Cast(Finn);Tick(3);Assert.That(S.v12.events.Any(x=>x.kind=="echo"),Is.False);}
        [Test] public void LethalContactDoesNotShootDeadTargetOrSpendEchoCooldown()
        {Cast(Evan);Enemy.hp=1;Basic(Mira);Assert.That(S.v12.events.Any(x=>x.kind=="echo"),Is.False);Assert.That(S.Actor(Evan).v12.echoCooldown,Is.Zero);}
        [Test] public void FullyAbsorbedContactDoesNotTriggerAnnotationOrFinnHeal()
        {Cast(Evan);Enemy.p6.absorptionShield=100;S.Actor(Finn).hp=10;Basic(Finn);Assert.That(S.v12.events.Any(x=>x.kind=="echo"),Is.False);Assert.That(S.Actor(Finn).v11.finnSustain,Is.Null);}
        [Test] public void FinalDoesNotRequireOpeningAndOpeningProvidesAdvantage()
        {Assert.That(session.Quote(Evan,Enemy.id,"v12_evan_final").legal,Is.True);Assert.That(session.Quote(Evan,Enemy.id,"v12_evan_final").advantage,Is.False);Cast(Evan);Tick(.5f);Assert.That(session.Quote(Evan,Enemy.id,"v12_evan_final").advantage,Is.True);S.Actor(Evan).statuses.Add(new StatusState{id="suppressed",remaining=3});Assert.That(session.Quote(Evan,Enemy.id,"v12_evan_final").disadvantage,Is.True);}
        [Test] public void ChaseNeedsPhysicalDestinationEvidenceAndDoesNotTeleport()
        {Enemy.x=4;var c=C(CommandKind.RequestHeroAction,Mira);c.x=2;c.qualified=false;Assert.That(session.Execute(c).success,Is.False);c.id+="/legal";c.qualified=true;Good(c);Assert.That(S.Actor(Mira).x,Is.Zero);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.Zero);}
        [Test] public void ActualChaseTravelReadiesEvenWhenTheSlashMisses()
        {Enemy.x=4;var c=C(CommandKind.RequestHeroAction,Mira);c.x=2;Good(c);S.Actor(Mira).x=2;Tick(.3f);SeedBackground(1);var end=C(CommandKind.ResolveHeroAction,Mira);end.orderId=c.id;end.authority=CommandAuthority.System;Assert.That(Good(end).hit,Is.False);Assert.That(S.Actor(Mira).x,Is.EqualTo(2));Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.EqualTo(4));}
        [Test] public void AdjacentChaseHasNoFreePreparationAndMissDoesNotConsumeExistingPreparation()
        {Cast(Mira);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.Zero);S.Actor(Mira).v12.readinessRemaining=4;Basic(Mira,1);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.GreaterThan(0));Basic(Mira);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.Zero);}
        [Test] public void EmptyDodgeDoesNotPrepareMira()
        {Good(C(CommandKind.StartDodge,Mira));Tick(.4f);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.Zero);}
        [Test] public void RegisteredDangerousDodgePreparesOnce()
        {S.momentumLedger.registeredDangerousThreats.Add("enemy-swing");Good(C(CommandKind.StartDodge,Mira));Good(new CombatCommand{id="threat",kind=CommandKind.IncomingAttack,actorId=Enemy.id,targetId=Mira,rootId="enemy-swing",amount=5,qualified=true,authority=CommandAuthority.System});Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.EqualTo(4));}
        [Test] public void ParasiteHasSixDelayedTicksIncludingTheExpiryTick()
        {Cast(Finn);Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.Zero);Tick(.99f);Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.Zero);Tick(5.02f);Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.EqualTo(6));Assert.That(Enemy.statuses.Any(x=>x.v12?.family=="parasite"),Is.False);}
        [Test] public void ParasiteRefreshKeepsNextTickAndNeverCreatesSecondTimer()
        {config.v12.seedCooldown=.5f;Cast(Finn);Tick(.4f);float next=Enemy.statuses.Single(x=>x.v12?.family=="parasite").v12.nextTick;Cast(Finn);Assert.That(Enemy.statuses.Count(x=>x.v12?.family=="parasite"),Is.EqualTo(1));Assert.That(Enemy.statuses.Single(x=>x.v12?.family=="parasite").v12.nextTick,Is.EqualTo(next));Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.Zero);}
        [Test] public void ParasiteDoesNotHealAndContinuesAfterFinnDown()
        {S.Actor(Finn).hp=10;Cast(Finn);Tick(3);Assert.That(S.Actor(Finn).hp,Is.EqualTo(10));S.Actor(Finn).hp=0;Tick(3.1f);Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.EqualTo(6));Assert.That(S.Actor(Finn).hp,Is.Zero);}
        [Test] public void RollFreezeAndPauseStopStatusTickCooldownAndFocus()
        {Cast(Finn);Good(C(CommandKind.EnterFocus));Good(C(CommandKind.ConfirmRoll,Mira,"v12_mira_break"));float now=S.simulationSeconds,cd=S.Actor(Finn).v12.basicCooldown,budget=S.focusBudget.secondsRemaining;Tick(2);Assert.That(S.simulationSeconds,Is.EqualTo(now));Assert.That(S.Actor(Finn).v12.basicCooldown,Is.EqualTo(cd));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(budget));Assert.That(S.v12.events.Any(x=>x.kind=="dot"),Is.False);Good(C(CommandKind.Pause));Tick(2);Assert.That(S.simulationSeconds,Is.EqualTo(now));}
        [Test] public void GroveChargesOnceWithoutFakeRollAndHealsOnlyInside()
        {foreach(var a in S.actors){a.maxHp=100;a.hp=50;}S.Actor(Evan).x=10;int charges=S.charges;Cast(Finn,"v12_finn_grove");Assert.That(S.charges,Is.EqualTo(charges-1));Assert.That(S.rolls,Is.Empty);Tick(6.1f);Assert.That(S.Actor(Finn).hp,Is.EqualTo(56));Assert.That(S.Actor(Mira).hp,Is.EqualTo(56));Assert.That(S.Actor(Evan).hp,Is.EqualTo(50));}
        [Test] public void LegacyGroveIntegerTickNeverPersistsNegativeRoundoffCarry()
        {Assert.That(config.v12.build,Is.Null);var a=S.Actor(Finn);a.maxHp=100;a.hp=50;Cast(Finn,"v12_finn_grove");for(int i=1;i<=6;i++){Tick(1.001f);Assert.That(S.Actor(Finn).hp,Is.EqualTo(50+i));Assert.That(S.Actor(Finn).v12.groveHealingRemainder,Is.Zero);Assert.That(session.Save().success,Is.True);}}
        [Test] public void LegacyGroveRestoredFractionNearWholeHealsOnceWithoutInvalidCarry()
        {Assert.That(config.v12.build,Is.Null);S.Actor(Finn).hp=10;S.Actor(Finn).v12.groveHealingRemainder=.78;session=new CombatSession(CombatJson.Clone(S),config);Cast(Finn,"v12_finn_grove");Tick(1.001f);Assert.That(S.Actor(Finn).hp,Is.EqualTo(11));Assert.That(S.Actor(Finn).v12.groveHealingRemainder,Is.InRange(0,1e-7));Assert.That(session.Save().success,Is.True);}
        [Test] public void LegacyGrovePositiveSavedFractionKeepsItsPrecisionWithoutBuildQuantization()
        {Assert.That(config.v12.build,Is.Null);const double carry=.123456789;S.Actor(Finn).hp=10;S.Actor(Finn).v12.groveHealingRemainder=carry;session=new CombatSession(CombatJson.Clone(S),config);Cast(Finn,"v12_finn_grove");Tick(1.001f);double nominal=(double)(S.Actor(Finn).maxHp*config.v12.groveHealFraction);Assert.That(S.Actor(Finn).hp,Is.EqualTo(10));Assert.That(S.Actor(Finn).v12.groveHealingRemainder,Is.EqualTo(carry+nominal).Within(3e-8));Assert.That(S.Actor(Finn).v12.groveHealingRemainder,Is.Not.EqualTo(Math.Round(carry+nominal,6)));Assert.That(session.Save().success,Is.True);}
        [Test] public void GroveExitRemovesEntangleButKeepsParasite()
        {Cast(Finn,"v12_finn_grove");Assert.That(session.V12MovementMultiplier(Enemy.id),Is.EqualTo(.65f));Enemy.x=10;Tick(.01f);Assert.That(session.V12MovementMultiplier(Enemy.id),Is.EqualTo(1));Assert.That(Enemy.statuses.Any(x=>x.v12?.family=="parasite"),Is.True);Enemy.x=1;Tick(.01f);Assert.That(S.v12.events.Count(x=>x.kind=="parasite"),Is.EqualTo(1));Assert.That(session.V12MovementMultiplier(Enemy.id),Is.EqualTo(.65f));}
        [Test] public void GroveLimitsFourEnemiesAndBossStillSlows()
        {for(int i=0;i<6;i++){var e=CombatJson.Clone(Enemy);e.id="extra"+i;S.enemies.Add(e);}S.world.strings["enemyTemplate/"+Enemy.id]="boss";Cast(Finn,"v12_finn_grove");Assert.That(S.v12.groves.Single().touchedEnemies.Count,Is.EqualTo(4));Assert.That(session.V12MovementMultiplier(Enemy.id),Is.EqualTo(.85f));}
        [Test] public void DeadFinnEndsGroveWithoutRevival()
        {Cast(Finn,"v12_finn_grove");S.Actor(Finn).hp=0;Tick(.01f);Assert.That(S.v12.groves,Is.Empty);Assert.That(S.Actor(Finn).hp,Is.Zero);Assert.That(session.V12MovementMultiplier(Enemy.id),Is.EqualTo(1));}
        [Test] public void AiCanUseFreeAidButCannotSpendSharedChargesOrReplacePlayerOrder()
        {var c=C(CommandKind.RequestHeroAction,Finn);c.authority=CommandAuthority.AllyAI;int charges=S.charges;Good(c);Assert.That(S.charges,Is.EqualTo(charges));c=C(CommandKind.RequestHeroAction,Evan,"v12_evan_final");c.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(c).success,Is.False);S.Actor(Evan).basicOrder=new BasicOrder{id="manual",kind=BasicOrderKind.Hold,stage="Holding"};c=C(CommandKind.RequestHeroAction,Evan);c.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(c).success,Is.False);}
        [Test] public void DuplicateGroveRequestCannotChargeTwice()
        {var c=C(CommandKind.RequestHeroAction,Finn,"v12_finn_grove");Good(c);int charges=S.charges;Assert.That(Good(c).duplicate,Is.True);Assert.That(S.charges,Is.EqualTo(charges));}
        [Test] public void PendingFreeAidWaitsForCurrentContactThenLocksItsOwnWindup()
        {var basic=C(CommandKind.StartBasicAttack,Evan);Good(basic);var mark=C(CommandKind.RequestHeroAction,Evan);Good(mark);Assert.That(S.Actor(Evan).v12.intent.stage,Is.EqualTo("WaitingBasic"));Tick(.13f);var contact=C(CommandKind.ResolveBasicAttack,Evan);contact.orderId=basic.id;contact.authority=CommandAuthority.System;Good(contact);Tick(.01f);Assert.That(S.Actor(Evan).v12.intent.stage,Is.EqualTo("Casting"));Assert.That(session.Execute(C(CommandKind.StartBasicAttack,Evan)).success,Is.False);}
        [Test] public void StateSerializationPreservesTickScheduleAndCommandReceipts()
        {Cast(Finn);Tick(1.5f);string serialized=CombatJson.Write(S);var restored=new CombatSession(CombatJson.Read<CombatState>(serialized),CombatJson.Clone(config));Assert.That(CombatJson.Write(restored.State),Is.EqualTo(serialized));session=restored;Tick(4.6f);Assert.That(S.v12.events.Count(x=>x.kind=="dot"),Is.EqualTo(6));}
        [Test] public void ExistingRadiantMarkAndNewOpeningTakeMaximumPerDamageChannel()
        {
            const string pal=P6CharacterCatalog.PaladinId;
            config.v11.coreActorId=pal;config.v11.actors.RemoveAll(a=>a.actorId==Mira||a.actorId==Finn);
            foreach(string id in new[]{pal,"mage"}){var d=V11Catalog.FindCharacterForRules(id,"v11-character-1");config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=d.passiveId,passiveUnlocked=false,unlockedActionIds=new List<string>(d.actionIds)});}
            config.v11.actors.Single(a=>a.actorId==Evan).passiveUnlocked=false;
            var initial=CombatState.CreatePartyForPreset("v12-p6-channel","run",41,config);foreach(var a in initial.actors){a.x=0;a.z=0;}initial.enemies[0].x=1;initial.enemies[0].z=0;initial.enemies[0].hp=initial.enemies[0].maxHp=2000;session=new CombatSession(initial,config);
            Cast(Evan);var bind=C(CommandKind.RequestBindRadiance,"mage",CombatSession.P6BindId);Good(bind);Tick(.76f);var resolve=C(CommandKind.ResolveBindRadiance,"mage",CombatSession.P6BindId);resolve.orderId=bind.id;resolve.authority=CommandAuthority.System;Good(resolve);
            var paired=S.Clone();var withoutOpening=paired.Clone();withoutOpening.enemies[0].statuses.RemoveAll(x=>x.v12?.family=="opening");
            P6DamageResult Run(CombatState start)
            {
                session=new CombatSession(start,config);Good(C(CommandKind.EnterFocus,pal));Good(C(CommandKind.ConfirmRoll,pal,CombatSession.P6SmiteId));Good(C(CommandKind.RevealRoll,pal));Good(C(CommandKind.Commit,pal));Good(C(CommandKind.ExitFocus,pal));var q=S.queue.Last();var begin=C(CommandKind.StartQueued,pal);begin.queueId=q.id;Good(begin);Tick(.36f);var finish=C(CommandKind.ResolveQueued,pal);finish.queueId=q.id;finish.authority=CommandAuthority.System;return Good(finish).p6.damages.Single();
            }
            var baseline=Run(withoutOpening);var coexist=Run(paired);Assert.That(baseline.hit,Is.True,"fixture seed must exercise an actual hit");Assert.That(coexist.attackFaces,Is.EqualTo(baseline.attackFaces));Assert.That(coexist.weaponFaces,Is.EqualTo(baseline.weaponFaces));Assert.That(coexist.radiantFaces,Is.EqualTo(baseline.radiantFaces));Assert.That(coexist.radiantMultiplier,Is.EqualTo(1.5));Assert.That(coexist.radiantApplied,Is.EqualTo(baseline.radiantApplied));Assert.That(coexist.physicalApplied,Is.EqualTo((int)Math.Floor(baseline.physicalApplied*1.2f)));Assert.That(coexist.markConsumed,Is.True);
        }
        [Test] public void DotAppliesLiveOpeningOnlyOnceAndCannotTriggerMicroHeal()
        {config.Template(ActorRole.Mage).basic.damage=20;S.Actor(Finn).hp=10;Cast(Evan);Cast(Finn);Tick(1.01f);var tick=S.v12.events.Single(e=>e.kind=="dot");Assert.That(tick.amount,Is.EqualTo(8));Assert.That(S.Actor(Finn).v11.finnSustain,Is.Null);Assert.That(S.v12.events.Any(e=>e.kind=="echo"),Is.False);}
        [Test] public void ParasiteTickDuplicateCommandDoesNotRepeatDamage()
        {Cast(Finn);session.Tick(1.1f);var c=new CombatCommand{id="fixed-tick",kind=CommandKind.ResolveHeroTick,authority=CommandAuthority.System};Good(c);int hp=Enemy.hp,count=S.v12.events.Count;Assert.That(Good(c).duplicate,Is.True);Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.v12.events.Count,Is.EqualTo(count));}
        [Test] public void ObstructedChaseCannotResolveOrGainPreparation()
        {Enemy.x=4;var c=C(CommandKind.RequestHeroAction,Mira);c.x=2;Good(c);Tick(.3f);var end=C(CommandKind.ResolveHeroAction,Mira);end.orderId=c.id;end.authority=CommandAuthority.System;end.qualified=false;var r=Good(end);Assert.That(r.damage,Is.Zero);Assert.That(S.Actor(Mira).v12.readinessRemaining,Is.Zero);Assert.That(S.Actor(Mira).x,Is.Zero);}
        [TestCase(1,6)][TestCase(2,12)] public void OpeningAccumulatesSmallIntegerContactsWithinItsOwnWindow(int damage,int expected)
        {Cast(Evan);int before=Enemy.hp;for(int i=0;i<5;i++){SeedBackground(14);Good(new CombatCommand{id="small/"+i,kind=CommandKind.IncomingAttack,actorId=Evan,targetId=Enemy.id,rootId="small/"+i,amount=damage,qualified=true,authority=CommandAuthority.System});}Assert.That(before-Enemy.hp,Is.EqualTo(expected));}
        [TestCase(true)][TestCase(false)] public void ImmunityAndFullyAbsorbedHitsDoNotBankOpeningFraction(bool immune)
        {Cast(Evan);if(immune)Enemy.p6.physicalDamageMultiplier=0;else Enemy.p6.absorptionShield=100;for(int i=0;i<5;i++){SeedBackground(14);Good(new CombatCommand{id="blocked/"+i,kind=CommandKind.IncomingAttack,actorId=Evan,targetId=Enemy.id,rootId="blocked/"+i,amount=1,qualified=true,authority=CommandAuthority.System});}Assert.That(Enemy.statuses.Single(x=>x.v12?.family=="opening").v12.damageRemainder,Is.Zero);Enemy.p6.physicalDamageMultiplier=1;Enemy.p6.absorptionShield=0;SeedBackground(14);int before=Enemy.hp;Good(new CombatCommand{id="unblocked",kind=CommandKind.IncomingAttack,actorId=Evan,targetId=Enemy.id,rootId="unblocked",amount=1,qualified=true,authority=CommandAuthority.System});Assert.That(before-Enemy.hp,Is.EqualTo(1));}
        [Test] public void TwoDamageBaseParasiteCanRealiseOpeningWithoutPerTickCeiling()
        {Cast(Finn);Cast(Evan);int before=Enemy.hp;Tick(6.01f);Assert.That(before-Enemy.hp,Is.EqualTo(5));Assert.That(S.v12.events.Where(x=>x.kind=="dot").Select(x=>x.amount).Max(),Is.EqualTo(1));}
        [TestCase("nan")][TestCase("negative")][TestCase("source")][TestCase("duplicates")][TestCase("ancient-schedule")]
        public void InvalidGroveSnapshotIsRejectedWithoutReset(string corruption)
        {Cast(Finn,"v12_finn_grove");var s=S.Clone();var g=s.v12.groves.Single();if(corruption=="nan")g.radius=float.NaN;if(corruption=="negative")g.nextTick=-1;if(corruption=="source")g.sourceId="unknown";if(corruption=="duplicates")g.touchedEnemies.Add(g.touchedEnemies[0]);if(corruption=="ancient-schedule")g.expiresAt=1e9f;Assert.Throws<InvalidOperationException>(()=>new CombatSession(s,config));Assert.That(S.v12.groves.Single().radius,Is.EqualTo(config.v12.groveRadius));}
        [TestCase("nan")][TestCase("fraction")][TestCase("generation")][TestCase("schedule")]
        public void InvalidStatusSnapshotIsRejected(string corruption)
        {Cast(Finn);var s=S.Clone();var p=s.enemies[0].statuses.Single(x=>x.v12?.family=="parasite").v12;if(corruption=="nan")p.multiplier=float.NaN;if(corruption=="fraction")p.damageRemainder=double.PositiveInfinity;if(corruption=="generation")p.generation=2;if(corruption=="schedule")p.nextTick=-1;Assert.Throws<InvalidOperationException>(()=>new CombatSession(s,config));}
        [Test] public void ParasiteHighThreatKillUsesExistingOutcomeLedgerExactlyOnce()
        {S.momentumLedger.registeredHighThreats.Add(Enemy.id);Enemy.hp=1;Cast(Finn);Tick(2.1f);Assert.That(Enemy.Alive,Is.False);Assert.That(S.momentumLedger.resolvedThreats.Count(x=>x==Enemy.id),Is.EqualTo(1));Tick(3);Assert.That(S.momentumLedger.resolvedThreats.Count(x=>x==Enemy.id),Is.EqualTo(1));}
    }
}
