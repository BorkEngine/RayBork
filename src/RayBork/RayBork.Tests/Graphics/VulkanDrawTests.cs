using System.Numerics;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the M4 Vulkan draw path: bindless textures, instanced batches, the shader ABI frame
/// and instance blocks, reverse-Z depth, pipeline recreation, and frame-safe bindless slot release.
/// </summary>
/// <remarks>
/// Every test uploads real meshes, textures, and shaders through the production GPU asset worker,
/// builds sealed <see cref="RenderFramePacket"/> values with the internal packet recording API, and
/// submits them through <see cref="Vulkan12Backend"/>. Assertions use validation-layer cleanliness,
/// render statistics, and single-pixel readback with explicit tolerances; there are no golden images
/// and no sleeps. The tests join the serialized <c>SDL</c> collection because SDL and Vulkan
/// initialization are process global.
/// </remarks>
[Collection("SDL")]
public sealed class VulkanDrawTests {
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TexturedDrawRendersExpectedLinearColor() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Color textureColor = new(128f / 255f, 64f / 255f, 192f / 255f, 1f);
        Texture texture = harness.CreateTexture("textures/textured-draw", textureColor);

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(
                quad,
                shader,
                [Matrix4x4.Identity],
                TextureBindings: [texture]));

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1, stats.Instances);
        Assert.Equal(1, stats.PipelineCount);
        Assert.Equal(2, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(2, stats.TextureCount);
        AssertColorNear(textureColor, harness.ReadColor(CenterX(host), CenterY(host)));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoTexturesInOneInstancedDrawKeepTheirOwnColor() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Color leftColor = new(0.9f, 0.15f, 0.2f, 1f);
        Color rightColor = new(0.15f, 0.85f, 0.35f, 1f);
        Texture leftTexture = harness.CreateTexture("textures/left", leftColor);
        Texture rightTexture = harness.CreateTexture("textures/right", rightColor);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 left = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(-aspect * 0.5f, 0f, 0f);
        Matrix4x4 right = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(aspect * 0.5f, 0f, 0f);

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [left], TextureBindings: [leftTexture]),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [right], TextureBindings: [rightTexture]));

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        AssertColorNear(leftColor, harness.ReadColor(host.SceneTarget.Extent.Width / 4, CenterY(host)));
        AssertColorNear(rightColor, harness.ReadColor(host.SceneTarget.Extent.Width * 3 / 4, CenterY(host)));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoMaterialsOverOneProgramAreOneInstancedDraw() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader firstMaterial = CreateTexturedShader(harness);
        Shader secondMaterial = harness.CreateMaterial(firstMaterial);
        Mesh quad = harness.CreateQuad();
        Color leftColor = new(0.8f, 0.2f, 0.2f, 1f);
        Color rightColor = new(0.2f, 0.2f, 0.8f, 1f);
        Texture leftTexture = harness.CreateTexture("textures/material-left", leftColor);
        Texture rightTexture = harness.CreateTexture("textures/material-right", rightColor);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 left = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(-aspect * 0.5f, 0f, 0f);
        Matrix4x4 right = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(aspect * 0.5f, 0f, 0f);

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, firstMaterial, [left], TextureBindings: [leftTexture]),
            new VulkanDrawTestHarness.DrawSpec(quad, secondMaterial, [right], TextureBindings: [rightTexture]));

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        Assert.Equal(1, harness.PipelineCount);
        AssertColorNear(leftColor, harness.ReadColor(host.SceneTarget.Extent.Width / 4, CenterY(host)));
        AssertColorNear(rightColor, harness.ReadColor(host.SceneTarget.Extent.Width * 3 / 4, CenterY(host)));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ThousandCubeInstancesAreOneDraw() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh cube = harness.CreateCube();
        Matrix4x4[] worlds = new Matrix4x4[1000];
        Array.Fill(worlds, Matrix4x4.CreateScale(0.1f));

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(cube, shader, worlds));

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(1000, stats.Instances);
        Assert.Equal(12000, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void UniformSnapshotPerDrawIsIndependent() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Texture white = harness.CreateTexture("textures/white", Color.White);

        shader.SetUniform("uTint", new Vector4(1f, 0f, 0f, 1f));
        (byte[] redTemplate, _) = VulkanDrawTestHarness.SnapshotCursor(shader);
        shader.SetUniform("uTint", new Vector4(0f, 1f, 0f, 1f));
        (byte[] greenTemplate, _) = VulkanDrawTestHarness.SnapshotCursor(shader);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 left = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(-aspect * 0.5f, 0f, 0f);
        Matrix4x4 right = Matrix4x4.CreateScale(aspect * 0.5f, 1f, 1f)
            * Matrix4x4.CreateTranslation(aspect * 0.5f, 0f, 0f);

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(
                quad,
                shader,
                [left],
                InstanceTemplate: redTemplate,
                TextureBindings: [white]),
            new VulkanDrawTestHarness.DrawSpec(
                quad,
                shader,
                [right],
                InstanceTemplate: greenTemplate,
                TextureBindings: [white]));

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        AssertColorNear(new Color(1f, 0f, 0f, 1f), harness.ReadColor(host.SceneTarget.Extent.Width / 4, CenterY(host)));
        AssertColorNear(new Color(0f, 1f, 0f, 1f), harness.ReadColor(host.SceneTarget.Extent.Width * 3 / 4, CenterY(host)));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void CustomAbiShaderRoundTripsThroughBakeAndDraw() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = harness.CreateShader(
            "shaders/custom-abi",
            VulkanDrawTestHarness.DEFAULT_VERTEX_SOURCE,
            VulkanDrawTestHarness.DEFAULT_FRAGMENT_SOURCE,
            uniforms: [
                new ShaderUniformAsset {
                    Name = "uTint",
                    Type = ShaderUniformType.Vector4,
                    Default = [1f, 1f, 1f, 1f],
                },
            ],
            textures: ["mainTexture"],
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Color textureColor = new(128f / 255f, 64f / 255f, 192f / 255f, 1f);
        Texture texture = harness.CreateTexture("textures/custom-abi", textureColor);
        Mesh quad = harness.CreateQuad();
        shader.SetUniform("mainTexture", texture);
        shader.SetUniform("uTint", new Vector4(0.5f, 1f, 0.25f, 1f));

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            VulkanDrawTestHarness.DrawSpec.Single(quad, shader));

        Color expected = new(
            textureColor.R * 0.5f,
            textureColor.G,
            textureColor.B * 0.25f,
            1f);
        AssertColorNear(expected, harness.ReadColor(CenterX(host), CenterY(host)));
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(1, host.Backend.Stats.Instances);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ReverseZNearerQuadWinsAtNearZeroPointZeroOne() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 1, hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad(halfWidth: 2f, halfHeight: 2f);
        Color nearColor = new(1f, 0f, 0f, 1f);
        Color farColor = new(0f, 1f, 0f, 1f);
        Texture nearTexture = harness.CreateTexture("textures/near", nearColor);
        Texture farTexture = harness.CreateTexture("textures/far", farColor);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.PerspectiveCamera(host, near: 0.01f);
        RenderState depthState = RenderState.Opaque with { Cull = CullMode.None };

        harness.SubmitFrame(
            1,
            _clearColor,
            camera,
            // The far quad is submitted first so the near quad must win the reverse-Z comparison.
            new VulkanDrawTestHarness.DrawSpec(
                quad,
                shader,
                [Matrix4x4.CreateTranslation(0f, 0f, -2f)],
                RenderState: depthState,
                TextureBindings: [farTexture]),
            new VulkanDrawTestHarness.DrawSpec(
                quad,
                shader,
                [Matrix4x4.CreateTranslation(0f, 0f, -1f)],
                RenderState: depthState,
                TextureBindings: [nearTexture]));

        AssertColorNear(nearColor, harness.ReadColor(CenterX(host), CenterY(host)));
        float depth = harness.ReadDepth(CenterX(host), CenterY(host));
        Assert.True(
            MathF.Abs(depth - 0.01f) <= 2e-3f,
            $"The stored reverse-Z depth {depth} is not within 2e-3 of the expected 0.01.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TexturesWithTheSameSamplerStateShareOneSampler() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);

        _ = harness.CreateTexture("textures/sampler-a", new Color(0.25f, 0.25f, 0.25f, 1f));
        _ = harness.CreateTexture("textures/sampler-b", new Color(0.5f, 0.5f, 0.5f, 1f));

        Assert.Equal(1, harness.Worker.SamplerCount);
        Assert.Equal(3, harness.TextureCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DeviceRecoveryRestoresBindlessSlotsBeforeTheNextDraw() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Color textureColor = new(0.25f, 0.5f, 0.75f, 1f);
        Texture texture = harness.CreateTexture("textures/device-recovery", textureColor);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        harness.SubmitFrame(
            1,
            _clearColor,
            camera,
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture]));
        AssertColorNear(textureColor, harness.ReadColor(CenterX(host), CenterY(host)));

        host.Backend.SimulateDeviceLossForTests();
        // The backend recovers the device while consuming this frame and drops it: the asset bridge has
        // not restored the live textures yet, so drawing would reference stale bindless slots.
        harness.Submit(harness.CreateFrame(
            2,
            _clearColor,
            camera,
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture])));
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        Assert.Equal(1, host.Backend.BindlessTextures.LiveCount);

        harness.RestoreAssets();
        Assert.Equal(2, host.Backend.BindlessTextures.LiveCount);

        harness.SubmitFrame(
            3,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture]));
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        AssertColorNear(textureColor, harness.ReadColor(CenterX(host), CenterY(host)));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeRecreatesPipelinesAndStillDraws() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Color firstColor = new(0.25f, 0.5f, 0.75f, 1f);
        Texture texture = harness.CreateTexture("textures/resize", firstColor);
        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture]));
        Assert.Equal(1, harness.PipelineCount);

        if (!TryResizeWindow(host, 320, 240)) {
            Assert.Skip("The host did not apply the SDL window resize synchronously.");
        }

        host.SyncWithWindow();
        host.Backend.Resize(320, 240);
        Assert.Equal(0, harness.PipelineCount);
        Assert.Equal(320u, host.SceneTarget.Extent.Width);
        Assert.Equal(240u, host.SceneTarget.Extent.Height);

        harness.SubmitFrame(
            2,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture]));

        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(1, harness.PipelineCount);
        AssertColorNear(firstColor, harness.ReadColor(160, 120));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TextureDestructionWaitsForFrameIdleAndReusesTheBindlessSlot() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTexturedShader(harness);
        Mesh quad = harness.CreateQuad();
        Texture texture = harness.CreateTexture("textures/deferred-destroy", new Color(1f, 0.5f, 0.25f, 1f));
        GpuAssetResource resource = texture.Lifetime.Resource;
        uint releasedSlot = ((VulkanTextureAssetResource)resource).BindlessSlot;
        Assert.Equal(1u, releasedSlot);
        int liveBefore = harness.TextureCount;
        Assert.Equal(2, liveBefore);

        harness.SubmitFrame(
            1,
            _clearColor,
            VulkanDrawTestHarness.OrthographicCamera(host),
            new VulkanDrawTestHarness.DrawSpec(quad, shader, [Matrix4x4.Identity], TextureBindings: [texture]));

        // The public Dispose releases the last owner reference, which queues the GPU destruction on
        // the bridge. The bridge must defer it while the frame that sampled the texture is in flight.
        texture.Dispose();
        harness.Bridge.ProcessPending(harness.Worker);
        if (harness.Bridge.PendingDestroyCount > 0) {
            // The frame is still in flight: the worker must keep the slot and the GPU image alive.
            Assert.False(harness.Worker.AreFrameResourcesIdle);
            Assert.Equal(liveBefore, harness.TextureCount);
        }

        host.Backend.WaitForFrameResourcesIdle();
        harness.Bridge.ProcessPending(harness.Worker);
        Assert.Equal(0, harness.Bridge.PendingDestroyCount);
        Assert.Equal(liveBefore - 1, harness.TextureCount);
        Assert.Equal(0u, ((VulkanTextureAssetResource)resource).BindlessSlot);

        Texture replacement = harness.CreateTexture("textures/reused-slot", Color.White);
        uint replacementSlot = ((VulkanTextureAssetResource)replacement.Lifetime.Resource).BindlessSlot;
        Assert.Equal(releasedSlot, replacementSlot);
        Assert.Equal(liveBefore, harness.TextureCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static Shader CreateTexturedShader(VulkanDrawTestHarness harness) => harness.CreateShader(
        "shaders/textured",
        VulkanDrawTestHarness.DEFAULT_VERTEX_SOURCE,
        VulkanDrawTestHarness.DEFAULT_FRAGMENT_SOURCE,
        uniforms: [
            new ShaderUniformAsset {
                Name = "uTint",
                Type = ShaderUniformType.Vector4,
                Default = [1f, 1f, 1f, 1f],
            },
        ],
        textures: ["mainTexture"],
        defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });

    private static uint CenterX(BackendTestHost host) => host.SceneTarget.Extent.Width / 2;

    private static uint CenterY(BackendTestHost host) => host.SceneTarget.Extent.Height / 2;

    private static bool TryResizeWindow(BackendTestHost host, int width, int height) {
        host.Platform.Window.SetSize(width, height);
        for (int attempt = 0; attempt < 200; attempt++) {
            host.Platform.Pump(TimeSpan.Zero);
            if (host.Platform.Window.RenderWidth == width && host.Platform.Window.RenderHeight == height) {
                return true;
            }
        }

        return false;
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
}
