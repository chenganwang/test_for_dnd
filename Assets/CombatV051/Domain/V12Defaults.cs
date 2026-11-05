namespace CombatV051.Domain
{
    // The editable Resource has an explicit build-time map shared by Player and rule tests.
    public static class V12Defaults
    {
        public static V12CombatConfig Create()=>CombatJson.Read<V12CombatConfig>(V12ConfigJson.Text);
        public static string SourceJson=>V12ConfigJson.Text;
    }
}
