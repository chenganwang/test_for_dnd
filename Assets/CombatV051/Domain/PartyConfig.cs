using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed class BasicAttackSpec
    {
        public string id="basic-attack";
        public float range=2.2f;
        public int damage=2;
        public bool ranged;
    }
    [Serializable] public sealed class ActorTemplate
    {
        public ActorRole role;
        public FormationPreference formation;
        public int hp=30,armorClass=14,attackBonus=5,proficiency=2,strengthModifier=3,dexterityModifier,intelligenceModifier,wisdomModifier=1,constitutionModifier=2,charismaModifier;
        public int saveBonus=3,dexSaveBonus,wisSaveBonus=1,inspiration=2,superiority=2,spellSlots,items=1;
        public BasicAttackSpec basic=new BasicAttackSpec();
        public List<string> actionIds=new List<string>();
        public ActorState Create(string id,float x,float z)
        {
            return new ActorState{id=id,x=x,z=z,lockedZ=1,role=role,formation=formation,hp=hp,maxHp=hp,armorClass=armorClass,attackBonus=attackBonus,proficiency=proficiency,strengthModifier=strengthModifier,dexterityModifier=dexterityModifier,intelligenceModifier=intelligenceModifier,wisdomModifier=wisdomModifier,constitutionModifier=constitutionModifier,charismaModifier=charismaModifier,saveBonus=saveBonus,dexSaveBonus=dexSaveBonus,wisSaveBonus=wisSaveBonus,inspiration=inspiration,superiority=superiority,spellSlots=spellSlots,items=items};
        }
    }
    [Serializable] public sealed class ReactionSpec
    {
        public string id;
        public ActorRole role;
        public bool requiresD20;
        public int spellCost,superiorityCost,shieldAcBonus;
        public float shieldSeconds;
        public bool protectAllies;
        public float protectionReduction=.5f;
    }
    public sealed partial class CombatConfig
    {
        public float reactionOfferSeconds=6,reactionDecisionSeconds=6,parryRange=3,rogueBackstabDot=-.5f;
        public int maxPendingIncoming=8;
        public int partyEnemyHealth=120;
        public List<ActorTemplate> actorTemplates=new List<ActorTemplate>{
            new ActorTemplate{role=ActorRole.Fighter,formation=FormationPreference.Front,actionIds=new List<string>{"attack","shove"}},
            new ActorTemplate{role=ActorRole.Rogue,formation=FormationPreference.Mid,hp=24,armorClass=15,strengthModifier=1,dexterityModifier=3,saveBonus=1,dexSaveBonus=5,superiority=0,actionIds=new List<string>{"sneak"}},
            new ActorTemplate{role=ActorRole.Mage,formation=FormationPreference.Back,hp=22,armorClass=12,strengthModifier=0,intelligenceModifier=3,saveBonus=0,wisSaveBonus=3,superiority=0,spellSlots=2,basic=new BasicAttackSpec{id="cantrip",range=7,damage=2,ranged=true},actionIds=new List<string>{"control"}}
        };
        public List<ActionSpec> partyActions=new List<ActionSpec>{
            new ActionSpec{id="sneak",range=2.5f,windup=.25f,ranged=false,damage=3,conditionalDamage=4,requiresFlanking=true},
            new ActionSpec{id="control",rollType="Save",saveAbility="Wisdom",range=7,windup=.4f,damage=0,spellCost=1,effectId="restrained",effectSeconds=2.5f}
        };
        public List<ReactionSpec> reactions=new List<ReactionSpec>{new ReactionSpec{id="shield",role=ActorRole.Mage,spellCost=1,shieldAcBonus=5,shieldSeconds=2},new ReactionSpec{id="parry",role=ActorRole.Fighter,requiresD20=true,superiorityCost=1,protectAllies=true,protectionReduction=.5f}};
        public ActorTemplate Template(ActorRole role){return role==ActorRole.Paladin&&p6!=null?p6.paladinTemplate:actorTemplates.Find(t=>t.role==role);}
        public BasicAttackSpec BasicFor(ActorState actor)
        {
            if(actor.role==ActorRole.Paladin&&p6!=null)return new BasicAttackSpec{id="basic_greatsword",range=p6.basicRange,damage=p6.paladinTemplate.basic.damage,ranged=false};
            var template=Template(actor.role);
            if(actor.role==ActorRole.Fighter||actor.isEnemy||template==null)return new BasicAttackSpec{range=basicRange,damage=basicDamage};
            return template.basic;
        }
        public ReactionSpec Reaction(string id){return reactions.Find(r=>r.id==id);}
    }
}
