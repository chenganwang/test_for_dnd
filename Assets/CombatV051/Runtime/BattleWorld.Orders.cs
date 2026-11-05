using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        readonly Dictionary<string,List<Vector3>> orderPaths=new Dictionary<string,List<Vector3>>();
        readonly Dictionary<string,float> orderStuck=new Dictionary<string,float>();
        sealed class AiWalkingRoute
        {
            public Vector3 goal, lastPosition;
            public float sinceSearch;
            public List<Vector3> points;
        }
        readonly Dictionary<string,AiWalkingRoute> aiWalkingRoutes=new Dictionary<string,AiWalkingRoute>();
        string aiWalkingEncounter;
        // Diagnostic counter: a capsule sweep is cheap; a bounded A* search is cached and rate limited.
        public int AiPathSearchCount { get; private set; }
        string rearActor,rearTarget,rearReason;
        Vector3 rearActorPosition,rearTargetPosition,rearFacing,rearDestination;
        float rearNextRefresh;
        bool rearFound;
        List<Vector3> rearPath;
        public void RefreshEngagementEvidence()
        {
            if(Session==null||IsFrozen)return;
            foreach(var actor in State.actors)
                if(actor.basicOrder?.kind==BasicOrderKind.Prepare&&actor.role==ActorRole.Fighter)
                {var ally=State.Actor(actor.basicOrder.protectTargetId);actor.basicOrder.protectionLineOfSight=ally!=null&&HasLineOfEffect(Position(actor),Position(ally));}
            bool training=State.world.strings.TryGetValue("context",out var context)&&context=="TRAINING";
            bool engaged=(!training||P6Enabled&&State.p6.training)&&State.inCombat&&!State.encounterResolved&&State.enemies.Any(enemy=>enemy.Alive&&State.actors.Any(actor=>actor.Alive&&
                BattleGeometry.DistanceXZ(Position(actor),Position(enemy))<=Session.Config.effectiveCombatRange&&HasLineOfEffect(Position(enemy),Position(actor))));
            State.momentumLedger.disengaged=!engaged;
            if(!engaged)return;
            if(Profile!=null&&State.momentumLedger.encounterKind=="Boss"&&State.momentumLedger.phaseId!="boss-half-health")
            {
                var boss=State.enemies.FirstOrDefault(enemy=>enemy.Alive&&Profile.EnemyTemplateFor(enemy.id)?.id=="boss"&&enemy.maxHp>0&&enemy.hp/(float)enemy.maxHp<=.5f);
                if(boss!=null)
                {
                    string root=boss.id+"/boss-half-health";
                    var phase=ExecuteWorldCommand(new CombatCommand{id="focus-phase/"+State.modeInstanceId+"/"+State.encounterId+"/"+root,kind=CommandKind.AwardFact,
                        actorId=State.actors.First(a=>a.Alive).id,targetId=boss.id,actionId="focus-boss-phase",rootId=root,factId=root,qualified=true,authority=CommandAuthority.System});
                    if(!phase.success)Message="阶段结算尚未保存："+phase.reason;
                }
            }
            foreach(var enemy in State.enemies)
            {
                if(!enemy.Alive||enemy.aiState!=1||string.IsNullOrEmpty(enemy.attackGroupId)||!State.momentumLedger.registeredHighThreats.Contains(enemy.id))continue;
                bool threatensParty=State.actors.Any(actor=>actor.Alive&&HasLineOfEffect(Position(enemy),Position(actor))&&
                    (Profile!=null?BattleGeometry.Contains(DungeonEnemyShape(enemy),Position(actor)):BattleGeometry.DistanceXZ(Position(enemy),Position(actor))<=Session.Config.effectiveCombatRange));
                if(threatensParty&&!State.momentumLedger.registeredDangerousThreats.Contains(enemy.attackGroupId))State.momentumLedger.registeredDangerousThreats.Add(enemy.attackGroupId);
            }
        }
        public string SelectedOrderSummary
        {
            get
            {
                var a=Session==null?null:State.Actor(SelectedActor);var o=a?.basicOrder;
                if(o==null)return "尚无基础指令";
                string name=o.kind==BasicOrderKind.Move?"移动":o.kind==BasicOrderKind.Hold?"守位":o.kind==BasicOrderKind.FocusTarget?"集火":o.kind==BasicOrderKind.Follow?"跟随":o.kind==BasicOrderKind.Followup?"接应下一次普攻":"职业准备";
                string stage=o.stage=="Preparing"?"准备中":o.stage=="WaitingWindow"?"等待敌人锁定攻击朝向":o.stage=="Canceled"?"已取消":o.stage=="Finished"?"已完成":o.stage=="Holding"?"保持位置":"执行中";
                return name+" · "+stage+(o.stage=="Canceled"?"（"+OrderReason(o.reason)+"）":"");
            }
        }
        static string OrderReason(string reason)
        {
            switch(reason)
            {
                case "order_destination_unreachable": return "墙体阻挡路线或目的地不可达，请选择其他位置";
                case "rear_destination_unreachable": return "目标后侧被遮挡或无法到达，请先调整位置";
                case "target_out_of_range": return "目标超出准备范围，请先靠近";
                case "target_facing_unknown": return "目标尚未确定攻击朝向，暂时无法选择后侧";
                case "actor_unavailable": return "队员当前无法执行指令";
                case "basic_order_missing": return "当前没有可取消的基础指令";
                case "target_unavailable": return "目标已失效，请重新选择";
                case "preparation_interrupted": return "准备被打断";
                case "preparation_moved": return "移动中止准备";
                case "route_blocked": return "路径受阻";
                case "flank_window_expired": return "本次侧翼窗口已过期";
                case "actor_down": return "队员倒下";
                case "emergency_reposition": return "危险区迫使队员自保";
                default: return "指令已撤回";
            }
        }
        public CombatResult IssueMoveOrder(Vector3 point,bool replace=false)
        {
            var actor=State.Actor(SelectedActor);
            if(actor==null)return OrderFailure("actor_unavailable");
            if(!TryOrderPath(Position(actor),point,out var path))return OrderFailure("order_destination_unreachable");
            var command=NewOrder(BasicOrderKind.Move,replace);command.x=point.x;command.z=point.z;command.qualified=true;
            var result=ExecuteWorldCommand(command);if(result.success)orderPaths[command.id]=path;return result;
        }
        public CombatResult IssueHoldOrder(bool replace=false)=>ExecuteWorldCommand(NewOrder(BasicOrderKind.Hold,replace));
        public CombatResult IssueFocusTargetOrder(string targetId,bool replace=false)
        {var c=NewOrder(BasicOrderKind.FocusTarget,replace);c.targetId=targetId;return ExecuteWorldCommand(c);}
        public CombatResult IssueFollowOrder(bool replace=false)=>ExecuteWorldCommand(NewOrder(BasicOrderKind.Follow,replace));
        public CombatResult IssuePreparationOrder(bool replace=false)
        {
            var actor=State.Actor(SelectedActor);if(actor==null)return OrderFailure("actor_unavailable");
            var c=NewOrder(BasicOrderKind.Prepare,replace);List<Vector3> path=null;
            if(actor.role==ActorRole.Fighter)
            {
                var ally=State.Actor(SelectedTarget);
                if(P5Enabled&&(ally==null||ally.isEnemy))ally=actor;
                if(ally==null||ally.isEnemy||!ally.Alive||BattleGeometry.DistanceXZ(Position(actor),Position(ally))>Session.Config.RulesFor(actor.id).protectionRange)
                    ally=State.actors.Where(a=>a.Alive&&a.id!=actor.id&&BattleGeometry.DistanceXZ(Position(actor),Position(a))<=Session.Config.RulesFor(actor.id).protectionRange).OrderBy(a=>a.hp/(float)a.maxHp).FirstOrDefault()??actor;
                c.targetId=ally.id;
            }
            else if(actor.role==ActorRole.Rogue)
            {
                if(!TryGetRearDestination(out var rear,out var reason))return OrderFailure(reason);
                if(!TryOrderPath(Position(actor),rear,out path))return OrderFailure("rear_destination_unreachable");
                c.x=rear.x;c.z=rear.z;c.qualified=true;
            }
            else if(actor.role==ActorRole.Mage)c.preparationActionCategory=Session.Config.Action(SelectedAction)?.rollType=="Save"?"Save":"Attack";
            var result=ExecuteWorldCommand(c);if(result.success&&path!=null)orderPaths[c.id]=path;return result;
        }
        public CombatResult CancelSelectedOrder()
        {
            var actor=State.Actor(SelectedActor);if(actor?.basicOrder==null)return OrderFailure("basic_order_missing");
            return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.CancelBasicOrder,actorId=actor.id,orderId=actor.basicOrder.id,actionId="player"});
        }
        CombatCommand NewOrder(BasicOrderKind kind,bool replace)=>new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.IssueBasicOrder,actorId=SelectedActor,targetId=SelectedTarget,actionId=kind.ToString(),replaceExisting=replace};
        CombatResult OrderFailure(string reason){Message="指令未执行："+OrderReason(reason);return new CombatResult{success=false,reason=reason};}
        public bool TryGetRearDestination(out Vector3 destination,out string reason)
        {
            destination=Vector3.zero;reason="target_unavailable";if(Session==null)return false;
            var actor=State.Actor(SelectedActor);var target=State.Actor(SelectedTarget);
            if(actor==null||target==null||!actor.Alive||!target.Alive||!target.isEnemy)return false;
            var facingPoint=new Vector3(target.lockedX,0,target.lockedZ);
            if(rearActor==actor.id&&rearTarget==target.id&&Time.unscaledTime<rearNextRefresh&&
                Vector3.Distance(rearActorPosition,Position(actor))<.2f&&Vector3.Distance(rearTargetPosition,Position(target))<.2f&&Vector3.Distance(rearFacing,facingPoint)<.2f)
            {destination=rearDestination;reason=rearReason;return rearFound;}
            rearActor=actor.id;rearTarget=target.id;rearActorPosition=Position(actor);rearTargetPosition=Position(target);rearFacing=facingPoint;rearNextRefresh=Time.unscaledTime+.2f;
            rearFound=ComputeRearDestination(actor,target,out rearDestination,out rearReason);destination=rearDestination;reason=rearReason;return rearFound;
        }
        bool ComputeRearDestination(ActorState actor,ActorState target,out Vector3 destination,out string reason)
        {
            destination=Vector3.zero;reason="rear_destination_unreachable";
            if(BattleGeometry.DistanceXZ(Position(actor),Position(target))>Session.Config.preparationRange){reason="target_out_of_range";return false;}
            var facing=BattleGeometry.Ground(new Vector3(target.lockedX,0,target.lockedZ)-Position(target));
            if(facing.sqrMagnitude<.001f){reason="target_facing_unknown";return false;}
            var candidates=new List<Vector3>();
            foreach(float distance in new[]{1.5f,2f})foreach(float angle in new[]{0f,30f,-30f})
            {
                var candidate=Position(target)+(Quaternion.AngleAxis(angle,Vector3.up)*-facing.normalized)*distance;
                candidate.y=actor.heightY;
                if(!OrderPointFree(candidate)||!HasLineOfEffect(candidate,Position(target)))continue;
                candidates.Add(candidate);
            }
            foreach(var candidate in candidates.OrderBy(p=>BattleGeometry.DistanceXZ(Position(actor),p)))
            {
                if(!TryOrderPath(Position(actor),candidate,out var path))continue;
                rearPath=path;destination=candidate;reason="ok";return true;
            }
            return false;
        }
        static float PathLength(Vector3 start,List<Vector3> path){float total=0;foreach(var p in path){total+=BattleGeometry.DistanceXZ(start,p);start=p;}return total;}
        Vector3[] OrderPreviewPath(ActorState actor,Vector3 fallback)
        {
            List<Vector3> path=null;
            if(actor.basicOrder!=null)orderPaths.TryGetValue(actor.basicOrder.id,out path);
            if(path==null&&rearFound&&rearActor==actor.id&&Vector3.Distance(fallback,rearDestination)<.05f)path=rearPath;
            var points=new List<Vector3>{Position(actor)};if(path!=null)points.AddRange(path);else points.Add(fallback);
            return points.Select(p=>p+Vector3.up*.055f).ToArray();
        }
        static bool OrderPointFree(Vector3 point)=>!Physics.CheckCapsule(point+Vector3.up*.36f,point+Vector3.up*1.24f,.36f,1<<WallLayer,QueryTriggerInteraction.Ignore);
        bool OrderSegmentFree(Vector3 from,Vector3 to)=>BattleGeometry.DistanceXZ(BattleGeometry.MoveAgainstWalls(from,to-from,.36f,1<<WallLayer),to)<.08f;
        bool TryAiWalkingWaypoint(ActorState actor,Vector3 destination,float dt,out Vector3 waypoint)
        {
            Vector3 origin=Position(actor);destination.y=actor.heightY;waypoint=destination;
            string encounter=State.modeInstanceId+"/"+State.encounterId;
            if(aiWalkingEncounter!=encounter)
            {aiWalkingRoutes.Clear();aiWalkingEncounter=encounter;AiPathSearchCount=0;}
            // CapsuleCast does not reliably report a collider overlapping the starting capsule.
            // Reject that state conservatively; never interpret a missed sweep as a path through a wall.
            if(!OrderPointFree(origin)){aiWalkingRoutes.Remove(actor.id);return false;}
            if(!aiWalkingRoutes.TryGetValue(actor.id,out var route))
            {route=new AiWalkingRoute{sinceSearch=1,lastPosition=origin};aiWalkingRoutes.Add(actor.id,route);}
            route.sinceSearch+=Mathf.Max(0,dt);
            // A load/placement fixture can move an actor without walking its cached route.
            if(BattleGeometry.DistanceXZ(origin,route.lastPosition)>2)route.points=null;
            route.lastPosition=origin;
            if(OrderPointFree(destination)&&OrderSegmentFree(origin,destination))
            {route.points=null;route.goal=destination;return true;}
            if(route.points!=null)
                while(route.points.Count>1&&BattleGeometry.DistanceXZ(origin,route.points[0])<.2f)route.points.RemoveAt(0);
            bool blocked=route.points==null||route.points.Count==0||!OrderSegmentFree(origin,route.points[0]);
            bool moved=BattleGeometry.DistanceXZ(destination,route.goal)>.75f;
            // Failed searches are cached as well. A moving target cannot trigger A* every frame.
            if(route.sinceSearch>=.75f&&(blocked||moved))
            {
                route.goal=destination;route.sinceSearch=0;AiPathSearchCount++;
                route.points=TryOrderPath(origin,destination,out var path)?path:null;
                blocked=route.points==null||route.points.Count==0;
            }
            if(blocked)return false;
            waypoint=route.points[0];return true;
        }
        public bool TryOrderPath(Vector3 from,Vector3 to,out List<Vector3> path) { using(P46Performance.Measure(7)) return TryOrderPathMeasured(from,to,out path); }
        bool TryOrderPathMeasured(Vector3 from,Vector3 to,out List<Vector3> path)
        {
            path=new List<Vector3>();to.y=from.y;
            if(BattleGeometry.DistanceXZ(from,to)>20||!OrderPointFree(from)||!OrderPointFree(to))return false;
            if(OrderSegmentFree(from,to)){path.Add(to);return true;}
            // Bounded local A*. Every edge uses the same capsule sweep as actual actor movement.
            const float cell=.65f;const int radius=32;
            var open=new List<Vector2Int>{Vector2Int.zero};var visited=new HashSet<Vector2Int>();
            var scores=new Dictionary<Vector2Int,float>{{Vector2Int.zero,0}};var parents=new Dictionary<Vector2Int,Vector2Int>();
            Vector2Int end=Vector2Int.zero;bool reached=false;
            while(open.Count>0&&visited.Count<2200)
            {
                var current=open.OrderBy(n=>scores[n]+BattleGeometry.DistanceXZ(from+new Vector3(n.x*cell,0,n.y*cell),to)).First();open.Remove(current);
                if(!visited.Add(current))continue;var point=from+new Vector3(current.x*cell,0,current.y*cell);
                if(BattleGeometry.DistanceXZ(point,to)<1.0f&&OrderSegmentFree(point,to)){end=current;reached=true;break;}
                for(int x=-1;x<=1;x++)for(int z=-1;z<=1;z++)
                {
                    if(x==0&&z==0)continue;var next=current+new Vector2Int(x,z);
                    if(Mathf.Abs(next.x)>radius||Mathf.Abs(next.y)>radius||visited.Contains(next))continue;
                    var step=from+new Vector3(next.x*cell,0,next.y*cell);if(!OrderPointFree(step)||!OrderSegmentFree(point,step))continue;
                    float cost=scores[current]+Vector3.Distance(point,step);if(scores.TryGetValue(next,out float old)&&old<=cost)continue;
                    scores[next]=cost;parents[next]=current;if(!open.Contains(next))open.Add(next);
                }
            }
            if(!reached)return false;
            var nodes=new List<Vector3>{to};while(end!=Vector2Int.zero){nodes.Add(from+new Vector3(end.x*cell,0,end.y*cell));end=parents[end];}nodes.Reverse();
            Vector3 last=from;for(int i=0;i<nodes.Count;i++){int far=i;while(far+1<nodes.Count&&OrderSegmentFree(last,nodes[far+1]))far++;path.Add(nodes[far]);last=nodes[far];i=far;}
            return true;
        }
        public void AdvanceBasicOrders(float dt) { using (P46Performance.Measure(6)) AdvanceBasicOrdersMeasured(dt); }
        public void AdvanceBasicOrdersMeasured(float dt)
        {
            if(Session==null||dt<=0||IsFrozen||ImpactFeedback!=null&&ImpactFeedback.Holding)return;
            foreach(string id in State.actors.Where(a=>a.Alive).Select(a=>a.id).ToArray())
            {
                if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                var actor=State.Actor(id);var intent=actor.basicAttackIntent;
                if(intent!=null&&!intent.resolved&&intent.remaining<=0)
                {
                    var contactTarget=State.Actor(intent.targetId);
                    ExecuteWorldCommand(new CombatCommand{id=intent.id+"/contact",kind=CommandKind.ResolveBasicAttack,actorId=id,targetId=intent.targetId,orderId=intent.id,authority=CommandAuthority.System,
                        lineOfSight=contactTarget!=null&&HasLineOfEffect(Position(actor),Position(contactTarget))});
                    actor=State.Actor(id);
                    // ResolveBasicAttack has finished damage, the intent and Major recovery together.
                    // Do not move this actor's order or resolve another actor in the remaining frame.
                    if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                }
                if(!CombatSession.HasPlayerOrder(actor)||!string.IsNullOrEmpty(actor.heldBy)||actor.windupRemaining>0||actor.staggerRemaining>0)continue;
                var order=actor.basicOrder;
                if(PartyDangerAt(Position(actor)))
                {
                    // Brace is explicitly ordered to meet a dangerous frontal contact.
                    if(!(P5Enabled&&order.kind==BasicOrderKind.Prepare&&actor.role==ActorRole.Fighter))
                    {CancelOrderInternal(actor,"danger");Message="队员暂时中止指令，避开危险区域。";continue;}
                }
                if(P5Enabled&&order.kind==BasicOrderKind.Followup)
                { AdvanceP5FollowupOrder(actor,dt);continue; }
                if(order.kind==BasicOrderKind.Prepare&&actor.role!=ActorRole.Rogue)
                {
                    var target=State.Actor(order.targetId);
                    if(target==null||!HasLineOfEffect(Position(actor),Position(target)))CancelOrderInternal(actor,"route_blocked");
                    continue;
                }
                if(order.kind==BasicOrderKind.Hold||order.kind==BasicOrderKind.FocusTarget)continue;
                Vector3 destination=new Vector3(order.x,actor.heightY,order.z);
                if(BattleGeometry.DistanceXZ(Position(actor),destination)<=Session.Config.orderArrivalDistance)
                {
                    if(order.stage=="WaitingWindow")
                    {
                        var target=State.Actor(order.targetId);
                        if(target==null||target.aiState!=1||string.IsNullOrEmpty(target.attackGroupId)||!Session.CanUseRearPosition(id,target.id))continue;
                    }
                    ExecuteWorldCommand(new CombatCommand{id=order.id+"/arrival/"+State.revision,kind=CommandKind.CompleteBasicOrder,actorId=id,targetId=order.targetId,orderId=order.id,authority=CommandAuthority.System});continue;
                }
                if(!orderPaths.TryGetValue(order.id,out var path)||path.Count==0)
                {if(!TryOrderPath(Position(actor),destination,out path)){CancelOrderInternal(actor,"route_blocked");continue;}orderPaths[order.id]=path;}
                while(path.Count>1&&BattleGeometry.DistanceXZ(Position(actor),path[0])<.25f)path.RemoveAt(0);
                var delta=BattleGeometry.Ground(path[0]-Position(actor));float multiplier=order.kind==BasicOrderKind.Prepare?Session.Config.RulesFor(actor.id).preparationMoveMultiplier:1;
                float travel=Mathf.Min(delta.magnitude,Presentation.moveSpeed*dt*multiplier);var before=Position(actor);MoveActor(actor,delta.normalized*travel);
                float stuck=orderStuck.TryGetValue(order.id,out var old)?old:0;
                orderStuck[order.id]=BattleGeometry.DistanceXZ(before,Position(actor))<travel*.05f?stuck+dt:0;
                if(orderStuck[order.id]>.5f){orderPaths.Remove(order.id);orderStuck[order.id]=0;if(!TryOrderPath(Position(actor),destination,out path))CancelOrderInternal(actor,"route_blocked");else orderPaths[order.id]=path;}
            }
        }
        void CancelOrderInternal(ActorState actor,string reason)
        {
            var result=ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.CancelBasicOrder,actorId=actor.id,orderId=actor.basicOrder.id,actionId=reason,authority=CommandAuthority.System});
            if(result.success)Message="基础指令取消："+OrderReason(reason);
        }
        public bool DirectMovementMayOverrideOrder(ActorState actor,bool hasMovementInput)
        {
            if(!CombatSession.HasPlayerOrder(actor))return true;
            if(hasMovementInput&&!IsFocus){CancelOrderInternal(actor,"player");return true;}
            return false;
        }
        public CombatResult BeginBasicAttack(string actorId,string targetId,CommandAuthority authority=CommandAuthority.Player)
        {
            return ExecuteWorldCommand(new CombatCommand{id=Guid.NewGuid().ToString("N"),kind=CommandKind.StartBasicAttack,actorId=actorId,targetId=targetId,authority=authority,flag=authority==CommandAuthority.AllyAI});
        }
    }
}
