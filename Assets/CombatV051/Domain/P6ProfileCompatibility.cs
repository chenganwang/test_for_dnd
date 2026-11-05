using System;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class P6DepartureSnapshot
    {
        public string presetId=P6CharacterCatalog.PresetId,recoveryPolicy="ExactState",rulesVersion,hash;
        public P6Config rules;
    }
    public sealed partial class ModeState
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P6DepartureSnapshot p6Departure;
    }
    public sealed partial class ProfileMeta
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public PartyPreparation p6Preparation;
    }
    public sealed partial class ProfileSession
    {
        P6Config nextDepartureP6Rules;
        public bool ActiveUsesP6 {get{return ActiveCombat!=null&&ActiveCombat.Config.p6!=null;}}
        static bool UsesP6(ModeState mode){return mode.p6Departure!=null;}

        public static ProfileSession CreateNewCompatible(string profileId,ulong seed,CombatConfig p6Config,CombatConfig p5Config,CombatConfig legacyConfig,DungeonConfig dungeonConfig,string path=null)
        {
            ValidateP6Defaults(p6Config,p5Config,legacyConfig);
            return CreateNewCore(profileId,seed,legacyConfig,dungeonConfig,path,p5Config.p5,p6Config.p6);
        }
        public static ProfileSession LoadCompatible(string path,CombatConfig p6Config,CombatConfig p5Config,CombatConfig legacyConfig,DungeonConfig dungeonConfig)
        {
            ValidateP6Defaults(p6Config,p5Config,legacyConfig);
            var legacy=CombatJson.Clone(legacyConfig);var previous=CombatJson.Clone(p5Config);var latest=CombatJson.Clone(p6Config);
            string legacyHash=Hash(legacy),previousHash=Hash(previous),latestHash=Hash(latest);
            var session=new ProfileSession{Config=CombatJson.Clone(dungeonConfig),BaseCombatConfig=legacy};
            session.ValidateConfig();bool selecting=true;CombatConfig selected=null;
            // One envelope-authoritative main/backup pass. Never migrate by trying
            // a second loader, which could incorrectly prefer an older backup.
            session.Store=new ProfileStore(path,candidate=>
            {
                if(!selecting){session.Validate(candidate);return;}
                CombatConfig config=candidate.baseCombatConfigHash==legacyHash?legacy:candidate.baseCombatConfigHash==previousHash?previous:candidate.baseCombatConfigHash==latestHash?latest:null;
                Require(config!=null,"profile_config_mismatch");
                new ProfileSession{BaseCombatConfig=config,Config=session.Config}.Validate(candidate);selected=config;
            });
            session.State=session.Store.Load();session.BaseCombatConfig=selected;selecting=false;
            session.nextDepartureP5Rules=CombatJson.Clone(p5Config.p5);session.nextDepartureP6Rules=CombatJson.Clone(p6Config.p6);
            session.ActiveCombat=session.CreateBinding(session.State);return session;
        }
        static void ValidateP6Defaults(CombatConfig latest,CombatConfig previous,CombatConfig legacy)
        {
            ValidateCompatibleDefaults(previous,legacy);
            Require(latest!=null&&latest.p6!=null&&previous.p6==null&&legacy.p6==null,"p6_and_previous_config_required");
            var original=CombatJson.Clone(latest);original.p6=null;
            Require(Hash(original)==Hash(previous),"p6_previous_base_changed");
            Require(latest.p6.presetId==P6CharacterCatalog.PresetId,"unsupported_p6_preset");
        }
        static string P6DepartureHash(P6DepartureSnapshot snapshot)
        {return Hash(new{snapshot.presetId,snapshot.recoveryPolicy,snapshot.rulesVersion,snapshot.rules});}
        void SnapshotP6Departure(ModeState mode)
        {
            if(nextDepartureP6Rules==null)return;
            var snapshot=new P6DepartureSnapshot{rules=CombatJson.Clone(nextDepartureP6Rules),rulesVersion=nextDepartureP6Rules.rulesVersion,presetId=nextDepartureP6Rules.presetId};
            snapshot.hash=P6DepartureHash(snapshot);mode.p6Departure=snapshot;
        }
        static void ValidateP6Departure(ModeState mode)
        {
            if(!UsesP6(mode))return;
            var d=mode.p6Departure;
            Require(d.rules!=null&&d.presetId==P6CharacterCatalog.PresetId&&d.rules.presetId==d.presetId&&d.recoveryPolicy=="ExactState"&&d.rulesVersion==d.rules.rulesVersion&&d.hash==P6DepartureHash(d),"departure_p6_rules_changed");
        }
    }
}
