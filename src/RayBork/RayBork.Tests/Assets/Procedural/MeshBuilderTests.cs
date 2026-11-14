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

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GeneratePolyCreatesAnXzFanWithUpNormals() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GeneratePoly(4, 2f);

        Assert.Equal(5, builder.Vertices.Count);
        Assert.Equal(12, builder.Indices.Count);
        Assert.Equal(PrimitiveTopology.TriangleList, builder.Topology);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            Assert.Equal(0f, vertex.Position.Y);
            AssertVector(Vector3.UnitY, vertex.Normal);
        }

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-2f, 0f, -2f), bounds.Min);
        AssertVector(new Vector3(2f, 0f, 2f), bounds.Max);
        Assert.Equal(new Vector2(0.5f, 0f), builder.Vertices[1].Uv);
        Assert.Equal(new Vector2(0.5f, 0.5f), builder.Vertices[0].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GeneratePlaneCreatesAnXzGridWithUpNormals() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GeneratePlane(2f, 4f, 2, 3);

        Assert.Equal(12, builder.Vertices.Count);
        Assert.Equal(36, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            AssertVector(Vector3.UnitY, vertex.Normal);
        }

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-1f, 0f, -2f), bounds.Min);
        AssertVector(new Vector3(1f, 0f, 2f), bounds.Max);
        Assert.Equal(new Vector2(0f, 0f), builder.Vertices[0].Uv);
        Assert.Equal(new Vector2(1f, 1f), builder.Vertices[^1].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateCubeCreatesACenteredBoxWithPerFaceNormals() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateCube(2f, 4f, 6f);

        Assert.Equal(24, builder.Vertices.Count);
        Assert.Equal(36, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == Vector3.UnitX));
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == -Vector3.UnitX));
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == Vector3.UnitY));
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == -Vector3.UnitY));
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == Vector3.UnitZ));
        Assert.Equal(4, builder.Vertices.Count(vertex => vertex.Normal == -Vector3.UnitZ));
        Assert.Equal(new Vector2(1f, 1f), builder.Vertices[0].Uv);

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-1f, -2f, -3f), bounds.Min);
        AssertVector(new Vector3(1f, 2f, 3f), bounds.Max);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateSphereCreatesAUvSphereWithYAxisPoles() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateSphere(2f, 8, 16);

        Assert.Equal(2 + (7 * 17), builder.Vertices.Count);
        Assert.Equal(6 * 16 * 7, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            Assert.Equal(2f, vertex.Position.Length(), 4);
            AssertVector(Vector3.Normalize(vertex.Position), vertex.Normal);
        }

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-2f, -2f, -2f), bounds.Min);
        AssertVector(new Vector3(2f, 2f, 2f), bounds.Max);
        Assert.Equal(new Vector2(0.5f, 0f), builder.Vertices[0].Uv);
        Assert.Equal(new Vector2(0.5f, 1f), builder.Vertices[^1].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateHemiSphereIsOpenAtTheEquator() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateHemiSphere(2f, 4, 8);

        Assert.Equal(1 + (4 * 9), builder.Vertices.Count);
        Assert.Equal(3 * 8 * 7, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            Assert.Equal(2f, vertex.Position.Length(), 4);
            Assert.True(vertex.Position.Y >= 0f, "The hemisphere must not extend below the equator.");
        }

        BoundingBox bounds = builder.ComputeBounds();
        Assert.Equal(0f, bounds.Min.Y, 4);
        Assert.Equal(2f, bounds.Max.Y, 4);
        Assert.Equal(2f, bounds.Max.X, 4);
        Assert.Equal(2f, bounds.Max.Z, 4);
        Assert.Equal(new Vector2(1f, 1f), builder.Vertices[^1].Uv);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateCylinderHasSideQuadsAndBothCaps() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateCylinder(1f, 2f, 8);

        Assert.Equal((4 * 8) + 4, builder.Vertices.Count);
        Assert.Equal(12 * 8, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        Assert.Equal(18, builder.Vertices.Count(vertex => vertex.Normal.Y == 0f));
        Assert.Equal(9, builder.Vertices.Count(vertex => vertex.Normal == Vector3.UnitY));
        Assert.Equal(9, builder.Vertices.Count(vertex => vertex.Normal == -Vector3.UnitY));

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-1f, 0f, -1f), bounds.Min);
        AssertVector(new Vector3(1f, 2f, 1f), bounds.Max);
        Assert.Equal(0f, builder.Vertices[0].Uv.Y);
        Assert.Equal(1f, builder.Vertices[1].Uv.Y);
        Assert.Equal(8, CountCapTriangles(builder, Vector3.UnitY));
        Assert.Equal(8, CountCapTriangles(builder, -Vector3.UnitY));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateConeHasASmoothSideAndABaseCap() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateCone(1f, 3f, 8);

        Assert.Equal((3 * 8) + 3, builder.Vertices.Count);
        Assert.Equal(6 * 8, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        Assert.Equal(9, builder.Vertices.Count(vertex => vertex.Position.Y == 3f));
        Assert.Equal(18, builder.Vertices.Count(vertex => vertex.Normal.Y > 0f));
        Assert.Equal(9, builder.Vertices.Count(vertex => vertex.Normal == -Vector3.UnitY));
        Assert.Equal(8, CountCapTriangles(builder, -Vector3.UnitY));

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-1f, 0f, -1f), bounds.Min);
        AssertVector(new Vector3(1f, 3f, 1f), bounds.Max);
        Assert.Equal(0f, builder.Vertices[0].Uv.Y);
        Assert.Equal(1f, builder.Vertices[1].Uv.Y);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateTorusCreatesARingAroundTheYAxis() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateTorus(2f, 0.5f, 16, 8);

        Assert.Equal(17 * 9, builder.Vertices.Count);
        Assert.Equal(16 * 8 * 6, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            float radial = MathF.Sqrt(
                (vertex.Position.X * vertex.Position.X) + (vertex.Position.Z * vertex.Position.Z));
            float tubeDistance = MathF.Sqrt(
                ((radial - 2f) * (radial - 2f)) + (vertex.Position.Y * vertex.Position.Y));
            Assert.Equal(0.5f, tubeDistance, 4);
            Vector3 closest = new(
                vertex.Position.X / radial * 2f,
                0f,
                vertex.Position.Z / radial * 2f);
            AssertVector(Vector3.Normalize(vertex.Position - closest), vertex.Normal);
        }

        BoundingBox bounds = builder.ComputeBounds();
        AssertVector(new Vector3(-2.5f, -0.5f, -2.5f), bounds.Min);
        AssertVector(new Vector3(2.5f, 0.5f, 2.5f), bounds.Max);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateKnotCreatesACenteredTrefoilTube() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateKnot(1f, 0.2f, 24, 8);

        Assert.Equal(25 * 9, builder.Vertices.Count);
        Assert.Equal(24 * 8 * 6, builder.Indices.Count);
        AssertGeneratedMeshIsWellFormed(builder);
        foreach (Vertex vertex in builder.Vertices) {
            Assert.True(
                vertex.Position.Length() <= 1.2f + 1e-4f,
                "Every knot vertex must stay inside the winding radius plus the tube radius.");
        }

        BoundingBox bounds = builder.ComputeBounds();
        Assert.True(bounds.Max.X > 0.5f && bounds.Max.X <= 1.2f + 1e-4f);
        Assert.True(bounds.Min.X < -0.5f && bounds.Min.X >= -1.2f - 1e-4f);
        Assert.True(bounds.Max.Y > 0.5f && bounds.Max.Y <= 1.2f + 1e-4f);
        Assert.True(bounds.Min.Y < -0.5f && bounds.Min.Y >= -1.2f - 1e-4f);
        Assert.True(bounds.Max.Z > 0.5f && bounds.Max.Z <= 1.2f + 1e-4f);
        Assert.True(bounds.Min.Z < -0.5f && bounds.Min.Z >= -1.2f - 1e-4f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GeneratePlaneReplacesGeometryAndSwitchesToTriangleList() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder(PrimitiveTopology.LineList);
        builder.SetVertices(CreateTrianglePositions());
        builder.SetIndices([0u, 1u]);
        Assert.Throws<NotSupportedException>(() => builder.ComputeNormals());

        builder.GeneratePlane(2f, 2f, 1, 1);

        Assert.Equal(PrimitiveTopology.TriangleList, builder.Topology);
        Assert.Equal(4, builder.Vertices.Count);
        Assert.Equal(6, builder.Indices.Count);
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.Equal(PrimitiveTopology.TriangleList, mesh.Topology);
            Assert.Equal(4, mesh.VertexCount);
            Assert.Equal(6, mesh.IndexCount);
            Assert.Equal(1, harness.Worker.UploadCallCount);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task GeneratedSphereBuildsThroughTheTestHost() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateSphere(1.5f, 4, 8);
        Mesh mesh = await builder.BuildAsync(TestContext.Current.CancellationToken);
        try {
            Assert.StartsWith("/_procedural/mesh/", mesh.Path, StringComparison.Ordinal);
            Assert.True(mesh.IsProcedural);
            Assert.Equal(PrimitiveTopology.TriangleList, mesh.Topology);
            Assert.Equal(builder.Vertices.Count, mesh.VertexCount);
            Assert.Equal(builder.Indices.Count, mesh.IndexCount);
            Assert.Equal(builder.ComputeBounds(), mesh.Bounds);
            Assert.Equal(builder.Vertices[0].Position, mesh.Vertices.Span[0].Position);
            Assert.Equal(builder.Vertices[0].Normal, mesh.Vertices.Span[0].Normal);
            Assert.Equal(builder.Vertices[0].Uv, mesh.Vertices.Span[0].Uv);
            Assert.Equal(builder.Indices[0], mesh.Indices.Span[0]);
            Assert.Equal(1, harness.Worker.UploadCallCount);
        } finally {
            mesh.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GeneratedSphereSupportsTheExistingNormalAndTangentPasses() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        builder.GenerateSphere(1f, 6, 12);
        builder.ComputeNormals();
        builder.ComputeTangents();

        bool anyTangent = false;
        foreach (Vertex vertex in builder.Vertices) {
            Vector3 tangent = new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
            Assert.True(float.IsFinite(tangent.X) && float.IsFinite(tangent.Y) && float.IsFinite(tangent.Z));
            Assert.InRange(tangent.Length(), 0f, 1.0001f);
            anyTangent |= tangent.LengthSquared() > 0f;
        }

        Assert.True(anyTangent, "The generated sphere must produce at least one tangent.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateRejectsEveryShapeAfterTheAppStops() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        harness.Service.Stop();

        Assert.Throws<ObjectDisposedException>(() => builder.GeneratePoly(3, 1f));
        Assert.Throws<ObjectDisposedException>(() => builder.GeneratePlane(1f, 1f, 1, 1));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateCube(1f, 1f, 1f));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateSphere(1f, 3, 3));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateHemiSphere(1f, 3, 3));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateCylinder(1f, 1f, 3));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateCone(1f, 1f, 3));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateTorus(1f, 0.2f, 3, 3));
        Assert.Throws<ObjectDisposedException>(() => builder.GenerateKnot(1f, 0.2f, 3, 3));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateValidatesPolyAndPlaneParameters() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();
        builder.GeneratePoly(3, 1f);
        int vertexCount = builder.Vertices.Count;

        AssertOutOfRange("sides", () => builder.GeneratePoly(2, 1f));
        AssertOutOfRange("radius", () => builder.GeneratePoly(3, 0f));
        AssertOutOfRange("radius", () => builder.GeneratePoly(3, -1f));
        AssertOutOfRange("radius", () => builder.GeneratePoly(3, float.NaN));
        AssertOutOfRange("radius", () => builder.GeneratePoly(3, float.PositiveInfinity));
        Assert.Equal(vertexCount, builder.Vertices.Count);

        AssertOutOfRange("width", () => builder.GeneratePlane(0f, 1f, 1, 1));
        AssertOutOfRange("length", () => builder.GeneratePlane(1f, -1f, 1, 1));
        AssertOutOfRange("resX", () => builder.GeneratePlane(1f, 1f, 0, 1));
        AssertOutOfRange("resZ", () => builder.GeneratePlane(1f, 1f, 1, 0));
        AssertOutOfRange("width", () => builder.GeneratePlane(float.NaN, 1f, 1, 1));
        AssertOutOfRange("length", () => builder.GeneratePlane(1f, float.PositiveInfinity, 1, 1));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateValidatesCubeParameters() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        AssertOutOfRange("width", () => builder.GenerateCube(0f, 1f, 1f));
        AssertOutOfRange("height", () => builder.GenerateCube(1f, -1f, 1f));
        AssertOutOfRange("length", () => builder.GenerateCube(1f, 1f, float.NaN));
        AssertOutOfRange("width", () => builder.GenerateCube(float.PositiveInfinity, 1f, 1f));
        AssertOutOfRange("height", () => builder.GenerateCube(1f, float.NegativeInfinity, 1f));
        Assert.Empty(builder.Vertices);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateValidatesSphereAndHemiSphereParameters() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        AssertOutOfRange("radius", () => builder.GenerateSphere(0f, 3, 3));
        AssertOutOfRange("radius", () => builder.GenerateSphere(float.NaN, 3, 3));
        AssertOutOfRange("rings", () => builder.GenerateSphere(1f, 2, 3));
        AssertOutOfRange("slices", () => builder.GenerateSphere(1f, 3, 2));
        AssertOutOfRange("radius", () => builder.GenerateHemiSphere(-1f, 3, 3));
        AssertOutOfRange("rings", () => builder.GenerateHemiSphere(1f, 2, 3));
        AssertOutOfRange("slices", () => builder.GenerateHemiSphere(1f, 3, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateValidatesCylinderAndConeParameters() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        AssertOutOfRange("radius", () => builder.GenerateCylinder(0f, 1f, 3));
        AssertOutOfRange("height", () => builder.GenerateCylinder(1f, float.NaN, 3));
        AssertOutOfRange("slices", () => builder.GenerateCylinder(1f, 1f, 2));
        AssertOutOfRange("radius", () => builder.GenerateCone(float.PositiveInfinity, 1f, 3));
        AssertOutOfRange("height", () => builder.GenerateCone(1f, 0f, 3));
        AssertOutOfRange("slices", () => builder.GenerateCone(1f, 1f, 2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GenerateValidatesTorusAndKnotParameters() {
        using ProceduralTestHarness harness = new();
        MeshBuilder builder = harness.Service.CreateMeshBuilder();

        AssertOutOfRange("radius", () => builder.GenerateTorus(0f, 0.2f, 3, 3));
        AssertOutOfRange("size", () => builder.GenerateTorus(1f, -0.2f, 3, 3));
        AssertOutOfRange("radSeg", () => builder.GenerateTorus(1f, 0.2f, 2, 3));
        AssertOutOfRange("sides", () => builder.GenerateTorus(1f, 0.2f, 3, 2));
        AssertOutOfRange("radius", () => builder.GenerateKnot(float.NaN, 0.2f, 3, 3));
        AssertOutOfRange("size", () => builder.GenerateKnot(1f, float.PositiveInfinity, 3, 3));
        AssertOutOfRange("radSeg", () => builder.GenerateKnot(1f, 0.2f, 2, 3));
        AssertOutOfRange("sides", () => builder.GenerateKnot(1f, 0.2f, 3, 2));
    }

    private static void AssertGeneratedMeshIsWellFormed(MeshBuilder builder) {
        Assert.True(builder.Vertices.Count > 0);
        Assert.True(builder.Indices.Count >= 3);
        Assert.Equal(0, builder.Indices.Count % 3);
        Assert.Equal(PrimitiveTopology.TriangleList, builder.Topology);

        foreach (Vertex vertex in builder.Vertices) {
            Assert.True(float.IsFinite(vertex.Position.X)
                && float.IsFinite(vertex.Position.Y)
                && float.IsFinite(vertex.Position.Z));
            Assert.True(float.IsFinite(vertex.Normal.X)
                && float.IsFinite(vertex.Normal.Y)
                && float.IsFinite(vertex.Normal.Z));
            Assert.True(float.IsFinite(vertex.Uv.X) && float.IsFinite(vertex.Uv.Y));
            Assert.Equal(1f, vertex.Normal.Length(), 4);
            Assert.InRange(vertex.Uv.X, 0f, 1f);
            Assert.InRange(vertex.Uv.Y, 0f, 1f);
        }

        BoundingBox bounds = builder.ComputeBounds();
        Assert.True(bounds.Min.X <= bounds.Max.X);
        Assert.True(bounds.Min.Y <= bounds.Max.Y);
        Assert.True(bounds.Min.Z <= bounds.Max.Z);

        for (int triangle = 0; triangle < builder.Indices.Count / 3; triangle++) {
            int offset = triangle * 3;
            int index0 = checked((int)builder.Indices[offset]);
            int index1 = checked((int)builder.Indices[offset + 1]);
            int index2 = checked((int)builder.Indices[offset + 2]);
            Assert.InRange(index0, 0, builder.Vertices.Count - 1);
            Assert.InRange(index1, 0, builder.Vertices.Count - 1);
            Assert.InRange(index2, 0, builder.Vertices.Count - 1);
            Vertex vertex0 = builder.Vertices[index0];
            Vertex vertex1 = builder.Vertices[index1];
            Vertex vertex2 = builder.Vertices[index2];
            Vector3 face = Vector3.Cross(
                vertex1.Position - vertex0.Position,
                vertex2.Position - vertex0.Position);
            Assert.True(face.LengthSquared() > 0f, $"Triangles {triangle} is degenerate.");
            Vector3 averageNormal = Vector3.Normalize(vertex0.Normal + vertex1.Normal + vertex2.Normal);
            Assert.True(
                Vector3.Dot(Vector3.Normalize(face), averageNormal) > 0f,
                $"Triangle {triangle} does not wind counter-clockwise around its normals.");
        }
    }

    private static int CountCapTriangles(MeshBuilder builder, Vector3 normal) {
        int count = 0;
        for (int triangle = 0; triangle < builder.Indices.Count / 3; triangle++) {
            int offset = triangle * 3;
            if (builder.Vertices[(int)builder.Indices[offset]].Normal == normal
                && builder.Vertices[(int)builder.Indices[offset + 1]].Normal == normal
                && builder.Vertices[(int)builder.Indices[offset + 2]].Normal == normal) {
                count++;
            }
        }

        return count;
    }

    private static void AssertOutOfRange(string parameterName, Action action) {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.Equal(parameterName, exception.ParamName);
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
