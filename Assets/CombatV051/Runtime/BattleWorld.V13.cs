using System;
using System.Linq;
using System.Collections.Generic;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public bool V13Enabled=>Session?.Config.v13!=null;
        public bool V14Enabled=>Session?.Config.v14!=null;
        public bool V13AssistMode=>Session?.V13AssistMode==true;
        public string V13PaidAssist {get;private set;}
        string v13RollingId;float v13RollTime;
        readonly Dictionary<string,GameObject> v13TerrainViews=new Dictionary<string,GameObject>();
        public string[] V13Assists=>V13Enabled?State.actors.Where(a=>a.id!=State.v13.coreId).Select(a=>a.id).ToArray():Array.Empty<string>();
        public string CoreBasicAction=>Session.HeroBasic(State.activeActorId);
        public string CoreKeyAction=>Session.HeroKey(State.activeActorId);
        public bool IsPhysical(ActorState a)=>CombatSession.V13Physical(State,a);
        public Vector3 CastOrigin(ActorState a)=>Position(V13Enabled&&!IsPhysical(a)?ActiveActor:a);
        public bool V13Visible(ActorState a)=>!V13Enabled||IsPhysical(a)||Session.Member(State,a.id)?.appearanceUntil>State.simulationSeconds||CombatSession.V12ActionActive(a)||a.windupRemaining>0;
        Vector3 V13PresentationPosition(ActorState a)
        {
            if(!V13Enabled||IsPhysical(a))return Position(a);
            var intent=a.v12?.intent;
            if(intent?.actionId=="v12_mira_chase"&&intent.stage=="Moving")return Vector3.Lerp(new Vector3(intent.startX,0,intent.startZ),new Vector3(intent.destinationX,0,intent.destinationZ),1-Mathf.Clamp01(intent.remaining/Session.Config.v12.chaseWindup));
            Vector3 offset=new Vector3(a.id==V13Assists.FirstOrDefault()?-1.1f:1.1f,0,-.7f);
            return BattleGeometry.MoveAgainstWalls(Position(ActiveActor),offset,.36f,1<<WallLayer);
        }
        public CombatResult RequestAssist(int slot,bool authorize=false)
        {
            var ids=V13Assists;if(slot<0||slot>=ids.Length)return new CombatResult{reason="assist_missing"};
            EnsureV12Target(State.activeActorId);string id=ids[slot];var actions=AvailableActions(id);string action=id==CombatSession.V12Evan?"v12_evan_mark":id==CombatSession.V12Mira?"v12_mira_chase":id==CombatSession.V12Finn?Session.HeroBasic(id):id==P6CharacterCatalog.PaladinId?CombatSession.P6SmiteId:actions.FirstOrDefault();
            if(action==CombatSession.P6SmiteId&&!authorize){V13PaidAssist=id;Message="至圣斩支援消耗1指挥次数；确认后使用基础斩，不购买全力。";return new CombatResult{reason="assist_payment_confirmation"};}
            V13PaidAssist=null;
            if(action==CombatSession.P6BindId)return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestBindRadiance,actorId=id,targetId=SelectedTarget,actionId=action});
            if(action==CombatSession.P6SmiteId)return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.ConfirmRoll,actorId=id,targetId=SelectedTarget,actionId=action,p6=new P6CommandData{preacceptBaseResult=true}});
            return RequestHeroAction(id,action,action=="v13_finn_shield"?State.activeActorId:SelectedTarget,CommandAuthority.Player);
        }
        public void ConfirmPaidAssist(){int slot=Array.IndexOf(V13Assists,V13PaidAssist);if(slot>=0)RequestAssist(slot,true);}
        public void CancelPaidAssist(){V13PaidAssist=null;}
        public CombatResult V13Correct(string choice=null)=>ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.RequestHeroAction,actorId=CombatSession.V12Evan,targetId=State.pendingRoll?.rollOwnerId??(IsFocus&&State.actors.Any(a=>a.id==SelectedTarget)?SelectedTarget:State.activeActorId),actionId="v13_evan_correct",v14CorrectionChoice=choice});
        public CombatResult V13QuickKey()
        {
            EnsureV12Target(State.activeActorId);string action=CoreKeyAction;
            if(action=="v13_evan_correct")return V13Correct();
            if(action=="v12_mira_break")return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.ConfirmRoll,actorId=State.activeActorId,targetId=SelectedTarget,actionId=action,p6=new P6CommandData{preacceptBaseResult=true}});
            return RequestHeroAction(State.activeActorId,action,action=="v13_finn_prayer"?State.activeActorId:SelectedTarget,CommandAuthority.Player);
        }
        public void V13ChooseFate(bool replace)
        {
            var f=State.v13?.fate;if(f?.stage!="Deciding")return;
            int raw=replace?f.echoRaw*5:f.original;
            bool safe=V13SafeTerrain(out var point);
            ExecuteWorldCommand(new CombatCommand{id=f.id+(replace?"/replace":"/accept"),kind=replace?CommandKind.V13FateReplace:CommandKind.V13FateAccept,actorId=State.activeActorId,qualified=safe,x=point.x,z=point.z});
        }
        bool V13SafeTerrain(out Vector3 point)
        {
            point=Vector3.zero;if(Profile==null)return false;var room=DungeonConfig.Room(Profile.ActiveRoom.templateId);
            foreach(var offset in new[]{new Vector3(-6,0,-4),new Vector3(6,0,-4),new Vector3(-6,0,4),new Vector3(6,0,4)})
            {
                var p=new Vector3(room.x,0,room.z)+offset;
                if(!OrderPointFree(p)||!HasLineOfEffect(Position(ActiveActor),p)||State.actors.Concat(State.enemies).Any(a=>a.Alive&&IsPhysical(a)&&BattleGeometry.DistanceXZ(Position(a),p)<2.5f))continue;
                if(Profile.ActiveMode.v12Run.points.Any(a=>BattleGeometry.DistanceXZ(new Vector3(a.x,0,a.z),p)<2.5f)||DungeonConfig.passages.Any(a=>BattleGeometry.DistanceXZ(new Vector3(a.x,0,a.z),p)<3))continue;
                if(State.v13.boss!=null&&V13BossMechanicPoints().Any(a=>BattleGeometry.DistanceXZ(a,p)<3))continue;
                // Side-pocket anchors leave the central >= 2m corridor and all exits open.
                if(Physics.CheckBox(p+Vector3.up*.5f,new Vector3(1.8f,.5f,.8f),Quaternion.identity,1<<WallLayer))continue;
                if(PartyDangerAt(p))continue;point=p;return true;
            }
            return false;
        }
        bool AdvanceV13Decision()
        {
            if(!V13Enabled||Menu||DungeonModal||State.mode==CombatMode.CombatPaused)return false;
            var f=State.v13.fate;
            if(f?.stage=="Rolled")
            {
                if(v13RollingId!=f.id){v13RollingId=f.id;v13RollTime=0;Audio?.PlayCue(AudioCue.DiceRoll,Position(ActiveActor),f.id+"/roll",.8f);}
                v13RollTime+=Time.unscaledDeltaTime;
                if(v13RollTime>=(QuickDice?.8f:2f)){ExecuteWorldCommand(new CombatCommand{id=f.id+"/reveal",kind=CommandKind.V13FateReveal,authority=CommandAuthority.System});Audio?.PlayCue(AudioCue.DiceLand,Position(ActiveActor),f.id+"/land",.8f);}
                ApplyTime();Hud.RefreshView();return true;
            }
            if(f?.stage=="Deciding"&&f.decision<=0)V13ChooseFate(false);
            return false;
        }
        void AdvanceV13Fate(float dt)
        {
            if(!V13Enabled||!State.inCombat||IsFrozen)return;var f=State.v13.fate;
            if(f?.stage=="Scheduled"&&f.warning<=0&&(State.mode==CombatMode.RealtimeCombat||State.mode==CombatMode.QueuedExecution)&&State.activePendingEvent==null&&!PartyDangerAt(Position(ActiveActor))&&!Session.V12BeamPending)
            {
                bool safe=State.enemies.All(a=>!a.Alive||a.aiState!=1)&&State.world.projectiles.All(p=>p.resolved)&&(State.v13.boss==null||State.v13.boss.stage=="Recovery");
                if(safe)ExecuteWorldCommand(new CombatCommand{id=f.id+"/start",kind=CommandKind.V13FateStart,authority=CommandAuthority.System,qualified=true});
            }
            if(!CombatSession.V13FateBusy(State)&&(f?.stage=="Applied"||State.v13.terrain.Any(t=>t.remaining<=0||t.warning<=0&&(t.kind=="lightning"||t.kind=="heal"&&t.nextTick<=State.simulationSeconds))||State.world.numbers.Any(p=>p.Key.StartsWith("v13-fate-shield/")&&p.Key.EndsWith("/until")&&p.Value<=State.simulationSeconds)))
                ExecuteWorldCommand(new CombatCommand{id="v13-effects/"+State.encounterId+"/"+State.revision,kind=CommandKind.V13FateEffects,authority=CommandAuthority.System});
            RefreshV13Terrain();
        }
        void RefreshV13Terrain()
        {
            foreach(var entry in v13TerrainViews)if(entry.Value)entry.Value.SetActive(V13Enabled&&State.v13.terrain.Any(t=>t.id==entry.Key));
            if(!V13Enabled)return;
            foreach(var t in State.v13.terrain)
            {
                if(!v13TerrainViews.TryGetValue(t.id,out var go)||!go)
                {
                    if(t.kind=="cover")go=BattleVisual.Box("潮汐浮石 / "+t.id,new Vector3(t.x,.6f,t.z),new Vector3(2.4f,1.2f,.65f),new Color(.45f,.62f,.72f),transform,WallLayer,true);
                    else go=BattleGeometry.CreatePreview(AreaShape.Circle(new Vector3(t.x,0,t.z),t.kind=="heal"?2.5f:1.1f),transform,t.kind=="heal"?new Color(.3f,.9f,.6f):new Color(1,.6f,.1f));
                    v13TerrainViews[t.id]=go;
                }
                go.SetActive(t.remaining>0);var collider=go.GetComponent<Collider>();if(collider)collider.enabled=t.warning<=0;
            }
            Physics.SyncTransforms();
        }
    }
}

