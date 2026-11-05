using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
namespace CombatV051.Runtime
{
    // Explicit visual fixture, never attached to an ordinary launch. No result faces are injected.
    public sealed partial class P46EvidenceFlow : MonoBehaviour
    {
        BattleWorld world; string directory; readonly List<object> checks=new List<object>();
        readonly List<string> errors=new List<string>();
        readonly List<KeyValuePair<string,Texture2D>> pendingCaptures=new List<KeyValuePair<string,Texture2D>>();
        bool p47Evidence;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] static void Attach()
        {
            var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-p46-evidence");
            bool p47=false;if(i<0){i=Array.IndexOf(args,"-p47-evidence");p47=i>=0;}
            if(i<0||i+1>=args.Length)return;
            var w=FindObjectOfType<BattleWorld>();if(w==null)return;
            var flow=w.gameObject.AddComponent<P46EvidenceFlow>();flow.world=w;flow.p47Evidence=p47;flow.directory=Path.GetFullPath(args[i+1]);flow.StartCoroutine(flow.Run());
        }
        IEnumerator Run()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Flow());Exception failure=null;
            while(stack.Count>0)
            {
                object next=null;try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}next=stack.Peek().Current;}catch(Exception e){failure=e;break;}
                if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;
            }
            if(failure!=null)errors.Add(failure.ToString());
            FlushCaptures();
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"summary.json"),CombatJson.Write(new{passed=errors.Count==0,engine=Application.unityVersion,
                utc=DateTime.UtcNow.ToString("o"),width=Screen.width,height=Screen.height,usesTestFixtures=true,humanPlayers=0,
                capture="ScreenCapture backbuffer of visible Player; scripted commands, quiet AI and explicit positions/statuses. Real domain dice and unmodified configured animation duration. Not natural victory or subjective playability.",checks,errors}));
            yield return new WaitForSecondsRealtime(.25f);Application.Quit(errors.Count==0?0:2);
        }
        IEnumerator Flow()
        {
            Require(Path.GetFullPath(world.SavePath).StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Launch with -p3-playtest-profile inside the fresh evidence directory; never use author save.");
            Require(!File.Exists(Path.Combine(directory,"summary.json")),"Evidence output already exists.");Directory.CreateDirectory(directory);
            yield return null;yield return null;
            foreach(string name in p47Evidence?new[]{"single","fast","ultra","advantage","disadvantage","natural20","natural1","budget","reaction"}:new[]{"single","advantage","disadvantage","budget","reaction"})yield return Dice(name);
            if(p47Evidence)File.WriteAllText(Path.Combine(directory,"dice-sound-timeline.json"),CombatJson.Write(world.Audio.DiceSoundTimeline));
            else yield return ImpactEvidence();
        }
        IEnumerator Dice(string name)
        {
            world.InitializeDungeonForTests(Path.Combine(directory,"Profiles",name+".json"),"p46-"+name,true);
            world.Controls.Map.Disable();world.IgnoreApplicationFocus=true;
            if(p47Evidence)world.SetDiceAnimationSpeed(name=="fast"?DiceAnimationSpeed.Fast:name=="ultra"?DiceAnimationSpeed.UltraFast:DiceAnimationSpeed.Standard);
            world.ChooseDungeonOption("close");
            var room=world.DungeonConfig.Room("R1");
            foreach(var a in world.State.actors){a.x=room.x;a.z=room.z;}
            Good(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.EnterRoom,roomId="R1",actorId=world.State.activeActorId,qualified=true}).success,"enter R1");
            if(!world.Profile.IsInEncounter)Good(world.ProfileAction(new ProfileCommand{kind=ProfileCommandKind.ActivateEncounter,roomId="R1",actorId=world.State.activeActorId,qualified=true}).success,"activate R1");
            var target=world.State.enemies.First(e=>e.Alive);var fighter=world.State.Actor("fighter");
            for(int i=0;i<world.State.actors.Count;i++)
            {var a=world.State.actors[i];a.x=target.x-3-i;a.z=target.z-i;a.majorRemaining=a.id=="fighter"?0:100;}
            foreach(var e in world.State.enemies){e.aiState=2;e.aiRemaining=120;}
            if(name=="advantage")fighter.aimReady=true;
            if(name=="disadvantage")fighter.statuses.Add(new StatusState{id="suppressed",sourceId="declared-visual-fixture",remaining=30});
            if(name=="natural20"||name=="natural1")
            {
                int desired=name=="natural20"?20:1;
                for(ulong seed=1;seed<10000;seed++){var rng=RandomStreams.Create(seed).visibleTactical;var probe=CombatJson.Clone(rng);if(RandomStreams.D20(probe)==desired){world.State.random.visibleTactical=rng;break;}}
            }
            world.SelectedTarget=target.id;world.SelectedAction="attack";
            world.Rig.Step(10);Physics.SyncTransforms();
            Good(world.Command(CommandKind.EnterFocus).success,"Focus");Good(world.SelectCommandActor("fighter").success,"select fighter");
            if(name=="budget")
            {
                float start=Time.realtimeSinceStartup,initial=world.State.focusBudget.secondsRemaining;
                while(world.State.mode==CombatMode.TacticalFocus){Require(Time.realtimeSinceStartup-start<(p47Evidence?18:10),"Budget did not expire");yield return null;}
                float activeSeconds=Time.realtimeSinceStartup-start;
                checks.Add(new{name,initial,activeSeconds,remaining=world.State.focusBudget.secondsRemaining});
                Require(activeSeconds>=(p47Evidence?14.95f:7.95f)&&activeSeconds<=(p47Evidence?15.75f:8.5f),"Budget disagrees with visible active wall-clock: "+activeSeconds);
                Require(world.State.focusBudget.secondsRemaining==0,"Expired budget must be zero");yield break;
            }
            yield return null;
            if(name=="reaction")
            {
                // Qualified high-danger fixture; only background incoming hit is fixed to isolate Reaction UI.
                for(ulong seed=1;seed<10000;seed++){var rng=RandomStreams.Create(seed).backgroundCombat;var probe=CombatJson.Clone(rng);if(RandomStreams.D20(probe)==12){world.State.random.backgroundCombat=rng;break;}}
                target.attackBonus=fighter.armorClass+1-12;
                Good(world.ExecuteWorldCommand(new CombatCommand{id="visual-reaction",kind=CommandKind.IncomingBatch,actorId=target.id,rootId="visual-reaction",qualified=true,authority=CommandAuthority.System,
                    incomingContacts=new List<IncomingContact>{new IncomingContact{targetId="fighter",damage=8,parryable=true,reactionEligibleActorIds=new List<string>{"fighter","mage"}}}}).success,"incoming Reaction");
                Require(world.State.mode==CombatMode.ReactionPrompt,"Automatic Reaction must precede damage");
                int hp=fighter.hp;world.Hud.RefreshView();yield return new WaitForEndOfFrame();CaptureVisible("reaction-prompt.png");
                Require(world.State.Actor("fighter").hp==hp&&world.Hud.ReactionCompactVisible,"Compact pending threat must not apply damage first");
                var choice=world.Session.GetReactionChoices().First(q=>q.legal&&q.actorId=="fighter"&&q.reactionId=="parry");
                Good(world.SelectReactionChoice(choice).success,"choose parry");
            }
            Good(world.ConfirmCurrentDecision().success,"ConfirmRoll");
            var ticket=CombatJson.Clone(world.State.pendingRoll);int expected=name=="advantage"||name=="disadvantage"?2:1;
            Require(ticket.rawFaces.Count==expected,"Unexpected fixture advantage state");
            if(name=="natural20"||name=="natural1")Require(ticket.selectedFace==(name=="natural20"?20:1),"Paid visible RNG did not produce the searched natural face");
            string frozen=Frozen();Vector3 camera=world.BattleCamera.transform.position;Quaternion facing=world.BattleCamera.transform.rotation;
            float budget=world.State.focusBudget.secondsRemaining;float began=Time.realtimeSinceStartup;int frames=0;bool rolling=false,landed=false;
            while(world.State.mode==CombatMode.RollFreeze)
            {
                yield return new WaitForEndOfFrame();
                if(world.State.mode!=CombatMode.RollFreeze)break;
                Require(Time.timeScale==0&&Frozen()==frozen,"World progressed during RollFreeze");
                Require(world.BattleCamera.transform.position==camera&&world.BattleCamera.transform.rotation==facing,"RollFreeze camera changed");
                Require(world.State.focusBudget.secondsRemaining==budget,"Roll consumed Focus budget");frames++;
                float progress=world.RollElapsed/world.CurrentRollDuration;
                if(!rolling&&progress>=.25f){CaptureDuringRoll(name+"-rolling.png");rolling=true;}
                if(!landed&&progress>=.86f){CaptureDuringRoll(name+"-landed.png");landed=true;}
                Require(Time.realtimeSinceStartup-began<5,"Animation failed to finish at configured duration");
            }
            float actualDuration=Time.realtimeSinceStartup-began;
            float configured=world.CurrentRollDuration;
            Require(p47Evidence?(actualDuration>=configured-.05f&&actualDuration<=configured+.25f):(actualDuration>=(expected==1?.8f:1.0f)&&actualDuration<=(expected==1?1.2f:1.4f)),"Visible animation duration outside requested bounds: "+actualDuration);
            Require(world.State.mode==CombatMode.PostRollDecision&&Time.timeScale>0&&Time.timeScale<1,"Roll must return to decision slowtime");
            Require(CombatJson.Write(ticket.rawFaces)==CombatJson.Write(world.State.pendingRoll.rawFaces),"Visual animation changed raw faces");
            // PNG compression and disk I/O are test artifacts, not animation work. Keep
            // captured pixels but defer their encoding until after the unchanged timer assertion.
            FlushCaptures();
            yield return new WaitForEndOfFrame();CaptureVisible(name+"-decision.png");
            checks.Add(new{name,ticket,frames,rolling,landed,worldFrozen=true,budgetFrozen=true,cameraStable=true,actualSeconds=actualDuration,configuredSeconds=configured});
            yield return new WaitForSecondsRealtime(.25f);if(name=="reaction")world.ConfirmCurrentDecision();else world.CancelCurrentDecision();
            yield return null;
        }
        string Frozen()=>CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.world,world.State.simulationSeconds});
        void CaptureDuringRoll(string file)
        {pendingCaptures.Add(new KeyValuePair<string,Texture2D>(file,ScreenCapture.CaptureScreenshotAsTexture()));}
        void FlushCaptures()
        {
            foreach(var capture in pendingCaptures)
                try{File.WriteAllBytes(Path.Combine(directory,capture.Key),capture.Value.EncodeToPNG());}finally{Destroy(capture.Value);}
            pendingCaptures.Clear();
        }
        void CaptureVisible(string file)
        {
            var frame=ScreenCapture.CaptureScreenshotAsTexture();
            try{File.WriteAllBytes(Path.Combine(directory,file),frame.EncodeToPNG());}finally{Destroy(frame);}
        }
        static void Good(bool okay,string operation)=>Require(okay,operation+" failed");
        static void Require(bool okay,string message){if(!okay)throw new InvalidOperationException(message);}
    }
}
