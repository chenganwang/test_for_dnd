using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public const string V12Evan="char_story_evan",V12Mira="char_story_mira",V12Finn="char_story_finn";
        public bool V12Enabled=>Config.v12!=null&&Config.v11?.version=="v12-character-1";
        public static bool V12Hero(string id)=>id==V12Evan||id==V12Mira||id==V12Finn;
        public static string V12BasicAction(string id)=>id==V12Evan?"v12_evan_mark":id==V12Mira?"v12_mira_chase":id==V12Finn?"v12_finn_seed":null;
        public static string V12KeyAction(string id)=>id==V12Evan?"v12_evan_final":id==V12Mira?"v12_mira_break":id==V12Finn?"v12_finn_grove":null;
        public static bool V12Action(string id)=>id=="v12_evan_mark"||id=="v12_evan_final"||id=="v12_mira_chase"||id=="v12_mira_break"||id=="v12_finn_seed"||id=="v12_finn_grove"||id=="v13_finn_shield"||id=="v13_finn_prayer"||id==V14DetonateId;
        public static bool V12ActionActive(ActorState a)=>a?.v12?.intent!=null&&(a.v12.intent.stage=="WaitingBasic"||a.v12.intent.stage=="Casting"||a.v12.intent.stage=="Moving");
        public static ActionSpec V12Spec(string id,V12CombatConfig c)
        {
            if(c==null||!V12Action(id))return null;
            if(id==V14DetonateId)return new ActionSpec{id=id,rollType="Check",range=7,windup=.35f,ranged=true};
            if(id==V14DetonateId)return new ActionSpec{id=id,rollType="Check",range=7,windup=.35f,ranged=true};
            if(id=="v13_finn_shield"||id=="v13_finn_prayer")return new ActionSpec{id=id,rollType="Check",range=6,windup=id=="v13_finn_shield"?.25f:.5f};
            return new ActionSpec{id=id,rollType=id=="v12_evan_final"||id=="v12_mira_break"?"Attack":"Check",damage=0,ranged=id!="v12_mira_chase"&&id!="v12_mira_break",
                range=id=="v12_evan_mark"?c.markRange:id=="v12_evan_final"?c.finalRange:id=="v12_mira_chase"?c.chaseRange:id=="v12_mira_break"?c.breakRange:id=="v12_finn_seed"?c.seedRange:c.groveRange,
                windup=id=="v12_evan_mark"?c.markWindup:id=="v12_evan_final"?c.finalWindup:id=="v12_mira_chase"?c.chaseWindup:id=="v12_mira_break"?c.breakWindup:id=="v12_finn_seed"?c.seedWindup:c.groveWindup};
        }
        public static void InitializeV12State(CombatState s,CombatConfig c)
        {if(c.v12==null)return;if(s.v12==null)s.v12=new V12CombatState();foreach(var a in s.actors)if(a.v12==null)a.v12=new V12ActorState();}
        public static void ResetV12EncounterState(CombatState s)
        {if(s.v12==null)return;s.v12=new V12CombatState();foreach(var a in s.actors.Concat(s.enemies)){a.statuses.RemoveAll(x=>x.v12!=null);if(a.v12!=null){float cd=a.v12.basicCooldown;a.v12=new V12ActorState();if(s.v13!=null)a.v12.basicCooldown=cd;}}}
        bool V12Passive(ActorState a)=>V12Enabled&&a!=null&&Config.v11.actors.Any(x=>x.actorId==a.id&&x.passiveUnlocked);
        void ValidateV12(CombatState s)
        {
            if(Config.v12==null){Require(Config.v11?.version!="v12-character-1"&&s.v12==null,"v12_rules_missing");ValidateV12Build(s);return;}
            Require(V12Enabled&&Config.v12.version=="v12-heroes-1"&&s.v12!=null&&s.v12.events!=null&&s.v12.groves!=null,"invalid_v12_state");
            ValidateV12Environment(s);ValidateV12Build(s);var c=Config.v12;Require(new[]{c.markSeconds,c.markMultiplier,c.markRange,c.markWindup,c.markCooldown,c.echoCoefficient,c.echoInterval,c.finalCoefficient,c.finalRange,c.finalWindup,c.chaseRange,c.chaseWindup,c.chaseCooldown,c.chaseCoefficient,c.chaseMinimumTravel,c.chaseTimeout,c.readinessSeconds,c.readinessMultiplier,c.breakCoefficient,c.breakRange,c.breakWindup,c.seedRange,c.seedWindup,c.seedCooldown,c.parasiteSeconds,c.parasiteInterval,c.parasiteCoefficient,c.groveRange,c.groveWindup,c.groveRadius,c.groveSeconds,c.groveHealFraction,c.entangleMultiplier,c.bossEntangleMultiplier}.All(x=>Finite(x)&&x>0)&&c.groveMaximumEnemies>=1&&c.groveMaximumEnemies<=4&&c.entangleMultiplier<=1&&c.bossEntangleMultiplier<=1,"invalid_v12_config");
            Require(c.parasiteInterval>=.1f&&c.parasiteSeconds<=60&&c.groveSeconds<=60&&c.breakDamage>=0&&c.breakDamage<=10,"invalid_v12_time_limits");
            Require(s.v12.groves.All(x=>x!=null)&&s.v12.events.All(x=>x!=null)&&s.v12.groves.Select(x=>x.sourceId).Distinct().Count()==s.v12.groves.Count&&s.v12.events.Select(x=>x.id).Distinct().Count()==s.v12.events.Count,"duplicate_v12_event");
            foreach(var g in s.v12.groves)Require(!string.IsNullOrWhiteSpace(g.id)&&g.sourceId==V12Finn&&s.actors.Any(a=>a.id==g.sourceId)&&new[]{g.x,g.z,g.radius,g.expiresAt,g.nextTick}.All(Finite)&&g.radius>0&&g.radius<=c.groveRadius+.001f&&g.expiresAt>=0&&g.nextTick>=0&&g.tickIndex>=0&&g.tickIndex<=Math.Ceiling(c.groveSeconds)&&g.touchedEnemies!=null&&g.touchedEnemies.Count<=c.groveMaximumEnemies&&g.touchedEnemies.Distinct().Count()==g.touchedEnemies.Count&&g.touchedEnemies.All(id=>s.enemies.Any(a=>a.id==id)),"invalid_v12_grove");
            foreach(var e in s.v12.events)Require(!string.IsNullOrWhiteSpace(e.id)&&!string.IsNullOrWhiteSpace(e.rootId)&&s.actors.Any(a=>a.id==e.sourceId)&&s.Actor(e.targetId)!=null&&e.amount>=0&&Finite(e.simulationTime)&&e.simulationTime>=0&&e.simulationTime<=s.simulationSeconds+.001f&&!string.IsNullOrWhiteSpace(e.actionId)&&new[]{"direct","echo","dot","heal","grove","opening","parasite","environment","splash","bloom","detonate","v14-settle","v14-counter","v14-breakflow"}.Contains(e.kind),"invalid_v12_effect");
            foreach(var a in s.actors){Require(a.v12!=null&&new[]{a.v12.basicCooldown,a.v12.echoCooldown,a.v12.readinessRemaining}.All(x=>Finite(x)&&x>=0)&&V11Finite(a.v12.groveHealingRemainder)&&a.v12.groveHealingRemainder>=0&&a.v12.groveHealingRemainder<1,"invalid_v12_actor");var i=a.v12.intent;if(i!=null)Require(V12Action(i.actionId)&&!string.IsNullOrEmpty(i.id)&&new[]{i.remaining,i.startX,i.startZ,i.destinationX,i.destinationZ,i.deadline}.All(Finite)&&i.remaining>=0&&i.deadline>=0&&new[]{"WaitingBasic","Casting","Moving","Resolved","Canceled"}.Contains(i.stage),"invalid_v12_intent");}
            foreach(var a in s.actors.Concat(s.enemies))foreach(var status in a.statuses.Where(x=>x.v12!=null)){var p=status.v12;Require(a.isEnemy&&s.actors.Any(source=>source.id==status.sourceId)&&new[]{"opening","entangle","parasite"}.Contains(p.family)&&!string.IsNullOrEmpty(p.rootId)&&Finite(p.expiresAt)&&p.expiresAt>=0&&Finite(p.nextTick)&&p.nextTick>=0&&Finite(p.multiplier)&&p.multiplier>0&&p.multiplier<=10&&p.tickIndex>=0&&p.generation>=0&&p.generation<=1&&V11Finite(p.damageRemainder)&&p.damageRemainder>=0&&p.damageRemainder<1&&(p.family!="entangle"||p.multiplier<=1),"invalid_v12_status");}
            foreach(var g in s.v12.groves)Require(g.nextTick>=g.expiresAt-c.groveSeconds-.001f&&g.nextTick<=g.expiresAt+1.001f,"invalid_v12_grove_schedule");
            foreach(var p in s.enemies.SelectMany(a=>a.statuses).Where(x=>x.v12?.family=="parasite").Select(x=>x.v12))Require(p.nextTick>0&&p.nextTick>=p.expiresAt-c.parasiteSeconds-c.parasiteInterval-.001f&&p.nextTick<=p.expiresAt+c.parasiteInterval+.001f,"invalid_v12_parasite_schedule");
            foreach(var ticket in s.rolls.Where(t=>t.v12!=null))Require((ticket.actionId=="v12_evan_final"||ticket.actionId=="v12_mira_break")&&ticket.p5==null&&ticket.p6==null&&Finite(ticket.v12.startDeadline)&&ticket.v12.startDeadline>=0,"invalid_v12_ticket");
        }
        void TickV12(CombatState s,float dt)
        {
            if(!V12Enabled||dt<=0)return;
            foreach(var a in s.actors)
            {
                var v=a.v12;v.basicCooldown=Down(v.basicCooldown,dt);v.echoCooldown=Down(v.echoCooldown,dt);v.readinessRemaining=Down(v.readinessRemaining,dt);
                if(!V12ActionActive(a))continue;var i=v.intent;
                if(!a.Alive||a.staggerRemaining>0){i.stage="Canceled";i.reason="actor_interrupted";a.windupRemaining=0;continue;}
                if(i.stage=="WaitingBasic"){if(a.basicAttackIntent!=null&&!a.basicAttackIntent.resolved)continue;i.stage=i.actionId=="v12_mira_chase"?"Moving":"Casting";a.majorRemaining=0;a.windupRemaining=i.remaining;i.deadline=s.simulationSeconds+Config.v12.chaseTimeout;}
                else i.remaining=Down(i.remaining,dt);
                // Held until the authoritative resolve command; do not let background AI take the action lock.
                a.windupRemaining=Math.Max(.001f,i.remaining);
                if(i.stage=="Moving"&&s.simulationSeconds>i.deadline){i.stage="Canceled";i.reason="chase_path_timeout";a.windupRemaining=0;}
            }
            V12CleanupSources(s);
        }
        void V12CleanupSources(CombatState s)
        {if(!V12Enabled)return;foreach(var e in s.enemies)e.statuses.RemoveAll(x=>(x.v12?.family=="opening"||x.v12?.family=="entangle"||V14Enabled&&x.v14Dot!=null)&&s.Actor(x.sourceId)?.Alive!=true);s.v12.groves.RemoveAll(g=>s.Actor(g.sourceId)?.Alive!=true);foreach(var a in s.actors)if(!a.Alive||a.hp>=a.maxHp){a.v12.groveHealingRemainder=0;a.v12.buildHealingRemainder=0;}}
        public float V12MovementMultiplier(string id)
        {var a=State.Actor(id);if(!V12Enabled||a==null)return 1;float m=V13Enabled&&State.v13.activeRule==1&&State.v13.ruleUntil>State.simulationSeconds?.8f:1;foreach(var status in a.statuses)if(status.v12?.family=="entangle"&&status.remaining>0)m=Math.Min(m,status.v12.multiplier);return m;}
        public bool V12TickPending
        {
            get{if(!V12Enabled||!SimulationMode(State)||!State.inCombat)return false;float now=State.simulationSeconds;
                return State.v12.groves.Any(g=>g.nextTick<=now+.00001f||g.expiresAt<=now||State.enemies.Any(e=>e.Alive&&DistanceTo(e,g.x,g.z)<=g.radius&&(!g.touchedEnemies.Contains(e.id)&&g.touchedEnemies.Count<Config.v12.groveMaximumEnemies||g.touchedEnemies.Contains(e.id)&&!e.statuses.Any(x=>x.v12?.family=="entangle"&&x.sourceId==g.sourceId)))||State.enemies.Any(e=>e.statuses.Any(x=>x.id=="v12-entangle"&&x.sourceId==g.sourceId)&&(DistanceTo(e,g.x,g.z)>g.radius||!e.Alive)))||State.enemies.Any(e=>e.statuses.Any(x=>x.v12!=null&&(x.v12.expiresAt<=now||x.v12.family=="parasite"&&x.v12.nextTick<=now+.00001f)));}
        }
    }
}
