using System;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        // These values describe the small P1 arena's conservative steering policy, not combat prices.
        public float PartyLeashRadius = 8f;
        public float PartyMemberSeparation = .9f;
        public float PartyArrivalDistance = .18f;
        // Optional dungeon routing only changes the next walking intention; the same wall sweep still moves the actor.
        public Func<Vector3, Vector3, Vector3> PartyNavigation;

        // ActorState.aiState for allies: 0 follow, 1 approach, 2 basic attack, 3 evade,
        // 4 commanded queue owns movement, 5 waiting for an existing action/status.
        // Enemy aiState retains its separate approach/windup/recovery meanings.
        readonly System.Collections.Generic.Dictionary<string,string> targetCache=new System.Collections.Generic.Dictionary<string,string>();
        readonly System.Collections.Generic.Dictionary<string,float> targetScanAt=new System.Collections.Generic.Dictionary<string,float>();
        ActorState CachedPartyTarget(ActorState actor,ActorState leader)
        {
            if(!State.inCombat)return null;
            targetCache.TryGetValue(actor.id,out var id);var target=State.Actor(id);
            targetScanAt.TryGetValue(actor.id,out var next);
            if(target!=null&&target.Alive&&!ReservedSmiteTarget(target.id,actor.id)&&State.simulationSeconds<next&&next-State.simulationSeconds<=.151f&&BattleGeometry.DistanceXZ(Position(target),Position(leader))<=PartyLeashRadius)return target;
            ActorState best=null;float distance=float.MaxValue;
            foreach(var enemy in State.enemies){if(!enemy.Alive||ReservedSmiteTarget(enemy.id,actor.id)||BattleGeometry.DistanceXZ(Position(enemy),Position(leader))>Mathf.Max(2,PartyLeashRadius))continue;float d=BattleGeometry.DistanceXZ(Position(enemy),Position(actor));if(d<distance){distance=d;best=enemy;}}
            targetCache[actor.id]=best?.id;targetScanAt[actor.id]=State.simulationSeconds+.15f;return best;
        }
        public void AdvanceParty(float dt) { using (P46Performance.Measure(5)) AdvancePartyMeasured(dt); }
        public void AdvancePartyMeasured(float dt)
        {
            if (V13AssistMode || dt <= 0 || IsFrozen || Session == null || State.actors.Count < 2 || ImpactFeedback!=null&&ImpactFeedback.Holding) return;
            var leader = ActiveActor;
            if (leader == null || !leader.Alive) leader = State.actors.FirstOrDefault(a => a.Alive);
            if (leader == null) return;
            string leaderId = leader.id;
            foreach (string actorId in State.actors.Where(a => a.Alive && a.id != State.activeActorId).Select(a => a.id).ToArray())
            {
                if(ImpactFeedback!=null&&ImpactFeedback.Holding)break;
                var actor = State.Actor(actorId);
                leader = State.Actor(leaderId); // Successful commands replace all DTOs.
                if (actor == null || !actor.Alive || leader == null || !leader.Alive) continue;
                actor.aiPolicyId = "conservative-v1";
                if (!string.IsNullOrEmpty(actor.heldBy) || State.queue.Any(q => q.actorId == actorId && (q.stage == "Queued" || q.stage == "Executing")))
                {
                    PartyIntent(actor, 4, actor.aiTargetId, Position(actor));
                    continue;
                }
                if (actor.windupRemaining > 0 || actor.staggerRemaining > 0 || actor.aimPreparing || CombatSession.V12ActionActive(actor) || P6Enabled&&actor.p6?.bindIntent!=null&&(actor.p6.bindIntent.stage=="WaitingBasic"||actor.p6.bindIntent.stage=="Casting"))
                {
                    PartyIntent(actor, 5, actor.aiTargetId, Position(actor));
                    continue;
                }
                if (CombatSession.HasPlayerOrder(actor) && (actor.basicOrder.kind == BasicOrderKind.Move || actor.basicOrder.kind == BasicOrderKind.Prepare || actor.basicOrder.kind == BasicOrderKind.Followup))
                {
                    PartyIntent(actor, 4, actor.basicOrder.targetId, new Vector3(actor.basicOrder.x, actor.heightY, actor.basicOrder.z));
                    continue; // The explicit order executor owns the movement until arrival/cancel.
                }
                float dodgeMotion=DodgeMovementSeconds(actor,dt);
                if (dodgeMotion > 0)
                {
                    // Switching away cannot convert an already-paid dodge into stationary invulnerability.
                    Vector3 destination = Position(actor) + new Vector3(actor.lockedX, 0, actor.lockedZ) * (Presentation.dodgeSpeed * dodgeMotion);
                    PartyIntent(actor, 3, null, destination);
                    MoveActor(actor, destination - Position(actor));
                    continue;
                }

                var orderedTarget = actor.basicOrder?.kind == BasicOrderKind.FocusTarget && CombatSession.HasPlayerOrder(actor) ? State.Actor(actor.basicOrder.targetId) : null;
                if(actor.basicOrder?.kind == BasicOrderKind.FocusTarget && CombatSession.HasPlayerOrder(actor) && (orderedTarget == null || !orderedTarget.Alive))
                { CancelOrderInternal(actor,"target_unavailable"); continue; }
                var target = orderedTarget ?? CachedPartyTarget(actor,leader);
                var basic = Session.Config.BasicFor(actor);
                Vector3 desired = PartyFormationPoint(actor, leader, target, basic.range);
                bool threatened = PartyDangerAt(Position(actor));
                if (threatened)
                {
                    if(CombatSession.HasPlayerOrder(actor)) { CancelOrderInternal(actor,"danger"); actor=State.Actor(actorId); }
                    desired = PartyEscapePoint(actor, desired);
                    PartyIntent(actor, 3, target?.id, desired);
                    PartyWalk(actor, desired, dt, true);
                    continue; // Repositioning takes priority over autonomous attacks.
                }

                // A new autonomous preparation must not lock an ally inside an existing warning.
                // Already committed actions and player movement orders retain their ownership above.
                if(TryV12AllyAssist(actor,target))continue;
                if (PartyNavigation != null) desired = PartyNavigation(Position(actor), desired);

                PartyIntent(actor, target == null ? 0 : 1, target?.id, desired);
                if(!(CombatSession.HasPlayerOrder(actor) && actor.basicOrder.kind == BasicOrderKind.Hold))PartyWalk(actor, desired, dt, false);
                if (target == null || !State.inCombat || actor.majorRemaining > 0 || actor.guardRemaining > 0 ||
                    actor.dodgeRemaining > 0 || actor.aimPreparing || !string.IsNullOrEmpty(actor.heldBy)) continue;
                if(IsFocus && SelectedActor == actor.id)continue; // Let the selected actor finish its current basic action, then retain the command window.
                if (BattleGeometry.DistanceXZ(Position(actor), Position(target)) > basic.range ||
                    !HasLineOfEffect(Position(actor), Position(target))) continue;
                actor.aiState = 2;
                ExecuteWorldCommand(new CombatCommand
                {
                    id = "party-basic/" + State.modeInstanceId + "/" + State.encounterId + "/" + actor.id + "/" + State.revision,
                    kind = CommandKind.StartBasicAttack, actorId = actor.id, targetId = target.id,
                    actionId = basic.id, templateId = "party-basic/" + actor.role,
                    flag = true, authority = CommandAuthority.AllyAI,
                    lineOfSight = HasLineOfEffect(Position(actor), Position(target))
                });
            }
        }

        static void PartyIntent(ActorState actor, int intent, string targetId, Vector3 destination)
        {
            actor.aiState = intent; actor.aiTargetId = targetId;
            actor.aiMoveX = destination.x; actor.aiMoveZ = destination.z;
        }

        Vector3 PartyFormationPoint(ActorState actor, ActorState leader, ActorState target, float attackRange)
        {
            Vector3 anchor = Position(leader);
            Vector3 forward = target == null ? Rig.ScreenMove(Vector2.up) : BattleGeometry.Ground(Position(target) - anchor).normalized;
            if (forward.sqrMagnitude < .01f) forward = Rig.ScreenMove(Vector2.up);
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 desired;
            if (target == null)
            {
                float depth = actor.formation == FormationPreference.Front ? 1.5f : actor.formation == FormationPreference.Back ? -2.2f : -.5f;
                float lateral = actor.formation == FormationPreference.Mid ? 1.35f : -.8f;
                desired = anchor + forward * depth + side * lateral;
            }
            else
            {
                // Front takes the nearer face, Mid takes a flank, Back uses its cantrip's actual range.
                float desiredRange = Mathf.Max(.7f, attackRange * (actor.formation == FormationPreference.Back ? .8f : .75f));
                Vector3 targetToLeader = -forward;
                if (actor.formation == FormationPreference.Mid) targetToLeader = (targetToLeader + side * .85f).normalized;
                if (actor.formation == FormationPreference.Front) targetToLeader = (targetToLeader - side * .25f).normalized;
                desired = Position(target) + targetToLeader * desiredRange;
                Vector3 offset = BattleGeometry.Ground(desired - anchor);
                if (offset.magnitude > PartyLeashRadius) desired = anchor + offset.normalized * PartyLeashRadius;
            }
            desired.y = actor.heightY;
            // Deterministic separation preserves distinct positions without a per-frame random stream.
            foreach (var other in State.actors.Where(a => a.Alive && a.id != actor.id))
            {
                Vector3 apart = BattleGeometry.Ground(desired - Position(other));
                float apartDistance = apart.magnitude;
                if (apartDistance >= PartyMemberSeparation) continue;
                if (apart.sqrMagnitude < .001f) apart = string.CompareOrdinal(actor.id, other.id) < 0 ? side : -side;
                desired += apart.normalized * (PartyMemberSeparation - apartDistance);
            }
            if(target!=null&&(!OrderPointFree(desired)||!HasLineOfEffect(desired,Position(target))))
            {
                // A comfortable range behind a wall is not an attack position. Walk the real route
                // towards the target until a valid firing position is reached, then retain it.
                bool canAttack=BattleGeometry.DistanceXZ(Position(actor),Position(target))<=attackRange&&HasLineOfEffect(Position(actor),Position(target));
                desired=canAttack?Position(actor):Position(target);
                desired.y=actor.heightY;
            }
            return desired;
        }

        bool PartyDangerAt(Vector3 point)
        {
            foreach (var enemy in State.enemies)
            {
                if (!enemy.Alive || enemy.aiState != 1 || enemy.aiRemaining <= 0) continue;
                AreaShape shape = EnemyShape(enemy);
                if (BattleGeometry.Contains(shape, point) && HasLineOfEffect(shape.Origin, point)) return true;
            }
            return false;
        }

        Vector3 PartyEscapePoint(ActorState actor, Vector3 formationPoint)
        {
            Vector3 origin = Position(actor), best = origin;
            float bestScore = float.PositiveInfinity;
            // Search actual world-space refuges around the actor, including the centre of a ring.
            // The same EnemyShape and wall sweep used by damage/preview decide whether they are safe.
            foreach (float distance in new[] { .8f, 1.6f, 2.8f, 4f, 5.6f })
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI * 2 / 16;
                    Vector3 delta = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                    Vector3 candidate = BattleGeometry.MoveAgainstWalls(origin, delta, .36f, 1 << WallLayer);
                    if (PartyDangerAt(candidate)) continue;
                    float score = BattleGeometry.DistanceXZ(origin, candidate) + .1f * BattleGeometry.DistanceXZ(candidate, formationPoint);
                    if (score < bestScore) { bestScore = score; best = candidate; }
                }
            return best;
        }

        void PartyWalk(ActorState actor, Vector3 destination, float dt, bool escaping)
        {
            if(!TryAiWalkingWaypoint(actor,destination,dt,out destination))return;
            Vector3 origin = Position(actor), delta = BattleGeometry.Ground(destination - origin);
            if (delta.magnitude <= Mathf.Max(.02f, PartyArrivalDistance)) return;
            float travel = Mathf.Min(delta.magnitude, Presentation.moveSpeed * dt * (actor.guardRemaining > 0 ? .3f : 1));
            Vector3 bestStep = Vector3.zero;
            float bestScore = float.PositiveInfinity;
            // The cached route can lead away from the final goal to pass a long wall.
            // Local avoidance and the final movement still use the actual collision capsule.
            foreach (float angle in new[] { 0f, 35f, -35f, 70f, -70f, 100f, -100f })
            {
                Vector3 step = Quaternion.AngleAxis(angle, Vector3.up) * delta.normalized * travel;
                Vector3 end = BattleGeometry.MoveAgainstWalls(origin, step, .36f, 1 << WallLayer);
                if (BattleGeometry.DistanceXZ(origin, end) < .0001f) continue;
                if (!escaping && (PartyDangerAt(end) || PartyDangerAt(Vector3.Lerp(origin, end, .5f)))) continue;
                float score = BattleGeometry.DistanceXZ(end, destination) + Mathf.Abs(angle) * .0005f;
                if (score < bestScore) { bestScore = score; bestStep = end - origin; }
            }
            if (bestStep.sqrMagnitude > 0) MoveActor(actor, bestStep);
        }
    }
}
