using System;

namespace CombatV051.Domain
{
    // Zero is the shipped three-entity party and the meaning of an absent legacy field.
    public enum PartyCombatMode { PersistentTrio=0, CoreAndAssist=1 }
    // Migration vocabulary only. It does not grant skills or permit off-field execution.
    public enum SkillUsageMode { Both=0, Core=1, Assist=2 }

    public static class PartyCombatModePolicy
    {
        public static void RequireImplemented(PartyCombatMode mode)
        {
            if(mode==PartyCombatMode.CoreAndAssist)return;
            if(mode!=PartyCombatMode.PersistentTrio)throw new InvalidOperationException("party_combat_mode_invalid");
        }
        // Existing action IDs keep their current owning-character permissions and execution pipeline.
        // This reserved classification is not a replacement for CombatConfig.AllowsAction.
        public static SkillUsageMode SkillUsageFor(string actionId)
        {
            if(string.IsNullOrWhiteSpace(actionId))throw new ArgumentException("action_id_required",nameof(actionId));
            return SkillUsageMode.Both;
        }
    }
}
