using UnityEngine;
namespace CombatV051.Runtime
{
    /// <summary>Identity and geometry only. Saved combat state owns damage and remaining uses.</summary>
    public sealed class P5ImpactSurface : MonoBehaviour
    {
        public string SurfaceId;
        public bool RestrictContactSpan;
        public Vector3 ContactCenter, ContactTangent = Vector3.right;
        public float ContactHalfWidth = 1;
        public bool AcceptsContact(Vector3 point) => enabled && !string.IsNullOrEmpty(SurfaceId) &&
            (!RestrictContactSpan || Mathf.Abs(Vector3.Dot(point - ContactCenter, ContactTangent.normalized)) <= ContactHalfWidth);
    }
}
