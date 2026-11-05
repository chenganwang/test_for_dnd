using System;
using UnityEngine;

namespace CombatV051.Runtime
{
    public struct P5PushSweep
    {
        public Vector3 start, end, contactPoint, contactNormal;
        public float actualDistance, distanceAtImpact;
        public Collider collision;
        public string impactSurfaceId;
        public bool initialOverlap;
        public bool HasImpact => collision != null && !string.IsNullOrEmpty(impactSurfaceId) && distanceAtImpact >= .6f;
    }

    /// <summary>Physical evidence only. Does not roll dice, alter actors, consume opportunities or award resources.</summary>
    public static class P5EnvironmentGeometry
    {
        const float Epsilon = .0001f;
        // A bounded nonalloc query. Saturation conservatively blocks LOS rather than losing an opaque wall.
        static readonly RaycastHit[] rayHits = new RaycastHit[64];

        public static bool HasLineOfEffect(Vector3 sourceFeet, Vector3 targetFeet, LayerMask walls)
        {
            Vector3 source = sourceFeet + Vector3.up * .55f, delta = targetFeet - sourceFeet;
            if (delta.sqrMagnitude < Epsilon * Epsilon) return true;
            int count = Physics.RaycastNonAlloc(source, delta.normalized, rayHits, delta.magnitude, walls, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (rayHits[i].collider && !rayHits[i].collider.GetComponent<P5LowCover>()) return false;
            return true;
        }

        public static int CoverArmorBonus(Vector3 sourceFeet, Vector3 targetFeet, bool rangedAttack, LayerMask walls)
        {
            if (!rangedAttack || !HasLineOfEffect(sourceFeet, targetFeet, walls)) return 0;
            Vector3 delta = targetFeet - sourceFeet;
            if (delta.sqrMagnitude < Epsilon * Epsilon) return 0;
            int count = Physics.RaycastNonAlloc(sourceFeet + Vector3.up * .55f, delta.normalized, rayHits,
                delta.magnitude, walls, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (rayHits[i].collider && rayHits[i].collider.GetComponent<P5LowCover>()) return 2;
            return 0;
        }

        /// <summary>Straight forced movement stops at the first surface. It cannot gain impact from wall sliding.</summary>
        public static P5PushSweep SweepPush(Vector3 start, Vector3 requestedDelta, float radius, LayerMask walls,
            float height = 1.6f, float skin = .02f)
        {
            if (!(radius > 0) || !Finite(start) || !Finite(requestedDelta)) throw new ArgumentException("Invalid push geometry.");
            var result = new P5PushSweep { start = start, end = start };
            Vector3 bottom = start + Vector3.up * radius;
            Vector3 top = start + Vector3.up * Mathf.Max(radius, height - radius);
            if (Physics.CheckCapsule(bottom, top, radius, walls, QueryTriggerInteraction.Ignore))
            { result.initialOverlap = true; return result; }
            Vector3 delta = BattleGeometry.Ground(requestedDelta);
            float distance = delta.magnitude;
            if (distance < Epsilon) return result;
            Vector3 direction = delta / distance;
            if (Physics.CapsuleCast(bottom, top, radius, direction, out RaycastHit hit, distance, walls, QueryTriggerInteraction.Ignore))
            {
                result.actualDistance = Mathf.Max(0, Mathf.Min(distance, hit.distance - Mathf.Max(0, skin)));
                result.end = start + direction * result.actualDistance;
                result.collision = hit.collider; result.contactPoint = hit.point; result.contactNormal = hit.normal;
                var surface = hit.collider.GetComponent<P5ImpactSurface>();
                if (surface != null && surface.AcceptsContact(hit.point))
                { result.impactSurfaceId = surface.SurfaceId; result.distanceAtImpact = result.actualDistance; }
            }
            else { result.end = start + delta; result.actualDistance = distance; }
            return result;
        }

        static bool Finite(Vector3 value) => !(float.IsNaN(value.x) || float.IsInfinity(value.x) ||
            float.IsNaN(value.y) || float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z));
    }
}
