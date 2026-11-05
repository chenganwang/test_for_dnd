using System;
using System.Collections;
using System.Linq;
using System.IO;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        // Real Player automation, exclusively input/UI. This is not a human trial.
        bool V13GuardRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-v13-guard")>=0;
        bool V13OneRoom=>Array.IndexOf(Environment.GetCommandLineArgs(),"-v13-one-room")>=0;
        string V13LegacyPath{get{var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"-v13-legacy-save");return i>=0&&i+1<args.Length?args[i+1]:null;}}
        IEnumerator FlowV13Legacy()
        {
            string original=File.ReadAllText(V13LegacyPath);var saved=CombatJson.Read<ProfileEnvelope>(original);
            Require(CombatStore.Digest(saved.payload)==saved.integrity,"Original legacy profile integrity");
            var prior=CombatJson.Read<ProfileState>(saved.payload);string snapshot=CombatJson.Write(prior.challenge.v11Departure);
            string copy=Path.Combine(directory,"Profiles","legacy.json");Directory.CreateDirectory(Path.GetDirectoryName(copy));File.Copy(V13LegacyPath,copy,false);
            world.InitializeP6FrontendForTests(copy);
            Require(CombatJson.Write(world.Profile.State.challenge.v11Departure)==snapshot,"Loading changed original departure snapshot");
            yield return null;stage="v13-actual-v12-continue";yield return FrontP47("explore");yield return FrontP47("continue-exploration");
            Require(!world.V13Enabled&&world.Profile.ActivePartyCombatMode==PartyCombatMode.PersistentTrio&&world.State.actors.Count==3,"Legacy save did not retain three bodies");
            yield return Press(Key.Digit2);Require(world.State.activeActorId==world.State.actors[1].id,"Legacy live switching failed");
            yield return Press(Key.Digit1);yield return CaptureP47("v13-legacy-trio.png");
            Require(File.ReadAllText(V13LegacyPath)==original,"Original legacy input was modified");
            Record("v13-actual-v12-continue","Real shipped V12 profile copied without changes, loaded by current Player, continued through UI and switched actors; source file unchanged.");
            Write("v13-legacy.json",new{input=V13LegacyPath,originalDigest=CombatStore.Digest(original),snapshot,originalUnchanged=true,mode=world.Profile.ActivePartyCombatMode.ToString(),humanRuns=0});
            completed=true;stage="v13-legacy-complete";
        }
        bool V13LeaveWarning(ActorState threat)
        {
            var position=BattleWorld.Position(world.ActiveActor);var shape=world.EnemyShape(threat);
            var candidates=Enumerable.Range(0,8).Select(i=>position+Quaternion.Euler(0,i*45,0)*Vector3.forward*3.2f)
                .Where(p=>world.DungeonMap.RoomAt(p)==world.Profile.ActiveRoom.templateId&&!BattleGeometry.Contains(shape,p))
                .Where(p=>BattleGeometry.DistanceXZ(BattleGeometry.MoveAgainstWalls(position,p-position,.36f,1<<8),p)<.08f)
                .OrderBy(p=>BattleGeometry.DistanceXZ(p,BattleWorld.Position(threat))).ToArray();
            if(candidates.Length==0)return false;DriveP4Combat(candidates[0]);return true;
        }
        bool V13FateControlCurrent(string fateId)=>fateId!=null&&world.State.v13?.fate?.id==fateId&&world.State.v13.fate.stage=="Deciding";
        void V13ExpiredFateControl(string id,string fateId)
        {
            Keys();MouseState(0);
            events.Add(new{kind="fate_control_expired_without_click",id,fateId,currentId=world.State.v13?.fate?.id,currentStage=world.State.v13?.fate?.stage,at=Time.realtimeSinceStartup-started});
        }
        bool V13FatePointer(Button button)
        {
            if(button==null||!button.gameObject.activeInHierarchy||!button.interactable)return false;
            var rect=(RectTransform)button.transform;pointer=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            if(pointer.x<=0||pointer.y<=0||pointer.x>=Screen.width||pointer.y>=Screen.height)return false;
            var rays=new System.Collections.Generic.List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=pointer},rays);
            return rays.Count>0&&(rays[0].gameObject==button.gameObject||rays[0].gameObject.transform.IsChildOf(button.transform));
        }
        IEnumerator V13ClickFate(string id,string fateId)
        {
            // The shipping decision continues to count down during capture/refresh yields.
            // An expired control is a state transition, never a claimed successful click.
            Keys();MouseState(0);float until=Time.realtimeSinceStartup+8;
            while(V13FateControlCurrent(fateId)&&Time.realtimeSinceStartup<until)
            {
                world.Hud.RefreshView();yield return null;
                if(!V13FateControlCurrent(fateId))break;Canvas.ForceUpdateCanvases();
                var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).SingleOrDefault(b=>b.name==id&&b.gameObject.activeInHierarchy);
                if(!V13FatePointer(button)){yield return null;continue;}
                MouseState(0);yield return null;yield return null;
                if(!V13FateControlCurrent(fateId))break;
                if(!V13FatePointer(button)){yield return null;continue;}
                int clicked=0;UnityEngine.Events.UnityAction observed=()=>clicked++;button.onClick.AddListener(observed);
                MouseState(1);yield return null;yield return null;MouseState(0);yield return null;yield return null;
                button.onClick.RemoveListener(observed);
                if(clicked==1)
                {p6Inputs.Add(new{kind="actual_pointer",id,fateId,x=pointer.x,y=pointer.y,training=world.Training,at=Time.realtimeSinceStartup-started});events.Add(new{kind="actual_pointer_click",id,fateId,screenX=pointer.x,screenY=pointer.y,at=Time.realtimeSinceStartup-started});yield break;}
                Require(clicked==0,"Fate pointer dispatched more than one click: "+id);
                if(!V13FateControlCurrent(fateId))break;
                events.Add(new{kind="fate_pointer_not_dispatched",id,fateId,at=Time.realtimeSinceStartup-started});
                yield return null;
            }
            if(!V13FateControlCurrent(fateId)){V13ExpiredFateControl(id,fateId);yield break;}
            Require(false,"Timed out waiting for actual fate UI: "+id+"; "+world.Message);
        }
        float v13AimBlockedSince=-1;
        bool V13TryAimOrRecover(Vector3 target)
        {
            if(TryAimAt(target)){v13AimBlockedSince=-1;return true;}
            Keys();MouseState(0);
            if(world.Menu||world.DungeonModal||CombatSession.V13FateBusy(world.State)||!V12RealtimeInputReady)return false;
            if(v13AimBlockedSince<0)
            {v13AimBlockedSince=Time.realtimeSinceStartup;events.Add(new{kind="aim_obscured_wait",stage,targetX=target.x,targetZ=target.z,at=Time.realtimeSinceStartup-started});return false;}
            if(Time.realtimeSinceStartup-v13AimBlockedSince<.35f)return false;
            var from=BattleWorld.Position(world.ActiveActor);
            var candidates=Enumerable.Range(0,8).Select(i=>from+Quaternion.Euler(0,i*45,0)*Vector3.forward*2.5f)
                .Where(p=>world.DungeonMap.RoomAt(p)==world.Profile.ActiveRoom.templateId)
                .Where(p=>BattleGeometry.DistanceXZ(BattleGeometry.MoveAgainstWalls(from,p-from,.36f,1<<8),p)<.08f)
                .Where(p=>!world.State.enemies.Any(e=>e.Alive&&e.aiState==1&&BattleGeometry.Contains(world.EnemyShape(e),p)))
                .Where(p=>TryAimPoint(p,target,out _)).OrderBy(p=>BattleGeometry.DistanceXZ(p,target)).ToArray();
            if(candidates.Length>0)DriveP4Combat(candidates[0]);
            return false;
        }
        IEnumerator V13ClickWorld(Vector3 target)
        {
            if(!V13TryAimOrRecover(target)){yield return null;yield break;}
            yield return null;yield return null;
            if(!V12RealtimeInputReady||CombatSession.V13FateBusy(world.State)||!TryAimAt(target)){Keys();MouseState(0);yield break;}
            MouseState(1);yield return null;yield return null;MouseState(0);yield return null;
        }
        IEnumerator V13Tactical(string actor,string action,ActorState enemy)
        {
            Keys();yield return Press(Key.Tab);if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            int slot=world.State.actors.FindIndex(a=>a.id==actor);yield return P6Click(new[]{"fighter","rogue","mage"}[slot]);
            int choice=world.AvailableActions(actor).ToList().IndexOf(action);if(choice<0){yield return P6Click("exit");yield break;}
            yield return P6Click("v11-action-"+choice);
            if(action=="v12_finn_grove")yield return V12GroundClick(BattleWorld.Position(world.ActiveActor));
            else if(action!="v13_finn_prayer")
            {int cap=world.State.enemies.Count+1;while(world.SelectedTarget!=enemy.id&&cap-->0&&world.State.mode==CombatMode.TacticalFocus)yield return P6Click("target");}
            if(world.State.mode==CombatMode.TacticalFocus&&world.SelectedActionQuote().legal)yield return P6Click("confirm");
            if(world.State.mode==CombatMode.TacticalFocus)yield return P6Click("exit");
        }
        IEnumerator V13FightRoom(string room)
        {
            float deadline=Time.realtimeSinceStartup+240,nextAssist=0,nextTactic=0,nextFree=0;int assist=0;
            var bossViews=new System.Collections.Generic.HashSet<string>();
            var bossObservations=new System.Collections.Generic.List<object>();string observedBoss=null,evadedEnemy=null;int evadedSequence=-1;
            string encounter=world.Profile.ActiveEncounter.id;
            while(world.Profile.IsInEncounter&&world.Profile.ActiveEncounter.id==encounter&&Time.realtimeSinceStartup<deadline)
            {
                if(world.DungeonModal)break;var s=world.State;
                if(s.v13.boss!=null)
                {
                    var b=s.v13.boss;string stamp=b.phase+"/"+b.root+"/"+b.stage+"/"+b.pillarA+"/"+b.pillarB+"/"+b.nodeA+"/"+b.nodeB;
                    if(stamp!=observedBoss){observedBoss=stamp;bossObservations.Add(new{realSeconds=Time.realtimeSinceStartup-started,boss=CombatJson.Clone(b),hp=s.Actor(b.actorId).hp,coreHp=world.ActiveActor.hp});Write("v13-boss-observations.json",bossObservations);}
                }
                if(s.v13.boss!=null&&s.v13.boss.stage!="Recovery")
                {string view=s.v13.boss.phase+"-"+s.v13.boss.move+"-"+s.v13.boss.stage;if(bossViews.Add(view)){yield return CaptureP47("v13-boss-"+view+".png");continue;}}
                if(s.v13.fate?.stage=="Deciding"){Keys();MouseState(0);string fateId=s.v13.fate.id;yield return CaptureP47("v13-tide-"+s.v13.fateCount+".png");yield return V13ClickFate("v13-fate-accept",fateId);continue;}
                if(world.V13PaidAssist!=null){yield return P6Click("v13-paid-yes");continue;}
                if(s.mode==CombatMode.ReactionPrompt)
                {Keys();if(s.activeReaction==null&&world.Session.GetReactionChoices().Any(q=>q.legal))yield return P6Click("reaction-choice-0");if(world.State.mode==CombatMode.ReactionPrompt)yield return P6Click(world.State.activeReaction!=null?"reaction-confirm":"reaction-decline");continue;}
                if(s.mode==CombatMode.RollFreeze||s.v13.fate?.stage=="Rolled"){Keys();yield return null;continue;}
                if(s.mode==CombatMode.PostRollDecision)
                {
                    var ticket=s.pendingRoll;
                    if(world.V14Enabled&&ticket!=null&&!ticket.hit)
                    {
                        var correction=world.Session.QuoteV14Correction();
                        if(correction.legal){bool plus=ticket.selectedFace!=1&&ticket.total+2>=ticket.targetNumber;
                            if(plus||correction.canReroll){string key=ticket.purpose=="Reaction"?(plus?"v14-reaction-correct":"v14-reaction-review"):(plus?"v13-correct":"v14-review");yield return P6Click(key);yield return CaptureP47("v14-correction-"+s.rolls.Count+".png");continue;}}
                    }
                    yield return P6Click(ticket?.purpose=="Reaction"?"reaction-commit":ticket?.p6!=null&&ticket.hit&&world.Session.AvailableSharedCharges>0?"p6-full":"commit");continue;
                }
                if(s.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                if(!V12RealtimeInputReady){Keys();yield return null;continue;}
                var core=world.ActiveActor;
                if(!core.Alive&&!world.V13AssistMode){int living=s.actors.FindIndex(a=>a.Alive);Require(living>=0,"Natural V14 trio defeat in "+room);yield return Press((Key)((int)Key.Digit1+living));continue;}
                Require(core.Alive,"Natural V13 core defeat in "+room);
                Require(world.V13AssistMode?s.activeActorId==s.v13.coreId&&s.actors.Count(a=>CombatSession.V13Physical(s,a))==1:s.actors.Count(a=>CombatSession.V13Physical(s,a))==3,"Mode/body invariant");
                var enemy=Enemy(false);if(enemy==null){yield return null;continue;}
                var position=BattleWorld.Position(core);var target=BattleWorld.Position(enemy);
                float distance=BattleGeometry.DistanceXZ(position,target);bool sight=world.HasLineOfEffect(position,target);
                bool free=world.Session.P6MajorAvailable(core)&&core.guardRemaining<=0&&core.dodgeRemaining<=0;
                // Keep the refuge until the telegraphed move finishes; do not immediately
                // chase back into the same charge after a successful movement input.
                var evading=s.Actor(evadedEnemy);
                if(evading?.Alive==true&&evading.aiState==1&&evading.attackSequence==evadedSequence&&!BattleGeometry.Contains(world.EnemyShape(evading),position))
                {Keys();yield return null;continue;}
                var threat=s.enemies.FirstOrDefault(e=>e.Alive&&e.aiState==1&&e.aiRemaining<.75f&&BattleGeometry.Contains(world.EnemyShape(e),position));
                if(threat!=null&&free&&core.dodgeCooldown<=0&&V12DodgeKeys(threat,out var dodge)){evadedEnemy=threat.id;evadedSequence=threat.attackSequence;Keys(dodge);yield return null;yield return null;Keys();yield return null;continue;}
                if(threat!=null&&free&&core.bonusRemaining<=0){Keys();if(!V13TryAimOrRecover(BattleWorld.Position(threat))){yield return null;continue;}MouseState(2);yield return null;yield return null;MouseState(0);yield return null;continue;}
                if(threat!=null&&V13LeaveWarning(threat)){evadedEnemy=threat.id;evadedSequence=threat.attackSequence;yield return null;continue;}
                if(s.v13.boss?.stage=="Ritual"&&free)
                {
                    string node=new[]{"v13-node-A","v13-node-B"}.Where(world.V13NodeAvailable).OrderBy(n=>BattleGeometry.DistanceXZ(position,world.V13NodePosition(n))).FirstOrDefault();
                    if(node!=null&&BattleGeometry.DistanceXZ(position,world.V13NodePosition(node))<=world.Session.Config.BasicFor(core).range&&world.HasLineOfEffect(position,world.V13NodePosition(node)))
                    {Keys();yield return V13ClickWorld(world.V13NodePosition(node));continue;}
                    if(node!=null){DriveP4Combat(world.V13NodePosition(node));yield return null;continue;}
                }
                if(world.V13AssistMode&&Time.realtimeSinceStartup>=nextAssist&&sight)
                {
                    int slot=assist++%2;var a=s.Actor(world.V13Assists[slot]);nextAssist=Time.realtimeSinceStartup+.7f;
                    if(a.v12.basicCooldown<=0&&a.p6.bindCooldown<=0&&world.Session.P6MajorAvailable(a))
                    {Keys();if(!V13TryAimOrRecover(target)){yield return null;continue;}yield return Press(slot==0?Key.Digit1:Key.Digit2);continue;}
                }
                if(free&&Time.realtimeSinceStartup>=nextFree&&sight)
                {
                    string action=world.CoreBasicAction;string victim=action=="v13_finn_shield"?core.id:enemy.id;
                    if(action!=null&&world.Session.Quote(core.id,victim,action).legal){nextFree=Time.realtimeSinceStartup+.6f;Keys();if(!V13TryAimOrRecover(target)){yield return null;continue;}yield return Press(Key.F);continue;}
                }
                if(s.charges>0&&s.focusBudget.secondsRemaining>5&&Time.realtimeSinceStartup>=nextTactic)
                {
                    string user=null,action=null;
                    if(core.id==P6Paladin&&free&&distance<=2.4f){nextTactic=Time.realtimeSinceStartup+2.5f;Keys();yield return V12UseKey(enemy);continue;}
                    var mira=s.Actor(CombatSession.V12Mira);var finn=s.Actor(CombatSession.V12Finn);
                    if(world.V14Enabled&&finn?.Alive==true&&world.Session.HeroKey(finn.id)=="v14_finn_detonate"&&world.Session.P6MajorAvailable(finn))
                    {var bloom=world.Session.PreviewV14Detonation(finn.id,enemy.id,world.HasLineOfEffect(world.CastOrigin(finn),target));if(bloom.legal&&bloom.remainingTicks>=2){user=finn.id;action="v14_finn_detonate";}}
                    if(user==null&&finn?.Alive==true&&world.Session.HeroKey(finn.id)!="v14_finn_detonate"&&world.Session.P6MajorAvailable(finn)&&core.hp<core.maxHp*.72f&&!s.v12.groves.Any(g=>g.expiresAt>s.simulationSeconds))
                    {action=world.Session.HeroKey(finn.id);if(action!="v13_finn_prayer"||world.Session.Member(s,finn.id).prayers>0)user=finn.id;}
                    if(user==null&&mira!=null&&world.Session.Quote(mira.id,enemy.id,"v12_mira_break").legal){user=mira.id;action="v12_mira_break";}
                    if(user!=null){nextTactic=Time.realtimeSinceStartup+5;yield return V13Tactical(user,action,enemy);continue;}
                }
                bool rangedCore=core.id!=CombatSession.V12Mira&&core.id!=P6Paladin;
                var assistantMira=s.Actor(CombatSession.V12Mira);
                bool approachPaidAssist=rangedCore&&assistantMira!=null&&s.charges>0&&s.focusBudget.secondsRemaining>5&&Time.realtimeSinceStartup>=nextTactic&&world.Session.P6MajorAvailable(assistantMira);
                // A core at 4.2m cannot order Mira's 4m chase or 2.6m strike. Move the
                // real core into the shipping quote's range; never alter the quote/anchor.
                float range=!rangedCore?2.05f:approachPaidAssist?world.Session.Config.v12.breakRange-.15f:assistantMira!=null?world.Session.V12ChaseRange(assistantMira.id)-.2f:4.2f;
                if(free&&rangedCore&&!approachPaidAssist&&distance<3.2f&&V12KeepRangedDistance(position,target)){yield return null;continue;}
                if(distance>Mathf.Min(range,world.Session.Config.BasicFor(core).range-.12f)||!sight){DriveP4Combat(target);yield return null;continue;}
                Keys();if(free&&core.majorRemaining<=0)yield return V13ClickWorld(target);else yield return null;
            }
            Keys();MouseState(0);Require(world.Profile.ActiveMode.Room(room).resolved,"Natural V13 failed/timeout: "+room+" / "+world.Message);
            v12RoomEvidence.Add(new{room,realSeconds=Time.realtimeSinceStartup-started,encounters=CombatJson.Clone(world.Profile.ActiveMode.encounters),mode=CombatJson.Clone(world.Profile.ActiveMode)});
            Write("v13-room-evidence.json",v12RoomEvidence);Write("v12-commands.json",v12ObservedCommands);
            WriteV14CombatEvidence();
        }
    }
}
