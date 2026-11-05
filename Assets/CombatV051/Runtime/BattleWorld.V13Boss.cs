using System;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        GameObject v13PillarA,v13PillarB,v13NodeA,v13NodeB;
        AreaPreview v13BandA,v13BandB;
        public Vector3[] V13BossMechanicPoints()
        {var b=State.v13?.boss;var center=b==null?Vector3.zero:new Vector3(b.arenaX,0,b.arenaZ);return new[]{center+new Vector3(-4,0,0),center+new Vector3(4,0,0),center+new Vector3(-4,0,4),center+new Vector3(4,0,4)};}
        public bool V13NodeAvailable(string id)=>V13Enabled&&State.v13.boss?.stage=="Ritual"&&(id=="v13-node-A"?State.v13.boss.nodeA:id=="v13-node-B"&&State.v13.boss.nodeB);
        public Vector3 V13NodePosition(string id)=>V13BossMechanicPoints()[id=="v13-node-A"?2:3];
        public CombatResult V13AttackNode(string actor,string node,string action=null)
        {
            var a=State.Actor(actor);var point=V13NodePosition(node);
            return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.V13NodeHit,actorId=actor,targetId=node,actionId=action??Session.Config.BasicFor(a).id,qualified=V13NodeAvailable(node),lineOfSight=HasLineOfEffect(CastOrigin(a),point),x=point.x,z=point.z});
        }
        bool V13TryClickNode()
        {
            if(!V13Enabled||State.v13.boss?.stage!="Ritual")return false;
            var id=new[]{"v13-node-A","v13-node-B"}.Where(V13NodeAvailable).OrderBy(n=>BattleGeometry.DistanceXZ(V13NodePosition(n),WorldAimPoint)).FirstOrDefault();
            if(id==null||BattleGeometry.DistanceXZ(V13NodePosition(id),WorldAimPoint)>1.3f)return false;
            if(IsFocus){SelectedTarget=id;Hud.RefreshView();}else V13AttackNode(State.activeActorId,id);return true;
        }
        void V13BossRecovery(ActorState enemy,float seconds)
        {var b=State.v13.boss;b.stage="Recovery";b.remaining=seconds;b.nodeA=b.nodeB=false;enemy.aiState=2;enemy.aiRemaining=seconds;enemy.attackGroupId=null;}
        bool AdvanceV13Boss(ActorState enemy,float dt)
        {
            var b=State.v13?.boss;if(!V13Enabled||b==null||b.actorId!=enemy.id)return false;
            if(!enemy.Alive)return true;
            if(b.phase==1&&enemy.hp<=enemy.maxHp*.5f){b.phase=2;Message="灰冠守门者 · 祷歌节点出现后可直接攻击；横扫可能接反向追击。";}
            if(enemy.staggerRemaining>0){if(b.stage!="Recovery")V13BossRecovery(enemy,3);b.breakImmunity=6;return true;}
            if(b.breakImmunity>0){b.breakImmunity=Mathf.Max(0,b.breakImmunity-dt);if(!enemy.statuses.Any(x=>x.id=="v13-break-immunity"))enemy.statuses.Add(new StatusState{id="v13-break-immunity",sourceId=enemy.id,remaining=b.breakImmunity});}
            b.remaining=Mathf.Max(0,b.remaining-dt);enemy.aiRemaining=b.remaining;
            var target=ActiveActor;
            if(CombatSession.V14Trio(State))
            {
                // A warned strike follows its original physical target until direction
                // lock; changing the operated character does not teleport its aim.
                if(b.stage!="Recovery")target=State.Actor(enemy.attackTargetId);
                if(target?.Alive!=true||!IsPhysical(target))target=State.actors.FirstOrDefault(a=>a.Alive&&IsPhysical(a));
            }
            if(target?.Alive!=true)return true;
            if(b.stage=="Recovery")
            {
                if(b.remaining>0||State.simulationSeconds<State.v13.enemyRestUntil||CombatSession.V13FateBusy(State)||State.v13.fate?.stage=="Scheduled"&&State.v13.fate.warning<=0)return true;
                string move=b.phase==1?(b.sequence%2==0?"Sweep":"Charge"):(b.sequence%3==2?"Ritual":b.sequence%3==0?"Sweep":"Charge");
                if(move=="Sweep"&&BattleGeometry.DistanceXZ(Position(enemy),Position(target))>3.8f){MoveDungeonEnemy(enemy,Position(target),2.3f*dt,dt);return true;}
                b.coreStartX=target.x;b.coreStartZ=target.z;b.sequence++;b.move=move;b.root=State.encounterId+"/v13-boss/"+b.sequence;enemy.attackGroupId=b.root;enemy.attackTargetId=target.id;enemy.attackSequence++;
                if(move=="Ritual"){b.stage="Ritual";b.remaining=3;b.nodeA=b.nodeB=true;enemy.aiState=1;Message="守门祷歌 · 攻击节点消除光带，或准备离开光带。";}
                else {b.stage="Warning";b.remaining=move=="Sweep"?1.1f:1.2f;enemy.aiState=1;enemy.lockedX=target.x;enemy.lockedZ=target.z;var d=(Position(target)-Position(enemy)).normalized;b.dx=d.x;b.dz=d.z;}
                if(!State.momentumLedger.registeredDangerousThreats.Contains(b.root))State.momentumLedger.registeredDangerousThreats.Add(b.root);
                if(move!="Ritual")V14CaptureEvadeThreat(enemy,b.root,V13BossShape(enemy));
                return true;
            }
            if(b.stage=="Warning")
            {
                float lockSeconds=b.move=="Sweep"?.55f:.65f;
                if(b.remaining>lockSeconds&&!b.secondSweep){var d=BattleGeometry.Ground(Position(target)-Position(enemy)).normalized;b.dx=d.x;b.dz=d.z;enemy.lockedX=target.x;enemy.lockedZ=target.z;}
                if(b.remaining>0)return true;
                if(b.move=="Charge"){b.stage="Charge";b.remaining=1;b.travel=0;return true;}
                var shape=AreaShape.Cone(Position(enemy),new Vector3(b.dx,0,b.dz),4,50);
                V13BossContact(enemy,shape,6,false,b.root+(b.secondSweep?"/second":"/first"));
                enemy=State.Actor(b.actorId);b=State.v13.boss;
                if(b.phase==2&&!b.secondSweep&&++b.sweeps%2==0){b.secondSweep=true;b.root+="/return";b.dx=-b.dx;b.dz=-b.dz;enemy.lockedX=enemy.x+b.dx*4;enemy.lockedZ=enemy.z+b.dz*4;b.remaining=.65f;enemy.aiState=1;}
                else {b.secondSweep=false;V13BossRecovery(enemy,1.25f);}return true;
            }
            if(b.stage=="Charge")
            {
                var origin=Position(enemy);var direction=new Vector3(b.dx,0,b.dz);float speed=Mathf.Min(Session.P5MovementMultiplier(enemy.id),Mathf.Min(Session.P6MovementMultiplier(enemy.id),Session.V12MovementMultiplier(enemy.id)));var intended=origin+direction*12*speed*dt;
                var actual=BattleGeometry.MoveAgainstWalls(origin,intended-origin,.55f,1<<WallLayer);enemy.x=actual.x;enemy.z=actual.z;b.travel+=BattleGeometry.DistanceXZ(origin,actual);
                if(CombatSession.V14Trio(State)||BattleGeometry.DistanceXZ(actual,Position(target))<1.2f)V13BossContact(enemy,AreaShape.Circle(actual,1.2f),8,false,b.root+"/charge");
                enemy=State.Actor(b.actorId);b=State.v13.boss;
                if(BattleGeometry.DistanceXZ(actual,intended)>.015f)
                {
                    V14SettleEvadeThreat(enemy,b.root+"/charge",AreaShape.Circle(actual,1.2f));
                    var points=V13BossMechanicPoints();int pillar=!b.pillarA&&BattleGeometry.DistanceXZ(actual,points[0])<1.5f?0:!b.pillarB&&BattleGeometry.DistanceXZ(actual,points[1])<1.5f?1:-1;
                    if(pillar>=0)ExecuteWorldCommand(new CombatCommand{id=b.root+"/pillar",kind=CommandKind.V13PillarHit,actorId=enemy.id,targetId=pillar==0?"pillar-A":"pillar-B",qualified=true,authority=CommandAuthority.System});
                    else V13BossRecovery(enemy,1.5f);
                }
                else if(b.remaining<=0||b.travel>=10){V14SettleEvadeThreat(enemy,b.root+"/charge",AreaShape.Circle(actual,1.2f));V13BossRecovery(enemy,1.5f);}return true;
            }
            if(b.stage=="Ritual")
            {if(b.remaining<=0){b.stage="Pulse";b.remaining=.9f;enemy.aiState=1;V14CaptureEvadeThreat(enemy,b.root,a=>b.nodeA&&V13InBossBand(a,0)||b.nodeB&&V13InBossBand(a,1));Message="能量带将爆发 · 走出条形边界。";}return true;}
            if(b.stage=="Pulse")
            {
                if(b.remaining>0)return true;
                V14SettleEvadeThreat(enemy,b.root+"/pulse",a=>b.nodeA&&V13InBossBand(a,0)||b.nodeB&&V13InBossBand(a,1));
                if(CombatSession.V14Trio(State))
                {
                    // Both surviving bands are one warned pulse and one reaction group.
                    bool bandA=b.nodeA,bandB=b.nodeB;
                    V14TrioBossContact(enemy,a=>bandA&&V13InBossBand(a,0)||bandB&&V13InBossBand(a,1),6,true,b.root+"/pulse");
                }
                else
                {
                    if(b.nodeA)V13BossBandContact(enemy,0,b.root+"/band-A");
                    enemy=State.Actor(b.actorId);b=State.v13.boss;
                    if(b.nodeB)V13BossBandContact(enemy,1,b.root+"/band-B");
                }
                V13BossRecovery(State.Actor(b.actorId),1.6f);return true;
            }
            return true;
        }
        void V13BossBandContact(ActorState boss,int index,string root)
        {
            var p=V13BossMechanicPoints()[index+2];var a=ActiveActor;
            if(Mathf.Abs(a.x-p.x)<=1&&a.z<=p.z&&a.z>=p.z-6)V13BossContact(boss,AreaShape.Circle(Position(a),.5f),6,true,root);
        }
        bool V13InBossBand(ActorState actor,int index)
        {
            var point=V13BossMechanicPoints()[index+2];
            return Mathf.Abs(actor.x-point.x)<=1&&actor.z<=point.z&&actor.z>=point.z-6;
        }
        void V14TrioBossContact(ActorState boss,Func<ActorState,bool> inside,int damage,bool save,string root)
        {
            if(State.deferredIncomingBatches.Any(batch=>batch.rootId==root))return;
            var targets=State.actors.Where(a=>a.Alive&&IsPhysical(a)&&inside(a)&&HasLineOfEffect(Position(boss),Position(a))&&
                !State.incomingRoots.Contains(root+"/"+a.id)).OrderBy(a=>a.id,StringComparer.Ordinal).ToArray();
            if(targets.Length==0)return;
            // A charge can reach another actor on a later simulation frame. Keep the
            // shared attack root, while each actor is committed at most once.
            var contacts=targets.Select(a=>new IncomingContact{targetId=a.id,damage=damage,savingThrow=save,dc=13,parryable=!save,
                reactionEligibleActorIds=State.actors.Where(other=>other.Alive&&IsPhysical(other)&&HasLineOfEffect(Position(other),Position(a))).Select(other=>other.id).ToList()}).ToList();
            ExecuteWorldCommand(new CombatCommand{id="contact/"+root+"/"+string.Join("+",targets.Select(a=>a.id)),kind=CommandKind.IncomingBatch,
                actorId=boss.id,rootId=root,qualified=true,authority=CommandAuthority.System,incomingContacts=contacts});
        }
        void V13BossContact(ActorState boss,AreaShape shape,int damage,bool save,string root)
        {
            if(State.v13?.boss?.stage!="Charge")V14SettleEvadeThreat(boss,root,shape);
            if(CombatSession.V14Trio(State)){V14TrioBossContact(boss,a=>BattleGeometry.Contains(shape,Position(a)),damage,save,root);return;}
            if(State.incomingRoots.Contains(root+"/"+State.activeActorId))return;
            var core=ActiveActor;if(!core.Alive||!BattleGeometry.Contains(shape,Position(core))||!HasLineOfEffect(Position(boss),Position(core)))
            {if(!V14Enabled&&BattleGeometry.Contains(shape,new Vector3(State.v13.boss.coreStartX,0,State.v13.boss.coreStartZ))&&BattleGeometry.DistanceXZ(Position(core),new Vector3(State.v13.boss.coreStartX,0,State.v13.boss.coreStartZ))>.4f&&State.v13.members.Any(m=>m.actorId==CombatSession.V12Mira)){var m=Session.Member(State,CombatSession.V12Mira);if(m.lastAvoidedRoot!=root){m.lastAvoidedRoot=root;if(m.actorId==State.v13.coreId)State.Actor(m.actorId).v12.readinessRemaining=4;else m.assistRewardUntil=State.simulationSeconds+4;}}return;}
            ExecuteWorldCommand(new CombatCommand{id="contact/"+root,kind=CommandKind.IncomingBatch,actorId=boss.id,rootId=root,qualified=true,authority=CommandAuthority.System,incomingContacts=new System.Collections.Generic.List<IncomingContact>{new IncomingContact{targetId=core.id,damage=damage,savingThrow=save,dc=13,parryable=!save,reactionEligibleActorIds=State.actors.Select(a=>a.id).ToList()}}});
        }
        AreaShape V13BossShape(ActorState e)
        {var b=State.v13.boss;return b.move=="Sweep"?AreaShape.Cone(Position(e),new Vector3(b.dx,0,b.dz),4,50):b.move=="Charge"?AreaShape.Line(Position(e),new Vector3(b.dx,0,b.dz),10,2.4f):AreaShape.Circle(Position(e),.1f);}
        void RefreshV13Boss()
        {
            bool active=V13Enabled&&State.v13.boss!=null&&State.Actor(State.v13.boss.actorId)?.Alive==true;
            if(!active){foreach(var go in new[]{v13PillarA,v13PillarB,v13NodeA,v13NodeB})if(go)go.SetActive(false);if(v13BandA)v13BandA.gameObject.SetActive(false);if(v13BandB)v13BandB.gameObject.SetActive(false);return;}
            var b=State.v13.boss;var p=V13BossMechanicPoints();
            if(!v13PillarA){v13PillarA=BattleVisual.Box("共鸣柱 A",p[0]+Vector3.up,new Vector3(.9f,2,.9f),new Color(.65f,.66f,.48f),transform,WallLayer);v13PillarB=BattleVisual.Box("共鸣柱 B",p[1]+Vector3.up,new Vector3(.9f,2,.9f),new Color(.65f,.66f,.48f),transform,WallLayer);v13NodeA=BattleVisual.Sphere("祷歌节点 A",p[2]+Vector3.up*.65f,.45f,new Color(.4f,1,.8f),transform);v13NodeB=BattleVisual.Sphere("祷歌节点 B",p[3]+Vector3.up*.65f,.45f,new Color(.4f,1,.8f),transform);v13BandA=BattleGeometry.CreatePreview(AreaShape.Circle(p[2],1),transform,Color.red).GetComponent<AreaPreview>();v13BandB=BattleGeometry.CreatePreview(AreaShape.Circle(p[3],1),transform,Color.red).GetComponent<AreaPreview>();}
            v13PillarA.SetActive(!b.pillarA);v13PillarB.SetActive(!b.pillarB);v13NodeA.SetActive(b.stage=="Ritual"&&b.nodeA);v13NodeB.SetActive(b.stage=="Ritual"&&b.nodeB);
            // Two parallel narrow rectangles, visible above healing and terrain outlines.
            foreach(var tuple in new[]{Tuple.Create(v13BandA,p[2],b.nodeA),Tuple.Create(v13BandB,p[3],b.nodeB)}){tuple.Item1.gameObject.SetActive(b.stage=="Pulse"&&tuple.Item3);tuple.Item1.GroundLift=.12f;tuple.Item1.SetShape(AreaShape.Line(tuple.Item2,Vector3.back,6,2));}
            Physics.SyncTransforms();
        }
    }
}
