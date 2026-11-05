using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    public enum ArtTone { Environment, Actor, Glow, Danger }

    /// <summary>One palette owner per world. Original meshes/materials only; never owns imported assets.</summary>
    public sealed class ArtResources : MonoBehaviour
    {
        sealed class Swatch { public Material material; public Color color; public ArtTone tone; }
        readonly Dictionary<string, Swatch> swatches = new Dictionary<string, Swatch>();
        readonly List<Mesh> meshes = new List<Mesh>();
        bool focused, frozen;
        public int MaterialCount => swatches.Count;
        public int MeshCount => meshes.Count;
        public static readonly Color Stone = new Color(.25f, .35f, .39f);
        public static readonly Color Floor = new Color(.15f, .235f, .26f);
        public static readonly Color Metal = new Color(.45f, .61f, .65f);
        public static readonly Color Dark = new Color(.065f, .11f, .16f);
        public static readonly Color Teal = new Color(.13f, .79f, .77f);
        public static readonly Color Blue = new Color(.30f, .53f, .93f);
        public static readonly Color Gold = new Color(1f, .70f, .27f);
        public static readonly Color Red = new Color(.82f, .22f, .18f);
        public static readonly Color Danger = new Color(1f, .30f, .13f);

        public Material Get(Color color, ArtTone tone)
        {
            string key = ColorUtility.ToHtmlStringRGBA(color) + "/" + tone;
            if (!swatches.TryGetValue(key, out var swatch))
            {
                swatch = new Swatch { color = color, tone = tone, material = BattleVisual.NewUnlitMaterial(color) };
                swatch.material.name = "P3 original palette " + key;
                swatches.Add(key, swatch); Apply(swatch);
            }
            return swatch.material;
        }
        public Material[] Facets(Color color, ArtTone tone) => new[] { Get(Shade(color, .62f), tone), Get(color, tone), Get(Shade(color, 1.20f), tone) };
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
        public Mesh Own(Mesh mesh) { meshes.Add(mesh); return mesh; }
        public void Release(Mesh mesh) { if(mesh&&meshes.Remove(mesh))Destroy(mesh); }
        public void SetMode(bool focus, bool freeze)
        {
            if (focused == focus && frozen == freeze) return;
            focused = focus; frozen = freeze;
            foreach (var item in swatches.Values) Apply(item);
        }
        void Apply(Swatch swatch)
        {
            Color c = swatch.color;
            if (swatch.tone == ArtTone.Environment && (focused || frozen))
            {
                Color grey = new Color(c.grayscale, c.grayscale, c.grayscale, c.a);
                c = Color.Lerp(c, grey, .28f); c = Shade(c, frozen ? .52f : .74f);
            }
            else if (swatch.tone == ArtTone.Actor && frozen) c = Shade(c, .90f);
            if (swatch.material.HasProperty("_BaseColor")) swatch.material.SetColor("_BaseColor", c);
            if (swatch.material.HasProperty("_Color")) swatch.material.SetColor("_Color", c);
        }
        public GameObject MeshObject(string title, Transform parent, Mesh mesh, Color color, ArtTone tone)
        {
            var obj = new GameObject(title, typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<MeshFilter>().sharedMesh = Own(mesh);
            var renderer = obj.GetComponent<MeshRenderer>(); renderer.sharedMaterials = mesh.subMeshCount == 3 ? Facets(color, tone) : new[] { Get(color, tone) };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            return obj;
        }
        void OnDestroy()
        {
            foreach (var mesh in meshes) if (mesh) Destroy(mesh);
            foreach (var swatch in swatches.Values) if (swatch.material) Destroy(swatch.material);
            meshes.Clear(); swatches.Clear();
        }
    }
}
