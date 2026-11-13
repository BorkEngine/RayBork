using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets.Procedural;

public sealed class MeshBuilderTests {
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateMeshBuilderRejectsAnUndefinedTopology() {
        using ProceduralTestHarness harness = new();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => harness.Service.CreateMeshBuilder((PrimitiveTopology)42));

        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        Assert.Equal(PrimitiveTopology.TriangleList, builder.Topology);
        Assert.Empty(builder.Vertices);
        Assert.Empty(builder.Indices);
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Topology = (PrimitiveTopology)42);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetVerticesAndSetIndicesReplaceTheContentsAndTheBuilderKeepsACopy() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        Vertex[] vertices = CreateQuadPositions();
        uint[] indices = [0, 1, 2, 0, 2, 3];

        builder.SetVertices(vertices);
        builder.SetIndices(indices);
        vertices[0].Position = new Vector3(99f, 99f, 99f);
        indices[0] = 3;

        Assert.Equal(4, builder.Vertices.Count);
        Assert.Equal(Vector3.Zero, builder.Vertices[0].Position);
        Assert.Equal(0u, builder.Indices[0]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task EmptyMeshIsRejectedWithoutStartingAnUpload() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MissingIndicesAreRejected() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices(CreateTrianglePositions());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PrimitiveTopology.TriangleList, 4)]
    [InlineData(PrimitiveTopology.TriangleList, 2)]
    [InlineData(PrimitiveTopology.LineList, 3)]
    [InlineData(PrimitiveTopology.LineStrip, 1)]
    [InlineData(PrimitiveTopology.PointList, 0)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task TopologySpecificIndexCountsAreValidated(PrimitiveTopology topology, int indexCount) {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.Topology = topology;
        builder.SetVertices(CreateTrianglePositions());
        uint[] indices = new uint[Math.Max(indexCount, 0)];
        builder.SetIndices(indices);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Worker.UploadCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task OutOfRangeIndexIsRejected() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices(CreateTrianglePositions());
        builder.SetIndices([0, 1, 3]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task NonFiniteAttributeIsRejected() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero),
            new Vertex(new Vector3(1f, 0f, 0f)),
            new Vertex(new Vector3(0f, 1f, 0f), uv: new Vector2(float.NaN, 0f)),
        ]);
        builder.SetIndices([0, 1, 2]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(PrimitiveTopology.LineList)]
    [InlineData(PrimitiveTopology.LineStrip)]
    [InlineData(PrimitiveTopology.PointList)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalsAndTangentsRejectLineAndPointTopologies(PrimitiveTopology topology) {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.Topology = topology;
        builder.SetVertices(CreateTrianglePositions());
        builder.SetIndices([0, 1, 2]);

        Assert.Throws<NotSupportedException>(() => builder.ComputeNormals());
        Assert.Throws<NotSupportedException>(() => builder.ComputeTangents());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputeNormalsRejectsAnInvalidTriangleCount() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices(CreateQuadPositions());
        builder.SetIndices([0, 1, 2, 0]);

        Assert.Throws<InvalidOperationException>(() => builder.ComputeNormals());
        Assert.Throws<InvalidOperationException>(() => builder.ComputeNormals(MeshNormalMode.Flat));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputeNormalsRejectsAnUndefinedMode() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateQuad(builder);

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ComputeNormals((MeshNormalMode)42));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SmoothNormalsAccumulateBySharedPosition() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        // Two triangles meeting at an edge, with the shared edge duplicated as two vertex pairs. The
        // smooth pass must merge the duplicates by position.
        Vector3 a = Vector3.Zero;
        Vector3 b = new(1f, 0f, 0f);
        Vector3 c = new(0.5f, 1f, 0f);
        Vector3 d = new(0.5f, 0f, 1f);
        builder.SetVertices([
            new Vertex(a),
            new Vertex(b),
            new Vertex(c),
            new Vertex(a),
            new Vertex(d),
            new Vertex(b),
        ]);
        builder.SetIndices([0, 1, 2, 3, 4, 5]);

        builder.ComputeNormals();

        Vector3 expected = Vector3.Normalize(new Vector3(0f, 1f, 1f));
        AssertVector(expected, builder.Vertices[0].Normal);
        AssertVector(expected, builder.Vertices[1].Normal);
        AssertVector(new Vector3(0f, 0f, 1f), builder.Vertices[2].Normal);
        AssertVector(expected, builder.Vertices[3].Normal);
        AssertVector(new Vector3(0f, 1f, 0f), builder.Vertices[4].Normal);
        AssertVector(expected, builder.Vertices[5].Normal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SmoothNormalsWeighFacesByArea() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        // A large +Z face and a small +Y face share the origin edge, so the +Z contribution is larger.
        builder.SetVertices([
            new Vertex(Vector3.Zero),
            new Vertex(new Vector3(4f, 0f, 0f)),
            new Vertex(new Vector3(2f, 4f, 0f)),
            new Vertex(new Vector3(0.5f, 0f, 1f)),
        ]);
        builder.SetIndices([0, 1, 2, 0, 3, 1]);

        builder.ComputeNormals();

        Vector3 normal = builder.Vertices[0].Normal;
        Assert.True(normal.Z > normal.Y, "The larger +Z face must dominate the smoothed normal.");
        AssertVector(Vector3.Normalize(new Vector3(0f, 0f, 1f)), builder.Vertices[2].Normal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SmoothNormalsShareTheCornerOfACubeAcrossDuplicatedVertices() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        Vector3 origin = Vector3.Zero;
        Vector3 x = new(1f, 0f, 0f);
        Vector3 y = new(0f, 1f, 0f);
        Vector3 z = new(0f, 0f, 1f);
        // Three cube faces meet at the origin, each with its own copy of the shared corner.
        builder.SetVertices([
            new Vertex(origin), new Vertex(x), new Vertex(y),
            new Vertex(origin), new Vertex(y), new Vertex(z),
            new Vertex(origin), new Vertex(z), new Vertex(x),
        ]);
        builder.SetIndices([0, 1, 2, 3, 4, 5, 6, 7, 8]);

        builder.ComputeNormals();

        Vector3 corner = Vector3.Normalize(Vector3.One);
        AssertVector(corner, builder.Vertices[0].Normal);
        AssertVector(corner, builder.Vertices[3].Normal);
        AssertVector(corner, builder.Vertices[6].Normal);
        AssertVector(Vector3.Normalize(new Vector3(0f, 1f, 1f)), builder.Vertices[1].Normal);
        AssertVector(Vector3.Normalize(new Vector3(1f, 0f, 1f)), builder.Vertices[4].Normal);
        AssertVector(Vector3.Normalize(new Vector3(1f, 1f, 0f)), builder.Vertices[7].Normal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FlatNormalsSplitEveryTriangleAndPreserveTheOtherAttributes() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        Vertex a = new(Vector3.Zero, uv: new Vector2(0.1f, 0.2f), color: Color.Red);
        Vertex b = new(new Vector3(1f, 0f, 0f), uv: new Vector2(0.3f, 0.4f), color: Color.Green);
        Vertex c = new(new Vector3(0f, 1f, 0f), color: Color.Blue);
        Vertex d = new(new Vector3(0.5f, 0f, 1f), color: Color.Yellow);
        builder.SetVertices([a, b, c, d]);
        builder.SetIndices([0, 1, 2, 0, 1, 3]);

        builder.ComputeNormals(MeshNormalMode.Flat);

        Assert.Equal(6, builder.Vertices.Count);
        Assert.Equal(6, builder.Indices.Count);
        Assert.Equal(new uint[] { 0u, 1u, 2u, 3u, 4u, 5u }, builder.Indices);
        AssertVector(new Vector3(0f, 0f, 1f), builder.Vertices[0].Normal);
        AssertVector(new Vector3(0f, 0f, 1f), builder.Vertices[1].Normal);
        AssertVector(new Vector3(0f, 0f, 1f), builder.Vertices[2].Normal);
        AssertVector(new Vector3(0f, -1f, 0f), builder.Vertices[3].Normal);
        AssertVector(new Vector3(0f, -1f, 0f), builder.Vertices[4].Normal);
        AssertVector(new Vector3(0f, -1f, 0f), builder.Vertices[5].Normal);
        Assert.Equal(a.Position, builder.Vertices[0].Position);
        Assert.Equal(a.Uv, builder.Vertices[0].Uv);
        Assert.Equal(Color.Red, builder.Vertices[0].Color);
        Assert.Equal(Color.Green, builder.Vertices[1].Color);
        Assert.Equal(Color.Blue, builder.Vertices[2].Color);
        Assert.Equal(Color.Yellow, builder.Vertices[5].Color);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FlatNormalsKeepAStripValidWithDegenerateConnectingIndices() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder(PrimitiveTopology.TriangleStrip);
        builder.SetVertices(CreateQuadPositions());
        // A quad strip: (A, B, D) and (D, B, C) both wind counter-clockwise around +Z.
        builder.SetIndices([0, 1, 3, 2]);

        builder.ComputeNormals(MeshNormalMode.Flat);

        Assert.Equal(6, builder.Vertices.Count);
        Assert.Equal(new uint[] { 0u, 1u, 2u, 2u, 3u, 4u, 5u }, builder.Indices);
        foreach (Vertex vertex in builder.Vertices) {
            AssertVector(new Vector3(0f, 0f, 1f), vertex.Normal);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TangentIsTheUVTangentWithHandedness() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero, uv: new Vector2(0f, 0f)),
            new Vertex(new Vector3(1f, 0f, 0f), uv: new Vector2(1f, 0f)),
            new Vertex(new Vector3(1f, 1f, 0f), uv: new Vector2(1f, 1f)),
            new Vertex(new Vector3(0f, 1f, 0f), uv: new Vector2(0f, 1f)),
        ]);
        builder.SetIndices([0, 1, 2, 0, 2, 3]);
        builder.ComputeNormals();

        builder.ComputeTangents();

        foreach (Vertex vertex in builder.Vertices) {
            AssertVector(new Vector3(1f, 0f, 0f), new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z));
            Assert.Equal(1f, vertex.Tangent.W);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DegenerateTrianglesLeaveAZeroTangent() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero, uv: Vector2.Zero),
            new Vertex(new Vector3(1f, 0f, 0f), uv: Vector2.Zero),
            new Vertex(new Vector3(0f, 1f, 0f), uv: Vector2.Zero),
        ]);
        builder.SetIndices([0, 1, 2]);
        builder.ComputeNormals();

        builder.ComputeTangents();

        foreach (Vertex vertex in builder.Vertices) {
            Assert.Equal(Vector4.Zero, vertex.Tangent);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TangentsRequireNonZeroNormals() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero, normal: Vector3.Zero, uv: Vector2.Zero),
            new Vertex(new Vector3(1f, 0f, 0f), normal: Vector3.Zero, uv: new Vector2(1f, 0f)),
            new Vertex(new Vector3(0f, 1f, 0f), normal: Vector3.Zero, uv: new Vector2(0f, 1f)),
        ]);
        builder.SetIndices([0, 1, 2]);

        Assert.Throws<InvalidOperationException>(() => builder.ComputeTangents());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputeBoundsUsesEveryVertexPosition() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(new Vector3(1f, 2f, 3f)),
            new Vertex(new Vector3(-1f, -2f, -3f)),
            new Vertex(new Vector3(0.5f, 0f, 0f)),
        ]);
        builder.SetIndices([0, 1, 2]);

        BoundingBox bounds = builder.ComputeBounds();

        Assert.Equal(new Vector3(-1f, -2f, -3f), bounds.Min);
        Assert.Equal(new Vector3(1f, 2f, 3f), bounds.Max);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputeBoundsRequiresAVertex() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.ComputeBounds());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuildAsyncCreatesAProceduralMeshWithCpuDataAndBounds() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        Vertex[] vertices = CreateQuadPositions();
        uint[] indices = [0, 1, 2, 0, 2, 3];
        builder.SetVertices(vertices);
        builder.SetIndices(indices);

        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);

        try {
            Assert.StartsWith("/_procedural/mesh/", mesh.Path, StringComparison.Ordinal);
            Assert.True(mesh.IsProcedural);
            Assert.Equal(PrimitiveTopology.TriangleList, mesh.Topology);
            Assert.Equal(4, mesh.VertexCount);
            Assert.Equal(6, mesh.IndexCount);
            Assert.Equal(new BoundingBox(Vector3.Zero, new Vector3(1f, 1f, 0f)), mesh.Bounds);
            AssertVerticesEqual(vertices, mesh.Vertices.Span);
            Assert.Equal(indices, mesh.Indices.ToArray());
            Assert.Equal(1, harness.Worker.UploadCallCount);
            MeshGpuUpload upload = Assert.IsType<MeshGpuUpload>(harness.Worker.UploadCalls[0]);
            Assert.Equal(mesh.Path[1..], upload.Path);
            Assert.Equal((4 * 88) + (6 * sizeof(uint)), upload.VertexAndIndexData.Length);
        } finally {
            mesh.Dispose();
        }

        Assert.True(mesh.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = mesh.Vertices);
        Assert.Throws<ObjectDisposedException>(() => _ = mesh.Indices);
        Assert.Throws<ObjectDisposedException>(() => _ = mesh.IsProcedural);
        Assert.Throws<ObjectDisposedException>(() => mesh.Dispose());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuildAsyncKeepsExplicitlyComputedBounds() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices(CreateQuadPositions());
        builder.SetIndices([0, 1, 2, 0, 2, 3]);
        BoundingBox explicitBounds = builder.ComputeBounds();
        builder.Vertices.Add(new Vertex(new Vector3(100f, 100f, 100f)));

        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(explicitBounds, mesh.Bounds);
            Assert.Equal(5, mesh.VertexCount);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuildAsyncReturnsOnlyAfterTheDeferredGpuUploadCompletes() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        Task<Mesh> build = builder.BuildAsync(TestContext.Current.CancellationToken);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);

        Assert.False(build.IsCompleted);
        operation.Complete();
        Mesh mesh = await build.WaitAsync(_timeout, TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.IsProcedural);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledBuildDestroysTheResourceAndLeavesTheBuilderReusable() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);
        using CancellationTokenSource cancellation = new();

        Task<Mesh> build = builder.BuildAsync(cancellation.Token);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => build.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.True(
            SpinWait.SpinUntil(() => harness.Worker.CancelCalls.Count == 1, _timeout),
            "The bridge did not cancel the upload operation.");
        Assert.True(
            SpinWait.SpinUntil(() => harness.Bridge.LiveResourceCount == 0, _timeout),
            "The canceled upload left a live resource.");

        harness.Worker.DeferUploads = false;
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.IsProcedural);
            Assert.NotEqual("/_procedural/mesh/1", mesh.Path);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task PreCanceledBuildStartsNoUpload() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => builder.BuildAsync(cancellation.Token));

        Assert.Equal(0, harness.Worker.UploadCallCount);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FailedUploadDestroysTheResourceAndLeavesTheBuilderReusable() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        Task<Mesh> build = builder.BuildAsync(TestContext.Current.CancellationToken);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);
        operation.Fail(new AssetLoadException(
            "The test upload failed.",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            null));

        AssetLoadException failure = await Assert.ThrowsAsync<AssetLoadException>(
            () => build.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.Equal(DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED, failure.DiagnosticCode);
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        harness.Worker.DeferUploads = false;
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.IsProcedural);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task UploadExceptionFailsTheBuildWithoutAResource() {
        using ProceduralTestHarness harness = new();
        harness.Worker.UploadException = new InvalidOperationException("The fake worker failed.");
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        await Assert.ThrowsAsync<AssetLoadException>(
            () => builder.BuildAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, harness.Bridge.LiveResourceCount);

        harness.Worker.UploadException = null;
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.IsProcedural);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BuilderIsReusableAndEveryBuildGetsAFreshPath() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        Mesh first = await builder.BuildAsync(TestContext.Current.CancellationToken);
        Mesh second;
        try {
            builder.ComputeNormals();
            builder.Vertices[0] = new Vertex(new Vector3(-1f, 0f, 0f));
            second = await builder.BuildAsync(TestContext.Current.CancellationToken);
        } catch {
            first.Dispose();
            throw;
        }

        try {
            Assert.NotEqual(first.Path, second.Path);
            Assert.NotEqual(first.Vertices.Span[0].Position, second.Vertices.Span[0].Position);
        } finally {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GenerateCollisionsMarksTheBuiltMesh() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        builder.GenerateCollisions();
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);

        try {
            Assert.True(mesh.HasCollisionAcceleration);
        } finally {
            mesh.Dispose();
        }

        Assert.Throws<ObjectDisposedException>(() => _ = mesh.HasCollisionAcceleration);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task MeshWithoutGenerateCollisionsHasNoAccelerationStructure() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);

        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.False(mesh.HasCollisionAcceleration);
        } finally {
            mesh.Dispose();
        }
    }

    [Theory]
    [InlineData(PrimitiveTopology.LineList)]
    [InlineData(PrimitiveTopology.LineStrip)]
    [InlineData(PrimitiveTopology.PointList)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateCollisionsRejectsLineAndPointTopologies(PrimitiveTopology topology) {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.Topology = topology;
        builder.SetVertices(CreateTrianglePositions());
        builder.SetIndices([0u, 1u, 2u]);

        Assert.Throws<NotSupportedException>(() => builder.GenerateCollisions());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GenerateCollisionsIsIdempotentAndDoesNotMutateTheGeometry() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateQuad(builder);
        Vertex[] vertices = [.. builder.Vertices];
        uint[] indices = [.. builder.Indices];

        builder.GenerateCollisions();
        builder.GenerateCollisions();

        AssertVerticesEqual(vertices, builder.Vertices.ToArray());
        Assert.Equal(indices, builder.Indices.ToArray());
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.HasCollisionAcceleration);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GenerateCollisionsPersistsForLaterBuildsFromTheSameBuilder() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);
        builder.GenerateCollisions();

        Mesh first = await builder.BuildAsync(TestContext.Current.CancellationToken);
        Mesh second;
        try {
            builder.Vertices[0] = new Vertex(new Vector3(-1f, 0f, 0f));
            second = await builder.BuildAsync(TestContext.Current.CancellationToken);
        } catch {
            first.Dispose();
            throw;
        }

        try {
            Assert.True(first.HasCollisionAcceleration);
            Assert.True(second.HasCollisionAcceleration);
            Ray ray = new(new Vector3(-0.25f, 0.25f, 1f), -Vector3.UnitZ);
            Assert.False(Collision.GetRayMesh(ray, first, Matrix4x4.Identity).Hit);
            Assert.True(Collision.GetRayMesh(ray, second, Matrix4x4.Identity).Hit);
        } finally {
            first.Dispose();
            second.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task FullyDegenerateGeometryBuildsWithoutAnAccelerationStructure() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.SetVertices([
            new Vertex(Vector3.Zero),
            new Vertex(new Vector3(1f, 0f, 0f)),
            new Vertex(new Vector3(2f, 0f, 0f)),
        ]);
        builder.SetIndices([0u, 1u, 2u]);
        builder.GenerateCollisions();

        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.False(mesh.HasCollisionAcceleration);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CanceledCollisionBuildLeavesNoAssetAndTheBuilderReusable() {
        using ProceduralTestHarness harness = new();
        harness.Worker.DeferUploads = true;
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        CreateTriangle(builder);
        builder.GenerateCollisions();
        using CancellationTokenSource cancellation = new();

        Task<Mesh> build = builder.BuildAsync(cancellation.Token);
        FakeGpuAssetUploadOperation operation = harness.Worker.WaitForUpload(_timeout);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => build.WaitAsync(_timeout, TestContext.Current.CancellationToken));
        Assert.True(
            SpinWait.SpinUntil(() => harness.Worker.CancelCalls.Count == 1, _timeout),
            "The bridge did not cancel the upload operation.");
        Assert.True(
            SpinWait.SpinUntil(() => harness.Bridge.LiveResourceCount == 0, _timeout),
            "The canceled upload left a live resource.");

        harness.Worker.DeferUploads = false;
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.True(mesh.HasCollisionAcceleration);
        } finally {
            mesh.Dispose();
        }
    }

    private static Vertex[] CreateQuadPositions() => [
        new Vertex(Vector3.Zero),
        new Vertex(new Vector3(1f, 0f, 0f)),
        new Vertex(new Vector3(1f, 1f, 0f)),
        new Vertex(new Vector3(0f, 1f, 0f)),
    ];

    private static Vertex[] CreateTrianglePositions() => [
        new Vertex(Vector3.Zero),
        new Vertex(new Vector3(1f, 0f, 0f)),
        new Vertex(new Vector3(0f, 1f, 0f)),
    ];

    private static void CreateTriangle(MeshBuilder builder) {
        builder.SetVertices(CreateTrianglePositions());
        builder.SetIndices([0, 1, 2]);
    }

    private static void CreateQuad(MeshBuilder builder) {
        builder.SetVertices(CreateQuadPositions());
        builder.SetIndices([0, 1, 2, 0, 2, 3]);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual) {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private static void AssertVerticesEqual(Vertex[] expected, ReadOnlySpan<Vertex> actual) {
        Assert.Equal(expected.Length, actual.Length);
        for (int index = 0; index < expected.Length; index++) {
            Vertex expectedVertex = expected[index];
            Vertex actualVertex = actual[index];
            Assert.Equal(expectedVertex.Position, actualVertex.Position);
            Assert.Equal(expectedVertex.Normal, actualVertex.Normal);
            Assert.Equal(expectedVertex.Uv, actualVertex.Uv);
            Assert.Equal(expectedVertex.Uv1, actualVertex.Uv1);
            Assert.Equal(expectedVertex.Uv2, actualVertex.Uv2);
            Assert.Equal(expectedVertex.Uv3, actualVertex.Uv3);
            Assert.Equal(expectedVertex.Color, actualVertex.Color);
            Assert.Equal(expectedVertex.Tangent, actualVertex.Tangent);
        }
    }
}
