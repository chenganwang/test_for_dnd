using System;
using System.Collections.Generic;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        CombatResult P6StartBasic(CombatState s,CombatCommand c)
        {
            Require(s.inCombat&&SimulationMode(s)&&(IsAI(c)||NormalMode(s)),"basic_attack_not_allowed");var a=Live(s,c.actorId);var target=Live(s,c.targetId);
            Require(a.role==ActorRole.Paladin&&!a.isEnemy&&target.isEnemy,"invalid_party_actor");MajorFree(a);Require(!a.aimPreparing&&a.guardRemaining<=0&&a.dodgeRemaining<=0,"actor_busy");Require(P6Line(c,target.id)&&Distance(a,target)<=Config.p6.basicRange+.001f,"target_out_of_range");
            float dx=c.p6?.directionX??0,dz=c.p6?.directionZ??0;if(dx*dx+dz*dz<.0001f){dx=target.x-a.x;dz=target.z-a.z;}float length=(float)Math.Sqrt(dx*dx+dz*dz);
            a.basicAttackIntent=new BasicAttackIntent{id=c.id,targetId=target.id,actionId="basic_greatsword",rootId=c.id,remaining=Config.p6.basicWindup,p6=new P6BasicContext{directionX=length<.001f?0:dx/length,directionZ=length<.001f?1:dz/length}};
            a.windupRemaining=Config.p6.basicWindup;a.p6.basicRecovery=false;return new CombatResult{basicAttackId=c.id};
        }
        CombatResult P6ResolveBasic(CombatState s,CombatCommand c)
        {
            Require(SimulationMode(s)&&c.authority==CommandAuthority.System,"system_command_required");var a=Live(s,c.actorId);var intent=a.basicAttackIntent;
            Require(intent?.p6!=null&&!intent.resolved&&intent.id==c.orderId&&intent.remaining<=0,"basic_contact_not_ready");intent.resolved=true;a.windupRemaining=0;a.majorRemaining=Math.Max(0,Config.p6.basicInterval-Config.p6.basicWindup);a.p6.basicRecovery=true;
            var result=new CombatResult{basicAttackId=intent.id,p6=new P6ActionResult(),damageApplied=true};
            foreach(var target in s.enemies.Where(x=>x.Alive))
            {
                if(c.p6?.targets==null&&target.id!=intent.targetId)continue;var evidence=c.p6?.targets?.Find(x=>x.targetId==target.id);
                if(!P6Line(c,target.id)||evidence!=null&&!evidence.inArea||!P6InBasicCone(a,target,intent.p6)||intent.p6.resolvedTargetIds.Contains(target.id))continue;
                intent.p6.resolvedTargetIds.Add(target.id);var faces=new List<int>{RandomStreams.D20(s.random.backgroundCombat)};
                bool advantage=target.staggerRemaining>0,disadvantage=a.statuses.Any(x=>x.id=="attack-disadvantage"&&x.remaining>0);int raw=faces[0];
                if(advantage!=disadvantage){int second=RandomStreams.D20(s.random.backgroundCombat);faces.Add(second);raw=advantage?Math.Max(raw,second):Math.Min(raw,second);}
                int total=raw+a.attackBonus+(V13Enabled&&s.v13.activeRule==3&&s.v13.ruleUntil>s.simulationSeconds?-1:0),ac=EffectiveArmorClass(target);bool hit=AttackHits(raw,total,ac),critical=hit&&raw==20;
                var damage=P6ApplyDamage(s,a,target,intent.rootId,"basic_greatsword",faces,raw,total,ac,hit,critical,critical?"自然20":null,0,null,false);result.p6.damages.Add(damage);result.damage+=damage.totalDamage;result.hit|=hit;result.critical|=critical;
                if(target.id==intent.targetId){result.rawFaces=faces;result.total=total;}
            }
            if(result.p6.damages.Count==0)result.reason="basic_swing_missed";return result;
        }
        bool P6InBasicCone(ActorState a,ActorState target,P6BasicContext context)
        {
            float x=target.x-a.x,z=target.z-a.z,length=(float)Math.Sqrt(x*x+z*z);return length<=Config.p6.basicRange+.001f&&(length<.001f||(x*context.directionX+z*context.directionZ)/length>=Math.Cos(Config.p6.basicAngle*Math.PI/360)-.0001);
        }
        P6DamageResult P6ApplyDamage(CombatState s,ActorState a,ActorState target,string root,string actionId,List<int> rawFaces,int raw,int total,int ac,bool hit,bool critical,string criticalSource,int radiantDice,StatusState mark,bool overcharged)
        {
            string id=root+"/damage/"+target.id;Require(!s.p6.damageResults.Any(x=>x.id==id),"p6_damage_already_applied");
            var record=new P6DamageResult{id=id,rootId=root,actorId=a.id,targetId=target.id,actionId=actionId,attackFaces=new List<int>(rawFaces),selectedRaw=raw,total=total,targetAc=ac,hit=hit,critical=critical,criticalSource=criticalSource,overcharged=overcharged,strengthBonus=a.strengthModifier,markId=mark?.p6.id,radiantMultiplier=mark?.p6.multiplier??1};
            if(hit)
            {
                int multiplier=critical?2:1;for(int i=0;i<Config.p6.weaponDice*multiplier;i++)record.weaponFaces.Add(RandomStreams.NextInt(s.p6.damageRandom,1,Config.p6.weaponSides+1));for(int i=0;i<radiantDice*multiplier;i++)record.radiantFaces.Add(RandomStreams.NextInt(s.p6.damageRandom,1,Config.p6.radiantSides+1));
                record.radiantMultiplier=V12Vulnerability(target,record.radiantMultiplier);record.physicalBeforeMitigation=(int)Math.Floor(Math.Max(0,record.weaponFaces.Sum()+record.strengthBonus)*V12Vulnerability(target));record.radiantBeforeMitigation=(int)Math.Floor(record.radiantFaces.Sum()*record.radiantMultiplier);
                double physicalSource=V14SourceDamage(a,Math.Max(0,record.weaponFaces.Sum()+record.strengthBonus)),radiantSource=V14SourceDamage(a,record.radiantFaces.Sum());
                int physical=V14Enabled?V14Quantize(target,a.id+"/physical",physicalSource*V12Vulnerability(target)*target.p6.physicalDamageMultiplier):(int)Math.Floor(record.physicalBeforeMitigation*target.p6.physicalDamageMultiplier),radiant=V14Enabled?V14Quantize(target,a.id+"/radiant",radiantSource*record.radiantMultiplier*target.p6.radiantDamageMultiplier):(int)Math.Floor(record.radiantBeforeMitigation*target.p6.radiantDamageMultiplier);
                if(V14Enabled){record.physicalBeforeMitigation=(int)Math.Floor(physicalSource*V12Vulnerability(target));record.radiantBeforeMitigation=(int)Math.Floor(radiantSource*record.radiantMultiplier);}
                int block=Math.Min(target.p6.absorptionShield,physical);physical-=block;target.p6.absorptionShield-=block;record.absorbed=block;block=Math.Min(target.p6.absorptionShield,radiant);radiant-=block;target.p6.absorptionShield-=block;record.absorbed+=block;if(V13Enabled)V13ConsumeShield(target,record.absorbed);
                // Applied components are actual HP loss: a physical lethal blow cannot
                // manufacture positive Radiant application from overkill.
                record.physicalApplied=Math.Min(target.hp,physical);record.radiantApplied=Math.Min(target.hp-record.physicalApplied,radiant);record.totalDamage=record.physicalApplied+record.radiantApplied;target.hp-=record.totalDamage;V14LogDamage(s,a,target,root,actionId,"physical",Math.Max(0,record.weaponFaces.Sum()+record.strengthBonus),physicalSource,record.physicalApplied,false,false,target.p6.physicalDamageMultiplier);V14LogDamage(s,a,target,root,actionId,"radiant",record.radiantFaces.Sum(),radiantSource,record.radiantApplied,false,false,target.p6.radiantDamageMultiplier);
                if(record.totalDamage>0){target.aimPreparing=false;target.aimRemaining=0;target.aimReady=false;BreakConcentrationFromDamage(s,target);AwardFocusBossPhaseIfReached(s,target,root);}
                if(mark!=null&&record.radiantApplied>0&&actionId==P6SmiteId){target.statuses.Remove(mark);record.markConsumed=true;P6AwardMark(s,root);}
            }
            V14CorrectionCommitted(s,s.rolls.Find(t=>t.id==root),record.totalDamage>0);V12OnKilled(s,a,target,root,actionId,record.totalDamage,actionId==P6SmiteId?root:null);V12OnDirectDamage(s,a,target,root,actionId,record.totalDamage);V11AfterSmite(s,a,target,record);s.p6.damageResults.Add(record);P6CleanupMarks(s);return record;
        }
    }
}
