using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        static bool UsesV12Build(ModeState mode)=>V11BuffCatalog.UsesV12Build(V11RulesFor(mode));
        static bool V12RewardAllowedAtRoom(ModeState mode,string roomId,string selection)
        {
            if(!UsesV12Build(mode))return true;
            var d=V11BuffCatalog.Find(selection);var room=mode.Room(roomId);if(d==null||room==null)return false;
            var rules=V11RulesFor(mode);
            if(V14BuildCatalog.UsesNewRoomOffers(rules))return new[]{"V12_R1","V12_R2","V12_R4","V12_R5"}.Contains(room.templateId)&&
                (d.pool=="Generic"||selection==V14BuildCatalog.Guaranteed(rules,room.templateId));
            if(room.templateId=="V12_R2"||room.templateId=="V12_R5")return d.pool=="Generic";
            if(room.templateId!="V12_R1"&&room.templateId!="V12_R4")return false;
            if(mode.v11Departure.preparation.coreId==P6CharacterCatalog.PaladinId)return d.pool=="Core"&&d.coreActorId==P6CharacterCatalog.PaladinId;
            return room.templateId=="V12_R1"?d.pool=="Core"&&!selection.EndsWith("_up",StringComparison.Ordinal):d.pool=="Generic"||selection.EndsWith("_up",StringComparison.Ordinal);
        }
        static void ShuffleRewardPool(ModeState mode,List<string> pool)
        {
            for(int i=pool.Count-1;i>0;i--){int j=RandomStreams.NextInt(mode.random.contentReward,0,i+1);string swap=pool[i];pool[i]=pool[j];pool[j]=swap;}
        }
        static List<string> V12RewardCandidates(ModeState mode,RoomState room)
        {
            var rules=V11RulesFor(mode);var legal=V11BuffCatalog.Eligible(rules);
            if(V14BuildCatalog.UsesNewRoomOffers(rules))return V14RewardCandidates(mode,room,rules);
            var generic=legal.Where(d=>d.pool=="Generic").Select(d=>d.id).ToList();
            var core=legal.Where(d=>d.pool=="Core").Select(d=>d.id).ToList();
            if(room.templateId=="V12_R2"||room.templateId=="V12_R5"){ShuffleRewardPool(mode,generic);return generic.Take(3).ToList();}
            Require(room.templateId=="V12_R1"||room.templateId=="V12_R4","v12_reward_room_invalid");
            if(rules.coreActorId==P6CharacterCatalog.PaladinId){ShuffleRewardPool(mode,core);return core.Take(3).ToList();}
            if(room.templateId=="V12_R1")return core.Where(id=>!id.EndsWith("_up",StringComparison.Ordinal)).Take(2).ToList();
            // A chosen form's single upgrade remains first. No form selected means generic only.
            var selected=core.Where(id=>id.EndsWith("_up",StringComparison.Ordinal)).Take(1).ToList();
            ShuffleRewardPool(mode,generic);selected.AddRange(generic.Take(selected.Count==0?3:2));return selected;
        }
        void ApplyV12CommittedExploration(ModeState mode,SafeEventState ev)
        {
            if(!UsesV12Build(mode)||!HasV12Map(mode)||ev.stage!="Committed")return;
            bool read=ev.templateId=="V12_READ"&&ev.usedAlternative&&ev.rollCount==0;
            bool search=ev.templateId=="V12_SEARCH"&&ev.success;
            if(read||search)CombatSession.ApplyV12ExplorationReward(Current(mode),BuildConfig(mode),ev.id);
        }
    }
}
