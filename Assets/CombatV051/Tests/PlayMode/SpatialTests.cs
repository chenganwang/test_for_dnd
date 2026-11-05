using System.Collections;
using System.Linq;
using CombatV051.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CombatV051.Tests
{
    /// <summary>Real Camera/Renderer/Physics tests. Execution evidence is supplied by the shared Unity test run.</summary>
    public sealed class SpatialTests
    {
        GameObject root;
        Camera camera;
        FixedBattleCamera rig;
        Vector3 active, centroid;
        bool frozen, focus;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            root = new GameObject("Spatial test fixture");
            var child = new GameObject("Spatial camera"); child.transform.SetParent(root.transform);
            camera = child.AddComponent<Camera>(); camera.pixelRect = new Rect(0, 0, 1280, 720);
            rig = child.AddComponent<FixedBattleCamera>();
            active = centroid = Vector3.zero; frozen = focus = false;
            rig.Initialize(camera, () => active, () => centroid, () => frozen, () => focus);
            rig.enabled = false; // Tests advance the actual component with explicit unscaled deltas.
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            if (root) Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScreenAxesRemainIntuitiveAcrossWorldPositionsAndFocus()
        {
            foreach (Vector3 location in new[] { Vector3.zero, new Vector3(24, 0, 18), new Vector3(-12, 0, -8) })
            {
                active = centroid = location;
                rig.Step(5);
                Vector3 screenOrigin = camera.WorldToScreenPoint(location);
                Vector3 screenUp = camera.WorldToScreenPoint(location + rig.ScreenMove(Vector2.up));
                Vector3 screenRight = camera.WorldToScreenPoint(location + rig.ScreenMove(Vector2.right));
                Assert.That(screenUp.y, Is.GreaterThan(screenOrigin.y));
                Assert.That(screenUp.x, Is.EqualTo(screenOrigin.x).Within(.002f));
                Assert.That(screenRight.x, Is.GreaterThan(screenOrigin.x));
                Assert.That(screenRight.y, Is.EqualTo(screenOrigin.y).Within(.002f));
                Assert.That(rig.ScreenMove(Vector2.one).magnitude, Is.EqualTo(1).Within(.0001f));
                Assert.That(rig.ScreenMove(Vector2.one).y, Is.Zero);
            }
            Quaternion orientation = camera.transform.rotation;
            focus = true; rig.Step(5);
            Assert.That(Quaternion.Angle(orientation, camera.transform.rotation), Is.LessThan(.001f));
            Assert.That(camera.orthographicSize, Is.EqualTo(rig.BaseSize * 1.1f).Within(.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GroundRayReachesTheActualXZPointAndRejectsMissingSurface()
        {
            Vector3 expected = new Vector3(3.25f, 0, -2.5f);
            Vector3 screen = camera.WorldToScreenPoint(expected);
            Assert.That(rig.TryGroundAim(screen, out Vector3 hit), Is.True);
            Assert.That(Vector3.Distance(expected, hit), Is.LessThan(.002f));
            rig.UseGroundColliders = true; rig.GroundSurfaceMask = 1 << 27;
            Assert.That(rig.TryGroundAim(screen, out _), Is.False);
            var ground = BattleVisual.Box("Aimable floor", new Vector3(0, -.1f, 0), new Vector3(30, .2f, 30), Color.gray, root.transform, 27);
            Physics.SyncTransforms();
            Assert.That(rig.TryGroundAim(screen, out hit), Is.True);
            Assert.That(Vector3.Distance(expected, hit), Is.LessThan(.002f));
            Assert.That(ground.GetComponent<Collider>().enabled, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DisplayShakeDoesNotMoveMouseAimProjection()
        {
            var pointer=new Vector2(580,340);
            Assert.That(rig.TryGroundAim(pointer,out var before),Is.True);
            rig.FeedbackOffset=new Vector3(.09f,.05f,-.06f);rig.Step(.016f);
            Assert.That(rig.TryGroundAim(pointer,out var after),Is.True);
            Assert.That(Vector3.Distance(before,after),Is.LessThan(.002f));
            Assert.That(camera.transform.position,Is.Not.EqualTo(-camera.transform.forward*rig.Distance));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RollFreezeLocksTheEntireCameraPoseDespitePendingFollowAndZoom()
        {
            active = centroid = new Vector3(4, 0, 3); focus = true;
            rig.Step(.01f);
            Vector3 before = camera.transform.position; Quaternion rotation = camera.transform.rotation;
            float size = camera.orthographicSize;
            frozen = true; active = centroid = new Vector3(20, 0, 20); focus = false;
            for (int i = 0; i < 40; i++) rig.Step(.02f);
            Assert.That(camera.transform.position, Is.EqualTo(before));
            Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
            Assert.That(camera.orthographicSize, Is.EqualTo(size));
            frozen = false; rig.Step(.02f);
            Assert.That(Vector3.Distance(before, camera.transform.position), Is.GreaterThan(.001f));
            Assert.That(Quaternion.Angle(rotation, camera.transform.rotation), Is.LessThan(.001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PartyCompositionUsesSeventyThirtyAndSwitchesSmoothlyWithoutYawChange()
        {
            active = new Vector3(2, 0, 0); centroid = new Vector3(0, 0, 0);
            rig.Step(5);
            Vector3 target = camera.transform.position + camera.transform.forward * rig.Distance;
            Assert.That(target.x, Is.EqualTo(1.4f).Within(.001f));
            Vector3 before = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            active = new Vector3(-2, 0, 0); rig.Step(.01f);
            float traveled = Vector3.Distance(before, camera.transform.position);
            Assert.That(traveled, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(camera.transform.rotation, Is.EqualTo(rotation));
            centroid = new Vector3(100, 0, 100); rig.Step(5);
            target = camera.transform.position + camera.transform.forward * rig.Distance;
            Assert.That(Vector3.Distance(target, active), Is.LessThanOrEqualTo(rig.MaxPartyOffset * rig.PartyWeight + .001f));
            yield return null;
        }

        [Test]
        public void CircleConeLineAndRingUseWorldGeometryIncludingTheirVisibleBoundaries()
        {
            Vector3 origin = new Vector3(7, 0, -3);
            var shapes = new[]
            {
                AreaShape.Circle(origin, 4),
                AreaShape.Cone(origin, new Vector3(1, 0, 1), 4, 30),
                AreaShape.Line(origin, new Vector3(1, 0, 1), 5, 1),
                AreaShape.Ring(origin, 2, 4)
            };
            foreach (AreaShape shape in shapes)
                foreach (Vector3[] loop in BattleGeometry.Outline(shape))
                    foreach (Vector3 point in loop)
                        Assert.That(BattleGeometry.Contains(shape, point), Is.True, shape.Kind + " visible edge " + point);

            Assert.That(BattleGeometry.Contains(shapes[0], origin + Vector3.forward * 4.01f), Is.False);
            Assert.That(BattleGeometry.Contains(shapes[1], origin - BattleGeometry.Forward(shapes[1])), Is.False);
            Assert.That(BattleGeometry.Contains(shapes[2], origin - BattleGeometry.Forward(shapes[2]) * .01f), Is.False);
            Assert.That(BattleGeometry.Contains(shapes[2], origin + BattleGeometry.Forward(shapes[2]) * 5.01f), Is.False);
            Assert.That(BattleGeometry.Contains(shapes[3], origin), Is.False);
            Assert.That(BattleGeometry.Contains(shapes[3], origin + Vector3.right * 1.99f), Is.False);
            AreaShape elevated = shapes[0]; elevated.HeightTolerance = .5f;
            Assert.That(BattleGeometry.Contains(elevated, origin + Vector3.up * .6f), Is.False);
            Assert.That(BattleGeometry.DistanceXZ(origin, origin + Vector3.up * 100), Is.Zero);
        }

        [UnityTest]
        public IEnumerator PreviewRendersTheSameXZOutlinesAndRingHasAnInnerSafeBoundary()
        {
            AreaShape shape = AreaShape.Ring(new Vector3(2, 0, 3), 2, 4);
            GameObject preview = BattleGeometry.CreatePreview(shape, root.transform, Color.red);
            LineRenderer[] lines = preview.GetComponentsInChildren<LineRenderer>();
            Assert.That(lines.Length, Is.EqualTo(2));
            foreach (LineRenderer line in lines)
            {
                Assert.That(line.loop && line.useWorldSpace, Is.True);
                for (int i = 0; i < line.positionCount; i++)
                {
                    Vector3 point = line.GetPosition(i);
                    Assert.That(point.y, Is.EqualTo(.025f).Within(.0001f));
                    Assert.That(BattleGeometry.Contains(shape, point), Is.True);
                }
            }
            preview.GetComponent<AreaPreview>().SetShape(AreaShape.Circle(Vector3.zero, 3));
            Assert.That(preview.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ThreeDimensionalSweepPreventsFastWallTunnelingAndAllowsSliding()
        {
            BattleVisual.Box("Solid wall", new Vector3(0, 1.5f, 0), new Vector3(.2f, 3, 12), Color.gray, root.transform, 28);
            Physics.SyncTransforms();
            Vector3 start = new Vector3(-2, 0, 0);
            Vector3 result = BattleGeometry.MoveAgainstWalls(start, new Vector3(50, 0, 0), .3f, 1 << 28);
            Assert.That(result.x, Is.LessThanOrEqualTo(-.4f));
            Assert.That(result.x, Is.GreaterThan(-.5f));
            result = BattleGeometry.MoveAgainstWalls(start, new Vector3(5, 0, 2), .3f, 1 << 28);
            Assert.That(result.x, Is.LessThanOrEqualTo(-.4f));
            Assert.That(result.z, Is.GreaterThan(1.9f));
            Assert.That(result.y, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FrontWallCutawayRetainsCollidersAndRestoresRendererState()
        {
            var walls = new[]
            {
                BattleVisual.Box("West", new Vector3(-5, 1, 0), new Vector3(.2f, 2, 10), Color.gray, root.transform, 28),
                BattleVisual.Box("East", new Vector3(5, 1, 0), new Vector3(.2f, 2, 10), Color.gray, root.transform, 28),
                BattleVisual.Box("South", new Vector3(0, 1, -5), new Vector3(10, 2, .2f), Color.gray, root.transform, 28),
                BattleVisual.Box("North", new Vector3(0, 1, 5), new Vector3(10, 2, .2f), Color.gray, root.transform, 28)
            };
            var roof = BattleVisual.Box("Roof", new Vector3(0, 3, 0), new Vector3(10, .2f, 10), Color.gray, root.transform, 28);
            var cutaway = root.AddComponent<FrontWallCutaway>();
            cutaway.RoofRenderers = new[] { roof.GetComponent<Renderer>() };
            cutaway.Configure(walls.Select(wall => wall.GetComponent<Renderer>()).ToArray(), camera, Vector3.zero);
            Assert.That(walls[0].GetComponent<Renderer>().enabled, Is.False);
            Assert.That(walls[2].GetComponent<Renderer>().enabled, Is.False);
            Assert.That(walls[1].GetComponent<Renderer>().enabled, Is.True);
            Assert.That(walls[3].GetComponent<Renderer>().enabled, Is.True);
            Assert.That(roof.GetComponent<Renderer>().enabled, Is.False);
            foreach (GameObject wall in walls) Assert.That(wall.GetComponent<Collider>().enabled, Is.True);
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(new Vector3(-8, 1, 0), Vector3.right, 5, 1 << 28), Is.True);
            cutaway.enabled = false;
            Assert.That(walls.All(wall => wall.GetComponent<Renderer>().enabled), Is.True);
            Assert.That(roof.GetComponent<Renderer>().enabled, Is.True);
            yield return null;
        }
    }
}
