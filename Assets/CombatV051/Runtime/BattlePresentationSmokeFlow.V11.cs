using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CombatV051.Runtime
{
    // Optional actual-Player input evidence. Never writes combat state, RNG or resources.
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool v11Mode,v11PrerequisiteBlocked,v11UnlockSaved,v11ReloadVerified,v11FormationVerified,v11SkillResolved,v11SkillSucceeded,v11RewardClaimed,v11ContinueVerified;
        string v11ChosenReward;int v11WardAttempts;
        bool v11ExtendedReached,v11PostBuffHit,v11PostBuffFull,v11SecondRoomCleared,v11NewRunClean;
        readonly List<object> v11ExtendedEvidence=new List<object>();
        readonly Dictionary<string,List<P6DamageResult>> v11PublishedDamage=new Dictionary<string,List<P6DamageResult>>();
        readonly HashSet<string> v11PublishedRoots=new HashSet<string>();
        bool V11ExtendedRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-v11-extended")>=0;
        string v11FrozenKey,v11FrozenDto;Vector3 v11FrozenCamera;Quaternion v11FrozenRotation;
        readonly List<object> v11RollFreezeFrames=new List<object>();
        readonly List<object> v11Actions=new List<object>();readonly HashSet<string> v11Observed=new HashSet<string>();
        public void BeginV11(BattleWorld owner,string evidenceDirectory){v11Mode=true;p6Mode=true;p4Mode=true;Begin(owner,evidenceDirectory);owner.CommandPublished+=ObserveV11PublishedCommand;}
        void ObserveV11PublishedCommand(CombatCommand command,CombatResult result)
        {
            try
            {
                if(!v11Mode||result==null||!result.success||result.duplicate)return;
                // The publisher supplies detached committed DTOs before end-of-room cleanup.
                string encounter=world.Profile?.ActiveEncounter?.id??world.State.encounterId;
                if(result.p6?.damages!=null)
                    foreach(var damage in result.p6.damages)
                    {
                        if(!v11PublishedRoots.Add(encounter+"/"+damage.id))continue;
                        if(!v11PublishedDamage.TryGetValue(encounter,out var list))v11PublishedDamage[encounter]=list=new List<P6DamageResult>();
                        list.Add(CombatJson.Clone(damage));
                        v11Actions.Add(new{source="committed-command-before-room-cleanup",encounter,commandId=command.id,damage=CombatJson.Clone(damage),at=Time.realtimeSinceStartup-started});
                    }
                // Preserve full receipt/actor/mark information for a lethal last contact too.
                P6ObserveState(world.State,"committed-command");V11ObserveCombat(world.State,"committed-command");
            }
            catch(Exception error){observerFailure="Committed v1.1 observer: "+error;}
        }
        void ObserveV11Actions()
        {
            if(!v11Mode||world?.Profile==null)return;
            V11ObserveCombat(world.State,"live");
            foreach(var encounter in world.Profile.ActiveMode.encounters)V11ObserveCombat(encounter.currentCombat,"encounter-record");
            ObserveV11Freeze();
        }
        void ObserveV11Freeze()
        {
            var s=world.State;if(s.mode!=CombatMode.RollFreeze||s.pendingRoll?.v11==null)return;
            string key=s.encounterId+"/"+s.pendingRoll.id;
            string frozen=CombatJson.Write(new{s.actors,s.enemies,s.world,s.random,s.focusBudget,s.p6,s.queue,s.charges,s.momentum,s.simulationSeconds});
            if(v11FrozenKey!=key)
            {v11FrozenKey=key;v11FrozenDto=frozen;v11FrozenCamera=world.BattleCamera.transform.position;v11FrozenRotation=world.BattleCamera.transform.rotation;Write("v11-freeze-"+s.rolls.Count+".json",new{ticket=CombatJson.Clone(s.pendingRoll),world=CombatJson.Clone(s)});}
            Require(Time.timeScale==0,"v1.1 ability RollFreeze did not completely pause the world.");
            Require(frozen==v11FrozenDto,"v1.1 ability RollFreeze advanced actors/enemies/world/RNG/resources/Focus/queue.");
            Require(world.BattleCamera.transform.position==v11FrozenCamera&&world.BattleCamera.transform.rotation==v11FrozenRotation,"v1.1 ability RollFreeze camera moved.");
            v11RollFreezeFrames.Add(new{ticket=s.pendingRoll.id,frame=Time.frameCount,worldScale=Time.timeScale,rollElapsed=world.RollElapsed,worldUnchanged=true,cameraUnchanged=true});
        }
        void V11ObserveCombat(CombatState state,string source)
        {
            if(state==null)return;
            foreach(var receipt in state.receipts.Where(r=>r.result?.v11!=null))
            {
                if(!v11Observed.Add(state.encounterId+"/"+receipt.id))continue;
                v11Actions.Add(new{source,state.encounterId,receipt=CombatJson.Clone(receipt),at=Time.realtimeSinceStartup-started});
                var result=receipt.result;
                if(result.v11.effects!=null&&result.v11.effects.Any(e=>e.actorId=="char_story_evan"&&e.kind=="absorption")){v11SkillSucceeded=true;v11SkillResolved=true;}
            }
            if(state.rolls.Any(r=>r.actorId=="char_story_evan"&&r.actionId=="v11_support_ward"&&r.stage=="Resolved"))v11SkillResolved=true;
        }
        void WriteV11Observations()
        {
            if(world.Profile!=null){V11ObserveCombat(world.State,"final");foreach(var e in world.Profile.ActiveMode.encounters)V11ObserveCombat(e.currentCombat,"final-encounter-record");}
            Write("v11-observations.json",new{phase="P6-v1.1",v11PrerequisiteBlocked,v11UnlockSaved,v11ReloadVerified,v11FormationVerified,v11SkillResolved,v11SkillSucceeded,v11WardAttempts,v11RewardClaimed,v11ChosenReward,v11ContinueVerified,
                profile=world.Profile?.State.meta.v11,departure=world.Profile?.ActiveMode.v11Departure,build=world.Profile?.ActiveMode.v11Build,runtime=world.Session?.Config.v11,actions=v11Actions,
                smiteActions=p6Ledger,combatInputs=p6Inputs,smiteFreezeFrames=p6FrozenFrames,p6BaseDamage,p6FullDamage,p6MarkedFullDamage,p6FormalBind,p6RoomCleared,
                newAbilityFreezeFrames=v11RollFreezeFrames,
                extendedRequested=V11ExtendedRequested,v11ExtendedReached,v11PostBuffHit,v11PostBuffFull,v11SecondRoomCleared,v11NewRunClean,extendedEvidence=v11ExtendedEvidence,
                committedDamageByEncounter=v11PublishedDamage,
                extendedScope="Optional original R3 after R1 reward, then actual return to R0 / abandon settlement / growth / new departure. R4, R5, R7 and a full three-reward route are NOT_RUN by this harness.",
                saveVerification="Actual UI purchases followed by in-process reload of the isolated profile through the existing loader. Not an OS process restart.",
                combatVerification="Original R1, natural RNG, existing AI, real WASD/F/Q/UI. Failed rolls and unsuccessful trials are retained. No HP/position/resources/RNG/dice configuration is assigned.",
                humanPlayers=0,humanRuns=0,normalWindowPresentationVerified=false,usesGameplayStateFixtures=false,usesInjectedResources=false,usesFixedSuccessDice=false});
        }
        IEnumerator V11ClickBlocked(string id)
        {
            Keys();yield return null;world.Hud.RefreshView();yield return null;Canvas.ForceUpdateCanvases();
            var button=world.Hud.Canvas.GetComponentsInChildren<Button>(true).Single(b=>b.name==id&&b.gameObject.activeInHierarchy);
            Require(!button.interactable,"Expected a legitimately blocked UI option: "+id);
            var rect=(RectTransform)button.transform;pointer=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
            var rays=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=pointer},rays);
            Require(rays.Count>0&&(rays[0].gameObject==button.gameObject||rays[0].gameObject.transform.IsChildOf(button.transform)),"Blocked node raycast was obscured: "+id);
            int clicked=0;UnityEngine.Events.UnityAction callback=()=>clicked++;button.onClick.AddListener(callback);
            MouseState(0);yield return null;MouseState(1);yield return null;MouseState(0);yield return null;yield return null;
            button.onClick.RemoveListener(callback);Require(clicked==0,"Disabled purchase dispatched a click.");
            events.Add(new{kind="actual_disabled_pointer",id,at=Time.realtimeSinceStartup-started});
        }
        IEnumerator V11RequestBind()
        {
            var caster=world.RadianceCaster;if(caster==null)yield break;string previous=caster.p6?.bindIntent?.id,active=world.State.activeActorId;
            yield return Press(Key.F);var intent=world.State.Actor(caster.id)?.p6?.bindIntent;
            bool accepted=intent!=null&&intent.id!=previous;if(accepted)p6ActualBindInputs.Add(intent.id);
            p6Inputs.Add(new{kind="actual_F_v11",caster=caster.id,accepted,intentId=intent?.id,activeBefore=active,activeAfter=world.State.activeActorId,at=Time.realtimeSinceStartup-started});
            Require(world.State.activeActorId==active,"F must not replace the controlled character.");
        }
        IEnumerator V11TryWard()
        {
            var evan=world.State.Actor("char_story_evan");if(evan?.Alive!=true||world.Session.AvailableSharedCharges<=0||!world.Session.P6MajorAvailable(evan))yield break;
            yield return Press(Key.Tab);if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            yield return P6Click("rogue"); // The existing second tactical slot now represents Evan.
            if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            Require(world.SelectedActor=="char_story_evan","Second tactical slot selected the wrong identity.");
            yield return P6Click("v11-action-1");if(world.State.mode!=CombatMode.TacticalFocus)yield break;
            if(!world.SelectedActionQuote().legal){yield return P6Click("exit");yield break;}
            yield return P6Click("confirm");var ticket=world.State.pendingRoll;if(ticket?.actionId!="v11_support_ward")yield break;
            v11WardAttempts++;Write("v11-ward-"+v11WardAttempts+"-ticket.json",CombatJson.Clone(ticket));
            yield return CaptureP47("07-v11-ward-roll-"+v11WardAttempts+".png");
            yield return Wait(()=>world.State.mode!=CombatMode.RollFreeze,8,"New ability dice did not reveal naturally.");
            if(world.State.mode==CombatMode.PostRollDecision)yield return P6Click("commit");
            if(world.State.mode==CombatMode.TacticalFocus)yield return P6Click("exit");
            float deadline=Time.realtimeSinceStartup+4;
            while(Time.realtimeSinceStartup<deadline&&world.State.rolls.Any(t=>t.id==ticket.id&&(t.stage=="Queued"||t.stage=="Executing")))yield return null;
            ObserveV11Actions();
        }
        IEnumerator FlowV11()
        {
            if(V13LegacyPath!=null){yield return FlowV13Legacy();yield break;}
            if(V12FullRequested){yield return FlowV12Route();yield break;}
            if(V12RoomRequested){yield return FlowV12Room();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-hero-pass")>=0){yield return FlowHeroes();yield break;}
            yield return null;yield return null;
            world.InitializeP6FrontendForTests(Path.Combine(directory,"Profiles","profile.json"));
            stage="v11-main";Require(world.Menu&&world.FrontPage=="main","Ordinary title was not reached.");
            yield return CaptureP47("00-v11-main.png");
            yield return FrontP47("campaign");Require(world.Menu&&world.FrontChoices.Any(c=>c.id=="training"),"Campaign lacks its real Demo training path.");yield return FrontP47("main");
            yield return FrontP47("characters");stage="v11-roster";
            Require(world.FrontChoices.Count(c=>c.id.StartsWith("role:"))==7,"Roster does not show 3 main and all 4 original companions.");
            yield return CaptureP47("01-v11-roster.png");
            yield return FrontP47("role:char_story_evan");yield return FrontP47("tree-next");Require(world.V11CurrentCharacter=="char_story_mira","Right arrow did not switch character.");
            yield return FrontP47("tree-prev");Require(world.V11CurrentCharacter=="char_story_evan","Left arrow did not restore Evan.");
            stage="v11-node-prerequisite";yield return FrontP47("node:char_story_evan/active2");int points=world.Profile.V11Progress.points;
            yield return V11ClickBlocked("front-node-confirm");v11PrerequisiteBlocked=world.Profile.V11Progress.points==points&&world.Profile.QuoteV11Node("char_story_evan","char_story_evan/active2").status=="Locked";
            Require(v11PrerequisiteBlocked,"Blocked node changed permanent points or unlocks.");yield return CaptureP47("02-v11-tree-blocked.png");
            yield return FrontP47("node:char_story_evan/passive");yield return FrontP47("node-confirm");yield return V11ClickBlocked("front-node-confirm");
            yield return FrontP47("node:char_story_evan/active2");yield return FrontP47("node-confirm");
            v11UnlockSaved=world.Profile.V11Progress.points==points-2&&world.Profile.QuoteV11Node("char_story_evan","char_story_evan/active2").status=="Unlocked";
            Require(v11UnlockSaved,"Actual two-node unlock did not debit exactly two Demo points.");yield return CaptureP47("03-v11-tree-unlocked.png");
            var progress=CombatJson.Write(world.Profile.State.meta.v11);Write("v11-unlock-before-reload.json",world.Profile.State.meta.v11);
            yield return FrontP47("characters");yield return FrontP47("main");
            string isolated=world.SavePath;world.InitializeP6FrontendForTests(isolated);yield return null;
            yield return FrontP47("characters");yield return FrontP47("role:char_story_evan");yield return FrontP47("node:char_story_evan/active2");
            v11ReloadVerified=CombatJson.Write(world.Profile.State.meta.v11)==progress;Require(v11ReloadVerified,"Profile loader lost unlocks or reclaimed Demo points.");
            Write("v11-unlock-after-reload.json",world.Profile.State.meta.v11);yield return CaptureP47("04-v11-reloaded-tree.png");
            Record("v11_ui_unlock_persistence","Actual pointer main/roster/tree/arrows; blocked prerequisite and disabled repeat; two real purchases; isolated profile disk reload preserved progress exactly.");
            yield return FrontP47("characters");yield return FrontP47("explore");yield return FrontP47("map-ashen");
            stage="v11-formation";yield return FrontP47("slot:0");yield return FrontP47("member:char_story_mira");
            Require(string.IsNullOrEmpty(world.Profile.V11PreparationDraft.coreId),"Removed core was silently reassigned.");yield return V11ClickBlocked("front-depart");
            yield return FrontP47("member:char_demo_paladin");yield return FrontP47("core:char_demo_paladin");
            yield return FrontP47("slot:1");yield return FrontP47("member:char_story_evan");yield return FrontP47("slot:2");yield return FrontP47("member:char_story_finn");
            var draft=world.Profile.V11PreparationDraft;v11FormationVerified=draft.teamIds.SequenceEqual(new[]{P6Paladin,"char_story_evan","char_story_finn"})&&draft.coreId==P6Paladin;
            Require(v11FormationVerified,"Preparation did not save three distinct selected identities and Paladin core.");Write("v11-preparation.json",draft);yield return CaptureP47("05-v11-preparation.png");
            yield return FrontP47("depart");Require(world.Training,"Fresh run skipped existing novice training unexpectedly.");p6PracticeStatus="SKIPPED_VIA_SHIPPING_UI_NOT_TESTED";yield return P6Click("p6-finish");
            Require(!world.Menu&&!world.Training&&world.Session.Config.v11!=null,"Exploration lacks its authoritative v1.1 snapshot.");
            Require(world.State.actors.Select(a=>a.id).SequenceEqual(draft.teamIds),"Actual battle team differs from saved preparation.");
            Require(world.AvailableActions("char_story_evan").Contains("v11_support_ward"),"Permanently unlocked ability did not reach the runtime loadout.");
            Write("v11-departure.json",new{world.Profile.ActiveMode.v11Departure,world.Session.Config.v11,world.State.actors});
            p6FormalStarted=Time.realtimeSinceStartup;stage="v11-walk-r1";yield return WalkTo(()=>new Vector3(26,0,0),.6f,55,()=>world.Profile.IsInEncounter);
            Require(world.Profile.IsInEncounter&&world.Profile.ActiveRoom.templateId=="R1","Actual movement did not reach authored R1.");
            p6Encounter=world.Profile.ActiveEncounter.id;yield return CaptureP47("06-v11-three-person-r1.png");
            float deadline=Time.realtimeSinceStartup+180,nextAction=0;bool bindRequested=false;
            while(world.Profile.ActiveEncounter?.id==p6Encounter&&Time.realtimeSinceStartup<deadline)
            {
                Require(observerFailure==null,observerFailure);ObserveP6Actions();ObserveV11Actions();Require(world.State.Actor(P6Paladin).Alive,"Natural trial: Paladin was defeated.");
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return P6Click("commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                var enemy=Enemy(false);if(enemy==null){Keys();yield return null;continue;}
                if(!v11SkillResolved&&v11WardAttempts<2&&world.Session.AvailableSharedCharges>0&&Time.realtimeSinceStartup>=nextAction)
                {yield return V11TryWard();nextAction=Time.realtimeSinceStartup+.25f;continue;}
                var paladin=world.State.Actor(P6Paladin);float distance=BattleGeometry.DistanceXZ(BattleWorld.Position(paladin),BattleWorld.Position(enemy));
                if(distance>2.05f||!world.HasLineOfEffect(BattleWorld.Position(paladin),BattleWorld.Position(enemy))){DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                Keys();
                if(!bindRequested&&world.RadianceCaster!=null&&world.Session.QuoteBind(world.RadianceCaster.id,enemy.id).legal)
                {yield return V11RequestBind();bindRequested=true;continue;}
                if(world.Session.AvailableSharedCharges>0&&world.Session.P6MajorAvailable(paladin)&&Time.realtimeSinceStartup>=nextAction)
                {stage="v11-natural-smite";yield return P6TrySmite(enemy.id,world.Session.AvailableSharedCharges>=2,false);nextAction=Time.realtimeSinceStartup+.3f;continue;}
                if(p6BaseDamage||p6FullDamage){yield return ClickWorld(BattleWorld.Position(enemy));continue;}
                AimAt(BattleWorld.Position(enemy));MouseState(2);yield return new WaitForSecondsRealtime(.18f);MouseState(0);yield return null;
            }
            Keys();MouseState(0);ObserveP6Actions();ObserveV11Actions();
            if(!world.State.enemies.Any(e=>e.Alive))yield return Wait(()=>world.Profile.ActiveMode.Room("R1").resolved,6,"Natural last contact did not settle R1.");
            p6RoomCleared=world.Profile.ActiveMode.Room("R1").resolved;
            Require(p6RoomCleared,"Natural R1 did not clear within the bounded trial.");
            Require(v11SkillResolved,"Unlocked new ability never completed an actual natural roll/execution.");
            Require(v11RollFreezeFrames.Count>0,"No actual freeze frame was observed for the new ability.");
            Require(p6BaseDamage||p6FullDamage,"No naturally successful Smite was observed; keep this failed trial.");
            stage="v11-reward";yield return Wait(()=>world.Profile.PendingReward!=null&&world.DungeonModal,4,"Cleared room did not offer its real build reward.");
            yield return CaptureP47("08-v11-build-reward.png");var offer=CombatJson.Clone(world.Profile.PendingReward);
            v11ChosenReward=V11ChooseNaturalReward(offer);
            yield return ClickChoice("reward:"+v11ChosenReward);
            v11RewardClaimed=world.Profile.ActiveMode.v11Build.buffs.Any(b=>b.id==v11ChosenReward&&b.stacks>0)&&world.Session.Config.v11.buffs.Any(b=>b.id==v11ChosenReward&&b.stacks>0);
            Require(v11RewardClaimed,"Selected reward did not reach both persisted build and runtime authority.");
            Write("v11-reward.json",new{offer,chosen=v11ChosenReward,world.Profile.ActiveMode.v11Build,world.Session.Config.v11});
            stage="v11-save-continue";yield return Press(Key.Escape);yield return ClickUi("save");
            string actors=CombatJson.Write(world.State.actors),build=CombatJson.Write(world.Profile.ActiveMode.v11Build),departure=CombatJson.Write(world.Profile.ActiveMode.v11Departure),run=world.Profile.ActiveMode.id;
            yield return FrontP47("explore");yield return FrontP47("continue-exploration");
            v11ContinueVerified=world.Profile.ActiveMode.id==run&&CombatJson.Write(world.State.actors)==actors&&CombatJson.Write(world.Profile.ActiveMode.v11Build)==build&&CombatJson.Write(world.Profile.ActiveMode.v11Departure)==departure;
            Require(v11ContinueVerified,"Continue changed squad resources, departure or this run's reward.");
            yield return CaptureP47("09-v11-continued-build.png");WriteV11Observations();
            Record("v11_natural_exploration","Actual selected three-person squad, unlocked ability execution, natural Smite and R1 clear, real reward selection applied to runtime, exact pause/save/continue snapshot. Skill success is separately recorded from execution.");
            if(V11ExtendedRequested)yield return V11ExtendedRun();
            completed=true;
        }
        string V11ChooseNaturalReward(RewardOffer offer)
        {
            // A player preference among the actual offered candidates; the offer and RNG are never changed.
            foreach(string desired in new[]{"core_smite_dice","core_full_dice","core_radiant_resonance","core_smite_accuracy"})
                if(offer.candidates.Contains(desired))return desired;
            return offer.candidates.FirstOrDefault(id=>V11BuffCatalog.Find(id)?.pool=="Core")??offer.candidates[0];
        }
        IEnumerator V11OptionalEvent(string id)
        {
            var definition=world.DungeonConfig.Event(id);stage="v11-extended-event-"+id;
            yield return WalkTo(()=>new Vector3(definition.x,0,definition.z),1.1f,55);yield return Press(Key.E);
            if(!world.DungeonModal){v11ExtendedEvidence.Add(new{kind="event_not_opened",id});yield break;}
            var actor=world.DungeonChoices.FirstOrDefault(c=>c.id=="actor:char_story_evan"&&c.enabled)??world.DungeonChoices.FirstOrDefault(c=>c.id.StartsWith("actor:")&&c.enabled);
            if(actor!=null)yield return ClickChoice(actor.id);
            if(world.DungeonChoices.Any(c=>c.id=="event-roll"&&c.enabled))yield return ClickChoice("event-roll");
            if(world.Profile.ActiveSafeEvent!=null)v11ExtendedEvidence.Add(new{kind="natural_safe_event",id,state=CombatJson.Clone(world.Profile.ActiveSafeEvent)});
            if(world.DungeonChoices.Any(c=>c.id=="event-accept"&&c.enabled))yield return ClickChoice("event-accept");
            if(world.DungeonModal&&world.DungeonChoices.Any(c=>c.id=="close"))yield return ClickChoice("close");
            Require(!world.DungeonModal,"Independent event retained a modal; do not bypass its decision.");
        }
        IEnumerator V11ExtendedRun()
        {
            v11ExtendedReached=true;stage="v11-extended-resume";
            Require(world.State.mode==CombatMode.CombatPaused,"Save/Continue no longer preserves the deliberate pause; inspect actual state before moving.");
            yield return Press(Key.Escape);Require(world.State.mode!=CombatMode.CombatPaused,"Actual Escape did not resume the preserved mode.");
            string firstRun=world.Profile.ActiveMode.id;string permanentNodes=CombatJson.Write(world.Profile.State.meta.v11.unlockedNodeIds);int permanentPoints=world.Profile.State.meta.v11.points;
            stage="v11-extended-r2";yield return WalkTo(()=>new Vector3(52,0,2),1.1f,55);yield return Wait(()=>world.Profile.ActiveRoom.templateId=="R2",8,"Followers did not reach R2.");
            yield return V11OptionalEvent("EVENT_A");
            stage="v11-extended-r3-entry";var room=world.DungeonConfig.Room("R3");
            yield return WalkTo(()=>new Vector3(room.x,0,room.z),.6f,55,()=>world.Profile.IsInEncounter);
            Require(world.Profile.IsInEncounter&&world.Profile.ActiveRoom.templateId=="R3","Actual movement did not activate original R3.");
            string encounterId=world.Profile.ActiveEncounter.id;var entryRules=CombatJson.Clone(world.Session.Config.v11);
            Require(entryRules.buffs.Count>0&&entryRules.buffs.Any(b=>b.id==v11ChosenReward),"R1 selected buff was absent from the next encounter's runtime rules.");
            v11ExtendedEvidence.Add(new{kind="r3_entry",encounterId,entryRules,actors=CombatJson.Clone(world.State.actors),charges=world.State.charges});
            yield return CaptureP47("10-v11-upgraded-r3-entry.png");
            float deadline=Time.realtimeSinceStartup+180,nextAttempt=0;bool bindRequested=false;
            while(world.Profile.ActiveEncounter?.id==encounterId&&Time.realtimeSinceStartup<deadline)
            {
                Require(observerFailure==null,observerFailure);ObserveP6Actions();ObserveV11Actions();
                Require(world.State.Actor(P6Paladin)?.Alive==true,"Natural upgraded trial: Paladin defeated; do not refill or retry this state.");
                if(world.State.mode==CombatMode.ReactionPrompt){yield return P6Click("reaction-decline");continue;}
                if(world.State.mode==CombatMode.RollFreeze){yield return null;continue;}
                if(world.State.mode==CombatMode.PostRollDecision){yield return P6Click("commit");continue;}
                if(world.State.mode==CombatMode.TacticalFocus){yield return P6Click("exit");continue;}
                var enemy=Enemy(false);if(enemy==null){Keys();yield return null;continue;}
                var caster=world.RadianceCaster;
                if(!bindRequested&&caster!=null&&world.Session.QuoteBind(caster.id,enemy.id).legal)
                {AimAt(BattleWorld.Position(enemy));yield return V11RequestBind();bindRequested=true;continue;}
                var paladin=world.State.Actor(P6Paladin);float distance=BattleGeometry.DistanceXZ(BattleWorld.Position(paladin),BattleWorld.Position(enemy));
                if(distance>2.05f||!world.HasLineOfEffect(BattleWorld.Position(paladin),BattleWorld.Position(enemy)))
                {DriveP4Combat(BattleWorld.Position(enemy));yield return null;continue;}
                Keys();
                var pendingBind=world.RadianceCaster?.p6?.bindIntent;
                bool waitingForMark=bindRequested&&pendingBind!=null&&(pendingBind.stage=="Casting"||pendingBind.stage=="WaitingBasic")&&pendingBind.targetId==enemy.id;
                if(!waitingForMark&&world.Session.AvailableSharedCharges>0&&world.Session.P6MajorAvailable(paladin)&&Time.realtimeSinceStartup>=nextAttempt)
                {stage="v11-upgraded-smite";yield return P6TrySmite(enemy.id,world.Session.AvailableSharedCharges>=2,false);nextAttempt=Time.realtimeSinceStartup+.3f;continue;}
                AimAt(BattleWorld.Position(enemy));MouseState(2);yield return new WaitForSecondsRealtime(.16f);MouseState(0);yield return null;
            }
            Keys();MouseState(0);ObserveP6Actions();ObserveV11Actions();
            if(!world.State.enemies.Any(e=>e.Alive))yield return Wait(()=>world.Profile.ActiveMode.Room("R3").resolved,6,"Upgraded encounter final contact did not settle R3.");
            v11SecondRoomCleared=world.Profile.ActiveMode.Room("R3").resolved;
            var recordedDamage=v11PublishedDamage.TryGetValue(encounterId,out var published)?published:new List<P6DamageResult>();
            var hits=recordedDamage.Where(d=>d.actionId==P6Smite&&d.totalDamage>0&&d.hit).ToArray();
            v11PostBuffHit=hits.Length>0;v11PostBuffFull=hits.Any(d=>d.overcharged);
            v11ExtendedEvidence.Add(new{kind="r3_result",encounterId,v11SecondRoomCleared,v11PostBuffHit,v11PostBuffFull,damage=CombatJson.Clone(recordedDamage),rules=entryRules,actorsAfterRoomCleanup=CombatJson.Clone(world.State.actors)});
            Write("v11-upgraded-combat.json",v11ExtendedEvidence);
            Require(v11SecondRoomCleared,"Original R3 was not naturally cleared within the bounded trial.");
            Require(v11PostBuffHit,"Original R3 cleared without a natural post-upgrade Smite hit; this remains a failed evidence trial.");
            yield return CaptureP47("11-v11-upgraded-r3-cleared.png");
            if(world.Profile.PendingReward!=null)
            {
                var offer=CombatJson.Clone(world.Profile.PendingReward);string selected=V11ChooseNaturalReward(offer);
                yield return ClickChoice("reward:"+selected);v11ExtendedEvidence.Add(new{kind="r3_reward",offer,selected,build=CombatJson.Clone(world.Profile.ActiveMode.v11Build)});
            }
            // Return through already opened passages, observing each real room transition.
            foreach(string id in new[]{"R2","R1","R0"})
            {
                stage="v11-return-"+id;var destination=world.DungeonConfig.Room(id);
                yield return WalkTo(()=>new Vector3(destination.x,0,destination.z),1.1f,55);
                yield return Wait(()=>world.Profile.ActiveRoom.templateId==id,8,"The party did not finish returning to "+id);
            }
            stage="v11-abandon-settlement";yield return Press(Key.E);
            Require(world.DungeonChoices.Any(c=>c.id=="settle:abandon"&&c.enabled),"Existing entrance UI does not expose legal abandonment for this live Demo run; do not fabricate tutorial unlock.");
            var oldBuild=CombatJson.Clone(world.Profile.ActiveMode.v11Build);yield return CaptureP47("12-v11-return-hub.png");yield return ClickChoice("settle:abandon");
            Require(world.Profile.State.meta.settledRunIds.Count(id=>id==firstRun)==1,"Abandonment did not settle the real run once.");
            yield return CaptureP47("13-v11-abandon-settled.png");yield return ClickChoice("growth-menu");Require(world.Menu&&world.FrontPage=="characters","Settlement did not return to character growth.");
            yield return FrontP47("explore");yield return FrontP47("map-ashen");yield return FrontP47("depart");
            Require(!world.Training,"The completed/skipped first tutorial unexpectedly restarted on the next run.");
            v11NewRunClean=!world.Menu&&world.Profile.ActiveMode.id!=firstRun&&world.Profile.ActiveMode.v11Build.buffs.Count==0&&world.Session.Config.v11.buffs.Count==0&&
                CombatJson.Write(world.Profile.State.meta.v11.unlockedNodeIds)==permanentNodes&&world.Profile.State.meta.v11.points==permanentPoints&&
                world.State.actors.All(a=>!a.statuses.Any(s=>s.id=="v11-afterlight"||s.id=="v11-oath-guard"));
            Require(v11NewRunClean,"New run inherited buffs/status or lost permanent unlocks; preserve both actual saves.");
            v11ExtendedEvidence.Add(new{kind="next_run",previous=firstRun,previousBuild=oldBuild,newRun=world.Profile.ActiveMode.id,newBuild=CombatJson.Clone(world.Profile.ActiveMode.v11Build),permanent=CombatJson.Clone(world.Profile.State.meta.v11),actors=CombatJson.Clone(world.State.actors)});
            yield return CaptureP47("14-v11-next-run-empty-build.png");WriteV11Observations();
            Record("v11_extended_two_encounter_loop","R1 reward was applied to a natural Smite in original R3; actual R3 clear/reward; walked back to R0, used legal abandonment settlement, returned to growth and began a distinct empty-buff run. R4/R5/R7 and three rewards were not played.");
        }
    }
}
