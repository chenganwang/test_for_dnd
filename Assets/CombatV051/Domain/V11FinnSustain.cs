using System;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        const string V11FinnId="char_story_finn";
        static bool V11Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        V11ActorLoadout V11SustainLoadout(string id)
        {var list=Config.v11?.actors;if(list!=null)for(int i=0;i<list.Count;i++)if(list[i].actorId==id)return list[i];return null;}
        void ValidateV11Sustain(CombatState s,V11ActorLoadout loadout,ActorState actor)
        {
            var rules=loadout.finnSustain;var state=actor.v11?.finnSustain;
            if(rules!=null)
                Require(loadout.actorId==V11FinnId&&V11Finite(rules.maxHpFraction)&&rules.maxHpFraction>0&&rules.maxHpFraction<=1&&Finite(rules.intervalSeconds)&&rules.intervalSeconds>0&&
                    (rules.targetActorId==null||s.actors.Exists(a=>a.id==rules.targetActorId)),"invalid_finn_sustain_config");
            if(state!=null)
                Require(rules!=null&&V11Finite(state.fractionalHp)&&state.fractionalHp>=0&&state.fractionalHp<1&&Finite(state.lastSimulationTime)&&state.lastSimulationTime>=0&&state.lastSimulationTime<=s.simulationSeconds+.0001f&&
                    (state.lastRoot==null?state.fractionalHp==0:!string.IsNullOrWhiteSpace(state.lastRoot)),"invalid_finn_sustain_state");
        }
        // Called only by the ordinary basic-contact authority after actual HP loss.
        // IncomingAttack/Save, skill/AOE, DoT and reaction paths never call this hook.
        void V11OnBasicHpDamage(CombatState s,ActorState source,ActorState victim,BasicAttackIntent intent,string basicId,int actualHpLoss,CombatResult result)
        {
            if(source?.id!=V11FinnId||!source.Alive||source.isEnemy||victim==null||!victim.isEnemy||!s.enemies.Contains(victim)||actualHpLoss<=0||
                intent==null||!intent.resolved||intent.targetId!=victim.id||intent.actionId!=basicId||string.IsNullOrEmpty(intent.rootId))return;
            var loadout=V11SustainLoadout(source.id);var rules=loadout?.finnSustain;
            if(rules==null||!loadout.passiveUnlocked||loadout.passiveId!="v11_finn_resolve")return;
            var target=rules.targetActorId==null?source:s.Actor(rules.targetActorId);
            if(target==null||target.isEnemy||!target.Alive||!s.actors.Contains(target))return;
            if(source.v11.finnSustain==null)source.v11.finnSustain=new V11FinnSustainState();var state=source.v11.finnSustain;
            if(state.lastRoot==intent.rootId)return;
            // No stored healing from hitting while already full, even inside the interval.
            if(target.hp>=target.maxHp)state.fractionalHp=0;
            if(state.lastRoot!=null&&s.simulationSeconds-state.lastSimulationTime+.00001f<rules.intervalSeconds)return;
            state.lastRoot=intent.rootId;state.lastSimulationTime=s.simulationSeconds;
            if(target.hp>=target.maxHp)return;
            double accrued=state.fractionalHp+target.maxHp*rules.maxHpFraction*V12HealingScale(Config);
            int whole=(int)Math.Floor(accrued);int healed=Math.Min(target.maxHp-target.hp,whole);
            target.hp+=healed;state.fractionalHp=target.hp>=target.maxHp?0:accrued-whole;
            if(healed<=0)return; // Fraction-only progress produces no VFX/SFX or floating number.
            if(result.v11==null)result.v11=new V11ActionResult();
            result.v11.effects.Add(new V11EffectResult{actorId=source.id,targetId=target.id,kind="micro_heal",amount=healed});
        }
        void CleanupV11Sustain(CombatState s)
        {
            if(Config.v11==null)return;
            foreach(var actor in s.actors)
            {
                var state=actor.v11?.finnSustain;if(state==null||state.fractionalHp==0)continue;
                var rules=V11SustainLoadout(actor.id)?.finnSustain;
                var target=rules?.targetActorId==null?actor:s.Actor(rules.targetActorId);
                if(!actor.Alive||target==null||!target.Alive||target.hp>=target.maxHp)state.fractionalHp=0;
            }
        }
    }
}
