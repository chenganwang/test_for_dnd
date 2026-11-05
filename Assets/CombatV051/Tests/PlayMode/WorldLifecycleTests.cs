using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    public sealed class WorldLifecycleTests
    {
        BattleWorld world;
        string directory, path;
        [UnitySetUp] public IEnumerator Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"V051-Lifecycle",Guid.NewGuid().ToString("N"));
            path=Path.Combine(directory,"profile.json");
            world=new GameObject("P0 lifecycle scene").AddComponent<BattleWorld>();
            world.InitializeForTests(path);
            world.Controls.Map.Disable(); // Lifecycle fixture; input is verified by the dedicated queued-device tests.
            world.State.enemies[0].aiRemaining=50;
            yield return null;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);
            yield return null;
            // Evidence snapshots remain in the task-specific temp folder for diagnosis.
            Time.timeScale=1;
        }
        [UnityTest] public IEnumerator NewAttemptArchivesOldAuthorityAndCanReallySaveAgain()
        {
            string oldId=world.State.modeInstanceId;
            Assert.That(world.Session.Save().success,Is.True);
            world.StartNew();
            Assert.That(world.State.modeInstanceId,Is.Not.EqualTo(oldId));
            Assert.That(Directory.GetFiles(directory,"profile.json.previous-*").Length,Is.EqualTo(1));
            Assert.That(CombatSession.Load(path,world.Session.Config).State.modeInstanceId,Is.EqualTo(world.State.modeInstanceId));
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True,world.Message);
            yield return null;
        }
        [UnityTest] public IEnumerator FailedConfirmPersistenceNeverEntersAnimationOrConsumesCharge()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            int charge=world.State.charges;
            ulong draws=world.State.random.visibleTactical.drawCount;
            world.Session.Store.WriteFault=stage=>{if(stage==CombatWriteStage.BeforeReplace)throw new IOException("injected full disk");};
            var rejected=world.Command(CommandKind.ConfirmRoll);
            Assert.That(rejected.success,Is.False);
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.TacticalFocus));
            Assert.That(world.State.charges,Is.EqualTo(charge));
            Assert.That(world.State.random.visibleTactical.drawCount,Is.EqualTo(draws));
            Assert.That(world.State.pendingRoll,Is.Null);
            Assert.That(world.ActiveActor.heldBy,Is.Null.Or.Empty);
            Assert.That(Time.timeScale,Is.EqualTo(world.Session.Config.focusScale));
            world.Session.Store.WriteFault=null;
            yield return null;
        }
        [UnityTest] public IEnumerator CancelInLiveWorldRetainsPaidCostsAndRestoresNoActionCooldown()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True);
            float deadline=Time.realtimeSinceStartup+3;
            while(world.State.mode==CombatMode.RollFreeze&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(world.Command(CommandKind.Modify).success,Is.True);
            int charge=world.State.charges,inspiration=world.ActiveActor.inspiration,spells=world.ActiveActor.spellSlots;
            string ticket=world.State.pendingRoll.id;
            Assert.That(world.Command(CommandKind.Cancel).success,Is.True);
            Assert.That(world.ActiveActor.majorRemaining,Is.Zero);
            Assert.That(world.ActiveActor.spellSlots,Is.EqualTo(spells));
            Assert.That(world.State.charges,Is.EqualTo(charge));
            Assert.That(world.ActiveActor.inspiration,Is.EqualTo(inspiration));
            Assert.That(world.State.queue,Is.Empty);
            world.Suspend();world.ContinueSaved();
            Assert.That(world.State.rolls.Single(r=>r.id==ticket).stage,Is.EqualTo("Canceled"));
            Assert.That(world.State.charges,Is.EqualTo(charge));
            Assert.That(world.ActiveActor.inspiration,Is.EqualTo(inspiration));
        }
        [UnityTest] public IEnumerator QueuedActorCannotGainStationaryDodgeButCanMoveAfterExplicitCancel()
        {
            Assert.That(world.Command(CommandKind.EnterFocus).success,Is.True);
            Assert.That(world.Command(CommandKind.ConfirmRoll).success,Is.True);
            world.AdvanceRollAnimation(world.CurrentRollDuration); // Timing is covered separately; this fixture isolates action ownership.
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));
            var committed=world.Command(CommandKind.Commit);
            Assert.That(committed.success,Is.True,committed.reason);
            Assert.That(world.Command(CommandKind.ExitFocus).success,Is.True);
            var before=BattleWorld.Position(world.ActiveActor);
            float oldX=world.ActiveActor.lockedX,oldZ=world.ActiveActor.lockedZ;
            Assert.That(world.Command(CommandKind.StartDodge, direction:Vector3.right).success,Is.False);
            Assert.That(world.ActiveActor.dodgeRemaining,Is.Zero);
            Assert.That(world.ActiveActor.dodgeCooldown,Is.Zero);
            Assert.That(world.ActiveActor.lockedX,Is.EqualTo(oldX));
            Assert.That(world.ActiveActor.lockedZ,Is.EqualTo(oldZ));
            int hp=world.ActiveActor.hp;
            var damage=world.Session.ResolveIncoming("fixture-guaranteed-save-failure","enemy-1","fighter",3,true,100,"fixture-risk");
            Assert.That(damage.success,Is.True);
            Assert.That(world.ActiveActor.hp,Is.EqualTo(hp-3));
            Assert.That(world.Command(CommandKind.CancelQueued, queue:committed.queueId).success,Is.True);
            Assert.That(world.Command(CommandKind.StartDodge, direction:Vector3.right).success,Is.True);
            yield return null;yield return null;
            Assert.That(BattleWorld.Position(world.ActiveActor).x,Is.GreaterThan(before.x));
        }
        [UnityTest] public IEnumerator SlowFinalDodgeFrameMovesOnlyItsRemainingIntervalInsteadOfLosingTheWholeDodge()
        {
            Assert.That(world.Command(CommandKind.StartDodge,direction:Vector3.right).success,Is.True);
            float before=world.ActiveActor.x,window=world.ActiveActor.dodgeRemaining;
            world.SendMessage("CaptureDodgeMotionWindow",SendMessageOptions.RequireReceiver);
            float dt=world.Session.Tick(window+.2f); // Deterministic slow-frame boundary; not an FPS measurement.
            Assert.That(world.ActiveActor.dodgeRemaining,Is.Zero,"No extra invulnerability after expiry.");
            world.SendMessage("MovePlayer",dt,SendMessageOptions.RequireReceiver);
            Assert.That(world.ActiveActor.x-before,Is.EqualTo(window*world.Presentation.dodgeSpeed).Within(.001f));
            Assert.That(world.ActiveActor.dodgeCooldown,Is.GreaterThan(0),"Motion cannot refund the dodge cooldown.");
            yield return null;
        }
    }
}
