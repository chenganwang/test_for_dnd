using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        static void ValidateCombatSnapshot(CombatState state,CombatConfig config)
        {new CombatSession(state,config);}
        void ValidateConfig()
        {
            Require(Config!=null&&Config.contentVersion=="dungeon-v051-p2-1"&&Config.rooms!=null&&Config.passages!=null&&Config.events!=null&&Config.enemies!=null&&Config.rules!=null,"invalid_dungeon_config");
            Require(Config.rooms.Count==8&&Enumerable.Range(0,8).All(i=>Config.Room("R"+i)!=null)&&Config.rooms.Select(r=>r.id).Distinct().Count()==8,"invalid_room_templates");
            Require(Config.rooms.Count(r=>r.encounterKind=="Normal")==3&&Config.rooms.Count(r=>r.encounterKind=="Elite")==1&&Config.rooms.Count(r=>r.encounterKind=="Boss")==1,"invalid_encounter_budget");
            Require(Config.rules.Select(r=>r.id).Distinct().Count()==Config.rules.Count&&Config.rules.Count(r=>r.kind=="Relic")>=3,"invalid_build_rules");
            foreach(var rule in Config.rules)Require((rule.kind=="Law"||rule.kind=="Relic")&&rule.chargeCapDelta>=0&&rule.chargeCapDelta<=1&&rule.startChargeDelta==0&&Finite(rule.aimMultiplier)&&rule.aimMultiplier>0&&Finite(rule.reactionMultiplier)&&rule.reactionMultiplier>0&&Finite(rule.preparationRepeatMultiplier)&&rule.preparationRepeatMultiplier>0,"invalid_build_modifier");
            foreach(var room in Config.rooms)
            {
                Require(Finite(room.x)&&Finite(room.z)&&room.width>=12&&room.depth>=12&&room.eventIds!=null&&room.enemies!=null,"invalid_room_geometry");
                Require(room.enemies.Select(e=>e.id).Distinct().Count()==room.enemies.Count,"duplicate_spawn_id");
                foreach(var spawn in room.enemies)Require(!string.IsNullOrEmpty(spawn.id)&&Config.Enemy(spawn.templateId)!=null&&Finite(spawn.x)&&Finite(spawn.z)&&Math.Abs(spawn.x-room.x)<room.width/2-1&&Math.Abs(spawn.z-room.z)<room.depth/2-1,"invalid_enemy_spawn");
                foreach(string id in room.eventIds)Require(Config.Event(id)!=null&&Config.Event(id).roomId==room.id,"invalid_event_room");
            }
            Require(Config.passages.Select(p=>p.id).Distinct().Count()==Config.passages.Count,"duplicate_passage");foreach(var p in Config.passages)Require(Config.Room(p.roomA)!=null&&Config.Room(p.roomB)!=null&&p.roomA!=p.roomB&&Finite(p.x)&&Finite(p.z)&&p.width>=3,"invalid_passage");
            Require(Config.enemies.Select(e=>e.id).Distinct().Count()==Config.enemies.Count,"duplicate_enemy_template");
            foreach(var enemy in Config.enemies){Require(enemy.hp>0&&enemy.hp<=10000&&enemy.armorClass>0&&enemy.breakMaximum>0&&enemy.speed>=0&&enemy.patterns!=null&&enemy.patterns.Count>0,"invalid_enemy_template");Require(enemy.patterns.Select(p=>p.id).Distinct().Count()==enemy.patterns.Count,"duplicate_enemy_pattern");foreach(var p in enemy.patterns)Require(!string.IsNullOrEmpty(p.id)&&(p.shape=="Cone"||p.shape=="Circle")&&p.windup>.1f&&p.recovery>.1f&&p.damage>=0&&p.damage<=10000&&p.phaseThreshold>0&&p.phaseThreshold<=1&&p.size>0&&p.radius>0&&p.angle>0&&p.angle<=360,"invalid_enemy_pattern");}
            Require(Config.events.Count==2&&Config.events.Select(e=>e.id).Distinct().Count()==2&&Config.restHealFraction>0&&Config.restHealFraction<=1,"invalid_event_or_rest_config");
            foreach(var e in Config.events)Require(Config.Room(e.roomId)!=null&&e.dc>0&&e.preparation>=0&&e.preparation<=2&&e.range>0&&new[]{"Strength","Dexterity","Intelligence","Wisdom","Charisma","Constitution"}.Contains(e.ability)&&new[]{"ROUTE_OPEN","BOSS_MECHANISM_DISABLED"}.Contains(e.worldEffect),"invalid_event_template");
            var probe=CombatState.CreatePartyForPreset("validate","validate",1,BaseCombatConfig);new CombatSession(probe,CombatJson.Clone(BaseCombatConfig));
        }
        public void Validate(ProfileState s)
        {
            using(DomainTiming.Measure("Profile.Validate"))
            {
            Require(s!=null&&s.schemaVersion==5&&s.contentVersion=="actioncrpg-v0.5.2-p4","unsupported_profile_version");
            Require(!string.IsNullOrWhiteSpace(s.profileId)&&!string.IsNullOrEmpty(s.profileInstanceId)&&s.revision>=0&&s.bindingRevision>=0&&s.modeSequence>=1,"invalid_profile_identity");
            Require(s.configHash==Hash(Config)&&s.baseCombatConfigHash==Hash(BaseCombatConfig),"profile_config_mismatch");Require(s.focusScale==0||s.focusScale>=.10f&&s.focusScale<=.15f,"invalid_focus_scale");
            Require(s.meta!=null&&s.meta.research>=0&&s.meta.trainingLevel>=0&&s.meta.trainingLevel<=1&&s.meta.settledRunIds!=null&&s.meta.unlockedLaws!=null&&s.receipts!=null&&s.story!=null,"missing_profile_domain");Require(Enum.IsDefined(typeof(ProfileModeKind),s.activeMode)&&s.ActiveMode!=null,"active_mode_missing");
            Require(s.meta.unlockedSpecializations!=null&&s.meta.unlockedSpecializations.Distinct().Count()==s.meta.unlockedSpecializations.Count&&s.meta.unlockedSpecializations.All(id=>ProfilePreparationCatalog.Find(id)!=null),"invalid_specialization_unlocks");
            ValidatePreparation(s,s.meta.lastPreparation,true);
            if(s.meta.p6Preparation!=null)ValidateP6Preparation(s,s.meta.p6Preparation,true);
            ValidateV11Progress(s.meta);
            Require(s.story.kind==ProfileModeKind.Story&&(s.challenge==null||s.challenge.kind==ProfileModeKind.Challenge&&s.challenge.id!=s.story.id),"mode_ownership_invalid");
            foreach(var mode in new[]{s.story,s.challenge}.Where(m=>m!=null))ValidateMode(s,mode);
            Require(s.meta.settledRunIds.Distinct().Count()==s.meta.settledRunIds.Count,"duplicate_settlement");Require(s.receipts.All(r=>r!=null&&!string.IsNullOrEmpty(r.id)&&r.result!=null)&&s.receipts.Select(r=>r.id).Distinct().Count()==s.receipts.Count,"invalid_profile_receipts");
            }
        }
        void ValidateMode(ProfileState profile,ModeState mode)
        {
            Require(!string.IsNullOrEmpty(mode.id)&&mode.rooms!=null&&mode.encounters!=null&&mode.events!=null&&mode.flags!=null&&mode.abilities!=null&&mode.departureAbilitySnapshot!=null&&mode.departureUnlockPoolSnapshot!=null&&mode.openedGateIds!=null&&mode.usedRestIds!=null&&mode.claimedRewardIds!=null&&mode.environmentFacts!=null,"missing_mode_domain");
            ValidateV12Dungeon(mode);var map=ConfigFor(mode);int roomCount=HasV12Map(mode)?6:8;
            Require(mode.planHash==PlanHash(mode)&&mode.rooms.Count==roomCount&&mode.rooms.Select(r=>r.id).Distinct().Count()==roomCount&&mode.Room(mode.currentRoomId)!=null,"dungeon_plan_mismatch");
            Require(mode.departurePreparation!=null&&mode.departurePreparation.rulesVersion==(mode.v11Departure!=null?"v11-preparation-1":UsesP6(mode)?"v070-preparation-1":"v052-preparation-1")&&mode.departurePreparation.hash==PreparationHash(mode.departurePreparation),"departure_preparation_changed");
            ValidateP5Departure(mode);ValidateP6Departure(mode);
            ValidateV11Departure(mode);
            if(mode.v11Departure==null){if(UsesP6(mode))ValidateP6Preparation(profile,mode.departurePreparation.preparation,false);else ValidatePreparation(profile,mode.departurePreparation.preparation,false);}
            Require(mode.departurePreparation.actorOverrides!=null&&mode.departurePreparation.actorOverrides.Count==3&&mode.departurePreparation.actorOverrides.All(a=>a!=null&&mode.departurePreparation.preparation.actors.Any(p=>p.actorId==a.actorId&&p.specializationId==a.specializationId))&&mode.departurePreparation.actorOverrides.Select(a=>a.actorId).Distinct().Count()==3,"invalid_departure_actor_rules");
            Require(mode.departureTraining>=0&&mode.departureTraining<=profile.meta.trainingLevel&&(mode.kind==ProfileModeKind.Challenge||mode.departureTraining==0),"invalid_departure_training");
            Require(new[]{"Exploring","Combat","Defeated","Settled"}.Contains(mode.stage)&&Finite(mode.elapsedRealSeconds)&&mode.elapsedRealSeconds>=0&&mode.runEarnings>=0,"invalid_mode_stage");
            Require(Finite(mode.explicitPauseSeconds)&&mode.explicitPauseSeconds>=0&&Finite(mode.focusLostSeconds)&&mode.focusLostSeconds>=0,"invalid_activity_clocks");
            Require(mode.build!=null&&Config.Rule(mode.build.primaryLawId)!=null&&Config.Rule(mode.build.primaryLawId).kind=="Law"&&mode.build.relicSlots!=null&&mode.build.relicSlots.Count==2,"invalid_build_slots");
            var relics=mode.build.relicSlots.Where(r=>r!=null).ToArray();Require(relics.Distinct().Count()==relics.Length&&relics.All(r=>Config.Rule(r)!=null&&Config.Rule(r).kind=="Relic"),"invalid_equipped_relic");
            Require(mode.explorationCombat!=null,"missing_exploration_combat");ValidateCombatSnapshot(mode.explorationCombat,BuildConfig(mode));var exploration=mode.explorationCombat;
            Require(exploration.profileId==profile.profileId&&exploration.modeInstanceId==mode.id&&!exploration.inCombat&&!exploration.encounterResolved&&exploration.enemies.Count==0&&exploration.pendingRoll==null&&exploration.rolls.Count==0&&exploration.queue.Count==0&&exploration.reactionWindows.Count==0&&exploration.incomingEvents.Count==0&&(UsesP6(mode)?exploration.charges>=0&&exploration.charges<=exploration.chargeCap:exploration.charges==0)&&exploration.momentum==0,"unsafe_exploration_state");
            foreach(var room in mode.rooms)Require(room.id==mode.id+"/"+room.templateId&&map.Room(room.templateId)!=null&&new[]{"Unvisited","Safe","Active","Resolved"}.Contains(room.stage)&&room.resolved==(room.stage=="Resolved"),"invalid_room_instance");
            Require(mode.encounters.Select(e=>e.id).Distinct().Count()==mode.encounters.Count,"duplicate_encounter");
            foreach(var record in mode.encounters)
            {
                Require(record.id==record.roomId+"/encounter"&&mode.Room(record.roomId)!=null&&record.resolvedConfig!=null&&record.configHash==Hash(record.resolvedConfig)&&record.attemptNumber>=0&&record.attemptId==record.id+"/attempt-"+record.attemptNumber,"invalid_encounter_record");
                if(record.stage=="Active"||record.stage=="Defeated")
                {
                    Require(record.id==mode.activeEncounterId&&record.currentCombat!=null&&record.encounterStartSnapshot!=null,"active_encounter_missing");
                    ValidateCombatSnapshot(record.currentCombat,record.resolvedConfig);ValidateCombatSnapshot(record.encounterStartSnapshot,record.resolvedConfig);
                    Require(record.currentCombat.profileId==profile.profileId&&record.currentCombat.modeInstanceId==mode.id&&record.currentCombat.encounterId==record.attemptId&&record.currentCombat.inCombat&&record.encounterStartSnapshot.random.masterSeed==record.seed,"combat_encounter_identity_mismatch");
                }
                else Require((record.stage=="Resolved"||record.stage=="Abandoned")&&record.currentCombat==null&&record.encounterStartSnapshot==null,"terminal_encounter_invalid");
            }
            var active=Encounter(mode);Require((mode.stage=="Combat"||mode.stage=="Defeated")?active!=null:active==null,"mode_encounter_stage_mismatch");
            Require(mode.events.Count==map.events.Count&&mode.events.Select(e=>e.id).Distinct().Count()==map.events.Count,"invalid_event_instances");
            foreach(var e in mode.events)
            {
                Require(map.Event(e.templateId)!=null&&e.id==e.roomId+"/"+e.templateId&&mode.Room(e.roomId)!=null&&e.worldEffect==map.Event(e.templateId).worldEffect&&new[]{"Unopened","Quoted","AwaitDecision","Committed"}.Contains(e.stage),"invalid_event_instance");
                Require(e.rollCount>=0&&e.rollCount<=2&&e.rerollUsed==(e.rollCount==2)&&e.rawFaces!=null,"invalid_event_roll_count");
                if(e.rollCount==0)Require(e.rawFaces.Count==0,"event_without_roll_has_faces");
                else Require(e.rawFaces.Count==(e.advantage!=e.disadvantage?2:1)&&e.rawFaces.All(f=>f>=1&&f<=20)&&e.rawFaces.Contains(e.selectedFace)&&e.total==e.selectedFace+e.bonus&&e.success==(e.total>=e.dc)&&exploration.actors.Any(a=>a.id==e.actorId),"invalid_event_result");
                if(e.stage=="Committed"&&!e.usedAlternative&&e.success)Require(mode.flags.Contains(e.worldEffect),"event_effect_missing");
            }
            if(!string.IsNullOrEmpty(mode.activeEventId))Require(active==null&&mode.events.Any(e=>e.id==mode.activeEventId&&e.stage!="Committed")&&exploration.mode==CombatMode.CombatPaused,"invalid_safe_event_binding");
            if(mode.pendingReward!=null){var offer=mode.pendingReward;Require(mode.Room(offer.roomId)!=null&&offer.id==offer.roomId+"/reward"&&offer.candidates!=null&&offer.candidates.Distinct().Count()==offer.candidates.Count&&offer.candidates.All(id=>mode.v11Departure!=null?V11BuffCatalog.Find(id)!=null:Config.Rule(id)!=null&&Config.Rule(id).kind=="Relic")&&new[]{"Offered","Claimed","Skipped"}.Contains(offer.stage),"invalid_reward_offer");if(offer.stage!="Offered")Require(mode.claimedRewardIds.Contains(offer.id),"missing_reward_receipt");}
            if(mode.v11Departure!=null&&mode.pendingReward?.stage=="Offered")
            {
                var offer=mode.pendingReward;Require(!mode.claimedRewardIds.Contains(offer.id),"v11_reward_already_consumed");
                var eligible=V11BuffCatalog.Eligible(V11RulesFor(mode));
                Require(offer.candidates.All(id=>eligible.Any(definition=>definition.id==id)),"v11_restored_reward_ineligible");
                if(UsesV12Build(mode))Require(offer.candidates.Count<=3&&offer.candidates.All(id=>V12RewardAllowedAtRoom(mode,offer.roomId,id)),"v12_restored_reward_wrong_room");
            }
            Require(mode.claimedRewardIds.Distinct().Count()==mode.claimedRewardIds.Count&&mode.usedRestIds.Distinct().Count()==mode.usedRestIds.Count&&mode.openedGateIds.Distinct().Count()==mode.openedGateIds.Count,"duplicate_mode_receipt");
            Require(mode.random!=null&&mode.random.streamVersion=="v051-streams-1","invalid_mode_rng");foreach(var rng in new[]{mode.random.layout,mode.random.contentReward,mode.random.visibleTactical,mode.random.backgroundCombat,mode.random.classResource,mode.random.eventDice,mode.random.cosmetic})Require(rng!=null&&rng.algorithmVersion=="pcg32-v1"&&(rng.increment&1UL)==1,"invalid_mode_random_stream");
        }
    }
}
