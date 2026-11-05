using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V14DamageCarry { public string key; public double remainder; }
    [Serializable] public sealed class V14DamageEvent
    {
        public string id,sourceId,targetId,actionId,kind,sourceStage;
        public double baseDamage,sourceDamage,globalScale,periodicScale,vulnerability,resistance;
        public int actualDamage;
        public float simulationTime;
    }
    public sealed partial class ActorState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public List<V14DamageCarry> v14DamageCarries; }
    public sealed partial class CombatState
    {
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public List<V14DamageEvent> v14DamageEvents;
        [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public List<V14DetonationEvent> v14Detonations;
    }
    public sealed partial class CombatSession
    {
        double V14SourceDamage(ActorState source,double amount,bool periodic=false)
        {return !V14Enabled||source==null||source.isEnemy?amount:amount*Config.v14.playerDamageScale*(periodic&&source.id==V12Finn?Config.v14.finnPeriodicScale:1);}
        int V14Quantize(ActorState target,string key,double exact)
        {
            if(target.v14DamageCarries==null)target.v14DamageCarries=new List<V14DamageCarry>();
            var carry=target.v14DamageCarries.Find(x=>x.key==key);if(carry==null){carry=new V14DamageCarry{key=key};target.v14DamageCarries.Add(carry);}
            // Same one-millionth HP grid as existing healing, deterministic in Mono/CoreCLR.
            double units=Math.Round(exact*1000000,MidpointRounding.ToEven)+Math.Round(carry.remainder*1000000,MidpointRounding.ToEven);
            int whole=(int)Math.Floor(units/1000000);carry.remainder=(units-whole*1000000)/1000000;return Math.Max(0,whole);
        }
        int V14Mitigate(ActorState target,ActorState source,double sourceDamage,string channel,float? at=null)
        {
            double resistance=target.p6?.physicalDamageMultiplier??1;if(sourceDamage<=0||resistance<=0)return 0;
            int whole=V14Quantize(target,(source?.id??"world")+"/"+channel,sourceDamage*V12Vulnerability(target,1,at)*resistance);
            int shield=Math.Min(whole,target.p6?.absorptionShield??0);if(shield>0){target.p6.absorptionShield-=shield;V13ConsumeShield(target,shield);}
            return Math.Min(target.hp,whole-shield);
        }
        void V14LogDamage(CombatState s,ActorState source,ActorState target,string root,string action,string kind,double raw,double scaled,int actual,bool periodic=false,bool alreadyScaled=false,double? resistance=null,float? at=null,bool worldFixed=false)
        {
            if(!V14Enabled)return;if(s.v14DamageEvents==null)s.v14DamageEvents=new List<V14DamageEvent>();string id=root+"/"+kind+"/"+target.id;
            if(s.v14DamageEvents.Any(x=>x.id==id))return;
            s.v14DamageEvents.Add(new V14DamageEvent{id=id,sourceId=source?.id,targetId=target.id,actionId=action,kind=kind,sourceStage=worldFixed?"WorldFixed":alreadyScaled?"SourceSnapshot":"RawBase",baseDamage=raw,sourceDamage=scaled,globalScale=!worldFixed&&source?.isEnemy==false?Config.v14.playerDamageScale:1,periodicScale=!worldFixed&&periodic&&source?.id==V12Finn?Config.v14.finnPeriodicScale:1,vulnerability=V12Vulnerability(target,1,at),resistance=resistance??target.p6?.physicalDamageMultiplier??1,actualDamage=actual,simulationTime=s.simulationSeconds});
        }
        public static void V14ResetDamageEncounter(CombatState s)
        {
            if(s.v14==null)return;s.v14DamageEvents=null;s.v14Detonations=null;
            foreach(var a in s.actors.Concat(s.enemies)){a.v14DamageCarries=null;a.statuses.RemoveAll(x=>x.v14Dot!=null);}
        }
        void ValidateV14Damage(CombatState s)
        {
            if(!V14Enabled){Require(s.v14DamageEvents==null&&s.v14Detonations==null&&s.actors.Concat(s.enemies).All(a=>a.v14DamageCarries==null&&a.statuses.All(x=>x.v14Dot==null)),"v14_damage_without_rules");return;}
            foreach(var a in s.actors.Concat(s.enemies))
            {
                if(a.v14DamageCarries!=null)Require(a.v14DamageCarries.All(x=>x!=null&&!string.IsNullOrEmpty(x.key)&&V11Finite(x.remainder)&&x.remainder>=0&&x.remainder<1)&&a.v14DamageCarries.Select(x=>x.key).Distinct().Count()==a.v14DamageCarries.Count,"invalid_v14_damage_carry");
                var plans=a.statuses.Where(x=>x.v14Dot!=null).ToList();Require(plans.Select(x=>x.sourceId).Distinct().Count()==plans.Count,"duplicate_v14_dot_family");
                foreach(var x in plans){var p=x.v14Dot;Require(x.v12?.family=="parasite"&&a.isEnemy&&s.actors.Any(source=>source.id==x.sourceId)&&!string.IsNullOrEmpty(p.instanceId)&&V11Finite(p.rawPerTick)&&p.rawPerTick>0&&V11Finite(p.sourcePerTick)&&p.sourcePerTick>0&&p.globalScale==Config.v14.playerDamageScale&&p.periodicScale==(x.sourceId==V12Finn?Config.v14.finnPeriodicScale:1)&&(p.buildScale==1||p.buildScale==1.25)&&Math.Abs(p.sourcePerTick-p.rawPerTick*p.globalScale*p.periodicScale*p.buildScale)<.000001&&Finite(p.interval)&&p.interval>=.2f&&p.interval<=60&&p.sourceId==x.sourceId&&!p.detonated,"invalid_v14_dot_plan");}
            }
            if(s.v14DamageEvents!=null)Require(s.v14DamageEvents.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&V11Finite(x.baseDamage)&&x.baseDamage>=0&&V11Finite(x.sourceDamage)&&x.sourceDamage>=0&&x.actualDamage>=0&&Finite(x.simulationTime)&&x.simulationTime>=0)&&s.v14DamageEvents.Select(x=>x.id).Distinct().Count()==s.v14DamageEvents.Count,"invalid_v14_damage_events");
            if(s.v14Detonations!=null)Require(s.v14Detonations.All(x=>x!=null&&!string.IsNullOrEmpty(x.id)&&x.targets!=null&&x.targets.Count>0&&x.targets.Count<=3&&x.chargeSpent==1)&&s.v14Detonations.Select(x=>x.id).Distinct().Count()==s.v14Detonations.Count,"invalid_v14_detonations");
        }
    }
}
