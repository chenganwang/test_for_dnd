using System;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    public sealed partial class V13RuleTests
    {
        [Test] public void DueFateDrainsExistingActionWithoutAllowingInputSpamToStarveIt()
        {
            var start=Cmd(CommandKind.RequestHeroAction,E,"v12_evan_mark");Good(start);
            S.v13.fate=new V13FateEvent{id="pending-entry",encounterId=S.encounterId,trigger="V12_R4",warning=0};
            Assert.That(game.Execute(Cmd(CommandKind.RequestHeroAction,F,"v12_finn_seed")).reason,Is.EqualTo("fate_waiting_current_action"));
            game.Tick(1);var resolve=Cmd(CommandKind.ResolveHeroAction,E,"v12_evan_mark");resolve.orderId=start.id;resolve.authority=CommandAuthority.System;Good(resolve);
            var begin=Cmd(CommandKind.V13FateStart);begin.authority=CommandAuthority.System;Good(begin);
            Assert.That(S.v13.fate.stage,Is.EqualTo("Rolled"));Assert.That(Enemy.statuses.Any(x=>x.sourceId==E&&x.v12?.family=="opening"),Is.True);
        }
        [Test] public void UnknownTreeAndInvalidTerrainFailWithoutReinterpretingSavedState()
        {
            var rules=CombatJson.Clone(config);rules.v13.finnTree="future-tree";
            Assert.Throws<InvalidOperationException>(()=>new CombatSession(S.Clone(),rules));
            var state=S.Clone();state.v13.terrain.Add(new V13Terrain{id="unknown",kind="future-terrain"});
            Assert.Throws<InvalidOperationException>(()=>new CombatSession(state,config));
            Assert.That(S.v13.terrain,Is.Empty);Assert.That(config.v13.finnTree,Is.EqualTo("growth"));
        }
        [Test] public void FourthFateCannotStartAndCannotSpendOrAdvanceEitherRng()
        {
            S.v13.fateCount=3;S.v13.fate=new V13FateEvent{id="fourth",encounterId=S.encounterId,trigger="time",warning=0};
            string before=CombatJson.Write(new{S.random,S.v13,S.charges});
            var start=Cmd(CommandKind.V13FateStart);start.authority=CommandAuthority.System;
            Assert.That(game.Execute(start).reason,Is.EqualTo("fate_not_ready"));
            Assert.That(CombatJson.Write(new{S.random,S.v13,S.charges}),Is.EqualTo(before));
        }
        [Test] public void FreeChaseConsumesMarkAndOwnModifiersButNeverCreatesPaidEcho()
        {
            Cast(E,"v12_evan_mark");game.Tick(2);
            var c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.targetId=M;Good(c);
            S.Actor(M).statuses.Add(new StatusState{id="v13-inspiration",remaining=12});
            Cast(M,"v12_mira_chase");
            Assert.That(Enemy.statuses.Single(x=>x.v12?.family=="opening").stacks,Is.Zero);
            Assert.That(game.Member(S,E).correctionUntil,Is.Zero);
            Assert.That(S.Actor(M).statuses.Any(x=>x.id=="v13-inspiration"),Is.False);
            Assert.That(S.v13.echo,Is.Null);
        }
        [Test] public void PreauthorizedCorrectionAndInspirationConsumeOnActualSaveOnce()
        {
            var c=Cmd(CommandKind.RequestHeroAction,E,"v13_evan_correct");c.targetId=M;Good(c);
            S.Actor(M).statuses.Add(new StatusState{id="v13-inspiration",remaining=12});
            var r=game.ResolveIncoming("own-save",Enemy.id,M,2,true,13);
            Assert.That(r.success,Is.True,r.reason);Assert.That(r.total,Is.EqualTo(r.rawFaces[0]+S.Actor(M).dexSaveBonus+4));
            Assert.That(game.Member(S,E).correctionUntil,Is.Zero);Assert.That(S.Actor(M).statuses.Any(x=>x.id=="v13-inspiration"),Is.False);
        }
        [Test] public void FateShieldExpiryPreservesOtherShieldPoolAndCannotPersistIntoNextRoom()
        {
            PrepareFate(35);Good(Cmd(CommandKind.V13FateAccept));var a=S.Actor(M);int fate=a.p6.v13FateShield;
            a.v11.shieldGranted=3;a.v11.shieldRemaining=20;a.p6.absorptionShield+=3;
            game.Tick(8.1f);Assert.That(S.Actor(M).p6.v13FateShield,Is.Zero);Assert.That(S.Actor(M).p6.absorptionShield,Is.EqualTo(3));
            CombatSession.V13ClearEncounter(S);Assert.That(S.Actor(M).p6.absorptionShield,Is.EqualTo(3));Assert.That(fate,Is.GreaterThan(0));
        }
        [Test] public void FateTimeWarningCannotFireBefore120EffectiveSecondsOnLargeStep()
        {
            S.v13.combatSinceFate=114;Enemy.aiState=1;Enemy.aiRemaining=100;
            game.Tick(3);Assert.That(S.v13.fate,Is.Not.Null);Assert.That(S.v13.fate.warning,Is.EqualTo(3).Within(.001));
            game.Tick(2);Assert.That(S.v13.fate.warning,Is.EqualTo(1).Within(.001));
            var start=Cmd(CommandKind.V13FateStart);start.authority=CommandAuthority.System;Assert.That(game.Execute(start).success,Is.False);
            game.Tick(1);Assert.That(S.v13.fate.warning,Is.Zero);Assert.That(game.Execute(start).reason,Is.EqualTo("fate_threat_in_flight"));
        }
        [Test] public void FateDoesNotConsumeVisibleOrDamageRngAcrossDiskRoundTrip()
        {
            PrepareFate(100);string rng=CombatJson.Write(new{S.random,S.p6.damageRandom});int draw=S.v13.fate.original;
            game=new CombatSession(CombatJson.Read<CombatState>(CombatJson.Write(S)),CombatJson.Clone(config));
            Good(Cmd(CommandKind.V13FateAccept));Assert.That(S.v13.fate.final,Is.EqualTo(draw));Assert.That(CombatJson.Write(new{S.random,S.p6.damageRandom}),Is.EqualTo(rng));
        }
        [Test] public void PrayerRefundReturnsOnlyActuallySpentChargeOnce()
        {
            Make("guard",M);S.Actor(M).hp=1;Cast(F,"v13_finn_prayer",M);game.Tick(2);Assert.That(game.Member(S,F).prayers,Is.EqualTo(1));
            PrepareFate(83);Good(Cmd(CommandKind.V13FateAccept));Assert.That(game.Member(S,F).prayers,Is.EqualTo(2));Assert.That(game.Member(S,F).refunded.Count,Is.EqualTo(1));
            var effects=Cmd(CommandKind.V13FateEffects);effects.authority=CommandAuthority.System;Good(effects);PrepareFate(83);Good(Cmd(CommandKind.V13FateAccept));Assert.That(game.Member(S,F).prayers,Is.EqualTo(2));Assert.That(game.Member(S,F).refunded.Count,Is.EqualTo(1));
        }
        [Test] public void TimeScaleAndPauseCannotAccelerateFateClock()
        {
            Enemy.aiState=1;Enemy.aiRemaining=100;Good(Cmd(CommandKind.EnterFocus));game.Tick(2);Assert.That(S.v13.combatSinceFate,Is.LessThan(.5f));
            Good(Cmd(CommandKind.Pause));float before=S.v13.combatSinceFate;game.Tick(30);Assert.That(S.v13.combatSinceFate,Is.EqualTo(before));
        }
    }
}
