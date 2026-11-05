using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V14DotPlan
    {
        public string instanceId,sourceId;
        public double rawPerTick,sourcePerTick,globalScale,periodicScale,buildScale;
        public float interval;
        public bool detonated;
    }
    [Serializable] public sealed class V14DetonationTarget
    {
        public string targetId,instanceId;
        public int paidTicks,remainingTicks,actualDamage;
        public double remainingSourceDamage,coefficient,sourceDamage,globalScale,periodicScale,buildScale,vulnerability,resistance;
    }
    [Serializable] public sealed class V14DetonationEvent
    {
        public string id,sourceId,actionId;
        public int chargeSpent;
        public float simulationTime;
        public List<V14DetonationTarget> targets=new List<V14DetonationTarget>();
    }
    [Serializable] public sealed class V14DetonationPreview
    {
        public bool legal;
        public string reason;
        public double sourceDamage;
        public int targetCount,remainingTicks,chargeCost=1;
    }
    public sealed partial class StatusState { [JsonProperty(DefaultValueHandling=DefaultValueHandling.Ignore)] public V14DotPlan v14Dot; }
    public sealed partial class CombatSession
    {
        public const string V14DetonateId="v14_finn_detonate";
        static int V14ReservedCharges(CombatState s)=>s.v14==null?0:s.actors.Count(a=>V12ActionActive(a)&&a.v12.intent.actionId==V14DetonateId);
        int V14RemainingTicks(StatusState status,bool excludeDue=false,float now=0)
        {
            if(status?.v14Dot==null||status.v14Dot.detonated)return 0;
            double next=status.v12.nextTick,expiry=status.v12.expiresAt,interval=status.v14Dot.interval;
            if(excludeDue&&next<=now+.00001)next+=(Math.Floor((now+.00001-next)/interval)+1)*interval;
            return next>expiry+.00001?0:Math.Max(0,(int)Math.Floor((expiry+.00001-next)/interval)+1);
        }
        StatusState V14OwnParasite(ActorState target,ActorState source)=>target?.statuses.Find(x=>x.sourceId==source?.id&&x.v12?.family=="parasite"&&x.v14Dot!=null&&!x.v14Dot.detonated);
        public V14DetonationPreview PreviewV14Detonation(string actorId,string targetId,bool lineOfSight=true)
        {
            var q=new V14DetonationPreview();if(!V14Enabled){q.reason="v14_rules_required";return q;}var a=State.Actor(actorId);var target=State.Actor(targetId);
            if(a?.Alive!=true||a.id!=V12Finn||target?.Alive!=true||!target.isEnemy){q.reason="living_enemy_required";return q;}
            if(!lineOfSight||Distance(V13Anchor(State,a),target)>7.001f){q.reason="target_out_of_range_or_blocked";return q;}
            var status=V14OwnParasite(target,a);q.remainingTicks=V14RemainingTicks(status,true,State.simulationSeconds);
            if(q.remainingTicks<=0){q.reason="own_parasite_no_unpaid_ticks";return q;}
            q.sourceDamage=q.remainingTicks*status.v14Dot.sourcePerTick*(V14CoreBuff("v14_finn_bloom",a.id)?1.25:1);q.targetCount=1;q.legal=true;q.reason="ok";return q;
        }
        void V14SnapshotParasite(CombatState s,ActorState source,ActorState target,StatusState status,string root)
        {
            if(!V14Enabled)return;
            double raw=Config.BasicFor(source).damage*(double)Config.v12.parasiteCoefficient,build=V14CoreBuff("v14_finn_densegrowth",source.id)?1.25:1;
            float interval=Math.Max(.2f,Config.v12.parasiteInterval*(V14CoreBuff("v14_finn_quickgrowth",source.id)?.75f:1));
            bool refreshing=status.v14Dot!=null;status.v14Dot=new V14DotPlan{instanceId=root+"/parasite/"+target.id,sourceId=source.id,rawPerTick=raw,sourcePerTick=V14SourceDamage(source,raw,true)*build,globalScale=Config.v14.playerDamageScale,periodicScale=source.id==V12Finn?Config.v14.finnPeriodicScale:1,buildScale=build,interval=interval};
            if(!refreshing)status.v12.nextTick=s.simulationSeconds+interval;
            status.v12.rootId=status.v14Dot.instanceId;status.v12.tickIndex=0;
        }
        void V14ResolveDueParasite(CombatState s,ActorState target,StatusState status,CombatResult result)
        {
            var plan=status.v14Dot;var p=status.v12;var source=s.Actor(status.sourceId);if(plan==null||plan.detonated||source?.Alive!=true)return;
            while(target.Alive&&p.nextTick<=s.simulationSeconds+.00001f&&p.nextTick<=p.expiresAt+.00001f)
            {
                p.tickIndex++;float tickTime=p.nextTick;p.nextTick+=plan.interval;
                result.damage+=V12Damage(s,source,target,plan.instanceId+"/tick/"+p.tickIndex,"v12_finn_seed","dot",plan.sourcePerTick,false,result,p,tickTime,null,true);
            }
        }
        void V14ResolveDueParasites(CombatState s,CombatResult result)
        {foreach(var e in s.enemies)foreach(var x in e.statuses.Where(x=>x.v14Dot!=null).ToList())V14ResolveDueParasite(s,e,x,result);}
        static void V14CleanupExpiredParasites(CombatState s)
        {foreach(var target in s.enemies)target.statuses.RemoveAll(x=>x.v14Dot!=null&&(x.v12.expiresAt<=s.simulationSeconds+.00001f||!target.Alive));}
        void V14SeedExtra(CombatState s,ActorState source,ActorState primary,string root,CombatResult result)
        {
            if(!V14CoreBuff("v14_finn_quickgrowth",source.id))return;var anchor=V13Anchor(s,source);
            var other=s.enemies.Where(e=>e.Alive&&e.id!=primary.id&&Distance(primary,e)<=2.501f&&Distance(anchor,e)<=Config.v12.seedRange+.001f&&V12BuildLine(anchor.id,e.id)).OrderBy(e=>Distance(primary,e)).ThenBy(e=>e.id,StringComparer.Ordinal).FirstOrDefault();
            if(other!=null)V12ApplyParasite(s,source,other,root+"/second",Config.v12.parasiteSeconds,0,result);
        }
        CombatResult V14ResolveDetonation(CombatState s,CombatCommand c,ActorState a,V12HeroIntent intent)
        {
            var result=new CombatResult{orderId=intent.id,damageApplied=true};
            // All due natural payments precede this command, including the expiry tick.
            V14ResolveDueParasites(s,result);
            var primary=s.Actor(intent.targetId);var anchor=V13Anchor(s,a);
            bool legal=a.Alive&&a.staggerRemaining<=0&&c.lineOfSight&&primary?.Alive==true&&primary.isEnemy&&Distance(anchor,primary)<=7.001f;
            var own=legal?V14OwnParasite(primary,a):null;
            if(!legal||V14RemainingTicks(own)<=0)
            {intent.stage="Canceled";intent.reason="own_parasite_expired_moved_or_blocked";a.windupRemaining=0;result.reason=intent.reason;V14CleanupExpiredParasites(s);return result;}
            var targets=new List<ActorState>{primary};bool bloom=V14CoreBuff("v14_finn_bloom",a.id);double coefficient=bloom?1.25:1;
            if(bloom)targets.AddRange(s.enemies.Where(e=>e.Alive&&e.id!=primary.id&&Distance(primary,e)<=2.501f&&Distance(anchor,e)<=7.001f&&V12BuildLine(anchor.id,e.id)&&V14RemainingTicks(V14OwnParasite(e,a))>0).OrderBy(e=>Distance(primary,e)).ThenBy(e=>e.id,StringComparer.Ordinal).Take(2));
            Require(s.charges>0,"reserved_charge_lost");
            var record=new V14DetonationEvent{id=intent.id,sourceId=a.id,actionId=V14DetonateId,chargeSpent=1,simulationTime=s.simulationSeconds};
            // Cancel the owned plans before any HP, kill or visual callback can observe them.
            foreach(var target in targets)
            {
                var status=V14OwnParasite(target,a);int remaining=V14RemainingTicks(status);var p=status.v14Dot;
                record.targets.Add(new V14DetonationTarget{targetId=target.id,instanceId=p.instanceId,paidTicks=status.v12.tickIndex,remainingTicks=remaining,remainingSourceDamage=remaining*p.sourcePerTick,coefficient=coefficient,sourceDamage=remaining*p.sourcePerTick*coefficient,globalScale=p.globalScale,periodicScale=p.periodicScale,buildScale=p.buildScale,vulnerability=V12Vulnerability(target),resistance=target.p6?.physicalDamageMultiplier??1});
                p.detonated=true;target.statuses.Remove(status);
            }
            intent.stage="Resolved";a.windupRemaining=0;a.majorRemaining=V13Enabled?Math.Max(0,Member(s,a.id).actionUntil-s.simulationSeconds):Config.majorRecovery;s.charges--;DrainMomentum(s);result.chargeSpent=1;
            if(s.v14Detonations==null)s.v14Detonations=new List<V14DetonationEvent>();s.v14Detonations.Add(record);
            foreach(var payment in record.targets){var target=s.Actor(payment.targetId);payment.actualDamage=V12Damage(s,a,target,intent.id,V14DetonateId,"detonate",payment.sourceDamage,false,result,null,null,intent.id,true);result.damage+=payment.actualDamage;}
            V14CleanupExpiredParasites(s);
            return result;
        }
    }
}
