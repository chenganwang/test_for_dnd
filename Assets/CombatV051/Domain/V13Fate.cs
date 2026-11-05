using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        static readonly int[] V13FateEnds={8,16,24,34,44,54,64,74,82,90,96,100};
        static readonly string[] V13FateNames={"滞涩之潮","逆命清算","天平回声","浮石脊","命运护幕","返潮","敌阵裂纹","宁息之地","灵启","潮汐偿还","裁雷","黎明回流"};
        static readonly string[] V13FateDetails={"在场敌我移速×0.8 · 8秒","战机−25，专注−6秒 · 不扣次数","敌我攻击总值−1 · 8秒；不改原始面","安全锚点低掩体 · 预警1.2秒，持续12秒；无合法位置改为敌我小盾","在场敌我获得4%生命护盾，上限8 · 8秒","战机+25，专注+6秒 · 受容量与恢复预算限制","最多4敌人破绽6秒 · 不赠优势","可见治疗地面 · 8秒，每秒回1%；无合法位置核心回4%","三个逻辑成员各一次非普攻攻击/豁免+2 · 12秒","返还一份实际花掉的祈愿或最低环法术位；没有则战机+20","至多3个固定落点 · 预警1.2秒后12雷伤，可走开","存活实体友军解缠绕、回8%生命；战机+20"};
        public static int FateIndex(int raw){if(raw<1||raw>100)throw new ArgumentOutOfRangeException(nameof(raw));for(int i=0;i<V13FateEnds.Length;i++)if(raw<=V13FateEnds[i])return i+1;return 12;}
        public static string FateName(int raw)=>V13FateNames[FateIndex(raw)-1];
        public static string FateDetail(int raw)=>V13FateDetails[FateIndex(raw)-1];
        public static int FateEchoValue(int raw){if(raw<1||raw>20)throw new ArgumentOutOfRangeException(nameof(raw));return raw*5;}
        public static void V13EnterEncounter(CombatState s,CombatConfig c,string room,float arenaX=0,float arenaZ=0)
        {
            if(c.v13==null)return;InitializeV13State(s,c);var v=s.v13;
            v.boss=null;v.terrain.Clear();v.fate=null;v.activeRule=0;v.ruleUntil=0;
            if((room=="V12_R4"||room=="V12_R6")&&!v.entryTriggers.Contains(room)&&v.fateCount<3)
            {v.entryTriggers.Add(room);v.fate=new V13FateEvent{id=s.modeInstanceId+"/fate/"+room,encounterId=s.encounterId,trigger=room,warning=c.v13.fateWarning};}
            if(room=="V12_R6")
            {var boss=s.enemies.FirstOrDefault(e=>s.world.strings.TryGetValue("enemyTemplate/"+e.id,out var name)&&name=="boss");if(boss!=null){v.boss=new V13Boss{actorId=boss.id,arenaX=arenaX,arenaZ=arenaZ};boss.breakMaximum=boss.breakValue=6;}}
        }
        public static void V13ClearEncounter(CombatState s)
        {
            if(s.v13==null)return;
            if(s.v13.fate!=null&&s.v13.fate.stage!="Scheduled"){s.v13.fate.stage="Archived";s.v13.history.Add(s.v13.fate);}
            s.v13.fate=null;s.v13.terrain.Clear();s.v13.boss=null;s.v13.activeRule=0;s.v13.ruleUntil=0;
            foreach(var a in s.actors.Concat(s.enemies)){a.p6.absorptionShield-=a.p6.v13FateShield;a.p6.v13FateShield=0;a.statuses.RemoveAll(x=>x.id.StartsWith("v13-",StringComparison.Ordinal));}
        }
        bool TryV13FateCommand(CombatState s,CombatCommand c,out CombatResult r)
        {
            r=null;var v=s.v13;var f=v.fate;
            if(c.kind==CommandKind.V13FateStart)
            {
                Require(c.authority==CommandAuthority.System&&c.qualified&&NormalMode(s)&&s.activePendingEvent==null&&s.pendingRoll==null,"fate_safe_window_required");
                Require(s.inCombat&&V13PartyAlive(s)&&s.enemies.Any(a=>a.Alive)&&f?.stage=="Scheduled"&&f.warning<=0&&v.fateCount<3,"fate_not_ready");
                Require(!s.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing")&&!s.actors.Any(a=>a.windupRemaining>0||V12ActionActive(a)||P6BindActive(a)||a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved)&&!s.enemies.Any(a=>a.Alive&&a.aiState==1)&&!s.incomingEvents.Any(e=>!e.damageApplied)&&!s.world.projectiles.Any(p=>!p.resolved),"fate_threat_in_flight");
                v.fateCount++;if(f.trigger=="time")v.timeUsed=true;f.rngIndex=v.fateRng.drawCount;f.original=f.final=RandomStreams.NextInt(v.fateRng,1,101);f.echoId=v.echo?.id;f.echoRaw=v.echo?.raw??0;f.stage="Rolled";f.decision=Config.v13.fateDecision;
                v.enemyRestUntil=s.simulationSeconds+Config.v13.fateRecovery;foreach(var e in s.enemies.Where(e=>e.Alive)){e.aiState=2;e.aiRemaining=Math.Max(e.aiRemaining,Config.v13.fateRecovery);}if(v.boss!=null)v.boss.remaining=Math.Max(v.boss.remaining,Config.v13.fateRecovery);
                r=new CombatResult{total=f.original};return true;
            }
            if(c.kind==CommandKind.V13FateReveal){Require(f?.stage=="Rolled"&&c.authority==CommandAuthority.System,"fate_not_rolling");f.stage="Deciding";r=new CombatResult{total=f.original};return true;}
            if(c.kind==CommandKind.V13FateAccept||c.kind==CommandKind.V13FateReplace)
            {
                Require(f?.stage=="Deciding","fate_not_deciding");
                if(c.kind==CommandKind.V13FateReplace)
                {
                    var core=Live(s,v.coreId);Require(f.echoId!=null&&v.echo?.id==f.echoId&&core.inspiration>0&&f.paymentId==null,"fate_echo_or_inspiration_unavailable");
                    core.inspiration--;f.paymentId=c.id;f.final=FateEchoValue(f.echoRaw);v.echo=null;
                }
                V13ApplyFate(s,c);f.stage="Applied";v.combatSinceFate=0;r=new CombatResult{total=f.final};return true;
            }
            if(c.kind==CommandKind.V13FateEffects)
            {
                Require(c.authority==CommandAuthority.System&&SimulationMode(s)&&!V13FateBusy(s),"system_command_required");r=new CombatResult();
                foreach(var t in v.terrain.ToList())
                {
                    if(t.warning>0)continue;
                    if(t.kind=="lightning")
                    {foreach(var e in s.enemies.Where(e=>e.Alive&&DistanceTo(e,t.x,t.z)<=1.1f)){int damage=Math.Min(e.hp,12);e.hp-=damage;r.damage+=damage;}v.receipts.Add(t.id);v.terrain.Remove(t);continue;}
                    if(t.kind=="heal"&&t.nextTick<=s.simulationSeconds&&t.remaining>0)
                    {
                        t.nextTick=s.simulationSeconds+1;
                        if(V14Trio(s))foreach(var a in s.actors.Where(a=>a.Alive&&V13Physical(s,a)&&DistanceTo(a,t.x,t.z)<=2.5f))V13Heal(a,a.maxHp*.01);
                        else {var a=s.Actor(v.coreId);if(a.Alive&&DistanceTo(a,t.x,t.z)<=2.5f)V13Heal(a,a.maxHp*.01);}
                    }
                    if(t.remaining<=0)v.terrain.Remove(t);
                }
                if(f?.stage=="Applied"){f.stage="Archived";v.history.Add(f);v.fate=null;}return true;
            }
            return false;
        }
        void V13Heal(ActorState a,double amount)
        {if(!a.Alive||!V13Physical(State,a))return;if(a.hp>=a.maxHp){a.v12.groveHealingRemainder=0;return;}a.v12.groveHealingRemainder+=amount;int value=(int)Math.Floor(a.v12.groveHealingRemainder+1e-7);a.v12.groveHealingRemainder-=value;a.hp=Math.Min(a.maxHp,a.hp+value);}
        void V13FateShield(CombatState s,ActorState a)
        {int amount=Math.Min(8,Math.Max(1,(int)Math.Floor(a.maxHp*.04)));a.p6.absorptionShield+=amount-a.p6.v13FateShield;a.p6.v13FateShield=amount;a.p6.v13FateShieldUntil=s.simulationSeconds+8;}

        void V13ApplyFate(CombatState s,CombatCommand c)
        {
            var v=s.v13;var f=v.fate;Require(f.effectId==null,"fate_already_applied");f.effectId=f.id+"/effect";int index=FateIndex(f.final);var core=s.Actor(v.coreId);
            f.targets=s.actors.Concat(s.enemies).Where(a=>a.Alive&&V13Physical(s,a)).Select(a=>a.id).ToList();v.activeRule=index;v.ruleUntil=s.simulationSeconds+8;
            switch(index)
            {
                case 1:break;
                case 2:s.momentum=Math.Max(0,s.momentum-25);s.focusBudget.secondsRemaining=Math.Max(0,s.focusBudget.secondsRemaining-6);break;
                case 3:break;
                case 4:
                    if(c.qualified)v.terrain.Add(new V13Terrain{id=f.effectId,kind="cover",x=c.x,z=c.z,warning=1.2f,remaining=12});
                    else {f.fallback="无安全锚点：敌我小盾";foreach(var id in f.targets)V13FateShield(s,s.Actor(id));}break;
                case 5:foreach(var id in f.targets)V13FateShield(s,s.Actor(id));break;
                case 6:P6AwardResource(s,f.id,"fate-return",25);AwardP6FocusRecovery(s,f.id,"echo");break;
                case 7:
                    foreach(var e in s.enemies.Where(e=>e.Alive).OrderByDescending(e=>e.id==v.boss?.actorId).ThenBy(e=>e.id,StringComparer.Ordinal).Take(4))
                        e.statuses.Add(new StatusState{id="v13-fate-opening",sourceId=v.coreId,stacks=0,remaining=6,v12=new V12StatusData{family="opening",rootId=f.id,expiresAt=s.simulationSeconds+6,multiplier=1.2f}});break;
                case 8:
                    if(c.qualified)v.terrain.Add(new V13Terrain{id=f.effectId,kind="heal",x=c.x,z=c.z,remaining=8,nextTick=s.simulationSeconds+1});
                    else {f.fallback="无安全落点：核心回复4%";V13Heal(core,core.maxHp*.04);}break;
                case 9:foreach(var a in s.actors){a.statuses.RemoveAll(x=>x.id=="v13-inspiration");a.statuses.Add(new StatusState{id="v13-inspiration",sourceId=v.coreId,remaining=12});}break;
                case 10:
                    bool refunded=false;
                    foreach(var m in v.members){var spend=m.prayerSpent.FirstOrDefault(x=>!m.refunded.Contains(x));if(spend==null||m.prayers>=Config.v13.prayerCapacity)continue;m.refunded.Add(spend);m.prayers++;f.paymentId="refund/"+spend;refunded=true;break;}
                    if(!refunded)foreach(var a in s.actors){string spend=v.receipts.FirstOrDefault(x=>x.StartsWith("spell-spent/"+a.id+"/",StringComparison.Ordinal)&&!v.receipts.Contains("refund/"+x));if(spend==null)continue;a.spellSlots++;v.receipts.Add("refund/"+spend);refunded=true;break;}
                    if(!refunded)P6AwardResource(s,f.id,"fate-refund-fallback",20);break;
                case 11:foreach(var e in s.enemies.Where(e=>e.Alive).OrderBy(e=>e.id,StringComparer.Ordinal).Take(3))v.terrain.Add(new V13Terrain{id=f.effectId+"/"+e.id,kind="lightning",x=e.x,z=e.z,warning=1.2f,remaining=1});break;
                case 12:core.statuses.RemoveAll(x=>x.id=="restrained"||x.id=="p6-root"||x.v12?.family=="entangle");V13Heal(core,core.maxHp*.08);P6AwardResource(s,f.id,"fate-dawn",20);break;
            }
        }
    }
}
