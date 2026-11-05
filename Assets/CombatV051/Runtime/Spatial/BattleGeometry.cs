using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    public enum AreaKind { Circle, Cone, Line, Ring }

    [Serializable]
    public struct AreaShape : IEquatable<AreaShape>
    {
        public AreaKind Kind;
        public Vector3 Origin, Direction;
        public float Radius, InnerRadius, Length, Width, HalfAngleDegrees;
        /// <summary>Zero means unlimited height for the P0 flat arena. Positive values constrain target foot height.</summary>
        public float HeightTolerance;

        // Exact value equality: a visual cache must not discard a small authoritative change.
        public bool Equals(AreaShape other) => Origin.Equals(other.Origin) && HeightTolerance.Equals(other.HeightTolerance) && HasSameOutline(other);
        public override bool Equals(object obj) => obj is AreaShape other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ Origin.GetHashCode(); hash = hash * 397 ^ Direction.GetHashCode();
                hash = hash * 397 ^ Radius.GetHashCode(); hash = hash * 397 ^ InnerRadius.GetHashCode();
                hash = hash * 397 ^ Length.GetHashCode(); hash = hash * 397 ^ Width.GetHashCode();
                hash = hash * 397 ^ HalfAngleDegrees.GetHashCode(); return hash * 397 ^ HeightTolerance.GetHashCode();
            }
        }
        // Translation and height tolerance do not change the planar outline. This is not a hit test.
        public bool HasSameOutline(AreaShape other) => Kind == other.Kind && Direction.Equals(other.Direction) &&
            Radius.Equals(other.Radius) && InnerRadius.Equals(other.InnerRadius) && Length.Equals(other.Length) &&
            Width.Equals(other.Width) && HalfAngleDegrees.Equals(other.HalfAngleDegrees);

        public static AreaShape Circle(Vector3 origin, float radius) =>
            new AreaShape { Kind = AreaKind.Circle, Origin = origin, Radius = radius };
        public static AreaShape Cone(Vector3 origin, Vector3 direction, float radius, float halfAngleDegrees) =>
            new AreaShape { Kind = AreaKind.Cone, Origin = origin, Direction = direction, Radius = radius, HalfAngleDegrees = halfAngleDegrees };
        /// <summary>Bounded rectangular line, from origin along direction. No rounded caps.</summary>
        public static AreaShape Line(Vector3 origin, Vector3 direction, float length, float width) =>
            new AreaShape { Kind = AreaKind.Line, Origin = origin, Direction = direction, Length = length, Width = width };
        public static AreaShape Ring(Vector3 origin, float innerRadius, float outerRadius) =>
            new AreaShape { Kind = AreaKind.Ring, Origin = origin, InnerRadius = innerRadius, Radius = outerRadius };
    }

    public static class BattleGeometry
    {
        const float Epsilon = .0001f;
        public static Vector3 Ground(Vector3 value) => new Vector3(value.x, 0, value.z);
        public static float DistanceXZ(Vector3 a, Vector3 b) => Ground(a - b).magnitude;
        public static Vector3 Forward(AreaShape shape)
        {
            Vector3 direction = Ground(shape.Direction);
            return direction.sqrMagnitude > Epsilon * Epsilon ? direction.normalized : Vector3.forward;
        }

        /// <summary>Tests the target's foot point. Rendering and targeting share these exact shape dimensions.</summary>
        public static bool Contains(AreaShape shape, Vector3 point)
        {
            if (shape.HeightTolerance > 0 && Mathf.Abs(point.y - shape.Origin.y) > shape.HeightTolerance + Epsilon) return false;
            Vector3 delta = Ground(point - shape.Origin);
            float distance = delta.magnitude;
            if (shape.Kind == AreaKind.Line)
            {
                if (shape.Length < 0 || shape.Width < 0) return false;
                Vector3 forward = Forward(shape), right = Vector3.Cross(Vector3.up, forward);
                float along = Vector3.Dot(delta, forward), side = Vector3.Dot(delta, right);
                return along >= -Epsilon && along <= shape.Length + Epsilon && Mathf.Abs(side) <= shape.Width * .5f + Epsilon;
            }
            if (shape.Radius < 0 || distance > shape.Radius + Epsilon) return false;
            if (shape.Kind == AreaKind.Circle) return true;
            if (shape.Kind == AreaKind.Ring)
                return shape.InnerRadius >= 0 && shape.InnerRadius <= shape.Radius && distance + Epsilon >= shape.InnerRadius;
            if (shape.Kind != AreaKind.Cone) return false;
            if (distance <= Epsilon) return true;
            float angle = Mathf.Clamp(shape.HalfAngleDegrees, 0, 180);
            return Vector3.Dot(delta / distance, Forward(shape)) + Epsilon >= Mathf.Cos(angle * Mathf.Deg2Rad);
        }

        /// <summary>World-space outlines at the actual shape height. Line and cone include their real closing edges.</summary>
        public static Vector3[][] Outline(AreaShape shape, int segments = 64, float lift = .025f)
        {
            Vector3[] outer = null, inner = null;
            int count = WriteOutline(shape, ref outer, ref inner, segments, lift);
            return count == 2 ? new[] { outer, inner } : new[] { outer };
        }

        /// <summary>Same authoritative outline, with storage reused until the vertex count changes.</summary>
        public static int WriteOutline(AreaShape shape, ref Vector3[] outer, ref Vector3[] inner, int segments = 64, float lift = .025f)
        {
            segments = Mathf.Clamp(segments, 12, 256);
            Vector3 origin = shape.Origin + Vector3.up * lift;
            if (shape.Kind == AreaKind.Line)
            {
                ResizeOutline(ref outer, 4);
                Vector3 forward = Forward(shape) * Mathf.Max(0, shape.Length);
                Vector3 side = Vector3.Cross(Vector3.up, Forward(shape)) * Mathf.Max(0, shape.Width) * .5f;
                outer[0] = origin - side; outer[1] = origin + forward - side;
                outer[2] = origin + forward + side; outer[3] = origin + side;
                return 1;
            }
            if (shape.Kind == AreaKind.Cone)
            {
                float half = Mathf.Clamp(shape.HalfAngleDegrees, 0, 180);
                int arcSegments = Mathf.Max(2, Mathf.CeilToInt(segments * half / 180));
                ResizeOutline(ref outer, arcSegments + 2);
                outer[0] = origin;
                for (int i = 0; i <= arcSegments; i++)
                    outer[i + 1] = origin + Quaternion.AngleAxis(-half + 2 * half * i / arcSegments, Vector3.up) * Forward(shape) * Mathf.Max(0, shape.Radius);
                return 1;
            }
            ResizeOutline(ref outer, segments);
            CircleOutline(origin, Mathf.Max(0, shape.Radius), outer);
            if (shape.Kind != AreaKind.Ring) return 1;
            ResizeOutline(ref inner, segments);
            CircleOutline(origin, Mathf.Clamp(shape.InnerRadius, 0, Mathf.Max(0, shape.Radius)), inner);
            return 2;
        }

        static void ResizeOutline(ref Vector3[] points, int count)
        { if (points == null || points.Length != count) points = new Vector3[count]; }

        static void CircleOutline(Vector3 origin, float radius, Vector3[] vertices)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                float a = i * Mathf.PI * 2 / vertices.Length;
                vertices[i] = origin + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius;
            }
        }

        public static GameObject CreatePreview(AreaShape shape, Transform parent, Color color)
        {
            var root = new GameObject("Area preview " + shape.Kind);
            root.transform.SetParent(parent, false);
            var preview = root.AddComponent<AreaPreview>();
            preview.Color = color;
            preview.SetShape(shape);
            return root;
        }

        /// <summary>Returns final foot position, sweeping a vertical capsule and then sliding once along the hit wall.</summary>
        public static Vector3 MoveAgainstWalls(Vector3 position, Vector3 delta, float radius, LayerMask wallMask,
            float skin = .02f, float height = 1.6f)
        {
            if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
            Vector3 movement = Ground(delta);
            if (movement.sqrMagnitude <= Epsilon * Epsilon) return position;
            float distance = movement.magnitude;
            if (!Cast(position, movement / distance, distance, radius, height, wallMask, out RaycastHit hit)) return position + movement;
            float travel = Mathf.Max(0, hit.distance - Mathf.Max(0, skin));
            Vector3 destination = position + movement / distance * travel;
            Vector3 remaining = movement - movement / distance * travel;
            Vector3 slide = Ground(Vector3.ProjectOnPlane(remaining, hit.normal));
            float slideDistance = slide.magnitude;
            if (slideDistance > Epsilon)
            {
                Vector3 direction = slide / slideDistance;
                if (Cast(destination, direction, slideDistance, radius, height, wallMask, out RaycastHit next))
                    slideDistance = Mathf.Max(0, next.distance - Mathf.Max(0, skin));
                destination += direction * slideDistance;
            }
            return destination;
        }

        static bool Cast(Vector3 feet, Vector3 direction, float distance, float radius, float height,
            LayerMask mask, out RaycastHit hit)
        {
            Vector3 bottom = feet + Vector3.up * radius;
            Vector3 top = feet + Vector3.up * Mathf.Max(radius, height - radius);
            return Physics.CapsuleCast(bottom, top, radius, direction, out hit, distance, mask, QueryTriggerInteraction.Ignore);
        }
    }

    public sealed class AreaPreview : MonoBehaviour
    {
        public Color Color = new Color(1, .5f, .15f, .9f);
        public float Width = .06f;
        public float GroundLift = .025f;
        public AreaShape Shape { get; private set; }
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        Vector3[] outer, inner, worldOuter, worldInner;
        int outlineCount;
        public int GeometryBuildCount { get; private set; }
        public int TranslationUploadCount { get; private set; }

        bool shapeDrawn; Color drawnColor; float drawnWidth,drawnLift;
        public void SetShape(AreaShape shape)
        {
            bool rebuild = !shapeDrawn || !Shape.HasSameOutline(shape) || !drawnLift.Equals(GroundLift);
            bool translated = !shapeDrawn || !Shape.Origin.Equals(shape.Origin);
            bool styleChanged = !shapeDrawn || !drawnColor.Equals(Color) || !drawnWidth.Equals(Width);
            Shape = shape;
            if (!rebuild && !translated && !styleChanged) return;
            shapeDrawn=true;drawnColor=Color;drawnWidth=Width;drawnLift=GroundLift;
            if (rebuild)
            {
                var local = shape; local.Origin = Vector3.zero;
                outlineCount = BattleGeometry.WriteOutline(local, ref outer, ref inner, 64, GroundLift);
                GeometryBuildCount++;
            }
            while (lines.Count < outlineCount)
            {
                var child = new GameObject("Ground outline");
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = true;
                line.alignment = LineAlignment.View;
                line.sharedMaterial = BattleVisual.NewUnlitMaterial(Color);
                child.AddComponent<SpatialResourceOwner>().OwnedMaterial = line.sharedMaterial;
                lines.Add(line);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].gameObject.SetActive(i < outlineCount);
                if (i >= outlineCount) continue;
                lines[i].startWidth = lines[i].endWidth = Mathf.Max(.01f, Width);
                lines[i].startColor = lines[i].endColor = UnityEngine.Color.white;
                if (styleChanged)
                {
                    var material = lines[i].sharedMaterial;
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", Color);
                }
                if (!rebuild && !translated) continue;
                if (i == 0) Upload(lines[i], outer, ref worldOuter, shape.Origin);
                else Upload(lines[i], inner, ref worldInner, shape.Origin);
            }
            if (translated && !rebuild) TranslationUploadCount++;
        }
        static void Upload(LineRenderer line, Vector3[] local, ref Vector3[] world, Vector3 origin)
        {
            if (world == null || world.Length != local.Length) world = new Vector3[local.Length];
            // Keep the public world-space LineRenderer contract; no trig or allocation on movement.
            for (int i = 0; i < local.Length; i++) world[i] = local[i] + origin;
            line.positionCount = world.Length; line.SetPositions(world);
        }
    }
}
