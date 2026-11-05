using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        // Legacy radiant marks share the UI family, but their advantage remains Smite-only.
        static bool V12HasOpening(ActorState target)=>target?.statuses.Any(x=>x.remaining>0&&x.v12?.family=="opening")==true;
        bool V12OpeningActive(StatusState status,float? atSimulationTime)
        {return status.v12?.family=="opening"&&(atSimulationTime.HasValue?status.v12.expiresAt+.00001f>=atSimulationTime.Value&&status.v12.expiresAt-Config.v12.markSeconds<=atSimulationTime.Value+.00001f:status.remaining>0);}
        float V12Vulnerability(ActorState target,float existing=1,float? atSimulationTime=null)
        {if(!V12Enabled)return existing;foreach(var x in target.statuses)if(V12OpeningActive(x,atSimulationTime))existing=Math.Max(existing,x.v12.multiplier);return existing;}
        int V12RollBase(CombatState s,ActorState source,float coefficient,bool critical=false)
        {
            int basic=Config.BasicFor(source).damage;if(basic<=0)return 0;
            int rolled=0;for(int i=0;i<(critical?2:1);i++)rolled+=RandomStreams.NextInt(s.p6.damageRandom,1,Math.Max(2,basic*2));
            double scaled=rolled*coefficient;int whole=(int)Math.Floor(scaled);double fraction=scaled-whole;
            return whole+(fraction>0&&RandomStreams.NextInt(s.p6.damageRandom,1,10001)<=fraction*10000?1:0);
        }
        void V12Emit(CombatState s,CombatResult result,string root,string source,string target,string action,string kind,int amount)
        {
            string id=root+"/"+kind+"/"+target;if(s.v12.events.Any(e=>e.id==id))return;
            var item=new V12Effect{id=id,rootId=root,sourceId=source,targetId=target,actionId=action,kind=kind,amount=amount,simulationTime=s.simulationSeconds};s.v12.events.Add(item);
            if(result==null)return;if(result.v12==null)result.v12=new V12ActionResult();result.v12.effects.Add(item);
        }
        void V12CollectResult(CombatState s,CombatResult result,int from)
        {
            if(!V12Enabled)return;for(int i=from;i<s.v12.events.Count;i++)
            {var e=s.v12.events[i];if(result.v12==null)result.v12=new V12ActionResult();if(!result.v12.effects.Any(x=>x.id==e.id))result.v12.effects.Add(e);}
        }
        int V12Mitigate(ActorState target,int amount)=>V12Mitigate(target,(double)amount,null);
        int V12Mitigate(ActorState target,double amount,V12StatusData fractional,float? atSimulationTime=null)
        {
            if(!V12Enabled)return V11Absorb(target,(int)amount);
            // A small hit must eventually realise the opening's 20%, rather than losing it
            // to integer HP rounding on every contact. This carry belongs to this source's
            // current window. It vanishes with the status; immunity/full absorption earns none.
            var opening=target.statuses.Where(x=>V12OpeningActive(x,atSimulationTime)).OrderByDescending(x=>x.v12.multiplier).FirstOrDefault();
            var carry=fractional??opening?.v12;double resistance=target.p6?.physicalDamageMultiplier??1;
            if(amount<=0||resistance<=0)return 0;
            double exact=amount*V12Vulnerability(target,1,atSimulationTime)*resistance+(carry?.damageRemainder??0);int whole=Math.Max(0,(int)Math.Floor(exact+1e-6));
            int availableShield=target.p6?.absorptionShield??0,shield=Math.Min(whole,availableShield);
            if(shield>0){target.p6.absorptionShield-=shield;V13ConsumeShield(target,shield);}
            int actual=Math.Min(target.hp,whole-shield);
            if(carry!=null&&(actual>0||whole==0&&availableShield==0))carry.damageRemainder=Math.Max(0,exact-whole);
            return actual;
        }
        int V12Damage(CombatState s,ActorState source,ActorState target,string root,string action,string kind,double amount,bool direct,CombatResult result,V12StatusData fractional=null,float? atSimulationTime=null,string paidRoot=null,bool sourceSnapshot=false)
        {
            if(target?.Alive!=true||amount<=0||!V13Physical(s,target))return 0;
            string id=root+"/"+kind+"/"+target.id;if(s.v12.events.Any(e=>e.id==id))return 0;
            bool worldFixed=kind=="environment";double scaled=V14Enabled&&!sourceSnapshot&&!worldFixed?V14SourceDamage(source,amount,kind=="dot"||kind=="bloom"):amount;
            int damage=V14Enabled&&!worldFixed?V14Mitigate(target,source,scaled,kind=="dot"||kind=="detonate"?"periodic":"physical",atSimulationTime):V12Mitigate(target,amount,fractional,atSimulationTime);target.hp-=damage;V14LogDamage(s,source,target,root,action,kind,amount,scaled,damage,kind=="dot"||kind=="detonate"||kind=="bloom",sourceSnapshot,null,atSimulationTime,worldFixed);V12Emit(s,result,root,source.id,target.id,action,kind,damage);
            if(damage>0){paidRoot=paidRoot??(action=="v12_evan_final"||action=="v12_mira_break"||direct&&V11AbilityCatalog.IsCustom(action)?root:null);if(kind!="detonate")V12OnKilled(s,source,target,root,action,damage,paidRoot);BreakConcentrationFromDamage(s,target);AwardFocusBossPhaseIfReached(s,target,paidRoot??root);AwardP4ActionOutcome(s,target,paidRoot??root,paidRoot,false,!target.Alive);if(direct)V12OnDirectDamage(s,source,target,root,action,damage);}
            return damage;
        }
        void V12OnDirectDamage(CombatState s,ActorState source,ActorState target,string root,string action,int actualHp)
        {
            if(!V12Enabled||source==null||source.isEnemy||target?.isEnemy!=true||!target.Alive||actualHp<=0)return;
            var evan=s.Actor(V12Evan);if(evan==null||source.id==evan.id||!evan.Alive||(!V12Passive(evan)&&!V12Form("v12_evan_coauthor",evan.id))||evan.staggerRemaining>0||evan.windupRemaining>0||P6BindActive(evan)||V12ActionActive(evan)||!string.IsNullOrEmpty(evan.heldBy)||evan.v12.echoCooldown>0)return;
            if(!target.statuses.Any(x=>x.v12?.family=="opening"&&x.sourceId==evan.id&&x.remaining>0))return;
            string echoRoot=root+"/annotation";if(s.v12.events.Any(x=>x.rootId==echoRoot))return;
            if(V13Enabled){if(action=="direct-attack"||action==Config.BasicFor(source).id)return;var mark=target.statuses.Find(x=>x.v12?.family=="opening"&&x.sourceId==evan.id&&x.remaining>0);if(mark==null||s.v13.receipts.Contains("annotation/"+mark.v12.rootId))return;s.v13.receipts.Add("annotation/"+mark.v12.rootId);}
            evan.v12.echoCooldown=V13Enabled?Config.v13.echoInterval:Config.v12.echoInterval;int raw=V12RollBase(s,evan,Config.v12.echoCoefficient);int actual=V12Damage(s,evan,target,echoRoot,"v12_evan_annotation","echo",raw,false,null);V12Coauthor(s,evan,target,echoRoot,raw,actual);
        }
        void V12ApplyMark(CombatState s,ActorState a,ActorState target,string root,CombatResult result)
        {
            foreach(var e in s.enemies)e.statuses.RemoveAll(x=>x.v12?.family=="opening"&&x.sourceId==a.id);
            target.statuses.Add(new StatusState{id="v12-opening",sourceId=a.id,remaining=Config.v12.markSeconds,v12=new V12StatusData{family="opening",rootId=root,expiresAt=s.simulationSeconds+Config.v12.markSeconds,multiplier=V13Enabled&&V12Form("v12_evan_conclusion",a.id)?(V12FormUp("v12_evan_conclusion",a.id)?1.30f:1.25f):Config.v12.markMultiplier}});
            V12Emit(s,result,root,a.id,target.id,"v12_evan_mark","opening",0);
        }
        void V12ApplyParasite(CombatState s,ActorState a,ActorState target,string root,float seconds,int generation,CombatResult result)
        {
            var status=target.statuses.Find(x=>x.v12?.family=="parasite"&&x.sourceId==a.id);
            if(V14Enabled&&status?.v14Dot!=null)V14ResolveDueParasite(s,target,status,result??new CombatResult());
            if(status==null)
            {status=new StatusState{id="v12-parasite",sourceId=a.id,v12=new V12StatusData{family="parasite",rootId=root,nextTick=s.simulationSeconds+Config.v12.parasiteInterval,generation=generation}};target.statuses.Add(status);}
            status.remaining=seconds;status.v12.expiresAt=s.simulationSeconds+seconds;V14SnapshotParasite(s,a,target,status,root);
            V12Emit(s,result,root,a.id,target.id,"v12_finn_seed","parasite",0);
        }
        void V12UpdateGroves(CombatState s,CombatResult result)
        {
            foreach(var e in s.enemies)e.statuses.RemoveAll(x=>x.v12?.family=="entangle"&&!s.v12.groves.Any(g=>g.sourceId==x.sourceId&&g.expiresAt>s.simulationSeconds&&s.Actor(g.sourceId)?.Alive==true&&DistanceTo(e,g.x,g.z)<=g.radius));
            foreach(var g in s.v12.groves)
            {
                var source=s.Actor(g.sourceId);if(source?.Alive!=true||g.expiresAt<=s.simulationSeconds)continue;
                foreach(var target in s.enemies.Where(e=>e.Alive&&DistanceTo(e,g.x,g.z)<=g.radius).OrderBy(e=>e.id,StringComparer.Ordinal))
                {
                    if(!g.touchedEnemies.Contains(target.id)){if(g.touchedEnemies.Count>=Config.v12.groveMaximumEnemies)continue;g.touchedEnemies.Add(target.id);V12ApplyParasite(s,source,target,g.id+"/enter/"+target.id,Config.v12.parasiteSeconds,0,result);}
                    var x=target.statuses.Find(t=>t.v12?.family=="entangle"&&t.sourceId==g.sourceId);if(x==null){x=new StatusState{id="v12-entangle",sourceId=g.sourceId,v12=new V12StatusData{family="entangle",rootId=g.id}};target.statuses.Add(x);}
                    bool boss=s.world.strings.TryGetValue("enemyTemplate/"+target.id,out var template)&&template=="boss";x.remaining=g.expiresAt-s.simulationSeconds;x.v12.expiresAt=g.expiresAt;x.v12.multiplier=boss?Config.v12.bossEntangleMultiplier:Config.v12.entangleMultiplier;
                }
            }
        }
        CombatResult V12ResolveTicks(CombatState s)
        {
            var result=new CombatResult{damageApplied=true};V12CleanupSources(s);V12UpdateGroves(s,result);float now=s.simulationSeconds;
            foreach(var target in s.enemies)
            {
                foreach(var status in target.statuses.Where(x=>x.v12?.family=="parasite").ToList())
                {
                    if(V14Enabled&&status.v14Dot!=null){V14ResolveDueParasite(s,target,status,result);continue;}
                    var p=status.v12;var source=s.Actor(status.sourceId);
                    while(target.Alive&&source!=null&&p.nextTick<=now+.00001f&&p.nextTick<=p.expiresAt+.00001f)
                    {
                        p.tickIndex++;string tickRoot=p.rootId+"/tick/"+p.tickIndex;float tickTime=p.nextTick;p.nextTick+=Config.v12.parasiteInterval;
                        // Do not quantise the base tick before applying the *current* window.
                        // The source's fractional HP is settled once; no vulnerability snapshot.
                        result.damage+=V12Damage(s,source,target,tickRoot,"v12_finn_seed","dot",Config.BasicFor(source).damage*Config.v12.parasiteCoefficient,false,result,p,tickTime);
                    }
                }
                target.statuses.RemoveAll(x=>x.v12!=null&&(x.v12.expiresAt<=now+.00001f||!target.Alive));
            }
            foreach(var g in s.v12.groves)
            {
                var source=s.Actor(g.sourceId);if(source?.Alive!=true)continue;
                while(g.nextTick<=now+.00001f&&g.nextTick<=g.expiresAt+.00001f)
                {
                    g.tickIndex++;g.nextTick++;foreach(var ally in s.actors.Where(a=>a.Alive&&V13Physical(s,a)&&DistanceTo(a,g.x,g.z)<=g.radius))
                    {
                        var v=ally.v12;if(ally.hp>=ally.maxHp){v.groveHealingRemainder=0;continue;}
                        double nominal=ally.maxHp*Config.v12.groveHealFraction*V12HealingScale(Config);int whole;
                        if(V12BuildEnabled)whole=V12AccumulateHealing(ref v.groveHealingRemainder,nominal);
                        else{double amount=v.groveHealingRemainder+nominal;whole=(int)Math.Floor(amount+1e-7);v.groveHealingRemainder=Math.Max(0,amount-whole);}
                        int heal=Math.Min(whole,ally.maxHp-ally.hp);ally.hp+=heal;if(ally.hp==ally.maxHp)v.groveHealingRemainder=0;
                        if(heal>0)V12Emit(s,result,g.id+"/heal/"+g.tickIndex,source.id,ally.id,"v12_finn_grove","heal",heal);
                    }
                }
            }
            s.v12.groves.RemoveAll(g=>g.expiresAt<=now+.00001f||s.Actor(g.sourceId)?.Alive!=true);V12UpdateGroves(s,result);return result;
        }
        void V12ObserveDodge(CombatState s,PendingIncoming e)
        {
            V14ObserveMiraDodge(s,e);
            if(V13Enabled&&e.defenderId==s.v13.coreId&&e.reason=="avoided_by_dodge"&&s.momentumLedger.registeredDangerousThreats.Contains(e.rootId)){var m=Member(s,V12Mira);if(m!=null&&m.actorId!=s.v13.coreId&&m.lastAvoidedRoot!=e.rootId){m.lastAvoidedRoot=e.rootId;m.assistRewardUntil=s.simulationSeconds+4;}}
            if(!V12Enabled||e.defenderId!=V12Mira||e.reason!="avoided_by_dodge"||e.baseDamage<=0||!s.momentumLedger.registeredDangerousThreats.Contains(e.rootId))return;
            var a=s.Actor(V12Mira);if(!V12Passive(a)||a.v12.lastDodgeRoot==e.rootId)return;a.v12.lastDodgeRoot=e.rootId;a.v12.readinessRemaining=Config.v12.readinessSeconds;
        }
        int V12BasicPreparedDamage(CombatState s,ActorState source,int amount)
        {if(V14Enabled)return amount;return V12Enabled&&source.id==V12Mira&&V12Passive(source)&&source.v12.readinessRemaining>0?(int)Math.Floor(amount*Config.v12.readinessMultiplier):amount;}
        void V12ConsumePreparedBasic(CombatState s,ActorState source,ActorState target,BasicAttackIntent intent,int actual)
        {V14MiraConsume(s,source,target,intent.rootId,intent.actionId,actual,intent.v14PlayerCommand);if(V12Enabled&&source.id==V12Mira&&intent.actionId==Config.BasicFor(source).id&&actual>0&&(!V14Enabled||intent.v14PlayerCommand))source.v12.readinessRemaining=0;}
    }
}
