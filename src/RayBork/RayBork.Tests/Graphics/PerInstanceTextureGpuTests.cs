using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the per-instance draw texture path through <see cref="Vulkan12Backend"/>.
/// </summary>
/// <remarks>
/// Tests use the production GPU asset bridge and worker through <see cref="PrimitiveDrawGpuHarness"/>
/// or <see cref="VulkanDrawTestHarness"/>, and record detached packets that retain every texture they
/// reference. GPU test classes run in parallel; a host without a usable Vulkan device is skipped.
/// </remarks>
public sealed class PerInstanceTextureGpuTests
{
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private static readonly Color _instanceAColor = new(0.9f, 0.1f, 0.1f, 1f);
    private static readonly Color _instanceBColor = new(0.1f, 0.2f, 0.9f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;
    private const float CAMERA_HEIGHT = 2f;
    private const float INSTANCE_OFFSET = 1f;

    /// <summary>The odd pixel width of the distance-ramp quad.</summary>
    private const int QUAD_PIXEL_WIDTH = 5;

    /// <summary>The window the half-coverage probe allows for filtering quantization.</summary>
    private const float HALF_COVERAGE_TOLERANCE = 0.02f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EachInstanceSamplesItsOwnTexture()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture textureA = harness.CreateTexture(1, 1, _instanceAColor);
        Texture textureB = harness.CreateTexture(1, 1, _instanceBColor);
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(
            harness.Width,
            harness.Height,
            CAMERA_HEIGHT
        );
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
    public void TheBuiltInMsdfProgramSamplesAPerInstanceTexture()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        // A 1x1 field of 1 is fully covered and a field of 0 is fully transparent. The probes therefore
        // prove that the program samples each instance's own texture and applies the reconstructed
        // coverage to the alpha; they say nothing about the width of the derivative band.
        Texture covered = harness.CreateTexture(1, 1, Color.White);
        Texture empty = harness.CreateTexture(1, 1, Color.Black);
        Shader material = harness.Primitives.MsdfMaterial;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);
        OrthographicView camera = CreateOrthographicView(
            harness.Width,
            harness.Height,
            CAMERA_HEIGHT
        );
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
    public void APartiallyCoveredGlyphPixelBlendsTheTintOverTheClearColor()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture ramp = CreateDistanceRamp(harness);
        Shader material = harness.Primitives.MsdfMaterial;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);
        float worldPerPixel = CAMERA_HEIGHT / harness.Height;
        // The quad covers five pixels with its center on a pixel center, so the middle pixel samples the
        // ramp exactly between the two texels: the texel weights are 0.5, which every filter's subtexel
        // precision represents exactly, and the sampled distance is exactly 0.5.
        float quadWidth = QUAD_PIXEL_WIDTH * worldPerPixel;
        float center = 0.5f * worldPerPixel;
        OrthographicView camera = CreateOrthographicView(
            harness.Width,
            harness.Height,
            CAMERA_HEIGHT
        );
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(packet, harness.Primitives.Quad, material, tint);
        record.SetPerInstanceShaderState(material);
        record.BeginInstances(1)[0] =
            Matrix4x4.CreateScale(quadWidth, 1f, 1f) * Matrix4x4.CreateTranslation(center, 0f, 0f);
        record.BeginInstanceTextureBindings()[0] = ramp;
        pass.Add(record);
        Retain(packet, ramp);
        packet.Seal();

        Submit(harness.Host.Backend, packet);

        Color halfway = new(
            (0.5f * tint.R) + (0.5f * _clearColor.R),
            (0.5f * tint.G) + (0.5f * _clearColor.G),
            (0.5f * tint.B) + (0.5f * _clearColor.B),
            1f
        );
        (uint x, uint y) = PixelOf(
            new Vector3(center, 0f, 0f),
            camera.ViewProjection,
            harness.Width,
            harness.Height
        );
        Assert.Equal(harness.Width / 2, x);
        // A driver may round the filtered texel to UNORM8 (1/255 above 0.5) or quantize the subtexel
        // weights, which moves the coverage by a few hundredths; the pre-fix rgb*coverage form is far
        // outside this window.
        AssertColorNear(halfway, harness.ReadColor(x, y), HALF_COVERAGE_TOLERANCE);

        // The clamped ends of the ramp are fully outside and fully inside the field.
        AssertColorNear(_clearColor, ReadColorAt(harness, center - (1.5f * worldPerPixel), camera));
        AssertColorNear(tint, ReadColorAt(harness, center + (1.5f * worldPerPixel), camera));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ATwoBindingProgramResolvesBothTextureSlotsPerInstance()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 1);
        using VulkanDrawTestHarness harness = new(host);
        Shader material = harness.CreateShader(
            "shaders/two-textures",
            TWO_TEXTURE_VERTEX_SOURCE,
            TWO_TEXTURE_FRAGMENT_SOURCE,
            uniforms: [],
            textures: [FIRST_TEXTURE, SECOND_TEXTURE],
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        Mesh quad = harness.CreateQuad(
            "meshes/two-texture-quad",
            halfWidth: 0.5f,
            halfHeight: 0.5f
        );
        Texture red = harness.CreateTexture("textures/red", new Color(1f, 0f, 0f, 1f));
        Texture green = harness.CreateTexture("textures/green", new Color(0f, 1f, 0f, 1f));
        Texture blue = harness.CreateTexture("textures/blue", new Color(0f, 0f, 1f, 1f));
        Texture black = harness.CreateTexture("textures/black", new Color(0f, 0f, 0f, 1f));
        Assert.Equal(2, material.Textures.Count);

        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(
            host,
            CAMERA_HEIGHT
        );
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
            _clearColor
        );
        RenderDrawRecord record = RentRecord(packet, quad, material);
        record.SetPerInstanceShaderState(material);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        // The fragment stage reads slot 0 into red and slot 1 into green. Instance 0 binds black in the
        // second slot, whose green channel differs from the white fallback a never-written index slot
        // resolves to, so its red probe proves the second slot was written. Instance 1 binds blue and
        // green, so the red channel identifies the instance.
        bindings[0] = red;
        bindings[1] = black;
        bindings[2] = blue;
        bindings[3] = green;
        pass.Add(record);
        Retain(packet, red, blue, black, green);
        packet.Seal();

        Submit(host.Backend, packet);

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        AssertColorNear(new Color(1f, 0f, 0f, 1f), ReadColorAt(harness, -INSTANCE_OFFSET, camera));
        AssertColorNear(new Color(0f, 1f, 0f, 1f), ReadColorAt(harness, INSTANCE_OFFSET, camera));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OneBatchCanMixPerInstanceAndSharedTextureRecords()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture red = harness.CreateTexture(1, 1, new Color(0.9f, 0.1f, 0.1f, 1f));
        Texture blue = harness.CreateTexture(1, 1, new Color(0.1f, 0.2f, 0.9f, 1f));
        Texture green = harness.CreateTexture(1, 1, new Color(0.1f, 0.8f, 0.2f, 1f));
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(
            harness.Width,
            harness.Height,
            CAMERA_HEIGHT
        );
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
    public void ASortedTransparentBatchKeepsEachInstancesOwnTexture()
    {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(
            sampleCount: 1
        );
        Texture near = harness.CreateTexture(1, 1, new Color(0.9f, 0.1f, 0.1f, 1f));
        Texture far = harness.CreateTexture(1, 1, new Color(0.1f, 0.2f, 0.9f, 1f));
        Shader material = harness.Primitives.MeshMaterial;
        OrthographicView camera = CreateOrthographicView(
            harness.Width,
            harness.Height,
            CAMERA_HEIGHT
        );
        RenderFramePacket packet = BeginFrame();
        CameraPass pass = packet.BeginPass();
        BeginCameraPass(pass, camera, harness.Width, harness.Height);

        RenderDrawRecord record = RentRecord(
            packet,
            harness.Primitives.Quad,
            material,
            renderState: RenderState.AlphaBlend
        );
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

        AssertColorNear(
            new Color(0.9f, 0.1f, 0.1f, 1f),
            ReadColorAt(harness, -INSTANCE_OFFSET, camera)
        );
        AssertColorNear(
            new Color(0.1f, 0.2f, 0.9f, 1f),
            ReadColorAt(harness, INSTANCE_OFFSET, camera)
        );
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

    /// <summary>Builds the two-texel linear distance ramp the glyph probes use.</summary>
    private static Texture CreateDistanceRamp(PrimitiveDrawGpuHarness harness)
    {
        float low = 64f / 255f;
        float high = 191f / 255f;
        return harness.CreateTexture(
            2,
            1,
            new SamplerState(TextureFilter.Linear, TextureWrap.Clamp, 1f),
            new Color(low, low, low, 1f),
            new Color(high, high, high, 1f)
        );
    }

    private static OrthographicView CreateOrthographicView(
        uint width,
        uint height,
        float visibleHeight
    )
    {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = width / (float)height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(
            visibleHeight,
            aspect,
            0.1f,
            10f
        );
        return new OrthographicView(view, projection, view * projection, transform.Position);
    }

    private static RenderFramePacket BeginFrame()
    {
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        return packet;
    }

    private static void BeginCameraPass(
        CameraPass pass,
        OrthographicView camera,
        uint width,
        uint height
    ) =>
        pass.Begin(
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
            _clearColor
        );

    private static RenderDrawRecord RentRecord(
        RenderFramePacket packet,
        Mesh mesh,
        Shader material,
        Color? tint = null,
        RenderState? renderState = null
    )
    {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            material.Program.Lifetime,
            renderState ?? material.DefaultRenderState,
            tint ?? Color.White
        );
        return record;
    }

    private static void Retain(RenderFramePacket packet, params Texture[] textures)
    {
        foreach (Texture texture in textures)
        {
            packet.Retention.Add(texture.Lifetime);
        }
    }

    private static void Submit(Vulkan12Backend backend, RenderFramePacket packet)
    {
        backend.Submit(packet);
        backend.Present();
        backend.WaitForFrameResourcesIdle();
        packet.Retention.Release();
    }

    /// <summary>Projects a world-space position to a render pixel with a top-left origin.</summary>
    private static (uint X, uint Y) PixelOf(
        Vector3 world,
        Matrix4x4 viewProjection,
        uint width,
        uint height
    )
    {
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
        OrthographicView camera
    )
    {
        (uint x, uint y) = PixelOf(
            new Vector3(worldX, 0f, 0f),
            camera.ViewProjection,
            harness.Width,
            harness.Height
        );
        return harness.ReadColor(x, y);
    }

    private static (float R, float G, float B) ReadColorAt(
        VulkanDrawTestHarness harness,
        float worldX,
        VulkanDrawTestHarness.CameraView camera
    )
    {
        (uint x, uint y) = PixelOf(
            new Vector3(worldX, 0f, 0f),
            camera.ViewProjection,
            checked((uint)camera.Width),
            checked((uint)camera.Height)
        );
        return harness.ReadColor(x, y);
    }

    private static void AssertColorNear(
        Color expected,
        (float R, float G, float B) actual,
        float tolerance = COLOR_TOLERANCE
    )
    {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}."
        );
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}."
        );
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}."
        );
    }

    /// <summary>A full-target orthographic camera snapshot for the manual packets.</summary>
    private readonly record struct OrthographicView(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Matrix4x4 ViewProjection,
        Vector3 Position
    );
}
