using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using NUnit.Framework;

namespace CombatV051.Tests
{
    // Rule fixtures deliberately use close positions/high target HP and seed search.
    // They are not natural Player or human fun evidence.
    public sealed class V11CombatTests
    {
        const string Pal="char_demo_paladin";
        CombatConfig config;CombatSession session;int sequence;
        CombatState S=>session.State;ActorState P=>S.Actor(Pal);ActorState E=>S.enemies[0];
        static ulong SeedFor(int raw){for(ulong i=1;i<10000;i++)if(RandomStreams.D20(RandomStreams.Create(i).visibleTactical)==raw)return i;throw new Exception("seed_missing");}
        static V11ActorLoadout Loadout(string id){var d=V11Catalog.FindCharacterForRules(id,"v11-character-1");return new V11ActorLoadout{actorId=id,passiveId=d.passiveId,passiveUnlocked=true,unlockedActionIds=new List<string>(d.actionIds)};}
        void Make(string a="mage",string b="rogue",int raw=14,params string[] buffs)
        {
            config=new CombatConfig{p5=new P5Config(),p6=new P6Config(),v11=new V11CombatRules{coreActorId=Pal,actors=new List<V11ActorLoadout>{Loadout(Pal),Loadout(a),Loadout(b)},buffs=buffs.GroupBy(x=>x).Select(g=>new V11BuffStack{id=g.Key,stacks=g.Count()}).ToList()}};
            var state=CombatState.CreatePartyForPreset("v11-rules","run",SeedFor(raw),config);foreach(var x in state.actors){x.x=0;x.z=0;}state.enemies[0].x=1;state.enemies[0].z=0;state.enemies[0].hp=state.enemies[0].maxHp=2000;state.enemies[0].armorClass=14;session=new CombatSession(state,config);sequence=0;
        }
        CombatCommand C(CommandKind kind,string actor=Pal,string action=CombatSession.P6SmiteId,string target=null)=>new CombatCommand{id="v11-test/"+(++sequence),kind=kind,actorId=actor,actionId=action,targetId=target??E.id};
        CombatResult Do(CombatCommand c){var r=session.Execute(c);Assert.That(r.success,Is.True,r.reason);return r;}
        CombatResult Do(CommandKind kind,string actor=Pal,string action=CombatSession.P6SmiteId,string target=null)=>Do(C(kind,actor,action,target));
        string Reveal(string actor=Pal,string action=CombatSession.P6SmiteId){Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll,actor,action,V11AbilityCatalog.IsSelfTarget(action)?actor:E.id);string id=S.pendingRoll.id;Do(CommandKind.RevealRoll);return id;}
        CombatResult Finish(bool full=false)
        {
            if(full){var select=C(CommandKind.SelectSmiteOvercharge);select.flag=true;Do(select);}Do(CommandKind.Commit);Do(CommandKind.ExitFocus);var q=S.queue.Last();var start=C(CommandKind.StartQueued,q.actorId,q.actionId,q.targetId);start.queueId=q.id;Do(start);session.Tick(S.rolls.Last().spec.windup+.01f);var end=C(CommandKind.ResolveQueued,q.actorId,q.actionId,q.targetId);end.queueId=q.id;end.authority=CommandAuthority.System;return Do(end);
        }
        void Bind(string id="mage")
        {var c=C(CommandKind.RequestBindRadiance,id,CombatSession.P6BindId);Do(c);session.Tick(.76f);var finish=C(CommandKind.ResolveBindRadiance,id,CombatSession.P6BindId);finish.rootId=c.id;finish.authority=CommandAuthority.System;Do(finish);}
        [TestCase("char_story_evan")][TestCase("char_story_mira")][TestCase("char_story_finn")][TestCase("fighter")][TestCase("mage")][TestCase("rogue")][TestCase(Pal)]
        public void EveryPassiveChangesActualActorOnlyOnce(string id)
        {
            Make(id==Pal?"mage":id,id=="rogue"?"mage":"rogue");var actor=S.Actor(id);var def=V11Catalog.FindCharacterForRules(id,"v11-character-1");var template=config.Template(def.role);
            switch(def.passiveId){case "v11_mira_vigor":case "v11_paladin_vigor":Assert.That(actor.maxHp,Is.EqualTo(template.hp+4));break;case "v11_evan_insight":case "v11_mage_insight":Assert.That(actor.intelligenceModifier,Is.EqualTo(template.intelligenceModifier+1));break;case "v11_finn_resolve":Assert.That(actor.wisSaveBonus,Is.EqualTo(template.wisSaveBonus+1));break;case "v11_fighter_guard":Assert.That(actor.armorClass,Is.EqualTo(template.armorClass+1));break;case "v11_rogue_accuracy":Assert.That(actor.attackBonus,Is.EqualTo(template.attackBonus+1));break;}
            actor.hp--;string state=CombatJson.Write(S);CombatSession.InitializeV11State(S,config,true);Assert.That(CombatJson.Write(S),Is.EqualTo(state));var restored=CombatJson.Read<CombatState>(state);CombatSession.InitializeV11State(restored,config,false);Assert.That(CombatJson.Write(restored),Is.EqualTo(state));
        }
        [Test] public void TwoMageIdentitiesUseTheirOwnPermissionsAndCanShareTheThreePersonParty()
        {
            Make("char_story_evan","char_story_finn");Assert.That(S.actors.Count,Is.EqualTo(3));Assert.That(S.actors.Count(a=>a.role==ActorRole.Mage),Is.EqualTo(2));
            Assert.That(session.Quote("char_story_finn","char_story_finn","v11_support_ward").legal,Is.False);Assert.That(session.Quote("char_story_evan","char_story_evan","v11_support_ward").legal,Is.True);Assert.That(session.QuoteBind("char_story_finn",E.id).legal,Is.False);Assert.That(session.QuoteBind("char_story_evan",E.id).legal,Is.True);
        }
        [TestCase("v11_support_ward","char_story_evan","absorption")][TestCase("v11_oath_aegis",Pal,"absorption")][TestCase("v11_nature_mend","char_story_finn","heal")][TestCase("v11_nature_bind","char_story_finn","p6-root")][TestCase("v11_quick_shot","rogue","damage")]
        public void EachNewActionUsesOnePaidVisibleRollAndActualEffect(string action,string actor,string effect)
        {
            Make(actor==Pal?"mage":actor,actor=="rogue"?"mage":"rogue");foreach(var a in S.actors)a.hp-=5;E.wisSaveBonus=-100;
            Do(CommandKind.EnterFocus);Do(CommandKind.ConfirmRoll,actor,action,V11AbilityCatalog.IsSelfTarget(action)?actor:E.id);Assert.That(S.charges,Is.EqualTo(1));var ticket=S.pendingRoll;float sim=S.simulationSeconds,focus=S.focusBudget.secondsRemaining;session.Tick(2);Assert.That(S.simulationSeconds,Is.EqualTo(sim));Assert.That(S.focusBudget.secondsRemaining,Is.EqualTo(focus));Do(CommandKind.RevealRoll);
            // The actual ticket determines success; enemy saves cannot be rerolled by the caster.
            bool hit=S.pendingRoll.hit;var r=Finish();Assert.That(r.v11.effects.Count,hit?Is.GreaterThan(0):Is.Zero);if(hit)Assert.That(r.v11.effects.All(e=>e.kind==effect),Is.True);Assert.That(S.charges,Is.EqualTo(1));Assert.That(S.Actor(actor).heldBy,Is.Null);Assert.That(S.Actor(actor).spellSlots,Is.EqualTo(config.Template(S.Actor(actor).role).spellSlots));
        }
        [Test] public void HighEnemySaveIsAResistedRootNotAPlayerSuccess()
        {Make("char_story_finn");E.wisSaveBonus=100;Reveal("char_story_finn","v11_nature_bind");Assert.That(S.pendingRoll.hit,Is.False);var r=Finish();Assert.That(r.v11.effects,Is.Empty);Assert.That(E.statuses.Any(x=>x.id=="p6-root"),Is.False);}
        [Test] public void SelfSupportCannotTargetEnemyAndLockedSkillConsumesNothing()
        {
            Make("char_story_evan");Do(CommandKind.EnterFocus);string original=CombatJson.Write(S);var bad=C(CommandKind.ConfirmRoll,"char_story_evan","v11_support_ward");Assert.That(session.Execute(bad).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(original));config.v11.actors[1].unlockedActionIds.Remove("v11_support_ward");bad=C(CommandKind.ConfirmRoll,"char_story_evan","v11_support_ward","char_story_evan");Assert.That(session.Execute(bad).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(original));
        }
        [Test] public void AegisAbsorbsBeforeFinalHpOnceAndExpiresInSimulationNotFreeze()
        {
            Make();Reveal(Pal,"v11_oath_aegis");Finish();Assert.That(P.v11.shieldGranted,Is.EqualTo(6));Assert.That(P.p6.absorptionShield,Is.EqualTo(6));P.reactionRemaining=20;E.attackBonus=100;
            var hit=C(CommandKind.IncomingAttack,E.id,"attack",Pal);hit.authority=CommandAuthority.System;hit.rootId="incoming-shield";hit.amount=10;hit.qualified=true;int health=P.hp;var r=Do(hit);Assert.That(r.damage,Is.EqualTo(Math.Max(0,S.incomingEvents.Last().prospectiveDamage-6)));Assert.That(P.hp,Is.EqualTo(health-r.damage));string after=CombatJson.Write(S);Assert.That(Do(hit).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(after));session.Tick(6);Assert.That(P.p6.absorptionShield,Is.Zero);
        }
        [Test] public void NewActionsUseSameAtomicSinkAsDamageAndCannotReplayHeal()
        {
            Make("char_story_finn");foreach(var a in S.actors)a.hp-=6;Reveal("char_story_finn","v11_nature_mend");Do(CommandKind.Commit);Do(CommandKind.ExitFocus);var q=S.queue.Last();var start=C(CommandKind.StartQueued);start.queueId=q.id;Do(start);session.Tick(.36f);bool fail=true;session=new CombatSession(S,config,persistenceSink:s=>{if(fail)throw new IOException("v11-save-fault");});string before=CombatJson.Write(S);var end=C(CommandKind.ResolveQueued);end.queueId=q.id;end.authority=CommandAuthority.System;Assert.That(session.Execute(end).success,Is.False);Assert.That(CombatJson.Write(S),Is.EqualTo(before));fail=false;var r=Do(end);Assert.That(r.v11.effects.All(e=>e.amount==4),Is.True);string after=CombatJson.Write(S);Assert.That(Do(end).duplicate,Is.True);Assert.That(CombatJson.Write(S),Is.EqualTo(after));
        }
        [Test] public void SmiteDiceAndFullBuffsChangeDamageDiceWithoutChangingRawOrDoublePaying()
        {
            Make("mage","rogue",18,"core_smite_dice","core_smite_dice","core_full_dice");Reveal();var r=Finish(true);var d=r.p6.damages.Single();Assert.That(d.selectedRaw,Is.EqualTo(18));Assert.That(d.radiantFaces.Count,Is.EqualTo(8));Assert.That(d.critical,Is.False);Assert.That(S.charges,Is.Zero);Assert.That(d.v11.radiantDiceBonus,Is.EqualTo(2));Assert.That(d.v11.fullDiceBonus,Is.EqualTo(1));
        }
        [Test] public void SmiteOnlyAccuracyDoesNotChangeNaturalFaceOrOtherAttackBonus()
        {Make("mage","rogue",18,"core_smite_accuracy","core_smite_accuracy");int normal=P.attackBonus;Reveal();Assert.That(S.pendingRoll.selectedFace,Is.EqualTo(18));Assert.That(S.pendingRoll.total,Is.EqualTo(18+normal+2));Assert.That(S.pendingRoll.critical,Is.False);Assert.That(P.attackBonus,Is.EqualTo(normal));}
        [Test] public void ResonanceUsesSameMarkAndConsumesItOnlyOnActualRadiantDamage()
        {
            Make("mage","rogue",18,"core_radiant_resonance");E.wisSaveBonus=100;Bind();Reveal();var d=Finish().p6.damages.Single();Assert.That(d.markConsumed,Is.True);Assert.That(d.radiantMultiplier,Is.EqualTo(1.75f));Assert.That(d.radiantBeforeMitigation,Is.EqualTo((int)Math.Floor(d.radiantFaces.Sum()*1.75f)));Assert.That(E.statuses.Any(x=>x.p6!=null),Is.False);Assert.That(S.p6.markRewardCount,Is.EqualTo(1));
        }
        [Test] public void QualifiedDefenseArmsOneGuardDieAndAddsThrottledTeamMomentum()
        {
            Make("mage","rogue",18,"core_oath_guard","team_ward");S.charges=0;Do(CommandKind.StartGuard);session.Tick(.4f);E.attackBonus=100;float before=S.momentum;
            var hit=C(CommandKind.IncomingAttack,E.id,"attack",Pal);hit.authority=CommandAuthority.System;hit.rootId="guard-root";hit.amount=2;hit.qualified=true;Do(hit);Assert.That(S.momentum-before,Is.EqualTo(30).Within(.001));Assert.That(P.statuses.Any(x=>x.id=="v11-oath-guard"),Is.True);Do(CommandKind.EndGuard);S.charges=1;Reveal();Assert.That(S.pendingRoll.v11.guardRadiantDice,Is.EqualTo(1));Assert.That(P.statuses.Any(x=>x.id=="v11-oath-guard"),Is.False);Do(CommandKind.Cancel);Assert.That(S.charges,Is.Zero);
        }
        [Test] public void DevotionChangesOnlyEffectiveSimulationRechargeAndFullCapacityDiscards()
        {
            Make("mage","rogue",18,"core_devotion","core_devotion");S.charges=0;session.Tick(2);Assert.That(S.momentum,Is.EqualTo(28).Within(.001));S.charges=2;S.momentum=90;session.Tick(2);Assert.That(S.momentum,Is.Zero);S.charges=0;S.momentumLedger.disengaged=true;session.Tick(2);Assert.That(S.momentum,Is.Zero);
        }
        [Test] public void SmiteActualKillHealsOnceAndActualHitGrantsShortAc()
        {
            Make("mage","rogue",18,"core_mercy","core_afterlight");P.hp-=8;int before=P.hp;E.hp=1;Reveal();var d=Finish().p6.damages.Single();Assert.That(d.totalDamage,Is.EqualTo(1));Assert.That(P.hp,Is.EqualTo(before+4));Assert.That(P.statuses.Single(x=>x.id=="v11-afterlight").armorClassBonus,Is.EqualTo(2));session.Tick(2.1f);Assert.That(P.statuses.Any(x=>x.id=="v11-afterlight"),Is.False);
        }
        [Test] public void WinningClearHealsSurvivorsOnceWithoutReviveOrNewRunCarry()
        {
            Make("mage","rogue",18,"team_mending","team_mending");P.hp-=10;S.Actor("mage").hp=0;E.hp=0;int hp=P.hp;var clear=C(CommandKind.EndEncounter);clear.authority=CommandAuthority.System;Do(clear);Assert.That(P.hp,Is.EqualTo(hp+4));Assert.That(S.Actor("mage").hp,Is.Zero);string state=CombatJson.Write(S);Do(clear);Assert.That(CombatJson.Write(S),Is.EqualTo(state));Make();Assert.That(config.v11.buffs,Is.Empty);
        }
        [Test] public void PoolContainsEightCoreThreeGenericAndFiltersPreconditionsCaps()
        {
            Make();Assert.That(V11BuffCatalog.All.Count(x=>x.pool=="Core"&&V11BuffCatalog.AvailableInRules(config.v11,x.id)),Is.EqualTo(8));Assert.That(V11BuffCatalog.All.Count(x=>x.pool=="Generic"&&V11BuffCatalog.AvailableInRules(config.v11,x.id)),Is.EqualTo(3));Assert.That(V11BuffCatalog.Eligible(config.v11).Any(x=>x.id=="core_full_dice"),Is.False);config.v11.buffs.Add(new V11BuffStack{id="core_smite_dice",stacks=2});Assert.That(V11BuffCatalog.Eligible(config.v11).Any(x=>x.id=="core_smite_dice"),Is.False);Assert.That(V11BuffCatalog.Eligible(config.v11).Any(x=>x.id=="core_full_dice"),Is.True);config.v11.actors[1].unlockedActionIds.Remove(CombatSession.P6BindId);Assert.That(V11BuffCatalog.Eligible(config.v11).Any(x=>x.id=="core_radiant_resonance"),Is.False);
        }
        [TestCase("fighter","attack")][TestCase("fighter","shove")][TestCase("char_story_mira","attack")][TestCase("char_story_mira","shove")][TestCase("mage","control")][TestCase("rogue","sneak")]
        public void CatalogMappedExistingActivesStillExecuteTheirOriginalRules(string actor,string action)
        {
            Make(actor,actor=="rogue"?"mage":"rogue");E.constitutionModifier=-100;Reveal(actor,action);var r=Finish();Assert.That(S.rolls.Last().stage,Is.EqualTo("Resolved"));Assert.That(S.charges,Is.EqualTo(1));Assert.That(r.p5Effects,Is.Not.Null);Assert.That(r.p5Effects.Any(e=>e.effective),Is.True);
        }
        [Test] public void EvanUsesExistingBindWithoutStealingActiveCharacterAndMarksThroughSuccessfulSave()
        {Make("char_story_evan");E.wisSaveBonus=100;Bind("char_story_evan");Assert.That(S.activeActorId,Is.EqualTo(Pal));Assert.That(S.p6.bindResults.Single().savePassed,Is.True);Assert.That(S.p6.bindResults.Single().markApplied,Is.True);}
        [Test] public void TeamReactionBuffChangesActualReactionPaymentTimer()
        {
            Make("mage","rogue",14,"team_reaction","team_reaction");var mage=S.Actor("mage");int raw=RandomStreams.D20(CombatJson.Clone(S.random.backgroundCombat));Assert.That(raw,Is.InRange(2,19),"This fixture needs a non-natural-one/twenty background seed");E.attackBonus=mage.armorClass+1-raw;
            var hit=C(CommandKind.IncomingAttack,E.id,"attack","mage");hit.authority=CommandAuthority.System;hit.rootId="reaction-v11";hit.amount=5;hit.qualified=true;Do(hit);var w=S.reactionWindows.Single();var begin=C(CommandKind.BeginReaction,"mage","shield","mage");begin.reactionWindowId=w.id;begin.reactionId="shield";Do(begin);var commit=C(CommandKind.CommitReaction,"mage","shield","mage");commit.reactionWindowId=w.id;commit.reactionId="shield";Do(commit);Assert.That(S.Actor("mage").reactionRemaining,Is.EqualTo(config.reactionRecovery*.85f*.85f).Within(.001));Assert.That(S.incomingEvents.Single().finalDamage,Is.Zero);
        }
        [Test] public void LegacyNullExtensionsSerializeWithoutV11AndKeepDefaultP6Permissions()
        {var old=new CombatConfig{p6=new P6Config()};var state=CombatState.CreatePartyForPreset("old","old",1,old);new CombatSession(state,old);Assert.That(CombatJson.Write(old),Does.Not.Contain("v11"));Assert.That(CombatJson.Write(state),Does.Not.Contain("v11"));Assert.That(old.AllowsAction(state.Actor(Pal),CombatSession.P6SmiteId),Is.True);}
    }
}
