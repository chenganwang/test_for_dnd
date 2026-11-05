using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        void CreateV11Reward(ModeState mode,RoomState room)
        {
            string id=room.id+"/reward";if(mode.claimedRewardIds.Contains(id))return;
            if(mode.pendingReward?.id==id)return; // Persist the actual offered cards; re-entry/load is not a reroll.
            if(UsesV12Build(mode))
            {Require(HasV12Map(mode),"v12_build_requires_six_room_reward_route");mode.pendingReward=new RewardOffer{id=id,roomId=room.id,candidates=V12RewardCandidates(mode,room)};return;}
            var rules=V11RulesFor(mode);var pool=V11BuffCatalog.Eligible(rules).Select(d=>d.id).ToList();
            for(int i=pool.Count-1;i>0;i--){int j=RandomStreams.NextInt(mode.random.contentReward,0,i+1);string swap=pool[i];pool[i]=pool[j];pool[j]=swap;}
            var selected=new List<string>();string core=pool.FirstOrDefault(x=>V11BuffCatalog.Find(x).coreActorId==rules.coreActorId);
            if(core!=null)selected.Add(core);foreach(string entry in pool)if(selected.Count<3&&!selected.Contains(entry))selected.Add(entry);
            mode.pendingReward=new RewardOffer{id=id,roomId=room.id,candidates=selected};
        }
        ProfileResult V11Reward(ProfileState profile,ProfileCommand command)
        {
            var mode=profile.ActiveMode;EnsureSafe(mode);var offer=mode.pendingReward;
            Require(offer!=null&&offer.stage=="Offered"&&(string.IsNullOrEmpty(command.rewardId)||command.rewardId==offer.id),"reward_not_available");
            Require(!mode.claimedRewardIds.Contains(offer.id),"v11_reward_already_consumed");
            if(command.kind==ProfileCommandKind.ClaimReward)
            {
                Require(offer.candidates.Contains(command.selectionId)&&V11BuffCatalog.Eligible(V11RulesFor(mode)).Any(d=>d.id==command.selectionId),"v11_reward_no_longer_eligible");
                Require(V12RewardAllowedAtRoom(mode,offer.roomId,command.selectionId),"v12_reward_wrong_room");
                var stack=mode.v11Build.buffs.Find(b=>b.id==command.selectionId);
                if(stack==null)mode.v11Build.buffs.Add(new V11BuffStack{id=command.selectionId,stacks=1});else stack.stacks++;
                if(UsesV12Build(mode))CombatSession.ApplyV12BuildReward(Current(mode),BuildConfig(mode),offer.id,command.selectionId);
                offer.chosenId=command.selectionId;offer.stage="Claimed";
            }
            else offer.stage="Skipped";
            AddOnce(mode.claimedRewardIds,offer.id);return new ProfileResult{rewardId=offer.id};
        }
    }
}
