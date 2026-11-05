using System;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>One fixed orientation for realtime, Focus and dice decisions. World coordinates are XZ/Y.</summary>
    public sealed class FixedBattleCamera : MonoBehaviour
    {
        public float Yaw = 45f;
        public float Pitch = 55f;
        public float BaseSize = 10f;
        public float FocusZoom = 1.10f;
        public float FollowSpeed = 6f;
        public float ZoomSpeed = 5f;
        public float Distance = 26f;
        public float PartyWeight = .30f;
        public float MaxPartyOffset = 3f;
        public float DeadZone = .05f;
        public float GroundHeight;
        public Vector3 FeedbackOffset;
        public bool UseGroundColliders;
        public LayerMask GroundSurfaceMask;
        public Camera View { get; private set; }

        Func<Vector3> activePosition, partyCentroid;
        Func<bool> frozen, focused;
        Vector3 center;
        Vector3 appliedFeedbackOffset;
        bool initialized;

        public void Initialize(Camera camera, Func<Vector3> active, Func<Vector3> centroid,
            Func<bool> freeze, Func<bool> focus)
        {
            View = camera ? camera : throw new ArgumentNullException(nameof(camera));
            activePosition = active ?? throw new ArgumentNullException(nameof(active));
            partyCentroid = centroid ?? active;
            frozen = freeze ?? (() => false);
            focused = focus ?? (() => false);
            View.orthographic = true;
            center = TargetCenter();
            View.orthographicSize = DesiredSize();
            SetPose();
            initialized = true;
        }

        public void Step(float unscaledDelta)
        {
            if (!initialized || !View || frozen()) return;
            float dt = Mathf.Max(0, unscaledDelta);
            Vector3 target = TargetCenter();
            if ((target - center).sqrMagnitude > DeadZone * DeadZone)
                center = Vector3.Lerp(center, target, 1 - Mathf.Exp(-Mathf.Max(0, FollowSpeed) * dt));
            View.orthographicSize = Mathf.Lerp(View.orthographicSize, DesiredSize(),
                1 - Mathf.Exp(-Mathf.Max(0, ZoomSpeed) * dt));
            SetPose();
        }

        /// <summary>Restore the saved battlefield view before a loaded RollFreeze can lock it.</summary>
        public void RestoreView(Vector3 position, Quaternion rotation, float size)
        {
            if (!initialized || !View) throw new InvalidOperationException("Initialize the camera before restoring it.");
            if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || !Finite(size) || size <= 0)
                throw new ArgumentException("Invalid saved camera pose.");
            Yaw = rotation.eulerAngles.y; Pitch = rotation.eulerAngles.x;
            center = position + rotation * Vector3.forward * Mathf.Max(1, Distance);
            View.orthographicSize = size;
            View.transform.SetPositionAndRotation(position, rotation);
            appliedFeedbackOffset = Vector3.zero;
        }

        Vector3 TargetCenter()
        {
            Vector3 active = activePosition();
            Vector3 offset = partyCentroid() - active;
            offset = Vector3.ClampMagnitude(offset, Mathf.Max(0, MaxPartyOffset));
            return active + offset * Mathf.Clamp01(PartyWeight);
        }

        float DesiredSize() => Mathf.Max(1, BaseSize) * (focused() ? Mathf.Clamp(FocusZoom, 1.05f, 1.15f) : 1);

        void SetPose()
        {
            Quaternion rotation = Quaternion.Euler(Mathf.Clamp(Pitch, 10, 85), Yaw, 0);
            View.transform.SetPositionAndRotation(center - rotation * Vector3.forward * Mathf.Max(1, Distance) + FeedbackOffset, rotation);
            appliedFeedbackOffset = FeedbackOffset;
        }

        /// <summary>Input Y follows screen up; Input X follows screen right. Diagonal input is bounded.</summary>
        public Vector3 ScreenMove(Vector2 input)
        {
            Quaternion rotation = View ? View.transform.rotation : Quaternion.Euler(Pitch, Yaw, 0);
            Vector3 right = BattleGeometry.Ground(rotation * Vector3.right).normalized;
            Vector3 forward = BattleGeometry.Ground(rotation * Vector3.forward).normalized;
            return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1);
        }

        public bool TryGroundAim(Vector2 screen, out Vector3 point)
        {
            point = default;
            if (!initialized || !View || !Finite(screen.x) || !Finite(screen.y)) return false;
            Ray ray = View.ScreenPointToRay(screen);
            // Shake is display-only. Aim uses the same unshaken battlefield projection.
            ray.origin -= appliedFeedbackOffset;
            if (UseGroundColliders)
            {
                if (!Physics.Raycast(ray, out RaycastHit hit, 1000, GroundSurfaceMask, QueryTriggerInteraction.Ignore)) return false;
                point = hit.point;
                return true;
            }
            Plane ground = new Plane(Vector3.up, new Vector3(0, GroundHeight, 0));
            if (!ground.Raycast(ray, out float distance) || distance < 0) return false;
            point = ray.GetPoint(distance);
            return true;
        }

        public Vector3 GroundAim(Vector2 screen)
        {
            if (TryGroundAim(screen, out Vector3 point)) return point;
            throw new InvalidOperationException("The pointer has no valid ground aim point.");
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void LateUpdate() => Step(Time.unscaledDeltaTime);
    }
}
