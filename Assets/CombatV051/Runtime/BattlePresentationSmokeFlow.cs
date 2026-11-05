using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    /// <summary>Opt-in P3 first-session input driver. No gameplay fixtures, direct command dispatch or outcome injection.</summary>
    public sealed partial class BattlePresentationSmokeFlow : MonoBehaviour
    {
        BattleWorld world;
        Keyboard keyboard;
        Mouse mouse;
        InputSettings.BackgroundBehavior previousBackground;
        string directory, stage="initialize", observerFailure;
        readonly List<object> checks=new List<object>(), events=new List<object>(), freezeFrames=new List<object>();
        readonly List<string> screenshots=new List<string>(), errors=new List<string>();
        readonly Dictionary<string,AudioOutputObservation> audioOutput=new Dictionary<string,AudioOutputObservation>();
        readonly Dictionary<string,DiceEarningObservation> diceEarnings=new Dictionary<string,DiceEarningObservation>();
        readonly float[] audioSamples=new float[2048];
        float nextAudioSample;
        Vector2 pointer;
        bool freezeArmed, freezeCaptured, completed;
        string frozenTicket, frozenDto, rawFaces;
        Vector3 frozenCamera;
        Quaternion frozenRotation;
        float started, originalRollDuration;

        sealed class AudioOutputObservation
        {
            public string stage,mode;
            public int samples,outputSampleRate;
            public double meanRms,maxRms,peak;
            public bool listenerPaused;
            public float listenerVolume;
            public List<string> playingClips=new List<string>();
        }
        sealed class DiceEarningObservation
        {
            public string encounterId,roomId,attemptId;
            public int initialCharge,observations,lastCharge,totalChargeSpent,additionalDiceObserved;
            public float maximumMomentum,totalMomentumEarned,passiveMomentumEarned;
        }

        public void Begin(BattleWorld owner,string evidenceDirectory)
        {
            Require(Path.IsPathRooted(evidenceDirectory),"P3 smoke needs an absolute isolated evidence directory.");
            directory=Path.GetFullPath(evidenceDirectory);
            Require(!Directory.Exists(directory)||!Directory.EnumerateFileSystemEntries(directory).Any(),"P3 evidence directory is not fresh.");
            Directory.CreateDirectory(directory);world=owner;
            Application.runInBackground=true;world.IgnoreApplicationFocus=true;
            previousBackground=InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard=InputSystem.AddDevice<Keyboard>("P3 isolated first-session keyboard");
            mouse=InputSystem.AddDevice<Mouse>("P3 isolated first-session mouse");
            InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);
            world.Controls.Map.devices=new InputDevice[]{keyboard,mouse};world.Controls.Map.Enable();
            // The visible desktop may continue sending physical mouse motion. Isolate the UI
            // action asset as well as gameplay actions so both read the same opt-in test devices.
            var uiModule=EventSystem.current?.GetComponent<InputSystemUIInputModule>();
            if(uiModule!=null && uiModule.actionsAsset!=null)uiModule.actionsAsset.devices=new InputDevice[]{keyboard,mouse};
            pointer=new Vector2(Screen.width*.5f,Screen.height*.43f);MouseState(0);
            Application.logMessageReceived+=Log;
            started=Time.realtimeSinceStartup;originalRollDuration=world.Presentation.rollAnimationSeconds;
            StartCoroutine(Run());
        }
        void Log(string message,string stack,LogType type)
        {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        IEnumerator Run()
        {
            Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(v11Mode?FlowV11():p6Mode?FlowP6():p5Mode?FlowP5():p47Mode?FlowP47():p4Mode ? FlowP4() : Flow());
            while(stack.Count>0)
            {
                bool next=false;object value=null;
                try{next=stack.Peek().MoveNext();if(next)value=stack.Peek().Current;}
                catch(Exception ex){failure=ex;}
                if(failure!=null)break;
                if(!next){stack.Pop();continue;}
                if(value is IEnumerator nested){stack.Push(nested);continue;}
                yield return value;
            }
            Keys();MouseState(0);
            if(failure!=null)
            {
                errors.Add(failure.ToString());
                try{Capture("failure-"+stage+".png");}catch(Exception ex){errors.Add("Failure capture: "+ex.Message);}
            }
            if(observerFailure!=null)errors.Add(observerFailure);
            bool pass=completed&&failure==null&&errors.Count==0;
            Write("final-state.json",new{stage,combat=world.State,profile=world.Profile?.State});
            WriteV14CombatEvidence();
            if(V12FullRequested)Write("v12-commands.json",v12ObservedCommands);
            if(p6Mode&&!v11Mode)WriteP6Observations();
            if(v11Mode)WriteV11Observations();
            if(p5Mode)Write("p5-observations.json",new{p5BraceRoute,p5FrostRoute,p5ZeroPreparation,p5ZeroFollowup,actions=p5Actions,usesInjectedResources=false,usesFixedSuccessDice=false});
            Write("summary.json",new
            {
                passed=pass,phase=V12FullRequested?"V12-six-room":V12RoomRequested?"V12-A":v11Mode?"P6-v1.1":p6Mode?"P6.1":p5Mode?"P5.1":p47Mode?"P47":p4Mode?"V051-P4":"V051-P3",engine=Application.unityVersion,utc=DateTime.UtcNow.ToString("o"),stage,
                v12FullRoutePassed,v12DiskContinuePassed,v12SettlementPassed,
                v14CombatMode,v14CoreCharacterId,v14RulesVersion,v14DefaultPreparationMode,applicationProductName=Application.productName,
                checks,errors,events,screenshots,freezeFrames,audioOutput=audioOutput.Values,diceEarnings=diceEarnings.Values,
                naturalAdditionalDiceObserved=diceEarnings.Values.Any(e=>e.additionalDiceObserved>0),
                diceEarnedTutorialFacts=world.Tutorial?.State.facts.Where(f=>f.key=="dice.earned").ToArray(),
                diceEvidenceScope="Additional dice are max observed (current charge + actual receipt chargeSpent - encounter initialCharge), excluding the starting grant. This is a lower bound observed before settlement; a final-blow gain and immediate state replacement may occur between samples. Tutorial facts are the actual accepted sidecar records, not one record per die. No gain or tutorial fact is supplied by the harness.",
                audioEvidenceScope="RMS/peak are actual AudioListener.GetOutputData channel 0 samples. Playing clip names are metadata only. Output samples demonstrate signal presence or absence, not subjective listening quality; no volume is changed by this harness.",
                captureMode="ACTUAL_PLAYER_GPU_RENDER_AUTOMATED_INPUT",normalWindowPresentationVerified=false,
                humanPlayers=0,humanRuns=0,usesTestFixtures=false,usesGameplayStateFixtures=false,usesHarnessIsolation=true,
                naturalVictoryClaimed=false,naturalTargetDurationVerified=false,
                unmodifiedConfigurationEncounterVictory=V12FullRequested?v12FullRoutePassed:p47Mode?p47CompletedDungeon:world.Profile?.ActiveMode.Room("R1")?.resolved==true,
                firstSessionReachedNextRoom=completed&&!V12RoomRequested,elapsedRealSeconds=Time.realtimeSinceStartup-started,p4Encounters=p4Encounters.Values,p4Runs=p4Runs,
                originalRollAnimationSeconds=originalRollDuration,finalRollAnimationSeconds=world.Presentation.rollAnimationSeconds,
                explanation="Automated real InputSystem keyboard/mouse and actual GraphicRaycaster/UI dispatch. The helper only redirects the fresh save path before clicking New. No actor/enemy HP, position, timers, RNG, roll faces, rule configuration or dice-animation duration is assigned. Navigation observes the existing route graph but walks via WASD and real wall sweeps. This is not a human playtest, subjective audio listening, a 5–10 minute timing verdict, or proof that an unassisted new player understands the UI.",
                savePath=world.SavePath,width=Screen.width,height=Screen.height,graphicsDevice=SystemInfo.graphicsDeviceName,
                activeAudioSources=world.GetComponentsInChildren<AudioSource>(true).Count(s=>s.clip!=null),
                tutorialSidecars=Directory.GetFiles(directory,"tutorial-*.json",SearchOption.AllDirectories).Select(p=>p.Substring(directory.Length+1)).ToArray()
            });
            Application.Quit(pass?0:2);
        }
        IEnumerator Flow()
        {
            yield return null;yield return null;
            // Isolation only: false does not create a profile or modify any game state. New is clicked below.
            world.InitializeDungeonForTests(Path.Combine(directory,"Profiles","profile.json"),"p3-first-session",false);
            Require(world.Menu,"P3 first-session smoke must begin at the actual title menu.");
            stage="title";Capture("00-title.png");yield return ClickUi("new");
            Require(world.Profile!=null&&world.DungeonModal,"New did not display the actual story opening.");
            Require(Path.GetFullPath(world.SavePath).StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Save escaped isolation.");
            yield return ClickChoice("close");yield return null;
            Require(world.State.actors.Count==3&&world.DungeonMap!=null,"The actual three-person dungeon did not start.");
            var overlay=world.Hud.Canvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name=="F1 developer overlay");
            Require(overlay!=null&&!overlay.gameObject.activeInHierarchy,"The P3 debug overlay must exist and default to hidden.");
            Capture("01-contextual-movement.png");
            stage="entry-interaction";
            Vector3 from=BattleWorld.Position(world.ActiveActor);
            yield return WalkTo(()=>Vector3.zero,.4f,12);
            Require(BattleGeometry.DistanceXZ(from,BattleWorld.Position(world.ActiveActor))>2,"The actual initial movement did not travel two meters.");
            Capture("02-nearby-interaction.png");
            yield return Press(Key.E);
            Require(world.DungeonModal,"Real E near the entrance did not open its interaction.");
            yield return ClickChoice("close");
            Record("title_move_interact","Clicked actual New/Close buttons; WASD walked over two meters from the unmodified spawn, and E opened the nearby entrance. Debug was hidden.");

            stage="settings-and-debug";
            yield return Press(Key.Escape);
            Require(world.State.mode==CombatMode.CombatPaused,"Actual Escape did not open the pause/settings panel.");
            yield return new WaitForSecondsRealtime(.25f);
            Capture("02a-settings-audio-controls.png");
            yield return Press(Key.Escape);
            Require(world.State.mode!=CombatMode.CombatPaused,"Actual Escape did not close the settings panel.");
            yield return Press(Key.F1);
            Require(world.Hud.DebugOverlayVisible,"Actual F1 did not show the developer overlay.");
            Capture("02b-f1-debug-overlay.png");
            yield return Press(Key.F1);
            Require(!world.Hud.DebugOverlayVisible,"Actual F1 did not hide the developer overlay again.");
            Record("settings_and_f1","Real Escape opened and closed the settings/audio controls without changing values. Real F1 showed and hid the debug overlay; both visible states were GPU-captured.");

            stage="walk-r1";
            yield return WalkTo(()=>new Vector3(24,0,0),.6f,35,()=>world.Profile.IsInEncounter);
            yield return Wait(()=>world.Profile.IsInEncounter,8,"The whole party did not enter R1 and start its encounter.");
            Require(world.Profile.ActiveRoom.templateId=="R1","The first physical encounter was not R1.");
            Require(!world.DungeonMap.IsPassageOpen("P01"),"R1 did not close its real entrance.");
            Capture("03-first-combat.png");
            Record("physical_party_entry","Walked from R0 through the open passage. Actual followers crossed and R1 locked its door through normal Update; no party positions were assigned.");

            stage="basic-attack";
            float attackDeadline=Time.realtimeSinceStartup+20;bool hit=false;
            while(!hit&&Time.realtimeSinceStartup<attackDeadline)
            {
                RequireLiveCombat();var enemy=Enemy();
                if(BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),BattleWorld.Position(enemy))>world.Session.Config.BasicFor(world.ActiveActor).range-.20f)
                {Drive(BattleWorld.Position(enemy),world.Session.Config.BasicFor(world.ActiveActor).range-.25f);yield return null;continue;}
                Keys();
                if(world.ActiveActor.majorRemaining>0){yield return null;continue;}
                var oldIds=new HashSet<string>(world.State.incomingEvents.Select(e=>e.id));string actorId=world.State.activeActorId;
                yield return ClickWorld(BattleWorld.Position(enemy));
                hit=world.State.incomingEvents.Any(e=>!oldIds.Contains(e.id)&&e.attackerId==actorId&&e.damageApplied&&e.finalDamage>0);
                if(!hit)yield return new WaitForSecondsRealtime(.05f);
            }
            Keys();Require(hit,"No real player basic attack hit within twenty seconds; dice outcomes were not overridden.");
            Record("real_basic_hit","Mouse aimed along the enemy direction; an actual player-owned background d20 attack applied positive damage.");
            stage="guard-dodge-switch";
            yield return Wait(()=>world.ActiveActor.bonusRemaining<=0&&world.ActiveActor.windupRemaining<=0,5,"Guard did not become available.");
            AimAt(BattleWorld.Position(Enemy()));MouseState(2);yield return null;yield return null;
            Require(world.ActiveActor.guardRemaining>0,"Real right mouse did not start Guard.");
            Capture("04-guard-feedback.png");yield return new WaitForSecondsRealtime(.22f);MouseState(0);yield return null;
            Keys(Key.Space);yield return null;yield return null;
            Require(world.ActiveActor.dodgeRemaining>0,"Real Space did not start Dodge.");
            Keys();yield return Wait(()=>world.ActiveActor.dodgeRemaining<=0,2,"Dodge did not finish naturally.");
            yield return Press(Key.Digit2);Require(world.State.activeActorId=="rogue","2 did not select Rogue.");
            yield return Press(Key.Digit3);Require(world.State.activeActorId=="mage","3 did not select Mage.");
            yield return Press(Key.Digit1);Require(world.State.activeActorId=="fighter","1 did not return to Fighter.");
            Record("real_guard_dodge_party_switch","Separate right-click Guard and Space Dodge started through gameplay inputs, then 2/3/1 switched all three real party members.");

            stage="tactical-selection";
            RequireLiveCombat();
            yield return WalkTo(()=>BattleWorld.Position(Enemy()),5.2f,12);
            yield return Wait(()=>world.ActiveActor.majorRemaining<=0&&world.ActiveActor.guardRemaining<=0&&world.ActiveActor.dodgeRemaining<=0,3,"Fighter remains action-busy before Focus.");
            int charges=world.State.charges;Require(charges>0,"No legitimate tactical action remains; smoke does not grant charges.");
            yield return Press(Key.Tab);Require(world.State.mode==CombatMode.TacticalFocus,"Tab did not enter Tactical Focus.");
            yield return ClickUi("fighter");yield return ClickUi("attack");yield return ClickUi("target");
            Capture("05-tactical-selection.png");
            freezeArmed=true;stage="confirm-and-freeze";
            yield return ClickUi("confirm");
            yield return Wait(()=>freezeCaptured,3,"No first-frame RollFreeze capture was observed at the default animation duration.");
            Require(observerFailure==null,observerFailure);
            Require(world.State.charges==charges-1,"Real Confirm did not spend exactly one shared charge.");
            yield return Wait(()=>world.State.mode==CombatMode.PostRollDecision,5,"The default roll animation did not reveal automatically.");
            Require(world.State.pendingRoll!=null&&world.State.pendingRoll.id==frozenTicket&&CombatJson.Write(world.State.pendingRoll.rawFaces)==rawFaces,"Reveal changed the paid original ticket.");
            Require(world.Presentation.rollAnimationSeconds==originalRollDuration,"Default roll duration changed during smoke.");
            string paidBeforeOptions=CombatJson.Write(world.State.pendingRoll);
            var precision=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name=="precision");
            Require(!precision.gameObject.activeInHierarchy,"First tutorial decision must fold the extra resource options.");
            yield return ClickUi("post-more");Require(precision.gameObject.activeInHierarchy,"More options did not expose the legal existing controls.");
            yield return ClickUi("post-more");Require(!precision.gameObject.activeInHierarchy,"Extra options did not fold again.");
            Require(CombatJson.Write(world.State.pendingRoll)==paidBeforeOptions,"Expanding presentation options changed the paid roll.");
            Record("first_decision_progressive_disclosure","Actual More/Collapse clicks exposed and folded existing resource choices without editing the paid roll.");
            Capture("07-paid-result.png");
            Record("normal_duration_roll_freeze","Actual Confirm clicked through UI, charged once, and the first true RollFreeze frame was GPU-captured without lengthening its animation. Original faces remained stable on reveal. See freezeFrames for the number of frames actually observed.");
            stage="commit-and-execute";
            yield return ClickUi("commit");
            Require(world.State.queue.Any(q=>q.ticketId==frozenTicket&&q.stage=="Queued"),"Actual Commit did not create the ticket's queue.");
            yield return Press(Key.Tab);
            yield return Wait(()=>world.State.queue.Any(q=>q.ticketId==frozenTicket&&q.stage=="Resolved"),12,"Queued action did not execute normally; no outcome was forced.");
            Capture("08-realtime-execution.png");
            Record("actual_commit_execute","Clicked Commit and pressed Tab. The actual queue reached Resolved through normal real-time movement/windup; success or failure of its natural d20 was accepted.");

            stage="natural-r1-clear";yield return FightToClear(105);
            Require(world.Profile.ActiveMode.Room("R1").resolved,"Unmodified R1 encounter was not won.");
            Capture("09-natural-clear-reward.png");
            if(world.DungeonModal&&world.DungeonChoices.Any(c=>c.id=="skip-reward"))yield return ClickChoice("skip-reward");
            Require(!world.DungeonModal,"Unexpected unresolved modal blocks the exit.");
            Record("unmodified_encounter_clear","Ordinary input attacks/defenses and conservative allies defeated the default-health guard using natural RNG. No enemy health, timers or resources were edited; reward was explicitly skipped through UI.");
            events.Add(new{kind="natural_dice_earning_outcome",additionalDiceObserved=diceEarnings.Values.Sum(e=>e.additionalDiceObserved),
                tutorialFactCount=world.Tutorial?.State.facts.Count(f=>f.key=="dice.earned")??0,
                detail="Observed actual play only. Zero means this run did not demonstrate an extra earned die; the starting charge is excluded and no artificial award or delayed clear is used."});
            stage="walk-r2";
            yield return WalkTo(()=>new Vector3(49,0,0),.7f,40);
            yield return Wait(()=>world.Profile.ActiveRoom.templateId=="R2",8,"The party did not enter the next room through the actual door.");
            Capture("10-next-objective.png");
            Record("first_session_next_objective","WASD walked the whole surviving party through the cleared exit to R2. The new room and objective follow actual progression.");
            Require(world.GetComponentsInChildren<AudioSource>(true).Any(s=>s.clip!=null),"No actual AudioSource has an assigned clip.");
            Require(world.Presentation.rollAnimationSeconds==originalRollDuration,"Presentation configuration changed.");
            completed=true;stage="completed";
        }
        IEnumerator FightToClear(float timeout)
        {
            float deadline=Time.realtimeSinceStartup+timeout;
            while(!world.Profile.ActiveMode.Room("R1").resolved&&Time.realtimeSinceStartup<deadline)
            {
                Require(world.State.actors.Any(a=>a.Alive),"The unmodified party was defeated; smoke will not heal or retry invisibly.");
                if(world.DungeonModal)break;
                if(!world.ActiveActor.Alive)
                {
                    int alive=world.State.actors.FindIndex(a=>a.Alive);yield return Press(alive==0?Key.Digit1:alive==1?Key.Digit2:Key.Digit3);continue;
                }
                var enemy=Enemy(false);if(enemy==null){Keys();yield return null;continue;}
                var actor=world.ActiveActor;
                bool imminent=world.State.enemies.Any(e=>e.Alive&&e.aiState==1&&e.aiRemaining<.27f&&BattleGeometry.Contains(world.EnemyShape(e),BattleWorld.Position(actor)));
                if(imminent&&actor.bonusRemaining<=0&&actor.guardRemaining<=0&&actor.dodgeRemaining<=0&&string.IsNullOrEmpty(actor.heldBy))
                {Keys();AimAt(BattleWorld.Position(enemy));MouseState(2);yield return new WaitForSecondsRealtime(.31f);MouseState(0);yield return null;continue;}
                if(imminent&&actor.dodgeCooldown<=0&&actor.dodgeRemaining<=0&&string.IsNullOrEmpty(actor.heldBy))
                {Keys(Key.Space);yield return null;yield return null;Keys();yield return new WaitForSecondsRealtime(.32f);continue;}
                float range=world.Session.Config.BasicFor(actor).range;
                if(BattleGeometry.DistanceXZ(BattleWorld.Position(actor),BattleWorld.Position(enemy))>range-.15f)
                {Drive(BattleWorld.Position(enemy),range-.2f);yield return null;continue;}
                Keys();
                if(actor.majorRemaining<=0&&actor.dodgeRemaining<=0&&actor.guardRemaining<=0&&string.IsNullOrEmpty(actor.heldBy))yield return ClickWorld(BattleWorld.Position(enemy));
                else yield return new WaitForSecondsRealtime(.035f);
            }
            Keys();MouseState(0);
            Require(world.Profile.ActiveMode.Room("R1").resolved,"R1 did not clear with ordinary inputs before timeout. Current HP: "+string.Join(",",world.State.actors.Select(a=>a.id+"="+a.hp))+"; enemies "+string.Join(",",world.State.enemies.Select(a=>a.id+"="+a.hp)));
        }
        IEnumerator WalkTo(Func<Vector3> destination,float distance,float timeout,Func<bool> stop=null)
        {
            float deadline=Time.realtimeSinceStartup+timeout;
            while(Time.realtimeSinceStartup<deadline&&(stop==null||!stop())&&BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),destination())>distance)
            {
                Require(world.ActiveActor.Alive&&!world.DungeonModal,"A dead controller or modal interrupted walking at "+stage);
                Vector3 goal=destination();var current=BattleWorld.Position(world.ActiveActor);
                var waypoint=world.DungeonMap!=null?world.DungeonMap.NextWaypoint(current,goal):goal;
                // Portal near->far handoff requires crossing within .20m of the near point.
                // A .25m input stop radius parked outside that threshold forever at P01 x=10.50.
                if(V12FullRequested)DriveP4Combat(waypoint);else Drive(waypoint,.05f);yield return null;
            }
            Keys();yield return null;
            Require(stop!=null&&stop()||BattleGeometry.DistanceXZ(BattleWorld.Position(world.ActiveActor),destination())<=distance+.15f,"Actual WASD navigation timed out at "+stage+"; position="+BattleWorld.Position(world.ActiveActor));
        }
        void Drive(Vector3 destination,float stop)
        {
            Vector3 delta=BattleGeometry.Ground(destination-BattleWorld.Position(world.ActiveActor));
            if(delta.magnitude<=stop){Keys();return;}delta.Normalize();
            float x=Vector3.Dot(delta,world.Rig.ScreenMove(Vector2.right)),y=Vector3.Dot(delta,world.Rig.ScreenMove(Vector2.up));
            var keys=new List<Key>();if(x>.18f)keys.Add(Key.D);if(x<-.18f)keys.Add(Key.A);if(y>.18f)keys.Add(Key.W);if(y<-.18f)keys.Add(Key.S);Keys(keys.ToArray());
        }
        ActorState Enemy(bool required=true)
        {
            var enemy=world.State.enemies.Where(e=>e.Alive).OrderBy(e=>BattleGeometry.DistanceXZ(BattleWorld.Position(e),BattleWorld.Position(world.ActiveActor))).FirstOrDefault();
            if(required)Require(enemy!=null,"No living enemy remains during "+stage);return enemy;
        }
        void RequireLiveCombat(){Require(world.Profile.IsInEncounter&&world.ActiveActor.Alive,"A living controller and active encounter are required at "+stage);}
        IEnumerator Press(Key key)
        {Keys();yield return null;Keys(key);yield return null;yield return null;Keys();yield return null;yield return null;}
        void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
        void MouseState(ushort buttons)=>InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState{position=pointer,buttons=buttons});
        bool OverUi(Vector2 point)
        {
            var results=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=point},results);return results.Count>0;
        }
        void AimAt(Vector3 target)
        {
            Require(TryAimAt(target),"No unobscured mouse-to-ground aim point toward the actual enemy.");
        }
        bool TryAimPoint(Vector3 feet,Vector3 target,out Vector2 point)
        {
            Vector3 direction=BattleGeometry.Ground(target-feet).normalized;
            foreach(float distance in new[]{1.5f,2.5f,.6f,4f})
            {
                Vector3 projected=world.BattleCamera.WorldToScreenPoint(feet+direction*distance);
                point=new Vector2(projected.x,projected.y);
                if(projected.z>0&&point.x>4&&point.y>4&&point.x<Screen.width-4&&point.y<Screen.height-4&&!OverUi(point))return true;
            }
            point=default;return false;
        }
        bool TryAimAt(Vector3 target)
        {
            if(!TryAimPoint(BattleWorld.Position(world.ActiveActor),target,out var point))return false;
            pointer=point;MouseState(0);return true;
        }
        IEnumerator ClickWorld(Vector3 target)
        {AimAt(target);yield return null;yield return null;MouseState(1);yield return null;yield return null;MouseState(0);yield return null;}
        IEnumerator ClickChoice(string id)
        {
            int index=world.DungeonChoices.FindIndex(c=>c.id==id&&c.enabled);
            Require(index>=0,"Actual enabled dungeon choice absent: "+id);
            // Shipping modal input protection requires a release and a fresh press.
            // Wait for its actual Button; do not bypass it with a domain command.
            MouseState(0);yield return null;
            yield return Wait(()=>world.Hud.Canvas.GetComponentsInChildren<Button>(true).Any(b=>b.name=="dungeon-choice-"+index&&b.gameObject.activeInHierarchy&&b.interactable),2,"Dungeon choice did not arm after release: "+id);
            yield return ClickUi("dungeon-choice-"+index);
        }
        IEnumerator ClickUi(string id,int reactionRetries=0)
        {
            if(p5Mode&&!id.StartsWith("reaction-")&&world.State.mode==CombatMode.ReactionPrompt)
            {
                Require(reactionRetries<3,"Repeated Reaction interrupted "+id);
                events.Add(new{kind="actual_input_interrupted_by_reaction",id,world.State.simulationSeconds});
                yield return ClickUi("reaction-decline");yield return ClickUi(id,reactionRetries+1);yield break;
            }
            Keys();yield return null;world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            // The world can enter Reaction or finish the room during the UI refresh yields.
            // Re-observe that transition; never count a hidden/disabled button as a click.
            if(p5Mode&&!id.StartsWith("front-")&&!id.StartsWith("reaction-")&&!world.State.inCombat)
            {events.Add(new{kind="actual_room_ended_before_ui",id});yield break;}
            if(p5Mode&&!id.StartsWith("reaction-")&&world.State.mode==CombatMode.ReactionPrompt)
            {Require(reactionRetries<3,"Repeated Reaction interrupted refresh "+id);events.Add(new{kind="actual_refresh_interrupted_by_reaction",id});yield return ClickUi("reaction-decline");yield return ClickUi(id,reactionRetries+1);yield break;}
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
            Require(button.gameObject.activeInHierarchy&&button.interactable,"Actual UI button unavailable: "+id+"; "+world.Message);
            var rect=(RectTransform)button.transform;pointer=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=pointer},rays);
            Require(rays.Count>0&&(rays[0].gameObject==button.gameObject||rays[0].gameObject.transform.IsChildOf(button.transform)),"Actual UI raycast misses "+id+" at "+pointer+"; top="+rays.FirstOrDefault().gameObject);
            int clicked=0;UnityEngine.Events.UnityAction observe=()=>clicked++;button.onClick.AddListener(observe);
            MouseState(0);yield return null;yield return null;MouseState(1);yield return null;yield return null;MouseState(0);yield return null;yield return null;
            button.onClick.RemoveListener(observe);
            if(p5Mode&&clicked==0&&!world.State.inCombat&&!id.StartsWith("front-"))
            {events.Add(new{kind="actual_room_ended_during_pointer",id});yield break;}
            if(p5Mode&&clicked==0&&!id.StartsWith("reaction-")&&world.State.mode==CombatMode.ReactionPrompt)
            {
                Require(reactionRetries<3,"Repeated Reaction interrupted pointer "+id);
                events.Add(new{kind="actual_pointer_interrupted_by_reaction",id,world.State.simulationSeconds});
                yield return ClickUi("reaction-decline");yield return ClickUi(id,reactionRetries+1);yield break;
            }
            Require(clicked==1,"Real pointer dispatched "+clicked+" clicks for "+id);
            events.Add(new{kind="actual_pointer_click",id,screenX=pointer.x,screenY=pointer.y,at=Time.realtimeSinceStartup-started});
        }
        IEnumerator Wait(Func<bool> condition,float seconds,string error)
        {float until=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<until){Require(observerFailure==null,observerFailure);yield return null;}Require(condition(),error);}
        void LateUpdate()
        {
            if(world==null||world.Session==null)return;
            try
            {
                ObserveDiceEarnings();
                ObserveP5Actions();ObserveP6Actions();ObserveV11Actions();
                if(p4Mode)ObserveP4Metrics();
                if(Time.realtimeSinceStartup>=nextAudioSample)
                {SampleAudioOutput();nextAudioSample=Time.realtimeSinceStartup+.08f;}
                if(!freezeArmed||world.State.mode!=CombatMode.RollFreeze)return;
                string dto=p5Mode||p6Mode?CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.world,world.State.p5,world.State.p6,world.State.focusBudget,world.State.simulationSeconds}):CombatJson.Write(new{world.State.actors,world.State.enemies,world.State.world,world.State.simulationSeconds});
                if(!freezeCaptured||(p5Mode||p6Mode)&&frozenTicket!=world.State.pendingRoll.id)
                {
                    SampleAudioOutput();
                    frozenTicket=world.State.pendingRoll.id;rawFaces=CombatJson.Write(world.State.pendingRoll.rawFaces);frozenDto=dto;
                    frozenCamera=world.BattleCamera.transform.position;frozenRotation=world.BattleCamera.transform.rotation;
                    Require(Time.timeScale==0,"Actual first RollFreeze frame has nonzero world scale.");
                    Capture(p6Mode?"06-p6-roll-"+world.State.rolls.Count+".png":p5Mode?"06-p5-roll-"+world.State.rolls.Count+".png":"06-first-frame-roll-freeze.png");freezeCaptured=true;
                    Write(p5Mode||p6Mode?"freeze-state-"+world.State.rolls.Count+".json":"first-freeze-state.json",new{ticket=frozenTicket,rawFaces,world=world.State,rollElapsed=world.RollElapsed});
                }
                Require(dto==frozenDto,"A later actual RollFreeze frame changed world state.");
                Require(world.BattleCamera.transform.position==frozenCamera&&world.BattleCamera.transform.rotation==frozenRotation,"The camera moved during actual RollFreeze.");
                freezeFrames.Add(new{ticket=frozenTicket,frame=Time.frameCount,rollElapsed=world.RollElapsed,scale=Time.timeScale,simulationSeconds=world.State.simulationSeconds,worldUnchanged=true});
            }
            catch(Exception ex){observerFailure=ex.ToString();freezeArmed=false;}
        }
        void ObserveDiceEarnings()
        {
            var encounter=world.Profile?.ActiveEncounter;
            if(!world.State.inCombat||encounter==null)return;
            if(!diceEarnings.TryGetValue(encounter.id,out var observation))
            {
                observation=new DiceEarningObservation{encounterId=encounter.id,roomId=encounter.roomId,attemptId=encounter.attemptId,initialCharge=encounter.initialCharge};
                diceEarnings.Add(encounter.id,observation);
            }
            observation.observations++;observation.lastCharge=world.State.charges;
            observation.totalChargeSpent=world.State.receipts.Sum(r=>r.result?.chargeSpent??0);
            int additional=Math.Max(0,observation.lastCharge+observation.totalChargeSpent-observation.initialCharge);
            if(additional>observation.additionalDiceObserved)
                events.Add(new{kind="observed_natural_additional_die",encounterId=encounter.id,additionalDice=additional,world.State.charges,chargeSpent=observation.totalChargeSpent,initialCharge=observation.initialCharge,at=Time.realtimeSinceStartup-started});
            observation.additionalDiceObserved=Math.Max(observation.additionalDiceObserved,additional);
            observation.maximumMomentum=Math.Max(observation.maximumMomentum,world.State.momentum);
            observation.totalMomentumEarned=Math.Max(observation.totalMomentumEarned,world.State.momentumLedger.totalEarned);
            observation.passiveMomentumEarned=Math.Max(observation.passiveMomentumEarned,world.State.momentumLedger.passiveEarned);
        }
        void SampleAudioOutput()
        {
            string mode=world.State.mode.ToString(),key=stage+"/"+mode;
            if(!audioOutput.TryGetValue(key,out var observation))
            {
                observation=new AudioOutputObservation{stage=stage,mode=mode};audioOutput.Add(key,observation);
            }
            AudioListener.GetOutputData(audioSamples,0);
            double sum=0,peak=0;
            foreach(float sample in audioSamples){sum+=(double)sample*sample;peak=Math.Max(peak,Math.Abs(sample));}
            double rms=Math.Sqrt(sum/audioSamples.Length);
            observation.samples++;observation.meanRms+=(rms-observation.meanRms)/observation.samples;
            observation.maxRms=Math.Max(observation.maxRms,rms);observation.peak=Math.Max(observation.peak,peak);
            observation.outputSampleRate=AudioSettings.outputSampleRate;observation.listenerPaused=AudioListener.pause;observation.listenerVolume=AudioListener.volume;
            foreach(var source in world.GetComponentsInChildren<AudioSource>(true))
                if(source.isPlaying&&source.clip!=null&&!observation.playingClips.Contains(source.clip.name))observation.playingClips.Add(source.clip.name);
        }
        void Record(string name,string detail)=>checks.Add(new{name,passed=true,detail,at=Time.realtimeSinceStartup-started});
        static void Require(bool okay,string message){if(!okay)throw new InvalidOperationException(message??"Unspecified smoke assertion failed.");}
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(directory,name),Newtonsoft.Json.JsonConvert.SerializeObject(value,Newtonsoft.Json.Formatting.Indented));
        void Capture(string filename)
        {
            // FPS acceptance runs keep all normal rendering but exclude the evidence
            // harness's second GPU render, PNG encoding and screenshot file writes.
            // Dedicated visible-evidence runs retain the original capture path.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-p47-perf-no-capture") >= 0) return;
            world.Hud.RefreshView();var camera=world.BattleCamera;var canvas=world.Hud.Canvas;
            var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;float oldDistance=canvas.planeDistance;
            bool oldOverride=canvas.overrideSorting;int oldSort=canvas.sortingOrder;
            var oldActive=RenderTexture.active;var oldTarget=camera.targetTexture;
            var target=RenderTexture.GetTemporary(Screen.width,Screen.height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);Texture2D texture=null;
            try
            {
                camera.targetTexture=target;canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;canvas.overrideSorting=true;canvas.sortingOrder=short.MaxValue;
                Canvas.ForceUpdateCanvases();RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});RenderTexture.active=target;
                texture=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);texture.Apply(false,false);
                var pixels=texture.GetPixels32();var colors=new HashSet<int>();int lit=0;
                for(int i=0;i<pixels.Length;i+=13){var p=pixels[i];colors.Add(p.r*65536+p.g*256+p.b);if(p.r>20||p.g>20||p.b>20)lit++;}
                File.WriteAllBytes(Path.Combine(directory,filename),texture.EncodeToPNG());Require(colors.Count>20&&lit>pixels.Length/1000,"Actual GPU capture contains insufficient rendered detail.");screenshots.Add(filename);
            }
            finally
            {
                camera.targetTexture=oldTarget;RenderTexture.active=oldActive;canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.planeDistance=oldDistance;canvas.overrideSorting=oldOverride;canvas.sortingOrder=oldSort;
                RenderTexture.ReleaseTemporary(target);if(texture)Destroy(texture);Canvas.ForceUpdateCanvases();
            }
        }
        void OnDestroy()
        {
            if(world!=null)world.CommandPublished-=ObserveHeroContact;
            if(v11Mode&&world!=null)world.CommandPublished-=ObserveV11PublishedCommand;
            Application.logMessageReceived-=Log;
            if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
            if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            if(keyboard!=null)InputSystem.settings.backgroundBehavior=previousBackground;
        }
    }
}
