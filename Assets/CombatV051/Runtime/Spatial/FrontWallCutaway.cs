using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Uniform room slice: camera-facing perimeter walls and roofs hide visually, retaining collision.</summary>
    public sealed class FrontWallCutaway : MonoBehaviour
    {
        public Vector3 RoomCenter;
        public Renderer[] RoofRenderers = Array.Empty<Renderer>();
        public float FrontThreshold = .05f;
        readonly Dictionary<Renderer, bool> originals = new Dictionary<Renderer, bool>();
        Renderer[] walls = Array.Empty<Renderer>();
        Camera view;

        public void Configure(Renderer[] roomWalls, Camera camera, Vector3 roomCenter)
        {
            Restore();
            originals.Clear();
            view = camera ? camera : throw new ArgumentNullException(nameof(camera));
            walls = roomWalls ?? Array.Empty<Renderer>();
            RoomCenter = roomCenter;
            Remember(walls); Remember(RoofRenderers);
            Refresh();
        }

        public void Configure(Renderer[] roomWalls, Camera camera)
        {
            Vector3 center = Vector3.zero;
            int count = 0;
            if (roomWalls != null) foreach (var wall in roomWalls) if (wall) { center += wall.bounds.center; count++; }
            Configure(roomWalls, camera, count > 0 ? center / count : Vector3.zero);
        }

        public void Refresh()
        {
            if (!view) return;
            Remember(walls); Remember(RoofRenderers);
            Vector3 toCamera = -BattleGeometry.Ground(view.transform.forward).normalized;
            foreach (Renderer wall in walls)
            {
                if (!wall) continue;
                Vector3 offset = BattleGeometry.Ground(wall.bounds.center - RoomCenter);
                wall.enabled = originals[wall] && Vector3.Dot(offset, toCamera) <= FrontThreshold;
            }
            if (RoofRenderers != null) foreach (Renderer roof in RoofRenderers) if (roof) roof.enabled = false;
        }

        void Remember(Renderer[] renderers)
        {
            if (renderers == null) return;
            foreach (var renderer in renderers) if (renderer && !originals.ContainsKey(renderer)) originals.Add(renderer, renderer.enabled);
        }

        void Restore()
        {
            foreach (var item in originals) if (item.Key) item.Key.enabled = item.Value;
        }

        void LateUpdate() => Refresh();
        void OnEnable() => Refresh();
        void OnDisable() => Restore();
        void OnDestroy() => Restore();
    }
}
