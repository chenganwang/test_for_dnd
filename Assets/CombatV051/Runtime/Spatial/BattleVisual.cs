using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CombatV051.Runtime
{
    /// <summary>Original geometric placeholders. No downloaded assets, textures, or complex shaders.</summary>
    public static class BattleVisual
    {
        public static Material NewUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (!shader) throw new InvalidOperationException("A basic unlit shader must be included in the build.");
            var material = new Material(shader) { name = "Original geometry " + ColorUtility.ToHtmlStringRGBA(color) };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (color.a < .999f)
            {
                material.SetOverrideTag("RenderType", "Transparent");
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", (int)CullMode.Off);
            return material;
        }

        public static GameObject Box(string name, Vector3 center, Vector3 size, Color color,
            Transform parent = null, int layer = 0, bool collider = true)
        {
            var result = Primitive(name, PrimitiveType.Cube, center, size, color, parent, layer, collider);
            return result;
        }

        public static GameObject Capsule(string name, Vector3 feet, float radius, float height, Color color,
            Transform parent = null, int layer = 0, bool collider = false)
        {
            height = Mathf.Max(radius * 2, height);
            return Primitive(name, PrimitiveType.Capsule, feet + Vector3.up * height * .5f,
                new Vector3(radius * 2, height * .5f, radius * 2), color, parent, layer, collider);
        }

        public static GameObject Sphere(string name, Vector3 center, float radius, Color color,
            Transform parent = null, int layer = 0, bool collider = false) =>
            Primitive(name, PrimitiveType.Sphere, center, Vector3.one * radius * 2, color, parent, layer, collider);

        public static GameObject Disc(string name, Vector3 center, float radius, Color color, Transform parent = null)
        {
            const int segments = 64;
            var vertices = new Vector3[segments + 1];
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * Mathf.Max(0, radius);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }
            var mesh = new Mesh { name = "Original XZ disc" };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var result = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            result.transform.SetParent(parent, false); result.transform.position = center;
            result.GetComponent<MeshFilter>().sharedMesh = mesh;
            result.GetComponent<MeshRenderer>().sharedMaterial = NewUnlitMaterial(color);
            var owner = result.AddComponent<SpatialResourceOwner>();
            owner.OwnedMesh = mesh; owner.OwnedMaterial = result.GetComponent<MeshRenderer>().sharedMaterial;
            return result;
        }

        static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color,
            Transform parent, int layer, bool collider)
        {
            var result = GameObject.CreatePrimitive(type);
            result.name = name; result.layer = layer;
            result.transform.SetParent(parent, false); result.transform.position = position; result.transform.localScale = scale;
            if (!collider)
            {
                var body = result.GetComponent<Collider>();
                body.enabled = false;
                UnityEngine.Object.Destroy(body);
            }
            var renderer = result.GetComponent<Renderer>();
            renderer.sharedMaterial = NewUnlitMaterial(color);
            result.AddComponent<SpatialResourceOwner>().OwnedMaterial = renderer.sharedMaterial;
            return result;
        }
    }

    /// <summary>Destroys only generated resources assigned here, never imported meshes or shared author assets.</summary>
    public sealed class SpatialResourceOwner : MonoBehaviour
    {
        public Material OwnedMaterial;
        public Mesh OwnedMesh;
        void OnDestroy()
        {
            if (OwnedMaterial) Destroy(OwnedMaterial);
            if (OwnedMesh) Destroy(OwnedMesh);
        }
    }
}
