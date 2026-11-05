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
    // Integration fixture, not natural Player play: an isolated training arena has
    // an explicitly unlocked Evan/Paladin/Rogue team, close positions, idle enemies
    // and long AI Major timers. Ward uses a declared successful PCG seed solely to
    // test effect publication. Quick Shot accepts the real naturally rolled result.
    public sealed class V11QueueRuntimeTests
    {
        const string Pal="char_demo_paladin",Evan="char_story_evan";
        BattleWorld world;string directory;
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Queue",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V11 real Update queue fixture").AddComponent<BattleWorld>();world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartTraining();world.enabled=false;world.Controls.Map.Disable();world.IgnoreApplicationFocus=true;
            var rules=new V11CombatRules{coreActorId=Pal};foreach(string id in new[]{Pal,Evan,"rogue"}){var definition=V11Catalog.FindCharacterForRules(id,"v11-character-1");rules.actors.Add(new V11ActorLoadout{actorId=id,passiveId=definition.passiveId,passiveUnlocked=true,unlockedActionIds=new List<string>(definition.actionIds)});}
            world.Session.Config.v11=rules;CombatState.ApplyV11Party(world.State,world.Session.Config,true);CombatSession.InitializeP5State(world.State,world.Session.Config);CombatSession.InitializeP6State(world.State,world.Session.Config,false);
            int index=0;foreach(var actor in world.State.actors){actor.x=-1;actor.z=-1+index++*.4f;actor.majorRemaining=100;actor.p5.basicRecovery=false;actor.p6.basicRecovery=false;}
            foreach(var enemy in world.State.enemies){enemy.x=2;enemy.z=0;enemy.hp=enemy.maxHp=1000;enemy.aiState=2;enemy.aiRemaining=100;enemy.lockedX=3;enemy.lockedZ=0;}
            world.SelectedActor=Pal;world.SelectedTarget=world.State.enemies[0].id;world.QuickDice=true;Physics.SyncTransforms();yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;Time.timeScale=1;
            string root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V11-Queue"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(root,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason+"; "+world.Message);
        IEnumerator Until(Func<bool> condition,float seconds,string label)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();while(!condition()&&timer.Elapsed.TotalSeconds<seconds)yield return null;
            Assert.That(condition(),Is.True,label+"; "+world.Message+"; LastTickError="+world.Session.LastTickError);
        }
        void WardSeed()
        {
            for(ulong seed=1;seed<10000;seed++){var streams=RandomStreams.Create(seed);if(RandomStreams.D20(CombatJson.Clone(streams.visibleTactical))==14){world.State.random.visibleTactical=streams.visibleTactical;return;}}
            Assert.Fail("Declared ward success seed unavailable");
        }
        IEnumerator RealQueue(string actor,string action,bool ward)
        {
            if(ward)WardSeed();world.State.Actor(actor).majorRemaining=0;world.SelectedActor=actor;world.SelectedAction=action;world.SelectedTarget=V11AbilityCatalog.IsSelfTarget(action)?actor:world.State.enemies[0].id;
            Good(world.Command(CommandKind.EnterFocus));Good(world.SelectCommandActor(actor));world.SelectedAction=action;world.SelectedTarget=V11AbilityCatalog.IsSelfTarget(action)?actor:world.State.enemies[0].id;
            int charges=world.State.charges,enemyHp=world.State.enemies[0].hp;Good(world.Command(CommandKind.ConfirmRoll));string ticketId=world.State.pendingRoll.id;int raw=world.State.pendingRoll.selectedFace;bool hit=world.State.pendingRoll.hit;float simulation=world.State.simulationSeconds;
            world.enabled=true;
            var rollTimer=System.Diagnostics.Stopwatch.StartNew(); while(world.State.mode==CombatMode.RollFreeze&&rollTimer.Elapsed.TotalSeconds<4){Assert.That(world.State.simulationSeconds,Is.EqualTo(simulation));yield return null;}
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(world.State.pendingRoll.selectedFace,Is.EqualTo(raw));Good(world.Command(CommandKind.Commit));string queueId=world.State.queue.Last().id;Good(world.Command(CommandKind.ExitFocus));int startFrame=world.SimulatedFrames;
            yield return Until(()=>world.State.queue.Single(q=>q.id==queueId).stage=="Resolved",4,"Enabled BattleWorld.Update must resolve the paid queue; no direct ResolveQueued test shortcut");
            world.enabled=false;var queue=world.State.queue.Single(q=>q.id==queueId);var ticket=world.State.rolls.Single(t=>t.id==ticketId);
            Assert.That(world.SimulatedFrames,Is.GreaterThan(startFrame));Assert.That(queue.effectApplied,Is.True);Assert.That(queue.resourcesSpent,Is.True);Assert.That(ticket.stage,Is.EqualTo("Resolved"));Assert.That(world.State.Actor(actor).heldBy,Is.Null);Assert.That(world.State.charges,Is.EqualTo(charges-1));
            var receipts=world.State.receipts.Where(r=>r.result.queueId==queueId&&r.result.v11!=null).ToArray();Assert.That(receipts.Length,Is.EqualTo(1),"Exactly one effect transaction must be published.");
            var effects=receipts[0].result.v11.effects;
            if(ward){Assert.That(hit,Is.True,"The ward fixture explicitly uses a successful seed.");Assert.That(effects.Count,Is.EqualTo(3));Assert.That(effects.All(e=>e.kind=="absorption"&&e.amount==3),Is.True);Assert.That(world.State.actors.All(a=>a.p6.absorptionShield==3&&a.v11.shieldRemaining>0),Is.True);}
            else{Assert.That(effects.Count,Is.EqualTo(hit?1:0));int damage=hit?6*(ticket.critical?2:1):0;Assert.That(enemyHp-world.State.enemies[0].hp,Is.EqualTo(damage));Assert.That(receipts[0].result.damage,Is.EqualTo(damage));}
            // More actual frames must not publish the same queue a second time.
            world.enabled=true;for(int i=0;i<3;i++)yield return null;world.enabled=false;
            Assert.That(world.State.receipts.Count(r=>r.result.queueId==queueId&&r.result.v11!=null),Is.EqualTo(1));Assert.That(world.State.charges,Is.EqualTo(charges-1));Assert.That(world.State.Actor(actor).heldBy,Is.Null);
        }
        [UnityTest] public IEnumerator WardSelfTargetCompletesThroughRealUpdateAndPublishesOneShieldPayment()
        {yield return RealQueue(Evan,"v11_support_ward",true);}
        [UnityTest] public IEnumerator QuickShotEnemyTargetCompletesThroughRealUpdateForItsActualRolledOutcome()
        {yield return RealQueue("rogue","v11_quick_shot",false);}
    }
}
