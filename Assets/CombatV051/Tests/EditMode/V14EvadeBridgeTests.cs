using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Explicit world-evidence fixtures: not natural gameplay or human feedback.
    public sealed class V14EvadeBridgeTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira,F=CombatSession.V12Finn,Root="registered-warning";
        CombatConfig config;CombatSession game;CombatState S=>game.State;ActorState Mira=>S.Actor(M);ActorState Enemy=>S.enemies[0];
        void Make(PartyCombatMode mode=PartyCombatMode.CoreAndAssist,string core=M)
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v12=new V12CombatConfig{build=new V12BuildConfig()},v13=new V13Rules(),v14=new V14Rules{partyCombatMode=mode},
                v11=new V11CombatRules{version="v12-character-1",v12BuildVersion="v12-build-1",v14BuildVersion=V14BuildCatalog.Version,v13FinnTree="growth",v14FinnGrowthAction="v14_finn_detonate",coreActorId=core}};
            foreach(string id in new[]{E,M,F})config.v11.actors.Add(new V11ActorLoadout{actorId=id,passiveId=id==E?"v11_evan_insight":id==M?"v11_mira_vigor":"v11_finn_resolve",passiveUnlocked=true,
                unlockedActionIds=id==E?new List<string>{"v12_evan_mark","v13_evan_correct"}:id==M?new List<string>{"v12_mira_chase","v12_mira_break"}:new List<string>{"v12_finn_seed","v14_finn_detonate"}});
            if(core==M)config.v11.buffs.Add(new V11BuffStack{id="v14_mira_press",stacks=1});
            var s=CombatState.CreatePartyForPreset("v14-evade","run",19,config);foreach(var actor in s.actors){actor.x=2;actor.z=0;}
            game=new CombatSession(s,config);Enemy.aiState=1;Enemy.attackGroupId=Root;S.momentumLedger.registeredDangerousThreats.Add(Root);
        }
        void Capture(bool covered=true,string actor=M)=>game.V14CaptureEvadeWarning(S,Enemy.id,Root,actor,covered);
        bool Settle(bool inside=false,string actor=M,string root=Root)=>game.V14SettleEvadeWarning(S,Enemy.id,root,actor,inside);
        [TestCase(PartyCombatMode.CoreAndAssist)][TestCase(PartyCombatMode.PersistentTrio)]
        public void ActualDepartureFromRegisteredWarningAwardsExistingQualificationWithoutDice(PartyCombatMode mode)
        {
            Make(mode);string random=CombatJson.Write(S.random);Capture();Mira.z=4;Assert.That(Settle(),Is.True);
            Assert.That(Mira.v14Core.dodgeRoot,Is.EqualTo(Root));Assert.That(Mira.v12.readinessRemaining,Is.EqualTo(4));Assert.That(game.V14MiraDamageMultiplier(S,Mira,"v12_mira_chase",true),Is.EqualTo(1.5));
            Assert.That(CombatJson.Write(S.random),Is.EqualTo(random));Assert.That(S.rolls,Is.Empty);Assert.That(S.incomingEvents,Is.Empty);
        }
        [Test] public void InitiallyOutsideEmptyDodgeCannotEnterThenLeaveToGainQualification()
        {Make();Capture(false);Mira.z=1;Capture(true);Mira.z=5;Assert.That(Settle(),Is.False);Assert.That(Mira.v14Core,Is.Null);}
        [Test] public void WallBlockedMovementAndStillCoveredMovementDoNotCount()
        {
            Make();Capture();Assert.That(Settle(),Is.False);Assert.That(Mira.v14Core,Is.Null);
            Make();Capture();Mira.x+=.3f;Assert.That(Settle(),Is.False);
            Make();Capture();Mira.x+=2;Assert.That(Settle(true),Is.False);
        }
        [Test] public void UnregisteredCanceledAndDamagedThreatsNeverGrant()
        {
            Make();S.momentumLedger.registeredDangerousThreats.Clear();Capture();Mira.z=5;Assert.That(Settle(),Is.False);
            Make();Enemy.aiState=2;Capture();Mira.z=5;Assert.That(Settle(),Is.False);
            Make();Capture();Enemy.hp=0;Mira.z=5;Assert.That(Settle(),Is.False);
            Make();Capture();Mira.hp--;Mira.z=5;Assert.That(Settle(),Is.False);
        }
        [Test] public void CanonicalBossContactAndOutsideAvoidanceShareOneStoredRoot()
        {
            Make();Capture();Mira.z=5;Assert.That(Settle(root:Root+"/first"),Is.True);
            game.V14MiraConsume(S,Mira,Enemy,"counter","v12_mira_chase",8,true);Assert.That(Mira.v14Core.dodgeRoot,Is.Null);
            game.V14ObserveMiraDodge(S,new PendingIncoming{rootId=Root+"/return/second",defenderId=M,attackerId=Enemy.id,reason="avoided_by_dodge",baseDamage=6});
            Assert.That(Mira.v14Core.dodgeRoot,Is.Null);Assert.That(Mira.v14Core.encounters.Single().dodgeRoots.Count,Is.EqualTo(1));Assert.That(Settle(),Is.False);
        }
        [Test] public void InVolumeBossInvulnerabilityRecognizesTheRegisteredParentWithoutASecondGrant()
        {
            Make();game.V14ObserveMiraDodge(S,new PendingIncoming{rootId=Root+"/first",defenderId=M,attackerId=Enemy.id,reason="avoided_by_dodge",baseDamage=6});
            Assert.That(Mira.v14Core.dodgeRoot,Is.EqualTo(Root));Assert.That(Mira.v14Core.encounters.Single().dodgeRoots.Single(),Is.EqualTo(Root));
        }
        [Test] public void SaveMidWarningAndAfterCounterPreservesEvidenceAndDedupe()
        {
            Make();Capture();string saved=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(saved),CombatJson.Clone(config));Mira.z=5;Assert.That(Settle(),Is.True);
            game.V14MiraConsume(S,Mira,Enemy,"counter","v12_mira_chase",8,true);saved=CombatJson.Write(S);game=new CombatSession(CombatJson.Read<CombatState>(saved),CombatJson.Clone(config));
            Assert.That(Settle(),Is.False);Assert.That(Mira.v14Core.dodgeRoot,Is.Null);Assert.That(Mira.v14Core.encounters.Single().dodgeRoots.Single(),Is.EqualTo(Root));
        }
        [Test] public void NewEncounterCleanupRemovesOnlyEvadeEvidence()
        {Make();Capture();S.world.strings["author-content"]="keep";S.world.numbers["focusScale"]=.12f;CombatSession.V14ResetEvadeWorld(S);Assert.That(S.world.strings.Keys.Any(x=>x.StartsWith("v14/evade/")),Is.False);Assert.That(S.world.numbers.Keys.Any(x=>x.StartsWith("v14/evade/")),Is.False);Assert.That(S.world.strings["author-content"],Is.EqualTo("keep"));Assert.That(S.world.numbers["focusScale"],Is.EqualTo(.12f));}
        [Test] public void NonCoreMiraOnlyReceivesHerExistingLimitedAssistReward()
        {Make(core:F);Capture(actor:F);S.Actor(F).z=5;Assert.That(Settle(actor:F),Is.True);Assert.That(game.Member(S,M).assistRewardUntil,Is.EqualTo(S.simulationSeconds+4));Assert.That(Mira.v14Core,Is.Null);}
    }
}
