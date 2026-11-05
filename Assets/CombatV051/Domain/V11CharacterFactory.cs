using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    public sealed partial class CombatState
    {
        public static void ApplyV11Party(CombatState state,CombatConfig config,bool newRun)
        {
            if(config.v11==null)return;
            if(config.v11.actors==null||config.v11.actors.Count!=3)throw new InvalidOperationException("v11_three_actor_loadouts_required");
            var actors=new List<ActorState>();
            for(int i=0;i<config.v11.actors.Count;i++)
            {
                var id=config.v11.actors[i].actorId;var definition=V11Catalog.FindCharacter(id);
                if(definition==null)throw new InvalidOperationException("v11_unknown_character:"+id);
                var actor=config.Template(definition.role).Create(id,-3-i,i==1?2:i==2?-2:0);
                actor.p6=new P6ActorState{characterId=id,classId=definition.role==ActorRole.Paladin?"class_paladin":definition.role==ActorRole.Mage?"class_mage":definition.role==ActorRole.Rogue?"class_rogue":"class_fighter",weaponId=definition.weaponId};
                actors.Add(actor);
            }
            state.actors=actors;state.activeActorId=state.commandActorId=config.v11.coreActorId;
            CombatSession.InitializeV11State(state,config,newRun);
        }
    }
}
