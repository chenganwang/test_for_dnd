using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Explicit rule fixtures, not natural Player/balance evidence. Seeds select test branches only.
    public sealed class V12BuildTests
    {
        const string Evan=CombatSession.V12Evan,Mira=CombatSession.V12Mira,Finn=CombatSession.V12Finn;
        CombatConfig config;CombatSession session;int seq;
        CombatState S=>session.State;ActorState Enemy=>S.enemies[0];
        [SetUp] public void Setup()
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig{build=new V12BuildConfig()},v11=new V11CombatRules{version="v12-character-1",v12BuildVersion="v12-build-1",coreActorId=Evan}};
            foreach(var id in new[]{Evan,Mira,Finn})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==Evan?"v11_evan_insight":id==Mira?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,unlockedActionIds=new List<string>{CombatSession.V12BasicAction(id),CombatSession.V12KeyAction(id)},finnSustain=id==Finn?new V11FinnSustainConfig():null});
            var s=CombatState.CreatePartyForPreset("v12-build-rule","run",41,config);foreach(var a in s.actors){a.x=0;a.z=0;}s.enemies[0].x=1;s.enemies[0].z=0;s.enemies[0].hp=s.enemies[0].maxHp=1000;session=new CombatSession(s,config);seq=0;
        }
        void Buff(string id,string core=null){if(core!=null)config.v11.coreActorId=core;config.v11.buffs.Add(new V11BuffStack{id=id,stacks=1});}
        ActorState Add(string id,float x,float z,int hp=1000)
        {var a=CombatJson.Clone(Enemy);a.id=id;a.x=x;a.z=z;a.hp=a.maxHp=hp;a.statuses.Clear();S.enemies.Add(a);return a;}
        V12CommandData Links()
        {var data=new V12CommandData();foreach(var a in S.actors.Concat(S.enemies))foreach(var b in S.enemies)if(a.id!=b.id)data.links.Add(new V12LineEvidence{fromId=a.id,toId=b.id,lineOfSight=true});return data;}
        CombatCommand C(CommandKind kind,string actor=Evan,string action=null,string target=null)=>new CombatCommand{id="build-test/"+(++seq),kind=kind,actorId=actor,targetId=target??Enemy.id,actionId=action??CombatSession.V12BasicAction(actor),qualified=true,v12=Links()};
        CombatResult Good(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        void Tick(float value){session.Tick(value);Assert.That(session.LastTickError,Is.Null);if(session.V12TickPending){var c=C(CommandKind.ResolveHeroTick);c.authority=CommandAuthority.System;Good(c);}}
        void Seed(bool visible,int face)
        {for(ulong seed=1;seed<10000;seed++){var r=RandomStreams.Create(seed);var stream=visible?r.visibleTactical:r.backgroundCombat;if(RandomStreams.D20(CombatJson.Clone(stream))!=face)continue;if(visible)S.random.visibleTactical=stream;else S.random.backgroundCombat=stream;return;}Assert.Fail("seed");}
        CombatResult Cast(string actor,string action=null,string target=null,bool links=true)
        {var c=C(CommandKind.RequestHeroAction,actor,action,target);Good(c);Tick(S.Actor(actor).v12.intent.remaining+.001f);var finish=C(CommandKind.ResolveHeroAction,actor,action,target);finish.orderId=c.id;finish.authority=CommandAuthority.System;if(!links)finish.v12=null;return Good(finish);}
        CombatResult Incoming(string target,int damage=1,bool links=true)
        {Seed(false,14);var c=C(CommandKind.IncomingAttack,Mira,"attack",target);c.rootId=c.id;c.amount=damage;c.authority=CommandAuthority.System;if(!links)c.v12=null;return Good(c);}
        CombatResult Basic(string actor)
        {Seed(false,14);var start=C(CommandKind.StartBasicAttack,actor);Good(start);Tick(config.basicContactSeconds+.001f);var end=C(CommandKind.ResolveBasicAttack,actor);end.orderId=start.id;end.authority=CommandAuthority.System;return Good(end);}
        CombatResult Paid(string actor,int raw=14,bool links=true)
        {
            if(S.Actor(actor).majorRemaining>0)Tick(S.Actor(actor).majorRemaining+.001f);
            Seed(true,raw);Good(C(CommandKind.EnterFocus,actor));var action=CombatSession.V12KeyAction(actor);Good(C(CommandKind.ConfirmRoll,actor,action));Good(C(CommandKind.RevealRoll,actor));Good(C(CommandKind.Commit,actor));Good(C(CommandKind.ExitFocus,actor));
            var q=S.queue.Last();var start=C(CommandKind.StartQueued,actor,action);start.queueId=q.id;Good(start);Tick(.56f);var finish=C(CommandKind.ResolveQueued,actor,action);finish.queueId=q.id;finish.authority=CommandAuthority.System;if(!links)finish.v12=null;return Good(finish);
        }
        [Test] public void LegacyNullBuildFieldsRemainAbsentFromSerializedContract()
        {config.v12.build=null;config.v11.v12BuildVersion=null;string json=CombatJson.Write(config);Assert.That(json,Does.Not.Contain("v12BuildVersion").And.Not.Contain("\"build\""));Assert.That(CombatJson.Write(S),Does.Not.Contain("capacityBonus").And.Not.Contain("buildHealingRemainder"));Assert.That(new CombatSession(S,config).Save().success,Is.True);}
        [Test] public void OldBuildCannotInjectNewForm()
        {config.v12.build=null;config.v11.v12BuildVersion=null;Buff("v12_evan_conclusion");Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));}
        [Test] public void InvalidBuildVersionAndInvalidNumbersRejectRestore()
        {config.v12.build.version="future";Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));config.v12.build.version="v12-build-1";config.v12.build.sweepRange=float.NaN;Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));}
        [Test] public void CoauthorEnablesOnlyRunPassiveAndHitsOneAdditionalEnemy()
        {Buff("v12_evan_coauthor");config.v11.actors.Find(a=>a.actorId==Evan).passiveUnlocked=false;config.Template(ActorRole.Mage).basic.damage=20;Add("side",1,.4f);Add("side2",1,-.4f);Cast(Evan);Tick(.5f);Incoming(Enemy.id,2);Assert.That(S.v12.events.Count(e=>e.kind=="echo"),Is.EqualTo(1));Assert.That(S.v12.events.Count(e=>e.kind=="splash"),Is.EqualTo(1));Assert.That(config.v11.actors.Find(a=>a.actorId==Evan).passiveUnlocked,Is.False);}
        [Test] public void CoauthorWithoutSpatialEvidenceFailsClosed()
        {Buff("v12_evan_coauthor");config.Template(ActorRole.Mage).basic.damage=20;Add("side",1,.4f);Cast(Evan);Tick(.5f);Incoming(Enemy.id,2,false);Assert.That(S.v12.events.Any(e=>e.kind=="echo"),Is.True);Assert.That(S.v12.events.Any(e=>e.kind=="splash"),Is.False);}
        [Test] public void CoauthorSecondaryUsesRawEchoNotPrimaryMitigatedHp()
        {
            Buff("v12_evan_coauthor");config.Template(ActorRole.Mage).basic.damage=20;Add("side",1,.4f);Cast(Evan);Tick(.5f);var baseState=S.Clone();
            int Run(float resistance){session=new CombatSession(baseState.Clone(),config);Enemy.p6.physicalDamageMultiplier=resistance;Incoming(Enemy.id,2);return S.v12.events.Single(e=>e.kind=="splash").amount;}
            Assert.That(Run(.5f),Is.EqualTo(Run(1)),"Secondary must not inherit primary resistance/HP clipping.");
        }
        [Test] public void CoauthorUpgradeReplacesRatioAndDoesNotAddTargets()
        {Buff("v12_evan_coauthor");Buff("v12_evan_coauthor_up");config.Template(ActorRole.Mage).basic.damage=20;Add("side",1,.4f);Add("side2",1,-.4f);Cast(Evan);Tick(.5f);Incoming(Enemy.id,2);var echo=S.v12.events.Single(e=>e.kind=="echo");var splash=S.v12.events.Single(e=>e.kind=="splash");Assert.That(splash.amount,Is.InRange((int)Math.Floor(echo.amount/1.2*.85)-1,(int)Math.Ceiling(echo.amount/1.2*.85)+1));}
        [TestCase(false,1.30f)][TestCase(true,1.45f)] public void ConclusionUsesOneMultiplierOnlyOnOpening(bool upgrade,float expected)
        {
            Cast(Evan);Tick(.5f);var basis=S.Clone();var baseline=Paid(Evan).damage;session=new CombatSession(basis,config);Buff("v12_evan_conclusion");if(upgrade)Buff("v12_evan_conclusion_up");int improved=Paid(Evan).damage;
            Assert.That(improved,Is.InRange((int)Math.Floor(baseline*expected)-1,(int)Math.Ceiling(baseline*expected)+1));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(2));
        }
        [Test] public void ConclusionDoesNothingWithoutOpening()
        {var basis=S.Clone();int baseline=Paid(Evan).damage;session=new CombatSession(basis,config);Buff("v12_evan_conclusion");Assert.That(Paid(Evan).damage,Is.EqualTo(baseline));}
        [Test] public void SweepUsesRealConeAndTwoTargetMaximumWithoutAnotherD20()
        {Buff("v12_mira_sweep",Mira);Add("one",1.4f,.2f);Add("two",1.4f,-.2f);Add("third",1.7f,0);Add("behind",-1,0);Paid(Mira);Assert.That(S.v12.events.Count(e=>e.kind=="splash"),Is.EqualTo(2));Assert.That(S.Actor("behind").hp,Is.EqualTo(1000));Assert.That(S.Actor("third").hp,Is.EqualTo(1000));Assert.That(S.random.visibleTactical.drawCount,Is.EqualTo(1));}
        [Test] public void SweepMissOrFullImmunityDoesNotCreateSecondaryHit()
        {Buff("v12_mira_sweep",Mira);Add("one",1.4f,.2f);Enemy.p6.physicalDamageMultiplier=0;Paid(Mira);Assert.That(S.v12.events.Any(e=>e.kind=="splash"),Is.False);}
        [Test] public void SweepMissingLineOfSightFailsClosed()
        {Buff("v12_mira_sweep",Mira);Add("one",1.4f,.2f);Paid(Mira,14,false);Assert.That(S.v12.events.Any(e=>e.kind=="splash"),Is.False);}
        [Test] public void SweepUpgradeExtendsRangeAndKeepsTargetCap()
        {Buff("v12_mira_sweep",Mira);Buff("v12_mira_sweep_up");Add("far",2.5f,0);Paid(Mira);Assert.That(S.v12.events.Single(e=>e.kind=="splash").targetId,Is.EqualTo("far"));}
        [Test] public void HuntOnlyActualBreakKillReducesCooldownOnce()
        {Buff("v12_mira_hunt",Mira);Enemy.hp=1;S.Actor(Mira).v12.basicCooldown=6;Paid(Mira);Assert.That(S.Actor(Mira).v12.basicCooldown,Is.EqualTo(2.44f).Within(.02));Assert.That(S.rewardIds.Count(x=>x.StartsWith("v12/hunt/")),Is.EqualTo(1));}
        [Test] public void HuntNonLethalDoesNotReduceCooldown()
        {Buff("v12_mira_hunt",Mira);S.Actor(Mira).v12.basicCooldown=6;Paid(Mira);Assert.That(S.Actor(Mira).v12.basicCooldown,Is.EqualTo(5.44f).Within(.02));}
        [Test] public void HuntUpgradeChangesDomainQuoteAndPhysicalDestinationLimit()
        {Buff("v12_mira_hunt",Mira);Buff("v12_mira_hunt_up");Enemy.x=7;Assert.That(session.V12ChaseRange(Mira),Is.EqualTo(5.5f));Assert.That(session.Quote(Mira,Enemy.id,"v12_mira_chase").legal,Is.True);var c=C(CommandKind.RequestHeroAction,Mira);c.x=5.3f;Good(c);Assert.That(S.Actor(Mira).x,Is.Zero,"Authorization cannot teleport the actor.");}
        [Test] public void SpreadKeepsRootExpiryScheduleAndStopsAtOneGeneration()
        {
            Buff("v12_finn_spread",Finn);Add("one",1.4f,.1f);Add("two",1.4f,-.1f);Add("third",1.8f,0);Cast(Finn);Tick(4.1f);var original=CombatJson.Clone(Enemy.statuses.Single(x=>x.v12?.family=="parasite"));Enemy.hp=1;Incoming(Enemy.id,20);
            var one=S.Actor("one").statuses.Single(x=>x.v12?.family=="parasite").v12;Assert.That(one.rootId,Is.EqualTo(original.v12.rootId));Assert.That(one.expiresAt,Is.EqualTo(original.v12.expiresAt));Assert.That(one.nextTick,Is.EqualTo(original.v12.nextTick));Assert.That(one.generation,Is.EqualTo(1));Assert.That(S.Actor("third").statuses.Any(x=>x.v12?.family=="parasite"),Is.False);S.Actor("one").hp=1;Incoming("one",20);Assert.That(S.Actor("third").statuses.Any(x=>x.v12?.family=="parasite"),Is.False);Tick(2);Assert.That(S.Actor("two").statuses.Any(x=>x.v12?.family=="parasite"),Is.False);
        }
        [Test] public void SpreadSkipsAlreadyInfectedTargetsAndMissingLinks()
        {Buff("v12_finn_spread",Finn);Add("one",1.4f,.1f);Cast(Finn);Enemy.hp=1;Incoming(Enemy.id,20,false);Assert.That(S.Actor("one").statuses,Is.Empty);}
        [Test] public void SpreadUpgradeReachesFourPointFiveMeters()
        {Buff("v12_finn_spread",Finn);Buff("v12_finn_spread_up");Add("far",5,0);Cast(Finn);Enemy.hp=1;Incoming(Enemy.id,20);Assert.That(S.Actor("far").statuses.Single().v12.generation,Is.EqualTo(1));}
        [Test] public void DotLethalSpreadsThroughSameAuthorityWithoutRecursing()
        {Buff("v12_finn_spread",Finn);config.Template(ActorRole.Mage).basic.damage=10;Add("one",1.4f,.1f);Cast(Finn);Enemy.hp=1;Tick(1.01f);Assert.That(Enemy.hp,Is.Zero);Assert.That(S.Actor("one").statuses.Single().v12.generation,Is.EqualTo(1));Assert.That(S.Actor(Finn).v11.finnSustain,Is.Null);}
        [Test] public void BloomOnlyHitsPreviouslyInfectedAndKeepsDotClock()
        {Buff("v12_finn_bloom",Finn);config.Template(ActorRole.Mage).basic.damage=10;Add("new",1.2f,.2f);Cast(Finn);Tick(.5f);var p=Enemy.statuses.Single().v12;float next=p.nextTick,expiry=p.expiresAt;Cast(Finn,"v12_finn_grove");Assert.That(S.v12.events.Count(e=>e.kind=="bloom"),Is.EqualTo(1));Assert.That(S.Actor("new").hp,Is.EqualTo(1000));Assert.That(Enemy.statuses.Single(x=>x.v12?.family=="parasite").v12.nextTick,Is.EqualTo(next+1).Within(.001));Assert.That(S.v12.events.Count(e=>e.actionId=="v12_finn_grove"&&e.kind=="grove"),Is.EqualTo(1));}
        [Test] public void BloomUpgradeHealsOneLowestLivingAllyAfterActualDamage()
        {Buff("v12_finn_bloom",Finn);Buff("v12_finn_bloom_up");config.Template(ActorRole.Mage).basic.damage=10;foreach(var a in S.actors){a.maxHp=100;a.hp=50;}S.Actor(Mira).hp=10;S.Actor(Evan).hp=0;Cast(Finn);Tick(.5f);Cast(Finn,"v12_finn_grove");Assert.That(S.Actor(Mira).hp,Is.EqualTo(12));Assert.That(S.Actor(Finn).hp,Is.EqualTo(50));Assert.That(S.Actor(Evan).hp,Is.Zero);Assert.That(S.v12.events.Count(e=>e.actionId=="v12_finn_bloom_up"&&e.kind=="heal"),Is.EqualTo(1));}
        [Test] public void BloomImmunityCannotHealOrAwardResources()
        {Buff("v12_finn_bloom",Finn);Buff("v12_finn_bloom_up");S.Actor(Finn).hp=5;Enemy.p6.physicalDamageMultiplier=0;Cast(Finn);Tick(.5f);Cast(Finn,"v12_finn_grove");Assert.That(S.Actor(Finn).hp,Is.EqualTo(5));Assert.That(S.v12.events.Any(e=>e.kind=="heal"),Is.False);}
        [Test] public void SteadfastUsesExistingShieldAndSameEncounterCannotRefresh()
        {Buff("v12_team_steadfast");CombatSession.InitializeV12BuildEncounter(S,config,"encounter-A");Assert.That(S.Actor(Evan).p6.absorptionShield,Is.EqualTo(1));Tick(2);CombatSession.InitializeV12BuildEncounter(S,config,"encounter-A");Assert.That(S.Actor(Evan).v11.shieldRemaining,Is.EqualTo(4));Tick(4.1f);Assert.That(S.Actor(Evan).p6.absorptionShield,Is.Zero);CombatSession.InitializeV12BuildEncounter(S,config,"encounter-A");Assert.That(S.Actor(Evan).p6.absorptionShield,Is.Zero);}
        [Test] public void LightfootChangesCooldownButNotInvulnerability()
        {Buff("v12_team_lightfoot");Good(C(CommandKind.StartDodge,Mira));Assert.That(S.Actor(Mira).dodgeCooldown,Is.EqualTo(config.dodgeRecovery*.85f).Within(.0001));Assert.That(S.Actor(Mira).dodgeRemaining,Is.EqualTo(config.dodgeSeconds));}
        [Test] public void LeewayCapacityRecoveryAndRepeatUseSameP6Ledger()
        {Buff("v12_team_leeway");CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");Assert.That(S.focusBudget.maximumSeconds,Is.EqualTo(135));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(105));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(15));CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(15));Assert.That(session.Save().success,Is.True);}
        [Test] public void FullFocusLeewayCannotBankLaterClaim()
        {Buff("v12_team_leeway");S.focusBudget.secondsRemaining=120;S.focusBudget.secondsRecovered=30;S.focusBudget.p6.rewards.Add(new P6FocusRewardReceipt{id="fixture",encounterId="run",category="echo",offered=30,credited=30});CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(135));S.focusBudget.secondsRemaining-=10;S.focusBudget.secondsSpent+=10;CombatSession.ApplyV12BuildReward(S,config,"reward-b","v12_team_leeway");Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(125));Assert.That(session.Save().success,Is.True);}
        [Test] public void LeewayCannotExceedOriginalGlobalSixtyCredit()
        {Buff("v12_team_leeway");S.focusBudget.secondsRecovered=60;S.focusBudget.p6.rewards.Add(new P6FocusRewardReceipt{id="fixture",encounterId="run",category="echo",offered=60,credited=60});CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(90));Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(60));Assert.That(S.focusBudget.secondsDiscarded,Is.EqualTo(15));}
        [Test] public void EdgeOnlyFirstPaidMajorKillAndNoHiddenFullCapMomentum()
        {Buff("v12_team_edge");Enemy.hp=1;Add("other",2,0);Paid(Evan);Assert.That(S.p6.resourceEvents.Count(e=>e.kind=="v12-edge"),Is.EqualTo(1));Assert.That(S.p6.resourceEvents.Single(e=>e.kind=="v12-edge").qualifiedAmount,Is.EqualTo(25));}
        [Test] public void BasicAndDotKillsCannotTriggerEdge()
        {Buff("v12_team_edge");Enemy.hp=1;Incoming(Enemy.id,20);Assert.That(S.p6.resourceEvents.Any(e=>e.kind=="v12-edge"),Is.False);}
        [Test] public void LifeAddsFractionWithoutCreatingNewHealingOrReviving()
        {Buff("v12_team_life");var a=S.Actor(Finn);a.hp=10;for(int i=0;i<20;i++)CombatSession.V12HealExisting(S,config,a,.1);Assert.That(a.hp,Is.EqualTo(12));Assert.That(a.v12.buildHealingRemainder,Is.EqualTo(.3).Within(.00001));a.hp=0;Assert.That(CombatSession.V12HealExisting(S,config,a,100),Is.Zero);a.hp=a.maxHp;CombatSession.V12HealExisting(S,config,a,100);Assert.That(a.v12.buildHealingRemainder,Is.Zero);}
        [Test] public void LifeAffectsFinnActualBasicFractionOnly()
        {Buff("v12_team_life");S.Actor(Finn).hp=10;Basic(Finn);Assert.That(S.Actor(Finn).hp,Is.EqualTo(10));Assert.That(S.Actor(Finn).v11.finnSustain.fractionalHp,Is.EqualTo(22*.005*1.15).Within(.000001));}
        [Test] public void PathfinderCreditsOnlyTwoDistinctCompletionsAndSameGlobalLedger()
        {Buff("v12_team_pathfinder");S.inCombat=false;S.mode=CombatMode.RealtimeCombat;Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"read"),Is.EqualTo(2));Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"read"),Is.Zero);Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"cache"),Is.EqualTo(2));Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"search"),Is.Zero);Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(4));}
        [Test] public void PathfinderIsUnavailableDuringCombatOrRollFreeze()
        {Buff("v12_team_pathfinder");Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"read"),Is.Zero);S.inCombat=false;S.mode=CombatMode.RollFreeze;Assert.That(CombatSession.ApplyV12ExplorationReward(S,config,"read"),Is.Zero);}
        [Test] public void DuplicatePaidSweepCannotDamageOrRefundAgain()
        {
            Buff("v12_mira_sweep",Mira);Buff("v12_team_edge");Enemy.hp=1;Add("one",1.4f,.2f,1);Add("two",1.4f,-.2f,1);var result=Paid(Mira);var receipt=S.receipts.Last();
            Assert.That(S.p6.resourceEvents.Count(e=>e.kind=="v12-edge"),Is.EqualTo(1));Assert.That(S.rewardIds.Count(x=>x.StartsWith("v12/edge/")),Is.EqualTo(1));
            // Canonical command reconstruction from this fixture's final command number.
            var c=new CombatCommand{id=receipt.id,kind=CommandKind.ResolveQueued,actorId=Mira,targetId=Enemy.id,actionId="v12_mira_break",queueId=result.queueId,qualified=true,authority=CommandAuthority.System,v12=Links()};
            string before=CombatJson.Write(S);Assert.That(Good(c).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(before));
        }
        [Test] public void MissedSweepDoesNotCreateSplash()
        {Buff("v12_mira_sweep",Mira);Add("one",1.4f,.2f);Assert.That(Paid(Mira,1).hit,Is.False);Assert.That(S.v12.events.Any(e=>e.kind=="splash"),Is.False);}
        [Test] public void SpreadDoesNotStackExistingSourceOrRefreshItsClock()
        {
            Buff("v12_finn_spread",Finn);Add("already",1.2f,.1f);Add("fresh",1.4f,.1f);Cast(Finn);var saved=CombatJson.Clone(Enemy.statuses.Single());saved.v12.rootId="existing-other-root";S.Actor("already").statuses.Add(saved);Enemy.hp=1;Incoming(Enemy.id,20);
            Assert.That(S.Actor("already").statuses.Count,Is.EqualTo(1));Assert.That(S.Actor("already").statuses.Single().v12.rootId,Is.EqualTo("existing-other-root"));Assert.That(S.Actor("fresh").statuses.Single().v12.generation,Is.EqualTo(1));
        }
        [Test] public void DeadSpreadOwnerDoesNotPropagateLateDot()
        {Buff("v12_finn_spread",Finn);config.Template(ActorRole.Mage).basic.damage=10;Add("one",1.4f,.1f);Cast(Finn);S.Actor(Finn).hp=0;Enemy.hp=1;Tick(1.01f);Assert.That(Enemy.hp,Is.Zero);Assert.That(S.Actor("one").statuses,Is.Empty);}
        [Test] public void NonCoreCannotEquipOtherHeroFormAndSwitchingActiveKeepsCore()
        {Buff("v12_evan_conclusion");S.activeActorId=Mira;Assert.That(session.Save().success,Is.True);Assert.That(config.v11.coreActorId,Is.EqualTo(Evan));Buff("v12_mira_hunt");Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));}
        [Test] public void FormUpgradeRequiresItsOriginalForm()
        {Buff("v12_evan_conclusion_up");Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));}
        [Test] public void LegacyCannotRestoreNewFocusReceiptEvenWithZeroCapacityBonus()
        {config.v12.build=null;config.v11.v12BuildVersion=null;S.focusBudget.p6.rewards.Add(new P6FocusRewardReceipt{id="injected",encounterId="run",category="v12-exploration",offered=2,credited=0});Assert.Throws<InvalidOperationException>(()=>new CombatSession(S,config));}
        [Test] public void LifeGroveKeepsFractionAcrossTicksInsteadOfCeilingEveryTick()
        {Buff("v12_team_life");S.Actor(Finn).hp=10;Cast(Finn,"v12_finn_grove");Tick(6.1f);Assert.That(S.Actor(Finn).hp,Is.EqualTo(11));Assert.That(S.Actor(Finn).v12.groveHealingRemainder,Is.EqualTo(.518).Within(.00001));}
        [Test] public void TwoSecondRollFreezeDoesNotAdvanceSpreadOrGroveHealing()
        {Buff("v12_finn_bloom",Finn);Cast(Finn);Tick(.5f);Cast(Finn,"v12_finn_grove");Good(C(CommandKind.EnterFocus,Mira));Good(C(CommandKind.ConfirmRoll,Mira,"v12_mira_break"));string before=CombatJson.Write(new{S.actors,S.enemies,S.v12,S.focusBudget});Tick(2);Assert.That(CombatJson.Write(new{S.actors,S.enemies,S.v12,S.focusBudget}),Is.EqualTo(before));}
        [Test] public void BuildStateRoundtripPreservesShieldCapacityAndHealingFraction()
        {Buff("v12_team_steadfast");Buff("v12_team_leeway");Buff("v12_team_life");CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");CombatSession.InitializeV12BuildEncounter(S,config,"encounter-A");S.Actor(Finn).hp=10;CombatSession.V12HealExisting(S,config,S.Actor(Finn),.1);string json=CombatJson.Write(S);session=new CombatSession(CombatJson.Read<CombatState>(json),CombatJson.Clone(config));Assert.That(CombatJson.Write(S),Is.EqualTo(json));CombatSession.ApplyV12BuildReward(S,config,"reward-a","v12_team_leeway");Assert.That(S.focusBudget.secondsRecovered,Is.EqualTo(15));}
        [Test] public void TwentyFiveSmallBloomHealsNeverCreateNegativeRoundoffRemainder()
        {Buff("v12_finn_bloom",Finn);Buff("v12_finn_bloom_up");var a=S.Actor(Finn);a.hp=1;for(int i=0;i<25;i++){CombatSession.V12HealExisting(S,config,a,a.maxHp*config.v12.build.bloomHealFraction);Assert.That(a.v12.buildHealingRemainder,Is.InRange(0,1));Assert.That(session.Save().success,Is.True);}Assert.That(a.hp,Is.EqualTo(12));Assert.That(a.v12.buildHealingRemainder,Is.Zero);}
        [TestCase(.4399999976158142)]
        [TestCase(.4399999901652336)]
        public void FloatProductPrecisionDoesNotChangeTwentyFiveBloomHeals(double nominal)
        {var a=S.Actor(Finn);a.hp=1;for(int i=0;i<25;i++)CombatSession.V12HealExisting(S,config,a,nominal);Assert.That(a.hp,Is.EqualTo(12));Assert.That(a.v12.buildHealingRemainder,Is.Zero);Assert.That(session.Save().success,Is.True);}
        [Test] public void FractionalHealingDoesNotRoundEachSmallTriggerUpToOneHp()
        {var a=S.Actor(Finn);a.hp=1;for(int i=0;i<99;i++)Assert.That(CombatSession.V12HealExisting(S,config,a,.01),Is.Zero);Assert.That(a.hp,Is.EqualTo(1));Assert.That(a.v12.buildHealingRemainder,Is.EqualTo(.99));Assert.That(CombatSession.V12HealExisting(S,config,a,.01),Is.EqualTo(1));Assert.That(a.hp,Is.EqualTo(2));Assert.That(a.v12.buildHealingRemainder,Is.Zero);}
    }
}
