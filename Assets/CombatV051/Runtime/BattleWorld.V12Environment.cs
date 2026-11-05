using System;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        GameObject v12BeamVisualRoot,v12BeamBody,v12BeamLever;
        AreaPreview v12BeamArea,v12BeamTriggerArea;
        readonly V12BeamState v12BeamHistoricalVisual=new V12BeamState();
        public bool V12BeamAvailable=>V12Enabled&&Session.V12BeamReady&&State.inCombat&&ActiveActor?.Alive==true&&
            BattleGeometry.DistanceXZ(Position(ActiveActor),new Vector3(Session.Config.v12.beam.triggerX,0,Session.Config.v12.beam.triggerZ))<=Session.Config.v12.beam.triggerRange;
        public string V12BeamHint=>!V12Enabled||State.v12?.beam==null?null:Session.V12BeamPending?"落梁将落下 · 敌我均受伤，请离开红圈":Session.V12BeamReady?"E 释放落梁 · 一次使用 · 红圈内敌我受伤":"落梁已使用";
        public bool TryV12BeamInteraction()
        {
            if(!V12BeamAvailable||IsFrozen||IsFocus)return false;
            var c=Session.Config.v12.beam;var trigger=new Vector3(c.triggerX,ActiveActor.heightY,c.triggerZ);
            var result=ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestV12Beam,actorId=ActiveActor.id,targetId=null,actionId="v12_falling_beam",qualified=true,lineOfSight=HasLineOfEffect(Position(ActiveActor),trigger)});
            if(result.success&&!result.duplicate)
            {Message="落梁已释放：0.8模拟秒后落下，敌我均受伤！";Audio?.PlayCue(AudioCue.EnemyTelegraph,new Vector3(c.x,0,c.z),State.v12.beam.id+"/warning",.75f);RefreshV12EnvironmentPresentation();}
            return result.success;
        }
        void AdvanceV12Environment(float dt)
        {
            if(dt<=0||!V12Enabled||IsFrozen||!Session.V12BeamPending||Session.V12BeamRemaining>0)return;
            var beam=State.v12.beam;var rules=Session.Config.v12.beam;
            var result=ExecuteWorldCommand(new CombatCommand{id=beam.id+"/resolve",kind=CommandKind.ResolveV12Beam,actorId=beam.actorId,targetId=null,actionId="v12_falling_beam",authority=CommandAuthority.System});
            if(result.success&&!result.duplicate)
            {Message=result.damage>0?"落梁落下 · 敌我真实伤害已结算":"落梁砸空 · 装置已消耗";Audio?.PlayCue(AudioCue.Break,new Vector3(rules.x,0,rules.z),beam.id+"/impact",.8f);}
        }
        void EnsureV12BeamVisuals()
        {
            if(v12BeamVisualRoot!=null)return;
            v12BeamVisualRoot=new GameObject("V12 R4 single-use beam");v12BeamVisualRoot.transform.SetParent(transform,false);
            v12BeamBody=BattleVisual.Box("Hanging beam - visual only",Vector3.zero,new Vector3(4.3f,.32f,.55f),new Color(.35f,.27f,.19f),v12BeamVisualRoot.transform,collider:false);
            v12BeamLever=BattleVisual.Box("Beam release post",Vector3.zero,new Vector3(.22f,.9f,.22f),new Color(.53f,.41f,.23f),v12BeamVisualRoot.transform,collider:false);
            var knob=BattleVisual.Sphere("Release handle",Vector3.zero,.16f,new Color(.83f,.66f,.31f),v12BeamLever.transform);
            knob.transform.localPosition=new Vector3(0,.5f,0);knob.transform.localScale=new Vector3(1.2f,.3f,1.2f);
            var area=new GameObject("Beam danger preview");area.transform.SetParent(v12BeamVisualRoot.transform,false);v12BeamArea=area.AddComponent<AreaPreview>();v12BeamArea.Width=.09f;v12BeamArea.GroundLift=.06f;
            var activation=new GameObject("Beam release interaction range");activation.transform.SetParent(v12BeamVisualRoot.transform,false);v12BeamTriggerArea=activation.AddComponent<AreaPreview>();v12BeamTriggerArea.Width=.04f;v12BeamTriggerArea.GroundLift=.05f;v12BeamTriggerArea.Color=new Color(.85f,.7f,.34f,.6f);
        }
        void RefreshV12EnvironmentPresentation()
        {
            var beam=State.v12?.beam;
            // The room authority clears encounter-only timers after victory; retain the
            // fallen prop from the durable world fact when returning to this same room.
            if(beam==null&&V12Enabled&&Profile?.ActiveRoom?.templateId=="V12_R4"&&State.world.strings.TryGetValue("v12/beamRegistered",out var registered))
            {v12BeamHistoricalVisual.id=registered;v12BeamHistoricalVisual.stage=State.world.strings.ContainsKey("v12/beamSpent/"+registered)?"Resolved":"Ready";beam=v12BeamHistoricalVisual;}
            bool show=V12Enabled&&!Menu&&beam!=null&&Session.Config.v12.beam!=null;
            if(!show){if(v12BeamVisualRoot!=null)v12BeamVisualRoot.SetActive(false);return;}
            EnsureV12BeamVisuals();v12BeamVisualRoot.SetActive(true);var c=Session.Config.v12.beam;
            float progress=beam.stage=="Resolved"?1:beam.stage=="Warning"?1-Mathf.Clamp01(Session.V12BeamRemaining/c.warningSeconds):0;
            float fall=Mathf.SmoothStep(0,1,Mathf.Clamp01((progress-.45f)/.55f));
            v12BeamBody.transform.position=new Vector3(c.x,Mathf.Lerp(3.4f,.18f,fall),c.z);v12BeamBody.transform.rotation=Quaternion.Euler(0,0,beam.stage=="Resolved"?4:0);
            v12BeamLever.transform.position=new Vector3(c.triggerX,.45f,c.triggerZ);
            v12BeamTriggerArea.gameObject.SetActive(beam.stage=="Ready");v12BeamTriggerArea.SetShape(AreaShape.Circle(new Vector3(c.triggerX,0,c.triggerZ),c.triggerRange));
            v12BeamArea.gameObject.SetActive(beam.stage!="Resolved");v12BeamArea.Color=beam.stage=="Warning"?new Color(1,.22f,.14f,.9f):new Color(.87f,.57f,.29f,.45f);v12BeamArea.SetShape(AreaShape.Circle(new Vector3(c.x,0,c.z),c.radius));
        }
    }
}
