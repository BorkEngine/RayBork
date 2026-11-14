using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Catalog;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the <see cref="Camera"/> primitive, line, and billboard helpers.
/// </summary>
/// <remarks>
/// The helpers are tick-thread recorder calls, so every test drives a real <see cref="CameraService"/>
/// frame through <see cref="CameraTestHost"/> and inspects the sealed packet. The engine primitive
/// assets come from the real built-in pack through a deterministic catalog harness, so the recorded
/// meshes are the same instances the app would use.
/// </remarks>
public sealed class PrimitiveDrawTests {
    private const float TOLERANCE = 1e-5f;

    private static readonly Color _color = new(0.25f, 0.5f, 0.75f, 0.5f);

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
            camera.DrawCube(Vector3.Zero, 0f, 1f, 1f, _color);
            camera.DrawWireCube(Vector3.Zero, 0f, 1f, 1f, _color);
            camera.DrawSphere(Vector3.Zero, 0f, rings: 2, slices: 4, _color);
            camera.DrawWireSphere(Vector3.Zero, 0f, rings: 2, slices: 4, _color);
            camera.DrawPlane(Transform.Identity, new Vector2(0f, 1f), _color);
            camera.DrawCylinder(Vector3.Zero, 0f, 0f, 1f, sides: 4, _color);
            camera.DrawCylinder(Vector3.Zero, 1f, 1f, 0f, sides: 4, _color);
            camera.DrawWireCylinder(Vector3.Zero, 0f, 0f, 1f, sides: 4, _color);
            camera.DrawWireCylinder(Vector3.Zero, 1f, 1f, 0f, sides: 4, _color);
            camera.DrawCapsule(Transform.Identity, 0f, 1f, rings: 2, slices: 4, _color);
            camera.DrawCapsule(Transform.Identity, 1f, 0f, rings: 2, slices: 4, _color);
            camera.DrawWireCapsule(Transform.Identity, 0f, 1f, rings: 2, slices: 4, _color);
            camera.DrawWireCapsule(Transform.Identity, 1f, 0f, rings: 2, slices: 4, _color);
            camera.DrawCircle(Transform.Identity, 0f, _color);
            camera.DrawCircle(Vector3.Zero, 0f, Vector3.UnitY, 0f, _color);
            camera.DrawRay(new Ray(Vector3.Zero, Vector3.Zero), _color);
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
            () => camera.DrawCube(Vector3.Zero, -1f, 1f, 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCube(Vector3.Zero, 1f, -1f, 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawSphere(Vector3.Zero, -1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireSphere(Vector3.Zero, -1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawPlane(Transform.Identity, new Vector2(-1f, 1f), _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, -1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, -1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCylinder(Vector3.Zero, -1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCylinder(Vector3.Zero, Vector3.UnitY, -1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCapsule(Vector3.Zero, Vector3.UnitY, -1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCapsule(Vector3.Zero, Vector3.UnitY, -1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCapsule(Transform.Identity, -1f, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCapsule(Transform.Identity, 1f, -1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCircle(Vector3.Zero, -1f, Vector3.UnitY, 0f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCircle(Transform.Identity, -1f, _color));

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
            () => camera.DrawCube(Vector3.Zero, float.NaN, 1f, 1f, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireCube(Vector3.Zero, 1f, 1f, float.PositiveInfinity, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(
                new Transform(new Vector3(float.NaN, 0f, 0f)),
                Vector3.One,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawSphere(new Vector3(float.NaN, 0f, 0f), 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireSphere(Vector3.Zero, float.NaN, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawPlane(Transform.Identity, new Vector2(float.NaN, 1f), _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCylinder(Vector3.Zero, float.NaN, 1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, float.NaN, 1f, sides: 4, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireCylinder(
                new Vector3(float.NaN, 0f, 0f),
                Vector3.UnitY,
                1f,
                1f,
                sides: 4,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCapsule(
                new Vector3(0f, float.PositiveInfinity, 0f),
                Vector3.UnitY,
                1f,
                rings: 2,
                slices: 4,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawWireCapsule(
                Transform.Identity,
                1f,
                float.NaN,
                rings: 2,
                slices: 4,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCircle(
                new Vector3(float.NaN, 0f, 0f),
                1f,
                Vector3.UnitY,
                0f,
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCircle(Vector3.Zero, 1f, Vector3.UnitY, float.NaN, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawTriangleStrip(
                [Vector3.Zero, Vector3.UnitX, new Vector3(0f, float.NaN, 0f)],
                _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawRay(new Ray(new Vector3(float.NaN, 0f, 0f), Vector3.UnitZ), _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawBoundingBox(
                new BoundingBox(Vector3.Zero, new Vector3(float.NaN, 1f, 1f)),
                _color));

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

        Texture disposedTexture = RenderingTestAssets.CreateTexture();
        disposedTexture.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawBillboard(disposedTexture, Vector3.Zero, Vector2.One, _color));

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
        using Camera camera = host.CreatePerspectiveCamera();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => camera.DrawCube(Vector3.Zero, Vector3.One, _color));
        Assert.Contains("PrimitiveAssets", thrown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(
            () => camera.DrawLine(Vector3.Zero, Vector3.UnitX, _color));

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
            () => camera.DrawCube(Transform.Identity, Vector3.One, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireCube(Transform.Identity, Vector3.One, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawSphere(Transform.Identity, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawSphere(Transform.Identity, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireSphere(Transform.Identity, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireSphere(Transform.Identity, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawPlane(Transform.Identity, Vector2.One, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCylinder(Transform.Identity, 1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireCylinder(Transform.Identity, 1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCylinder(Vector3.Zero, Vector3.UnitY, 1f, 1f, sides: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireCylinder(Vector3.Zero, Vector3.UnitY, 1f, 1f, sides: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCapsule(Transform.Identity, 1f, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCapsule(Vector3.Zero, Vector3.UnitY, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireCapsule(Transform.Identity, 1f, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireCapsule(Vector3.Zero, Vector3.UnitY, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCircle(Transform.Identity, 1f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawCircle(Vector3.Zero, 1f, Vector3.UnitY, 0f, _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawTriangleStrip([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawRay(new Ray(Vector3.Zero, Vector3.UnitZ), _color));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawBoundingBox(new BoundingBox(Vector3.Zero, Vector3.One), _color));

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
    public async Task DrawCubeScalarAndTransformComposeTheSizeAndTransform() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(1f, 2f, -5f);
        Vector3 size = new(2f, 4f, 6f);
        Vector3 localSize = new(1f, 2f, 3f);
        Transform transform = new(
            position,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 3f, 4f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCube(position, size.X, size.Y, size.Z, _color);
            camera.DrawCube(transform, localSize, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord scalarRecord = pass.Draws[0];
        Assert.Same(harness.Primitives.Cube.Lifetime, scalarRecord.Mesh);
        Assert.Equal(1, scalarRecord.InstanceCount);
        AssertWorldBounds(
            scalarRecord.MeshBounds,
            scalarRecord.GetWorldMatrices()[0],
            position - (size * 0.5f),
            position + (size * 0.5f));

        RenderDrawRecord transformRecord = pass.Draws[1];
        Assert.Same(harness.Primitives.Cube.Lifetime, transformRecord.Mesh);
        Matrix4x4 matrix = transformRecord.GetWorldMatrices()[0];
        AssertVector(position, MatrixPosition(matrix));
        AssertVector(new Vector3(0f, 0f, -2f), Axis(matrix, 0));
        AssertVector(new Vector3(0f, 6f, 0f), Axis(matrix, 1));
        AssertVector(new Vector3(12f, 0f, 0f), Axis(matrix, 2));
        AssertWorldBounds(
            transformRecord.MeshBounds,
            matrix,
            position - new Vector3(6f, 3f, 1f),
            position + new Vector3(6f, 3f, 1f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireCubeScalarAndTransformComposeTheSizeAndTransform() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(-1f, 2f, -5f);
        Vector3 localSize = new(1f, 2f, 3f);
        Transform transform = new(
            position,
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 3f, 4f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireCube(position, 2f, 4f, 6f, _color);
            camera.DrawWireCube(transform, localSize, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord scalarRecord = pass.Draws[0];
        Assert.Same(harness.Primitives.WireCube.Lifetime, scalarRecord.Mesh);
        AssertWorldBounds(
            scalarRecord.MeshBounds,
            scalarRecord.GetWorldMatrices()[0],
            position - new Vector3(1f, 2f, 3f),
            position + new Vector3(1f, 2f, 3f));

        Matrix4x4 matrix = pass.Draws[1].GetWorldMatrices()[0];
        AssertVector(position, MatrixPosition(matrix));
        AssertVector(new Vector3(0f, 0f, -2f), Axis(matrix, 0));
        AssertVector(new Vector3(0f, 6f, 0f), Axis(matrix, 1));
        AssertVector(new Vector3(12f, 0f, 0f), Axis(matrix, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawSphereTessellatedComposesTheRequestedSurface() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(1f, -1f, -6f);
        const float radius = 2f;
        const int rings = 3;
        const int slices = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawSphere(center, radius, rings, slices, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Triangle.Lifetime, record.Mesh);
        Assert.Same(harness.Primitives.PrimitiveMaterial.Program.Lifetime, record.Program);
        Assert.Equal(2 * slices * (rings - 1), record.InstanceCount);

        Vector3[] vertices = TriangleVertices(record);
        Assert.Equal(record.InstanceCount * 3, vertices.Length);
        foreach (Vector3 vertex in vertices) {
            Assert.Equal(radius, Vector3.Distance(vertex, center), 1e-4f);
        }

        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        for (int index = 0; index < matrices.Length; index++) {
            Vector3 a = Vector3.Transform(Vector3.Zero, matrices[index]);
            Vector3 b = Vector3.Transform(Vector3.UnitX, matrices[index]);
            Vector3 c = Vector3.Transform(Vector3.UnitY, matrices[index]);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 centroid = (a + b + c) / 3f;
            Assert.True(
                Vector3.Dot(normal, centroid - center) > 0f,
                "Every tessellated sphere triangle must wind outward.");
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawSphereTransformComposesThePositionRotationAndScale() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(2f, -1f, -6f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 1f, 3f));
        const float radius = 1.5f;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawSphere(transform, radius, _color);
            camera.DrawSphere(transform, radius, rings: 3, slices: 4, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        Assert.Same(harness.Primitives.Sphere.Lifetime, pass.Draws[0].Mesh);
        Matrix4x4 matrix = pass.Draws[0].GetWorldMatrices()[0];
        AssertVector(transform.Position, MatrixPosition(matrix));
        AssertVector(new Vector3(0f, 0f, -3f), Axis(matrix, 0));
        AssertVector(new Vector3(0f, 1.5f, 0f), Axis(matrix, 1));
        AssertVector(new Vector3(4.5f, 0f, 0f), Axis(matrix, 2));
        AssertWorldBounds(
            pass.Draws[0].MeshBounds,
            matrix,
            transform.Position - new Vector3(4.5f, 1.5f, 3f),
            transform.Position + new Vector3(4.5f, 1.5f, 3f));

        RenderDrawRecord tessellatedRecord = pass.Draws[1];
        Assert.Same(harness.Primitives.Triangle.Lifetime, tessellatedRecord.Mesh);
        Assert.Equal(2 * 4 * (3 - 1), tessellatedRecord.InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireSphereTessellatedRecordsLatitudeAndMeridianLines() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(0f, 1f, -5f);
        const float radius = 2f;
        const int rings = 3;
        const int slices = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireSphere(center, radius, rings, slices, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Equal(PrimitiveTopology.LineList, record.Topology);
        Assert.Equal(slices * ((2 * rings) - 1), record.InstanceCount);

        Vector3[] endpoints = LineEndpoints(record);
        Assert.Equal(record.InstanceCount * 2, endpoints.Length);
        foreach (Vector3 endpoint in endpoints) {
            Assert.Equal(radius, Vector3.Distance(endpoint, center), 1e-4f);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireSphereTransformComposesThePositionRotationAndScale() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(-2f, 3f, -6f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 2f, 2f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireSphere(transform, 1f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.WireSphere.Lifetime, record.Mesh);
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertVector(transform.Position, MatrixPosition(matrix));
        AssertVector(new Vector3(0f, 0f, -2f), Axis(matrix, 0));
        AssertVector(new Vector3(2f, 0f, 0f), Axis(matrix, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawPlaneTransformComposesTheSizeAndTransform() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(1f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 1f, 3f));
        Vector2 size = new(2f, 4f);
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawPlane(transform, size, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Plane.Lifetime, record.Mesh);
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertVector(transform.Position, MatrixPosition(matrix));
        AssertVector(new Vector3(0f, 0f, -4f), Axis(matrix, 0));
        AssertVector(Vector3.UnitY, Axis(matrix, 1));
        AssertVector(new Vector3(12f, 0f, 0f), Axis(matrix, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCylinderConvenienceOverloadUsesSixteenSides() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(new Vector3(0f, 0f, -5f), 1f, 2f, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        Assert.Same(harness.Primitives.Quad.Lifetime, pass.Draws[0].Mesh);
        Assert.Same(harness.Primitives.Triangle.Lifetime, pass.Draws[1].Mesh);
        Assert.Equal(16, pass.Draws[0].InstanceCount);
        Assert.Equal(32, pass.Draws[1].InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCylinderFullSupportsUnequalRadiiAndConeCaps() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(0f, 0f, -5f);
        const float radiusTop = 0.5f;
        const float radiusBottom = 1.5f;
        const float height = 2f;
        const int sides = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(position, radiusTop, radiusBottom, height, sides, _color);
            camera.DrawCylinder(position, 0f, radiusBottom, height, sides, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord taperedRecord = pass.Draws[0];
        Assert.Same(harness.Primitives.Triangle.Lifetime, taperedRecord.Mesh);
        Assert.Equal(sides * 4, taperedRecord.InstanceCount);

        ReadOnlySpan<Matrix4x4> matrices = taperedRecord.GetWorldMatrices();
        for (int side = 0; side < sides; side++) {
            Matrix4x4 first = matrices[side * 2];
            Vector3 bottomEnd = Vector3.Transform(Vector3.Zero, first);
            Vector3 bottomStart = Vector3.Transform(Vector3.UnitX, first);
            Vector3 topStart = Vector3.Transform(Vector3.UnitY, first);
            Assert.Equal(position.Y, bottomEnd.Y, TOLERANCE);
            Assert.Equal(position.Y, bottomStart.Y, TOLERANCE);
            Assert.Equal(position.Y + height, topStart.Y, TOLERANCE);
            Assert.Equal(radiusBottom, HorizontalDistance(bottomEnd, position), 1e-4f);
            Assert.Equal(radiusBottom, HorizontalDistance(bottomStart, position), 1e-4f);
            Assert.Equal(radiusTop, HorizontalDistance(topStart, position), 1e-4f);

            Matrix4x4 second = matrices[(side * 2) + 1];
            AssertVector(bottomEnd, Vector3.Transform(Vector3.Zero, second));
            AssertVector(topStart, Vector3.Transform(Vector3.UnitX, second));
            Assert.Equal(radiusTop, HorizontalDistance(Vector3.Transform(Vector3.UnitY, second), position), 1e-4f);
        }

        for (int side = 0; side < sides; side++) {
            Matrix4x4 bottomCap = matrices[(sides * 2) + side];
            Vector3 bottomCenter = Vector3.Transform(Vector3.Zero, bottomCap);
            Assert.Equal(position.Y, bottomCenter.Y, TOLERANCE);
            Assert.Equal(radiusBottom, HorizontalDistance(Vector3.Transform(Vector3.UnitX, bottomCap), bottomCenter), 1e-4f);
            Assert.Equal(-1f, NormalFromMatrix(bottomCap).Y, 1e-4f);

            Matrix4x4 topCap = matrices[(sides * 3) + side];
            Vector3 topCenter = Vector3.Transform(Vector3.Zero, topCap);
            Assert.Equal(position.Y + height, topCenter.Y, TOLERANCE);
            Assert.Equal(radiusTop, HorizontalDistance(Vector3.Transform(Vector3.UnitX, topCap), topCenter), 1e-4f);
            Assert.Equal(1f, NormalFromMatrix(topCap).Y, 1e-4f);
        }

        // The cone's side and base cap share one triangle batch: sides first, then the base cap.
        RenderDrawRecord coneRecord = pass.Draws[1];
        Assert.Same(harness.Primitives.Triangle.Lifetime, coneRecord.Mesh);
        Assert.Equal(sides * 2, coneRecord.InstanceCount);
        Matrix4x4 coneSide = coneRecord.GetWorldMatrices()[0];
        Vector3 apex = Vector3.Transform(Vector3.Zero, coneSide);
        AssertVector(position + new Vector3(0f, height, 0f), apex);
        Vector3 ringStart = Vector3.Transform(Vector3.UnitX, coneSide);
        Vector3 ringEnd = Vector3.Transform(Vector3.UnitY, coneSide);
        Assert.Equal(radiusBottom, HorizontalDistance(ringStart, position), 1e-4f);
        Assert.Equal(radiusBottom, HorizontalDistance(ringEnd, position), 1e-4f);
        Matrix4x4 coneCap = coneRecord.GetWorldMatrices()[sides];
        Assert.Equal(-1f, NormalFromMatrix(coneCap).Y, 1e-4f);
        Assert.Equal(position.Y, Vector3.Transform(Vector3.Zero, coneCap).Y, TOLERANCE);
        Assert.Equal(
            radiusBottom,
            HorizontalDistance(Vector3.Transform(Vector3.UnitX, coneCap), position),
            1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCylinderBetweenPointsOrientsTheAxis() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 start = new(1f, 0f, -5f);
        Vector3 end = new(3f, 0f, -5f);
        const float startRadius = 1f;
        const float endRadius = 0.5f;
        const int sides = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(start, end, startRadius, endRadius, sides, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(1, pass.DrawCount);
        RenderDrawRecord record = pass.Draws[0];
        Assert.Same(harness.Primitives.Triangle.Lifetime, record.Mesh);
        Assert.Equal(sides * 4, record.InstanceCount);

        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        for (int side = 0; side < sides; side++) {
            Matrix4x4 first = matrices[side * 2];
            Vector3 bottomEnd = Vector3.Transform(Vector3.Zero, first);
            Vector3 bottomStart = Vector3.Transform(Vector3.UnitX, first);
            Vector3 topStart = Vector3.Transform(Vector3.UnitY, first);
            Assert.Equal(startRadius, DistanceToAxis(bottomEnd, start, end), 1e-4f);
            Assert.Equal(startRadius, DistanceToAxis(bottomStart, start, end), 1e-4f);
            Assert.Equal(endRadius, DistanceToAxis(topStart, start, end), 1e-4f);
            Assert.Equal(start.X, bottomEnd.X, TOLERANCE);
            Assert.Equal(start.X, bottomStart.X, TOLERANCE);
            Assert.Equal(end.X, topStart.X, TOLERANCE);

            Matrix4x4 second = matrices[(side * 2) + 1];
            AssertVector(bottomEnd, Vector3.Transform(Vector3.Zero, second));
            AssertVector(topStart, Vector3.Transform(Vector3.UnitX, second));
            Assert.Equal(endRadius, DistanceToAxis(Vector3.Transform(Vector3.UnitY, second), start, end), 1e-4f);
        }

        for (int side = 0; side < sides; side++) {
            Matrix4x4 startCap = matrices[(sides * 2) + side];
            AssertVector(start, Vector3.Transform(Vector3.Zero, startCap));
            Assert.Equal(startRadius, Vector3.Distance(Vector3.Transform(Vector3.UnitX, startCap), start), 1e-4f);
            AssertVector(-Vector3.UnitX, NormalFromMatrix(startCap));

            Matrix4x4 endCap = matrices[(sides * 3) + side];
            AssertVector(end, Vector3.Transform(Vector3.Zero, endCap));
            Assert.Equal(endRadius, Vector3.Distance(Vector3.Transform(Vector3.UnitX, endCap), end), 1e-4f);
            AssertVector(Vector3.UnitX, NormalFromMatrix(endCap));
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCylinderTransformUsesTheBaseAtTheTransformOrigin() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(1f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 2f, 2f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(transform, 0.5f, 1f, 2f, sides: 4, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(1, pass.DrawCount);
        RenderDrawRecord record = pass.Draws[0];
        Assert.Equal(4 * 4, record.InstanceCount);
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        for (int side = 0; side < 4; side++) {
            Matrix4x4 first = matrices[side * 2];
            Vector3 bottomEnd = Vector3.Transform(Vector3.Zero, first);
            Vector3 bottomStart = Vector3.Transform(Vector3.UnitX, first);
            Vector3 topStart = Vector3.Transform(Vector3.UnitY, first);
            Assert.Equal(transform.Position.Y, bottomEnd.Y, TOLERANCE);
            Assert.Equal(transform.Position.Y, bottomStart.Y, TOLERANCE);
            Assert.Equal(transform.Position.Y + 4f, topStart.Y, TOLERANCE);
            Assert.Equal(2f, HorizontalDistance(bottomEnd, transform.Position), 1e-4f);
            Assert.Equal(2f, HorizontalDistance(bottomStart, transform.Position), 1e-4f);
            Assert.Equal(1f, HorizontalDistance(topStart, transform.Position), 1e-4f);
        }

        AssertVector(
            transform.Position + new Vector3(0f, 4f, 0f),
            Vector3.Transform(Vector3.Zero, matrices[12]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireCylinderRecordsVerticalsAndRings() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 position = new(0f, 0f, -5f);
        const int sides = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireCylinder(position, 1f, 2f, 2f, sides, _color);
            camera.DrawWireCylinder(
                position,
                position + new Vector3(0f, 3f, 0f),
                1f,
                0.5f,
                sides,
                _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord first = pass.Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, first.Mesh);
        Assert.Equal(sides * 3, first.InstanceCount);
        ReadOnlySpan<Matrix4x4> matrices = first.GetWorldMatrices();
        for (int index = 0; index < sides; index++) {
            Vector3 bottom = Vector3.Transform(Vector3.Zero, matrices[index]);
            Vector3 top = Vector3.Transform(Vector3.UnitX, matrices[index]);
            Assert.Equal(position.Y, bottom.Y, TOLERANCE);
            Assert.Equal(position.Y + 2f, top.Y, TOLERANCE);
            Assert.Equal(2f, HorizontalDistance(bottom, position), 1e-4f);
            Assert.Equal(1f, HorizontalDistance(top, position), 1e-4f);
        }

        for (int index = sides; index < sides * 2; index++) {
            Vector3 a = Vector3.Transform(Vector3.Zero, matrices[index]);
            Vector3 b = Vector3.Transform(Vector3.UnitX, matrices[index]);
            Assert.Equal(position.Y, a.Y, TOLERANCE);
            Assert.Equal(2f, HorizontalDistance(a, position), 1e-4f);
            Assert.Equal(2f, HorizontalDistance(b, position), 1e-4f);
        }

        for (int index = sides * 2; index < sides * 3; index++) {
            Vector3 a = Vector3.Transform(Vector3.Zero, matrices[index]);
            Vector3 b = Vector3.Transform(Vector3.UnitX, matrices[index]);
            Assert.Equal(position.Y + 2f, a.Y, TOLERANCE);
            Assert.Equal(1f, HorizontalDistance(a, position), 1e-4f);
            Assert.Equal(1f, HorizontalDistance(b, position), 1e-4f);
        }

        RenderDrawRecord second = pass.Draws[1];
        Assert.Equal(sides * 3, second.InstanceCount);
        for (int index = 0; index < sides; index++) {
            Vector3 bottom = Vector3.Transform(Vector3.Zero, second.GetWorldMatrices()[index]);
            Vector3 top = Vector3.Transform(Vector3.UnitX, second.GetWorldMatrices()[index]);
            Assert.Equal(position.Y, bottom.Y, TOLERANCE);
            Assert.Equal(position.Y + 3f, top.Y, TOLERANCE);
            Assert.Equal(1f, DistanceToAxis(bottom, position, position + new Vector3(0f, 3f, 0f)), 1e-4f);
            Assert.Equal(0.5f, DistanceToAxis(top, position, position + new Vector3(0f, 3f, 0f)), 1e-4f);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCapsuleRecordsTheBodyAndBothHemispheres() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 start = new(0f, -1f, 0f);
        Vector3 end = new(0f, 1f, 0f);
        const float radius = 1f;
        const int rings = 2;
        const int slices = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCapsule(start, end, radius, rings, slices, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        RenderDrawRecord sideRecord = pass.Draws[0];
        RenderDrawRecord capRecord = pass.Draws[1];
        Assert.Same(harness.Primitives.Quad.Lifetime, sideRecord.Mesh);
        Assert.Same(harness.Primitives.Triangle.Lifetime, capRecord.Mesh);
        Assert.Equal(slices, sideRecord.InstanceCount);
        Assert.Equal(2 * slices * ((2 * rings) - 1), capRecord.InstanceCount);

        Vector3[] vertices = [.. QuadVertices(sideRecord), .. TriangleVertices(capRecord)];
        foreach (Vector3 vertex in vertices) {
            AssertOnCapsuleSurface(vertex, start, end, radius);
        }

        Assert.Contains(vertices, point => Vector3.Distance(point, new Vector3(0f, 2f, 0f)) < 1e-4f);
        Assert.Contains(vertices, point => Vector3.Distance(point, new Vector3(0f, -2f, 0f)) < 1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCapsuleTransformCentersTheCapsuleAndScalesItsDimensions() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(1f, 0f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 2f, 2f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCapsule(transform, 0.5f, 1f, rings: 2, slices: 4, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2, pass.DrawCount);
        Vector3[] vertices = [.. QuadVertices(pass.Draws[0]), .. TriangleVertices(pass.Draws[1])];
        Assert.Contains(
            vertices,
            point => Vector3.Distance(point, transform.Position + new Vector3(0f, 2f, 0f)) < 1e-4f);
        Assert.Contains(
            vertices,
            point => Vector3.Distance(point, transform.Position - new Vector3(0f, 2f, 0f)) < 1e-4f);
        foreach (Vector3 vertex in vertices) {
            AssertOnCapsuleSurface(vertex, transform.Position - new Vector3(0f, 1f, 0f), transform.Position + new Vector3(0f, 1f, 0f), 1f);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawWireCapsuleRecordsBodyRingsAndMeridians() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 start = new(0f, -1f, 0f);
        Vector3 end = new(0f, 1f, 0f);
        const int rings = 2;
        const int slices = 4;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireCapsule(start, end, 1f, rings, slices, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Equal(slices * ((4 * rings) + 1), record.InstanceCount);
        Vector3[] endpoints = LineEndpoints(record);
        foreach (Vector3 endpoint in endpoints) {
            AssertOnCapsuleSurface(endpoint, start, end, 1f);
        }

        Assert.Contains(endpoints, point => Vector3.Distance(point, new Vector3(0f, 2f, 0f)) < 1e-4f);
        Assert.Contains(endpoints, point => Vector3.Distance(point, new Vector3(0f, -2f, 0f)) < 1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TransformWireOverloadsComposeTheirInstanceMatrices() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(1f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 2f, 2f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawWireCylinder(transform, 1f, 0.5f, 2f, sides: 4, _color);
            camera.DrawWireCapsule(transform, 1f, 2f, rings: 2, slices: 4, _color);
            camera.DrawWireSphere(transform, 1f, rings: 2, slices: 4, _color);
        }

        harness.Host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(3, pass.DrawCount);
        RenderDrawRecord cylinderRecord = pass.Draws[0];
        Assert.Equal(4 * 3, cylinderRecord.InstanceCount);
        ReadOnlySpan<Matrix4x4> cylinderMatrices = cylinderRecord.GetWorldMatrices();
        for (int index = 0; index < 4; index++) {
            Vector3 bottom = Vector3.Transform(Vector3.Zero, cylinderMatrices[index]);
            Vector3 top = Vector3.Transform(Vector3.UnitX, cylinderMatrices[index]);
            Assert.Equal(transform.Position.Y, bottom.Y, TOLERANCE);
            Assert.Equal(transform.Position.Y + 4f, top.Y, TOLERANCE);
            Assert.Equal(1f, DistanceToAxis(bottom, transform.Position, transform.Position + new Vector3(0f, 4f, 0f)), 1e-4f);
            Assert.Equal(2f, DistanceToAxis(top, transform.Position, transform.Position + new Vector3(0f, 4f, 0f)), 1e-4f);
        }

        RenderDrawRecord capsuleRecord = pass.Draws[1];
        Assert.Equal(4 * ((4 * 2) + 1), capsuleRecord.InstanceCount);
        Vector3 capsuleStart = transform.Position - new Vector3(0f, 2f, 0f);
        Vector3 capsuleEnd = transform.Position + new Vector3(0f, 2f, 0f);
        Vector3[] capsuleEndpoints = LineEndpoints(capsuleRecord);
        foreach (Vector3 endpoint in capsuleEndpoints) {
            AssertOnCapsuleSurface(endpoint, capsuleStart, capsuleEnd, 2f);
        }

        Assert.Contains(
            capsuleEndpoints,
            point => Vector3.Distance(point, transform.Position + new Vector3(0f, 4f, 0f)) < 1e-4f);
        Assert.Contains(
            capsuleEndpoints,
            point => Vector3.Distance(point, transform.Position - new Vector3(0f, 4f, 0f)) < 1e-4f);

        RenderDrawRecord sphereRecord = pass.Draws[2];
        Assert.Equal(4 * ((2 * 2) - 1), sphereRecord.InstanceCount);
        foreach (Vector3 endpoint in LineEndpoints(sphereRecord)) {
            Assert.Equal(2f, Vector3.Distance(endpoint, transform.Position), 1e-4f);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCircleAxisAngleDefinesThePlane() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3 center = new(0f, 0f, -5f);
        const float radius = 2f;
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCircle(center, radius, Vector3.UnitX, 90f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Equal(36, record.InstanceCount);
        Vector3[] endpoints = LineEndpoints(record);
        foreach (Vector3 endpoint in endpoints) {
            Assert.Equal(center.Z, endpoint.Z, TOLERANCE);
            Assert.Equal(radius, Vector3.Distance(endpoint, center), 1e-4f);
        }

        Matrix4x4 first = record.GetWorldMatrices()[0];
        AssertVector(new Vector3(0f, -radius, center.Z), Vector3.Transform(Vector3.Zero, first));
        AssertVector(
            Vector3.Transform(Vector3.Zero, first),
            Vector3.Transform(Vector3.UnitX, record.GetWorldMatrices()[35]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawCircleTransformUsesTheLocalXzPlaneAndScale() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(
            new Vector3(1f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f, 1f, 3f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCircle(transform, 1f, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(36, record.InstanceCount);
        Vector3[] endpoints = LineEndpoints(record);
        foreach (Vector3 endpoint in endpoints) {
            float x = (endpoint.X - transform.Position.X) / 3f;
            float z = (endpoint.Z - transform.Position.Z) / 2f;
            Assert.Equal(transform.Position.Y, endpoint.Y, TOLERANCE);
            Assert.Equal(1f, (x * x) + (z * z), 1e-4f);
        }

        Assert.Contains(
            endpoints,
            point => Vector3.Distance(point, transform.Position + new Vector3(3f, 0f, 0f)) < 1e-4f);
        Assert.Contains(
            endpoints,
            point => Vector3.Distance(point, transform.Position + new Vector3(0f, 0f, -2f)) < 1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTriangleStripDecomposesWithStripWinding() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Vector3[] points = [
            new Vector3(0f, 0f, -5f),
            new Vector3(1f, 0f, -5f),
            new Vector3(0f, 1f, -5f),
            new Vector3(1f, 1f, -5f),
        ];
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTriangleStrip(points, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Triangle.Lifetime, record.Mesh);
        Assert.Equal(2, record.InstanceCount);
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        AssertVector(points[0], Vector3.Transform(Vector3.Zero, matrices[0]));
        AssertVector(points[1], Vector3.Transform(Vector3.UnitX, matrices[0]));
        AssertVector(points[2], Vector3.Transform(Vector3.UnitY, matrices[0]));
        AssertVector(points[2], Vector3.Transform(Vector3.Zero, matrices[1]));
        AssertVector(points[1], Vector3.Transform(Vector3.UnitX, matrices[1]));
        AssertVector(points[3], Vector3.Transform(Vector3.UnitY, matrices[1]));

        Vector3 firstNormal = Vector3.Normalize(
            Vector3.Cross(
                Vector3.Transform(Vector3.UnitX, matrices[0]) - Vector3.Transform(Vector3.Zero, matrices[0]),
                Vector3.Transform(Vector3.UnitY, matrices[0]) - Vector3.Transform(Vector3.Zero, matrices[0])));
        Vector3 secondNormal = Vector3.Normalize(
            Vector3.Cross(
                Vector3.Transform(Vector3.UnitX, matrices[1]) - Vector3.Transform(Vector3.Zero, matrices[1]),
                Vector3.Transform(Vector3.UnitY, matrices[1]) - Vector3.Transform(Vector3.Zero, matrices[1])));
        AssertVector(firstNormal, secondNormal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawTriangleStripSkipsDegenerateAndShortStrips() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawTriangleStrip([Vector3.Zero, Vector3.UnitX], _color);
            camera.DrawTriangleStrip(
                [Vector3.Zero, Vector3.UnitX, new Vector3(2f, 0f, 0f)],
                _color);
            camera.DrawTriangleStrip(
                [
                    Vector3.Zero,
                    Vector3.UnitX,
                    new Vector3(2f, 0f, 0f),
                    new Vector3(3f, 0f, 0f),
                ],
                _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawRayRecordsOneSegmentWithTheDirectionLength() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Ray ray = new(new Vector3(1f, 2f, -5f), new Vector3(0f, 0f, -3f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawRay(ray, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Equal(1, record.InstanceCount);
        Matrix4x4 matrix = record.GetWorldMatrices()[0];
        AssertVector(ray.Origin, Vector3.Transform(Vector3.Zero, matrix));
        AssertVector(ray.Origin + ray.Direction, Vector3.Transform(Vector3.UnitX, matrix));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawBoundingBoxRecordsTwelveEdges() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        BoundingBox box = new(new Vector3(-1f, -2f, -3f), new Vector3(1f, 2f, 3f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawBoundingBox(box, _color);
        }

        harness.Host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Same(harness.Primitives.Line.Lifetime, record.Mesh);
        Assert.Equal(12, record.InstanceCount);
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        float totalLength = 0f;
        for (int index = 0; index < matrices.Length; index++) {
            Vector3 start = Vector3.Transform(Vector3.Zero, matrices[index]);
            Vector3 end = Vector3.Transform(Vector3.UnitX, matrices[index]);
            Assert.True(start.X == box.Min.X || start.X == box.Max.X);
            Assert.True(start.Y == box.Min.Y || start.Y == box.Max.Y);
            Assert.True(start.Z == box.Min.Z || start.Z == box.Max.Z);
            Assert.True(end.X == box.Min.X || end.X == box.Max.X);
            Assert.True(end.Y == box.Min.Y || end.Y == box.Max.Y);
            Assert.True(end.Z == box.Min.Z || end.Z == box.Max.Z);
            float difference = 0f;
            difference += MathF.Abs(start.X - end.X) > 0f ? 1f : 0f;
            difference += MathF.Abs(start.Y - end.Y) > 0f ? 1f : 0f;
            difference += MathF.Abs(start.Z - end.Z) > 0f ? 1f : 0f;
            Assert.Equal(1f, difference);
            totalLength += Vector3.Distance(start, end);
        }

        Assert.Equal(4f * (2f + 4f + 6f), totalLength, 1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task DrawRayAndBoundingBoxRecordNothingForDegenerateInput() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        BoundingBox point = new(new Vector3(1f), new Vector3(1f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawRay(new Ray(Vector3.Zero, Vector3.Zero), _color);
            camera.DrawBoundingBox(point, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TransformOverloadsValidateTheTransform() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();
        Transform negativeScale = new(Vector3.Zero, Quaternion.Identity, new Vector3(-1f, 1f, 1f));
        Transform nanPosition = new(new Vector3(float.NaN, 0f, 0f));
        Transform nanScale = new(Vector3.Zero, Quaternion.Identity, new Vector3(1f, float.PositiveInfinity, 1f));
        Transform nanRotation = new() {
            Position = Vector3.Zero,
            Scale = Vector3.One,
            Rotation = new Quaternion(float.NaN, 0f, 0f, 1f),
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCube(negativeScale, Vector3.One, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(nanPosition, Vector3.One, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(nanScale, Vector3.One, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCube(nanRotation, Vector3.One, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawSphere(negativeScale, 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawPlane(negativeScale, Vector2.One, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(negativeScale, 1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCylinder(negativeScale, 1f, 1f, 1f, sides: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCapsule(negativeScale, 1f, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCapsule(negativeScale, 1f, 1f, rings: 2, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCircle(negativeScale, 1f, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireSphere(negativeScale, 1f, _color));

        camera.Dispose();
        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ZeroTransformScaleRecordsNothing() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        Transform transform = new(Vector3.Zero, Quaternion.Identity, new Vector3(1f, 0f, 1f));
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCube(transform, Vector3.One, _color);
            camera.DrawWireCube(transform, Vector3.One, _color);
            camera.DrawSphere(transform, 1f, _color);
            camera.DrawSphere(transform, 1f, rings: 2, slices: 4, _color);
            camera.DrawWireSphere(transform, 1f, _color);
            camera.DrawWireSphere(transform, 1f, rings: 2, slices: 4, _color);
            camera.DrawPlane(transform, Vector2.One, _color);
            camera.DrawCylinder(transform, 1f, 1f, 1f, sides: 4, _color);
            camera.DrawWireCylinder(transform, 1f, 1f, 1f, sides: 4, _color);
            camera.DrawCapsule(transform, 1f, 1f, rings: 2, slices: 4, _color);
            camera.DrawWireCapsule(transform, 1f, 1f, rings: 2, slices: 4, _color);
            camera.DrawCircle(transform, 1f, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ZeroLengthCylinderAndCapsuleSegmentsRecordNothing() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using (Camera camera = harness.Host.CreatePerspectiveCamera()) {
            camera.DrawCylinder(Vector3.Zero, Vector3.Zero, 1f, 1f, sides: 4, _color);
            camera.DrawWireCylinder(Vector3.Zero, Vector3.Zero, 1f, 1f, sides: 4, _color);
            camera.DrawCapsule(Vector3.Zero, Vector3.Zero, 1f, rings: 2, slices: 4, _color);
            camera.DrawWireCapsule(Vector3.Zero, Vector3.Zero, 1f, rings: 2, slices: 4, _color);
            camera.DrawCircle(Vector3.Zero, 0f, Vector3.UnitY, 0f, _color);
            camera.DrawCapsule(Vector3.Zero, Vector3.UnitY, 0f, rings: 2, slices: 4, _color);
        }

        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NewTessellationAndAxisValidationThrows() {
        using PrimitiveHarness harness = await PrimitiveHarness.CreateAsync();
        RenderFramePacket packet = harness.Host.BeginFrame();
        using Camera camera = harness.Host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawSphere(Vector3.Zero, 1f, rings: 1, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawSphere(Vector3.Zero, 1f, rings: 2, slices: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireSphere(Vector3.Zero, 1f, rings: 1, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireSphere(Vector3.Zero, 1f, rings: 2, slices: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCapsule(Vector3.Zero, Vector3.UnitY, 1f, rings: 1, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCapsule(Vector3.Zero, Vector3.UnitY, 1f, rings: 2, slices: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCapsule(Vector3.Zero, Vector3.UnitY, 1f, rings: 1, slices: 4, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, 1f, 1f, 1f, sides: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCylinder(Vector3.Zero, 1f, 1f, 1f, sides: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawCylinder(Vector3.Zero, Vector3.UnitY, 1f, 1f, sides: 2, _color));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => camera.DrawWireCylinder(Vector3.Zero, Vector3.UnitY, 1f, 1f, sides: 2, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCircle(Vector3.Zero, 1f, Vector3.Zero, 0f, _color));
        Assert.Throws<ArgumentException>(
            () => camera.DrawCircle(Vector3.Zero, 1f, new Vector3(float.NaN, 0f, 0f), 0f, _color));

        camera.Dispose();
        harness.Host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
    }

    private static Vector3 Axis(Matrix4x4 matrix, int index) => index switch {
        0 => new Vector3(matrix.M11, matrix.M12, matrix.M13),
        1 => new Vector3(matrix.M21, matrix.M22, matrix.M23),
        _ => new Vector3(matrix.M31, matrix.M32, matrix.M33),
    };

    private static Vector3 MatrixPosition(Matrix4x4 matrix) => new(matrix.M41, matrix.M42, matrix.M43);

    private static Vector3[] TriangleVertices(RenderDrawRecord record) {
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        Vector3[] vertices = new Vector3[matrices.Length * 3];
        int index = 0;
        foreach (Matrix4x4 matrix in matrices) {
            vertices[index++] = Vector3.Transform(Vector3.Zero, matrix);
            vertices[index++] = Vector3.Transform(Vector3.UnitX, matrix);
            vertices[index++] = Vector3.Transform(Vector3.UnitY, matrix);
        }

        return vertices;
    }

    private static Vector3[] QuadVertices(RenderDrawRecord record) {
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        Vector3[] vertices = new Vector3[matrices.Length * 4];
        int index = 0;
        foreach (Matrix4x4 matrix in matrices) {
            vertices[index++] = Vector3.Transform(new Vector3(-0.5f, -0.5f, 0f), matrix);
            vertices[index++] = Vector3.Transform(new Vector3(0.5f, -0.5f, 0f), matrix);
            vertices[index++] = Vector3.Transform(new Vector3(0.5f, 0.5f, 0f), matrix);
            vertices[index++] = Vector3.Transform(new Vector3(-0.5f, 0.5f, 0f), matrix);
        }

        return vertices;
    }

    private static Vector3[] LineEndpoints(RenderDrawRecord record) {
        ReadOnlySpan<Matrix4x4> matrices = record.GetWorldMatrices();
        Vector3[] endpoints = new Vector3[matrices.Length * 2];
        int index = 0;
        foreach (Matrix4x4 matrix in matrices) {
            endpoints[index++] = Vector3.Transform(Vector3.Zero, matrix);
            endpoints[index++] = Vector3.Transform(Vector3.UnitX, matrix);
        }

        return endpoints;
    }

    private static void AssertOnCapsuleSurface(Vector3 point, Vector3 start, Vector3 end, float radius) {
        Vector3 axis = Vector3.Normalize(end - start);
        float height = Vector3.Distance(start, end);
        float along = Vector3.Dot(point - start, axis);
        if (along >= 0f && along <= height) {
            Vector3 onAxis = start + (axis * along);
            Assert.Equal(radius, Vector3.Distance(point, onAxis), 1e-4f);
            return;
        }

        Vector3 capCenter = along < 0f ? start : end;
        Assert.Equal(radius, Vector3.Distance(point, capCenter), 1e-4f);
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

    private static float DistanceToAxis(Vector3 point, Vector3 start, Vector3 end) {
        Vector3 direction = Vector3.Normalize(end - start);
        Vector3 offset = point - start;
        return (offset - (direction * Vector3.Dot(offset, direction))).Length();
    }

    private static Vector3 NormalFromMatrix(Matrix4x4 matrix) {
        Vector3 a = Vector3.Transform(Vector3.Zero, matrix);
        Vector3 b = Vector3.Transform(Vector3.UnitX, matrix);
        Vector3 c = Vector3.Transform(Vector3.UnitY, matrix);
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
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
