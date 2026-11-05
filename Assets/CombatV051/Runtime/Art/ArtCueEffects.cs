using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Short original world-space accents. Their only clock is the world's simulation clock.</summary>
    public sealed class ArtCueEffects : MonoBehaviour
    {
        sealed class Cue {public string key;public Transform root;public float start,duration,scale=1;public bool rise;public MeshFilter[] meshes;}
        readonly List<Cue> cues=new List<Cue>();
        readonly Dictionary<string,Stack<Cue>> pools=new Dictionary<string,Stack<Cue>>();
        ArtResources art;
        public int ActiveCount=>cues.Count;
        public void Initialize(ArtResources resources)
        {
            if(art)return;
            art=resources;
            // These are the existing cue names, not new gameplay content. Prepare their
            // meshes/materials before the first attack/skill and return them to the pool.
            foreach(var name in new[]{"hit","block","parry","dodge","perfect-dodge","momentum","dice-earned","break","telegraph","attack","spell","door","interact","critical","contact","deflect","miss","smite","smite-full"})
            {for(int i=0;i<4;i++)Play(name,Vector3.zero,0);Clear();}
        }
        public void Play(string name,Vector3 feet,float now,float scale=1)
        {
            if(cues.Count>=48){Release(cues[0]);cues.RemoveAt(0);}
            if(pools.TryGetValue(name,out var pool)&&pool.Count>0){var reused=pool.Pop();reused.root.position=feet;reused.scale=scale;reused.root.localScale=Vector3.one*scale;reused.root.gameObject.SetActive(true);reused.start=now;cues.Add(reused);return;}
            bool smite=name=="smite"||name=="smite-full";
            Color color=smite?new Color(1f,.81f,.35f):name=="parry"||name=="dice-earned"||name=="break"||name=="critical"?ArtResources.Gold:name=="hit"?ArtResources.Danger:name=="deflect"?ArtResources.Metal:name=="miss"?ArtResources.Dark:ArtResources.Teal;
            bool strong=smite||name=="parry"||name=="break"||name=="perfect-dodge"||name=="dice-earned"||name=="critical";
            var root=new GameObject("P3 cue "+name).transform;root.SetParent(transform,false);root.position=feet;
            var ring=art.MeshObject("Impact ring",root,ArtMeshBuilder.Ring(strong ? .37f : .24f,strong ? .47f : .31f,24),color,ArtTone.Glow);
            ring.transform.localPosition=Vector3.up*.07f;
            if(smite)
            {
                var blade=new ArtMeshBuilder();
                blade.Box(new Vector3(0,1.35f,0),new Vector3(name=="smite-full"?.26f:.13f,2.7f,.08f),Quaternion.Euler(0,35,-17));
                blade.Box(new Vector3(0,.72f,0),new Vector3(1.7f,.07f,.11f),Quaternion.Euler(0,35,24));
                art.MeshObject("Oathlight narrow blade and column",root,blade.Build("smite glyph"),color,ArtTone.Glow);
            }
            if(strong)
            {
                var b=new ArtMeshBuilder();
                for(int i=0;i<6;i++)
                {
                    float angle=i*60*Mathf.Deg2Rad;
                    b.Box(new Vector3(Mathf.Sin(angle)*.48f,.45f,Mathf.Cos(angle)*.48f),new Vector3(.045f,.29f,.045f),Quaternion.Euler(0,-i*60,28));
                }
                art.MeshObject("Radial accent",root,b.Build("six impact strokes"),color,ArtTone.Glow);
            }
            cues.Add(new Cue{key=name,root=root,start=now,scale=scale,duration=strong ? .50f : .28f,rise=name=="dice-earned"||name=="momentum",meshes=root.GetComponentsInChildren<MeshFilter>()});
        }
        public void Refresh(float simulationTime,bool frozen)
        {
            if(frozen)return;
            for(int i=cues.Count-1;i>=0;i--)
            {
                var cue=cues[i];float progress=Mathf.Clamp01((simulationTime-cue.start)/cue.duration);
                if(progress>=1||!cue.root){Release(cue);cues.RemoveAt(i);continue;}
                cue.root.localScale=Vector3.one*cue.scale*(1+progress*(cue.rise ? .45f : 1.2f));
            }
        }
        void Release(Cue cue){if(!cue.root)return;if(!pools.TryGetValue(cue.key,out var pool)){pool=new Stack<Cue>();pools[cue.key]=pool;}cue.root.gameObject.SetActive(false);pool.Push(cue);}
        void OnDestroy(){foreach(var cue in cues)foreach(var mesh in cue.meshes)if(mesh&&art)art.Release(mesh.sharedMesh);foreach(var pool in pools.Values)foreach(var cue in pool)foreach(var mesh in cue.meshes)if(mesh&&art)art.Release(mesh.sharedMesh);}
        public void Clear(){foreach(var cue in cues)Release(cue);cues.Clear();}
    }
}
