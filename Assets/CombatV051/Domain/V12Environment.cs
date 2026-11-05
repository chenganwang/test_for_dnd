using System;
using System.Linq;

namespace CombatV051.Domain
{
    [Serializable] public sealed class V12BeamConfig
    {
        public float triggerX=46,triggerZ=18,triggerRange=2.5f,x=55,z=23,radius=2.5f,warningSeconds=.8f;
        public int damage=12;
    }
    [Serializable] public sealed class V12BeamState
    {
        public string id,stage="Ready",actorId,requestRoot;
        public float startedAt,resolveAt;
    }
    public sealed partial class CombatSession
    {
        const string BeamRegistered="v12/beamRegistered",BeamSpent="v12/beamSpent/";
        public bool V12BeamPending=>V12Enabled&&State.v12?.beam?.stage=="Warning";
        public bool V12BeamReady=>V12Enabled&&State.v12?.beam?.stage=="Ready";
        public float V12BeamRemaining=>V12BeamPending?Math.Max(0,State.v12.beam.resolveAt-State.simulationSeconds):0;
        // Called only while the existing room authority constructs the R4 encounter snapshot.
        // Re-registering after a safe-state restore never re-arms a consumed device.
        public static void RegisterV12Beam(CombatState s,CombatConfig config,string stableInstanceId)
        {
            Require(config?.v12?.beam!=null&&s.v12!=null&&!string.IsNullOrWhiteSpace(stableInstanceId)&&stableInstanceId.StartsWith(s.modeInstanceId+"/",StringComparison.Ordinal),"v12_beam_registration_invalid");
            if(s.v12.beam!=null){Require(s.v12.beam.id==stableInstanceId,"v12_beam_duplicate_registration");return;}
            s.world.strings[BeamRegistered]=stableInstanceId;
            s.v12.beam=new V12BeamState{id=stableInstanceId,stage=s.world.strings.ContainsKey(BeamSpent+stableInstanceId)?"Resolved":"Ready"};
        }
        void ValidateV12Environment(CombatState s)
        {
            var c=Config.v12?.beam;var b=s.v12?.beam;
            if(c!=null)Require(new[]{c.triggerX,c.triggerZ,c.triggerRange,c.x,c.z,c.radius,c.warningSeconds}.All(Finite)&&c.triggerRange>0&&c.triggerRange<=6&&c.radius>0&&c.radius<=6&&c.warningSeconds>=.1f&&c.warningSeconds<=3&&c.damage>=0&&c.damage<=10000,"v12_beam_config_invalid");
            if(b==null)return;
            Require(V12Enabled&&c!=null&&!string.IsNullOrWhiteSpace(b.id)&&s.world.strings.TryGetValue(BeamRegistered,out var id)&&id==b.id&&new[]{"Ready","Warning","Resolved"}.Contains(b.stage)&&Finite(b.startedAt)&&b.startedAt>=0&&Finite(b.resolveAt)&&b.resolveAt>=b.startedAt,"v12_beam_state_invalid");
            if(b.stage=="Ready")Require(!s.world.strings.ContainsKey(BeamSpent+b.id)&&b.requestRoot==null,"v12_beam_ready_already_spent");
            else Require(s.world.strings.ContainsKey(BeamSpent+b.id),"v12_beam_spend_missing");
            if(b.stage=="Warning")Require(s.actors.Any(a=>a.id==b.actorId)&&!string.IsNullOrWhiteSpace(b.requestRoot)&&Math.Abs(b.resolveAt-b.startedAt-c.warningSeconds)<.001f&&b.startedAt<=s.simulationSeconds+.001f,"v12_beam_warning_invalid");
        }
        bool TryV12EnvironmentCommand(CombatState s,CombatCommand c,out CombatResult result)
        {
            result=null;if(c.kind!=CommandKind.RequestV12Beam&&c.kind!=CommandKind.ResolveV12Beam)return false;
            Require(V12Enabled&&Config.v12.beam!=null&&s.v12.beam!=null&&s.inCombat&&!s.encounterResolved,"v12_beam_not_registered");
            var b=s.v12.beam;var rules=Config.v12.beam;
            if(c.kind==CommandKind.RequestV12Beam)
            {
                Require(c.authority==CommandAuthority.Player&&!IsAI(c),"player_order_required");Require(NormalMode(s)&&s.pendingRoll==null&&s.activePendingEvent==null,"v12_beam_realtime_required");
                var a=Live(s,c.actorId);Require(!a.isEnemy&&DistanceTo(a,rules.triggerX,rules.triggerZ)<=rules.triggerRange+.001f&&c.qualified&&c.lineOfSight,"v12_beam_trigger_out_of_reach");
                Require(b.stage=="Ready"&&!s.world.strings.ContainsKey(BeamSpent+b.id),"v12_beam_already_used");
                b.actorId=a.id;b.requestRoot=b.id+"/fall";b.startedAt=s.simulationSeconds;b.resolveAt=b.startedAt+rules.warningSeconds;b.stage="Warning";s.world.strings[BeamSpent+b.id]="1";
                return (result=new CombatResult{reason="v12_beam_warning",orderId=b.id})!=null;
            }
            Require(c.authority==CommandAuthority.System&&SimulationMode(s),"system_command_required");Require(b.stage=="Warning"&&s.simulationSeconds+.00001f>=b.resolveAt,"v12_beam_not_ready");
            // Consume before iterating unique actors. Visuals can only read the published result.
            b.stage="Resolved";var source=s.Actor(b.actorId);Require(source!=null,"v12_beam_source_missing");result=new CombatResult{reason="v12_beam_resolved",orderId=b.id,damageApplied=true};
            foreach(var target in s.actors.Concat(s.enemies).Where(a=>a.Alive&&V13Physical(s,a)&&DistanceTo(a,rules.x,rules.z)<=rules.radius+.001f).ToList())
                result.damage+=V12Damage(s,source,target,b.requestRoot,"v12_falling_beam","environment",rules.damage,false,result);
            result.hit=result.damage>0;return true;
        }
    }
}
