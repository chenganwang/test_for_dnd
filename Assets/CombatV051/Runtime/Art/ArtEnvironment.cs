using System;
using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    [DefaultExecutionOrder(120)]
    public sealed class ArtSurfaceVisibility : MonoBehaviour
    {
        public Renderer Source;
        public Renderer[] Decorations = Array.Empty<Renderer>();
        public void Refresh()
        {
            if(!Source)return;
            foreach(var r in Decorations)if(r)r.enabled=Source.enabled;
        }
        void LateUpdate()=>Refresh();
    }

    /// <summary>All decoration lies on existing surfaces or within existing blocker volumes. No physics or navigation writes.</summary>
    public sealed class ArtEnvironment : MonoBehaviour
    {
        readonly HashSet<Renderer> surfaces=new HashSet<Renderer>();
        readonly Dictionary<string,ArtLandmark> landmarks=new Dictionary<string,ArtLandmark>();
        ArtResources art;
        public int DecoratedSurfaceCount=>surfaces.Count;
        public IReadOnlyDictionary<string,ArtLandmark> Landmarks=>landmarks;
        public void Initialize(ArtResources resources){art=resources;}
        public void ResetMapPresentation()
        {
            foreach(var marker in landmarks.Values)if(marker){marker.gameObject.SetActive(false);Destroy(marker.gameObject);}
            landmarks.Clear();surfaces.Clear();
        }
        public void Decorate(Transform geometry)
        {
            if(!geometry)return;
            foreach(var collider in geometry.GetComponentsInChildren<BoxCollider>(true))
            {
                var renderer=collider.GetComponent<MeshRenderer>();
                if(!renderer||surfaces.Contains(renderer))continue;
                surfaces.Add(renderer);
                string title=collider.name;
                bool floor=title.IndexOf("floor",StringComparison.OrdinalIgnoreCase)>=0||title=="Ground";
                bool cover=title.IndexOf("cover",StringComparison.OrdinalIgnoreCase)>=0;
                bool door=title.StartsWith("Door ",StringComparison.Ordinal);
                if(!floor&&!cover&&!door&&collider.gameObject.layer!=8)continue;
                var root=new GameObject("P3 surface art").transform;root.SetParent(collider.transform,false);
                Vector3 scaling=collider.transform.lossyScale;
                root.localScale=new Vector3(1/Mathf.Abs(scaling.x),1/Mathf.Abs(scaling.y),1/Mathf.Abs(scaling.z));
                Vector3 size=Vector3.Scale(collider.size,new Vector3(Mathf.Abs(scaling.x),Mathf.Abs(scaling.y),Mathf.Abs(scaling.z)));
                root.localPosition=collider.center;
                renderer.sharedMaterial=art.Get(floor?ArtResources.Floor:cover?new Color(.26f,.34f,.34f):door?new Color(.37f,.30f,.20f):ArtResources.Stone,ArtTone.Environment);
                if(floor)BuildFloor(root,size);
                else if(cover)BuildCover(root,size);
                else if(door)BuildDoor(root,size);
                else BuildWall(root,size);
                var visibility=root.gameObject.AddComponent<ArtSurfaceVisibility>();visibility.Source=renderer;
                visibility.Decorations=root.GetComponentsInChildren<Renderer>(true);visibility.Refresh();
            }
        }
        void BuildFloor(Transform root,Vector3 size)
        {
            var b=new ArtMeshBuilder();int nx=Mathf.Max(1,Mathf.RoundToInt(size.x/2.2f)),nz=Mathf.Max(1,Mathf.RoundToInt(size.z/2.2f));
            float x=size.x/nx,z=size.z/nz;
            for(int i=0;i<nx;i++)for(int j=0;j<nz;j++)
                b.Box(new Vector3(-size.x*.5f+(i+.5f)*x,size.y*.5f+.004f,-size.z*.5f+(j+.5f)*z),new Vector3(x-.045f,.008f,z-.045f),Quaternion.identity,(i+j*3)%5==0?1:0);
            var slabs=art.MeshObject("Low contrast stone floor slabs",root,b.Build("floor slabs"),new Color(.25f,.35f,.36f),ArtTone.Environment);
            // Floors use a quieter range than weapon/body facets so telegraphs and silhouettes carry the contrast.
            slabs.GetComponent<MeshRenderer>().sharedMaterials=new[]{
                art.Get(new Color(.17f,.255f,.28f),ArtTone.Environment),
                art.Get(new Color(.185f,.27f,.295f),ArtTone.Environment),
                art.Get(new Color(.20f,.29f,.315f),ArtTone.Environment)};
            // Flush inlay, not raised steps: actor feet and the ground aiming plane stay at their verified height.
            if(size.x>15&&size.z>15)
            {
                var ring=art.MeshObject("Quiet floor medallion",root,ArtMeshBuilder.Ring(2.35f,2.41f),new Color(.26f,.42f,.43f),ArtTone.Environment);
                ring.transform.localPosition=Vector3.up*(size.y*.5f+.018f);
            }
        }
        void BuildWall(Transform root,Vector3 size)
        {
            bool horizontal=size.x>=size.z;float length=horizontal?size.x:size.z,thickness=horizontal?size.z:size.x;
            var b=new ArtMeshBuilder();int count=Mathf.Max(1,Mathf.CeilToInt(length/2.8f));float w=length/count;
            for(int row=0;row<3;row++)for(int i=0;i<count;i++)
            {
                float p=-length*.5f+(i+.5f)*w;
                Vector3 center=horizontal?new Vector3(p,-size.y*.5f+(row+.5f)*size.y/3,0):new Vector3(0,-size.y*.5f+(row+.5f)*size.y/3,p);
                Vector3 block=horizontal?new Vector3(w-.035f,size.y/3-.035f,thickness+.022f):new Vector3(thickness+.022f,size.y/3-.035f,w-.035f);
                b.Box(center,block,Quaternion.identity);
            }
            // Capitals and pilasters stay within the same wall width; none narrows a doorway.
            var cap=horizontal?new Vector3(length,.16f,thickness+.04f):new Vector3(thickness+.04f,.16f,length);
            b.Box(new Vector3(0,size.y*.5f-.10f,0),cap,Quaternion.identity);
            b.Box(new Vector3(0,-size.y*.5f+.12f,0),cap,Quaternion.identity);
            art.MeshObject("Stone courses and cornice",root,b.Build("wall courses"),ArtResources.Stone,ArtTone.Environment);
        }
        void BuildDoor(Transform root,Vector3 size)
        {
            bool xWidth=size.x>size.z;float width=xWidth?size.x:size.z;
            var b=new ArtMeshBuilder();int n=Mathf.Max(3,Mathf.RoundToInt(width/.5f));
            for(int i=0;i<n;i++)
            {
                float p=-width*.5f+(i+.5f)*width/n;
                b.Box(xWidth?new Vector3(p,0,0):new Vector3(0,0,p),xWidth?new Vector3(.09f,size.y-.14f,size.z+.035f):new Vector3(size.x+.035f,size.y-.14f,.09f),Quaternion.identity);
            }
            for(int sign=-1;sign<=1;sign+=2)
                b.Box(new Vector3(0,sign*size.y*.33f,0),new Vector3(size.x+.035f,.11f,size.z+.035f),Quaternion.identity);
            art.MeshObject("Iron gate lattice",root,b.Build("gate bars"),ArtResources.Metal,ArtTone.Environment);
            var seal=art.MeshObject("Warm gate lock",root,ArtMeshBuilder.FrustumMesh(.23f,.14f,.22f,6),ArtResources.Gold,ArtTone.Glow);
            seal.transform.localPosition=new Vector3(0,.16f,0);seal.transform.localRotation=Quaternion.Euler(90,0,0);
        }
        void BuildCover(Transform root,Vector3 size)
        {
            var wood=new ArtMeshBuilder();var bands=new ArtMeshBuilder();int n=Mathf.Max(1,Mathf.RoundToInt(size.x/1.1f));float width=size.x/n;
            for(int i=0;i<n;i++)
            {
                Vector3 pos=new Vector3(-size.x*.5f+(i+.5f)*width,0,0);
                wood.Box(pos,new Vector3(width-.045f,size.y-.025f,size.z-.02f),Quaternion.identity);
                for(int sign=-1;sign<=1;sign+=2)
                {
                    bands.Box(pos+new Vector3(sign*width*.35f,0,0),new Vector3(.06f,size.y+.014f,size.z+.014f),Quaternion.identity);
                    bands.Box(pos+new Vector3(0,sign*size.y*.34f,0),new Vector3(width-.025f,.055f,size.z+.02f),Quaternion.identity);
                }
            }
            art.MeshObject("Storage crate faces",root,wood.Build("crate panels"),new Color(.25f,.34f,.33f),ArtTone.Environment);
            art.MeshObject("Storage crate bands",root,bands.Build("crate hoops"),ArtResources.Metal,ArtTone.Environment);
            // One inset barrel end identifies storage; it remains inside the old cover's physical envelope.
            var barrel=art.MeshObject("Inset barrel end",root,ArtMeshBuilder.FrustumMesh(.27f,.27f,.12f,8),new Color(.30f,.40f,.36f),ArtTone.Environment);
            barrel.transform.localPosition=new Vector3(-size.x*.5f+.34f,0,size.z*.5f-.055f);barrel.transform.localRotation=Quaternion.Euler(90,0,0);
        }
        public void AddLandmark(string id,Vector3 point,string type)
        {
            if(landmarks.ContainsKey(id))return;
            var obj=new GameObject("P3 interaction "+id);obj.transform.SetParent(transform,false);obj.transform.position=point;
            var marker=obj.AddComponent<ArtLandmark>();marker.Build(art,type);landmarks.Add(id,marker);
        }
        public void RefreshLandmarks(string nearby,bool frozen,float simulationTime)
        {foreach(var pair in landmarks)pair.Value.Refresh(pair.Key==nearby,frozen,simulationTime);}
    }

    public sealed class ArtLandmark : MonoBehaviour
    {
        Transform outline,crystal;
        bool observed;
        public bool Highlighted=>outline&&outline.gameObject.activeSelf;
        public void Build(ArtResources art,string type)
        {
            // These are light-emitting floor mechanisms, not tall solid props in an otherwise walkable cell.
            var baseMesh=art.MeshObject("Inset interaction dais",transform,ArtMeshBuilder.FrustumMesh(.68f,.59f,.11f,8),ArtResources.Stone,ArtTone.Environment);
            baseMesh.transform.localPosition=Vector3.up*.06f;
            var rune=art.MeshObject("Golden mechanism inlay",transform,ArtMeshBuilder.Ring(.38f,.43f,6),ArtResources.Gold,ArtTone.Glow);rune.transform.localPosition=Vector3.up*.125f;
            crystal=art.MeshObject(type+" hologlyph",transform,ArtMeshBuilder.FrustumMesh(type=="rest" ? .30f : .20f,.02f,type=="rest" ? .16f : .40f,type=="gate"?4:6),new Color(1,.72f,.30f,.68f),ArtTone.Glow).transform;
            crystal.localPosition=Vector3.up*.56f;
            outline=art.MeshObject("Nearby interaction outline",transform,ArtMeshBuilder.Ring(.76f,.84f),ArtResources.Gold,ArtTone.Glow).transform;
            outline.localPosition=Vector3.up*.027f;outline.gameObject.SetActive(false);
            if(type=="rest")
            {
                var b=new ArtMeshBuilder();b.Box(new Vector3(0,.18f,0),new Vector3(.55f,.045f,.08f),Quaternion.identity);b.Box(new Vector3(0,.18f,0),new Vector3(.08f,.045f,.55f),Quaternion.identity);
                art.MeshObject("Rest cross sigil",transform,b.Build("rest sigil"),ArtResources.Gold,ArtTone.Glow);
            }
        }
        public void Refresh(bool nearby,bool frozen,float simulationTime)
        {
            if(frozen&&observed)return;observed=true;
            outline.gameObject.SetActive(nearby);crystal.localRotation=Quaternion.Euler(0,simulationTime*22,0);
            crystal.localPosition=Vector3.up*(.56f+Mathf.Sin(simulationTime*2)*.045f);
        }
    }
}
