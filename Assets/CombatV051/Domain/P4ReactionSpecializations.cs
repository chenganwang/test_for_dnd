using System.Linq;
namespace CombatV051.Domain
{
    public sealed partial class ActorState
    {
        public bool p4FirstShoveUsed,p4NearMissUsed,p4ConcentrationProtectionUsed;
    }
    public sealed partial class CombatSession
    {
        public static void ResetP4ReactionSpecializations(CombatState s)
        {foreach(var a in s.actors.Concat(s.enemies)){a.p4FirstShoveUsed=false;a.p4NearMissUsed=false;a.p4ConcentrationProtectionUsed=false;}}
        void BreakConcentrationFromDamage(CombatState s,ActorState owner)
        {
            if(string.IsNullOrEmpty(owner.concentrationId))return;
            if(owner.Alive&&Config.RulesFor(owner.id).concentrationProtection&&!owner.p4ConcentrationProtectionUsed)
            {owner.p4ConcentrationProtectionUsed=true;return;}
            BreakConcentration(s,owner);
        }
    }
}
