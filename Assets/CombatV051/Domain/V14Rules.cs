namespace CombatV051.Domain
{
    public sealed partial class CombatSession
    {
        public bool V14Enabled=>Config.v14!=null;
        public static bool V14Trio(CombatState s)=>s?.v14?.partyCombatMode==PartyCombatMode.PersistentTrio;
        public bool V14CoreBuff(string id,string actorId)=>V14Enabled&&Config.v11.coreActorId==actorId&&V12HasBuff(Config,id);
    }
}
