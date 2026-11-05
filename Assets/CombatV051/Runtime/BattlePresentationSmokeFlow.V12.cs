using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool V12RoomRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-v12-room")>=0;
        readonly List<object> v12ObservedCommands=new List<object>();
        readonly HashSet<string> v12ActionsDone=new HashSet<string>();
        void ObserveV12Published(CombatCommand command,CombatResult result)
        {
            if(result.success&&!result.duplicate)ObserveV14Combat();
            if(!result.success||result.duplicate||result.v12==null)return;
            foreach(var e in result.v12.effects)
                if(e.kind=="opening"||e.kind=="parasite"||e.kind=="grove"||e.kind=="direct"&&e.amount>0)v12ActionsDone.Add(e.actionId);
            v12ObservedCommands.Add(new{command=CombatJson.Clone(command),result=CombatJson.Clone(result),at=Time.realtimeSinceStartup-started,simulation=world.State.simulationSeconds});
            if(!V12FullRequested)Write("v12-commands.json",v12ObservedCommands);
        }
        IEnumerator V12Approach(float range)
        {
            float deadline=Time.realtimeSinceStartup+18;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                var e=Enemy(false);Require(e!=null,"Natural target unavailable");
                if(BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(e))<=range&&world.HasLineOfEffect(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(e)))break;
                DriveP4Combat(BattleWorld.Position(e));yield return null;
            }
            Keys();Require(world.ActiveActor.Alive,"Controlled character was defeated during natural approach");
        }
        IEnumerator V12Key(string action)
        {
            float deadline=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<deadline&&!v12ActionsDone.Contains(action))
            {
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(!world.ActiveActor.Alive)break;
                yield return V12Approach(action=="v12_mira_break"?2.15f:5.3f);
                var target=Enemy(false);if(target==null)break;
                AimAt(BattleWorld.Position(target));yield return null;yield return Press(Key.Q);
                if(world.State.mode!=CombatMode.TacticalFocus){yield return new WaitForSecondsRealtime(.3f);continue;}
                // Real battlefield click both selects the target and establishes a ground point for Grove.
                yield return ClickWorld(BattleWorld.Position(target));
                if(!world.SelectedActionQuote().legal||world.State.charges<=0){yield return P6Click("exit");yield return new WaitForSecondsRealtime(.3f);continue;}
                yield return P6Click("confirm");
                if(world.State.mode==CombatMode.RollFreeze)
                {
                    string stable=CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.v12,world.State.focusBudget,world.State.simulationSeconds});
                    var position=world.BattleCamera.transform.position;var rotation=world.BattleCamera.transform.rotation;
                    yield return CaptureP47("v12-"+action+"-dice.png");
                    while(world.State.mode==CombatMode.RollFreeze)
                    {
                        Require(Time.timeScale==0&&stable==CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.v12,world.State.focusBudget,world.State.simulationSeconds}),"V12 roll advanced world/status/Focus");
                        Require(position==world.BattleCamera.transform.position&&rotation==world.BattleCamera.transform.rotation,"V12 roll camera changed");
                        freezeFrames.Add(new{phase="V12-A",action,frame=Time.frameCount,simulation=world.State.simulationSeconds,scale=Time.timeScale});yield return null;
                    }
                    if(world.State.mode==CombatMode.PostRollDecision)yield return P6Click("commit");
                }
                if(world.State.mode==CombatMode.TacticalFocus)yield return P6Click("exit");
                yield return new WaitForSecondsRealtime(1.2f);
            }
            Require(v12ActionsDone.Contains(action),"Natural V12 action did not produce a real effect: "+action);
        }
        IEnumerator FlowV12Room()
        {
            yield return null;yield return null;
            world.InitializeP6FrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            yield return FrontP47("characters");
            foreach(var id in new[]{"char_story_evan","char_story_mira","char_story_finn"})
            {yield return FrontP47("role:"+id);yield return FrontP47("node:"+id+"/passive");yield return FrontP47("node-confirm");yield return FrontP47("characters");}
            yield return FrontP47("main");yield return FrontP47("campaign");yield return FrontP47("training-main");
            Require(world.V12Enabled&&world.MainCharacterTraining,"V12 arena rules not installed");
            world.CommandPublished+=ObserveV12Published;
            for(int i=0;i<3;i++)
            {
                string id=new[]{"char_story_evan","char_story_mira","char_story_finn"}[i];stage="v12-room-"+id;
                if(world.State.mode==CombatMode.ReactionPrompt)yield return P6Click("reaction-decline");
                yield return Press((Key)((int)Key.Digit1+i));
                Require(world.State.activeActorId==id,"Actual character switching failed");
                yield return V12Approach(i==1?4.7f:5.5f);
                AimAt(BattleWorld.Position(Enemy()));yield return null;yield return Press(Key.F);
                float until=Time.realtimeSinceStartup+12;
                string basic=BattleWorld.V12BasicAction(id);
                while(Time.realtimeSinceStartup<until&&!v12ActionsDone.Contains(basic))
                {
                    if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                    yield return V12Approach(i==1?3.8f:5.5f);
                    if(world.State.Actor(id).v12.basicCooldown<=0){AimAt(BattleWorld.Position(Enemy()));yield return null;yield return Press(Key.F);}
                    yield return null;
                }
                Require(v12ActionsDone.Contains(basic),"Natural free action failed: "+basic);
                yield return V12Key(BattleWorld.V12KeyAction(id));
                yield return CaptureP47("v12-"+id+"-battle.png");
            }
            yield return new WaitForSecondsRealtime(6.5f);
            Write("v12-room-final.json",new{actions=v12ActionsDone,combat=world.State,commands=v12ObservedCommands,usesInjectedResources=false,usesFixedSuccessDice=false,scope="Existing isolated arena, real input and natural RNG; not six-room or human fun acceptance."});
            Require(v12ActionsDone.Count>=6,"Not all six real actions observed");
            yield return P6Click("p6-finish");Require(world.Menu&&!world.Training,"Training did not restore original menu");
            world.CommandPublished-=ObserveV12Published;completed=true;stage="v12-room-complete";
        }
    }
}
