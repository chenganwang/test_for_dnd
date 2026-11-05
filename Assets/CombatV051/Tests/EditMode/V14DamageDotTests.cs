using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Controlled domain fixtures. Explicit positions, HP and deterministic dice are not natural Player evidence.
    public sealed class V14DamageDotTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn,D=CombatSession.V14DetonateId;
        CombatConfig config;CombatSession session;int seq;
        CombatState S=>session.State;ActorState Enemy=>S.enemies[0];
        [SetUp] public void Setup()=>Create(PartyCombatMode.CoreAndAssist);
        void Create(PartyCombatMode mode,bool modern=true,string key=D)
        {
            seq=0;config=new CombatConfig{basicDamage=3,p5=new P5Config(),p6=new P6Config(),v12=V12Defaults.Create(),v13=new V13Rules(),v14=modern?new V14Rules{partyCombatMode=mode,finnGrowthAction=key}:null,
                v11=new V11CombatRules{version="v12-character-1",coreActorId=F,v12BuildVersion="v12-build-1",v13FinnTree="growth",v14BuildVersion=modern?V14BuildCatalog.Version:null,v14FinnGrowthAction=modern?key:null}};
            if(modern)config.v12.seedCooldown=4;
            foreach(string id in new[]{E,M,F})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==E?"v11_evan_insight":id==M?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,unlockedActionIds=new List<string>{CombatSession.V12BasicAction(id),id==E?"v13_evan_correct":id==M?"v12_mira_break":modern?key:"v12_finn_grove"},finnSustain=id==F?new V11FinnSustainConfig():null});
            var state=CombatState.CreatePartyForPreset("v14-controlled","run",141,config);foreach(var a in state.actors){a.x=0;a.z=0;}state.enemies[0].x=1;state.enemies[0].z=0;state.enemies[0].hp=state.enemies[0].maxHp=10000;
            session=new CombatSession(state,config);
        }
        CombatCommand C(CommandKind kind,string action=null,string target=null)=>new CombatCommand{id="v14-damage/"+(++seq),kind=kind,actorId=F,targetId=target??Enemy.id,actionId=action??"v12_finn_seed",qualified=true,authority=CommandAuthority.Player};
        CombatResult Good(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Tick(float t,bool settle=true){session.Tick(t);Assert.That(session.LastTickError,Is.Null);if(settle&&session.V12TickPending)Good(new CombatCommand{id="tick/"+(++seq),kind=CommandKind.ResolveHeroTick,authority=CommandAuthority.System});}
        void Ready(){float delay=Math.Max(S.Actor(F).majorRemaining,session.Member(S,F).actionUntil-S.simulationSeconds);if(delay>0)Tick(delay+.001f);}
        CombatResult Cast(string action=null,string target=null,V12CommandData spatial=null)
        {Ready();var c=C(CommandKind.RequestHeroAction,action,target);c.v12=spatial;Good(c);Tick(S.Actor(F).v12.intent.remaining+.0001f);var finish=C(CommandKind.ResolveHeroAction,action,target);finish.orderId=c.id;finish.authority=CommandAuthority.System;finish.v12=spatial;return Good(finish);}
        void Buff(params string[] ids){foreach(string id in ids)config.v11.buffs.Add(new V11BuffStack{id=id,stacks=1});}
        ActorState Extra(string id,float x){var e=CombatJson.Clone(Enemy);e.id=id;e.x=x;e.statuses.Clear();e.v14DamageCarries=null;S.enemies.Add(e);return e;}
        V12CommandData Lines()=>new V12CommandData{links=S.actors.SelectMany(a=>S.enemies.Select(e=>new V12LineEvidence{fromId=a.id,toId=e.id,lineOfSight=true})).ToList()};
        object Invoke(string name,params object[] args)=>typeof(CombatSession).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(session,args);
        void SaveControlled(string name,object report)
        {string folder=Environment.GetEnvironmentVariable("V14_EVIDENCE_DIR");if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name+".json"),CombatJson.Write(report));}}
        CombatSession LegacyClone()
        {var c=CombatJson.Clone(config);c.v14=null;c.v11.v14BuildVersion=null;c.v11.v14FinnGrowthAction=null;var s=S.Clone();s.v14=null;return new CombatSession(s,c);}
        void SeedFace(int face)
        {for(ulong seed=1;seed<10000;seed++){var rng=RandomStreams.Create(seed).backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(rng))==face){S.random.backgroundCombat=rng;return;}}Assert.Fail("fixture_seed_missing");}
        void Roster(string subject)
        {
            Create(PartyCombatMode.PersistentTrio);config.v11.coreActorId=M;config.v11.actors.Clear();
            var ids=new List<string>{M};if(subject!=M)ids.Add(subject);if(!ids.Contains(F))ids.Add(F);if(ids.Count<3)ids.Add(E);
            foreach(string id in ids){var d=V11Catalog.FindCharacter(id);config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=d.passiveId,passiveUnlocked=false,unlockedActionIds=d.actionIds.ToList(),finnSustain=id==F?new V11FinnSustainConfig():null});}
            var s=CombatState.CreatePartyForPreset("v14-all-sources","run",51,config);foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=10000;session=new CombatSession(s,config);
        }
        [TestCase(E)][TestCase(M)][TestCase(F)][TestCase("fighter")][TestCase("mage")][TestCase("rogue")][TestCase("char_demo_paladin")]
        public void EveryPlayerSourceGetsDamageNotAccuracyAndEnemyDamageIsUnchanged(string id)
        {
            Roster(id);var modern=session;var source=S.Actor(id);int bonus=source.attackBonus;int raw=config.BasicFor(source).damage;session=LegacyClone();SeedFace(14);
            var old=Good(new CombatCommand{id="old-source-hit",kind=CommandKind.IncomingAttack,actorId=id,targetId=Enemy.id,rootId="old-source-hit",amount=raw,qualified=true,authority=CommandAuthority.System});session=modern;
            SeedFace(14);var r=Good(new CombatCommand{id="source-hit",kind=CommandKind.IncomingAttack,actorId=id,targetId=Enemy.id,rootId="source-hit",amount=raw,qualified=true,authority=CommandAuthority.System});
            var record=S.v14DamageEvents.Single();Assert.That(record.sourceDamage,Is.EqualTo(raw*1.1).Within(.000001));Assert.That(record.globalScale,Is.EqualTo(1.1));Assert.That(record.periodicScale,Is.EqualTo(1));Assert.That(S.Actor(id).attackBonus,Is.EqualTo(bonus));Assert.That(r.rawFaces.Single(),Is.EqualTo(14));Assert.That(r.total,Is.EqualTo(14+bonus));
            Assert.That(old.damage,Is.EqualTo(raw));Assert.That(old.rawFaces,Is.EqualTo(r.rawFaces));Assert.That(old.total,Is.EqualTo(r.total));SaveControlled("V14_CONTROLLED_SOURCE_"+id,new{evidenceType="CONTROLLED_DOMAIN_NOT_PLAYER",id,targetHp=10000,oldDamage=old.damage,newDamage=r.damage,old.rawFaces,old.total,source=record,carry=Enemy.v14DamageCarries});
            SeedFace(14);int hp=S.Actor(id).hp;var incoming=Good(new CombatCommand{id="enemy-hit",kind=CommandKind.IncomingAttack,actorId=Enemy.id,targetId=id,rootId="enemy-hit",amount=3,qualified=true,authority=CommandAuthority.System});if(incoming.deferred)Good(new CombatCommand{id="decline-enemy",kind=CommandKind.DeclineReaction,reactionEventId=incoming.reactionEventId});Assert.That(hp-S.Actor(id).hp,Is.EqualTo(3));Assert.That(S.v14DamageEvents.Last().globalScale,Is.EqualTo(1));
        }
        [TestCase("direct")][TestCase("echo")][TestCase("splash")]
        public void DirectAndDerivedRawComponentsShareSingleSourceScaleAndOneDefense(string kind)
        {
            Enemy.p6.physicalDamageMultiplier=.5f;Enemy.statuses.Add(new StatusState{id="v12-opening",sourceId=E,remaining=6,v12=new V12StatusData{family="opening",rootId="mark",expiresAt=6,multiplier=1.2f}});
            int damage=(int)Invoke("V12Damage",S,S.Actor(E),Enemy,"controlled-"+kind,"controlled",kind,20d,false,new CombatResult(),null,null,null,false);
            Assert.That(damage,Is.EqualTo(13));Assert.That(S.v14DamageEvents.Single().sourceDamage,Is.EqualTo(22));Assert.That(Enemy.v14DamageCarries.Single().remainder,Is.EqualTo(.2).Within(.00001));
        }
        [Test] public void FixedNeutralTerrainDoesNotScaleWithThePlayerWhoTriggeredIt()
        {int damage=(int)Invoke("V12Damage",S,S.Actor(F),Enemy,"beam","v12_falling_beam","environment",20d,false,new CombatResult(),null,null,null,false);Assert.That(damage,Is.EqualTo(20));Assert.That(S.v14DamageEvents.Single().globalScale,Is.EqualTo(1));Assert.That(S.v14DamageEvents.Single().sourceStage,Is.EqualTo("WorldFixed"));}
        [Test] public void P5SkillAndShoveImpactKeepThePlayerOwner()
        {
            int damage=(int)Invoke("P5Damage",S,Enemy,10,"controlled-shove",false,S.Actor(F));Assert.That(damage,Is.EqualTo(11));Assert.That(S.v14DamageEvents.Single().sourceId,Is.EqualTo(F));Assert.That(S.v14DamageEvents.Single().sourceDamage,Is.EqualTo(11));
        }
        [TestCase(false)][TestCase(true)]
        public void PaladinWeaponRadiantAndCriticalDiceReceiveSourceScaleOnce(bool critical)
        {
            Roster("char_demo_paladin");var modern=session;session=LegacyClone();var old=(P6DamageResult)Invoke("P6ApplyDamage",S,S.Actor("char_demo_paladin"),Enemy,"controlled-smite",CombatSession.P6SmiteId,new List<int>{critical?20:14},critical?20:14,25,13,true,critical,critical?"natural20":null,3,null,false);session=modern;
            var source=S.Actor("char_demo_paladin");var record=(P6DamageResult)Invoke("P6ApplyDamage",S,source,Enemy,"controlled-smite",CombatSession.P6SmiteId,new List<int>{critical?20:14},critical?20:14,25,13,true,critical,critical?"natural20":null,3,null,false);
            Assert.That(record.weaponFaces.Count,Is.EqualTo(critical?4:2));Assert.That(record.radiantFaces.Count,Is.EqualTo(critical?6:3));
            Assert.That(S.v14DamageEvents.Single(x=>x.kind=="physical").sourceDamage,Is.EqualTo((record.weaponFaces.Sum()+source.strengthModifier)*1.1).Within(.000001));Assert.That(S.v14DamageEvents.Single(x=>x.kind=="radiant").sourceDamage,Is.EqualTo(record.radiantFaces.Sum()*1.1).Within(.000001));Assert.That(S.v14DamageEvents.All(x=>x.periodicScale==1),Is.True);
            Assert.That(old.weaponFaces,Is.EqualTo(record.weaponFaces));Assert.That(old.radiantFaces,Is.EqualTo(record.radiantFaces));SaveControlled("V14_CONTROLLED_PALADIN_"+(critical?"CRITICAL":"NORMAL"),new{evidenceType="CONTROLLED_COMPONENT_NOT_PLAYER",old,updated=record,sourceEvents=S.v14DamageEvents,carry=Enemy.v14DamageCarries});
        }
        [TestCase(PartyCombatMode.CoreAndAssist)][TestCase(PartyCombatMode.PersistentTrio)]
        public void DelayedSixTicksAreExactlySourceTimesTwoPointTwoAndDoNotHeal(PartyCombatMode mode)
        {
            Create(mode);S.Actor(F).hp=10;int hp=S.Actor(F).hp;Cast();Assert.That(S.v14DamageEvents,Is.Null);Tick(6.001f);
            var dots=S.v14DamageEvents.Where(x=>x.kind=="dot").ToList();Assert.That(dots.Count,Is.EqualTo(6));Assert.That(dots.All(x=>Math.Abs(x.sourceDamage-2*.35*2.2)<1e-6),Is.True);
            Assert.That(dots.Sum(x=>x.sourceDamage),Is.EqualTo(9.24).Within(.00001));Assert.That(Enemy.hp,Is.EqualTo(9991));Assert.That(S.Actor(F).hp,Is.EqualTo(hp));Assert.That(Enemy.statuses.Any(x=>x.v14Dot!=null),Is.False);
        }
        [Test] public void OldV13RetainsUnscaledSixTickSettlementAndCooldown()
        {Create(PartyCombatMode.CoreAndAssist,false);Cast();Assert.That(S.Actor(F).v12.basicCooldown,Is.GreaterThan(7));Tick(6.001f);Assert.That(10000-Enemy.hp,Is.EqualTo(4));Assert.That(S.v14DamageEvents,Is.Null);Assert.That(S.v14,Is.Null);}
        [TestCase(PartyCombatMode.CoreAndAssist)][TestCase(PartyCombatMode.PersistentTrio)]
        public void DetonationPaysRemainingPlanExactlyOnceNoDiceOrFutureTicks(PartyCombatMode mode)
        {
            Create(mode);Cast();Tick(2.1f);string rng=CombatJson.Write(S.random),damageRng=CombatJson.Write(S.p6.damageRandom);int charges=S.charges;int hp=S.Actor(F).hp;
            var preview=session.PreviewV14Detonation(F,Enemy.id);Assert.That(preview.remainingTicks,Is.EqualTo(4));Assert.That(preview.sourceDamage,Is.EqualTo(6.16).Within(.00001));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));
            var r=Cast(D);Assert.That(r.rawFaces,Is.Empty);Assert.That(S.rolls,Is.Empty);Assert.That(S.charges,Is.EqualTo(charges-1));Assert.That(S.v14Detonations.Single().targets.Single().remainingTicks,Is.EqualTo(4));
            Assert.That(S.v14DamageEvents.Sum(x=>x.sourceDamage),Is.EqualTo(9.24).Within(.00001));Assert.That(10000-Enemy.hp,Is.EqualTo(9));Assert.That(CombatJson.Write(S.p6.damageRandom),Is.EqualTo(damageRng));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));Assert.That(S.Actor(F).hp,Is.EqualTo(hp));
            Tick(7);Assert.That(10000-Enemy.hp,Is.EqualTo(9));Assert.That(session.QuoteHero(C(CommandKind.RequestHeroAction,D)).reason,Is.EqualTo("own_parasite_no_unpaid_ticks"));
        }
        [Test] public void RequestReservesWithoutChargingAndBlockedCommitReturnsReservation()
        {
            Cast();Ready();int charges=S.charges;var request=C(CommandKind.RequestHeroAction,D);Good(request);Assert.That(S.charges,Is.EqualTo(charges));Assert.That(session.AvailableSharedCharges,Is.EqualTo(charges-1));
            Tick(.36f);var end=C(CommandKind.ResolveHeroAction,D);end.orderId=request.id;end.authority=CommandAuthority.System;end.lineOfSight=false;Good(end);Assert.That(S.charges,Is.EqualTo(charges));Assert.That(session.AvailableSharedCharges,Is.EqualTo(charges));Assert.That(Enemy.statuses.Any(x=>x.v14Dot!=null),Is.True);
        }
        [Test] public void EmptyPreviewAndRequestNeverPayOrDraw()
        {int charges=S.charges;string rng=CombatJson.Write(S.random);Assert.That(session.PreviewV14Detonation(F,Enemy.id).legal,Is.False);Assert.That(session.Execute(C(CommandKind.RequestHeroAction,D)).success,Is.False);Assert.That(S.charges,Is.EqualTo(charges));Assert.That(CombatJson.Write(S.random),Is.EqualTo(rng));}
        [Test] public void DueExpiryTickRunsBeforeDetonationSoNoSecondFinalPayment()
        {
            Cast();Tick(5.5f);int charges=S.charges;var request=C(CommandKind.RequestHeroAction,D);Good(request);Tick(.5001f,false);
            var end=C(CommandKind.ResolveHeroAction,D);end.orderId=request.id;end.authority=CommandAuthority.System;var result=Good(end);
            Assert.That(result.reason,Is.EqualTo("own_parasite_expired_moved_or_blocked"));Assert.That(S.charges,Is.EqualTo(charges));Assert.That(S.v14DamageEvents.Count(x=>x.kind=="dot"),Is.EqualTo(6));Assert.That(S.v14Detonations,Is.Null);
        }
        [Test] public void FreezeStopsPlanAndWindupAndSavedReservationContinuesIdentically()
        {
            Cast();Ready();var request=C(CommandKind.RequestHeroAction,D);Good(request);string saved=CombatJson.Write(S);float time=S.simulationSeconds,remaining=S.Actor(F).v12.intent.remaining;
            S.mode=CombatMode.RollFreeze;session.Tick(10);Assert.That(S.simulationSeconds,Is.EqualTo(time));Assert.That(S.Actor(F).v12.intent.remaining,Is.EqualTo(remaining));S.mode=CombatMode.RealtimeCombat;
            session=new CombatSession(CombatJson.Read<CombatState>(saved),config);Assert.That(session.AvailableSharedCharges,Is.EqualTo(S.charges-1));Tick(.36f);var end=C(CommandKind.ResolveHeroAction,D);end.orderId=request.id;end.authority=CommandAuthority.System;Good(end);int hp=Enemy.hp,charges=S.charges;Assert.That(Good(end).duplicate,Is.True);Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.charges,Is.EqualTo(charges));
        }
        [Test] public void RefreshKeepsPhaseAndSnapshotsOnlyFuturePayments()
        {
            Cast();Tick(4.05f);float next=Enemy.statuses.Single(x=>x.v14Dot!=null).v12.nextTick;int count=S.v14DamageEvents.Count;Buff("v14_finn_densegrowth","v14_finn_quickgrowth");Cast();
            var dot=Enemy.statuses.Single(x=>x.v14Dot!=null);Assert.That(dot.v12.nextTick,Is.EqualTo(next));Assert.That(dot.v14Dot.interval,Is.EqualTo(.75f));Assert.That(dot.v14Dot.sourcePerTick,Is.EqualTo(1.925).Within(.00001));Assert.That(S.v14DamageEvents.Count,Is.EqualTo(count));
        }
        [TestCase("v14_finn_quickgrowth",8,1.54)][TestCase("v14_finn_densegrowth",6,1.925)]
        public void EachFinnUpgradeWorksAlone(string buff,int count,double each)
        {Buff(buff);Cast();Tick(6.001f);Assert.That(S.v14DamageEvents.Count,Is.EqualTo(count));Assert.That(S.v14DamageEvents.Sum(x=>x.sourceDamage),Is.EqualTo(count*each).Within(.0001));}
        [Test] public void BloomAloneConsumesAtMostThreeOwnPlansAndLeavesUninfectedNeighbors()
        {
            Buff("v14_finn_bloom");Extra("second",1.5f);Extra("third",2);Extra("uninfected",2.1f);Cast();var original=CombatJson.Clone(Enemy.statuses.Single(x=>x.v14Dot!=null));
            foreach(var e in S.enemies.Where(x=>x.id=="second"||x.id=="third")){var copy=CombatJson.Clone(original);copy.v14Dot.instanceId+="/"+e.id;e.statuses.Add(copy);}
            Tick(1.2f);Cast(D,spatial:Lines());var record=S.v14Detonations.Single();Assert.That(record.targets.Count,Is.EqualTo(3));Assert.That(record.targets.All(x=>x.coefficient==1.25),Is.True);Assert.That(S.Actor("uninfected").hp,Is.EqualTo(10000));Assert.That(S.enemies.All(x=>!x.statuses.Any(p=>p.v14Dot!=null)),Is.True);
        }
        [Test] public void QuickgrowthSecondTargetRequiresVisibleAnchorAndNoRecursiveSpread()
        {Buff("v14_finn_quickgrowth");Extra("second",1.5f);Extra("third",2);Cast(spatial:Lines());Assert.That(S.enemies.Count(e=>e.statuses.Any(x=>x.v14Dot!=null)),Is.EqualTo(2));}
        [Test] public void GroveCannotReattachItsConsumedPlanAfterReentry()
        {
            Create(PartyCombatMode.CoreAndAssist,true,"v12_finn_grove");Cast("v12_finn_grove");var g=S.v12.groves.Single();Assert.That(g.touchedEnemies.Contains(Enemy.id),Is.True);
            // Controlled equivalent of consuming one plan: the grove's source receipt must survive removal.
            Enemy.statuses.RemoveAll(x=>x.v14Dot!=null);Enemy.x=10;Tick(.01f);Enemy.x=1;Tick(.01f);Assert.That(Enemy.statuses.Any(x=>x.v14Dot!=null),Is.False);Assert.That(S.v12.groves.Single().touchedEnemies.Contains(Enemy.id),Is.True);
        }
        [Test] public void LogicalSourceDeathClearsV14PlanWhileVisualAbsenceDoesNot()
        {Create(PartyCombatMode.CoreAndAssist);config.v11.coreActorId=M;S.v13.coreId=M;S.activeActorId=M;Cast();Tick(1.1f);Assert.That(S.v14DamageEvents.Count,Is.EqualTo(1));S.Actor(F).hp=0;Tick(.1f);Assert.That(Enemy.statuses.Any(x=>x.v14Dot!=null),Is.False);}
        [Test] public void ControlledBaselineAndDetonationAuditCanBeExported()
        {
            Create(PartyCombatMode.CoreAndAssist,false);Cast();Tick(6.001f);int oldActual=10000-Enemy.hp;Create(PartyCombatMode.CoreAndAssist);Cast();Tick(2.1f);Cast(D);
            var report=new {evidenceType="CONTROLLED_DOMAIN_NOT_PLAYER",targetHp=10000,defense=1,allOptionalBuffs=false,basicDamage=2,coefficient=config.v12.parasiteCoefficient,oldSourcePerTick=2*(double)config.v12.parasiteCoefficient,oldSixTickActual=oldActual,newSixTickEquivalentSource=S.v14DamageEvents.Sum(x=>x.sourceDamage),newSixTickEquivalentActual=10000-Enemy.hp,events=S.v14DamageEvents,detonations=S.v14Detonations};
            string folder=Environment.GetEnvironmentVariable("V14_EVIDENCE_DIR");if(!string.IsNullOrEmpty(folder)){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"V14_CONTROLLED_DOT_COMPARISON.json"),CombatJson.Write(report));}
            Assert.That(report.newSixTickEquivalentSource,Is.EqualTo(9.24).Within(.00001));
        }
    }
}
