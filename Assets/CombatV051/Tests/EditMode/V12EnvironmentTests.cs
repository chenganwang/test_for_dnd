using System;
using System.Linq;
using System.Collections.Generic;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed class V12EnvironmentTests
    {
        const string Evan=CombatSession.V12Evan,Mira=CombatSession.V12Mira,Finn=CombatSession.V12Finn;
        CombatConfig config;CombatSession session;int sequence;
        CombatState S=>session.State;ActorState Enemy=>S.enemies[0];V12BeamConfig B=>config.v12.beam;
        [SetUp] public void Setup()
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig{beam=new V12BeamConfig()},v11=new V11CombatRules{version="v12-character-1",coreActorId=Mira}};
            foreach(var id in new[]{Evan,Mira,Finn})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==Evan?"v11_evan_insight":id==Mira?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,unlockedActionIds=new List<string>{CombatSession.V12BasicAction(id),CombatSession.V12KeyAction(id)},finnSustain=id==Finn?new V11FinnSustainConfig():null});
            var s=CombatState.CreatePartyForPreset("beam-rules","run",1337,config);foreach(var a in s.actors){a.x=B.triggerX;a.z=B.triggerZ;a.hp=a.maxHp=100;}s.enemies[0].x=B.x;s.enemies[0].z=B.z;s.enemies[0].hp=s.enemies[0].maxHp=100;CombatSession.RegisterV12Beam(s,config,"run/V12_R4/beam");session=new CombatSession(s,config);sequence=0;
        }
        CombatCommand C(CommandKind kind)=>new CombatCommand{id="beam-test/"+(++sequence),kind=kind,actorId=Evan,targetId=null,qualified=true,authority=kind==CommandKind.ResolveV12Beam?CommandAuthority.System:CommandAuthority.Player};
        CombatResult Good(CombatCommand c){var result=session.Execute(c);Assert.That(result.success,Is.True,result.reason);return result;}
        void Begin()=>Good(C(CommandKind.RequestV12Beam));
        CombatResult Finish(){session.Tick(.81f);return Good(C(CommandKind.ResolveV12Beam));}
        [Test] public void WarningDoesNotApplyDamageEarlyAndOnlyCostsTheDevice()
        {int charges=S.charges;Begin();Assert.That(S.v12.beam.stage,Is.EqualTo("Warning"));Assert.That(Enemy.hp,Is.EqualTo(100));Assert.That(S.charges,Is.EqualTo(charges));session.Tick(.79f);Assert.That(session.Execute(C(CommandKind.ResolveV12Beam)).success,Is.False);Assert.That(Enemy.hp,Is.EqualTo(100));session.Tick(.02f);Assert.That(Good(C(CommandKind.ResolveV12Beam)).damage,Is.EqualTo(B.damage));}
        [Test] public void FixedAreaHasFriendlyFireAndUsesActualPositionsAtImpact()
        {Begin();S.Actor(Mira).x=B.x;S.Actor(Mira).z=B.z;S.Actor(Finn).x=B.x+B.radius+.05f;S.Actor(Finn).z=B.z;var result=Finish();Assert.That(Enemy.hp,Is.EqualTo(88));Assert.That(S.Actor(Mira).hp,Is.EqualTo(88));Assert.That(S.Actor(Finn).hp,Is.EqualTo(100));Assert.That(result.v12.effects.Count(e=>e.kind=="environment"),Is.EqualTo(2));}
        [Test] public void LeavingTheAreaCanMakeBeamMissAndStillConsumesIt()
        {Begin();Enemy.x=B.x+20;Assert.That(Finish().damage,Is.Zero);Assert.That(S.v12.beam.stage,Is.EqualTo("Resolved"));Assert.That(session.Execute(C(CommandKind.RequestV12Beam)).success,Is.False);}
        [Test] public void ReplayingRequestAndResolveCannotRepeatAnyCostOrDamage()
        {var request=C(CommandKind.RequestV12Beam);Good(request);Assert.That(Good(request).duplicate,Is.True);session.Tick(.81f);var resolve=C(CommandKind.ResolveV12Beam);Good(resolve);int hp=Enemy.hp,events=S.v12.events.Count;Assert.That(Good(resolve).duplicate,Is.True);Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.v12.events.Count,Is.EqualTo(events));Assert.That(session.Execute(C(CommandKind.ResolveV12Beam)).success,Is.False);}
        [Test] public void SafeWorldSpentFlagSurvivesRegistrationInAnotherEncounter()
        {Begin();Finish();CombatSession.ResetV12EncounterState(S);CombatSession.RegisterV12Beam(S,config,"run/V12_R4/beam");Assert.That(S.v12.beam.stage,Is.EqualTo("Resolved"));Assert.That(session.Execute(C(CommandKind.RequestV12Beam)).success,Is.False);}
        [Test] public void RegistrationCannotReplaceAnExistingInstance()
        {Assert.Throws<InvalidOperationException>(()=>CombatSession.RegisterV12Beam(S,config,"run/other-beam"));Assert.That(S.v12.beam.id,Is.EqualTo("run/V12_R4/beam"));}
        [TestCase("distance")][TestCase("sight")][TestCase("evidence")][TestCase("AI")]
        public void IllegalTriggerDoesNotConsume(string why)
        {var c=C(CommandKind.RequestV12Beam);if(why=="distance")S.Actor(Evan).x-=20;if(why=="sight")c.lineOfSight=false;if(why=="evidence")c.qualified=false;if(why=="AI")c.authority=CommandAuthority.AllyAI;Assert.That(session.Execute(c).success,Is.False);Assert.That(S.v12.beam.stage,Is.EqualTo("Ready"));}
        [Test] public void FreezeAndPauseStopTheWarningClock()
        {
            Begin();S.Actor(Mira).x=B.x-1;S.Actor(Mira).z=B.z;
            Good(C(CommandKind.EnterFocus));var roll=C(CommandKind.ConfirmRoll);roll.actorId=Mira;roll.targetId=Enemy.id;roll.actionId="v12_mira_break";Good(roll);float before=session.V12BeamRemaining;session.Tick(2);Assert.That(session.V12BeamRemaining,Is.EqualTo(before));Assert.That(session.Execute(C(CommandKind.ResolveV12Beam)).success,Is.False);Good(C(CommandKind.Pause));session.Tick(2);Assert.That(session.V12BeamRemaining,Is.EqualTo(before));
        }
        [Test] public void FocusOnlySlowsWarningUsingSimulationSeconds()
        {Begin();Good(C(CommandKind.EnterFocus));session.Tick(1);Assert.That(session.V12BeamRemaining,Is.EqualTo(.68f).Within(.0001f));Assert.That(Enemy.hp,Is.EqualTo(100));}
        [Test] public void ADownedActivatorAndEntireDownedPartyDoNotLeaveWarningStuck()
        {Begin();foreach(var a in S.actors)a.hp=0;var result=Finish();Assert.That(result.success,Is.True);Assert.That(session.V12BeamPending,Is.False);Assert.That(Enemy.hp,Is.EqualTo(88));}
        [TestCase(false)][TestCase(true)] public void AuthorityRejectsEncounterEndUntilTheArmedHazardSettles(bool partyDefeated)
        {Begin();foreach(var actor in partyDefeated?S.actors:S.enemies)actor.hp=0;var rejected=session.Execute(C(CommandKind.EndEncounter));Assert.That(rejected.success,Is.False);Assert.That(rejected.reason,Is.EqualTo("finish_environment_first"));Assert.That(S.inCombat,Is.True);Assert.That(S.v12.beam.stage,Is.EqualTo("Warning"));Finish();Good(C(CommandKind.EndEncounter));Assert.That(S.encounterResolved,Is.True);Assert.That(S.world.strings.ContainsKey("v12/beamSpent/run/V12_R4/beam"),Is.True);}
        [Test] public void EnvironmentNeverTriggersFinnBasicHealingOrEvanAnnotation()
        {S.Actor(Finn).hp=50;Enemy.statuses.Add(new StatusState{id="v12-opening",sourceId=Evan,remaining=6,v12=new V12StatusData{family="opening",rootId="rule-mark",expiresAt=6,multiplier=1.2f}});var request=C(CommandKind.RequestV12Beam);request.actorId=Finn;Good(request);Finish();Assert.That(S.Actor(Finn).hp,Is.EqualTo(50));Assert.That(S.Actor(Finn).v11.finnSustain,Is.Null);Assert.That(S.v12.events.Any(e=>e.kind=="echo"),Is.False);}
        [Test] public void BeamKillUsesExistingHighThreatLedgerOnce()
        {S.momentumLedger.registeredHighThreats.Add(Enemy.id);Enemy.hp=5;Begin();Finish();Assert.That(S.momentumLedger.resolvedThreats.Count(x=>x==Enemy.id),Is.EqualTo(1));Assert.That(S.v12.events.Count(e=>e.kind=="environment"),Is.EqualTo(1));}
        [Test] public void WarningRestoreKeepsSameInstanceDeadlineAndUsedFlag()
        {Begin();session.Tick(.3f);Assert.That(session.V12BeamRemaining,Is.EqualTo(.5f).Within(.0001f),"before serialization");string before=CombatJson.Write(S);session=new CombatSession(CombatJson.Read<CombatState>(before),CombatJson.Clone(config));Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(session.V12Enabled,Is.True,"restored rules");Assert.That(S.v12.beam,Is.Not.Null,"restored beam");Assert.That(S.v12.beam.stage,Is.EqualTo("Warning"),"restored stage");Assert.That(session.V12BeamRemaining,Is.EqualTo(.5f).Within(.0001f));session.Tick(.51f);Good(C(CommandKind.ResolveV12Beam));Assert.That(Enemy.hp,Is.EqualTo(88));}
        [Test] public void OldAConfigurationOmitsNullableEnvironmentFields()
        {var old=new V12CombatConfig();Assert.That(CombatJson.Write(old),Does.Not.Contain("beam"));var state=new V12CombatState();Assert.That(CombatJson.Write(state),Does.Not.Contain("beam"));}
        [TestCase("nan")][TestCase("clock")][TestCase("receipt")]
        public void CorruptWarningIsRejectedWithoutMutatingOriginal(string kind)
        {Begin();var state=S.Clone();if(kind=="nan")state.v12.beam.resolveAt=float.NaN;if(kind=="clock")state.v12.beam.resolveAt=-1;if(kind=="receipt")state.world.strings.Remove("v12/beamSpent/"+state.v12.beam.id);Assert.Throws<InvalidOperationException>(()=>new CombatSession(state,config));Assert.That(S.v12.beam.stage,Is.EqualTo("Warning"));}
    }
}
