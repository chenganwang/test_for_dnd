using System;
using System.Collections.Generic;
using System.Linq;
using CombatV051.Domain;
using UnityEngine;

namespace CombatV051.Runtime
{
    public sealed partial class BattleWorld
    {
        public const string P5R1ImpactId = "P5/R1/NORTH_IMPACT";
        Transform p5EnvironmentRoot;
        P5ImpactSurface p5Impact;
        GameObject p5SpentCracks;
        int p5DisplayedUses = -1;
        public bool P5EnvironmentEnabled => Session != null && Session.Config.p5 != null;
        void ResetP5EnvironmentForMap()
        {
            if(p5EnvironmentRoot){p5EnvironmentRoot.gameObject.SetActive(false);Destroy(p5EnvironmentRoot.gameObject);}
            p5EnvironmentRoot=null;p5Impact=null;p5SpentCracks=null;p5DisplayedUses=-1;
        }

        /// <summary>Call after ReconcileDungeon. Only these additional runtime objects are owned here.</summary>
        public void ReconcileP5Environment()
        {
            bool enabled = P5EnvironmentEnabled && DungeonMap != null && Profile?.UsesV12Dungeon!=true;
            if (!enabled) { if (p5EnvironmentRoot) p5EnvironmentRoot.gameObject.SetActive(false); return; }
            if (!p5EnvironmentRoot)
            {
                var room = DungeonConfig.rooms.Find(r => r.id == "R1");
                if (room == null) return;
                Vector3 center = new Vector3(room.x, 0, room.z);
                p5EnvironmentRoot = new GameObject("P5 R1 optional combat environment").transform;
                p5EnvironmentRoot.SetParent(DungeonMap.transform, false);
                // The two ends retain several metres of clearance; neither lies in the existing doorways.
                var cover = BattleVisual.Box("低掩体 · 远程防护", center + new Vector3(0, .45f, 3),
                    new Vector3(4, .9f, .65f), new Color(.38f, .39f, .34f), p5EnvironmentRoot, WallLayer);
                cover.AddComponent<P5LowCover>();
                Physics.SyncTransforms();
                Vector3 probe = center + new Vector3(7, .7f, room.depth * .5f - 2);
                if (!Physics.Raycast(probe, Vector3.forward, out RaycastHit hit, 3, 1 << WallLayer, QueryTriggerInteraction.Ignore) ||
                    !hit.collider.transform.IsChildOf(DungeonMap.transform))
                    throw new InvalidOperationException("The existing R1 wall required by the P5 impact marker is missing.");
                p5Impact = hit.collider.GetComponent<P5ImpactSurface>();
                if (p5Impact && p5Impact.SurfaceId != P5R1ImpactId)
                    throw new InvalidOperationException("Existing impact identity differs; preserve the authored surface.");
                if (!p5Impact) p5Impact = hit.collider.gameObject.AddComponent<P5ImpactSurface>();
                p5Impact.SurfaceId = P5R1ImpactId;
                p5Impact.RestrictContactSpan = true; p5Impact.ContactCenter = hit.point;
                p5Impact.ContactTangent = Vector3.right; p5Impact.ContactHalfWidth = 1;
                // Decorative panels are not extra blockers. The actual registered surface is the existing wall collider.
                Vector3 plaque = hit.point + Vector3.back * .035f;
                BattleVisual.Box("撞击石面", plaque, new Vector3(2, 1.15f, .025f),
                    new Color(.47f, .36f, .21f), p5EnvironmentRoot, 0, false);
                p5SpentCracks = new GameObject("耗尽裂痕");
                p5SpentCracks.transform.SetParent(p5EnvironmentRoot, false);
                for (int i = 0; i < 2; i++)
                {
                    var crack = BattleVisual.Box("裂痕", plaque + new Vector3(i == 0 ? -.25f : .2f, 0, -.02f),
                        new Vector3(.035f, .8f, .02f), new Color(.10f, .09f, .08f), p5SpentCracks.transform, 0, false);
                    crack.transform.rotation = Quaternion.Euler(0, 0, i == 0 ? 25 : -30);
                }
                p5DisplayedUses = -1;
            }
            p5EnvironmentRoot.gameObject.SetActive(true);
            RefreshP5EnvironmentVisuals();
        }

        /// <summary>Read saved uses after commands/load; visual state cannot restore or spend an impact.</summary>
        public void RefreshP5EnvironmentVisuals()
        {
            if (!p5SpentCracks || Session == null) return;
            var use = State.p5?.impactSurfaces.Find(x => x.id == P5R1ImpactId);
            int used = use?.uses ?? 0;
            if (used == p5DisplayedUses) return;
            p5DisplayedUses = used;
            p5SpentCracks.SetActive(used >= (Session.Config.p5?.impactMaximumUses ?? 2));
        }

        public bool P5LineOfEffect(Vector3 sourceFeet, Vector3 targetFeet) =>
            P5EnvironmentGeometry.HasLineOfEffect(sourceFeet, targetFeet, 1 << WallLayer);
        public int P5CoverArmorBonus(Vector3 sourceFeet, Vector3 targetFeet, bool rangedAttack) =>
            P5EnvironmentEnabled ? P5EnvironmentGeometry.CoverArmorBonus(sourceFeet, targetFeet, rangedAttack, 1 << WallLayer) : 0;
        public P5PushSweep SweepP5Push(Vector3 sourceFeet, Vector3 delta) =>
            P5EnvironmentGeometry.SweepPush(sourceFeet, delta, .36f, 1 << WallLayer);

        public int P5FrontalShieldArmorBonus(ActorState source, ActorState target)
        {
            if (source == null || target?.p5 == null || target.p5.frontalShieldAcBonus <= 0) return 0;
            Vector3 facing = new Vector3(target.lockedX - target.x, 0, target.lockedZ - target.z);
            Vector3 towardSource = BattleGeometry.Ground(Position(source) - Position(target));
            if (facing.sqrMagnitude < .0001f || towardSource.sqrMagnitude < .0001f) return 0;
            return Vector3.Dot(facing.normalized, towardSource.normalized) > 0 ? target.p5.frontalShieldAcBonus : 0;
        }

        void PopulateP5IncomingEvidence(CombatCommand command)
        {
            var source = State.Actor(command.actorId);
            if (source == null) return;
            // The existing scout projectile is parryable. Only its explicitly registered
            // attack pattern is ranged here; parryability, cone shape and range alone do
            // not classify a spell save or an ordinary melee strike as a projectile.
            bool ranged = source.isEnemy && Profile?.EnemyTemplateFor(source.id)?.id == "scout" &&
                LockedDungeonPattern(source)?.id == "scout-shot";
            var contacts = command.kind == CommandKind.IncomingBatch ? command.incomingContacts :
                new List<IncomingContact> { new IncomingContact { targetId = command.targetId,
                    savingThrow = command.kind == CommandKind.IncomingSave } };
            command.p5 = command.p5 ?? new P5CommandData();
            command.p5.targets = new List<P5TargetEvidence>();
            if (contacts == null) return;
            foreach (var contact in contacts)
            {
                var target = State.Actor(contact.targetId);
                if (target == null) continue;
                command.p5.targets.Add(new P5TargetEvidence { targetId = target.id,
                    lineOfSight = P5LineOfEffect(Position(source), Position(target)), inArea = true,
                    coverAcBonus = P5CoverArmorBonus(Position(source), Position(target), ranged && !contact.savingThrow),
                    frontalShieldAcBonus = P5FrontalShieldArmorBonus(source, target), x = target.x, z = target.z });
            }
        }

        /// <summary>Rebuild evidence from command/locked ticket identities, never from whichever actor the UI now selects.</summary>
        public void PopulateP5CommandEvidence(CombatCommand command)
        {
            if (!P5EnvironmentEnabled || command == null) return;
            if (command.kind == CommandKind.IncomingBatch || command.kind == CommandKind.IncomingAttack || command.kind == CommandKind.IncomingSave)
            { PopulateP5IncomingEvidence(command); return; }
            bool queued = command.kind == CommandKind.StartQueued || command.kind == CommandKind.ResolveQueued;
            bool basic = command.kind == CommandKind.StartBasicAttack || command.kind == CommandKind.ResolveBasicAttack || command.kind == CommandKind.BasicAttack;
            if (!queued && !basic && command.kind != CommandKind.ConfirmRoll) return;
            RollTicket ticket = null;
            if (queued)
            {
                var order = State.queue.Find(q => q.id == command.queueId);
                if (order == null) return;
                ticket = State.rolls.Find(t => t.id == order.ticketId);
                if (ticket == null) return;
                command.actorId = ticket.actorId; command.targetId = ticket.targetId; command.actionId = ticket.actionId;
            }
            var actor = State.Actor(command.actorId);
            if (actor == null) return;
            var intent = command.kind == CommandKind.ResolveBasicAttack ? actor.basicAttackIntent : null;
            if (intent != null) command.targetId = intent.targetId;
            var primary = State.Actor(command.targetId);
            var context = ticket?.p5 ?? intent?.p5;
            command.p5 = command.p5 ?? new P5CommandData();
            Vector3 direction = context != null ? new Vector3(context.directionX, 0, context.directionZ) :
                new Vector3(command.p5.directionX, 0, command.p5.directionZ);
            if (direction.sqrMagnitude < .0001f && primary != null)
                direction = BattleGeometry.Ground(Position(primary) - Position(actor)) * (command.actionId == "sneak" ? -1 : 1);
            if (direction.sqrMagnitude < .0001f) direction = Vector3.forward;
            direction.Normalize(); command.p5.directionX = direction.x; command.p5.directionZ = direction.z;
            var action = ticket?.spec ?? Session.Config.p5.actions.Find(a => a.id == command.actionId) ?? Session.Config.Action(command.actionId);
            bool area = !basic && command.actionId == "control";
            bool ranged = basic ? Session.Config.BasicFor(actor).ranged : action != null && action.ranged;
            bool resolve = command.kind == CommandKind.ResolveQueued || command.kind == CommandKind.ResolveBasicAttack;
            var shape = AreaShape.Cone(Position(actor), direction, action?.range ?? 3, Session.Config.p5.thunderAngle * .5f);
            var evidence = new List<P5TargetEvidence>();
            IEnumerable<ActorState> targets = area ? State.actors.Concat(State.enemies) : primary == null ? Array.Empty<ActorState>() : new[] { primary };
            foreach (var target in targets)
            {
                if (!target.Alive || area && target.id == actor.id) continue;
                Vector3 targetPoint = Position(target);
                var item = new P5TargetEvidence { targetId = target.id, lineOfSight = P5LineOfEffect(Position(actor), targetPoint),
                    inArea = !area || BattleGeometry.Contains(shape, targetPoint),
                    coverAcBonus = P5CoverArmorBonus(Position(actor), targetPoint, ranged),
                    // P5.1 conservatively preserves every already-committed Boss action. Parryability is unrelated.
                    allowInterrupt = !(target.isEnemy && target.aiState == 1 &&
                        State.world.strings.TryGetValue("enemyTemplate/" + target.id, out string targetTemplate) && targetTemplate == "boss"),
                    frontalShieldAcBonus = P5FrontalShieldArmorBonus(actor, target), x = target.x, z = target.z };
                float push = context == null ? 0 : Session.P5DisplacementDistance(context, target.id);
                if (resolve && push > 0 && item.inArea && item.lineOfSight)
                {
                    Vector3 pushDirection = area ? BattleGeometry.Ground(targetPoint - Position(actor)).normalized : direction;
                    if (pushDirection.sqrMagnitude < .0001f) pushDirection = direction;
                    var sweep = SweepP5Push(targetPoint, pushDirection * push);
                    item.hasDisplacement = true; item.x = sweep.end.x; item.z = sweep.end.z; item.actualDistance = sweep.actualDistance;
                    item.impactSurfaceId = sweep.impactSurfaceId;
                    item.impactRegistered = !sweep.initialOverlap && sweep.collision != null && !string.IsNullOrEmpty(sweep.impactSurfaceId);
                }
                evidence.Add(item);
            }
            if (resolve && context != null && context.retreatDistance > 0)
            {
                var retreat = SweepP5Push(Position(actor), direction * context.retreatDistance);
                // This caster entry is movement only: never an impact damage request against oneself.
                evidence.Add(new P5TargetEvidence { targetId = actor.id, inArea = false, lineOfSight = true,
                    hasDisplacement = true, x = retreat.end.x, z = retreat.end.z, actualDistance = retreat.actualDistance });
            }
            command.p5.targets = evidence;
            command.lineOfSight = primary != null && P5LineOfEffect(Position(actor), Position(primary));
        }
    }
}
