using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Flat-faced, three-shade original geometry. Every method creates visual meshes, never colliders.</summary>
    public sealed class ArtMeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
        void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int shade = -1)
        {
            int start = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (shade < 0) shade = normal.y > .4f ? 2 : Vector3.Dot(normal, new Vector3(-.6f, .5f, -.6f)) > 0 ? 1 : 0;
            triangles[shade].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        public void Box(Vector3 center, Vector3 size, Quaternion rotation, int shade = -1)
        {
            Vector3 h = size * .5f;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++) p[i] = center + rotation * new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
            Face(p[0], p[2], p[3], p[1], shade); Face(p[4], p[5], p[7], p[6], shade);
            Face(p[0], p[4], p[6], p[2], shade); Face(p[1], p[3], p[7], p[5], shade);
            Face(p[2], p[6], p[7], p[3], shade); Face(p[0], p[1], p[5], p[4], shade);
        }
        public void Frustum(Vector3 center, float bottom, float top, float height, int sides = 6, float yaw = 0)
        {
            sides = Mathf.Clamp(sides, 3, 16);
            for (int i = 0; i < sides; i++)
            {
                float a = (yaw + i * 360f / sides) * Mathf.Deg2Rad, b = (yaw + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                Vector3 ba = center + new Vector3(Mathf.Cos(a) * bottom, -height * .5f, Mathf.Sin(a) * bottom);
                Vector3 bb = center + new Vector3(Mathf.Cos(b) * bottom, -height * .5f, Mathf.Sin(b) * bottom);
                Vector3 ta = center + new Vector3(Mathf.Cos(a) * top, height * .5f, Mathf.Sin(a) * top);
                Vector3 tb = center + new Vector3(Mathf.Cos(b) * top, height * .5f, Mathf.Sin(b) * top);
                Face(ba, ta, tb, bb); Face(center + Vector3.up * height * .5f, tb, ta, ta, 2);
                Face(center - Vector3.up * height * .5f, ba, bb, bb, 0);
            }
        }
        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = "P3 original " + name, subMeshCount = 3 };
            mesh.SetVertices(vertices);
            for (int i = 0; i < 3; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public static Mesh BoxMesh(Vector3 size) { var b = new ArtMeshBuilder(); b.Box(Vector3.zero, size, Quaternion.identity); return b.Build("beveled-language block"); }
        public static Mesh FrustumMesh(float bottom, float top, float height, int sides = 6) { var b = new ArtMeshBuilder(); b.Frustum(Vector3.zero, bottom, top, height, sides); return b.Build("faceted prism"); }
        public static Mesh Ring(float inner, float outer, int count = 48)
        {
            var v = new Vector3[count * 2]; var t = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2 / count; var d = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                v[i * 2] = d * inner; v[i * 2 + 1] = d * outer;
                int n = ((i + 1) % count) * 2, k = i * 6;
                t[k] = i * 2; t[k + 1] = n; t[k + 2] = n + 1; t[k + 3] = i * 2; t[k + 4] = n + 1; t[k + 5] = i * 2 + 1;
            }
            var mesh = new Mesh { name = "P3 original hollow ground ring", vertices = v, triangles = t }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
