using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;
using RayBork.Tests.Assets.Procedural;

namespace RayBork.Tests;

public sealed class CollisionTests {
    private static readonly BoundingBox _unitBox = new(new Vector3(-1f), new Vector3(1f));

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckSpheresDetectsOverlapSeparationTouchingAndContainment() {
        Assert.True(Collision.CheckSpheres(Vector3.Zero, 1f, new Vector3(1.5f, 0f, 0f), 1f));
        Assert.False(Collision.CheckSpheres(Vector3.Zero, 1f, new Vector3(3f, 0f, 0f), 1f));
        Assert.True(Collision.CheckSpheres(Vector3.Zero, 1f, new Vector3(2f, 0f, 0f), 1f));
        Assert.True(Collision.CheckSpheres(Vector3.Zero, 5f, new Vector3(1f, 0f, 0f), 0.5f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckSpheresRejectsNegativeRadiiAndAcceptsZeroRadius() {
        Assert.False(Collision.CheckSpheres(Vector3.Zero, -1f, new Vector3(0.5f, 0f, 0f), 2f));
        Assert.False(Collision.CheckSpheres(Vector3.Zero, -1f, new Vector3(0.5f, 0f, 0f), -1f));
        Assert.True(Collision.CheckSpheres(Vector3.Zero, 0f, Vector3.Zero, 0f));
        Assert.False(Collision.CheckSpheres(Vector3.Zero, 0f, new Vector3(1f, 0f, 0f), 0f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckBoxesDetectsOverlapSeparationTouchingAndContainment() {
        Assert.True(Collision.CheckBoxes(_unitBox, new BoundingBox(Vector3.Zero, new Vector3(2f))));
        Assert.False(Collision.CheckBoxes(_unitBox, new BoundingBox(new Vector3(2f, -1f, -1f), new Vector3(3f, 1f, 1f))));
        Assert.True(Collision.CheckBoxes(_unitBox, new BoundingBox(new Vector3(1f, -1f, -1f), new Vector3(3f, 1f, 1f))));
        Assert.True(Collision.CheckBoxes(_unitBox, new BoundingBox(new Vector3(-0.5f), new Vector3(0.5f))));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckBoxSphereDetectsIntersectionSeparationTouchingAndContainment() {
        BoundingBox box = new(Vector3.Zero, Vector3.One);

        Assert.True(Collision.CheckBoxSphere(box, new Vector3(0.5f, 0.5f, 0.5f), 0.25f));
        Assert.True(Collision.CheckBoxSphere(box, new Vector3(2f, 0.5f, 0.5f), 1.5f));
        Assert.False(Collision.CheckBoxSphere(box, new Vector3(3f, 0.5f, 0.5f), 1f));
        Assert.True(Collision.CheckBoxSphere(box, new Vector3(2f, 0.5f, 0.5f), 1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckBoxSphereRejectsANegativeRadiusAndTreatsZeroRadiusAsAPoint() {
        BoundingBox box = new(Vector3.Zero, Vector3.One);

        Assert.False(Collision.CheckBoxSphere(box, new Vector3(2f, 0.5f, 0.5f), -1f));
        Assert.False(Collision.CheckBoxSphere(box, new Vector3(0.5f, 0.5f, 0.5f), -5f));
        Assert.True(Collision.CheckBoxSphere(box, new Vector3(0.5f, 0.5f, 0.5f), 0f));
        Assert.True(Collision.CheckBoxSphere(box, Vector3.Zero, 0f));
        Assert.False(Collision.CheckBoxSphere(box, new Vector3(2f, 0.5f, 0.5f), 0f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CheckHelpersReportNoCollisionForNaNInput() {
        Assert.False(Collision.CheckSpheres(new Vector3(float.NaN, 0f, 0f), 1f, Vector3.Zero, 1f));
        Assert.False(Collision.CheckSpheres(Vector3.Zero, float.NaN, Vector3.Zero, 1f));
        Assert.False(Collision.CheckBoxes(
            new BoundingBox(new Vector3(float.NaN), Vector3.One),
            new BoundingBox(Vector3.Zero, Vector3.One)));
        Assert.False(Collision.CheckBoxSphere(
            new BoundingBox(Vector3.Zero, Vector3.One),
            new Vector3(float.NaN, 0f, 0f),
            1f));
        Assert.False(Collision.CheckBoxSphere(
            new BoundingBox(Vector3.Zero, Vector3.One),
            new Vector3(0.5f),
            float.NaN));
        Assert.False(Collision.CheckBoxSphere(
            new BoundingBox(new Vector3(float.NaN), Vector3.One),
            new Vector3(0.5f),
            1f));
        Assert.False(Collision.CheckBoxSphere(
            new BoundingBox(Vector3.Zero, new Vector3(float.NaN)),
            new Vector3(0.5f),
            1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereHitsTheFrontSurface() {
        RayCollision hit = Collision.GetRaySphere(
            new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ),
            Vector3.Zero,
            1f);

        AssertHit(hit, 4f, new Vector3(0f, 0f, 1f), Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereReturnsTheExitPointWhenTheOriginIsInside() {
        RayCollision hit = Collision.GetRaySphere(
            new Ray(Vector3.Zero, -Vector3.UnitZ),
            Vector3.Zero,
            1f);

        AssertHit(hit, 1f, new Vector3(0f, 0f, -1f), -Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereCountsTangencyAsAHit() {
        RayCollision hit = Collision.GetRaySphere(
            new Ray(new Vector3(1f, 0f, 5f), -Vector3.UnitZ),
            Vector3.Zero,
            1f);

        AssertHit(hit, 5f, new Vector3(1f, 0f, 0f), Vector3.UnitX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereMissesWhenTheSphereIsBehindOrBesideTheRay() {
        AssertMiss(Collision.GetRaySphere(
            new Ray(new Vector3(0f, 0f, 5f), Vector3.UnitZ),
            Vector3.Zero,
            1f));
        AssertMiss(Collision.GetRaySphere(
            new Ray(new Vector3(5f, 0f, 0f), -Vector3.UnitZ),
            Vector3.Zero,
            1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereNormalizesTheDirectionAndReportsTheWorldDistance() {
        Ray ray = new(new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, -2f));
        RayCollision hit = Collision.GetRaySphere(ray, Vector3.Zero, 1f);

        Assert.Equal(1f, ray.Direction.Length(), 1e-6f);
        AssertVector(-Vector3.UnitZ, ray.Direction);
        AssertHit(hit, 4f, new Vector3(0f, 0f, 1f), Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRaySphereMissesForAnInvalidRadiusOrNonFiniteInput() {
        Ray ray = new(new Vector3(0f, 0f, 5f), -Vector3.UnitZ);

        AssertMiss(Collision.GetRaySphere(ray, Vector3.Zero, 0f));
        AssertMiss(Collision.GetRaySphere(ray, Vector3.Zero, -1f));
        AssertMiss(Collision.GetRaySphere(ray, Vector3.Zero, float.NaN));
        AssertMiss(Collision.GetRaySphere(ray, Vector3.Zero, float.PositiveInfinity));
        AssertMiss(Collision.GetRaySphere(
            ray,
            new Vector3(float.PositiveInfinity, 0f, 0f),
            1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxHitsEveryFaceWithItsOutwardNormal() {
        AssertBoxFaceHit(new Vector3(5f, 0f, 0f), -Vector3.UnitX, 4f, new Vector3(1f, 0f, 0f), Vector3.UnitX);
        AssertBoxFaceHit(new Vector3(-5f, 0f, 0f), Vector3.UnitX, 4f, new Vector3(-1f, 0f, 0f), -Vector3.UnitX);
        AssertBoxFaceHit(new Vector3(0f, 5f, 0f), -Vector3.UnitY, 4f, new Vector3(0f, 1f, 0f), Vector3.UnitY);
        AssertBoxFaceHit(new Vector3(0f, -5f, 0f), Vector3.UnitY, 4f, new Vector3(0f, -1f, 0f), -Vector3.UnitY);
        AssertBoxFaceHit(new Vector3(0f, 0f, 5f), -Vector3.UnitZ, 4f, new Vector3(0f, 0f, 1f), Vector3.UnitZ);
        AssertBoxFaceHit(new Vector3(0f, 0f, -5f), Vector3.UnitZ, 4f, new Vector3(0f, 0f, -1f), -Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxReturnsTheExitFaceWhenTheOriginIsInside() {
        RayCollision hit = Collision.GetRayBox(new Ray(Vector3.Zero, Vector3.UnitX), _unitBox);

        AssertHit(hit, 1f, new Vector3(1f, 0f, 0f), Vector3.UnitX);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxHandlesParallelRaysInsideAndOutsideASlab() {
        RayCollision inside = Collision.GetRayBox(new Ray(Vector3.Zero, Vector3.UnitY), _unitBox);
        AssertHit(inside, 1f, new Vector3(0f, 1f, 0f), Vector3.UnitY);

        AssertMiss(Collision.GetRayBox(new Ray(new Vector3(2f, 0f, 0f), Vector3.UnitY), _unitBox));
        AssertMiss(Collision.GetRayBox(new Ray(new Vector3(0f, 0f, 2f), Vector3.UnitX), _unitBox));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxMissesARayThatPassesBesideTheBox() {
        AssertMiss(Collision.GetRayBox(new Ray(new Vector3(0f, 3f, 5f), -Vector3.UnitZ), _unitBox));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxMissesWhenTheBoxIsBehindTheRay() {
        AssertMiss(Collision.GetRayBox(new Ray(new Vector3(0f, 0f, 5f), Vector3.UnitZ), _unitBox));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxMissesForAnInvertedBox() {
        BoundingBox inverted = new(new Vector3(1f), new Vector3(-1f));

        AssertMiss(Collision.GetRayBox(new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ), inverted));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayBoxCountsTouchingTheSurfaceAtZeroAsAHit() {
        RayCollision hit = Collision.GetRayBox(new Ray(new Vector3(0f, 0f, 1f), -Vector3.UnitZ), _unitBox);

        AssertHit(hit, 0f, new Vector3(0f, 0f, 1f), Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayTriangleHitsBothSidesWithTheGeometricNormal() {
        RayCollision front = Collision.GetRayTriangle(
            new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY);
        AssertHit(front, 1f, new Vector3(0.25f, 0.25f, 0f), Vector3.UnitZ);

        RayCollision back = Collision.GetRayTriangle(
            new Ray(new Vector3(0.25f, 0.25f, -1f), Vector3.UnitZ),
            Vector3.Zero,
            Vector3.UnitX,
            Vector3.UnitY);
        AssertHit(back, 1f, new Vector3(0.25f, 0.25f, 0f), Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayTriangleMissesADegenerateTriangle() {
        Ray ray = new(new Vector3(0f, 0f, 1f), -Vector3.UnitZ);

        AssertMiss(Collision.GetRayTriangle(ray, Vector3.Zero, Vector3.Zero, Vector3.Zero));
        AssertMiss(Collision.GetRayTriangle(
            ray,
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(2f, 0f, 0f)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayTriangleMissesParallelBehindAndOutsideRays() {
        Vector3 a = Vector3.Zero;
        Vector3 b = Vector3.UnitX;
        Vector3 c = Vector3.UnitY;

        AssertMiss(Collision.GetRayTriangle(new Ray(new Vector3(0.25f, 0.25f, 1f), Vector3.UnitX), a, b, c));
        AssertMiss(Collision.GetRayTriangle(new Ray(new Vector3(0.25f, 0.25f, 1f), Vector3.UnitZ), a, b, c));
        AssertMiss(Collision.GetRayTriangle(new Ray(new Vector3(1f, 1f, 1f), -Vector3.UnitZ), a, b, c));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayPlaneHitsBothSidesWithANormalizedNormal() {
        Plane plane = new(Vector3.UnitZ, 0f);

        RayCollision front = Collision.GetRayPlane(new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ), plane);
        AssertHit(front, 5f, Vector3.Zero, Vector3.UnitZ);

        RayCollision back = Collision.GetRayPlane(new Ray(new Vector3(0f, 0f, -5f), Vector3.UnitZ), plane);
        AssertHit(back, 5f, Vector3.Zero, Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayPlaneNormalizesAnUnnormalizedPlaneNormal() {
        Plane plane = new(new Vector3(0f, 0f, 2f), -4f);

        RayCollision hit = Collision.GetRayPlane(new Ray(new Vector3(0f, 0f, 5f), -Vector3.UnitZ), plane);

        AssertHit(hit, 3f, new Vector3(0f, 0f, 2f), Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayPlaneMissesParallelRaysIncludingARayInThePlane() {
        Plane plane = new(Vector3.UnitZ, 0f);

        AssertMiss(Collision.GetRayPlane(new Ray(new Vector3(0f, 0f, 5f), Vector3.UnitX), plane));
        AssertMiss(Collision.GetRayPlane(new Ray(Vector3.Zero, Vector3.UnitX), plane));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayPlaneMissesWhenThePlaneIsBehindTheRay() {
        AssertMiss(Collision.GetRayPlane(new Ray(new Vector3(0f, 0f, 5f), Vector3.UnitZ), new Plane(Vector3.UnitZ, 0f)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayQueriesMissForADefaultRay() {
        Plane plane = new(Vector3.UnitZ, 0f);

        Assert.Equal(Vector3.Zero, default(Ray).Direction);
        AssertMiss(Collision.GetRaySphere(default, Vector3.Zero, 1f));
        AssertMiss(Collision.GetRayBox(default, _unitBox));
        AssertMiss(Collision.GetRayTriangle(default, Vector3.Zero, Vector3.UnitX, Vector3.UnitY));
        AssertMiss(Collision.GetRayPlane(default, plane));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshHitsATriangleListMesh() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildQuadAsync(harness);
        try {
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);
            AssertHit(hit, 1f, new Vector3(0.25f, 0.25f, 0f), Vector3.UnitZ);

            AssertMiss(Collision.GetRayMesh(
                new Ray(new Vector3(2f, 2f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity));
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshHitsATriangleStripMeshWithFlippedWinding() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleStrip,
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY),
                new Vertex(Vector3.UnitX + Vector3.UnitY),
            ],
            [0u, 1u, 2u, 3u]);
        try {
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.75f, 0.75f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);
            AssertHit(hit, 1f, new Vector3(0.75f, 0.75f, 0f), Vector3.UnitZ);

            AssertMiss(Collision.GetRayMesh(
                new Ray(new Vector3(2f, 2f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity));
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshReturnsTheNearestOfTwoOverlappingTriangles() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleList,
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY),
                new Vertex(Vector3.UnitZ),
                new Vertex(Vector3.UnitX + Vector3.UnitZ),
                new Vertex(Vector3.UnitY + Vector3.UnitZ),
            ],
            [3u, 4u, 5u, 0u, 1u, 2u]);
        try {
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 2f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);

            AssertHit(hit, 1f, new Vector3(0.25f, 0.25f, 1f), Vector3.UnitZ);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshMapsTheHitThroughTheTransform() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleList,
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY),
            ],
            [0u, 1u, 2u]);
        Matrix4x4 transform = new Transform(
            new Vector3(10f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(2f, 1f, 0.5f)).ToMatrix();
        try {
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(11f, 0.25f, -1f), -Vector3.UnitX),
                mesh,
                transform);

            // The transform's 2x X scale makes the transformed mesh-space direction half as long as
            // the unit world direction; the query still reports the world distance, not the
            // normalized mesh-space parameter.
            AssertHit(hit, 1f, new Vector3(10f, 0.25f, -1f), Vector3.UnitX);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshMissReturnsTheDefaultResult() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildQuadAsync(harness);
        try {
            RayCollision miss = Collision.GetRayMesh(
                new Ray(new Vector3(2f, 2f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);

            AssertMiss(miss);
            Assert.Equal(default, miss);

            // A default ray has a zero direction and is a miss, never a throw.
            AssertMiss(Collision.GetRayMesh(default, mesh, Matrix4x4.Identity));
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayMeshRejectsANullMesh() {
        Assert.Throws<ArgumentNullException>(() => Collision.GetRayMesh(
            new Ray(Vector3.Zero, -Vector3.UnitZ),
            null!,
            Matrix4x4.Identity));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshRejectsADisposedMesh() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildQuadAsync(harness);
        mesh.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Collision.GetRayMesh(
            new Ray(Vector3.Zero, -Vector3.UnitZ),
            mesh,
            Matrix4x4.Identity));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshRejectsALineListMesh() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildMeshAsync(
            harness,
            PrimitiveTopology.LineList,
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
            ],
            [0u, 1u]);
        try {
            Assert.Throws<NotSupportedException>(() => Collision.GetRayMesh(
                new Ray(new Vector3(0.5f, 0f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity));
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshRejectsASingularTransform() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildQuadAsync(harness);
        try {
            Assert.Throws<ArgumentException>(() => Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.CreateScale(0f)));
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshHitsAPackLoadedMesh() {
        Vertex[] vertices = [
            new Vertex(Vector3.Zero),
            new Vertex(Vector3.UnitX),
            new Vertex(Vector3.UnitY),
        ];
        uint[] indices = [0u, 1u, 2u];
        MeshAssetDescriptor descriptor = new() {
            Payload = "meshes/collision-mesh",
            VertexCount = 3,
            IndexCount = 3,
            Bounds = [0f, 0f, 0f, 1f, 1f, 0f],
            Topology = PrimitiveTopology.TriangleList,
        };
        byte[] pack = CatalogTestData.BuildPack(
            ("meshes/collision", AssetEntryType.Mesh, AssetSerialization.Serialize(descriptor)),
            ("meshes/collision-mesh", AssetEntryType.Raw, CatalogTestData.CreateMeshPayload(vertices, indices)));
        using CatalogTestHarness harness = new(userPacks: [pack]);
        Mesh mesh = await harness.Catalog.Load<Mesh>("/meshes/collision", TestContext.Current.CancellationToken);
        try {
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);

            Assert.False(mesh.IsProcedural);
            Assert.False(mesh.HasCollisionAcceleration);
            AssertHit(hit, 1f, new Vector3(0.25f, 0.25f, 0f), Vector3.UnitZ);
        } finally {
            mesh.Dispose();
        }
    }

    [Theory]
    [InlineData("cube")]
    [InlineData("sphere")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshAcceleratedAndBruteForceAgreeOnPrimitiveHitsAndMisses(string shape) {
        PrimitiveMeshData geometry = shape switch {
            "cube" => PrimitiveMeshFactory.CreateCube(),
            "sphere" => PrimitiveMeshFactory.CreateSphere(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown test shape."),
        };
        using ProceduralTestHarness harness = new();
        Mesh bruteForce = await BuildMeshAsync(
            harness,
            geometry.Topology,
            geometry.Vertices,
            geometry.Indices);
        Mesh accelerated = await BuildMeshAsync(
            harness,
            geometry.Topology,
            geometry.Vertices,
            geometry.Indices,
            collisions: true);
        try {
            Assert.False(bruteForce.HasCollisionAcceleration);
            Assert.True(accelerated.HasCollisionAcceleration);
            Ray[] rays = [
                new(new Vector3(0f, 0f, 5f), -Vector3.UnitZ),
                new(new Vector3(0.25f, 0.25f, 5f), -Vector3.UnitZ),
                new(new Vector3(2f, 2f, 5f), -Vector3.UnitZ),
                new(Vector3.Zero, Vector3.UnitX),
                new(new Vector3(0f, 0f, 5f), Vector3.UnitZ),
                new(new Vector3(0.5f, 0f, -5f), Vector3.UnitZ),
            ];
            foreach (Ray ray in rays) {
                AssertSameCollision(
                    Collision.GetRayMesh(ray, bruteForce, Matrix4x4.Identity),
                    Collision.GetRayMesh(ray, accelerated, Matrix4x4.Identity));
            }
        } finally {
            bruteForce.Dispose();
            accelerated.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshAcceleratedPathReturnsTheNearestTriangle() {
        using ProceduralTestHarness harness = new();
        Mesh mesh = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleList,
            [
                new Vertex(Vector3.Zero),
                new Vertex(Vector3.UnitX),
                new Vertex(Vector3.UnitY),
                new Vertex(Vector3.UnitZ),
                new Vertex(Vector3.UnitX + Vector3.UnitZ),
                new Vertex(Vector3.UnitY + Vector3.UnitZ),
            ],
            [3u, 4u, 5u, 0u, 1u, 2u],
            collisions: true);
        try {
            Assert.True(mesh.HasCollisionAcceleration);
            RayCollision hit = Collision.GetRayMesh(
                new Ray(new Vector3(0.25f, 0.25f, 2f), -Vector3.UnitZ),
                mesh,
                Matrix4x4.Identity);

            AssertHit(hit, 1f, new Vector3(0.25f, 0.25f, 1f), Vector3.UnitZ);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GetRayMeshMapsAnAcceleratedHitThroughANonUniformTransform() {
        using ProceduralTestHarness harness = new();
        Vertex[] vertices = [
            new Vertex(Vector3.Zero),
            new Vertex(Vector3.UnitX),
            new Vertex(Vector3.UnitY),
        ];
        uint[] indices = [0u, 1u, 2u];
        Mesh bruteForce = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleList,
            vertices,
            indices);
        Mesh accelerated = await BuildMeshAsync(
            harness,
            PrimitiveTopology.TriangleList,
            vertices,
            indices,
            collisions: true);
        Matrix4x4 transform = new Transform(
            new Vector3(10f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(2f, 1f, 0.5f)).ToMatrix();
        try {
            Ray ray = new(new Vector3(11f, 0.25f, -1f), -Vector3.UnitX);
            RayCollision expected = Collision.GetRayMesh(ray, bruteForce, transform);
            RayCollision actual = Collision.GetRayMesh(ray, accelerated, transform);

            AssertHit(expected, 1f, new Vector3(10f, 0.25f, -1f), Vector3.UnitX);
            AssertSameCollision(expected, actual);
        } finally {
            bruteForce.Dispose();
            accelerated.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayQuadHitsBothHalvesWithTheQuadNormal() {
        Vector3 a = Vector3.Zero;
        Vector3 b = Vector3.UnitX;
        Vector3 c = Vector3.UnitX + Vector3.UnitY;
        Vector3 d = Vector3.UnitY;
        Ray fromFront = new(new Vector3(0.75f, 0.25f, 1f), -Vector3.UnitZ);
        Ray fromBack = new(new Vector3(0.25f, 0.75f, -1f), Vector3.UnitZ);

        AssertHit(
            Collision.GetRayQuad(fromFront, a, b, c, d),
            1f,
            new Vector3(0.75f, 0.25f, 0f),
            Vector3.UnitZ);
        AssertHit(
            Collision.GetRayQuad(fromBack, a, b, c, d),
            1f,
            new Vector3(0.25f, 0.75f, 0f),
            Vector3.UnitZ);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayQuadReturnsTheNearestNonPlanarTriangle() {
        Vector3 a = Vector3.Zero;
        Vector3 b = new(2f, 2f, 0f);
        Vector3 c = new(2f, 0f, 1f);
        Vector3 d = new(0f, 2f, 0f);
        Ray ray = new(new Vector3(1f, 0.5f, 5f), -Vector3.UnitZ);

        RayCollision hit = Collision.GetRayQuad(ray, a, b, c, d);

        // Triangle (a, c, d) is in front of triangle (a, b, c) at this point.
        AssertHit(
            hit,
            4.5f,
            new Vector3(1f, 0.5f, 0.5f),
            Vector3.Normalize(new Vector3(-2f, 0f, 4f)));
        Assert.Equal(Collision.GetRayTriangle(ray, a, c, d).Distance, hit.Distance, 4);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GetRayQuadMissesOutsideAndDegenerateQuads() {
        Vector3 a = Vector3.Zero;
        Vector3 b = Vector3.UnitX;
        Vector3 c = Vector3.UnitX + Vector3.UnitY;
        Vector3 d = Vector3.UnitY;

        AssertMiss(Collision.GetRayQuad(
            new Ray(new Vector3(2f, 2f, 1f), -Vector3.UnitZ),
            a,
            b,
            c,
            d));
        AssertMiss(Collision.GetRayQuad(
            new Ray(new Vector3(0.25f, 0f, 1f), -Vector3.UnitZ),
            Vector3.Zero,
            Vector3.UnitX,
            new Vector3(2f, 0f, 0f),
            new Vector3(3f, 0f, 0f)));
        AssertMiss(Collision.GetRayQuad(
            new Ray(new Vector3(0.25f, 0.25f, 1f), -Vector3.UnitZ),
            new Vector3(float.NaN, 0f, 0f),
            b,
            c,
            d));
    }

    private static void AssertSameCollision(RayCollision expected, RayCollision actual) {
        Assert.Equal(expected.Hit, actual.Hit);
        if (!expected.Hit) {
            return;
        }

        Assert.Equal(expected.Distance, actual.Distance, 4);
        AssertVector(expected.Point, actual.Point);
        AssertVector(expected.Normal, actual.Normal);
    }

    private static void AssertBoxFaceHit(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Vector3 point,
        Vector3 normal) {
        RayCollision hit = Collision.GetRayBox(new Ray(origin, direction), _unitBox);
        AssertHit(hit, distance, point, normal);
    }

    private static void AssertHit(RayCollision hit, float distance, Vector3 point, Vector3 normal) {
        Assert.True(hit.Hit);
        Assert.Equal(distance, hit.Distance, 4);
        AssertVector(point, hit.Point);
        AssertVector(normal, hit.Normal);
    }

    private static void AssertMiss(RayCollision miss) {
        Assert.False(miss.Hit);
        Assert.Equal(0f, miss.Distance);
        Assert.Equal(Vector3.Zero, miss.Point);
        Assert.Equal(Vector3.Zero, miss.Normal);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual) {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private static Task<Mesh> BuildQuadAsync(ProceduralTestHarness harness) => BuildMeshAsync(
        harness,
        PrimitiveTopology.TriangleList,
        [
            new Vertex(Vector3.Zero),
            new Vertex(Vector3.UnitX),
            new Vertex(Vector3.UnitX + Vector3.UnitY),
            new Vertex(Vector3.UnitY),
        ],
        [0u, 1u, 2u, 0u, 2u, 3u]);

    private static async Task<Mesh> BuildMeshAsync(
        ProceduralTestHarness harness,
        PrimitiveTopology topology,
        Vertex[] vertices,
        uint[] indices,
        bool collisions = false) {
        MeshBuilder builder = harness.Service.CreateMeshBuilder(topology);
        builder.SetVertices(vertices);
        builder.SetIndices(indices);
        if (collisions) {
            builder.GenerateCollisions();
        }

        return await builder.BuildAsync(TestContext.Current.CancellationToken);
    }
}
