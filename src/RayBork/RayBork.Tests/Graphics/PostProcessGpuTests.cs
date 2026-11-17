using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Rendering;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the camera post-process chain: a custom invert/tint pass on the scene target and a
/// render target, chained effect order, the source depth binding, viewport store-back, disabled
/// semantics, retained materials, readback inclusion, and target resize and device recovery.
/// </summary>
/// <remarks>
/// The tests compile real shaders through the bake prologue, submit sealed frames through the real
/// backend, and read numeric probes. They assert validation-layer cleanliness and never compare images.
/// </remarks>
public sealed class PostProcessGpuTests
{
    private const float HDR_TOLERANCE = 4e-3f;
    private const float DEPTH_TOLERANCE = 4e-3f;

    private const string FULLSCREEN_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            vUv = aUv;
            gl_Position = vec4(aPosition.xy * 2.0, 0.0, 1.0);
        }
        """;

    private const string INVERT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            vec4 source = texture(sourceColor, vUv);
            oColor = vec4(vec3(1.0) - source.rgb, source.a);
        }
        """;

    private const string TINT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(sourceColor, vUv) * uTint;
        }
        """;

    private const string DEPTH_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            float depth = texture(sourceDepth, vUv).r;
            oColor = vec4(depth, depth, depth, 1.0);
        }
        """;

    private const string SAMPLE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(sourceColor, vUv);
        }
        """;

    private const string USER_TEXTURE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(sourceColor, vUv) * texture(uTintTexture, vUv);
        }
        """;

    private const string BUILT_IN_SCALE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = vec4(texture(sourceColor, vUv).rgb * uScale, 1.0);
        }
        """;

    private const string BUILT_IN_COMPOSITE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(uSource, vUv) + uBias;
        }
        """;

    private const string POSITION_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            vUv = aUv;
            gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(aPosition, 1.0);
        }
        """;

    private const string TINT_DRAW_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = rbInstance.Color;
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnInvertEffectOnTheSceneTargetWritesThePostProcessedColor()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ChainedEffectsRunInStackOrderAndUseTheUserUniform()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-chain-invert",
            INVERT_FRAGMENT_SOURCE
        );
        using Shader tint = CreateEffectShader(
            harness,
            "shaders/post-chain-tint",
            TINT_FRAGMENT_SOURCE,
            [
                new ShaderUniformAsset
                {
                    Name = "uTint",
                    Type = ShaderUniformType.Vector4,
                    Default = [1f, 1f, 1f, 1f],
                },
            ]
        );
        Vector4 tintValue = new(0.5f, 0.5f, 0.5f, 1f);
        tint.SetUniform("uTint", tintValue);
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.375f, 0.25f, 0.125f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert), Capture(tint)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SourceDepthVisualizationMatchesTheStoredReverseZValue()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader depthEffect = CreateEffectShader(
            harness,
            "shaders/post-depth",
            DEPTH_FRAGMENT_SOURCE,
            textureNames: ["sourceColor", "sourceDepth"]
        );
        using Shader tint = harness.CreateShader(
            "shaders/post-depth-tint",
            POSITION_VERTEX_SOURCE,
            TINT_DRAW_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, width, height, 2f);
        Mesh quad = harness.CreateQuad(
            "meshes/post-depth-quad",
            halfWidth: width / (float)height,
            halfHeight: 1f
        );
        float expectedDepth = ExpectedReverseZDepth(camera, Vector3.Zero);
        VulkanDrawTestHarness.PassSpec pass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            Color.Black,
            [new VulkanDrawTestHarness.DrawSpec(quad, tint, [Matrix4x4.Identity])],
            PostProcessEffects: [Capture(depthEffect)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        (float r, float g, float b) = harness.ReadColor((uint)x, (uint)y);
        AssertDepthNear(expectedDepth, r);
        AssertDepthNear(expectedDepth, g);
        AssertDepthNear(expectedDepth, b);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnEffectOnARenderTextureIsSampledByALaterSceneCamera()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-target-invert",
            INVERT_FRAGMENT_SOURCE
        );
        using Shader sample = CreateEffectShader(
            harness,
            "shaders/post-target-sample",
            SAMPLE_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);
        (int width, int height) = SceneSize(host);

        VulkanDrawTestHarness.PassSpec targetPass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            target.TargetId,
            [Capture(invert)]
        );
        VulkanDrawTestHarness.PassSpec samplePass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            Color.Black,
            [
                new VulkanDrawTestHarness.DrawSpec(
                    harness.CreateQuad(
                        "meshes/post-target-sample-quad",
                        halfWidth: width / (float)height,
                        halfHeight: 1f
                    ),
                    sample,
                    [Matrix4x4.Identity],
                    RenderState: RenderState.Opaque with
                    {
                        Cull = CullMode.None,
                    },
                    TextureBindings: [target.ColorTexture]
                ),
            ],
            PostProcessEffects: []
        );

        harness.Submit(harness.CreateFrameWithPasses(1, targetPass, samplePass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        AssertColorNear(expected, harness.ReadRenderTargetColor(target, 32, 32), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnEffectOnTheSceneTargetIsPreservedByALaterHudCamera()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-hud-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.2f, 0.4f, 0.6f, 1f);
        Color expected = new(0.8f, 0.6f, 0.4f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, width, height, 2f);
        VulkanDrawTestHarness.PassSpec scenePass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert)]
        );
        VulkanDrawTestHarness.PassSpec hudPass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.None,
            Color.Black,
            []
        );

        harness.Submit(harness.CreateFrameWithPasses(1, scenePass, hudPass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void MultipleCamerasWithEffectsProcessTheirOwnContentInCreationOrder()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-multi-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color left = new(0.1f, 0.2f, 0.3f, 1f);
        Color right = new(0.6f, 0.5f, 0.4f, 1f);
        (int width, int height) = SceneSize(host);
        int leftWidth = width / 2;
        int rightWidth = width - leftWidth;
        VulkanDrawTestHarness.PassSpec leftPass = new(
            CreateOrthoView(0, 0, leftWidth, height, 2f),
            0,
            0,
            leftWidth,
            height,
            ClearMode.ColorAndDepth,
            left,
            [],
            PostProcessEffects: [Capture(invert)]
        );
        VulkanDrawTestHarness.PassSpec rightPass = new(
            CreateOrthoView(leftWidth, 0, rightWidth, height, 2f),
            leftWidth,
            0,
            rightWidth,
            height,
            ClearMode.ColorAndDepth,
            right,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, leftPass, rightPass));

        AssertColorNear(Invert(left), harness.ReadColor(8, (uint)(height / 2)), HDR_TOLERANCE);
        AssertColorNear(
            Invert(right),
            harness.ReadColor((uint)(width - 8), (uint)(height / 2)),
            HDR_TOLERANCE
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ADisabledEffectSnapshotIsSkippedAndLeavesNoChainState()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-disabled-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        (int width, int height) = SceneSize(host);
        int centerX = width / 2;
        int centerY = height / 2;

        // A disabled effect that is captured still records a disabled snapshot; the renderer skips it.
        PostProcessEffect disabledEffect = new ShaderPostProcessEffect(invert) { Enabled = false };
        VulkanDrawTestHarness.PassSpec disabledEffectPass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [CaptureEffect(disabledEffect)]
        );
        harness.Submit(harness.CreateFrameWithPasses(1, disabledEffectPass));
        AssertColorNear(clear, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);

        // A later frame with no effect snapshot runs no chain and is not affected by the skipped one.
        VulkanDrawTestHarness.PassSpec noEffectPass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: []
        );
        harness.Submit(harness.CreateFrameWithPasses(2, noEffectPass));
        AssertColorNear(clear, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ANoneClearingCameraWithAnEffectSeesThePriorContent()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-none-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color prior = new(0.4f, 0.3f, 0.2f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, width, height, 2f);
        VulkanDrawTestHarness.PassSpec clearPass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            prior,
            []
        );
        VulkanDrawTestHarness.PassSpec nonePass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.None,
            Color.Black,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, clearPass, nonePass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(Invert(prior), harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AViewportEffectStoresBackOnlyItsViewportAndSamplesTheFullTarget()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-viewport-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color background = new(0.1f, 0.2f, 0.9f, 1f);
        Color viewportClear = new(1f, 0f, 0f, 1f);
        (int width, int height) = SceneSize(host);
        int viewportX = width / 4;
        int viewportY = height / 4;
        int viewportWidth = Math.Max(4, width / 4);
        int viewportHeight = Math.Max(4, height / 4);
        VulkanDrawTestHarness.CameraView backgroundCamera = CreateOrthoView(
            0,
            0,
            width,
            height,
            2f
        );
        VulkanDrawTestHarness.CameraView viewportCamera = CreateOrthoView(
            viewportX,
            viewportY,
            viewportWidth,
            viewportHeight,
            2f
        );
        VulkanDrawTestHarness.PassSpec backgroundPass = new(
            backgroundCamera,
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            background,
            []
        );
        VulkanDrawTestHarness.PassSpec viewportPass = new(
            viewportCamera,
            viewportX,
            viewportY,
            viewportWidth,
            viewportHeight,
            ClearMode.ColorAndDepth,
            viewportClear,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, backgroundPass, viewportPass));

        int insideX = viewportX + (viewportWidth / 2);
        int insideY = viewportY + (viewportHeight / 2);
        AssertColorNear(
            Invert(viewportClear),
            harness.ReadColor((uint)insideX, (uint)insideY),
            HDR_TOLERANCE
        );
        // The pixels outside the camera viewport keep the earlier camera pass' content; the store-back
        // does not cover them even though the chain sampled the whole target.
        AssertColorNear(background, harness.ReadColor(0, 0), HDR_TOLERANCE);
        AssertColorNear(
            background,
            harness.ReadColor((uint)(width - 1), (uint)(height - 1)),
            HDR_TOLERANCE
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheReservedSourceColorBindingOverridesTheMaterialsTextureValue()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = harness.CreateShader(
            "shaders/post-reserved-invert",
            FULLSCREEN_VERTEX_SOURCE,
            INVERT_FRAGMENT_SOURCE,
            textures: ["sourceColor"]
        );
        using Texture unrelated = harness.CreateTexture(
            "textures/post-unrelated",
            new Color(0f, 1f, 0f, 1f)
        );
        invert.SetUniform("sourceColor", unrelated);
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(Invert(clear), harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheShaderDefaultRenderStateIsIgnoredByThePostPass()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = harness.CreateShader(
            "shaders/post-state-invert",
            FULLSCREEN_VERTEX_SOURCE,
            INVERT_FRAGMENT_SOURCE,
            textures: ["sourceColor"],
            defaultRenderState: RenderState.AlphaBlend with
            {
                Cull = CullMode.Front,
                DepthTest = DepthTest.Less,
            }
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(Invert(clear), harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DisposingTheEffectMaterialAfterCameraCreationRetainsTheRecordedFrame()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader invert = CreateEffectShader(
            harness,
            "shaders/post-retained-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(invert)]
        );
        RenderFramePacket packet = harness.CreateFrameWithPasses(1, pass);

        // The frame's retention set holds the program, so disposing the material before submission
        // cannot queue the program's GPU destruction under the recorded frame. The owned engine asset
        // reports disposed as soon as its last owner reference is gone; the frame hold is what keeps
        // the GPU resource and its bindless slot alive for the recorded pass.
        Assert.False(invert.Program.IsDisposed);
        invert.Dispose();
        Assert.True(invert.IsDisposed);

        harness.Submit(packet);

        (int x, int y) = SceneCenter(host);
        AssertColorNear(Invert(clear), harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        packet.Retention.Release();
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public async Task ReadbackIncludesThePostProcessedOutput()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(8, 4));
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-readback-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            target.TargetId,
            [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        Color[,] colors = await host.ReadColorsAsync(target);
        byte[] pixels = await host.ReadPixelsAsync(target);
        Assert.Equal(target.Height, colors.GetLength(0));
        Assert.Equal(target.Width, colors.GetLength(1));
        for (int y = 0; y < colors.GetLength(0); y++)
        {
            for (int x = 0; x < colors.GetLength(1); x++)
            {
                AssertColorNear(expected, colors[y, x], HDR_TOLERANCE);
            }
        }

        AssertPixelsMatchReference(
            pixels,
            colors,
            TonemappingSnapshot.Default,
            TonemappingMode.AgX
        );
        Assert.True(host.Backend.HasRenderTargetColorContentForTests(target.TargetId));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeAndDeviceRecoveryRecreateThePostTargetsAndSceneSourceSlots()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-resize-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        int initialSceneSlots = host.Backend.SceneTargetSourceSlotAllocationCountForTests;

        VulkanDrawTestHarness.PassSpec Pass(int width, int height) =>
            new(
                CreateOrthoView(0, 0, width, height, 2f),
                0,
                0,
                width,
                height,
                ClearMode.ColorAndDepth,
                clear,
                [],
                target.TargetId,
                [Capture(invert)]
            );

        // Fill every frame ring slot so the next frame reuses slot 0 and proves that a resize recreates
        // that slot's post targets rather than only creating them for a new slot.
        for (int frame = 1; frame <= 3; frame++)
        {
            harness.Submit(harness.CreateFrameWithPasses(frame, Pass(target.Width, target.Height)));
        }

        AssertColorNear(Invert(clear), harness.ReadRenderTargetColor(target, 8, 8), HDR_TOLERANCE);
        Assert.Equal(3, host.Backend.PostTargetRecreationCountForTests);

        host.ResizeRenderTexture(target, 32, 16);
        harness.Submit(harness.CreateFrameWithPasses(4, Pass(target.Width, target.Height)));
        AssertColorNear(Invert(clear), harness.ReadRenderTargetColor(target, 16, 8), HDR_TOLERANCE);
        Assert.Equal(4, host.Backend.PostTargetRecreationCountForTests);

        host.Backend.SimulateDeviceLossForTests();
        harness.Submit(harness.CreateFrameWithPasses(5, Pass(target.Width, target.Height)));
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        harness.RestoreAssets();
        Assert.True(
            host.Backend.SceneTargetSourceSlotAllocationCountForTests >= initialSceneSlots + 1
        );
        int afterRecovery = host.Backend.PostTargetRecreationCountForTests;
        harness.Submit(harness.CreateFrameWithPasses(6, Pass(target.Width, target.Height)));
        Assert.True(host.Backend.PostTargetRecreationCountForTests > afterRecovery);
        AssertColorNear(Invert(clear), harness.ReadRenderTargetColor(target, 16, 8), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheEffectChainPreservesTheTopLeftImageOrientation()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader sample = CreateEffectShader(
            harness,
            "shaders/post-orientation-sample",
            SAMPLE_FRAGMENT_SOURCE
        );
        Color top = new(0.9f, 0.1f, 0.1f, 1f);
        Color bottom = new(0.1f, 0.1f, 0.9f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, width, height, 2f);
        int topHeight = height / 2;
        int bottomHeight = height - topHeight;
        VulkanDrawTestHarness.PassSpec topPass = new(
            camera,
            0,
            0,
            width,
            topHeight,
            ClearMode.ColorAndDepth,
            top,
            []
        );
        VulkanDrawTestHarness.PassSpec bottomPass = new(
            camera,
            0,
            topHeight,
            width,
            bottomHeight,
            ClearMode.ColorAndDepth,
            bottom,
            []
        );
        VulkanDrawTestHarness.PassSpec effectPass = new(
            camera,
            0,
            0,
            width,
            height,
            ClearMode.None,
            Color.Black,
            [],
            PostProcessEffects: [Capture(sample)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, topPass, bottomPass, effectPass));

        // (0, 0) is the image top-left; the chain must not flip the resolved source vertically.
        AssertColorNear(top, harness.ReadColor((uint)(width / 2), 2), HDR_TOLERANCE);
        AssertColorNear(
            bottom,
            harness.ReadColor((uint)(width / 2), (uint)(height - 3)),
            HDR_TOLERANCE
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AHudCameraWithAStackProcessesTheCurrentTargetContent()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-hud-stack-invert",
            INVERT_FRAGMENT_SOURCE
        );
        using Shader hudTint = harness.CreateShader(
            "shaders/post-hud-stack-tint",
            POSITION_VERTEX_SOURCE,
            TINT_DRAW_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        Color background = new(0.2f, 0.4f, 0.6f, 1f);
        Color hud = new(1f, 0f, 0f, 1f);
        (int width, int height) = SceneSize(host);
        int hudX = width / 2;
        int hudWidth = width - hudX;
        VulkanDrawTestHarness.CameraView sceneCamera = CreateOrthoView(0, 0, width, height, 2f);
        VulkanDrawTestHarness.CameraView hudCamera = CreateOrthoView(hudX, 0, hudWidth, height, 2f);
        float hudAspect = hudWidth / (float)height;
        Mesh hudQuad = harness.CreateQuad(
            "meshes/post-hud-stack-quad",
            halfWidth: hudAspect * 0.25f,
            halfHeight: 0.5f
        );
        VulkanDrawTestHarness.PassSpec scenePass = new(
            sceneCamera,
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            background,
            []
        );
        VulkanDrawTestHarness.PassSpec hudPass = new(
            hudCamera,
            hudX,
            0,
            hudWidth,
            height,
            ClearMode.None,
            Color.Black,
            [new VulkanDrawTestHarness.DrawSpec(hudQuad, hudTint, [Matrix4x4.Identity], Tint: hud)],
            PostProcessEffects: [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, scenePass, hudPass));

        int hudCenterX = hudX + (hudWidth / 2);
        int hudCenterY = height / 2;
        // The HUD camera's chain processes the HUD draw and the loaded earlier scene content, and its
        // store-back covers only the HUD camera viewport.
        AssertColorNear(
            Invert(hud),
            harness.ReadColor((uint)hudCenterX, (uint)hudCenterY),
            HDR_TOLERANCE
        );
        AssertColorNear(
            Invert(background),
            harness.ReadColor((uint)(width - 4), (uint)hudCenterY),
            HDR_TOLERANCE
        );
        AssertColorNear(background, harness.ReadColor(4, (uint)hudCenterY), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ACustomEffectSamplesItsMaterialTextureBindings()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader effect = CreateEffectShader(
            harness,
            "shaders/post-user-texture",
            USER_TEXTURE_FRAGMENT_SOURCE,
            textureNames: ["sourceColor", "uTintTexture"]
        );
        using Texture tintTexture = harness.CreateTexture(
            "textures/post-user-tint",
            new Color(0.5f, 0.5f, 0.5f, 1f)
        );
        effect.SetUniform("uTintTexture", tintTexture);
        Color clear = new(0.2f, 0.4f, 0.6f, 1f);
        float tint = 128f / 255f;
        Color expected = new(clear.R * tint, clear.G * tint, clear.B * tint, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [Capture(effect)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheChainPreservesSceneLinearValuesAboveOneAcrossTwoPasses()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(8, 4));
        using Shader sample = CreateEffectShader(
            harness,
            "shaders/post-hdr-sample",
            SAMPLE_FRAGMENT_SOURCE
        );
        Color hdr = new(2f, 0.5f, 0.25f, 1f);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            hdr,
            [],
            target.TargetId,
            [Capture(sample), Capture(sample)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        // The Rgba16Float post targets and the store-back apply no tone mapping or clamp, so a value
        // above 1 survives two intermediate passes and the resolved target keeps it.
        AssertColorNear(hdr, harness.ReadRenderTargetColor(target, 4, 2), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ASingleSampleDepthlessRenderTargetStoresTheEffectBack()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(
                8,
                8,
                ColorFormat: TextureFormat.Rgba8Unorm,
                Depth: false,
                SampleCount: 1
            )
        );
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-single-sample-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            target.TargetId,
            [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        // A single-sample target has no MSAA resolve in the store-back and a Depth: false target has no
        // depth image, so the chain runs with the color source alone.
        AssertColorNear(expected, harness.ReadRenderTargetColor(target, 4, 4), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnEffectOnADepthBearingRenderTargetSamplesThatTargetsDepth()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));
        using Shader depthEffect = CreateEffectShader(
            harness,
            "shaders/post-target-depth",
            DEPTH_FRAGMENT_SOURCE,
            textureNames: ["sourceColor", "sourceDepth"]
        );
        using Shader tint = harness.CreateShader(
            "shaders/post-target-depth-tint",
            POSITION_VERTEX_SOURCE,
            TINT_DRAW_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(
            0,
            0,
            target.Width,
            target.Height,
            2f
        );
        Mesh quad = harness.CreateQuad("meshes/post-target-depth-quad");
        float expectedDepth = ExpectedReverseZDepth(camera, Vector3.Zero);
        VulkanDrawTestHarness.PassSpec pass = new(
            camera,
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            Color.Black,
            [new VulkanDrawTestHarness.DrawSpec(quad, tint, [Matrix4x4.Identity])],
            target.TargetId,
            [CaptureEffect(new ShaderPostProcessEffect(depthEffect))]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        (float r, float g, float b) = harness.ReadRenderTargetColor(target, 8, 8);
        AssertDepthNear(expectedDepth, r);
        AssertDepthNear(expectedDepth, g);
        AssertDepthNear(expectedDepth, b);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EffectsOnDifferentTargetExtentsInOneFrameKeepTheirOwnPostTargets()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-multi-extent-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color targetClear = new(0.25f, 0.5f, 0.75f, 1f);
        Color sceneClear = new(0.4f, 0.3f, 0.2f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec targetPass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            targetClear,
            [],
            target.TargetId,
            [Capture(invert)]
        );
        VulkanDrawTestHarness.PassSpec scenePass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            sceneClear,
            [],
            PostProcessEffects: [Capture(invert)]
        );

        // Two effect passes of different extents in one frame must not destroy each other's post
        // targets while the recorded command buffer still references them: the processor keeps one target set
        // per extent per frame ring slot.
        harness.Submit(harness.CreateFrameWithPasses(1, targetPass, scenePass));
        Assert.Equal(2, host.Backend.PostTargetRecreationCountForTests);
        AssertColorNear(
            Invert(targetClear),
            harness.ReadRenderTargetColor(target, 8, 8),
            HDR_TOLERANCE
        );
        (int centerX, int centerY) = SceneCenter(host);
        AssertColorNear(
            Invert(sceneClear),
            harness.ReadColor((uint)centerX, (uint)centerY),
            HDR_TOLERANCE
        );

        harness.Submit(harness.CreateFrameWithPasses(2, targetPass, scenePass));
        harness.Submit(harness.CreateFrameWithPasses(3, targetPass, scenePass));
        Assert.Equal(6, host.Backend.PostTargetRecreationCountForTests);

        // The fourth frame reuses frame slot 0, whose sets the third frame did not touch: the previous
        // frame on that slot used both extents, so both sets must survive and be reused.
        harness.Submit(harness.CreateFrameWithPasses(4, targetPass, scenePass));
        Assert.Equal(6, host.Backend.PostTargetRecreationCountForTests);
        AssertColorNear(
            Invert(targetClear),
            harness.ReadRenderTargetColor(target, 8, 8),
            HDR_TOLERANCE
        );
        AssertColorNear(
            Invert(sceneClear),
            harness.ReadColor((uint)centerX, (uint)centerY),
            HDR_TOLERANCE
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ABuiltInMultiPassEffectRentsIntermediatesAndRecordsPerPassLayouts()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader scale = harness.CreateShader(
            "shaders/post-builtin-scale",
            FULLSCREEN_VERTEX_SOURCE,
            BUILT_IN_SCALE_FRAGMENT_SOURCE,
            [
                new ShaderUniformAsset
                {
                    Name = "uScale",
                    Type = ShaderUniformType.Float,
                    Default = [0.5f],
                },
            ],
            ["sourceColor"]
        );
        using Shader composite = harness.CreateShader(
            "shaders/post-builtin-composite",
            FULLSCREEN_VERTEX_SOURCE,
            BUILT_IN_COMPOSITE_FRAGMENT_SOURCE,
            [
                new ShaderUniformAsset
                {
                    Name = "uBias",
                    Type = ShaderUniformType.Vector4,
                    Default = [0.1f, 0f, 0f, 0f],
                },
                new ShaderUniformAsset
                {
                    Name = "uReserved",
                    Type = ShaderUniformType.Vector4,
                    Default = [0f, 0f, 0f, 0f],
                },
            ],
            ["uSource"]
        );
        scale.SetUniform("uScale", 0.5f);
        composite.SetUniform("uBias", new Vector4(0.1f, 0f, 0f, 0f));
        var builtIn = new TestBuiltInEffect(scale, composite);
        Color clear = new(0.2f, 0.4f, 0.6f, 1f);
        Color expected = new((clear.R * 0.5f) + 0.1f, clear.G * 0.5f, clear.B * 0.5f, 1f);
        (int width, int height) = SceneSize(host);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, width, height, 2f),
            0,
            0,
            width,
            height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            PostProcessEffects: [builtIn]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        // The two passes use different shaders and uniform sizes, so their instance strides differ; the
        // recorder must reserve and write both without the frame's instance simulation under-sizing the
        // buffer. The first pass writes a half-extent auxiliary target, the second composites it.
        Assert.True(builtIn.FirstStride != builtIn.SecondStride);
        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnEffectOnAnSrgbRenderTargetWritesTheEncodedResult()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(
                8,
                8,
                ColorFormat: TextureFormat.Rgba8Srgb,
                Depth: true,
                SampleCount: 4
            )
        );
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-srgb-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clear,
            [],
            target.TargetId,
            [Capture(invert)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        // The store-back writes linear color into an sRGB attachment; the fixed-function encode stores it
        // and the readback decodes it back to the linear value the sampling shader would see.
        AssertColorNear(expected, harness.ReadRenderTargetColor(target, 4, 4), 0.01f);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AnEffectOnANeverWrittenTargetMarksItColorWritten()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(8, 8));
        using Shader sample = CreateEffectShader(
            harness,
            "shaders/post-unwritten-sample",
            SAMPLE_FRAGMENT_SOURCE
        );
        VulkanDrawTestHarness.PassSpec pass = new(
            CreateOrthoView(0, 0, target.Width, target.Height, 2f),
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.None,
            Color.Black,
            [],
            target.TargetId,
            [Capture(sample)]
        );

        harness.Submit(harness.CreateFrameWithPasses(1, pass));

        // The source color was never written, but the store-back still wrote the target for the camera
        // viewport, so the target must report color content instead of the unwritten default.
        Assert.True(host.Backend.HasRenderTargetColorContentForTests(target.TargetId));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ACameraCapturedChainRendersThroughTheRealCameraService()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        (int width, int height) = SceneSize(host);
        using CameraTestHost cameraHost = new(width, height);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-camera-invert",
            INVERT_FRAGMENT_SOURCE
        );
        PostProcessStack stack = new();
        stack.Add(new ShaderPostProcessEffect(invert));
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);

        cameraHost.BeginFrame();
        using (
            Camera camera = cameraHost.CreatePerspectiveCamera(
                clearColor: clear,
                postProcess: stack
            )
        )
        {
            Assert.True(cameraHost.Packet.Passes[0].HasEnabledPostProcessEffects);
        }

        cameraHost.EndFrame();
        host.Backend.Submit(cameraHost.Packet);
        host.Backend.Present();
        cameraHost.Packet.Retention.Release();

        // The whole tick path runs: the camera service snapshots the caller's stack, the packet carries
        // the frozen effect, and the renderer records the chain from that snapshot.
        (int x, int y) = SceneCenter(host);
        AssertColorNear(expected, harness.ReadColor((uint)x, (uint)y), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizingTheSceneTargetKeepsAChainWorking()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using Shader invert = CreateEffectShader(
            harness,
            "shaders/post-resize-scene-invert",
            INVERT_FRAGMENT_SOURCE
        );
        Color clear = new(0.25f, 0.5f, 0.75f, 1f);
        Color expected = new(0.75f, 0.5f, 0.25f, 1f);

        VulkanDrawTestHarness.PassSpec FullFrame()
        {
            (int width, int height) = SceneSize(host);
            return new VulkanDrawTestHarness.PassSpec(
                CreateOrthoView(0, 0, width, height, 2f),
                0,
                0,
                width,
                height,
                ClearMode.ColorAndDepth,
                clear,
                [],
                PostProcessEffects: [Capture(invert)]
            );
        }

        harness.Submit(harness.CreateFrameWithPasses(1, FullFrame()));
        AssertColorNear(expected, harness.ReadColor(8, 8), HDR_TOLERANCE);
        int beforeResize = host.Backend.PostTargetRecreationCountForTests;

        if (!TryResizeWindow(host, 320, 240))
        {
            Assert.Skip("The host did not apply the SDL window resize synchronously.");
        }

        host.SyncWithWindow();
        host.Backend.Resize(320, 240);
        harness.Submit(harness.CreateFrameWithPasses(2, FullFrame()));

        Assert.Equal(320u, host.SceneTarget.Extent.Width);
        Assert.Equal(240u, host.SceneTarget.Extent.Height);
        Assert.True(host.Backend.PostTargetRecreationCountForTests > beforeResize);
        AssertColorNear(expected, harness.ReadColor(8, 8), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static Shader CreateEffectShader(
        VulkanDrawTestHarness harness,
        string path,
        string fragmentSource,
        ShaderUniformAsset[]? uniforms = null,
        string[]? textureNames = null
    )
    {
        string[] declaredTextures = textureNames ?? ["sourceColor"];
        return harness.CreateShader(
            path,
            FULLSCREEN_VERTEX_SOURCE,
            fragmentSource,
            uniforms,
            declaredTextures
        );
    }

    private static PostProcessEffectSnapshot Capture(Shader shader) =>
        CaptureEffect(new ShaderPostProcessEffect(shader));

    private static PostProcessEffectSnapshot CaptureEffect(PostProcessEffect effect) =>
        PostProcessSnapshots.Capture(effect, targetHasDepth: true, "the app scene target");

    private static Color Invert(Color color) =>
        new(1f - color.R, 1f - color.G, 1f - color.B, color.A);

    private static VulkanDrawTestHarness.CameraView CreateOrthoView(
        int x,
        int y,
        int width,
        int height,
        float worldHeight
    )
    {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = width / (float)height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(
            worldHeight,
            aspect,
            0.1f,
            10f
        );
        return new VulkanDrawTestHarness.CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            width,
            height
        );
    }

    private static (int Width, int Height) SceneSize(BackendTestHost host) =>
        (checked((int)host.SceneTarget.Extent.Width), checked((int)host.SceneTarget.Extent.Height));

    private static (int X, int Y) SceneCenter(BackendTestHost host) =>
        (
            checked((int)host.SceneTarget.Extent.Width) / 2,
            checked((int)host.SceneTarget.Extent.Height) / 2
        );

    private static float ExpectedReverseZDepth(
        VulkanDrawTestHarness.CameraView camera,
        Vector3 worldPoint
    )
    {
        Vector4 clip = Vector4.Transform(new Vector4(worldPoint, 1f), camera.ViewProjection);
        return clip.Z / clip.W;
    }

    private static void AssertColorNear(Color expected, Color actual, float tolerance)
    {
        AssertColorNear(expected, (actual.R, actual.G, actual.B), tolerance);
        Assert.True(
            MathF.Abs(expected.A - actual.A) <= tolerance,
            $"Alpha {actual.A} is not within {tolerance} of {expected.A}."
        );
    }

    private static void AssertColorNear(
        Color expected,
        (float R, float G, float B) actual,
        float tolerance
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

    private static void AssertDepthNear(float expected, float actual)
    {
        Assert.True(
            MathF.Abs(expected - actual) <= DEPTH_TOLERANCE,
            $"Depth {actual} is not within {DEPTH_TOLERANCE} of {expected}."
        );
    }

    private static void AssertPixelsMatchReference(
        byte[] pixels,
        Color[,] colors,
        TonemappingSnapshot snapshot,
        TonemappingMode tonemappingMode
    )
    {
        int height = colors.GetLength(0);
        int width = colors.GetLength(1);
        Assert.Equal(checked(width * height * 4), pixels.Length);
        float exposureMultiplier = MathF.Pow(2f, snapshot.Exposure);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color color = colors[y, x];
                Vector3 present = AgxToneMap.Present(
                    new Vector3(color.R, color.G, color.B),
                    exposureMultiplier,
                    tonemappingMode,
                    PresentEncoding.ManualSrgb,
                    WindowHdrState.Sdr,
                    snapshot.AgxWhite,
                    snapshot.AgxContrast
                );
                int offset = ((y * width) + x) * 4;
                AssertByteWithinOne(Quantize(present.X), pixels[offset], x, y, "red");
                AssertByteWithinOne(Quantize(present.Y), pixels[offset + 1], x, y, "green");
                AssertByteWithinOne(Quantize(present.Z), pixels[offset + 2], x, y, "blue");
                Assert.Equal(byte.MaxValue, pixels[offset + 3]);
            }
        }
    }

    private static void AssertByteWithinOne(int expected, byte actual, int x, int y, string channel)
    {
        Assert.True(
            Math.Abs(expected - actual) <= 1,
            $"The {channel} byte at ({x}, {y}) is {actual}, expected {expected} within 1."
        );
    }

    private static bool TryResizeWindow(BackendTestHost host, int width, int height)
    {
        host.Platform.Window.SetSize(width, height);
        for (int attempt = 0; attempt < 200; attempt++)
        {
            host.Platform.Pump(TimeSpan.Zero);
            if (
                host.Platform.Window.RenderWidth == width
                && host.Platform.Window.RenderHeight == height
            )
            {
                return true;
            }
        }

        return false;
    }

    private static byte Quantize(float channel) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>
    /// A test-only built-in multi-pass effect: a half-extent scale pass into an auxiliary target, then a
    /// full-extent composite pass from it into the chain target, with one instance record per pass.
    /// </summary>
    /// <remarks>
    /// The two passes use different shaders and uniform sizes, so their strides differ. The effect
    /// reports its ordered record layout, which is what the frame's instance-byte simulation walks.
    /// </remarks>
    private sealed class TestBuiltInEffect : PostProcessEffectSnapshot
    {
        private readonly Shader _scale;
        private readonly Shader _composite;
        private readonly byte[] _scaleInstance;
        private readonly byte[] _compositeInstance;
        private readonly PostProcessInstanceRecord[] _layout;

        internal TestBuiltInEffect(Shader scale, Shader composite)
        {
            _scale = scale;
            _composite = composite;
            _scaleInstance = scale.InstanceData.ToArray();
            _compositeInstance = composite.InstanceData.ToArray();
            _layout =
            [
                new PostProcessInstanceRecord(
                    ShaderAbiLayout.INSTANCE_HEADER_SIZE + _scaleInstance.Length,
                    1
                ),
                new PostProcessInstanceRecord(
                    ShaderAbiLayout.INSTANCE_HEADER_SIZE + _compositeInstance.Length,
                    1
                ),
            ];
        }

        internal int FirstStride => _layout[0].Stride;

        internal int SecondStride => _layout[1].Stride;

        internal override ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => _layout;

        internal override void Retain(FrameAssetRetention retention)
        {
            ArgumentNullException.ThrowIfNull(retention);
            retention.Add(_scale.Program.Lifetime);
            retention.Add(_composite.Program.Lifetime);
        }

        internal override void Run(PostProcessContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Extent2D auxiliaryExtent = new()
            {
                Width = (uint)Math.Max(2, context.Extent.Width / 2),
                Height = (uint)Math.Max(2, context.Extent.Height / 2),
            };

            VulkanPostTarget auxiliary = context.RentColorTarget(auxiliaryExtent);
            Pipeline scalePipeline = context.GetOrCreateFullscreenPipeline(
                _scale.Program.Lifetime,
                PostProcessContext.FullscreenRenderState
            );
            ulong scaleOffset = context.ReserveInstanceRecord(_layout[0].Stride);
            WriteRecord(
                context,
                _scale,
                _scaleInstance,
                scaleOffset,
                _layout[0].Stride,
                context.SourceColorSlot
            );
            context.RecordFullscreenPass(auxiliary, scalePipeline, scaleOffset, _layout[0].Stride);
            context.CompleteAuxiliaryTarget(auxiliary);

            VulkanPostTarget destination = context.RentColorTarget();
            Pipeline compositePipeline = context.GetOrCreateFullscreenPipeline(
                _composite.Program.Lifetime,
                PostProcessContext.FullscreenRenderState
            );
            ulong compositeOffset = context.ReserveInstanceRecord(_layout[1].Stride);
            WriteRecord(
                context,
                _composite,
                _compositeInstance,
                compositeOffset,
                _layout[1].Stride,
                auxiliary.BindlessSlot
            );
            context.RecordFullscreenPass(
                destination,
                compositePipeline,
                compositeOffset,
                _layout[1].Stride
            );
            context.CompleteColorTarget(destination);
        }

        private static void WriteRecord(
            PostProcessContext context,
            Shader shader,
            ReadOnlySpan<byte> instanceData,
            ulong byteOffset,
            int stride,
            uint sourceTextureSlot
        )
        {
            Span<byte> record = context.GetInstanceRecordSpan(byteOffset, stride);
            VulkanInstanceRecordWriter.WriteMatrix(
                record,
                ShaderAbiLayout.TRANSFORM_OFFSET,
                Matrix4x4.Identity
            );
            VulkanInstanceRecordWriter.WriteVector4(
                record,
                ShaderAbiLayout.COLOR_OFFSET,
                Vector4.One
            );
            VulkanInstanceRecordWriter.WriteVector4(
                record,
                ShaderAbiLayout.UV_RECT_OFFSET,
                new Vector4(0f, 0f, 1f, 1f)
            );
            instanceData.CopyTo(record[ShaderAbiLayout.INSTANCE_HEADER_SIZE..]);
            VulkanInstanceRecordWriter.WriteUInt32(
                record,
                shader.Abi.Textures[0].IndexOffset,
                sourceTextureSlot
            );
        }
    }
}
