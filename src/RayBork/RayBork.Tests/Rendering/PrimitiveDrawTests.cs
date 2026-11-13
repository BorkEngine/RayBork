using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the <see cref="Camera"/> primitive, line, billboard, and texture helpers.
/// </summary>
/// <remarks>
/// The helpers are tick-thread recorder calls, so every test drives a real <see cref="CameraService"/>
/// frame through <see cref="CameraTestHost"/> and inspects the sealed packet. The engine primitive
/// assets come from the real built-in pack through a deterministic catalog harness, so the recorded
/// meshes are the same instances the app would use.
/// </remarks>
public sealed class PrimitiveDrawTests {
    private const float TOLERANCE = 1e-5f;
    private const float PIXEL_TOLERANCE = 1e-2f;

    private static readonly Color _color = new(0.25f, 0.5f, 0.75f, 0.5f);

    private static readonly RenderState _overlayState = new() {
        Blend = BlendMode.Alpha,
        Cull = CullMode.None,
        DepthTest = DepthTest.Always,
        DepthWrite = false,
    };

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawLineRecordsOneSegmentMappedToTheEndpoints() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 start = new(1f, 2f, -3f);
        Vector3 end = new(4f, 6f, -3f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLine(start, end, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(1, pass.DrawCount);
        RenderDrawRecord record = pass.Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(2, record.IndexCount);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        Assert.Empty(record.GetTextureBindings().ToArray());

        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertVector(start, Vector3.Transform(Vector3.Zero, matrix));
        AssertVector(end, Vector3.Transform(Vector3.UnitX, matrix));

        // One mesh and the internal primitive program; the frame keeps no other hold.
        Assert.Equal(2, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AZeroLengthLineRecordsNothing() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLine(new Vector3(1f, 2f, -5f), new Vector3(1f, 2f, -5f), _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawLinesRecordsOneInstancePerNonZeroPair() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3[] points = [
            new Vector3(0f, 0f, -5f),
            new Vector3(1f, 0f, -5f),
            new Vector3(1f, 1f, -5f),
            new Vector3(1f, 1f, -5f),
            new Vector3(2f, 1f, -5f),
        ];
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLines(points, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(1, pass.DrawCount);
        RenderDrawRecord record = pass.Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(3, record.InstanceCount);

        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        AssertVector(points[0], Vector3.Transform(Vector3.Zero, matrices[0]));
        AssertVector(points[1], Vector3.Transform(Vector3.UnitX, matrices[0]));
        AssertVector(points[1], Vector3.Transform(Vector3.Zero, matrices[1]));
        AssertVector(points[2], Vector3.Transform(Vector3.UnitX, matrices[1]));
        AssertVector(points[3], Vector3.Transform(Vector3.Zero, matrices[2]));
        AssertVector(points[4], Vector3.Transform(Vector3.UnitX, matrices[2]));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DrawLinesWithFewerThanTwoPointsRecordsNothing(int pointCount) {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3[] points = [.. Enumerable.Range(0, pointCount).Select(index => new Vector3(index, 0f, -5f))];
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLines(points, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawPointRecordsACameraFacingQuadOfTheRequestedSize() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawPoint(new Vector3(0f, 0f, -5f), _color, 2f);
        }

        Quaternion yaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera(
            transform: new Transform(Vector3.Zero, yaw, Vector3.One))) {
            camera.DrawPoint(new Vector3(-5f, 0f, 0f), _color, 2f);
        }

        harness.Host.EndFrame();

        Assert.Equal(2, packet.Passes.Length);
        RenderDrawRecord identityRecord = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Quad.Lifetime, identityRecord.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, identityRecord.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, identityRecord.Topology);
        Assert.Equal(RenderState.Opaque, identityRecord.RenderState);
        Assert.Equal(_color, identityRecord.Tint);
        Assert.Equal(1, identityRecord.InstanceCount);
        Matrix4x4 identityMatrix = identityRecord.GetWorldMatrices()[0];
        AssertWorldBounds(
            identityRecord.MeshBounds,
            identityMatrix,
            new Vector3(-1f, -1f, -5f),
            new Vector3(1f, 1f, -5f));
        AssertVector(new Vector3(2f, 0f, 0f), Axis(identityMatrix, 0));
        AssertVector(new Vector3(0f, 2f, 0f), Axis(identityMatrix, 1));
        AssertVector(new Vector3(0f, 0f, 1f), Axis(identityMatrix, 2));
        AssertVector(
            new Vector3(-1f, 1f, -5f),
            Vector3.Transform(new Vector3(-0.5f, 0.5f, 0f), identityMatrix));
        AssertVector(
            new Vector3(1f, -1f, -5f),
            Vector3.Transform(new Vector3(0.5f, -0.5f, 0f), identityMatrix));

        // A yawed camera turns the quad's basis with it; the rear camera faces world -X.
        RenderDrawRecord yawedRecord = packet.Passes[1].Draws[0];
        Matrix4x4 yawedMatrix = yawedRecord.GetWorldMatrices()[0];
        AssertVector(new Vector3(0f, 0f, -2f), Axis(yawedMatrix, 0));
        AssertVector(new Vector3(0f, 2f, 0f), Axis(yawedMatrix, 1));
        AssertVector(new Vector3(1f, 0f, 0f), Axis(yawedMatrix, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTriangleMapsTheReferenceTriangleInOrder() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 a = new(0f, 0f, -5f);
        Vector3 b = new(1f, 0f, -5f);
        Vector3 c = new(0f, 1f, -5f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTriangle(a, b, c, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Triangle.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(3, record.IndexCount);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertWorldBounds(
            record.MeshBounds,
            matrix,
            new Vector3(0f, 0f, -5f),
            new Vector3(1f, 1f, -5f));
        AssertVector(a, Vector3.Transform(Vector3.Zero, matrix));
        AssertVector(b, Vector3.Transform(Vector3.UnitX, matrix));
        AssertVector(c, Vector3.Transform(Vector3.UnitY, matrix));

        // The map keeps the reference orientation: the unit triangle's +Z normal becomes the user
        // triangle's normal, so a counter-clockwise triangle stays front-facing.
        AssertVector(Vector3.UnitZ, Axis(matrix, 2));
        Assert.True(matrix.GetDeterminant() > 0f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTriangleSkipsDegenerateTriangles() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTriangle(
                new Vector3(0f, 0f, -5f),
                new Vector3(1f, 1f, -5f),
                new Vector3(2f, 2f, -5f),
                _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCubeRecordsAScaledCubeAtPosition() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(1f, 2f, -5f);
        Vector3 size = new(2f, 4f, 6f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCube(position, size, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Cube.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(36, record.IndexCount);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        AssertWorldBounds(
            record.MeshBounds,
            record.GetWorldMatrices()[0],
            position - (size * 0.5f),
            position + (size * 0.5f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawSphereRecordsAUniformlyScaledSphere() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(1f, 2f, -6f);
        const float radius = 2.5f;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawSphere(center, radius, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Sphere.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        AssertWorldBounds(
            record.MeshBounds,
            record.GetWorldMatrices()[0],
            center - new Vector3(radius),
            center + new Vector3(radius));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawPlaneRecordsAnXzQuad() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(0f, 1f, -5f);
        Vector2 size = new(2f, 4f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawPlane(center, size, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Plane.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        AssertWorldBounds(
            record.MeshBounds,
            record.GetWorldMatrices()[0],
            center + new Vector3(-size.X * 0.5f, 0f, -size.Y * 0.5f),
            center + new Vector3(size.X * 0.5f, 0f, size.Y * 0.5f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireCubeRecordsAScaledWireCube() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(1f, 2f, -5f);
        Vector3 size = new(2f, 4f, 6f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireCube(position, size, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.WireCube.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(24, record.IndexCount);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        AssertWorldBounds(
            record.MeshBounds,
            record.GetWorldMatrices()[0],
            position - (size * 0.5f),
            position + (size * 0.5f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireSphereRecordsAUniformlyScaledWireSphere() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(1f, 2f, -6f);
        const float radius = 3f;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireSphere(center, radius, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.WireSphere.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(384, record.IndexCount);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        AssertWorldBounds(
            record.MeshBounds,
            record.GetWorldMatrices()[0],
            center - new Vector3(radius),
            center + new Vector3(radius));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task DrawCylinderRecordsSidesAndCapsWithOutwardWinding(int sides) {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(0f, 0f, -5f);
        const float radius = 1.5f;
        const float height = 2f;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(position, radius, height, _color, sides);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord sideRecord = pass.Draws[0];
        RenderDrawRecord capRecord = pass.Draws[1];
        Assert.Same(harness.Primitives.Quad.Lifetime, sideRecord.Mesh);
        Assert.Same(harness.Primitives.Triangle.Lifetime, capRecord.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, sideRecord.Program);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, capRecord.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, sideRecord.Topology);
        Assert.Equal(PrimitiveTopology.TriangleList, capRecord.Topology);
        Assert.Equal(RenderState.Opaque, sideRecord.RenderState);
        Assert.Equal(RenderState.Opaque, capRecord.RenderState);
        Assert.Equal(sides, sideRecord.InstanceCount);
        Assert.Equal(sides * 2, capRecord.InstanceCount);
        Assert.Equal(sides * 3, sideRecord.InstanceCount + capRecord.InstanceCount);
        Assert.Equal(_color, sideRecord.Tint);
        Assert.Equal(_color, capRecord.Tint);

        ReadOnlySpan<Matrix4x4> sideMatrices = sideRecord.GetWorldMatrices();
        for (int index = 0; index < sideMatrices.Length; index++) {
            Matrix4x4 matrix = sideMatrices[index];
            // The sides are vertical quads of the requested height.
            AssertVector(new Vector3(0f, height, 0f), Axis(matrix, 1));
            Vector3 normal = Vector3.Normalize(Axis(matrix, 2));
            Assert.Equal(0f, normal.Y, TOLERANCE);
            Vector3 center = new(matrix.M41, matrix.M42, matrix.M43);
            Vector3 outward = Vector3.Normalize(new Vector3(
                center.X - position.X,
                0f,
                center.Z - position.Z));
            Assert.Equal(1f, Vector3.Dot(normal, outward), 1e-4f);

            Vector3 baseStart = Vector3.Transform(new Vector3(-0.5f, -0.5f, 0f), matrix);
            Vector3 baseEnd = Vector3.Transform(new Vector3(0.5f, -0.5f, 0f), matrix);
            Vector3 topStart = Vector3.Transform(new Vector3(-0.5f, 0.5f, 0f), matrix);
            Assert.Equal(position.Y, baseStart.Y, TOLERANCE);
            Assert.Equal(position.Y + height, topStart.Y, TOLERANCE);
            Assert.Equal(radius, HorizontalDistance(baseStart, position), 1e-4f);
            Assert.Equal(radius, HorizontalDistance(baseEnd, position), 1e-4f);
            Assert.Equal(radius, HorizontalDistance(topStart, position), 1e-4f);
        }

        ReadOnlySpan<Matrix4x4> capMatrices = capRecord.GetWorldMatrices();
        for (int index = 0; index < capMatrices.Length; index++) {
            Matrix4x4 matrix = capMatrices[index];
            Vector3 a = Vector3.Transform(Vector3.Zero, matrix);
            Vector3 b = Vector3.Transform(Vector3.UnitX, matrix);
            Vector3 c = Vector3.Transform(Vector3.UnitY, matrix);
            bool isTopCap = (index & 1) == 1;
            float capY = isTopCap ? position.Y + height : position.Y;
            Assert.Equal(capY, a.Y, TOLERANCE);
            Assert.Equal(capY, b.Y, TOLERANCE);
            Assert.Equal(capY, c.Y, TOLERANCE);
            Assert.Equal(radius, HorizontalDistance(b, position), 1e-4f);
            Assert.Equal(radius, HorizontalDistance(c, position), 1e-4f);
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Assert.Equal(isTopCap ? 1f : -1f, normal.Y, 1e-4f);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawGridRecordsSlicesPlusOneLinesPerAxis() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera(transform: new Transform(new Vector3(0f, 0f, 10f)))) {
            camera.DrawGrid(2, 2f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(6, record.InstanceCount);

        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        for (int line = 0; line <= 2; line++) {
            float offset = -2f + (line * 2f);
            Matrix4x4 xLine = matrices[line];
            AssertVector(new Vector3(-2f, 0f, offset), Vector3.Transform(Vector3.Zero, xLine));
            AssertVector(new Vector3(2f, 0f, offset), Vector3.Transform(Vector3.UnitX, xLine));
        }

        for (int line = 0; line <= 2; line++) {
            float offset = -2f + (line * 2f);
            Matrix4x4 zLine = matrices[3 + line];
            AssertVector(new Vector3(offset, 0f, -2f), Vector3.Transform(Vector3.Zero, zLine));
            AssertVector(new Vector3(offset, 0f, 2f), Vector3.Transform(Vector3.UnitX, zLine));
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawBillboardRecordsACameraFacingQuadAndResetsTheTextureBinding() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        using (Camera camera = harness.Host.CreatePerspectiveCamera(
            transform: new Transform(new Vector3(0f, 0f, 5f)))) {
            camera.DrawBillboard(texture, Vector3.Zero, new Vector2(2f, 3f), _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Quad.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.MeshMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(RenderState.AlphaBlend, record.RenderState);
        Assert.Equal(_color, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        Assert.Same(texture, record.GetTextureBindings()[0]);

        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertVector(new Vector3(2f, 0f, 0f), Axis(matrix, 0));
        AssertVector(new Vector3(0f, 3f, 0f), Axis(matrix, 1));
        AssertVector(Vector3.UnitZ, Axis(matrix, 2));
        AssertVector(
            new Vector3(-1f, 1.5f, 0f),
            Vector3.Transform(new Vector3(-0.5f, 0.5f, 0f), matrix));

        // The shared engine material must not keep the game texture after the call.
        Assert.Null(harness.Primitives.MeshMaterial.TextureValues[0]);
        // The quad mesh, the internal mesh program, and the recorded texture.
        Assert.Equal(3, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawBillboardFaceCameraFalseKeepsWorldUpAndTheCameraYaw() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 4f);
        Transform transform = new(new Vector3(0f, 5f, 5f), pitch, Vector3.One);
        using (Camera camera = harness.Host.CreatePerspectiveCamera(transform: transform)) {
            camera.DrawBillboard(texture, Vector3.Zero, new Vector2(2f, 2f), _color, faceCamera: false);
        }

        using (Camera camera = harness.Host.CreatePerspectiveCamera(transform: transform)) {
            camera.DrawBillboard(texture, Vector3.Zero, new Vector2(2f, 2f), _color);
        }

        harness.Host.EndFrame();

        // The vertical billboard drops the camera pitch: world up and the horizontal yaw only.
        Matrix4x4 verticalMatrix = packet.Passes[0].Draws[0].GetWorldMatrices()[0];
        AssertVector(new Vector3(2f, 0f, 0f), Axis(verticalMatrix, 0));
        AssertVector(new Vector3(0f, 2f, 0f), Axis(verticalMatrix, 1));
        AssertVector(Vector3.UnitZ, Axis(verticalMatrix, 2));

        // The camera-facing billboard tilts with the camera's pitch.
        Matrix4x4 facingMatrix = packet.Passes[1].Draws[0].GetWorldMatrices()[0];
        float component = MathF.Sqrt(0.5f);
        AssertVector(new Vector3(2f, 0f, 0f), Axis(facingMatrix, 0));
        AssertVector(new Vector3(0f, 2f * component, -2f * component), Axis(facingMatrix, 1));
        AssertVector(new Vector3(0f, component, component), Axis(facingMatrix, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTextureMapsTheQuadToTheDestinationRectangleForAPerspectiveCamera() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        Rect destination = new(100f, 50f, 320f, 180f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTexture(texture, destination);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        RenderDrawRecord record = pass.Draws[0];
        Assert.Same(harness.Primitives.Quad.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.MeshMaterial.Program.Lifetime, record.Program);
        Assert.Equal(PrimitiveTopology.TriangleList, record.Topology);
        Assert.Equal(_overlayState, record.RenderState);
        Assert.Equal(Color.White, record.Tint);
        Assert.Equal(1, record.InstanceCount);
        Assert.Same(texture, record.GetTextureBindings()[0]);
        Assert.Null(harness.Primitives.MeshMaterial.TextureValues[0]);
        AssertTextureQuadMapping(harness.Primitives, record, pass, destination);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTextureMapsTheQuadToTheDestinationRectangleForAnOrthographicCamera() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        Rect destination = new(240f, 90f, 400f, 220f);
        using (Camera camera = harness.Host.CreateOrthographicCamera()) {
            camera.DrawTexture(texture, destination, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        RenderDrawRecord record = pass.Draws[0];
        Assert.Equal(_overlayState, record.RenderState);
        Assert.Equal(_color, record.Tint);
        AssertTextureQuadMapping(harness.Primitives, record, pass, destination);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ZeroExtentsRecordNothing() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLine(Vector3.Zero, Vector3.Zero, _color);
            camera.DrawLines([Vector3.Zero], _color);
            camera.DrawPoint(Vector3.Zero, _color, 0f);
            camera.DrawTriangle(
                Vector3.Zero,
                new Vector3(1f, 0f, 0f),
                new Vector3(2f, 0f, 0f),
                _color);
            camera.DrawCube(Vector3.Zero, new Vector3(1f, 0f, 1f), _color);
            camera.DrawSphere(Vector3.Zero, 0f, _color);
            camera.DrawCylinder(Vector3.Zero, 0f, 1f, _color);
            camera.DrawCylinder(Vector3.Zero, 1f, 0f, _color);
            camera.DrawPlane(Vector3.Zero, new Vector2(0f, 1f), _color);
            camera.DrawWireCube(Vector3.Zero, new Vector3(1f, 1f, 0f), _color);
            camera.DrawWireSphere(Vector3.Zero, 0f, _color);
            camera.DrawBillboard(texture, Vector3.Zero, new Vector2(0f, 1f), _color);
            camera.DrawTexture(texture, new Rect(0f, 0f, 0f, 10f));
            camera.DrawTexture(texture, new Rect(0f, 0f, 10f, 0f));
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NegativeExtentsThrowArgumentOutOfRangeException() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        using Camera camera = harness.Host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentOutOfRangeException>(() => camera.DrawPoint(Vector3.Zero, _color, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCube(Vector3.Zero, new Vector3(1f, -1f, 1f), _color));
        Assert.Throws<ArgumentOutOfRangeException>(() => camera.DrawSphere(Vector3.Zero, -1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, -1f, 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, -1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawPlane(Vector3.Zero, new Vector2(-1f, 1f), _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCube(Vector3.Zero, new Vector3(1f, 1f, -1f), _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireSphere(Vector3.Zero, -1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawBillboard(texture, Vector3.Zero, new Vector2(-1f, 1f), _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawTexture(texture, new Rect(0f, 0f, -10f, 10f)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawTexture(texture, new Rect(0f, 0f, 10f, -10f)));

        camera.Dispose();
        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task InvalidGridAndCylinderTessellationThrowsArgumentOutOfRangeException() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, 1f, _color, sides: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, 1f, _color, sides: 2));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawGrid(slices: 0, spacing: 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawGrid(slices: -1, spacing: 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawGrid(slices: 1, spacing: 0f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawGrid(slices: 1, spacing: -1f, _color));

        camera.Dispose();
        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NonFiniteArgumentsThrowArgumentException() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        using Camera camera = harness.Host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentException>(
            () => camera.DrawLine(new Vector3(float.NaN, 0f, 0f), Vector3.Zero, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawLine(Vector3.Zero, new Vector3(float.PositiveInfinity, 0f, 0f), _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawLines([Vector3.Zero, new Vector3(0f, float.NaN, 0f)], _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawPoint(new Vector3(0f, float.PositiveInfinity, 0f), _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawPoint(Vector3.Zero, _color, float.NaN));
        Assert.Throws<ArgumentException>(
            () => camera.DrawTriangle(new Vector3(float.NaN, 0f, 0f), Vector3.UnitX, Vector3.UnitY, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(new Vector3(0f, float.NaN, 0f), Vector3.One, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(Vector3.Zero, new Vector3(1f, float.NaN, 1f), _color));
        Assert.Throws<ArgumentException>(() => camera.DrawSphere(Vector3.Zero, float.NaN, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCylinder(Vector3.Zero, float.NaN, 1f, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, float.PositiveInfinity, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawPlane(new Vector3(float.NaN, 0f, 0f), Vector2.One, _color));
        Assert.Throws<ArgumentException>(() => camera.DrawGrid(slices: 1, spacing: float.NaN, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireCube(Vector3.Zero, new Vector3(1f, float.NaN, 1f), _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireSphere(Vector3.Zero, float.NegativeInfinity, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawBillboard(
                texture,
                new Vector3(float.NaN, 0f, 0f),
                Vector2.One,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawBillboard(
                texture,
                Vector3.Zero,
                new Vector2(float.NaN, 1f),
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawTexture(texture, new Rect(float.NaN, 0f, 1f, 1f)));
        Assert.Throws<ArgumentException>(
            () => camera.DrawTexture(texture, new Rect(0f, 0f, float.PositiveInfinity, 1f)));

        camera.Dispose();
        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NullAndDisposedTexturesThrow() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentNullException>(
            () => camera.DrawBillboard(null!, Vector3.Zero, Vector2.One, _color));
        Assert.Throws<ArgumentNullException>(
            () => camera.DrawTexture(null!, new Rect(0f, 0f, 1f, 1f)));

        Texture disposedTexture = RenderingTestAssets.CreateTexture();
        disposedTexture.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawBillboard(disposedTexture, Vector3.Zero, Vector2.One, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawTexture(disposedTexture, new Rect(0f, 0f, 1f, 1f)));

        camera.Dispose();
        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedPrimitiveAssetsThrowObjectDisposedException() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();
        PrimitiveAssets primitives = harness.Primitives;
        primitives.Dispose();

        Assert.Throws<ObjectDisposedException>(() => camera.DrawCube(Vector3.Zero, Vector3.One, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawLine(Vector3.Zero, Vector3.UnitX, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawGrid(slices: 1, spacing: 1f, _color));

        camera.Dispose();
        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MissingPrimitiveAssetsThrowInvalidOperationException() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        using Camera camera = host.CreatePerspectiveCamera();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => camera.DrawCube(Vector3.Zero, Vector3.One, _color));
        Assert.Contains("PrimitiveAssets", thrown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(
            () => camera.DrawLine(Vector3.Zero, Vector3.UnitX, _color));
        Assert.Throws<InvalidOperationException>(
            () => camera.DrawTexture(texture, new Rect(0f, 0f, 1f, 1f)));

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DisposedCameraRejectsEveryPrimitiveHelper() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        Camera camera = harness.Host.CreatePerspectiveCamera();

        camera.Dispose();

        Assert.Throws<ObjectDisposedException>(() => camera.DrawLine(Vector3.Zero, Vector3.UnitX, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawLines([Vector3.Zero, Vector3.UnitX], _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawPoint(Vector3.Zero, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawCube(Vector3.Zero, Vector3.One, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawSphere(Vector3.Zero, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, 1f, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawPlane(Vector3.Zero, Vector2.One, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawGrid(1, 1f, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawWireCube(Vector3.Zero, Vector3.One, _color));
        Assert.Throws<ObjectDisposedException>(() => camera.DrawWireSphere(Vector3.Zero, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawBillboard(texture, Vector3.Zero, Vector2.One, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawTexture(texture, new Rect(0f, 0f, 1f, 1f)));

        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PrimitiveHelpersRequireTheTickThread() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();
        Exception? captured = null;

        Thread thread = new(() => {
            try {
                camera.DrawCube(Vector3.Zero, Vector3.One, _color);
            } catch (Exception exception) {
                captured = exception;
            }
        });
        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(captured);
        camera.Dispose();
        harness.Host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TwoCubeCallsPlanOneBatchWithTwoInstancesAndTwentyFourTriangles() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCube(new Vector3(0f, 0f, -5f), Vector3.One, _color);
            camera.DrawCube(new Vector3(1.5f, 0f, -5f), Vector3.One, _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(2, plan.Instances);
        Assert.Equal(24, plan.Triangles);
        Assert.Equal(0, plan.CulledInstances);
        PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
        Assert.Same(harness.Primitives.Cube.Lifetime, batch.Mesh);
        Assert.Equal(2, batch.InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task LineHelpersPlanOneBatchWithNoTriangles() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawLine(new Vector3(-2f, 0f, -5f), new Vector3(-1f, 0f, -5f), _color);
            camera.DrawLines(
                [
                    new Vector3(0f, 0f, -5f),
                    new Vector3(1f, 0f, -5f),
                    new Vector3(2f, 0f, -5f),
                ],
                _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(3, plan.Instances);
        Assert.Equal(0, plan.Triangles);
        PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
        Assert.Same(harness.Primitives.Line.Lifetime, batch.Mesh);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawGridPlansOneInstancedLineDraw() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera(
            transform: new Transform(new Vector3(0f, 0f, 20f)))) {
            camera.DrawGrid(2, 2f, _color);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Equal(6, plan.Instances);
        Assert.Equal(0, plan.Triangles);
        Assert.Equal(0, plan.CulledInstances);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCylinderPlansTwoBatches() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(new Vector3(0f, 0f, -5f), 1f, 2f, _color, sides: 4);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(12, plan.Instances);
        Assert.Equal(16, plan.Triangles);
        Assert.Equal(0, plan.CulledInstances);
        Assert.Equal(2, plan.Passes[0].Batches.Count);
        Assert.Same(harness.Primitives.Quad.Lifetime, plan.Passes[0].Batches[0].Mesh);
        Assert.Same(harness.Primitives.Triangle.Lifetime, plan.Passes[0].Batches[1].Mesh);
        Assert.Equal(4, plan.Passes[0].Batches[0].InstanceCount);
        Assert.Equal(8, plan.Passes[0].Batches[1].InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BillboardAndTextureOverlaysDoNotBatchTogether() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Texture texture = RenderingTestAssets.CreateTexture();
        Rect destination = new(100f, 50f, 320f, 180f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawBillboard(texture, new Vector3(0f, 0f, -5f), new Vector2(2f, 2f), _color);
            camera.DrawTexture(texture, destination);
        }

        harness.Host.EndFrame();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(2, plan.DrawCalls);
        Assert.Equal(2, plan.Instances);
        Assert.Equal(2, plan.Passes[0].Batches.Count);
        Assert.Contains(plan.Passes[0].Batches, batch => batch.RenderState == RenderState.AlphaBlend);
        Assert.Contains(plan.Passes[0].Batches, batch => batch.RenderState == _overlayState);
        foreach (PlannedBatch batch in plan.Passes[0].Batches) {
            Assert.Same(harness.Primitives.Quad.Lifetime, batch.Mesh);
            Assert.Equal(1, batch.InstanceCount);
        }
    }

    private static Vector3 Axis(Matrix4x4 matrix, int index) => index switch {
        0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
        1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
        _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
    };

    private static Vector3 ProjectToNdc(Matrix4x4 world, Matrix4x4 viewProjection, Vector3 local) {
        Vector4 clip = Vector4.Transform(
            Vector4.Transform(new Vector4(local, 1f), world),
            viewProjection);
        Assert.True(clip.W > 0f);
        return new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
    }

    private static void AssertTextureQuadMapping(
        PrimitiveAssets primitives,
        RenderDrawRecord record,
        CameraPass pass,
        Rect destination) {
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        ReadOnlySpan<Vertex> vertices = primitives.Quad.Vertices.Span;
        foreach (Vertex vertex in vertices) {
            Vector3 ndc = ProjectToNdc(matrix, pass.ViewProjection, vertex.Position);
            Assert.Equal(0.5f, ndc.Z, 1e-3f);
            Vector2 pixel = new(
                ((ndc.X * 0.5f) + 0.5f) * pass.ViewportWidth,
                ((ndc.Y * 0.5f) + 0.5f) * pass.ViewportHeight);
            Vector2 expected = new(
                destination.X + (vertex.Uv.X * destination.Width),
                destination.Y + (vertex.Uv.Y * destination.Height));
            AssertVector2(expected, pixel, PIXEL_TOLERANCE);
            if (vertex.Uv == Vector2.Zero) {
                // The quad's top-left UV must land on the destination top-left corner.
                Assert.Equal(destination.X, pixel.X, PIXEL_TOLERANCE);
                Assert.Equal(destination.Y, pixel.Y, PIXEL_TOLERANCE);
            }
        }
    }

    private static void AssertWorldBounds(
        BoundingBox meshBounds,
        Matrix4x4 world,
        Vector3 expectedMinimum,
        Vector3 expectedMaximum) {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++) {
            Vector3 point = new(
                (corner & 1) == 0 ? meshBounds.Min.X : meshBounds.Max.X,
                (corner & 2) == 0 ? meshBounds.Min.Y : meshBounds.Max.Y,
                (corner & 4) == 0 ? meshBounds.Min.Z : meshBounds.Max.Z);
            Vector3 transformed = Vector3.Transform(point, world);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }

        AssertVector(expectedMinimum, minimum);
        AssertVector(expectedMaximum, maximum);
    }

    private static float HorizontalDistance(Vector3 point, Vector3 center) {
        float x = point.X - center.X;
        float z = point.Z - center.Z;
        return MathF.Sqrt((x * x) + (z * z));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    private static void AssertVector2(Vector2 expected, Vector2 actual, float tolerance) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
    }

    /// <summary>
    /// Owns a real camera frame host, the built-in catalog, and the engine primitive assets, and
    /// releases them in reverse creation order. The assets are attached before the first frame, like the
    /// app does before <see cref="IGame.Setup"/>.
    /// </summary>
    private sealed class PrimitiveHarness : IDisposable {
        private readonly CatalogTestHarness _catalog;
        private readonly PrimitiveAssets _primitives;
        private bool _disposed;

        private PrimitiveHarness(
            CameraTestHost host,
            CatalogTestHarness catalog,
            PrimitiveAssets primitives) {
            Host = host;
            _catalog = catalog;
            _primitives = primitives;
            host.Service.AttachPrimitives(primitives);
        }

        internal CameraTestHost Host { get; }

        internal PrimitiveAssets Primitives => _primitives;

        internal static async Task<PrimitiveHarness> CreateAsync() {
            CameraTestHost host = new();
            CatalogTestHarness? catalog = null;
            PrimitiveAssets? primitives = null;
            try {
                catalog = new CatalogTestHarness(builtInPack: BuiltInAssetPack.Bytes.ToArray());
                primitives = await PrimitiveAssets.LoadAsync(catalog.Catalog);
                return new PrimitiveHarness(host, catalog, primitives);
            } catch (Exception exception) when (exception is not OutOfMemoryException) {
                primitives?.Dispose();
                catalog?.Dispose();
                host.Dispose();
                throw;
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            _primitives.Dispose();
            _catalog.Dispose();
            Host.Dispose();
        }
    }
}
