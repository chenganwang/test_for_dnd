using System;
using System.Collections;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    // Explicit engine fixtures: the existing isolated training arena is given
    // the registered R4 device at local coordinates. Actor positions and AI
    // holds isolate the hazard contract; these are not natural-play evidence.
    public sealed class V12EnvironmentPlayTests
    {
        const string Evan=CombatSession.V12Evan,Finn=CombatSession.V12Finn;
        BattleWorld world;Keyboard keyboard;Mouse mouse;string directory;
        InputSettings.BackgroundBehavior background;
        ActorState Enemy=>world.State.enemies[0];
        Transform Beam=>world.transform.Find("V12 R4 single-use beam/Hanging beam - visual only");
        static void Place(ActorState a,float x,float z){a.x=x;a.z=z;a.heightY=0;}
        static void Good(CombatResult r)=>Assert.That(r.success,Is.True,r.reason);

        [UnitySetUp] public IEnumerator Setup()
        {
            background=InputSystem.settings.backgroundBehavior;InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            directory=Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Beam-Engine",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            world=new GameObject("V12 registered environment engine fixture").AddComponent<BattleWorld>();
            world.InitializeP6FrontendForTests(Path.Combine(directory,"profile.json"));world.StartMainCharacterTraining();world.enabled=false;
            Assert.That(world.MainCharacterTraining&&world.V12Enabled,Is.True);
            if(world.V12FreeAssistance)world.ToggleV12FreeAssistance();
            world.Session.Config.v12.beam=new V12BeamConfig{triggerX=-4,triggerZ=-4,x=-1,z=-4,radius=1.3f};
            for(int i=0;i<world.State.actors.Count;i++)
            {var a=world.State.actors[i];Place(a,-8+i,-8);a.hp=a.maxHp=100;a.majorRemaining=100;a.staggerRemaining=100;a.windupRemaining=0;a.basicAttackIntent=null;}
            var active=world.State.Actor(Evan);Place(active,-4,-4);active.majorRemaining=active.staggerRemaining=0;
            Place(world.State.Actor(Finn),-1,-3.5f);
            for(int i=0;i<world.State.enemies.Count;i++)
            {var a=world.State.enemies[i];Place(a,-1+i*4,-4);a.hp=a.maxHp=100;a.aiState=2;a.aiRemaining=100;a.lockedX=a.x+1;a.lockedZ=a.z;}
            CombatSession.RegisterV12Beam(world.State,world.Session.Config,world.State.modeInstanceId+"/engine-r4/beam");
            Good(world.SwitchActive(Evan));world.SelectedActor=Evan;world.SelectedTarget=Enemy.id;world.SelectedAction="v12_evan_final";
            world.ConfigureDiceAnimationSpeed(DiceAnimationSpeed.Standard);world.Rig.Step(10);Physics.SyncTransforms();world.enabled=true;
            yield return null;Assert.That(Beam,Is.Not.Null);Assert.That(world.V12BeamAvailable,Is.True);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(world)UnityEngine.Object.Destroy(world.gameObject);yield return null;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior=background;Time.timeScale=1;
            string allowed=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"ActionCRPG-V12-Beam-Engine"))+Path.DirectorySeparatorChar;
            if(directory!=null&&Path.GetFullPath(directory).StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(directory))Directory.Delete(directory,true);
        }
        IEnumerator Until(Func<bool> done,float limit,string reason)
        {var timer=System.Diagnostics.Stopwatch.StartNew();while(!done()&&timer.Elapsed.TotalSeconds<limit)yield return null;Assert.That(done(),Is.True,reason+"; "+world.Message);}

        [UnityTest] public IEnumerator RuntimeWarningFallsAndAppliesEnemyAndFriendlyDamageExactlyOnce()
        {
            Assert.That(world.TryV12BeamInteraction(),Is.True,world.Message);float started=world.State.simulationSeconds;
            Assert.That(world.State.v12.beam.stage,Is.EqualTo("Warning"));Assert.That(Enemy.hp,Is.EqualTo(100));
            Assert.That(Beam.position.y,Is.EqualTo(3.4f).Within(.001f));
            var preview=world.transform.Find("V12 R4 single-use beam/Beam danger preview").GetComponent<AreaPreview>();
            Assert.That(preview.gameObject.activeInHierarchy,Is.True);Assert.That(preview.Shape.Radius,Is.EqualTo(1.3f));
            int geometryBuilds=preview.GeometryBuildCount;bool observedWarning=false;var timeout=System.Diagnostics.Stopwatch.StartNew();
            while(world.Session.V12BeamPending&&timeout.Elapsed.TotalSeconds<4)
            {
                Assert.That(Enemy.hp,Is.EqualTo(100));Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(100));
                if(world.State.simulationSeconds-started<.8f)observedWarning=true;
                yield return null;
            }
            Assert.That(world.Session.V12BeamPending,Is.False,"Runtime must dispatch the armed resolution");Assert.That(observedWarning,Is.True);Assert.That(world.State.simulationSeconds-started,Is.GreaterThanOrEqualTo(.8f));
            Assert.That(Enemy.hp,Is.EqualTo(88));Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(88));
            Assert.That(Beam.position.y,Is.EqualTo(.18f).Within(.001f));Assert.That(preview.gameObject.activeInHierarchy,Is.False);
            Assert.That(preview.GeometryBuildCount,Is.EqualTo(geometryBuilds),"Timer updates must reuse the cached outline.");
            int events=world.State.v12.events.Count(e=>e.kind=="environment");Assert.That(events,Is.EqualTo(2));
            Assert.That(world.TryV12BeamInteraction(),Is.False);yield return new WaitForSecondsRealtime(.25f);
            Assert.That(Enemy.hp,Is.EqualTo(88));Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(88));
            Assert.That(world.State.v12.events.Count(e=>e.kind=="environment"),Is.EqualTo(events));LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator NaturalTwoSecondRollFreezesWarningClockAndBeamTransform()
        {
            Assert.That(world.TryV12BeamInteraction(),Is.True);Good(world.Command(CommandKind.EnterFocus));Good(world.ConfirmCurrentDecision());
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.RollFreeze));Assert.That(world.CurrentRollDuration,Is.EqualTo(2).Within(.001f));
            float simulation=world.State.simulationSeconds,remaining=world.Session.V12BeamRemaining;Vector3 position=Beam.position;
            Quaternion rotation=Beam.rotation;int face=world.State.pendingRoll.selectedFace,frames=0;var timer=System.Diagnostics.Stopwatch.StartNew();
            while(world.State.mode==CombatMode.RollFreeze&&timer.Elapsed.TotalSeconds<5)
            {
                Assert.That(Time.timeScale,Is.Zero);Assert.That(world.State.simulationSeconds,Is.EqualTo(simulation));
                Assert.That(world.Session.V12BeamRemaining,Is.EqualTo(remaining));Assert.That(Beam.position,Is.EqualTo(position));Assert.That(Beam.rotation,Is.EqualTo(rotation));
                Assert.That(Enemy.hp,Is.EqualTo(100));Assert.That(world.State.pendingRoll.selectedFace,Is.EqualTo(face));frames++;yield return null;
            }
            Assert.That(frames,Is.GreaterThan(1));Assert.That(timer.Elapsed.TotalSeconds,Is.GreaterThan(1.7));
            Assert.That(world.State.mode,Is.EqualTo(CombatMode.PostRollDecision));Assert.That(world.Session.V12BeamPending,Is.True);
            Assert.That(world.State.simulationSeconds,Is.EqualTo(simulation));Assert.That(Time.timeScale,Is.GreaterThan(0).And.LessThan(1));
            Good(world.Command(CommandKind.Cancel));Good(world.Command(CommandKind.ExitFocus));
            yield return Until(()=>!world.Session.V12BeamPending,4,"Same armed beam resumes after the natural reveal");
            Assert.That(Enemy.hp,Is.EqualTo(88));Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(88));LogAssert.NoUnexpectedReceived();
        }

        [UnityTest] public IEnumerator LastEnemyDeathCannotEndEncounterBeforeArmedFriendlyHazardSettles()
        {
            int resolved=0,ended=0;bool settledBeforeEnd=false;int impactHp=0;string instance=world.State.v12.beam.id;
            Action<CombatCommand,CombatResult> observation=(c,r)=>{
                if(!r.success||r.duplicate)return;
                if(c.kind==CommandKind.ResolveV12Beam){resolved++;impactHp=world.State.Actor(Finn).hp;settledBeforeEnd=!world.State.encounterResolved&&world.State.v12.beam.stage=="Resolved";}
                if(c.kind==CommandKind.EndEncounter){ended++;Assert.That(resolved,Is.EqualTo(1),"Environment transaction precedes encounter cleanup");}
            };
            world.CommandPublished+=observation;
            Assert.That(world.TryV12BeamInteraction(),Is.True);foreach(var enemy in world.State.enemies)enemy.hp=0;
            // Explicit death fixture: this checks lifecycle ordering, not attack RNG or victory balance.
            yield return null;Assert.That(world.Session.V12BeamPending,Is.True);Assert.That(world.State.inCombat,Is.True);
            Assert.That(world.State.encounterResolved,Is.False);Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(100));
            yield return Until(()=>world.State.encounterResolved,4,"EndEncounter waits for the already-armed hazard");
            world.CommandPublished-=observation;
            Assert.That(world.Session.V12BeamPending,Is.False);Assert.That(world.State.Actor(Finn).hp,Is.EqualTo(88));
            Assert.That(resolved,Is.EqualTo(1));Assert.That(ended,Is.EqualTo(1));Assert.That(settledBeforeEnd,Is.True);Assert.That(impactHp,Is.EqualTo(88));
            Assert.That(world.State.world.strings.ContainsKey("v12/beamSpent/"+instance),Is.True,"Durable consumption survives encounter timer cleanup");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
