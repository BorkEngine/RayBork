using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Rendering;

public sealed class BatchPlannerTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GroupsInstancesByMeshShaderRenderStateAndTopology() {
        Mesh meshA = RenderingTestAssets.CreateMesh("meshes/a");
        Mesh meshB = RenderingTestAssets.CreateMesh("meshes/b");
        Mesh strip = RenderingTestAssets.CreateMesh("meshes/strip", PrimitiveTopology.TriangleStrip);
        Shader shaderA = RenderingTestAssets.CreateShader("shaders/a");
        Shader shaderB = RenderingTestAssets.CreateShader("shaders/b");
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(packet, pass, meshA, shaderA, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, meshA, shaderA, RenderState.Opaque, Translation(1f, 0f, -5f));
        AddDraw(packet, pass, meshB, shaderA, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, meshA, shaderB, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, meshA, shaderA, RenderState.AlphaBlend, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, meshA, shaderA, RenderState.Opaque, Translation(2f, 0f, -5f));
        AddDraw(packet, pass, strip, shaderA, RenderState.Opaque, Translation(0f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Single(plan.Passes);
        IReadOnlyList<PlannedBatch> batches = plan.Passes[0].Batches;
        Assert.Equal(5, batches.Count);
        Assert.Equal(3, batches[0].InstanceCount);
        Assert.Equal(1, batches[1].InstanceCount);
        Assert.Equal(1, batches[2].InstanceCount);
        Assert.Equal(1, batches[3].InstanceCount);
        Assert.True(batches[0].IsOpaque);
        Assert.False(batches[4].IsOpaque);
        Assert.Equal(PrimitiveTopology.TriangleStrip, batches[3].Topology);
        Assert.Equal(7, plan.Instances);
        Assert.Equal(5, plan.DrawCalls);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FrustumCullingRemovesInstancesOutsideTheView() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(
            packet,
            pass,
            mesh,
            shader,
            RenderState.Opaque,
            Translation(0f, 0f, -5f),
            Translation(0f, 0f, 5f),
            Translation(1000f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Equal(1, plan.Instances);
        Assert.Equal(2, plan.CulledInstances);
        Assert.Equal(0, plan.Passes[0].Batches[0].Instances[0].InstanceIndex);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpaqueBatchesKeepFirstRecordOrder() {
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(packet, pass, secondMesh, shader, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, firstMesh, shader, RenderState.Opaque, Translation(0f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Collection(
            plan.Passes[0].Batches,
            batch => Assert.Same(secondMesh.Lifetime, batch.Mesh),
            batch => Assert.Same(firstMesh.Lifetime, batch.Mesh));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TransparentBatchesAreSortedBackToFrontByNearestDepth() {
        Mesh nearMesh = RenderingTestAssets.CreateMesh("meshes/near");
        Mesh farMesh = RenderingTestAssets.CreateMesh("meshes/far");
        Mesh middleMesh = RenderingTestAssets.CreateMesh("meshes/middle");
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(packet, pass, nearMesh, shader, RenderState.AlphaBlend, Translation(0f, 0f, -2f));
        AddDraw(packet, pass, farMesh, shader, RenderState.Additive, Translation(0f, 0f, -20f));
        AddDraw(packet, pass, middleMesh, shader, RenderState.Multiply, Translation(0f, 0f, -8f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Collection(
            plan.Passes[0].Batches,
            batch => Assert.Same(farMesh.Lifetime, batch.Mesh),
            batch => Assert.Same(middleMesh.Lifetime, batch.Mesh),
            batch => Assert.Same(nearMesh.Lifetime, batch.Mesh));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InstancesInsideATransparentBatchAreSortedBackToFront() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(
            packet,
            pass,
            mesh,
            shader,
            RenderState.AlphaBlend,
            Translation(0f, 0f, -2f),
            Translation(0f, 0f, -10f),
            Translation(0f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
        Assert.Equal([1, 2, 0], batch.Instances.Select(instance => instance.InstanceIndex));
        Assert.True(batch.Instances[0].Depth > batch.Instances[1].Depth);
        Assert.True(batch.Instances[1].Depth > batch.Instances[2].Depth);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EqualDepthsKeepRecordingOrder() {
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(packet, pass, firstMesh, shader, RenderState.AlphaBlend, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, secondMesh, shader, RenderState.AlphaBlend, Translation(0f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Collection(
            plan.Passes[0].Batches,
            batch => Assert.Same(firstMesh.Lifetime, batch.Mesh),
            batch => Assert.Same(secondMesh.Lifetime, batch.Mesh));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SplittingByInstanceLimitPreservesInstanceOrder() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);
        AddDraw(
            packet,
            pass,
            mesh,
            shader,
            RenderState.Opaque,
            Translation(0f, 0f, -1f),
            Translation(0f, 0f, -2f),
            Translation(0f, 0f, -3f),
            Translation(0f, 0f, -4f),
            Translation(0f, 0f, -5f));

        BatchPlanner planner = new(new BatchPlanningOptions(maxInstancesPerBatch: 2, maxInstanceBytesPerBatch: int.MaxValue));
        FramePlan plan = planner.Plan(packet);

        Assert.Equal(3, plan.Passes[0].Batches.Count);
        Assert.Equal([2, 2, 1], plan.Passes[0].Batches.Select(batch => batch.InstanceCount));
        Assert.Equal(3, plan.DrawCalls);
        Assert.Equal(5, plan.Instances);
        Assert.Equal(
            [0, 1, 2, 3, 4],
            plan.Passes[0].Batches.SelectMany(batch => batch.Instances).Select(instance => instance.InstanceIndex));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SplittingByByteLimitPreservesInstanceOrder() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);
        int bytesPerInstance = shader.InstanceDataSize;
        AddDraw(
            packet,
            pass,
            mesh,
            shader,
            RenderState.Opaque,
            Translation(0f, 0f, -1f),
            Translation(0f, 0f, -2f),
            Translation(0f, 0f, -3f));

        BatchPlanner planner = new(new BatchPlanningOptions(
            maxInstancesPerBatch: 100,
            maxInstanceBytesPerBatch: bytesPerInstance * 2));
        FramePlan plan = planner.Plan(packet);

        Assert.Equal([2, 1], plan.Passes[0].Batches.Select(batch => batch.InstanceCount));
        Assert.Equal(
            [0, 1, 2],
            plan.Passes[0].Batches.SelectMany(batch => batch.Instances).Select(instance => instance.InstanceIndex));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CountsTrianglesPerTopology() {
        Mesh triangleList = RenderingTestAssets.CreateMesh("meshes/tri-list", PrimitiveTopology.TriangleList, indexCount: 6);
        Mesh triangleStrip = RenderingTestAssets.CreateMesh("meshes/tri-strip", PrimitiveTopology.TriangleStrip, indexCount: 5);
        Mesh lineList = RenderingTestAssets.CreateMesh("meshes/line-list", PrimitiveTopology.LineList, indexCount: 4);
        Mesh pointList = RenderingTestAssets.CreateMesh("meshes/point-list", PrimitiveTopology.PointList, indexCount: 3);
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass pass = BeginPass(packet);

        AddDraw(packet, pass, triangleList, shader, RenderState.Opaque, Translation(0f, 0f, -5f), Translation(1f, 0f, -5f));
        AddDraw(packet, pass, triangleStrip, shader, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, lineList, shader, RenderState.Opaque, Translation(0f, 0f, -5f));
        AddDraw(packet, pass, pointList, shader, RenderState.Opaque, Translation(0f, 0f, -5f));

        FramePlan plan = new BatchPlanner().Plan(packet);

        // 2 triangles x 2 instances + 3 triangles from the strip.
        Assert.Equal(7, plan.Triangles);
        Assert.Equal(5, plan.Instances);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StatisticsSumAcrossPassesAndReportDrawCallsAfterSplitting() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        RenderFramePacket packet = CreatePacket();
        CameraPass first = BeginPass(packet);
        AddDraw(packet, first, mesh, shader, RenderState.Opaque, Translation(0f, 0f, -5f), Translation(1f, 0f, -5f));
        CameraPass second = BeginPass(packet);
        AddDraw(packet, second, mesh, shader, RenderState.Opaque, Translation(0f, 0f, -5f));

        BatchPlanner planner = new(new BatchPlanningOptions(maxInstancesPerBatch: 1, maxInstanceBytesPerBatch: int.MaxValue));
        FramePlan plan = planner.Plan(packet);

        Assert.Equal(2, plan.Passes.Count);
        Assert.Equal(3, plan.DrawCalls);
        Assert.Equal(3, plan.Instances);
        Assert.Equal(0, plan.CulledInstances);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyFrameProducesAnEmptyPlan() {
        RenderFramePacket packet = new(7);

        FramePlan plan = new BatchPlanner().Plan(packet);

        Assert.Equal(7, plan.FrameIndex);
        Assert.Empty(plan.Passes);
        Assert.Equal(0, plan.DrawCalls);
        Assert.Equal(0, plan.Instances);
        Assert.Equal(0, plan.Triangles);
        Assert.Equal(0, plan.CulledInstances);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MaterialsOverTheSameProgramShareOneBatch() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram("shaders/shared");
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        try {
            RenderFramePacket packet = CreatePacket();
            CameraPass pass = BeginPass(packet);

            AddDraw(packet, pass, mesh, first, RenderState.Opaque, Translation(0f, 0f, -5f));
            AddDraw(packet, pass, mesh, second, RenderState.Opaque, Translation(1f, 0f, -5f));

            FramePlan plan = new BatchPlanner().Plan(packet);

            PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
            Assert.Equal(2, batch.InstanceCount);
            Assert.Equal(2, plan.Instances);
            Assert.Equal(1, plan.DrawCalls);
            Assert.Same(program.Lifetime, batch.Program);
            // Each draw record carries its own instance list, so both planned instances are the first
            // instance of their record.
            Assert.Equal(
                [0, 0],
                batch.Instances.Select(instance => instance.InstanceIndex));
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerMaterialUniformsAndTexturesStayWithEachInstanceWithoutSplittingTheBatch() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram("shaders/shared", defaultValue: 0f);
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        Texture firstTexture = RenderingTestAssets.CreateTexture("textures/first");
        Texture secondTexture = RenderingTestAssets.CreateTexture("textures/second");
        try {
            OnTickThread(() => {
                first.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 1f);
                first.SetUniform(RenderingTestAssets.TEXTURE_BINDING, firstTexture);
                second.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 2f);
                second.SetUniform(RenderingTestAssets.TEXTURE_BINDING, secondTexture);
            });

            RenderFramePacket packet = CreatePacket();
            CameraPass pass = BeginPass(packet);
            AddDraw(packet, pass, mesh, first, RenderState.Opaque, Translation(0f, 0f, -5f));
            AddDraw(packet, pass, mesh, second, RenderState.Opaque, Translation(1f, 0f, -5f));

            FramePlan plan = new BatchPlanner().Plan(packet);

            PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
            Assert.Equal(2, batch.InstanceCount);
            Assert.Equal(1f, RenderingTestAssets.ReadInstanceFloat(
                first,
                batch.Instances[0].Record.InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
            Assert.Equal(2f, RenderingTestAssets.ReadInstanceFloat(
                second,
                batch.Instances[1].Record.InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
            Assert.Same(firstTexture, batch.Instances[0].Record.GetTextureBindings()[0]);
            Assert.Same(secondTexture, batch.Instances[1].Record.GetTextureBindings()[0]);
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
            if (!firstTexture.IsDisposed) {
                firstTexture.Dispose();
            }

            if (!secondTexture.IsDisposed) {
                secondTexture.Dispose();
            }
        }
    }

    private static RenderFramePacket CreatePacket() {
        RenderFramePacket packet = new(1);
        packet.BeginRecording(1.0, 1.0 / 60.0);
        return packet;
    }

    private static CameraPass BeginPass(RenderFramePacket packet, Vector3 cameraPosition = default) {
        Matrix4x4 view = CameraMath.CreateViewMatrix(new Transform(cameraPosition));
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(60f, 16f / 9f, 0.1f, 1000f);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            packet.PassCount - 1,
            view,
            projection,
            view * projection,
            cameraPosition,
            0,
            0,
            1280,
            720,
            ClearMode.ColorAndDepth,
            Color.Black);
        return pass;
    }

    private static void AddDraw(
        RenderFramePacket packet,
        CameraPass pass,
        Mesh mesh,
        Shader shader,
        RenderState renderState,
        params Matrix4x4[] worldMatrices) {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            shader.Program.Lifetime,
            renderState,
            Color.White);
        record.SetShaderState(shader.InstanceData, shader.TextureValues);
        worldMatrices.AsSpan().CopyTo(record.BeginInstances(worldMatrices.Length));
        pass.Add(record);
    }

    private static void OnTickThread(Action action) {
        TickThread.Attach();
        try {
            action();
        } finally {
            TickThread.Detach();
        }
    }

    private static Matrix4x4 Translation(float x, float y, float z)
        => Matrix4x4.CreateTranslation(x, y, z);
}
