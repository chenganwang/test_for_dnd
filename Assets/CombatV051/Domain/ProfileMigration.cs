using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class LegacyProfilePreview
    {
        public bool canMigrate,requiresEncounterRollback,usedBackup;
        public string reason,profileId,profileInstanceId,sourcePath,sourceDigest;
        public int activeEncounterCount;
    }
    public sealed partial class ProfileSession
    {
        public static LegacyProfilePreview InspectLegacyForP4(string oldPath)
        {
            try
            {
                string source,text;bool backup;var state=ReadLegacyProfile(oldPath,out source,out text,out backup);
                int active=new[]{state.story,state.challenge}.Where(m=>m!=null).Count(m=>!string.IsNullOrEmpty(m.activeEncounterId));
                return new LegacyProfilePreview{canMigrate=true,requiresEncounterRollback=active>0,activeEncounterCount=active,usedBackup=backup,
                    profileId=state.profileId,profileInstanceId=state.profileInstanceId,sourcePath=source,sourceDigest=CombatStore.Digest(text),
                    reason=active>0?"legacy_active_encounters_restore_whole_start_snapshot":"legacy_safe_state_copied_without_resource_refill"};
            }
            catch(Exception error){return new LegacyProfilePreview{reason=error.Message,sourcePath=oldPath};}
        }
        public static ProfileSession LoadLegacyIntoP4(string oldPath,string newPath,CombatConfig baseConfig,DungeonConfig dungeonConfig,bool acceptEncounterRollback=false)
        {
            oldPath=Path.GetFullPath(oldPath);newPath=Path.GetFullPath(newPath);
            Require(!string.Equals(oldPath,newPath,StringComparison.OrdinalIgnoreCase),"migration_requires_separate_destination");
            Require(!File.Exists(newPath)&&!File.Exists(newPath+".bak")&&!File.Exists(newPath+".tmp"),"migration_destination_exists");
            string source,text;bool backup;var state=ReadLegacyProfile(oldPath,out source,out text,out backup);
            bool rollback=new[]{state.story,state.challenge}.Where(m=>m!=null).Any(m=>!string.IsNullOrEmpty(m.activeEncounterId));
            Require(!rollback||acceptEncounterRollback,"legacy_encounter_rollback_confirmation_required");
            var session=new ProfileSession{BaseCombatConfig=CombatJson.Clone(baseConfig),Config=CombatJson.Clone(dungeonConfig)};session.ValidateConfig();
            state.schemaVersion=5;state.contentVersion="actioncrpg-v0.5.2-p4";state.configHash=Hash(session.Config);state.baseCombatConfigHash=Hash(session.BaseCombatConfig);
            state.migrationSourceDigest=CombatStore.Digest(text);state.migrationPolicy=rollback?"explicit_whole_encounter_start_v051_to_v052":"safe_exact_resources_v051_to_v052";
            state.meta.lastPreparation=PartyPreparation.Default();state.meta.unlockedSpecializations=new System.Collections.Generic.List<string>{"F_GUARD","R_FLANK","M_INSIGHT"};
            foreach(string room in new[]{"R1","R3"})if(state.story.Room(room).resolved)GrantSpecializationMilestone(state,room);
            foreach(var mode in new[]{state.story,state.challenge}.Where(m=>m!=null))
            {
                var preparation=PartyPreparation.Default();preparation.primaryLawId=mode.build.primaryLawId;preparation.defaultActiveActorId=mode.explorationCombat.activeActorId;
                mode.departurePreparation=session.MakePreparationSnapshot(state,preparation);
                foreach(var record in mode.encounters)
                {
                    // The old recorded config is never silently treated as the new rules. Keep its source in the immutable legacy copy.
                    record.resolvedConfig=session.BuildConfig(mode);record.configHash=Hash(record.resolvedConfig);
                    if(record.id!=mode.activeEncounterId)continue;
                    Require(record.encounterStartSnapshot!=null,"legacy_encounter_start_snapshot_missing");
                    var restored=record.encounterStartSnapshot.Clone();
                    // A complete retry may restore previously spent resources only as part of the entire acknowledged start state.
                    // It does not grant P4's new encounter allotment or rebuild/heal the party from templates.
                    record.attemptNumber++;record.attemptId=record.id+"/attempt-"+record.attemptNumber;
                    restored.encounterId=record.attemptId;restored.receipts.Clear();
                    if(restored.charges>=restored.chargeCap)restored.momentum=0; // Discard old hidden full-cap reserve; never create a charge.
                    // Register P4 objective identities once during the explicit rules transition.
                    // This adds no award, changes no old RNG, and is never repeated by normal load.
                    CombatSession.ConfigureP4Encounter(restored,session.Config.Room(mode.Room(record.roomId).templateId).encounterKind,
                        objectiveRoots:mode.Room(record.roomId).templateId=="R5"?new[]{mode.id+"/R5_SHIFT"}:null,
                        highThreatRoots:restored.enemies.Where(e=>{
                            string enemyTemplate;return restored.world.strings.TryGetValue("enemyTemplate/"+e.id,out enemyTemplate)&&new[]{"elite","boss","mechanism"}.Contains(enemyTemplate);
                        }).Select(e=>e.id));
                    record.initialCharge=restored.charges;record.chargeCap=restored.chargeCap;
                    record.currentCombat=restored;record.encounterStartSnapshot=restored.Clone();record.stage="Active";mode.stage="Combat";
                }
                mode.planHash=session.PlanHash(mode);
            }
            state.revision++;state.bindingRevision++;session.State=state;session.Validate(state);
            // Read twice so copying a concurrently replaced legacy file never presents a mixed snapshot as a successful import.
            Require(File.ReadAllText(source,Encoding.UTF8)==text,"legacy_source_changed_during_migration");
            Directory.CreateDirectory(Path.GetDirectoryName(newPath));
            string archive=newPath+".legacy-v051.json";
            Require(!File.Exists(archive)||File.ReadAllText(archive,Encoding.UTF8)==text,"migration_archive_conflict");
            if(!File.Exists(archive))using(var stream=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {var bytes=new UTF8Encoding(false).GetBytes(text);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
            session.Store=new ProfileStore(newPath,session.Validate);session.ActiveCombat=session.CreateBinding(state);session.Store.Save(state);return session;
        }
        static ProfileState ReadLegacyProfile(string oldPath,out string chosenPath,out string raw,out bool backup)
        {
            oldPath=Path.GetFullPath(oldPath);chosenPath=null;raw=null;backup=false;
            JObject mainHeader=null;
            if(File.Exists(oldPath))
            {
                try{mainHeader=JObject.Parse(File.ReadAllText(oldPath,Encoding.UTF8));}catch(JsonException){}
                if(mainHeader!=null)Require((int?)mainHeader["schemaVersion"]==4&&(string)mainHeader["contentVersion"]=="actioncrpg-v0.5.1-p2","unsupported_legacy_profile_version");
            }
            string errors="";
            foreach(string candidate in new[]{oldPath,oldPath+".bak"})
            {
                try
                {
                    string text=File.ReadAllText(candidate,Encoding.UTF8);var envelope=JObject.Parse(text);
                    Require((int?)envelope["schemaVersion"]==4&&(string)envelope["contentVersion"]=="actioncrpg-v0.5.1-p2","unsupported_legacy_profile_version");
                    string payload=(string)envelope["payload"];
                    Require((string)envelope["integrityAlgorithm"]=="sha256-utf8-payload-v1"&&payload!=null&&(string)envelope["integrity"]==CombatStore.Digest(payload),"legacy_integrity_failed");
                    ValidateLegacyHashes(JObject.Parse(payload));
                    var state=CombatJson.Read<ProfileState>(payload);
                    Require(state.schemaVersion==4&&state.contentVersion=="actioncrpg-v0.5.1-p2"&&state.profileId==(string)envelope["profileId"]&&state.profileInstanceId==(string)envelope["profileInstanceId"]&&state.revision==(long?)envelope["revision"],"legacy_envelope_identity_mismatch");
                    if(candidate!=oldPath&&mainHeader!=null)
                    {
                        string mainId=(string)mainHeader["profileId"],mainInstance=(string)mainHeader["profileInstanceId"];
                        Require((string.IsNullOrEmpty(mainId)||mainId==state.profileId)&&(string.IsNullOrEmpty(mainInstance)||mainInstance==state.profileInstanceId),"legacy_backup_identity_mismatch");
                    }
                    Require(state.story!=null&&state.meta!=null&&state.ActiveMode!=null,"legacy_profile_incomplete");
                    chosenPath=candidate;raw=text;backup=candidate!=oldPath;return state;
                }
                catch(Exception error){errors+=Path.GetFileName(candidate)+":"+error.Message+"; ";}
            }
            throw new IOException("No valid legacy profile; original files retained. "+errors);
        }
        static void ValidateLegacyHashes(JObject payload)
        {
            // Validate against the original JSON shape. Re-serializing through P4 DTOs would
            // inject new defaults and invalidate the historical config/plan hashes.
            foreach(string key in new[]{"story","challenge"})
            {
                var mode=payload[key] as JObject;if(mode==null)continue;
                var rooms=mode["rooms"] as JArray;Require(rooms!=null,"legacy_rooms_missing");
                var projected=new JArray(rooms.Select(r=>new JObject{
                    ["id"]=r["id"].DeepClone(),["templateId"]=r["templateId"].DeepClone(),["enemySpawns"]=r["enemySpawns"].DeepClone()}));
                var plan=new JObject{["version"]="dungeon-v051-p2-1",["rooms"]=projected,
                    ["abilities"]=mode["departureAbilitySnapshot"].DeepClone(),["departureTraining"]=mode["departureTraining"].DeepClone()};
                Require((string)mode["planHash"]==CombatStore.Digest(plan.ToString(Formatting.None)),"legacy_dungeon_plan_mismatch");
                var records=mode["encounters"] as JArray;Require(records!=null,"legacy_encounters_missing");
                foreach(var record in records)
                    Require(record["resolvedConfig"] is JObject&&(string)record["configHash"]==CombatStore.Digest(record["resolvedConfig"].ToString(Formatting.None)),"legacy_encounter_config_mismatch");
            }
        }
    }
}
