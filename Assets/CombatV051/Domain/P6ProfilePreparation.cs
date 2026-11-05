using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class ProfileSession
    {
        public PartyPreparation P6PreparationDraft {get{return (State.meta.p6Preparation??P6CharacterCatalog.DefaultPreparation()).Clone();}}
        public ProfileResult SaveP6Preparation(PartyPreparation preparation,string commandId)
        {return Execute(new ProfileCommand{id=commandId,kind=ProfileCommandKind.SaveP6Preparation,preparation=preparation});}
        void ValidateP6Preparation(ProfileState profile,PartyPreparation preparation,bool requireUnlocked)
        {
            Require(preparation!=null&&preparation.actors!=null&&preparation.actors.Count==3&&preparation.actors.All(a=>a!=null),"invalid_p6_preparation");
            var defaults=P6CharacterCatalog.DefaultPreparation();
            Require(preparation.actors.Select(a=>a.actorId).Distinct().Count()==3&&defaults.actors.All(a=>preparation.actors.Any(x=>x.actorId==a.actorId)),"invalid_p6_party");
            Require(preparation.actors.Any(a=>a.actorId==preparation.defaultActiveActorId),"invalid_default_active_actor");
            Require(Config.Rule(preparation.primaryLawId)?.kind=="Law"&&(!requireUnlocked||profile.meta.unlockedLaws.Contains(preparation.primaryLawId)),"prepared_law_locked_or_invalid");
            foreach(var actor in preparation.actors)
            {
                var baseline=defaults.actors.Single(a=>a.actorId==actor.actorId);
                if(actor.actorId==P6CharacterCatalog.PaladinId)Require(actor.specializationId==P6CharacterCatalog.PresetId,"unsupported_paladin_preset");
                else
                {
                    var definition=ProfilePreparationCatalog.Find(actor.specializationId);
                    Require(definition!=null&&ProfilePreparationCatalog.ActorId(definition.role)==actor.actorId,"invalid_actor_specialization");
                    Require(!requireUnlocked||profile.meta.unlockedSpecializations.Contains(definition.id),"specialization_locked");
                }
                Require(actor.weaponId==baseline.weaponId&&actor.preparedActionIds!=null&&actor.preparedActionIds.SequenceEqual(baseline.preparedActionIds),"unsupported_p6_equipment_or_actions");
            }
        }
        PreparationSnapshot MakeP6PreparationSnapshot(ProfileState profile,PartyPreparation preparation)
        {
            ValidateP6Preparation(profile,preparation,true);
            var snapshot=new PreparationSnapshot{rulesVersion="v070-preparation-1",preparation=preparation.Clone()};
            foreach(var actor in snapshot.preparation.actors)
            {
                var rule=actor.actorId==P6CharacterCatalog.PaladinId?new ActorRuleOverride{specializationId=P6CharacterCatalog.PresetId}:CombatJson.Clone(ProfilePreparationCatalog.Find(actor.specializationId).effects);
                rule.actorId=actor.actorId;snapshot.actorOverrides.Add(rule);
            }
            snapshot.hash=PreparationHash(snapshot);return snapshot;
        }
        ProfileResult ApplyP6PreparationCommand(ProfileState profile,ProfileCommand command)
        {
            Require(nextDepartureP6Rules!=null||UsesP6(profile.ActiveMode),"p6_departure_unavailable");
            ValidateP6Preparation(profile,command.preparation,true);
            // A draft never replaces the immutable loadout of a departed run.
            profile.meta.p6Preparation=command.preparation.Clone();return new ProfileResult();
        }
    }
}
