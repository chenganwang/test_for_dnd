using System;
using System.Collections.Generic;

namespace CombatV051.Domain
{
    [Serializable] public sealed partial class ActionSpec
    {
        public string id="attack",rollType="Attack";
        public float range=7,windup=.35f;
        public int damage=4,spellCost,itemCost;
        public bool ranged=true;
        public float shoveDistance=1.5f,breakDamage=1;
        public string saveAbility="Strength",effectId;
        public float effectSeconds;
        public int conditionalDamage;
        public bool requiresFlanking;
    }
    [Serializable] public sealed partial class CombatConfig
    {
        public float focusScale=.1f,rollAnimationSeconds=.55f;
        public float majorRecovery=1.2f,bonusRecovery=4,reactionRecovery=8;
        public float basicRange=2.2f;
        public int basicDamage=2;
        public float aimSeconds=1,guardSeconds=1.4f,parrySeconds=.3f,dodgeSeconds=.3f,dodgeRecovery=1;
        // The two historical caps are retained for old config parsing only. P4 budgets only tactical awards.
        public float passiveMomentumPerSecond=4,passiveEncounterCap=0,totalEncounterMomentumCap=0,basicHitInternalCooldown=1,templateWindow=4;
        public float basicHitMomentum=1,perfectDodgeMomentum=30,parryMomentum=30,weakpointMomentum=30,flankMomentum=30,interruptMomentum=40,environmentMomentum=40,threatKillMomentum=25,aimMomentum=0,guardMomentum=30;
        public float tacticalBudgetNormal=100,tacticalBudgetElite=150,tacticalBudgetBossPhase=100,paidChainMomentumCap=60;
        public float preparationSeconds=1,preparationEffectSeconds=4,preparationRepeatSeconds=6,basicContactSeconds=.12f;
        public float preparationRange=8,orderArrivalDistance=.28f,orderTimeoutSeconds=12;
        public float lastLowThreatHealthFraction=.25f,effectiveCombatRange=12;
        public List<ActionSpec> actions=new List<ActionSpec>{new ActionSpec(),new ActionSpec{id="shove",rollType="Save",range=2.5f,windup=.25f,damage=0,ranged=false,shoveDistance=1.5f,breakDamage=1}};
        public ActionSpec Action(string id) { if(v13!=null&&id=="v13_evan_correct")return new ActionSpec{id=id,range=7,rollType="Check"}; if(v12!=null&&CombatSession.V12Action(id))return CombatSession.V12Spec(id,v12); if(v11!=null&&V11AbilityCatalog.IsCustom(id))return V11AbilityCatalog.Action(id); if(p6!=null){if(id=="ability_smite_recharge")return p6.SmiteSpec();if(id=="ability_bind_radiance")return p6.BindSpec();}return p5?.actions?.Find(a=>a.id==id)??actions.Find(a=>a.id==id)??partyActions.Find(a=>a.id==id); }
    }
}
