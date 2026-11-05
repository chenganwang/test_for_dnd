using System;
using System.Linq;

namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        // Explicit training commands use the same transaction/receipt path as combat.
        // The UI sidecar only tracks instructions; it cannot award resources.
        bool TryP6TrainingCommand(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;
            if(c.kind!=CommandKind.RefillPracticeResources&&c.kind!=CommandKind.ChoosePracticeUpgrade)return false;
            Require(Config.p6!=null&&s.p6!=null&&s.p6.training&&s.world.strings.TryGetValue("context",out var context)&&context=="TRAINING","practice_only");
            Require(c.authority==CommandAuthority.Player,"player_order_required");
            Require(s.pendingRoll==null&&s.activePendingEvent==null&&!s.queue.Any(q=>q.stage=="Queued"||q.stage=="Executing"),"finish_pending_roll_first");
            if(c.kind==CommandKind.RefillPracticeResources)
            {
                s.charges=s.chargeCap;s.momentum=0;
                foreach(var actor in s.actors){actor.hp=actor.maxHp;actor.inspiration=Math.Max(actor.inspiration,2);}
                // Explicit reset keeps this finite training target usable after a kill.
                // It preserves roll/payment history and never awards formal encounter rewards.
                foreach(var enemy in s.enemies){enemy.hp=enemy.maxHp;enemy.statuses.Clear();enemy.staggerRemaining=0;enemy.aiState=0;enemy.aiRemaining=1;}
                s.inCombat=true;s.encounterResolved=false;
                if(s.mode!=CombatMode.TacticalFocus)s.mode=CombatMode.RealtimeCombat;
                if(c.flag)s.p6.trainingBlessingEnabled=true;
                result=new CombatResult{reason=c.flag?"practice_refill_and_visible_blessing":"practice_refill"};
            }
            else
            {
                Require(string.IsNullOrEmpty(s.p6.trainingUpgradeId),"practice_upgrade_already_taken");
                Require(c.actionId=="practice_radiant"||c.actionId=="practice_accuracy"||c.actionId=="practice_bind","unknown_practice_upgrade");
                Require(s.p6.damageResults.Any(x=>x.actionId=="ability_smite_recharge"&&x.overcharged&&x.totalDamage>0),"practice_finish_overcharge_first");
                s.p6.trainingUpgradeId=c.actionId;
                if(c.actionId=="practice_accuracy")s.Actor("char_demo_paladin").attackBonus+=2;
                result=new CombatResult{reason="practice_upgrade_applied"};
            }
            return true;
        }
    }
}
