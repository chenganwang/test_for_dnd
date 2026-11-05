using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        bool TryV13BossCommand(CombatState s,CombatCommand c,out CombatResult r)
        {
            r=null;var b=s.v13?.boss;if(b==null)return false;
            if(c.kind==CommandKind.V13PillarHit)
            {
                var boss=Live(s,b.actorId);Require(c.authority==CommandAuthority.System&&c.qualified&&c.actorId==boss.id&&b.stage=="Charge","boss_collision_required");
                bool a=c.targetId=="pillar-A";Require((a?!b.pillarA:c.targetId=="pillar-B"&&!b.pillarB)&&DistanceTo(boss,b.arenaX+(a?-4:4),b.arenaZ)<=1.5f,"pillar_collision_invalid");
                if(a)b.pillarA=true;else b.pillarB=true;int damage=Math.Min(boss.hp,12);boss.hp-=damage;P5ApplyBreak(boss,3);b.stage="Recovery";b.remaining=2.4f;boss.aiState=2;boss.aiRemaining=2.4f;r=new CombatResult{damage=damage,damageApplied=true};return true;
            }
            if(c.kind==CommandKind.V13NodeHit)
            {
                Require(s.inCombat&&SimulationMode(s)&&s.pendingRoll==null&&s.activePendingEvent==null&&b.stage=="Ritual"&&c.qualified&&c.lineOfSight,"node_contact_invalid");
                bool first=c.targetId=="v13-node-A";Require(first?b.nodeA:c.targetId=="v13-node-B"&&b.nodeB,"node_already_broken");
                var actor=Live(s,c.actorId);var anchor=V13Anchor(s,actor);bool basic=c.actionId==Config.BasicFor(actor).id;
                Require(basic?V13Physical(s,actor):Config.AllowsAction(actor,c.actionId)&&new[]{"v12_mira_chase","v12_mira_break",P6SmiteId,"v11_quick_shot","attack"}.Contains(c.actionId),"ability_cannot_hit_scene_object");
                float range=basic?Config.BasicFor(actor).range:Config.Action(c.actionId).range;
                Require(P6MajorAvailable(actor)&&Member(s,actor.id).actionUntil<=s.simulationSeconds&&DistanceTo(anchor,b.arenaX+(first?-4:4),b.arenaZ+4)<=range,"node_out_of_range_or_busy");
                bool paid=!basic&&c.actionId!="v12_mira_chase";if(paid){Require(AvailableP6Charges(s)>0,"no_charge");s.charges--;DrainMomentum(s);}
                if(c.actionId=="v12_mira_chase"){Require(actor.v12.basicCooldown<=0,"hero_basic_cooldown");actor.v12.basicCooldown=Config.v12.chaseCooldown;}
                actor.majorRemaining=Config.v13.actionSeconds;Member(s,actor.id).actionUntil=s.simulationSeconds+Config.v13.actionSeconds;Member(s,actor.id).appearanceUntil=s.simulationSeconds+1;
                if(first)b.nodeA=false;else b.nodeB=false;
                if(!b.nodeA&&!b.nodeB){var boss=s.Actor(b.actorId);P5ApplyBreak(boss,2);b.stage="Recovery";b.remaining=1.6f;boss.aiState=2;boss.aiRemaining=1.6f;}
                s.v13.receipts.Add(b.root+"/"+c.targetId);r=new CombatResult{damageApplied=true,chargeSpent=paid?1:0,reason="ritual_node_destroyed"};return true;
            }
            return false;
        }
    }
}
