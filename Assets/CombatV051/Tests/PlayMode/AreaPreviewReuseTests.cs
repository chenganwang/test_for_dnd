using System.Collections;
using System.Linq;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    /// <summary>Actual Renderer/Mesh projection checks; not a frame-rate or zero-GC claim.</summary>
    public sealed class AreaPreviewReuseTests
    {
        GameObject root;
        ArtResources palette;

        [SetUp]
        public void Setup()
        {
            root=new GameObject("Area preview reuse fixture");
            palette=root.AddComponent<ArtResources>();
            root.transform.position=new Vector3(-3,2,7);
            root.transform.rotation=Quaternion.Euler(11,37,4);
            root.transform.localScale=new Vector3(.75f,1.4f,2);
        }
        [UnityTearDown]
        public IEnumerator Teardown(){if(root)Object.Destroy(root);yield return null;}

        AreaPreview Create(AreaShape shape,out AreaFillOverlay fill)
        {
            var obj=BattleGeometry.CreatePreview(shape,root.transform,Color.red);
            var preview=obj.GetComponent<AreaPreview>();
            fill=obj.AddComponent<AreaFillOverlay>();fill.Initialize(preview,palette,true);
            return preview;
        }
        static MeshFilter Filter(AreaFillOverlay fill)=>fill.GetComponentsInChildren<MeshFilter>().Single(f=>f.sharedMesh==fill.FillMesh);
        static Vector3[] WorldVertices(AreaFillOverlay fill)
        {
            var filter=Filter(fill);
            return fill.FillMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
        }
        static void AssertTranslated(Vector3[] before,Vector3[] after,Vector3 delta,string label)
        {
            Assert.That(after.Length,Is.EqualTo(before.Length),label);
            for(int i=0;i<before.Length;i++)
                Assert.That(Vector3.Distance(after[i],before[i]+delta),Is.LessThan(.00008f),label+" vertex "+i);
        }
        static void AssertFillInside(AreaFillOverlay fill,AreaShape shape)
        {
            var world=WorldVertices(fill);var triangles=fill.FillMesh.triangles;
            Assert.That(triangles.Length,Is.GreaterThan(0));
            for(int i=0;i<triangles.Length;i+=3)
            {
                var center=(world[triangles[i]]+world[triangles[i+1]]+world[triangles[i+2]])/3;
                Assert.That(BattleGeometry.Contains(shape,center),Is.True,shape.Kind+" filled outside its unchanged hit geometry at "+center);
                if(shape.Kind==AreaKind.Ring)
                    Assert.That(BattleGeometry.DistanceXZ(shape.Origin,center),Is.GreaterThan(shape.InnerRadius),"Ring hole must remain unfilled.");
            }
        }

        [UnityTest]
        public IEnumerator AllFourShapesTranslateUnderTransformedParentsWithoutRebuildingGeometry()
        {
            Vector3 origin=new Vector3(3,1.5f,-2);
            foreach(var initial in new[]{AreaShape.Circle(origin,4),AreaShape.Cone(origin,new Vector3(1,0,1),4,40),
                AreaShape.Line(origin,new Vector3(1,0,1),6,2),AreaShape.Ring(origin,2,4)})
            {
                var preview=Create(initial,out var fill);
                var mesh=fill.FillMesh;var initialVertices=WorldVertices(fill);
                var lines=preview.GetComponentsInChildren<LineRenderer>();
                var initialLines=lines.Select(line=>Enumerable.Range(0,line.positionCount).Select(line.GetPosition).ToArray()).ToArray();
                var materialIds=lines.Select(line=>line.sharedMaterial.GetInstanceID()).ToArray();
                int rendererCount=preview.GetComponentsInChildren<Renderer>(true).Length;
                for(int step=1;step<=64;step++)
                {
                    Vector3 delta=new Vector3(step*.037f,step*.003f,-step*.029f);
                    var moved=initial;moved.Origin=origin+delta;
                    preview.SetShape(moved);fill.Refresh(false);
                    Assert.That(preview.Shape.Equals(moved),Is.True);
                    Assert.That(fill.DrawnShape.Equals(moved),Is.True);
                    Assert.That(fill.FillMesh,Is.SameAs(mesh));
                    Assert.That(preview.GeometryBuildCount,Is.EqualTo(1),"Movement must not recalculate the planar shape.");
                    Assert.That(fill.MeshBuildCount,Is.EqualTo(1),"Movement must not upload a replacement mesh.");
                    AssertTranslated(initialVertices,WorldVertices(fill),delta,"fill "+initial.Kind);
                    for(int l=0;l<lines.Length;l++)
                    {
                        Assert.That(lines[l].useWorldSpace&&lines[l].loop,Is.True,"Existing snapshot contract stays world-space.");
                        var points=Enumerable.Range(0,lines[l].positionCount).Select(lines[l].GetPosition).ToArray();
                        AssertTranslated(initialLines[l],points,delta,"outline "+initial.Kind);
                        Assert.That(lines[l].sharedMaterial.GetInstanceID(),Is.EqualTo(materialIds[l]));
                    }
                    AssertFillInside(fill,moved);
                }
                Assert.That(preview.TranslationUploadCount,Is.EqualTo(64));
                Assert.That(fill.TranslationCount,Is.EqualTo(64));
                Assert.That(preview.GetComponentsInChildren<Renderer>(true).Length,Is.EqualTo(rendererCount));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SizeDirectionAndTopologyChangesUpdateTheActualWorldGeometryAndKeepRingHole()
        {
            var origin=new Vector3(8,.5f,3);
            var preview=Create(AreaShape.Circle(origin,2),out var fill);
            var shapes=new[]{AreaShape.Circle(origin,5),AreaShape.Cone(origin,Vector3.right,4,35),
                AreaShape.Cone(origin,Vector3.back,6,60),AreaShape.Line(origin,Vector3.left,7,3),
                AreaShape.Ring(origin,1.5f,5),AreaShape.Circle(origin,3),AreaShape.Ring(origin,2,4)};
            int expectedBuilds=1;
            foreach(var shape in shapes)
            {
                preview.SetShape(shape);fill.Refresh(false);expectedBuilds++;
                Assert.That(preview.GeometryBuildCount,Is.EqualTo(expectedBuilds));
                Assert.That(fill.MeshBuildCount,Is.EqualTo(expectedBuilds));
                Assert.That(preview.GetComponentsInChildren<LineRenderer>().Length,Is.EqualTo(shape.Kind==AreaKind.Ring?2:1));
                AssertFillInside(fill,shape);
                foreach(var line in preview.GetComponentsInChildren<LineRenderer>())
                    for(int i=0;i<line.positionCount;i++)
                    {
                        var point=line.GetPosition(i);
                        Assert.That(BattleGeometry.Contains(shape,point),Is.True,"Changed outline must use the real hit geometry.");
                        Assert.That(point.y,Is.EqualTo(origin.y+preview.GroundLift).Within(.00001f));
                    }
                if(shape.Kind==AreaKind.Circle)
                    foreach(var vertex in WorldVertices(fill))
                        Assert.That(BattleGeometry.DistanceXZ(origin,vertex),Is.EqualTo(shape.Radius).Within(.00008f));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FrozenFillDefersMotionAndGeometryChangesUntilRefreshResumes()
        {
            var original=AreaShape.Ring(new Vector3(2,0,3),1,3);
            var preview=Create(original,out var fill);
            var mesh=fill.FillMesh;var before=WorldVertices(fill);
            var moved=AreaShape.Cone(new Vector3(9,1,-4),Vector3.left,5,30);
            // Even a caller publishing a new source shape cannot move the frozen fill.
            preview.SetShape(moved);
            for(int i=0;i<30;i++)fill.Refresh(true);
            Assert.That(fill.DrawnShape.Equals(original),Is.True);
            Assert.That(fill.MeshBuildCount,Is.EqualTo(1));
            Assert.That(fill.TranslationCount,Is.Zero);
            AssertTranslated(before,WorldVertices(fill),Vector3.zero,"Frozen fill");
            yield return null;
            fill.Refresh(false);
            Assert.That(fill.FillMesh,Is.SameAs(mesh));
            Assert.That(fill.DrawnShape.Equals(moved),Is.True);
            Assert.That(fill.MeshBuildCount,Is.EqualTo(2));
            AssertFillInside(fill,moved);
        }

        [UnityTest]
        public IEnumerator HeightOnlyChangesRetainAuthoritativeShapeAndLiftChangesUpdateGeometry()
        {
            var shape=AreaShape.Circle(new Vector3(4,2,8),3);
            var equal=shape;
            Assert.That(shape.Equals(equal),Is.True);Assert.That(shape.GetHashCode(),Is.EqualTo(equal.GetHashCode()));
            var preview=Create(shape,out var fill);
            shape.HeightTolerance=.1f;
            Assert.That(shape.Equals(equal),Is.False,"Typed equality must include height; planar cache equality is deliberately separate.");
            preview.SetShape(shape);fill.Refresh(false);
            Assert.That(preview.Shape.Equals(shape)&&fill.DrawnShape.Equals(shape),Is.True);
            Assert.That(BattleGeometry.Contains(preview.Shape,shape.Origin+Vector3.up*.2f),Is.False);
            Assert.That(preview.GeometryBuildCount,Is.EqualTo(1));Assert.That(fill.MeshBuildCount,Is.EqualTo(1));
            preview.GroundLift=.12f;preview.SetShape(shape);fill.Refresh(false);
            Assert.That(preview.GeometryBuildCount,Is.EqualTo(2));Assert.That(fill.MeshBuildCount,Is.EqualTo(2));
            foreach(var vertex in WorldVertices(fill))
                Assert.That(vertex.y,Is.EqualTo(shape.Origin.y+.12f*.55f).Within(.00008f));
            int build=fill.MeshBuildCount;
            for(int i=0;i<30;i++){preview.SetShape(shape);fill.Refresh(false);}
            Assert.That(fill.MeshBuildCount,Is.EqualTo(build));
            yield return null;
        }
    }
}
