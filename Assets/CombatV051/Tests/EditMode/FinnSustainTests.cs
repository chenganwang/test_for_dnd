using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Isolated rule fixtures: explicit HP, close positions and declared background
    // seeds distinguish branches. These are not natural Player/fun evidence.
    public sealed class FinnSustainTests
    {
        const string Finn="char_story_finn",Pal="char_demo_paladin";
        CombatConfig config;CombatSession session;int sequence;
        CombatState S=>session.State;ActorState F=>S.Actor(Finn);ActorState E=>S.enemies[0];
        V11FinnSustainState Sustain=>F.v11.finnSustain;
        static V11ActorLoadout Loadout(string id)
        {var d=V11Catalog.FindCharacterForRules(id,"v11-character-1");return new V11ActorLoadout{actorId=id,passiveId=d.passiveId,passiveUnlocked=true,unlockedActionIds=new List<string>(d.actionIds),finnSustain=id==Finn?new V11FinnSustainConfig():null};}
        [SetUp] public void Setup()
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v11=new V11CombatRules{coreActorId=Pal,actors=new List<V11ActorLoadout>{Loadout(Pal),Loadout(Finn),Loadout("char_story_evan")}}};
            var state=CombatState.CreatePartyForPreset("finn-test","run",81,config);foreach(var a in state.actors){a.x=0;a.z=0;}state.Actor(Finn).maxHp=100;state.Actor(Finn).hp=50;
            state.enemies[0].x=1;state.enemies[0].z=0;state.enemies[0].hp=state.enemies[0].maxHp=1000;state.enemies[0].armorClass=14;session=new CombatSession(state,config);sequence=0;
        }
        void Seed(int raw)
        {for(ulong i=1;i<10000;i++){var streams=RandomStreams.Create(i);if(RandomStreams.D20(CombatJson.Clone(streams.backgroundCombat))==raw){S.random.backgroundCombat=streams.backgroundCombat;return;}}Assert.Fail("fixture seed unavailable");}
        CombatCommand C(CommandKind kind)=>new CombatCommand{id="finn-test/"+(++sequence),kind=kind,actorId=Finn,targetId=E.id};
        CombatResult Good(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        CombatCommand Start(CommandAuthority authority=CommandAuthority.Player,int raw=14)
        {if(F.majorRemaining>0)session.Tick(F.majorRemaining+.001f);Seed(raw);var c=C(CommandKind.StartBasicAttack);c.authority=authority;Good(c);session.Tick(config.basicContactSeconds+.001f);return c;}
        CombatCommand Contact(CombatCommand start)
        {var c=C(CommandKind.ResolveBasicAttack);c.authority=CommandAuthority.System;c.orderId=start.id;return c;}
        CombatResult Hit(CommandAuthority authority=CommandAuthority.Player,int raw=14)=>Good(Contact(Start(authority,raw)));
        int Healing(CombatResult r)=>r.v11?.effects.Where(e=>e.kind=="micro_heal").Sum(e=>e.amount)??0;
        [Test] public void TwoHalfHpHitsPublishOnlyOneActualHpAndNoFractionFeedback()
        {
            var first=Hit();Assert.That(first.damage,Is.GreaterThan(0));Assert.That(F.hp,Is.EqualTo(50));Assert.That(Sustain.fractionalHp,Is.EqualTo(.5));Assert.That(first.v11,Is.Null);
            var second=Hit();Assert.That(F.hp,Is.EqualTo(51));Assert.That(Healing(second),Is.EqualTo(1));Assert.That(Sustain.fractionalHp,Is.Zero);Assert.That(second.v11.effects.Single().actorId,Is.EqualTo(Finn));
        }
        [Test] public void RealTwentyTwoHpScaleDoesNotRoundEveryContactUpToOne()
        {F.maxHp=22;F.hp=10;for(int i=0;i<9;i++)Assert.That(Healing(Hit()),Is.Zero);Assert.That(F.hp,Is.EqualTo(10));Assert.That(Sustain.fractionalHp,Is.EqualTo(.99).Within(1e-9));Assert.That(Healing(Hit()),Is.EqualTo(1));Assert.That(F.hp,Is.EqualTo(11));Assert.That(Sustain.fractionalHp,Is.EqualTo(.1).Within(1e-9));}
        [TestCase(1)][TestCase(2)] public void NaturalMissOrAcBlockDoesNotAccumulate(int raw)
        {E.armorClass=1000;Assert.That(Hit(raw:raw).damage,Is.Zero);Assert.That(F.hp,Is.EqualTo(50));Assert.That(Sustain,Is.Null);}
        [TestCase("range")][TestCase("occlusion")][TestCase("dead")][TestCase("missing")]
        public void SwingWithNoValidEnemyContactDoesNotHeal(string invalid)
        {
            var start=Start();var contact=Contact(start);if(invalid=="range")E.x=100;if(invalid=="occlusion")contact.lineOfSight=false;if(invalid=="dead")E.hp=0;if(invalid=="missing")F.basicAttackIntent.targetId="environment/stone";
            var r=Good(contact);Assert.That(r.damage,Is.Zero);Assert.That(Healing(r),Is.Zero);Assert.That(Sustain,Is.Null);
        }
        [TestCase("dodge")][TestCase("shield")][TestCase("zero")]
        public void FullyAvoidedAbsorbedOrZeroDamageContactDoesNotHeal(string prevention)
        {
            var start=Start();if(prevention=="dodge")E.dodgeRemaining=2;
            if(prevention=="shield"){E.v11=new V11ActorState{shieldGranted=100,shieldRemaining=2};E.p6.absorptionShield=100;}
            if(prevention=="zero")config.Template(ActorRole.Mage).basic.damage=0;
            int before=E.hp;var r=Good(Contact(start));Assert.That(E.hp,Is.EqualTo(before));Assert.That(Healing(r),Is.Zero);Assert.That(Sustain,Is.Null);
        }
        [Test] public void FullHpNeverStoresHiddenHealingOrPublishesAnEffect()
        {F.hp=F.maxHp;for(int i=0;i<4;i++)Assert.That(Healing(Hit()),Is.Zero);Assert.That(F.hp,Is.EqualTo(100));Assert.That(Sustain.fractionalHp,Is.Zero);F.hp=99;Assert.That(Healing(Hit()),Is.Zero);Assert.That(F.hp,Is.EqualTo(99));}
        [Test] public void HealingToCapDropsRemainderAndNeverOverflows()
        {config.v11.actors.Find(a=>a.actorId==Finn).finnSustain.maxHpFraction=.025;F.hp=99;Assert.That(Healing(Hit()),Is.EqualTo(1));Assert.That(F.hp,Is.EqualTo(100));Assert.That(Sustain.fractionalHp,Is.Zero);}
        [Test] public void DownedFinnCannotFinishAttackOrRevive()
        {var start=Start();F.hp=0;var r=session.Execute(Contact(start));Assert.That(r.success,Is.False);Assert.That(F.hp,Is.Zero);Assert.That(Healing(r),Is.Zero);}
        [Test] public void DuplicateCommandAndDifferentColliderCommandCannotRepeatRoot()
        {
            F.maxHp=200;F.hp=50;var start=Start();var contact=Contact(start);var first=Good(contact);Assert.That(Healing(first),Is.EqualTo(1));string snapshot=CombatJson.Write(S);
            Assert.That(Good(contact).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(snapshot));var otherCollider=Contact(start);Assert.That(session.Execute(otherCollider).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(snapshot));
            Assert.That(Healing(Hit()),Is.EqualTo(1));Assert.That(F.hp,Is.EqualTo(52));
        }
        [Test] public void FastSeparateRootsRespectSimulationInterval()
        {
            F.maxHp=200;F.hp=50;Assert.That(Healing(Hit()),Is.EqualTo(1));F.majorRemaining=0;
            Assert.That(Healing(Hit()),Is.Zero);Assert.That(F.hp,Is.EqualTo(51));F.majorRemaining=0;session.Tick(.5f);Assert.That(Healing(Hit()),Is.EqualTo(1));
        }
        [Test] public void AllyAiAndPlayerControlUseExactlyTheSameHook()
        {F.maxHp=200;F.hp=50;var r=Hit(CommandAuthority.AllyAI);Assert.That(r.damage,Is.GreaterThan(0));Assert.That(Healing(r),Is.EqualTo(1));Assert.That(F.hp,Is.EqualTo(51));Good(new CombatCommand{id="switch-finn",kind=CommandKind.SwitchActive,actorId=Finn});Assert.That(Healing(Hit()),Is.EqualTo(1));}
        [TestCase("dot")][TestCase("reflected")][TestCase("skill")]
        public void OtherIncomingDamagePathsNeverHeal(string source)
        {
            F.maxHp=200;F.hp=50;Seed(14);var c=C(source=="skill"?CommandKind.IncomingSave:CommandKind.IncomingAttack);c.rootId=source+"/root";c.authority=CommandAuthority.System;c.qualified=true;c.amount=2;c.fromReaction=source=="reflected";c.dc=15;int hp=E.hp;
            var r=Good(c);Assert.That(E.hp,Is.LessThan(hp));Assert.That(F.hp,Is.EqualTo(50));Assert.That(Healing(r),Is.Zero);Assert.That(Sustain,Is.Null);
        }
        [Test] public void UnlockedOtherMageCannotTriggerFinnTrait()
        {var c=C(CommandKind.BasicAttack);c.actorId="char_story_evan";Seed(14);var r=Good(c);Assert.That(r.damage,Is.GreaterThan(0));Assert.That(Healing(r),Is.Zero);Assert.That(F.hp,Is.EqualTo(50));}
        [Test] public void LockedPassiveOrOldLoadoutKeepsTraitDisabled()
        {var l=config.v11.actors.Find(a=>a.actorId==Finn);l.passiveUnlocked=false;Hit();Assert.That(Sustain,Is.Null);l.passiveUnlocked=true;l.finnSustain=null;Hit();Assert.That(Sustain,Is.Null);Assert.That(F.hp,Is.EqualTo(50));Assert.That(CombatJson.Write(config.v11),Does.Not.Contain("finnSustain"));}
        [Test] public void OptionalSingleAllyTargetDoesNotBecomeWholePartyHealing()
        {config.v11.actors.Find(a=>a.actorId==Finn).finnSustain.targetActorId=Pal;var pal=S.Actor(Pal);pal.maxHp=200;pal.hp=50;var r=Hit();Assert.That(S.Actor(Pal).hp,Is.EqualTo(51));Assert.That(F.hp,Is.EqualTo(50));Assert.That(r.v11.effects.Single().targetId,Is.EqualTo(Pal));}
        [Test] public void SaveRestorePreservesFractionCooldownAndDeduplication()
        {
            var start=Start();var contact=Contact(start);Good(contact);var serialized=CombatJson.Write(S);session=new CombatSession(CombatJson.Read<CombatState>(serialized),CombatJson.Read<CombatConfig>(CombatJson.Write(config)));
            Assert.That(Sustain.fractionalHp,Is.EqualTo(.5));Assert.That(Good(contact).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(serialized));Assert.That(Healing(Hit()),Is.EqualTo(1));Assert.That(F.hp,Is.EqualTo(51));
        }
        [Test] public void AtomicDiskSaveRoundtripKeepsUnpaidFractionAndSameHitReceipt()
        {
            string directory=Path.Combine(Path.GetTempPath(),"FinnSustainRules",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                string path=Path.Combine(directory,"combat.json");session=new CombatSession(S,config,path);var contact=Contact(Start());Good(contact);Assert.That(session.Save().success,Is.True);
                var before=CombatJson.Write(S);session=CombatSession.Load(path,config,S.rulesProfileId);Assert.That(CombatJson.Write(S),Is.EqualTo(before));Assert.That(Sustain.fractionalHp,Is.EqualTo(.5));Assert.That(Good(contact).duplicate,Is.True);Assert.That(Healing(Hit()),Is.EqualTo(1));
            }
            finally
            {
                string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FinnSustainRules"))+Path.DirectorySeparatorChar;
                if(Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
            }
        }
        [Test] public void FailedPersistenceCommitsNeitherDamageNorFractionAndRetryIsOnce()
        {
            F.maxHp=200;F.hp=50;var start=Start();var contact=Contact(start);bool fail=true;session=new CombatSession(S,config,persistenceSink:s=>{if(fail)throw new IOException("declared save failure");});string before=CombatJson.Write(S);
            Assert.That(session.Execute(contact).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));fail=false;Assert.That(Healing(Good(contact)),Is.EqualTo(1));Assert.That(Good(contact).duplicate,Is.True);Assert.That(F.hp,Is.EqualTo(51));
        }
        [Test] public void RollFreezeAndPauseDoNotAgeAttackIntervalOrProduceHealing()
        {
            Hit();float sim=S.simulationSeconds;double fraction=Sustain.fractionalHp;Good(C(CommandKind.EnterFocus));var roll=C(CommandKind.ConfirmRoll);roll.actorId=Pal;roll.actionId=CombatSession.P6SmiteId;Good(roll);session.Tick(2);
            Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(S.simulationSeconds,Is.EqualTo(sim));Assert.That(Sustain.fractionalHp,Is.EqualTo(fraction));Assert.That(F.hp,Is.EqualTo(50));Good(C(CommandKind.Pause));session.Tick(10);Assert.That(S.simulationSeconds,Is.EqualTo(sim));Assert.That(Sustain.fractionalHp,Is.EqualTo(fraction));
        }
        [Test] public void FullHealthOrDeathClearsPriorFractionAndEncounterResetClearsOnlyPendingMicroHeal()
        {Hit();F.hp=F.maxHp;Good(C(CommandKind.EnterFocus));Assert.That(Sustain.fractionalHp,Is.Zero);Good(C(CommandKind.ExitFocus));F.hp=50;Hit();Assert.That(Sustain.fractionalHp,Is.EqualTo(.5));int hp=F.hp;CombatSession.ResetP6EncounterState(S);Assert.That(F.v11.finnSustain,Is.Null);Assert.That(F.hp,Is.EqualTo(hp));Assert.That(session.Save().success,Is.True);}
        [Test] public void MainCharacterTrainingExceptionCannotAuthorizeFormalNoCoreSave()
        {
            config.v11.actors=new List<V11ActorLoadout>{Loadout("char_story_evan"),Loadout("char_story_mira"),Loadout(Finn)};config.v11.coreActorId=null;var state=S.Clone();CombatState.ApplyV11Party(state,config,true);state.activeActorId=state.commandActorId=Finn;CombatSession.InitializeP5State(state,config);state.world.strings["context"]="TRAINING";state.world.strings["trainingRoster"]="MAIN_CHARACTERS";CombatSession.InitializeP6State(state,config);
            session=new CombatSession(state,config);Assert.That(session.Save().success,Is.True);S.world.strings["context"]="CHALLENGE";Assert.That(session.Save().success,Is.False);S.world.strings["context"]="TRAINING";S.p6.training=false;Assert.That(session.Save().success,Is.False);S.p6.training=true;S.world.strings.Remove("trainingRoster");Assert.That(session.Save().success,Is.False);S.world.strings["trainingRoster"]="MAIN_CHARACTERS";config.v11.buffs.Add(new V11BuffStack{id="team_ward",stacks=1});Assert.That(session.Save().success,Is.False);
        }
        [Test] public void InvalidSustainRulesRejectRatherThanHealUnexpectedTargets()
        {config.v11.actors.Find(a=>a.actorId==Finn).finnSustain.targetActorId=E.id;Assert.That(session.Save().success,Is.False);config.v11.actors.Find(a=>a.actorId==Finn).finnSustain.targetActorId=null;config.v11.actors.Find(a=>a.actorId==Finn).finnSustain.maxHpFraction=double.NaN;Assert.That(session.Save().success,Is.False);}
    }
}
