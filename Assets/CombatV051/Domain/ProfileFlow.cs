using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        public EnemyTemplate EnemyTemplateFor(string actorId)
        {string id;return ActiveCombat.State.world.strings.TryGetValue("enemyTemplate/"+actorId,out id)?ConfigFor(ActiveMode).Enemy(id):null;}
        public bool IsPassageOpen(string id){return PassageOpen(ActiveMode,id);}
        bool PassageOpen(ModeState mode,string id)
        {
            var p=ConfigFor(mode).passages.Find(x=>x.id==id);if(p==null)return false;
            var encounter=Encounter(mode);if(encounter!=null){var room=mode.Room(encounter.roomId);if(p.roomA==room.templateId||p.roomB==room.templateId)return false;}
            if(mode.pendingReward!=null&&mode.pendingReward.stage=="Offered"){var room=mode.Room(mode.pendingReward.roomId);if(p.roomA==room.templateId||p.roomB==room.templateId)return false;}
            if(HasV12Map(mode))return V12PassageOpen(mode,p);
            switch(id)
            {
                case "P01":return true;
                case "P12":case "P23":return mode.Room("R1").resolved;
                case "P24_SMASH":return mode.Room("R3").resolved&&mode.HasAbility("SMASH")&&mode.openedGateIds.Contains(id);
                case "P24_OUTER":return mode.kind==ProfileModeKind.Challenge&&!mode.HasAbility("SMASH")&&mode.Room("R3").resolved;
                case "P45":return mode.Room("R4").resolved;
                case "P56":case "P67":return mode.Room("R5").resolved;
                case "P60_SHORTCUT":return mode.openedGateIds.Contains(id);
                default:return false;
            }
        }
        public bool CanEnterRoom(string roomId)
        {
            var room=ActiveMode.Room(roomId);if(room==null||!IsSafe||ActiveSafeEvent!=null)return false;if(room.id==ActiveRoom.id)return true;
            return ConfigFor(ActiveMode).passages.Any(p=>((p.roomA==ActiveRoom.templateId&&p.roomB==room.templateId)||(p.roomB==ActiveRoom.templateId&&p.roomA==room.templateId))&&PassageOpen(ActiveMode,p.id));
        }
        static bool Inside(ActorState actor,RoomTemplate room,float margin=0){return Math.Abs(actor.x-room.x)<=room.width/2-margin&&Math.Abs(actor.z-room.z)<=room.depth/2-margin;}
        static bool Near(ActorState actor,float x,float z,float range){float dx=actor.x-x,dz=actor.z-z;return dx*dx+dz*dz<=range*range;}
        ProfileResult ApplyProfile(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;var result=new ProfileResult();
            switch(c.kind)
            {
                case ProfileCommandKind.SaveCurrent:return result;
                case ProfileCommandKind.SaveP6Preparation:return ApplyP6PreparationCommand(s,c);
                case ProfileCommandKind.EnsureV11Progress:case ProfileCommandKind.UnlockV11Node:case ProfileCommandKind.SaveV11Preparation:return ApplyV11ProfileCommand(s,c);
                case ProfileCommandKind.SavePreparation:case ProfileCommandKind.UnlockSpecialization:case ProfileCommandKind.ApplyStoryPreparation:return ApplyPreparationCommand(s,c);
                case ProfileCommandKind.UnlockChallenge:
                    Require(s.story.Room("R1").resolved || (mode.kind==ProfileModeKind.Challenge && mode.HasFlag("DEMO_EXPLORATION") && mode.Room(HasV12Map(mode)?"V12_R1":"R1").resolved && mode.Room(HasV12Map(mode)?"V12_R2":"R2").visited && c.qualified),"tutorial_encounter_incomplete");
                    s.meta.challengeUnlocked=true;return result;
                case ProfileCommandKind.StartDemoExploration:
                case ProfileCommandKind.StartChallenge:
                {
                    EnsureSafe(mode);EnsureRewardFinished(mode);Require(c.kind==ProfileCommandKind.StartDemoExploration||s.meta.challengeUnlocked,"challenge_locked");Require(s.challenge==null||s.challenge.stage=="Settled","challenge_already_active");
                    string law=c.selectionId??"BASE";Require(s.meta.unlockedLaws.Contains(law)&&Config.Rule(law)!=null&&Config.Rule(law).kind=="Law","law_locked_or_invalid");
                    long number=++s.modeSequence;ulong seed=c.seed==0?s.seed^((ulong)number*0x9E3779B97F4A7C15UL):c.seed;
                    s.challenge=CreateMode(s,ProfileModeKind.Challenge,s.profileId+"/challenge-"+number,seed,law);
                    if(c.kind==ProfileCommandKind.StartDemoExploration)AddOnce(s.challenge.flags,"DEMO_EXPLORATION");
                    s.activeMode=ProfileModeKind.Challenge;return result;
                }
                case ProfileCommandKind.StartStory:case ProfileCommandKind.ContinueStory:if(mode.stage!="Settled")EnsureSafe(mode);Require(string.IsNullOrEmpty(mode.activeEventId),"finish_safe_event_first");EnsureRewardFinished(mode);s.activeMode=ProfileModeKind.Story;return result;
                case ProfileCommandKind.ContinueChallenge:EnsureSafe(mode);EnsureRewardFinished(mode);Require(s.challenge!=null&&s.challenge.stage!="Settled","no_active_challenge");s.activeMode=ProfileModeKind.Challenge;return result;
                case ProfileCommandKind.EnterRoom:
                {
                    EnsureSafe(mode);EnsureRewardFinished(mode);Require(c.qualified,"world_fact_required");var target=mode.Room(c.roomId);Require(target!=null,"unknown_room");var from=mode.Room(mode.currentRoomId);
                    if(target.id==from.id)return result;
                    Require(ConfigFor(mode).passages.Any(p=>((p.roomA==from.templateId&&p.roomB==target.templateId)||(p.roomB==from.templateId&&p.roomA==target.templateId))&&PassageOpen(mode,p.id)),"passage_closed");
                    var combat=Current(mode);Require(Inside(combat.Actor(combat.activeActorId),ConfigFor(mode).Room(target.templateId)),"not_inside_target_room");mode.currentRoomId=target.id;target.visited=true;if(target.stage=="Unvisited")target.stage="Safe";combat.world.strings["roomTemplateId"]=target.templateId;V12RoomEntered(mode,target);return result;
                }
                case ProfileCommandKind.ActivateEncounter:return Activate(s,c);
                case ProfileCommandKind.ResolveEncounter:return Resolve(s,c);
                case ProfileCommandKind.RetryStoryEncounter:return RetryStory(s,c);
                case ProfileCommandKind.OpenGate:
                {
                    EnsureSafe(mode);Require(c.qualified&&c.gateId=="P24_SMASH","invalid_gate_fact");Require(mode.Room("R3").resolved&&mode.HasAbility("SMASH"),"smash_ability_required");
                    var actor=Current(mode).Actor(c.actorId);Require(actor!=null&&actor.Alive&&Near(actor,48,11,5),"gate_out_of_reach");AddOnce(mode.openedGateIds,c.gateId);return result;
                }
                case ProfileCommandKind.OpenShortcut:
                {
                    EnsureSafe(mode);Require(c.qualified&&c.gateId=="P60_SHORTCUT"&&mode.Room(mode.currentRoomId).templateId=="R6","shortcut_inner_side_required");var actor=Current(mode).Actor(c.actorId);Require(actor!=null&&actor.Alive&&actor.z>=11&&Near(actor,0,11,5),"shortcut_out_of_reach");AddOnce(mode.openedGateIds,c.gateId);return result;
                }
                case ProfileCommandKind.UseEnvironment:return UseEnvironment(s,c);
                case ProfileCommandKind.OpenEvent:case ProfileCommandKind.RollEvent:case ProfileCommandKind.RerollEvent:case ProfileCommandKind.ConfirmEvent:case ProfileCommandKind.UseEventAlternative:case ProfileCommandKind.CloseEvent:return ApplyEvent(s,c);
                case ProfileCommandKind.ShortRest:return Rest(s,c);
                case ProfileCommandKind.TrainParty:return TrainParty(s,c);
                case ProfileCommandKind.ClaimReward:case ProfileCommandKind.SkipReward:return Reward(s,c);
                case ProfileCommandKind.SettlementOnce:return Settle(s,c);
                case ProfileCommandKind.SetFocusScale:Require(c.amount==0||c.amount>=.10f&&c.amount<=.15f,"invalid_focus_scale");s.focusScale=c.amount;return result;
                default:throw new InvalidOperationException("unsupported_profile_command");
            }
        }
        ProfileResult Activate(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;EnsureSafe(mode);EnsureRewardFinished(mode);Require(c.qualified,"world_fact_required");var room=mode.Room(mode.currentRoomId);var template=ConfigFor(mode).Room(room.templateId);
            Require(!room.resolved&&!string.IsNullOrEmpty(template.encounterKind)&&string.IsNullOrEmpty(room.encounterId),"encounter_already_resolved_or_not_available");
            var exploration=Current(mode);Require(exploration.actors.Any(a=>a.Alive)&&exploration.actors.Where(a=>a.Alive&&CombatSession.V13Physical(exploration,a)).All(a=>Inside(a,template,1)),"party_not_inside_room");
            var purpose=mode.v12Run?.purposes.Find(p=>p.roomId==room.templateId);
            if(purpose?.optionalEncounter==true)Require(Near(exploration.Actor(exploration.activeActorId),purpose.triggerX,purpose.triggerZ,purpose.triggerRadius),"v12_optional_patrol_not_approached");
            string id=room.id+"/encounter";ulong seed=((ulong)RandomStreams.NextUInt(mode.random.layout)<<32)|RandomStreams.NextUInt(mode.random.layout);
            var record=new EncounterRecord{id=id,roomId=room.id,attemptId=id+"/attempt-0",seed=seed,resolvedConfig=BuildConfig(mode),chargeCap=BuildCap(mode),initialCharge=BuildStartCharge(mode)};record.configHash=Hash(record.resolvedConfig);
            var combat=CombatState.CreatePartyForPreset(s.profileId,mode.id,seed,record.resolvedConfig,false);combat.encounterId=record.attemptId;combat.actors=CombatJson.Clone(exploration.actors);combat.activeActorId=exploration.activeActorId;combat.commandActorId=exploration.commandActorId;combat.charges=record.initialCharge;combat.chargeCap=record.chargeCap;combat.world=CombatJson.Clone(exploration.world);combat.world.telegraphs.Clear();combat.world.projectiles.Clear();combat.enemies.Clear();if(record.resolvedConfig.v13!=null){combat.v13=CombatJson.Clone(exploration.v13);combat.simulationSeconds=exploration.simulationSeconds;}
            foreach(var spawn in room.enemySpawns)
            {
                if(record.resolvedConfig.v13!=null&&room.templateId=="V12_R6"&&spawn.templateId!="boss")continue;
                if(spawn.bossMechanism&&mode.HasFlag("BOSS_MECHANISM_DISABLED"))continue;var enemy=ConfigFor(mode).Enemy(spawn.templateId);string actorId=id+"/"+spawn.id;
                combat.enemies.Add(new ActorState{id=actorId,role=ActorRole.Enemy,isEnemy=true,x=spawn.x+(HasV12Map(mode)&&room.templateId=="V12_R3"?(float)Math.Sin(exploration.simulationSeconds*.5f)*.8f:0),z=spawn.z,lockedX=template.x,lockedZ=template.z,hp=enemy.hp,maxHp=enemy.hp,armorClass=enemy.armorClass,attackBonus=enemy.attackBonus,saveBonus=enemy.saveBonus,dexSaveBonus=enemy.dexSaveBonus,wisSaveBonus=enemy.wisSaveBonus,breakValue=enemy.breakMaximum,breakMaximum=enemy.breakMaximum,inspiration=0,superiority=0,spellSlots=0,items=0});
                combat.world.strings["enemyTemplate/"+actorId]=enemy.id;
            }
            CombatSession.InitializeP5State(combat,record.resolvedConfig);
            CombatSession.ResetP5EncounterState(combat);
            CombatSession.ConfigureP4Encounter(combat,template.encounterKind,objectiveRoots:room.templateId=="R5"?new[]{mode.id+"/R5_SHIFT"}:null,
                highThreatRoots:combat.enemies.Where(e=>new[]{"elite","boss","mechanism"}.Contains(combat.world.strings["enemyTemplate/"+e.id])).Select(e=>e.id));
            CombatSession.ResetP4ReactionSpecializations(combat);
            combat.focusBudget=CombatJson.Clone(exploration.focusBudget);
            combat.focusBudget.recoveryMultiplier=mode.build.relicSlots.Contains("FOCUS")?FocusBudgetRules.FocusRelicMultiplier:1;
            CombatSession.InitializeP6State(combat,record.resolvedConfig,false);
            CombatSession.ResetP6EncounterState(combat);
            CombatSession.V14ResetDamageEncounter(combat);
            if(combat.v14!=null)CombatSession.V14ResetEvadeWorld(combat);
            CombatSession.InitializeV12BuildEncounter(combat,record.resolvedConfig,record.id);
            if(HasV12Map(mode)&&room.templateId=="V12_R4")CombatSession.RegisterV12Beam(combat,record.resolvedConfig,mode.id+"/V12_R4/beam");
            CombatSession.ResetP4PreparationState(combat);
            CombatSession.V13EnterEncounter(combat,record.resolvedConfig,room.templateId,template.x,template.z);
            combat.momentum=0;
            record.currentCombat=combat;record.encounterStartSnapshot=combat.Clone();mode.encounters.Add(record);mode.activeEncounterId=id;mode.stage="Combat";room.encounterId=id;room.stage="Active";
            return new ProfileResult{encounterId=id};
        }
        ProfileResult Resolve(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;var record=Encounter(mode);Require(record!=null,"no_active_encounter");var current=record.currentCombat;
            Require(CombatSession.V13PartyAlive(current)&&current.enemies.All(a=>!a.Alive),"encounter_not_won");Require(current.incomingEvents.All(e=>e.damageApplied),"finish_incoming_first");
            var temporary=new CombatSession(current,record.resolvedConfig);var end=temporary.Execute(new CombatCommand{id=c.id+"/end",kind=CommandKind.EndEncounter,authority=CommandAuthority.System});Require(end.success,end.reason);
            var finished=temporary.State;var exploration=mode.explorationCombat;if(finished.v13!=null){exploration.v13=CombatJson.Clone(finished.v13);exploration.simulationSeconds=finished.simulationSeconds;}exploration.actors=CombatJson.Clone(finished.actors);exploration.activeActorId=finished.activeActorId;exploration.commandActorId=finished.commandActorId;exploration.world=CombatJson.Clone(finished.world);exploration.world.telegraphs.Clear();exploration.world.projectiles.Clear();exploration.charges=UsesP6(mode)?finished.charges:0;if(UsesP6(mode))exploration.chargeCap=finished.chargeCap;exploration.momentum=0;exploration.mode=CombatMode.RealtimeCombat;exploration.resumeMode=CombatMode.RealtimeCombat;
            foreach(var actor in exploration.actors){actor.heldBy=null;actor.reactionHeldBy=null;actor.heldSpellSlots=0;actor.heldItems=0;actor.heldSuperiority=0;actor.windupRemaining=0;actor.aimPreparing=false;actor.aimRemaining=0;actor.guardRemaining=0;actor.dodgeRemaining=0;actor.concentrationId=null;actor.statuses.RemoveAll(x=>!string.IsNullOrEmpty(x.concentrationId));}
            CombatSession.ResetP4ReactionSpecializations(exploration);
            exploration.focusBudget=CombatJson.Clone(finished.focusBudget);exploration.focusBudget.active=false;exploration.focusBudget.revealGraceRemaining=0;
            CombatSession.ResetP4PreparationState(exploration);
            CombatSession.ResetP5EncounterState(exploration);
            if(UsesP6(mode)){exploration.p6=CombatJson.Clone(finished.p6);CombatSession.ResetP6EncounterState(exploration);}
            var room=mode.Room(record.roomId);room.resolved=true;room.stage="Resolved";record.stage="Resolved";record.currentCombat=null;record.encounterStartSnapshot=null;mode.activeEncounterId=null;mode.stage="Exploring";
            string objective=room.id+"/objective";if(!mode.flags.Contains(objective)){mode.flags.Add(objective);if(mode.kind==ProfileModeKind.Challenge)mode.runEarnings+=room.templateId==(HasV12Map(mode)?"V12_R6":"R7")?2:1;}
            if(room.templateId=="R1"&&mode.kind==ProfileModeKind.Story)s.meta.challengeUnlocked=true;
            if(mode.kind==ProfileModeKind.Story)GrantSpecializationMilestone(s,room.templateId);
            if(room.templateId=="R3"&&(mode.kind==ProfileModeKind.Story||mode.HasFlag("DEMO_EXPLORATION")))AddOnce(mode.abilities,"SMASH");
            if(HasV12Map(mode)?new[]{"V12_R1","V12_R2","V12_R4"}.Contains(room.templateId):room.templateId=="R1"||room.templateId=="R3"||room.templateId=="R5")
            {if(mode.v11Departure!=null)CreateV11Reward(mode,room);else if(!UsesP6(mode))CreateReward(mode,room);}
            return new ProfileResult{encounterId=record.id,rewardId=mode.pendingReward==null?null:mode.pendingReward.id};
        }
        ProfileResult RetryStory(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;Require(mode.kind==ProfileModeKind.Story,"challenge_retry_forbidden");var record=Encounter(mode);Require(record!=null&&record.encounterStartSnapshot!=null,"no_retry_snapshot");
            var combat=record.encounterStartSnapshot.Clone();record.attemptNumber++;record.attemptId=record.id+"/attempt-"+record.attemptNumber;combat.encounterId=record.attemptId;combat.receipts.Clear();record.currentCombat=combat;record.stage="Active";mode.stage="Combat";return new ProfileResult{encounterId=record.id};
        }
        ProfileResult UseEnvironment(ProfileState s,ProfileCommand c)
        {
            var mode=s.ActiveMode;Require(c.qualified&&c.selectionId=="R5_SHIFT"&&!string.IsNullOrEmpty(c.sourceFactId)&&mode.Room(mode.currentRoomId).templateId=="R5","invalid_environment_fact");Require(!mode.HasFlag("COVER_SHIFTED"),"environment_already_used");
            var combat=Current(mode);Require(combat.mode!=CombatMode.RollFreeze&&combat.mode!=CombatMode.CombatPaused,"world_frozen");var actor=combat.Actor(c.actorId);Require(actor!=null&&actor.Alive&&!actor.isEnemy&&actor.bonusRemaining<=0&&actor.staggerRemaining<=0&&actor.windupRemaining<=0&&string.IsNullOrEmpty(actor.heldBy)&&Near(actor,26,22,6),"environment_actor_unavailable");
            actor.bonusRemaining=(Encounter(mode)==null?BaseCombatConfig:Encounter(mode).resolvedConfig).bonusRecovery;AddOnce(mode.flags,"COVER_SHIFTED");AddOnce(mode.environmentFacts,c.sourceFactId);
            if(combat.inCombat){var temp=new CombatSession(combat,Encounter(mode).resolvedConfig);var reward=temp.Execute(new CombatCommand{id=c.id+"/environment",kind=CommandKind.AwardFact,actorId=actor.id,actionId="environment",rootId=mode.id+"/R5_SHIFT",factId=c.sourceFactId,qualified=true,authority=CommandAuthority.System});Require(reward.success,reward.reason);SetCurrent(mode,temp.State);}
            return new ProfileResult();
        }
    }
}
