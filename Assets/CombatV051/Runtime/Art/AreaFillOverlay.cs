using System.Collections.Generic;
using UnityEngine;

namespace CombatV051.Runtime
{
    /// <summary>Transparent fill projected from the existing AreaShape; no second range definition or hit test.</summary>
    public sealed class AreaFillOverlay : MonoBehaviour
    {
        AreaPreview source;
        Mesh mesh;
        Transform meshTransform;
        AreaShape drawn;
        bool initialized;
        float drawnLift;
        Matrix4x4 drawnBasis;
        readonly FillBuffers buffers = new FillBuffers();
        public Mesh FillMesh=>mesh;
        public AreaShape DrawnShape=>drawn;
        public int MeshBuildCount { get; private set; }
        public int TranslationCount { get; private set; }
        public void Initialize(AreaPreview preview,ArtResources resources,bool danger)
        {
            source=preview;
            var obj=new GameObject("P3 exact range fill",typeof(MeshFilter),typeof(MeshRenderer));obj.transform.SetParent(transform,false);
            meshTransform=obj.transform;
            mesh=new Mesh{name="P3 exact AreaShape fill"};obj.GetComponent<MeshFilter>().sharedMesh=mesh;
            Color color=danger?new Color(1,.24f,.08f,.14f):new Color(.23f,.80f,.87f,.045f);
            obj.GetComponent<MeshRenderer>().sharedMaterial=resources.Get(color,danger?ArtTone.Danger:ArtTone.Glow);
            obj.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            obj.GetComponent<MeshRenderer>().receiveShadows=false;
            Refresh(false);
        }
        public void Refresh(bool frozen)
        {
            if(!source||!mesh||(frozen&&initialized))return;
            var shape=source.Shape;
            float lift=source.GroundLift*.55f;
            var basis=transform.worldToLocalMatrix;
            bool rebuild=!initialized||!shape.HasSameOutline(drawn)||!drawnLift.Equals(lift)||!SameBasis(basis,drawnBasis);
            bool translated=initialized&&!meshTransform.position.Equals(shape.Origin);
            if(rebuild)
            {
                var local=shape;local.Origin=Vector3.zero;
                BuildInto(mesh,local,lift,basis,buffers);MeshBuildCount++;
            }
            // The mesh child owns only the translation. Its parent may have any invertible basis.
            // Do not move AreaPreview itself: its world-space lines and saved shape remain authoritative.
            if(rebuild||translated)meshTransform.position=shape.Origin;
            if(translated&&!rebuild)TranslationCount++;
            drawn=shape;drawnLift=lift;drawnBasis=basis;initialized=true;
        }
        public static Mesh CreateMesh(AreaShape shape,float lift=.015f)
        {
            var result=new Mesh{name="P3 AreaShape verification mesh"};
            BuildInto(result,shape,lift,Matrix4x4.identity,new FillBuffers());return result;
        }
        static bool SameBasis(Matrix4x4 a,Matrix4x4 b)=>
            a.m00.Equals(b.m00)&&a.m01.Equals(b.m01)&&a.m02.Equals(b.m02)&&
            a.m10.Equals(b.m10)&&a.m11.Equals(b.m11)&&a.m12.Equals(b.m12)&&
            a.m20.Equals(b.m20)&&a.m21.Equals(b.m21)&&a.m22.Equals(b.m22);
        sealed class FillBuffers
        {
            public Vector3[] outer,inner;
            public readonly List<Vector3> vertices=new List<Vector3>(128);
            public readonly List<int> triangles=new List<int>(384);
        }
        static void BuildInto(Mesh result,AreaShape shape,float lift,Matrix4x4 basis,FillBuffers buffers)
        {
            BattleGeometry.WriteOutline(shape,ref buffers.outer,ref buffers.inner,64,lift);
            var vertices=buffers.vertices;var triangles=buffers.triangles;
            vertices.Clear();triangles.Clear();
            if(shape.Kind==AreaKind.Ring)
            {
                int count=buffers.outer.Length;
                for(int i=0;i<count;i++){vertices.Add(buffers.outer[i]);vertices.Add(buffers.inner[i]);}
                for(int i=0;i<count;i++)
                {
                    int a=i*2,b=((i+1)%count)*2;
                    triangles.Add(a);triangles.Add(b);triangles.Add(b+1);
                    triangles.Add(a);triangles.Add(b+1);triangles.Add(a+1);
                }
            }
            else
            {
                vertices.AddRange(buffers.outer);
                for(int i=1;i+1<vertices.Count;i++){triangles.Add(0);triangles.Add(i);triangles.Add(i+1);}
            }
            for(int i=0;i<vertices.Count;i++)vertices[i]=basis.MultiplyVector(vertices[i]);
            result.Clear();result.SetVertices(vertices);result.SetTriangles(triangles,0);result.RecalculateNormals();result.RecalculateBounds();
        }
        void OnDestroy(){if(mesh)Destroy(mesh);}
    }
}
