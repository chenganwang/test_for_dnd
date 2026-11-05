using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class ActorPreparation
    {
        public string actorId,specializationId,weaponId;
        public List<string> preparedActionIds=new List<string>();
    }
    [Serializable] public sealed class PartyPreparation
    {
        public string defaultActiveActorId="fighter",primaryLawId="BASE";
        public List<ActorPreparation> actors=new List<ActorPreparation>();
        public PartyPreparation Clone(){return CombatJson.Clone(this);}
        public static PartyPreparation Default()
        {
            return new PartyPreparation{actors=new List<ActorPreparation>{
                new ActorPreparation{actorId="fighter",specializationId="F_GUARD",weaponId="fighter-sword-shield",preparedActionIds=new List<string>{"attack","shove"}},
                new ActorPreparation{actorId="rogue",specializationId="R_FLANK",weaponId="rogue-blades",preparedActionIds=new List<string>{"sneak"}},
                new ActorPreparation{actorId="mage",specializationId="M_INSIGHT",weaponId="mage-staff",preparedActionIds=new List<string>{"control"}}}};
        }
    }
    [Serializable] public sealed class SpecializationDefinition
    {
        public string id,name,description,unlockMilestone;
        public ActorRole role;
        public int costResearch;
        public ActorRuleOverride effects;
    }
    [Serializable] public sealed class PreparationSnapshot
    {
        public string rulesVersion="v052-preparation-1",hash;
        public PartyPreparation preparation;
        public List<ActorRuleOverride> actorOverrides=new List<ActorRuleOverride>();
    }
    public static class ProfilePreparationCatalog
    {
        static SpecializationDefinition Definition(string id,ActorRole role,string name,string description,string milestone,ActorRuleOverride effects)
        {return new SpecializationDefinition{id=id,role=role,name=name,description=description,unlockMilestone=milestone,costResearch=milestone==null?0:3,effects=effects};}
        // Return independent definitions: UI inspection cannot mutate the runtime catalog.
        public static IReadOnlyList<SpecializationDefinition> All {get{return new[]{
            Definition("F_GUARD",ActorRole.Fighter,"护卫","守势与保护反应的合法保护范围增加至4.5米。",null,new ActorRuleOverride{specializationId="F_GUARD",protectionRange=4.5f}),
            Definition("F_PRESSURE",ActorRole.Fighter,"压迫","每遭遇首次成功推击额外削减1点韧性。","R1",new ActorRuleOverride{specializationId="F_PRESSURE",firstShoveBonus=true}),
            Definition("F_ADEPT",ActorRole.Fighter,"熟手","每遭遇一次非自然1、差不超过2的攻击失败，可消耗战技资源确定+2补正。","R3",new ActorRuleOverride{specializationId="F_ADEPT",nearMissCorrection=true}),
            Definition("R_FLANK",ActorRole.Rogue,"侧袭","合格侧翼准备效果持续6模拟秒。",null,new ActorRuleOverride{specializationId="R_FLANK",flankWindowSeconds=6}),
            Definition("R_MOBILE",ActorRole.Rogue,"机动","侧翼准备移动倍率提高至0.85，仍按真实地形移动。","R1",new ActorRuleOverride{specializationId="R_MOBILE",preparationMoveMultiplier=.85f}),
            Definition("R_DISTRACT",ActorRole.Rogue,"牵制","队友首次利用侧翼准备时延长目标已有暴露一次；不凭空制造失衡。","R3",new ActorRuleOverride{specializationId="R_DISTRACT",firstExposeExtension=true}),
            Definition("M_INSIGHT",ActorRole.Mage,"洞察","对有效目标的观察准备缩短至0.8模拟秒。",null,new ActorRuleOverride{specializationId="M_INSIGHT",studySeconds=.8f}),
            Definition("M_WARD",ActorRole.Mage,"护持","可为合法范围内的队友使用护盾反应，仍支付本角色反应与法术位。","R1",new ActorRuleOverride{specializationId="M_WARD",allyShield=true}),
            Definition("M_FOCUS",ActorRole.Mage,"专注","每遭遇首次非致命伤害保留已有专注；后续伤害或倒下仍打断。","R3",new ActorRuleOverride{specializationId="M_FOCUS",concentrationProtection=true})};}}
        public static SpecializationDefinition Find(string id){return All.FirstOrDefault(d=>d.id==id);}
        public static string ActorId(ActorRole role){return role==ActorRole.Fighter?"fighter":role==ActorRole.Rogue?"rogue":role==ActorRole.Mage?"mage":null;}
    }
    public sealed partial class ProfileSession
    {
        public PartyPreparation PreparationDraft {get{return State.meta.lastPreparation.Clone();}}
        public bool SpecializationUnlocked(string id){return State.meta.unlockedSpecializations.Contains(id);}
        public ProfileResult SavePreparation(PartyPreparation preparation,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.SavePreparation,preparation=preparation});}
        public ProfileResult UnlockSpecialization(string id,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.UnlockSpecialization,selectionId=id});}
        public ProfileResult ApplyStoryPreparation(PartyPreparation preparation,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.ApplyStoryPreparation,preparation=preparation});}
        void ValidatePreparation(ProfileState profile,PartyPreparation preparation,bool requireUnlocked)
        {
            Require(preparation!=null&&preparation.actors!=null&&preparation.actors.Count==3&&preparation.actors.All(a=>a!=null),"invalid_party_preparation");
            var defaults=PartyPreparation.Default();
            Require(preparation.actors.Select(a=>a.actorId).Distinct().Count()==3&&defaults.actors.All(a=>preparation.actors.Any(x=>x.actorId==a.actorId)),"invalid_prepared_party");
            Require(preparation.actors.Any(a=>a.actorId==preparation.defaultActiveActorId),"invalid_default_active_actor");
            Require(Config.Rule(preparation.primaryLawId)?.kind=="Law"&&(!requireUnlocked||profile.meta.unlockedLaws.Contains(preparation.primaryLawId)),"prepared_law_locked_or_invalid");
            foreach(var actor in preparation.actors)
            {
                var definition=ProfilePreparationCatalog.Find(actor.specializationId);var baseline=defaults.actors.Single(a=>a.actorId==actor.actorId);
                Require(definition!=null&&ProfilePreparationCatalog.ActorId(definition.role)==actor.actorId,"invalid_actor_specialization");
                Require(!requireUnlocked||profile.meta.unlockedSpecializations.Contains(definition.id),"specialization_locked");
                Require(actor.weaponId==baseline.weaponId&&actor.preparedActionIds!=null&&actor.preparedActionIds.SequenceEqual(baseline.preparedActionIds),"unsupported_equipment_or_prepared_actions");
            }
        }
        static string PreparationHash(PreparationSnapshot snapshot)
        {return Hash(new{snapshot.rulesVersion,snapshot.preparation,snapshot.actorOverrides});}
        PreparationSnapshot MakePreparationSnapshot(ProfileState profile,PartyPreparation preparation)
        {
            ValidatePreparation(profile,preparation,true);
            var snapshot=new PreparationSnapshot{preparation=preparation.Clone()};
            foreach(var actor in snapshot.preparation.actors)
            {var rule=CombatJson.Clone(ProfilePreparationCatalog.Find(actor.specializationId).effects);rule.actorId=actor.actorId;snapshot.actorOverrides.Add(rule);}
            snapshot.hash=PreparationHash(snapshot);return snapshot;
        }
        ProfileResult ApplyPreparationCommand(ProfileState profile,ProfileCommand command)
        {
            if(command.kind==ProfileCommandKind.UnlockSpecialization)
            {
                var definition=ProfilePreparationCatalog.Find(command.selectionId);Require(definition!=null,"unknown_specialization");
                if(profile.meta.unlockedSpecializations.Contains(definition.id))return new ProfileResult{reason="already_unlocked"};
                Require(definition.costResearch>0&&profile.meta.research>=definition.costResearch,"research_unavailable");
                profile.meta.research-=definition.costResearch;AddOnce(profile.meta.unlockedSpecializations,definition.id);return new ProfileResult();
            }
            var preparation=command.preparation??profile.meta.lastPreparation;
            ValidatePreparation(profile,preparation,true);
            if(command.kind==ProfileCommandKind.ApplyStoryPreparation)
            {
                var mode=profile.ActiveMode;EnsureSafe(mode);EnsureRewardFinished(mode);
                Require(!UsesP6(mode),"p6_departure_preparation_is_immutable");
                Require(mode.kind==ProfileModeKind.Story&&mode.Room(mode.currentRoomId).templateId=="R0","story_preparation_point_required");
                var combat=Current(mode);Require(Near(combat.Actor(combat.activeActorId),0,0,6),"preparation_point_out_of_reach");
                Require(combat.Actor(preparation.defaultActiveActorId).Alive,"prepared_active_actor_unavailable");
                mode.departurePreparation=MakePreparationSnapshot(profile,preparation);mode.build.primaryLawId=preparation.primaryLawId;
                combat.activeActorId=preparation.defaultActiveActorId;combat.commandActorId=preparation.defaultActiveActorId;
                // Do not recreate actors: HP, spent slots, inspiration, cooldowns and used rest remain exactly as held.
                mode.planHash=PlanHash(mode);
            }
            profile.meta.lastPreparation=preparation.Clone();return new ProfileResult();
        }
        static void GrantSpecializationMilestone(ProfileState profile,string roomId)
        {foreach(var definition in ProfilePreparationCatalog.All.Where(d=>d.unlockMilestone==roomId))AddOnce(profile.meta.unlockedSpecializations,definition.id);}
        public void RecordActivity(float realDelta,bool explicitPaused=false,bool focusLost=false)
        {
            Require(!float.IsNaN(realDelta)&&!float.IsInfinity(realDelta)&&realDelta>=0,"invalid_delta");
            if(focusLost)ActiveMode.focusLostSeconds+=realDelta;
            else if(explicitPaused)ActiveMode.explicitPauseSeconds+=realDelta;
            else ActiveMode.elapsedRealSeconds+=realDelta;
        }
    }
}
