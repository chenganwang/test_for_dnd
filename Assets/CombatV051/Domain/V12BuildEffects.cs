using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        int V12SecondaryBase(CombatState s,int original,float multiplier)
        {
            double scaled=original*multiplier;int whole=(int)Math.Floor(scaled);double fraction=scaled-whole;
            return whole+(fraction>0&&RandomStreams.NextInt(s.p6.damageRandom,1,10001)<=fraction*10000?1:0);
        }
        void V12Coauthor(CombatState s,ActorState evan,ActorState primary,string root,int raw,int actual)
        {
            if(actual<=0||!V12Form("v12_evan_coauthor",evan.id))return;
            var c=Config.v12.build;var extra=s.enemies.Where(e=>e.Alive&&e.id!=primary.id&&Distance(e,primary)<=c.coauthorRange&&V12BuildLine(primary.id,e.id)).OrderBy(e=>Distance(e,primary)).ThenBy(e=>e.id,StringComparer.Ordinal).FirstOrDefault();
            if(extra!=null)V12Damage(s,evan,extra,root+"/coauthor","v12_evan_coauthor","splash",V12SecondaryBase(s,raw,V12FormUp("v12_evan_coauthor",evan.id)?c.coauthorUpgradeMultiplier:c.coauthorMultiplier),false,null);
        }
        void V12Sweep(CombatState s,ActorState actor,ActorState primary,string root,int raw,CombatResult result)
        {
            if(!V12Form("v12_mira_sweep",actor.id))return;
            var c=Config.v12.build;bool up=V12FormUp("v12_mira_sweep",actor.id);float range=up?c.sweepUpgradeRange:c.sweepRange;
            float dx=primary.x-actor.x,dz=primary.z-actor.z,length=(float)Math.Sqrt(dx*dx+dz*dz);if(length<.0001f)return;
            var targets=s.enemies.Where(e=>e.Alive&&e.id!=primary.id&&Distance(e,actor)<=range&&V12BuildLine(actor.id,e.id)&&
                (Distance(e,actor)<.0001f||((e.x-actor.x)*dx+(e.z-actor.z)*dz)/(Distance(e,actor)*length)>=Math.Cos(c.sweepAngle*Math.PI/360))).OrderBy(e=>Distance(e,actor)).ThenBy(e=>e.id,StringComparer.Ordinal).Take(c.sweepTargets).ToList();
            foreach(var extra in targets)V12Damage(s,actor,extra,root+"/sweep","v12_mira_sweep","splash",V12SecondaryBase(s,raw,up?c.sweepUpgradeMultiplier:c.sweepMultiplier),false,result,null,null,root);
        }
        void V12OnKilled(CombatState s,ActorState source,ActorState victim,string root,string action,int actual,string paidRoot=null)
        {
            if(!V12BuildEnabled||victim?.isEnemy!=true||victim.Alive||actual<=0)return;
            if(action=="v12_mira_break"&&source?.Alive==true&&V12Form("v12_mira_hunt",source.id))
            {string id="v12/hunt/"+root;if(!s.rewardIds.Contains(id)){s.rewardIds.Add(id);source.v12.basicCooldown=Down(source.v12.basicCooldown,Config.v12.build.huntCooldownReduction);}}
            if(paidRoot!=null&&V12HasBuff(Config,"v12_team_edge"))
            {string id="v12/edge/"+s.encounterId;if(!s.rewardIds.Contains(id)){s.rewardIds.Add(id);P6AwardResource(s,paidRoot,"v12-edge",Config.v12.build.edgeMomentum);}}
            var finn=s.Actor(V12Finn);if(finn?.Alive!=true||!V12Form("v12_finn_spread",finn.id))return;
            var status=victim.statuses.Find(x=>x.sourceId==finn.id&&x.v12?.family=="parasite"&&x.v12.generation==0&&x.v12.expiresAt>s.simulationSeconds);
            if(status==null)return;string spreadRoot="v12/spread/"+status.v12.rootId+"/"+victim.id;
            if(s.rewardIds.Contains(spreadRoot))return;s.rewardIds.Add(spreadRoot);
            var c=Config.v12.build;float range=V12FormUp("v12_finn_spread",finn.id)?c.spreadUpgradeRange:c.spreadRange;
            foreach(var target in s.enemies.Where(e=>e.Alive&&Distance(e,victim)<=range&&V12BuildLine(victim.id,e.id)&&!e.statuses.Any(x=>x.sourceId==finn.id&&x.v12?.family=="parasite"&&x.v12.expiresAt>s.simulationSeconds)).OrderBy(e=>Distance(e,victim)).ThenBy(e=>e.id,StringComparer.Ordinal).Take(c.spreadTargets).ToList())
            {
                target.statuses.RemoveAll(x=>x.sourceId==finn.id&&x.v12?.family=="parasite");
                target.statuses.Add(new StatusState{id="v12-parasite",sourceId=finn.id,remaining=status.v12.expiresAt-s.simulationSeconds,
                    v12=new V12StatusData{family="parasite",rootId=status.v12.rootId,expiresAt=status.v12.expiresAt,nextTick=status.v12.nextTick,tickIndex=status.v12.tickIndex,generation=1}});
                V12Emit(s,null,spreadRoot,finn.id,target.id,"v12_finn_spread","parasite",0);
            }
        }
        void V12Bloom(CombatState s,ActorState finn,V12Grove grove,CombatResult result)
        {
            if(!V12Form("v12_finn_bloom",finn.id))return;
            // Called before Grove tags new entrants: only existing parasites get the extra tick.
            bool effective=false;
            var targets=s.enemies.Where(e=>e.Alive&&DistanceTo(e,grove.x,grove.z)<=grove.radius&&V12BuildLine(finn.id,e.id)&&e.statuses.Any(x=>x.sourceId==finn.id&&x.v12?.family=="parasite"&&x.v12.expiresAt>s.simulationSeconds)).OrderBy(e=>e.id,StringComparer.Ordinal).ToList();
            foreach(var target in targets)
            {
                var parasite=target.statuses.Find(x=>x.sourceId==finn.id&&x.v12?.family=="parasite").v12;
                effective|=V12Damage(s,finn,target,grove.id+"/bloom","v12_finn_bloom","bloom",Config.BasicFor(finn).damage*Config.v12.parasiteCoefficient,false,result,parasite,null,grove.id)>0;
            }
            if(!effective||!V12FormUp("v12_finn_bloom",finn.id))return;
            var ally=s.actors.Where(a=>a.Alive&&V13Physical(s,a)&&a.hp<a.maxHp&&DistanceTo(a,grove.x,grove.z)<=grove.radius).OrderBy(a=>(double)a.hp/a.maxHp).ThenBy(a=>a.id,StringComparer.Ordinal).FirstOrDefault();
            if(ally==null)return;int healed=V12HealExisting(s,Config,ally,ally.maxHp*Config.v12.build.bloomHealFraction);
            if(healed>0)V12Emit(s,result,grove.id+"/bloom-heal",finn.id,ally.id,"v12_finn_bloom_up","heal",healed);
        }
    }
}
