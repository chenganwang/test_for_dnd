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
    // Opt-in same executable verification. Navigation, attacks, purchases and saves
    // all use the shipping input/UI. No gameplay position/HP/resource/RNG assignments.
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool V12FullRequested=>V14RouteRequested||Array.IndexOf(Environment.GetCommandLineArgs(),"-v12-route")>=0;
        bool V12RewardsRequested=>V14RouteRequested?Array.IndexOf(Environment.GetCommandLineArgs(),"-v14-decline-route")<0:Array.IndexOf(Environment.GetCommandLineArgs(),"-v12-rewards")>=0;
        string V12RequestedForm
        {
            get {var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-v12-form");return i>=0&&i+1<args.Length?args[i+1]:null;}
        }
        string V12RequestedCore
        {
            get {if(V14RouteRequested)return V14Argument("-v14-core","char_story_evan");var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-v12-core");return i>=0&&i+1<args.Length?args[i+1]:"char_story_evan";}
        }
        bool v12FullRoutePassed,v12DiskContinuePassed,v12SettlementPassed;
        readonly List<object> v12RoomEvidence=new List<object>();
        readonly List<string> v12ChosenRewards=new List<string>();
        readonly HashSet<string> v12RoomsVisited=new HashSet<string>();
        Vector3 v12RetreatPoint;float v12NextRetreat;bool v12RetreatAvailable;
        bool v12KeyOpened;
        bool V12RealtimeInputReady=>!world.IsFrozen&&!world.IsFocus&&(world.ImpactFeedback==null||!world.ImpactFeedback.Holding)&&
            (world.State.mode==CombatMode.RealtimeCombat||world.State.mode==CombatMode.QueuedExecution);

        bool V12DodgeKeys(ActorState threat,out Key[] keys)
        {
            keys=null;var position=BattleWorld.Position(world.ActiveActor);
            var away=BattleGeometry.Ground(position-BattleWorld.Position(threat)).normalized;
            float travel=world.Presentation.dodgeSpeed*world.Session.Config.dodgeSeconds;
            var axes=new[]{Vector2.up,Vector2.down,Vector2.left,Vector2.right,new Vector2(1,1),new Vector2(1,-1),new Vector2(-1,1),new Vector2(-1,-1)};
            foreach(var axis in axes.OrderByDescending(a=>Vector3.Dot(world.Rig.ScreenMove(a),away)))
            {
                var direction=world.Rig.ScreenMove(axis);var destination=position+direction*travel;
                if(world.DungeonMap.RoomAt(destination)!=world.Profile.ActiveRoom.templateId)continue;
                if(world.State.enemies.Any(e=>e.Alive&&e.aiState==1&&BattleGeometry.Contains(world.EnemyShape(e),destination)))continue;
                // Read the same shipping wall sweep; the position below is only a prediction.
                var swept=BattleGeometry.MoveAgainstWalls(position,destination-position,.36f,1<<8);
                if(BattleGeometry.DistanceXZ(swept,destination)>.08f)continue;
                var input=new List<Key>{Key.Space};
                if(axis.x>0)input.Add(Key.D);else if(axis.x<0)input.Add(Key.A);
                if(axis.y>0)input.Add(Key.W);else if(axis.y<0)input.Add(Key.S);
                keys=input.ToArray();return true;
            }
            return false;
        }

        bool V12KeepRangedDistance(Vector3 position,Vector3 target)
        {
            if(Time.realtimeSinceStartup>=v12NextRetreat)
            {
                v12NextRetreat=Time.realtimeSinceStartup+.75f;v12RetreatAvailable=false;
                var away=BattleGeometry.Ground(position-target).normalized;if(away.sqrMagnitude<.01f)away=Vector3.left;
                foreach(float angle in new[]{0f,45f,-45f,90f,-90f,135f,-135f})
                {
                    var point=target+Quaternion.Euler(0,angle,0)*away*4.8f;
                    if(world.DungeonMap.RoomAt(point)!=world.Profile.ActiveRoom.templateId||!world.HasLineOfEffect(point,target))continue;
                    if(!world.TryOrderPath(position,point,out var path)||path==null)continue;
                    v12RetreatPoint=point;v12RetreatAvailable=true;break;
                }
            }
            if(!v12RetreatAvailable)return false;
            DriveP4Combat(v12RetreatPoint);return true;
        }

        IEnumerator V12GroundClick(Vector3 point)
        {
            var screen=world.BattleCamera.WorldToScreenPoint(point);pointer=new Vector2(screen.x,screen.y);
            Require(screen.z>0&&pointer.x>2&&pointer.y>2&&pointer.x<Screen.width-2&&pointer.y<Screen.height-2&&!OverUi(pointer),"Ground target obscured by actual UI: "+point);
            MouseState(0);yield return null;yield return null;MouseState(1);yield return null;yield return null;MouseState(0);yield return null;
        }
        IEnumerator V12UseKey(ActorState enemy)
        {
            v12KeyOpened=false;if(!V12RealtimeInputReady)yield break;
            string id=enemy.id;
            if(world.V13Enabled){if(!V13TryAimOrRecover(BattleWorld.Position(enemy))){yield return null;yield break;}}
            else AimAt(BattleWorld.Position(enemy));
            yield return Press(Key.Q);
            if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            v12KeyOpened=true;
            enemy=world.State.Actor(id);if(enemy?.Alive!=true){yield return P6Click("exit");yield break;}
            if(world.ActiveActor.id!="char_story_finn")
            {
                int cycle=world.State.enemies.Count+1;
                while(world.State.mode==CombatMode.TacticalFocus&&world.SelectedTarget!=id&&cycle-->0)yield return P6Click("target");
            }
            else
            {
                bool visible=false;Vector3 point=BattleWorld.Position(enemy);var candidates=new List<Vector3>();
                // Ordinary ground targeting: cover a living ally and enemy when both fit.
                // A centre on a distant enemy was wasting every Grove healing pulse.
                float diameter=world.Session.Config.v12.groveRadius*2-.3f;
                foreach(var ally in world.State.actors.Where(a=>a.Alive).OrderBy(a=>(float)a.hp/a.maxHp))
                    if(BattleGeometry.DistanceXZ(BattleWorld.Position(ally),point)<=diameter)
                        candidates.Add((BattleWorld.Position(ally)+point)*.5f);
                foreach(var offset in new[]{Vector3.zero,Vector3.left*1.5f,Vector3.right*1.5f,Vector3.forward*1.5f,Vector3.back*1.5f})candidates.Add(point+offset);
                foreach(var candidate in candidates)
                {
                    var screen=world.BattleCamera.WorldToScreenPoint(candidate);var p=new Vector2(screen.x,screen.y);
                    if(screen.z<=0||p.x<4||p.y<4||p.x>Screen.width-4||p.y>Screen.height-4||OverUi(p))continue;
                    if(BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),candidate)>6||!world.HasLineOfEffect(BattleWorld.Position(world.ActiveActor),candidate))continue;
                    point=candidate;visible=true;break;
                }
                if(!visible){yield return P6Click("exit");yield break;}
                p6Inputs.Add(new{kind="v12_grove_ground_input",actor=world.ActiveActor.id,target=id,x=point.x,z=point.z,radius=world.Session.Config.v12.groveRadius,
                    allies=world.State.actors.Where(a=>a.Alive).Select(a=>new{a.id,a.hp,x=a.x,z=a.z,distance=BattleGeometry.DistanceXZ(BattleWorld.Position(a),point)}).ToArray()});
                yield return V12GroundClick(point);
            }
            if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            if(!world.SelectedActionQuote().legal||world.Session.AvailableSharedCharges<=0){yield return P6Click("exit");yield break;}
            yield return P6Click("confirm");
            if(world.State.mode==CombatMode.RollFreeze)
            {
                float sim=world.State.simulationSeconds,focus=world.State.focusBudget.secondsRemaining;
                var camera=world.BattleCamera.transform.position;var rotation=world.BattleCamera.transform.rotation;
                string timerState=CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.v12});
                var ticket=world.State.pendingRoll;string faces=CombatJson.Write(ticket.rawFaces);
                if(world.State.rolls.Count<=2)yield return CaptureP47("route-dice-"+world.Profile.ActiveRoom.templateId+"-"+world.State.rolls.Count+".png");
                while(world.State.mode==CombatMode.RollFreeze)
                {
                    Require(Time.timeScale==0&&world.State.simulationSeconds==sim&&world.State.focusBudget.secondsRemaining==focus,"Full-route RollFreeze advanced world/Focus");
                    Require(camera==world.BattleCamera.transform.position&&rotation==world.BattleCamera.transform.rotation,"RollFreeze camera moved");
                    yield return null;
                }
                Require(CombatJson.Write(world.State.rolls.First(t=>t.id==ticket.id).rawFaces)==faces,"Natural faces changed after animation");
                // Character/status timers must remain equal at the first reveal frame.
                Require(timerState==CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.v12}),"RollFreeze changed combat DTOs");
                Write("route-last-roll.json",new{ticket=world.State.rolls.First(t=>t.id==ticket.id),worldScale=Time.timeScale,sim,focus});
            }
            if(world.State.mode==CombatMode.PostRollDecision)
            {
                // Alternate ordinary and full Paladin hits using actual optional payment.
                bool full=world.State.pendingRoll?.p6!=null&&world.State.pendingRoll.hit&&world.Session.AvailableSharedCharges>0&&world.State.rolls.Count%2==0;
                yield return P6Click(full?"p6-full":"commit");
            }
            if(world.State.mode==CombatMode.TacticalFocus)yield return P6Click("exit");
        }
        IEnumerator V12FightRoom(string room)
        {
            if(world.V13Enabled){yield return V13FightRoom(room);yield break;}
            float deadline=Time.realtimeSinceStartup+200,nextKey=0,nextFree=0,nextSwitch=Time.realtimeSinceStartup+18;string encounter=world.Profile.ActiveEncounter?.id;
            while(world.Profile.IsInEncounter&&world.Profile.ActiveEncounter.id==encounter&&Time.realtimeSinceStartup<deadline)
            {
                if(world.DungeonModal)break;
                var s=world.State;
                if(s.mode==CombatMode.ReactionPrompt)
                {
                    Keys();var legal=world.Session.GetReactionChoices().Where(q=>q.legal).ToList();
                    if(s.activeReaction==null&&legal.Count>0)yield return P6Click("reaction-choice-0");
                    if(world.State.mode==CombatMode.ReactionPrompt)yield return P6Click(world.State.activeReaction!=null?"reaction-confirm":"reaction-decline");
                    continue;
                }
                if(s.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(s.mode==CombatMode.PostRollDecision){yield return P6Click(s.pendingRoll?.purpose=="Reaction"?"reaction-commit":"commit");continue;}
                if(s.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                if(!V12RealtimeInputReady){Keys();MouseState(0);yield return null;continue;}
                if(!world.ActiveActor.Alive)
                {int living=s.actors.FindIndex(a=>a.Alive);Require(living>=0,"Natural six-room party defeat in "+room);yield return Press((Key)((int)Key.Digit1+living));continue;}
                if(V12RequestedCore!=P6Paladin&&Time.realtimeSinceStartup>=nextSwitch&&s.mode==CombatMode.RealtimeCombat)
                {
                    var active=world.ActiveActor;
                    if(active.windupRemaining<=0&&!CombatSession.V12ActionActive(active)&&string.IsNullOrEmpty(active.heldBy))
                    {
                        int index=s.actors.FindIndex(a=>a.id==active.id);
                        string requested=null;
                        for(int step=1;step<s.actors.Count;step++)
                        {int next=(index+step)%s.actors.Count;if(!s.actors[next].Alive)continue;requested=s.actors[next].id;yield return Press((Key)((int)Key.Digit1+next));break;}
                        if(requested==null||world.State.activeActorId==requested)
                        {
                            nextSwitch=Time.realtimeSinceStartup+18;
                            p4CombatPath=null;v12RetreatAvailable=false;v12NextRetreat=0;
                        }
                        else p6Inputs.Add(new{kind="v12_switch_input_interrupted",requested,actual=world.State.activeActorId,mode=world.State.mode.ToString()});
                        continue;
                    }
                }
                var enemy=Enemy(false);if(enemy==null){yield return null;continue;}
                var actor=world.ActiveActor;var position=BattleWorld.Position(actor);var target=BattleWorld.Position(enemy);
                bool free=string.IsNullOrEmpty(actor.heldBy)&&actor.staggerRemaining<=0&&actor.guardRemaining<=0&&actor.dodgeRemaining<=0&&actor.windupRemaining<=0&&!CombatSession.V12ActionActive(actor);
                var threat=s.enemies.FirstOrDefault(e=>e.Alive&&e.aiState==1&&e.aiRemaining<.3f&&BattleGeometry.Contains(world.EnemyShape(e),position));
                bool imminent=threat!=null;
                if(imminent&&free&&actor.bonusRemaining<=0)
                {
                    string guardActor=actor.id;
                    Keys();AimAt(BattleWorld.Position(threat));MouseState(2);yield return null;yield return null;
                    string threatId=threat.id;float guardDeadline=Time.realtimeSinceStartup+2;
                    while(world.State.activeActorId==guardActor&&world.State.Actor(guardActor)?.guardRemaining>0&&Time.realtimeSinceStartup<guardDeadline&&world.State.Actor(threatId)?.Alive==true&&world.State.Actor(threatId).aiState==1&&world.State.mode!=CombatMode.ReactionPrompt)yield return null;
                    MouseState(0);yield return null;continue;
                }
                if(imminent&&free&&actor.dodgeCooldown<=0&&V12DodgeKeys(threat,out var dodgeKeys))
                {Keys(dodgeKeys);yield return null;yield return null;Keys();yield return null;continue;}
                float range=actor.id=="char_story_mira"||actor.id==P6Paladin?2.05f:actor.id=="char_story_finn"?4.6f:5.3f;
                bool sight=world.HasLineOfEffect(position,target);float distance=BattleGeometry.DistanceXZ(position,target);
                if(free&&range>3&&distance<3.3f&&V12KeepRangedDistance(position,target)){yield return null;continue;}
                if(free&&Time.realtimeSinceStartup>=nextFree&&sight&&distance<=7)
                {
                    // F intentionally keeps a live selected target in the shipping input path.
                    // When none survives, the shipping F handler selects nearest to the aimed enemy.
                    // Predict that choice without assigning SelectedTarget or any gameplay state.
                    var freeTarget=world.State.Actor(world.SelectedTarget);bool freeLegal=false;
                    if(freeTarget?.Alive!=true||!freeTarget.isEnemy)freeTarget=enemy;
                    if(freeTarget?.Alive==true&&freeTarget.isEnemy)
                    {
                        if(actor.id==P6Paladin&&world.RadianceCaster?.Alive==true)
                            freeLegal=world.HasLineOfEffect(BattleWorld.Position(world.RadianceCaster),BattleWorld.Position(freeTarget))&&world.Session.QuoteBind(world.RadianceCaster.id,freeTarget.id).legal;
                        else if(BattleWorld.V12Hero(actor.id)&&actor.v12.basicCooldown<=0)
                            freeLegal=world.HasLineOfEffect(position,BattleWorld.Position(freeTarget))&&world.Session.Quote(actor.id,freeTarget.id,BattleWorld.V12BasicAction(actor.id)).legal;
                    }
                    if(freeLegal)
                    {nextFree=Time.realtimeSinceStartup+.6f;Keys();AimAt(BattleWorld.Position(freeTarget));yield return Press(Key.F);continue;}
                }
                if(distance>range||!sight){DriveP4Combat(target);yield return null;continue;}
                if(free&&Time.realtimeSinceStartup>=nextKey&&s.charges>0&&s.focusBudget.secondsRemaining>3&&world.Session.P6MajorAvailable(actor))
                {Keys();yield return V12UseKey(enemy);nextKey=Time.realtimeSinceStartup+(v12KeyOpened?2.5f:.1f);continue;}
                float basicRange=world.Session.Config.BasicFor(actor).range;
                if(distance>basicRange-.12f){DriveP4Combat(target);yield return null;continue;}
                Keys();if(free&&actor.majorRemaining<=0)yield return ClickWorld(target);else yield return null;
            }
            Keys();MouseState(0);
            Require(world.Profile.ActiveMode.Room(room).resolved,"Natural encounter failed/timeout: "+room+" / "+world.Message);
            v12RoomEvidence.Add(new{room,realSeconds=Time.realtimeSinceStartup-started,encounters=CombatJson.Clone(world.Profile.ActiveMode.encounters),mode=CombatJson.Clone(world.Profile.ActiveMode)});
            Write("v12-room-evidence.json",v12RoomEvidence);
            Write("v12-commands.json",v12ObservedCommands);
        }
        IEnumerator V12Reward()
        {
            if(world.Profile.PendingReward==null)yield break;
            yield return CaptureP47("reward-"+world.Profile.ActiveRoom.templateId+".png");
            string choice=V12RewardsRequested?world.DungeonChoices.FirstOrDefault(c=>c.id.StartsWith("reward:")&&c.enabled)?.id:null;
            if(V12RewardsRequested&&!string.IsNullOrEmpty(V12RequestedForm))
            {
                string room=world.Profile.ActiveRoom.templateId;
                string expected=room=="V12_R1"?"reward:"+V12RequestedForm:room=="V12_R4"?"reward:"+V12RequestedForm+"_up":null;
                if(expected!=null)
                {
                    Require(world.DungeonChoices.Any(c=>c.id==expected&&c.enabled),"Requested form/upgrade is not a genuinely offered legal choice: "+expected);
                    choice=expected;
                }
            }
            if(choice==null)choice="skip-reward";
            yield return ClickChoice(choice);v12ChosenRewards.Add(choice);
            Write("v12-reward-choices.json",new{requestedForm=V12RequestedForm,choices=v12ChosenRewards,build=world.Profile.ActiveMode.v11Build});
        }
        IEnumerator V12Travel(string id)
        {
            var room=world.Profile.ActiveDungeonConfig.Room(id);stage="v12-walk-"+id;
            var destination=new Vector3(room.x,0,room.z);
            if(id=="V12_R2")destination=new Vector3(21,0,6); // The authored centre is a real obstacle.
            if(id=="V12_R3")destination=new Vector3(45,0,3); // Public northern bypass, no patrol-state edits.
            yield return WalkTo(()=>destination,.8f,70,()=>world.Profile.IsInEncounter||world.DungeonModal);
            float wait=Time.realtimeSinceStartup+15;
            while(world.Profile.ActiveRoom.templateId!=id&&Time.realtimeSinceStartup<wait){Keys();yield return null;}
            Require(world.Profile.ActiveRoom.templateId==id,"Party did not enter actual room "+id);
            v12RoomsVisited.Add(id);yield return CaptureP47(id+"-entry.png");
            if(world.Profile.IsInEncounter)yield return V12FightRoom(id);
            yield return V12Reward();
        }
        IEnumerator V12ReadEvent(string id,bool free)
        {
            stage="v12-event-"+id+"-walk";
            var ev=world.Profile.ActiveDungeonConfig.events.First(e=>e.id==id);var position=new Vector3(ev.x,0,ev.z);
            yield return WalkTo(()=>position,1,35);stage="v12-event-"+id+"-open";yield return Press(Key.E);
            var reader=world.DungeonChoices.FirstOrDefault(c=>c.id.StartsWith("actor:")&&c.enabled);
            if(reader!=null)yield return ClickChoice(reader.id);
            stage="v12-event-"+id+"-commit";
            if(free)yield return ClickChoice("event-free");
            else
            {
                yield return ClickChoice("event-roll");yield return ClickChoice("event-accept");
            }
            if(world.DungeonModal&&world.DungeonChoices.Any(c=>c.id=="close"))yield return ClickChoice("close");
        }
        IEnumerator V12ContinueDisk()
        {
            yield return WalkTo(()=>new Vector3(45,0,3),.5f,30);stage="v12-safe-save";
            if(V14RouteRequested&&!world.V13AssistMode)
            {
                string fixedCore=world.Profile.ActiveMode.v11Departure.preparation.coreId,locked=world.Profile.ActiveMode.v11Departure.hash;
                int old=world.State.actors.FindIndex(a=>a.id==world.State.activeActorId),other=world.State.actors.FindIndex(a=>a.Alive&&a.id!=world.State.activeActorId);
                if(other>=0){yield return Press((Key)((int)Key.Digit1+other));Require(world.State.activeActorId==world.State.actors[other].id&&world.Session.Config.v11.coreActorId==fixedCore&&world.Profile.ActiveMode.v11Departure.hash==locked,"Trio active switch changed locked core");yield return CaptureP47("v14-trio-active-switch.png");yield return Press((Key)((int)Key.Digit1+old));}
            }
            yield return Press(Key.Escape);yield return ClickUi("save");
            string run=world.Profile.ActiveMode.id,actors=CombatJson.Write(world.State.actors),departure=CombatJson.Write(world.Profile.ActiveMode.v11Departure),layout=CombatJson.Write(world.Profile.ActiveMode.v12Run);
            string v13=CombatJson.Write(world.State.v13);
            string save=world.SavePath;world.InitializeP6FrontendForTests(save);yield return null;
            yield return FrontP47("explore");yield return FrontP47("continue-exploration");
            Require(world.Profile.ActiveMode.id==run&&CombatJson.Write(world.State.actors)==actors&&CombatJson.Write(world.Profile.ActiveMode.v11Departure)==departure&&CombatJson.Write(world.Profile.ActiveMode.v12Run)==layout,"Safe disk continue changed actors/resources/layout/departure");
            Require(CombatJson.Write(world.State.v13)==v13,"Safe disk continue changed V13 tide RNG, receipts, resources or terrain");
            if(world.State.mode==CombatMode.CombatPaused)yield return Press(Key.Escape);
            v12DiskContinuePassed=true;Write("v12-continue.json",new{run,actors,departure,layout,v13,verification="Actual save-menu, loader disk reload, actual continue click; no gameplay state modification."});
            Record("v12-save-continue","Actual save-menu, disk loader and Continue preserved the same safe-state actors/resources/run layout.");
        }
        IEnumerator FlowV12Route()
        {
            yield return null;yield return null;world.InitializeP6FrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            stage="v12-main";Require(world.Menu&&world.FrontChoices.Select(c=>c.id).SequenceEqual(new[]{"campaign","explore","characters"}),"Three-entry ordinary main changed");
            Record("v12-main-menu","Actual executable entered the three-entry title before opening character pages.");
            yield return CaptureP47("00-main.png");yield return FrontP47("characters");yield return CaptureP47("01-roster.png");
            foreach(var id in new[]{"char_story_evan","char_story_mira","char_story_finn"})
            {yield return FrontP47("role:"+id);yield return FrontP47("node:"+id+"/passive");if(world.FrontChoices.Any(c=>c.id=="node-confirm"&&c.enabled))yield return FrontP47("node-confirm");yield return CaptureP47("tree-"+id+".png");yield return FrontP47("characters");}
            Record("v12-character-trees","All three main-character trees opened by real pointer input; purchases used the original permanent-node transaction.");
            yield return FrontP47("explore");yield return FrontP47("map-ashen");
            if(V14RouteRequested){v14DefaultPreparationMode=world.Profile.V11PreparationDraft.partyCombatMode.ToString();yield return CaptureP47("02a-default-preparation.png");}
            string core=V12RequestedCore;Require(new[]{"char_story_evan","char_story_mira","char_story_finn",P6Paladin}.Contains(core),"Unsupported V12 route core");
            var team=core==P6Paladin?new[]{P6Paladin,"mage","char_story_finn"}:new[]{"char_story_evan","char_story_mira","char_story_finn"};
            for(int i=0;i<3;i++){yield return FrontP47("slot:"+i);yield return FrontP47("member:"+team[i]);}
            yield return FrontP47("core:"+core);
            if(V14RouteRequested){yield return FrontP47("mode:"+V14RequestedMode);if(V14Argument("-v14-finn-slot","bloom")=="grove")yield return FrontP47("finn-slot");}
            for(int i=0;i<3&&world.FrontChoices.Any(c=>c.id=="active"&&c.enabled)&&world.Profile.V11PreparationDraft.defaultActiveActorId!=core;i++)yield return FrontP47("active");
            if(V13GuardRequested&&world.Profile.V11PreparationDraft.finnTree!="guard")yield return FrontP47("finn-tree");
            yield return CaptureP47("02-preparation.png");yield return FrontP47("depart");
            if(world.Training)yield return P6Click("p6-finish");
            Require(world.Profile.UsesV12Dungeon&&world.State.actors.Count==3,"New run did not use actual six-room snapshot");
            Require(world.Profile.ActiveMode.v11Departure.preparation.coreId==core,"Actual departure replaced chosen core");
            V14RecordDeparture();
            Record("v12-core-selection","Three-member preparation and requested single core reached the saved runtime snapshot.");
            string run=world.Profile.ActiveMode.id;world.CommandPublished+=ObserveV12Published;
            yield return V12Travel("V12_R1");if(V13OneRoom){completed=true;stage="v13-first-encounter-complete";yield break;}yield return V12Travel("V12_R2");yield return V12Travel("V12_R3");
            yield return V12ReadEvent("V12_READ",true);yield return V12ReadEvent("V12_SEARCH",false);
            var cache=world.Profile.ActiveMode.v12Run.points.First(p=>p.id=="V12_R3_CACHE");stage="v12-library-supply";
            yield return WalkTo(()=>new Vector3(cache.x,0,cache.z),.7f,35);yield return Press(Key.E);
            if(world.DungeonChoices.Any(c=>c.id=="supply-claim"&&c.enabled))yield return ClickChoice("supply-claim");
            if(world.DungeonModal&&world.DungeonChoices.Any(c=>c.id=="close"))yield return ClickChoice("close");
            yield return V12ContinueDisk();
            yield return V12Travel("V12_R4");yield return V12Travel("V12_R5");
            yield return WalkTo(()=>new Vector3(26,0,22),.7f,20);yield return Press(Key.E);
            if(world.DungeonChoices.Any(c=>c.id=="supply-claim"&&c.enabled))yield return ClickChoice("supply-claim");
            if(world.DungeonModal&&world.DungeonChoices.Any(c=>c.id=="close"))yield return ClickChoice("close");
            yield return V12Travel("V12_R6");
            Require(world.DungeonChoices.Any(c=>c.id=="settle:victory"),"Final boss did not reach settlement");
            var finished=CombatJson.Clone(world.Profile.ActiveMode);yield return CaptureP47("09-victory.png");yield return ClickChoice("settle:victory");yield return ClickChoice("growth-menu");
            Require(world.Menu&&world.FrontPage=="characters","Six-room settlement did not return to growth");
            v12SettlementPassed=true;
            Require(v12RoomsVisited.Count==6&&v12ChosenRewards.Count==4&&v12DiskContinuePassed,"Missing rooms/reward points/disk continue");
            Record("v12-six-rooms","Actual WASD traversed all six room IDs; required encounters and optional nonblocking investigations completed through ordinary actions.");
            Record("v12-settlement","Final boss reached the existing one-time victory transaction and returned to the character growth page.");
            Write("v12-full-route.json",new{run,core,requestedForm=V12RequestedForm,finished,rooms=v12RoomsVisited,rewards=v12ChosenRewards,elapsedRealSeconds=finished.elapsedRealSeconds,finished.explicitPauseSeconds,
                target6To9Minutes=finished.elapsedRealSeconds>=360&&finished.elapsedRealSeconds<=540,under10Minutes=finished.elapsedRealSeconds<600,usesInjectedResources=false,usesFixedSuccessDice=false,humanRuns=0});
            world.CommandPublished-=ObserveV12Published;v12FullRoutePassed=true;completed=true;stage="v12-six-room-complete";
        }
    }
}


