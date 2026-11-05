using System;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    public sealed partial class ModeState
    {
        // Absent on historical modes: omitting null preserves their JSON/hash contract.
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public P5Config departureP5Rules;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public string departureP5RulesHash;
    }

    public sealed partial class ProfileSession
    {
        P5Config nextDepartureP5Rules;
        public bool ActiveUsesP5 { get { return ActiveCombat!=null && ActiveCombat.Config.p5!=null; } }

        public static ProfileSession CreateNewCompatible(string profileId,ulong seed,CombatConfig p5Config,CombatConfig legacyConfig,DungeonConfig dungeonConfig,string path=null)
        {
            ValidateCompatibleDefaults(p5Config,legacyConfig);
            return CreateNewCore(profileId,seed,legacyConfig,dungeonConfig,path,p5Config.p5);
        }

        public static ProfileSession LoadCompatible(string path,CombatConfig p5Config,CombatConfig legacyConfig,DungeonConfig dungeonConfig)
        {
            ValidateCompatibleDefaults(p5Config,legacyConfig);
            var legacy=CombatJson.Clone(legacyConfig);var current=CombatJson.Clone(p5Config);
            string legacyHash=Hash(legacy),currentHash=Hash(current);
            var session=new ProfileSession{Config=CombatJson.Clone(dungeonConfig),BaseCombatConfig=legacy};
            session.ValidateConfig();bool selecting=true;CombatConfig selected=null;
            // ProfileStore authenticates the envelope before calling this validator. Select
            // the config for each candidate inside ONE main/backup load, never by retrying
            // loads with different configs and accidentally preferring an older backup.
            session.Store=new ProfileStore(path,candidate=>
            {
                if(!selecting){session.Validate(candidate);return;}
                CombatConfig config=candidate.baseCombatConfigHash==legacyHash?legacy:candidate.baseCombatConfigHash==currentHash?current:null;
                Require(config!=null,"profile_config_mismatch");
                var probe=new ProfileSession{BaseCombatConfig=config,Config=session.Config};
                probe.Validate(candidate);selected=config;
            });
            session.State=session.Store.Load();session.BaseCombatConfig=selected;selecting=false;
            session.nextDepartureP5Rules=CombatJson.Clone(p5Config.p5);
            session.ActiveCombat=session.CreateBinding(session.State);
            return session;
        }

        static void ValidateCompatibleDefaults(CombatConfig p5Config,CombatConfig legacyConfig)
        {
            Require(p5Config!=null&&p5Config.p5!=null&&legacyConfig!=null&&legacyConfig.p5==null,"p5_and_legacy_config_required");
            var original=CombatJson.Clone(p5Config);original.p5=null;
            Require(Hash(original)==Hash(legacyConfig),"p5_legacy_base_changed");
        }
        void SnapshotP5Departure(ModeState mode)
        {
            if(nextDepartureP5Rules==null)return;
            mode.departureP5Rules=CombatJson.Clone(nextDepartureP5Rules);
            mode.departureP5RulesHash=Hash(mode.departureP5Rules);
        }
        static void ValidateP5Departure(ModeState mode)
        {
            Require(mode.departureP5Rules==null?mode.departureP5RulesHash==null:
                !string.IsNullOrEmpty(mode.departureP5RulesHash)&&mode.departureP5RulesHash==Hash(mode.departureP5Rules),"departure_p5_rules_changed");
        }
    }
}
