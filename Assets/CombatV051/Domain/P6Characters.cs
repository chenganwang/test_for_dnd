using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class P6CharacterDefinition
    {
        public string id,classId,displayName,weaponId;
        public ActorRole role;
        public List<string> actionIds=new List<string>();
    }
    public static class P6CharacterCatalog
    {
        public const string PresetId="preset_greatsword_smite",PaladinId="char_demo_paladin";
        // Author names are deliberately absent. These labels describe playable roles.
        public static IReadOnlyList<P6CharacterDefinition> All {get{return new[]{
            new P6CharacterDefinition{id=PaladinId,classId="class_paladin",displayName="圣武士 [姓名待填写]",weaponId="weapon_demo_greatsword",role=ActorRole.Paladin,actionIds=new List<string>{"ability_smite_recharge"}},
            new P6CharacterDefinition{id="mage",classId="class_mage",displayName="法师 [姓名待填写]",weaponId="mage-staff",role=ActorRole.Mage,actionIds=new List<string>{"ability_bind_radiance","control"}},
            new P6CharacterDefinition{id="rogue",classId="class_rogue",displayName="游荡者 [姓名待填写]",weaponId="rogue-blades",role=ActorRole.Rogue,actionIds=new List<string>{"sneak"}}
        };}}
        public static P6CharacterDefinition Find(string id){return All.FirstOrDefault(c=>c.id==id);}
        public static PartyPreparation DefaultPreparation()
        {
            return new PartyPreparation{defaultActiveActorId=PaladinId,actors=All.Select(c=>new ActorPreparation{
                actorId=c.id,weaponId=c.weaponId,preparedActionIds=new List<string>(c.actionIds),
                specializationId=c.role==ActorRole.Paladin?PresetId:c.role==ActorRole.Mage?"M_INSIGHT":"R_FLANK"
            }).ToList()};
        }
    }
    public sealed partial class CombatState
    {
        public static CombatState CreatePartyForPreset(string profileId,string modeInstanceId,ulong seed,CombatConfig config,bool newRun=true)
        {
            if(config==null)throw new ArgumentNullException(nameof(config));
            var state=CreatePartyP1(profileId,modeInstanceId,seed,config);
            if(config.p6!=null)
            {
                if(config.p6.presetId!=P6CharacterCatalog.PresetId)throw new InvalidOperationException("unsupported_p6_preset");
                state.actors=new List<ActorState>{config.Template(ActorRole.Paladin).Create(P6CharacterCatalog.PaladinId,-3,0),config.Template(ActorRole.Mage).Create("mage",-5,2),config.Template(ActorRole.Rogue).Create("rogue",-4,-2)};
                state.activeActorId=state.commandActorId=P6CharacterCatalog.PaladinId;
            }
            ApplyV11Party(state,config,newRun);
            CombatSession.InitializeP5State(state,config);
            CombatSession.InitializeP6State(state,config,newRun);
            return state;
        }
    }
}
