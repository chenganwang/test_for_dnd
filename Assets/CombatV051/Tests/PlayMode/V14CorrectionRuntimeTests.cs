using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Real BattleWorld entry/derived-command fixtures with explicit rules, positions
    // and a deterministic RNG seed. Not natural-route or human-play evidence.
    public sealed class V14CorrectionRuntimeTests
    {
        const string E=CombatSession.V12Evan,M=CombatSession.V12Mira;
        BattleWorld world;string directory;
        CombatState S=>world.State;ActorState Enemy=>S.enemies[0];
        static void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Correction-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V14 correction runtime fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartMainCharacterTraining();world.enabled=false;world.Controls.Map.Disable();
            if(world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            var config=world.Session.Config;config.v11.coreActorId=E;config.v11.buffs.Clear();config.v11.v13FinnTree="growth";
            config.v11.v14BuildVersion=V14BuildCatalog.Version;config.v11.v14FinnGrowthAction=CombatSession.V14DetonateId;config.v13=new V13Rules();config.v14=new V14Rules();
            config.v11.actors.Find(a=>a.actorId==E).unlockedActionIds=new List<string>{"v12_evan_mark","v13_evan_correct"};
            config.v11.actors.Find(a=>a.actorId==CombatSession.V12Finn).unlockedActionIds=new List<string>{"v12_finn_seed",CombatSession.V14DetonateId};
            foreach(string id in new[]{"v14_evan_firstproof","v14_evan_review","v14_evan_settle"})config.v11.buffs.Add(new V11BuffStack{id=id,stacks=1});
            CombatSession.InitializeV13State(S,config);S.activeActorId=S.commandActorId=E;
            foreach(var a in S.actors){a.x=-4;a.z=-4;a.majorRemaining=a.reactionRemaining=a.guardRemaining=a.dodgeRemaining=a.windupRemaining=a.staggerRemaining=0;a.aimPreparing=false;a.basicAttackIntent=null;}
            foreach(var enemy in S.enemies){enemy.x=10;enemy.z=10;enemy.aiState=2;enemy.aiRemaining=100;enemy.hp=enemy.maxHp=500;enemy.statuses.Clear();}
            Enemy.x=-3;Enemy.z=-4;world.SelectedActor=E;world.SelectedTarget=Enemy.id;world.SendMessage("SynchronizeViews");Physics.SyncTransforms();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V14-Correction-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        void Mode(PartyCombatMode mode){world.Session.Config.v14.partyCombatMode=mode;S.v14.partyCombatMode=mode;}
        void BackgroundNine()
        {
            for(ulong seed=1;seed<200000;seed++)
            {var stream=RandomStreams.Stream(seed,"v14-world-correction");var copy=CombatJson.Clone(stream);if(RandomStreams.D20(copy)==9){S.random.backgroundCombat=stream;return;}}
            Assert.Fail("deterministic RNG seed not found");
        }
        IEnumerator Review(DiceAnimationSpeed speed)
        {
            world.SetDiceAnimationSpeed(speed);Good(world.Command(CommandKind.EnterFocus,actor:E));
            Good(world.Command(CommandKind.ConfirmRoll,actor:M,target:Enemy.id,action:"v12_mira_break"));world.AdvanceRollAnimation(world.CurrentRollDuration+.01f);
            Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));string ticket=S.pendingRoll.id,fate=CombatJson.Write(S.v13.fateRng);ulong draw=S.pendingRoll.rngAfter;
            Good(world.V13Correct("reroll"));Assert.That(S.pendingRoll.id,Is.EqualTo(ticket));Assert.That(S.pendingRoll.rngAfter,Is.GreaterThan(draw));Assert.That(world.RollElapsed,Is.Zero);
            float duration=world.CurrentRollDuration;world.AdvanceRollAnimation(duration*.5f);Assert.That(S.mode,Is.EqualTo(CombatMode.RollFreeze));
            world.AdvanceRollAnimation(duration*.51f);Assert.That(S.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(CombatJson.Write(S.v13.fateRng),Is.EqualTo(fate));
            Assert.That(world.Command(CommandKind.Modify).reason,Is.EqualTo("manual_intervention_already_used"));yield return null;
        }
        [UnityTest] public IEnumerator ReviewButtonRestartsFullStandardDicePresentation(){yield return Review(DiceAnimationSpeed.Standard);}
        [UnityTest] public IEnumerator ReviewButtonRestartsFullFastDicePresentation(){yield return Review(DiceAnimationSpeed.Fast);}
        IEnumerator Chase(PartyCombatMode mode,bool retreat)
        {
            Mode(mode);Enemy.armorClass=S.Actor(M).attackBonus+10;Good(world.Command(CommandKind.EnterFocus,actor:E));world.SelectedTarget=M;Good(world.V13Correct());
            Good(world.Command(CommandKind.ExitFocus));world.Session.Tick(1.3f);BackgroundNine();int history=S.rolls.Count;ulong draw=S.random.backgroundCombat.drawCount;
            Good(world.RequestHeroAction(M,"v12_mira_chase",Enemy.id,CommandAuthority.Player));string root=S.Actor(M).v12.intent.id;int hp=Enemy.hp;
            if(retreat){S.Actor(E).x=Enemy.x-world.Session.V12ChaseRange(M)-.01f;world.SendMessage("SynchronizeViews");Physics.SyncTransforms();}
            world.Session.Tick(world.Session.Config.v12.chaseWindup+.01f);world.SendMessage("AdvanceV12Actions",.01f);
            var receipt=S.receipts.Single(r=>r.id=="resolve/"+root);Good(receipt.result);Assert.That(S.rolls.Count,Is.EqualTo(history));
            if(retreat)
            {Assert.That(receipt.result.reason,Is.EqualTo("hero_target_moved_or_blocked"));Assert.That(Enemy.hp,Is.EqualTo(hp));Assert.That(S.random.backgroundCombat.drawCount,Is.EqualTo(draw));Assert.That(S.p6.resourceEvents.Any(e=>e.kind=="v14-settle"),Is.False);}
            else
            {Assert.That(receipt.result.rawFaces,Is.EqualTo(new[]{9}));Assert.That(receipt.result.damage,Is.GreaterThan(0));Assert.That(S.p6.resourceEvents.Single(e=>e.root==root).qualifiedAmount,Is.EqualTo(30));Assert.That(S.Actor(E).v14Core.encounters.Single(e=>e.encounterId==S.encounterId).settlementRoots,Does.Contain(root));}
            yield return null;
        }
        [UnityTest] public IEnumerator TrioPreparedChaseCommitsSettlementThroughDerivedWorldCommand(){yield return Chase(PartyCombatMode.PersistentTrio,false);}
        [UnityTest] public IEnumerator AssistPreparedChaseCommitsSettlementThroughDerivedWorldCommand(){yield return Chase(PartyCombatMode.CoreAndAssist,false);}
        [UnityTest] public IEnumerator AssistChaseCancelsWhenCurrentCoreAnchorRetreatsOutOfRange(){yield return Chase(PartyCombatMode.CoreAndAssist,true);}
    }
}
