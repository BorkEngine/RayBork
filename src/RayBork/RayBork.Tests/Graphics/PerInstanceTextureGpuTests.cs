using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the per-instance draw texture path: draw records with their own texture per
/// instance are recorded directly, submitted through the real <see cref="Vulkan12Backend"/>, and probed
/// with linear scene-target readbacks.
/// </summary>
/// <remarks>
/// <para>
/// The tests use the production GPU asset bridge and worker through
/// <see cref="PrimitiveDrawGpuHarness"/> (the engine quad and the built-in <c>/shaders/mesh</c> and
/// <c>/shaders/msdf</c> programs) or <see cref="VulkanDrawTestHarness"/> for a two-binding program
/// compiled at test time. The bindless slots, the geometry pipeline, the instance writer, and the
/// readback are the production ones. Instances of one record sit side by side and bind different
/// textures, so a correct per-instance write samples each instance's own texture; a shared-set draw would
/// sample one texture twice.
/// </para>
/// <para>
/// The draws are recorded into a detached <see cref="RenderFramePacket"/> because the public
/// <see cref="Camera"/> does not expose the per-instance texture path yet; the record API is the same one
/// the text draw path uses. A frame retains every texture it references in its retention set, exactly as
/// a recorder must, and releases the set after the backend reaches frame idleness. The tests join the
/// serialized <c>SDL</c> collection because SDL and Vulkan initialization are process global, and they
/// report a skip with the reason when the host cannot open a GPU device.
/// </para>
/// </remarks>
[Collection("SDL")]
public sealed class PerInstanceTextureGpuTests {
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private static readonly Color _instanceAColor = new(0.9f, 0.1f, 0.1f, 1f);
    private static readonly Color _instanceBColor = new(0.1f, 0.2f, 0.9f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;
    private const float CAMERA_HEIGHT = 2f;
    private const float INSTANCE_OFFSET = 1f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EachInstanceSamplesItsOwnTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture textureA = harness.CreateTexture(1, 1, _instanceAColor);
        Texture textureB = harness.CreateTexture(1, 1, _instanceBColor);
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(packet, harness.Primitives.Quad, material);
        record.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        bindings[0] = textureA;
        bindings[1] = textureB;
        pass.Add(record);
        Retain(packet, textureA, textureB);
        packet.Seal();

        Submit(harness.Host.Backend, packet);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(1, stats.PipelineCount);

        AssertColorNear(_instanceAColor, ReadColorAt(harness, -INSTANCE_OFFSET, camera));
        AssertColorNear(_instanceBColor, ReadColorAt(harness, INSTANCE_OFFSET, camera));
        // The clear color still owns the gap between the two quads, so neither instance wrote across.
        AssertColorNear(_clearColor, harness.ReadColor(harness.Width / 2, harness.Height / 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheBuiltInMsdfProgramSamplesAPerInstanceTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        // A 1x1 field of 1 is fully covered and a field of 0 is fully transparent. The probes therefore
        // prove that the program samples each instance's own texture and applies the reconstructed
        // coverage to the alpha; they say nothing about the width of the derivative band.
        Texture covered = harness.CreateTexture(1, 1, Color.White);
        Texture empty = harness.CreateTexture(1, 1, Color.Black);
        Shader material = harness.Primitives.MsdfMaterial;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(packet, harness.Primitives.Quad, material, tint);
        record.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        bindings[0] = covered;
        bindings[1] = empty;
        pass.Add(record);
        Retain(packet, covered, empty);
        packet.Seal();

        Submit(harness.Host.Backend, packet);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);

        AssertColorNear(tint, ReadColorAt(harness, -INSTANCE_OFFSET, camera));
        AssertColorNear(_clearColor, ReadColorAt(harness, INSTANCE_OFFSET, camera));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void APartiallyCoveredGlyphPixelBlendsTheTintOverTheClearColor() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture gradient = CreateDistanceGradient(harness);
        Shader material = harness.Primitives.MsdfMaterial;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);
        // A half pixel in world units: the field's 1x1 quad spans one world unit, so this shifts the
        // crossing at the quad center onto a pixel center.
        float halfPixel = 0.5f * CAMERA_HEIGHT / harness.Height;
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(packet, harness.Primitives.Quad, material, tint);
        record.SetPerInstanceShaderState(material);
        record.BeginInstances(1)[0] = Matrix4x4.CreateTranslation(halfPixel, 0f, 0f);
        record.BeginInstanceTextureBindings()[0] = gradient;
        pass.Add(record);
        Retain(packet, gradient);
        packet.Seal();

        Submit(harness.Host.Backend, packet);

        Color halfway = new(
            (0.5f * tint.R) + (0.5f * _clearColor.R),
            (0.5f * tint.G) + (0.5f * _clearColor.G),
            (0.5f * tint.B) + (0.5f * _clearColor.B),
            1f);
        (uint x, uint y) = PixelOf(new Vector3(halfPixel, 0f, 0f), camera.ViewProjection, harness.Width, harness.Height);
        Assert.Equal(harness.Width / 2, x);
        AssertColorNear(halfway, harness.ReadColor(x, y));

        // The clamped ends of the gradient are fully outside and fully inside the field.
        AssertColorNear(_clearColor, ReadColorAt(harness, halfPixel - 0.4f, camera));
        AssertColorNear(tint, ReadColorAt(harness, halfPixel + 0.4f, camera));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheDerivativeBandSpansOnePixelAcrossTheGlyphEdge() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture gradient = CreateDistanceGradient(harness);
        Shader material = harness.Primitives.MsdfMaterial;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        // The quad center is on a pixel boundary, so the 0.5 crossing sits exactly between two pixels.
        // A half-width band puts the whole transition inside those two pixels, so both are saturated;
        // a full-width band would leave both at roughly a quarter and three quarters of the tint.
        RenderDrawRecord record = RentRecord(packet, harness.Primitives.Quad, material, tint);
        record.SetPerInstanceShaderState(material);
        record.BeginInstances(1)[0] = Matrix4x4.Identity;
        record.BeginInstanceTextureBindings()[0] = gradient;
        pass.Add(record);
        Retain(packet, gradient);
        packet.Seal();

        Submit(harness.Host.Backend, packet);

        (uint x, uint y) = PixelOf(Vector3.Zero, camera.ViewProjection, harness.Width, harness.Height);
        Assert.Equal(harness.Width / 2, x);
        AssertColorNear(_clearColor, harness.ReadColor(x - 1, y));
        AssertColorNear(tint, harness.ReadColor(x, y));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ATwoBindingProgramResolvesBothTextureSlotsPerInstance() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 1);
        using VulkanDrawTestHarness harness = new(host);
        Shader material = harness.CreateShader(
            "shaders/two-textures",
            TWO_TEXTURE_VERTEX_SOURCE,
            TWO_TEXTURE_FRAGMENT_SOURCE,
            uniforms: [],
            textures: [FIRST_TEXTURE, SECOND_TEXTURE],
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Mesh quad = harness.CreateQuad("meshes/two-texture-quad", halfWidth: 0.5f, halfHeight: 0.5f);
        Texture red = harness.CreateTexture("textures/red", new Color(1f, 0f, 0f, 1f));
        Texture green = harness.CreateTexture("textures/green", new Color(0f, 1f, 0f, 1f));
        Texture blue = harness.CreateTexture("textures/blue", new Color(0f, 0f, 1f, 1f));
        Assert.Equal(2, material.Textures.Count);

        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            index: 0,
            camera.View,
            camera.Projection,
            camera.ViewProjection,
            camera.Position,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: camera.Width,
            viewportHeight: camera.Height,
            ClearMode.ColorAndDepth,
            _clearColor);
        RenderDrawRecord record = RentRecord(packet, quad, material);
        record.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        // The fragment stage reads slot 0 into red and slot 1 into green, so instance 0 (red, green) is
        // yellow and instance 1 (blue, green) is green. The red channel therefore identifies the instance
        // and the green channel proves the second slot was sampled at all.
        bindings[0] = red;
        bindings[1] = green;
        bindings[2] = blue;
        bindings[3] = green;
        pass.Add(record);
        Retain(packet, red, green, blue);
        packet.Seal();

        Submit(host.Backend, packet);

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        AssertColorNear(new Color(1f, 1f, 0f, 1f), ReadColorAt(harness, -INSTANCE_OFFSET, camera));
        AssertColorNear(new Color(0f, 1f, 0f, 1f), ReadColorAt(harness, INSTANCE_OFFSET, camera));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OneBatchCanMixPerInstanceAndSharedTextureRecords() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture red = harness.CreateTexture(1, 1, new Color(0.9f, 0.1f, 0.1f, 1f));
        Texture blue = harness.CreateTexture(1, 1, new Color(0.1f, 0.2f, 0.9f, 1f));
        Texture green = harness.CreateTexture(1, 1, new Color(0.1f, 0.8f, 0.2f, 1f));
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        // One record with a texture per instance, then one record that repeats its shared texture. Both
        // use the same mesh, program, and state, so the planner merges them into one batch.
        RenderDrawRecord perInstance = RentRecord(packet, harness.Primitives.Quad, material);
        perInstance.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = perInstance.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-1.5f, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(0f, 0f, 0f);
        Span<Texture?> bindings = perInstance.BeginInstanceTextureBindings();
        bindings[0] = red;
        bindings[1] = blue;
        pass.Add(perInstance);

        RenderDrawRecord shared = RentRecord(packet, harness.Primitives.Quad, material);
        shared.SetShaderState(material.InstanceData, [green]);
        shared.BeginInstances(1)[0] = Matrix4x4.CreateTranslation(1.5f, 0f, 0f);
        pass.Add(shared);

        Retain(packet, red, blue, green);
        packet.Seal();

        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Single(plan.Passes[0].Batches);
        Assert.Equal(3, plan.Passes[0].Batches[0].InstanceCount);

        Submit(harness.Host.Backend, packet);

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(3, stats.Instances);
        AssertColorNear(new Color(0.9f, 0.1f, 0.1f, 1f), ReadColorAt(harness, -1.5f, camera));
        AssertColorNear(new Color(0.1f, 0.2f, 0.9f, 1f), ReadColorAt(harness, 0f, camera));
        AssertColorNear(new Color(0.1f, 0.8f, 0.2f, 1f), ReadColorAt(harness, 1.5f, camera));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ASortedTransparentBatchKeepsEachInstancesOwnTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture near = harness.CreateTexture(1, 1, new Color(0.9f, 0.1f, 0.1f, 1f));
        Texture far = harness.CreateTexture(1, 1, new Color(0.1f, 0.2f, 0.9f, 1f));
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(harness.Width, harness.Height, CAMERA_HEIGHT);
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(
            packet,
            harness.Primitives.Quad,
            material,
            renderState: RenderState.AlphaBlend);
        record.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, -1f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, -3f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        bindings[0] = near;
        bindings[1] = far;
        pass.Add(record);
        Retain(packet, near, far);
        packet.Seal();

        // The transparent batch sorts back to front, so the far instance is submitted first and the
        // writer must pair it with record instance 1's matrix and texture.
        FramePlan plan = new BatchPlanner().Plan(packet);
        PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
        Assert.Equal(1, batch.Instances[0].InstanceIndex);
        Assert.Equal(0, batch.Instances[1].InstanceIndex);

        Submit(harness.Host.Backend, packet);

        AssertColorNear(new Color(0.9f, 0.1f, 0.1f, 1f), ReadColorAt(harness, -INSTANCE_OFFSET, camera));
        AssertColorNear(new Color(0.1f, 0.2f, 0.9f, 1f), ReadColorAt(harness, INSTANCE_OFFSET, camera));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    private const string FIRST_TEXTURE = "firstTexture";
    private const string SECOND_TEXTURE = "secondTexture";

    private const string TWO_TEXTURE_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            gl_Position = rbFrame.ViewProjection * (rbInstance.Transform * vec4(aPosition, 1.0));
            vUv = aUv;
        }
        """;

    private const string TWO_TEXTURE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = vec4(texture(firstTexture, vUv).r, texture(secondTexture, vUv).g, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// Builds the two-texel linear distance gradient the glyph probes use. An 8-bit field cannot store
    /// <c>0.5</c> exactly, but the midpoint of 125 and 130 is exactly <c>0.5</c>, so the sampled field
    /// crosses the glyph edge at the center of a quad that spans the full texture.
    /// </summary>
    private static Texture CreateDistanceGradient(PrimitiveDrawGpuHarness harness) {
        float low = 125f / 255f;
        float high = 130f / 255f;
        return harness.CreateTexture(
            2,
            1,
            new SamplerState(TextureFilter.Linear, TextureWrap.Clamp, 1f),
            new Color(low, low, low, 1f),
            new Color(high, high, high, 1f));
    }

    private static OrthographicView CreateOrthographicView(uint width, uint height, float visibleHeight) {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = width / (float)height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(visibleHeight, aspect, 0.1f, 10f);
        return new OrthographicView(view, projection, view * projection, transform.Position);
    }

    private static RenderFramePacket BeginFrame() {
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        return packet;
    }

    private static void BeginCameraPass(
        CameraPass pass,
        OrthographicView camera,
        uint width,
        uint height) => pass.Begin(
            index: 0,
            camera.View,
            camera.Projection,
            camera.ViewProjection,
            camera.Position,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: checked((int)width),
            viewportHeight: checked((int)height),
            ClearMode.ColorAndDepth,
            _clearColor);

    private static RenderDrawRecord RentRecord(
        RenderFramePacket packet,
        Mesh mesh,
        Shader material,
        Color? tint = null,
        RenderState? renderState = null) {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            material.Program.Lifetime,
            renderState ?? material.DefaultRenderState,
            tint ?? Color.White);
        return record;
    }

    private static void Retain(RenderFramePacket packet, params Texture[] textures) {
        foreach (Texture texture in textures) {
            packet.Retention.Add(texture.Lifetime);
        }
    }

    private static void Submit(Vulkan12Backend backend, RenderFramePacket packet) {
        backend.Submit(packet);
        backend.Present();
        backend.WaitForFrameResourcesIdle();
        packet.Retention.Release();
    }

    /// <summary>
    /// Projects a world-space position to a render pixel with a top-left origin, mirroring
    /// <see cref="Camera.WorldToScreen"/> for a full-target viewport.
    /// </summary>
    private static (uint X, uint Y) PixelOf(
        Vector3 world,
        Matrix4x4 viewProjection,
        uint width,
        uint height) {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        uint x = checked((uint)MathF.Floor(((ndcX * 0.5f) + 0.5f) * width));
        uint y = checked((uint)MathF.Floor(((ndcY * 0.5f) + 0.5f) * height));
        return (x, y);
    }

    private static (float R, float G, float B) ReadColorAt(
        PrimitiveDrawGpuHarness harness,
        float worldX,
        OrthographicView camera) {
        (uint x, uint y) = PixelOf(new Vector3(worldX, 0f, 0f), camera.ViewProjection, harness.Width, harness.Height);
        return harness.ReadColor(x, y);
    }

    private static (float R, float G, float B) ReadColorAt(
        VulkanDrawTestHarness harness,
        float worldX,
        VulkanDrawTestHarness.CameraView camera) {
        (uint x, uint y) = PixelOf(
            new Vector3(worldX, 0f, 0f),
            camera.ViewProjection,
            checked((uint)camera.Width),
            checked((uint)camera.Height));
        return harness.ReadColor(x, y);
    }

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= COLOR_TOLERANCE,
            $"Red {actual.R} is not within {COLOR_TOLERANCE} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= COLOR_TOLERANCE,
            $"Green {actual.G} is not within {COLOR_TOLERANCE} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= COLOR_TOLERANCE,
            $"Blue {actual.B} is not within {COLOR_TOLERANCE} of {expected.B}.");
    }

    /// <summary>
    /// A full-target orthographic camera snapshot for the manual packets.
    /// </summary>
    private readonly record struct OrthographicView(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Matrix4x4 ViewProjection,
        Vector3 Position);
}
