using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;

namespace CombatV051.Runtime
{
    public sealed partial class BattlePresentationSmokeFlow
    {
        bool V14RouteRequested=>Array.IndexOf(Environment.GetCommandLineArgs(),"-v14-route")>=0;
        string V14Argument(string name,string fallback=null){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        string V14RequestedMode=>V14Argument("-v14-mode","CoreAndAssist");
        string v14CombatMode,v14CoreCharacterId,v14RulesVersion,v14DefaultPreparationMode;
        readonly Dictionary<string,V14DamageEvent> v14DamageObserved=new Dictionary<string,V14DamageEvent>();
        readonly Dictionary<string,V14DetonationEvent> v14DetonationsObserved=new Dictionary<string,V14DetonationEvent>();
        void ObserveV14Combat()
        {
            if(!V14RouteRequested||world.State.v14==null)return;
            if(world.State.v14DamageEvents!=null)foreach(var e in world.State.v14DamageEvents)if(!v14DamageObserved.ContainsKey(e.id))v14DamageObserved.Add(e.id,CombatJson.Clone(e));
            if(world.State.v14Detonations!=null)foreach(var e in world.State.v14Detonations)if(!v14DetonationsObserved.ContainsKey(e.id))v14DetonationsObserved.Add(e.id,CombatJson.Clone(e));
        }
        void WriteV14CombatEvidence()
        {
            if(!V14RouteRequested)return;
            Write("v14-damage.json",new{classification="ACTUAL_PLAYER_OBSERVED_DAMAGE",events=v14DamageObserved.Values.ToArray()});
            Write("v14-detonations.json",new{classification="ACTUAL_PLAYER_OBSERVED_ATOMIC_DETONATIONS",events=v14DetonationsObserved.Values.ToArray()});
        }
        void V14RecordDeparture()
        {
            if(!V14RouteRequested)return;
            var d=world.Profile.ActiveMode.v11Departure;
            v14CombatMode=d.preparation.partyCombatMode.ToString();v14CoreCharacterId=d.preparation.coreId;v14RulesVersion=d.v14?.version;
            Require(v14CombatMode==V14RequestedMode&&v14RulesVersion=="v14-dual-builds-1","Actual departure did not retain V14 mode/rules");
            Require(d.v14.finnGrowthAction==(V14Argument("-v14-finn-slot","bloom")=="grove"?"v12_finn_grove":"v14_finn_detonate"),"Actual Finn second slot differs");
        }
    }
}
