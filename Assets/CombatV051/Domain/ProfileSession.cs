using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        public ProfileState State {get;private set;}
        public DungeonConfig Config {get;private set;}
        public CombatConfig BaseCombatConfig {get;private set;}
        public ProfileStore Store {get;private set;}
        public CombatSession ActiveCombat {get;private set;}
        public ModeState ActiveMode {get{return State.ActiveMode;}}
        public RoomState ActiveRoom {get{return ActiveMode.Room(ActiveMode.currentRoomId);}}
        public RoomTemplate ActiveRoomTemplate {get{return ConfigFor(ActiveMode).Room(ActiveRoom.templateId);}}
        public EncounterRecord ActiveEncounter {get{return Encounter(ActiveMode);}}
        public SafeEventState ActiveSafeEvent {get{return ActiveMode.events.Find(e=>e.id==ActiveMode.activeEventId);}}
        public RewardOffer PendingReward {get{return ActiveMode.pendingReward!=null&&ActiveMode.pendingReward.stage=="Offered"?ActiveMode.pendingReward:null;}}
        public bool IsInEncounter {get{return ActiveEncounter!=null;}}
        public bool IsSafe {get{return !IsInEncounter&&ActiveMode.stage=="Exploring";}}
        public string LastTickError {get{return ActiveCombat.LastTickError;}}
        public ProfileSession(ProfileState state,CombatConfig baseCombatConfig,DungeonConfig dungeonConfig,string path=null)
        {
            State=state??throw new ArgumentNullException(nameof(state));BaseCombatConfig=CombatJson.Clone(baseCombatConfig??throw new ArgumentNullException(nameof(baseCombatConfig)));Config=CombatJson.Clone(dungeonConfig??throw new ArgumentNullException(nameof(dungeonConfig)));
            ValidateConfig();Validate(State);if(path!=null)Store=new ProfileStore(path,Validate);ActiveCombat=CreateBinding(State);
        }
        public static ProfileSession CreateNew(string profileId,ulong seed,CombatConfig baseCombatConfig,DungeonConfig dungeonConfig,string path=null)
        {return CreateNewCore(profileId,seed,baseCombatConfig,dungeonConfig,path,baseCombatConfig?.p5,baseCombatConfig?.p6);}
        static ProfileSession CreateNewCore(string profileId,ulong seed,CombatConfig baseCombatConfig,DungeonConfig dungeonConfig,string path,P5Config departureRules,P6Config p6DepartureRules=null)
        {
            Require(!string.IsNullOrWhiteSpace(profileId),"profile_id_required");
            var state=new ProfileState{profileId=profileId,profileInstanceId=Guid.NewGuid().ToString("N"),seed=seed,focusScale=p6DepartureRules==null?baseCombatConfig.focusScale:p6DepartureRules.focus.worldScale,configHash=Hash(dungeonConfig),baseCombatConfigHash=Hash(baseCombatConfig),modeSequence=1};
            var session=new ProfileSession();session.State=state;session.BaseCombatConfig=CombatJson.Clone(baseCombatConfig);session.Config=CombatJson.Clone(dungeonConfig);session.nextDepartureP5Rules=CombatJson.Clone(departureRules);session.nextDepartureP6Rules=CombatJson.Clone(p6DepartureRules);session.ValidateConfig();
            state.story=session.CreateMode(state,ProfileModeKind.Story,profileId+"/story-1",seed,"BASE");session.Validate(state);if(path!=null)session.Store=new ProfileStore(path,session.Validate);session.ActiveCombat=session.CreateBinding(state);if(session.Store!=null)session.Store.Save(state);return session;
        }
        ProfileSession(){}
        public static ProfileSession Load(string path,CombatConfig baseCombatConfig,DungeonConfig dungeonConfig)
        {
            var session=new ProfileSession{BaseCombatConfig=CombatJson.Clone(baseCombatConfig),Config=CombatJson.Clone(dungeonConfig)};session.ValidateConfig();session.Store=new ProfileStore(path,session.Validate);session.State=session.Store.Load();session.ActiveCombat=session.CreateBinding(session.State);return session;
        }
        static string Hash<T>(T value){return CombatStore.Digest(CombatJson.Write(value));}
        static void Require(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        static EncounterRecord Encounter(ModeState mode){return mode.encounters.Find(e=>e.id==mode.activeEncounterId);}
        static CombatState Current(ModeState mode){var e=Encounter(mode);return e==null?mode.explorationCombat:e.currentCombat;}
        static void SetCurrent(ModeState mode,CombatState combat){var e=Encounter(mode);if(e==null)mode.explorationCombat=combat;else e.currentCombat=combat;}
        static void AddOnce(List<string> list,string id){if(!list.Contains(id))list.Add(id);}
        void CheckConfig(){Require(State.configHash==Hash(Config)&&State.baseCombatConfigHash==Hash(BaseCombatConfig),"profile_config_changed");}
        public ProfileResult Execute(ProfileCommand command)
        {
            if(command==null||string.IsNullOrWhiteSpace(command.id))return Failure("command_id_required");string fingerprint=Hash(command);
            var old=State.receipts.Find(r=>r.id==command.id);if(old!=null){if(old.fingerprint!=fingerprint)return Failure("command_payload_mismatch");var replay=CombatJson.Clone(old.result);replay.duplicate=true;return replay;}
            try
            {
                CheckConfig();Require(command.expectedRevision<0||command.expectedRevision==State.revision,"stale_profile_revision");Require(command.expectedBindingRevision<0||command.expectedBindingRevision==State.bindingRevision,"stale_binding");
                Require(string.IsNullOrEmpty(command.modeInstanceId)||command.modeInstanceId==ActiveMode.id,"mode_instance_mismatch");Require(string.IsNullOrEmpty(command.roomInstanceId)||command.roomInstanceId==ActiveRoom.id,"room_instance_mismatch");
                Require(string.IsNullOrEmpty(command.encounterId)||ActiveEncounter!=null&&command.encounterId==ActiveEncounter.id,"encounter_instance_mismatch");
                var candidate=CaptureProfile();var result=ApplyProfile(candidate,command);candidate.revision++;candidate.bindingRevision++;result.success=true;result.reason=result.reason??"ok";result.revision=candidate.revision;
                candidate.receipts.Add(new ProfileReceipt{id=command.id,fingerprint=fingerprint,result=CombatJson.Clone(result)});Validate(candidate);var binding=CreateBinding(candidate);if(Store!=null)Store.Save(candidate);State=candidate;ActiveCombat=binding;return result;
            }
            catch(Exception ex){return Failure(ex.Message);}
        }
        ProfileResult Failure(string reason){return new ProfileResult{success=false,reason=reason,revision=State.revision};}
        public ProfileResult SaveCurrent(string commandId)
        {
            // A save is an idempotent outer command, and always captures the latest inner reference.
            if(string.IsNullOrWhiteSpace(commandId))return Failure("command_id_required");
            // Constructor validation is read-only when neither a path nor a sink is supplied.
            // Do not call ActiveCombat.Save here: it would publish a second transaction.
            try{ValidateCombatSnapshot(ActiveCombat.State,ActiveCombat.Config);}
            catch(Exception ex){return Failure("save_failed:"+ex.Message);}
            var command=new ProfileCommand{id=commandId,kind=ProfileCommandKind.SaveCurrent};
            return Execute(command);
        }
        public CombatResult ExecuteCombat(CombatCommand command,long expectedBindingRevision=-1)
        {
            if(expectedBindingRevision>=0&&expectedBindingRevision!=State.bindingRevision)return new CombatResult{reason="stale_binding"};
            if(ActiveSafeEvent!=null)return new CombatResult{reason="safe_event_active"};
            return ActiveCombat.Execute(command);
        }
        public float Tick(float realDelta,bool decisionUiAvailable=true,float? decisionDelta=null)
        {
            Require(!float.IsNaN(realDelta)&&!float.IsInfinity(realDelta)&&realDelta>=0,"invalid_delta");
            Require(!decisionDelta.HasValue||!float.IsNaN(decisionDelta.Value)&&!float.IsInfinity(decisionDelta.Value)&&decisionDelta.Value>=0,"invalid_delta");
            float before=ActiveMode.elapsedRealSeconds,pauseBefore=ActiveMode.explicitPauseSeconds;
            RecordActivity(realDelta,explicitPaused:ActiveCombat.State.mode==CombatMode.CombatPaused&&ActiveSafeEvent==null);
            float dt=ActiveCombat.Tick(realDelta,decisionUiAvailable,decisionDelta);
            if(ActiveCombat.LastTickError!=null){ActiveMode.elapsedRealSeconds=before;ActiveMode.explicitPauseSeconds=pauseBefore;}
            return dt;
        }
        ProfileState CaptureProfile(CombatState explicitCombatCandidate=null)
        {
            using(DomainTiming.Measure("Profile.Capture"))
                return CombatDeepClone.CopyReplacingCurrent(State,explicitCombatCandidate??ActiveCombat.State);
        }
        CombatSession CreateBinding(ProfileState profile)
        {
            var mode=profile.ActiveMode;var record=Encounter(mode);var config=record==null?BuildConfig(mode):CombatJson.Clone(record.resolvedConfig);config.focusScale=profile.focusScale;
            long binding=profile.bindingRevision;string modeId=mode.id,context=record==null?"exploration":record.id;
            var combat=Current(mode).Clone();combat.focusBudget.recoveryMultiplier=mode.build.relicSlots.Contains("FOCUS")?FocusBudgetRules.FocusRelicMultiplier:1;
            return new CombatSession(combat,config,persistenceSink:combat=>PersistCombat(combat,binding,modeId,context));
        }
        void PersistCombat(CombatState combat,long binding,string modeId,string context)
        {
            Require(State.bindingRevision==binding&&ActiveMode.id==modeId&&(ActiveEncounter==null?"exploration":ActiveEncounter.id)==context,"stale_binding");CheckConfig();
            var expected=ActiveEncounter==null?BuildConfig(ActiveMode):CombatJson.Clone(ActiveEncounter.resolvedConfig);expected.focusScale=State.focusScale;Require(Hash(expected)==Hash(ActiveCombat.Config),"combat_config_changed");
            var candidate=CaptureProfile(combat);candidate.revision++;var record=Encounter(candidate.ActiveMode);
            if(record!=null&&!CombatSession.V13PartyAlive(combat)){record.stage="Defeated";candidate.ActiveMode.stage="Defeated";}
            if(Store!=null)Store.Save(candidate);else Validate(candidate);State=candidate;
        }
        ModeState CreateMode(ProfileState profile,ProfileModeKind kind,string id,ulong seed,string law)
        {
            var mode=new ModeState{id=id,kind=kind,seed=seed,random=RandomStreams.Create(seed),build=new BuildState{primaryLawId=law}};
            SnapshotP5Departure(mode);SnapshotP6Departure(mode);
            SnapshotV11Departure(profile,mode);
            SnapshotV12Dungeon(mode);
            var prepared=(UsesP6(mode)?profile.meta.p6Preparation??P6CharacterCatalog.DefaultPreparation():profile.meta.lastPreparation).Clone();prepared.primaryLawId=law;mode.departurePreparation=UsesP6(mode)?MakeP6PreparationSnapshot(profile,prepared):MakePreparationSnapshot(profile,prepared);
            if(mode.v11Departure!=null){mode.departurePreparation=MakeV11PreparationSnapshot(mode,law);prepared=mode.departurePreparation.preparation;}
            if(kind==ProfileModeKind.Challenge){mode.departureAbilitySnapshot=new List<string>(profile.story.abilities);mode.departureUnlockPoolSnapshot=new List<string>(profile.meta.unlockedLaws);mode.departureTraining=profile.meta.trainingLevel;}
            foreach(var template in ConfigFor(mode).rooms)
            {
                var room=new RoomState{id=id+"/"+template.id,templateId=template.id,enemySpawns=CombatJson.Clone(template.enemies)};
                foreach(string eventId in template.eventIds){var e=ConfigFor(mode).Event(eventId);var instance=new SafeEventState{id=room.id+"/"+eventId,templateId=eventId,roomId=room.id,ability=e.ability,dc=e.dc,worldEffect=e.worldEffect};mode.events.Add(instance);room.eventIds.Add(instance.id);}
                mode.rooms.Add(room);
            }
            var entry=mode.Room(HasV12Map(mode)?"V12_R1":"R0");entry.visited=true;entry.stage="Safe";mode.currentRoomId=entry.id;
            var modeConfig=BuildConfig(mode);var combat=CombatState.CreatePartyForPreset(profile.profileId,id,seed,modeConfig);combat.encounterId=id+"/exploration";combat.inCombat=false;combat.encounterResolved=false;combat.charges=0;combat.momentum=0;combat.enemies.Clear();
            combat.activeActorId=prepared.defaultActiveActorId;combat.commandActorId=prepared.defaultActiveActorId;
            CombatSession.ResetP6EncounterState(combat);
            foreach(var actor in combat.actors){actor.maxHp+=2*mode.departureTraining;actor.hp+=2*mode.departureTraining;}
            if(modeConfig.v13!=null&&(modeConfig.v14==null||modeConfig.v14.partyCombatMode==PartyCombatMode.CoreAndAssist)){var core=combat.Actor(modeConfig.v11.coreActorId);core.maxHp=(int)Math.Ceiling(core.maxHp*(decimal)modeConfig.v13.coreHealthScale);core.hp=core.maxHp;}
            combat.world.strings["roomTemplateId"]=entry.templateId;mode.explorationCombat=combat;mode.planHash=PlanHash(mode);return mode;
        }
        string PlanHash(ModeState mode){if(HasV12Map(mode))return Hash(new {version=mode.v12Run.version,contentHash=mode.v12Run.hash,rooms=mode.rooms.Select(r=>new {r.id,r.templateId,r.enemySpawns}),abilities=mode.departureAbilitySnapshot,mode.departureTraining,preparationHash=mode.departurePreparation?.hash});return Hash(new {version=Config.contentVersion,rooms=mode.rooms.Select(r=>new {r.id,r.templateId,r.enemySpawns}),abilities=mode.departureAbilitySnapshot,mode.departureTraining,preparationHash=mode.departurePreparation?.hash});}
        CombatConfig BuildConfig(ModeState mode)
        {
            var result=CombatJson.Clone(BaseCombatConfig);if(mode.departureP5Rules!=null)result.p5=CombatJson.Clone(mode.departureP5Rules);if(UsesP6(mode))result.p6=CombatJson.Clone(mode.p6Departure.rules);result.actorOverrides=CombatJson.Clone(mode.departurePreparation.actorOverrides);foreach(string id in new[]{mode.build.primaryLawId}.Concat(mode.build.relicSlots.Where(x=>x!=null)))
            {var rule=Config.Rule(id);Require(rule!=null,"unknown_build_rule");result.aimSeconds*=rule.aimMultiplier;result.reactionRecovery*=rule.reactionMultiplier;result.preparationRepeatSeconds*=rule.preparationRepeatMultiplier;}
            result.v11=V11RulesFor(mode);
            result.v12=CombatJson.Clone(mode.v11Departure?.v12);result.v13=CombatJson.Clone(mode.v11Departure?.v13);result.v14=CombatJson.Clone(mode.v11Departure?.v14);
            return result;
        }
        int BuildCap(ModeState mode){return Math.Max(2,Math.Min(3,2+new[]{mode.build.primaryLawId}.Concat(mode.build.relicSlots.Where(x=>x!=null)).Sum(id=>Config.Rule(id).chargeCapDelta)));}
        int BuildStartCharge(ModeState mode){return UsesP6(mode)?Math.Min(BuildCap(mode),Math.Max(mode.p6Departure.rules.newEncounterFloor,mode.explorationCombat.charges)):2;}
        static void EnsureSafe(ModeState mode,bool allowEvent=false)
        {Require(Encounter(mode)==null&&mode.stage=="Exploring","safe_state_required");if(!allowEvent)Require(string.IsNullOrEmpty(mode.activeEventId),"finish_safe_event_first");}
        static void EnsureRewardFinished(ModeState mode){Require(mode.pendingReward==null||mode.pendingReward.stage!="Offered","finish_reward_first");}
    }
}
