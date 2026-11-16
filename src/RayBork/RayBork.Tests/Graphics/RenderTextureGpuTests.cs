using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the public render texture color and depth paths: a camera routing into a runtime target,
/// the resolved color and depth views sampled by a later camera, the per-target image barriers, the
/// render target formats and sample counts, MSAA depth resolve, and the target lifetime.
/// </summary>
/// <remarks>
/// The tests read the scene target and the render target's resolved images through the diagnostic
/// readback probes and assert numeric tolerances. They also assert validation-layer cleanliness. The
/// test host requests core validation, not synchronization validation, so the access masks are proven
/// by the rendered result and the clean core validation, not by the synchronization model.
/// </remarks>
public sealed class RenderTextureGpuTests {
    private const float HDR_TOLERANCE = 3e-3f;
    private const float BYTE_TOLERANCE = 6e-3f;
    private const float DEPTH_TOLERANCE = 3e-3f;

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

    private const string TINT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = rbInstance.Color;
        }
        """;

    private const string SAMPLE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(mainTexture, vUv);
        }
        """;

    private const string DEPTH_SAMPLE_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            float depth = texture(mainTexture, vUv).r;
            oColor = vec4(depth, depth, depth, 1.0);
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AHdrTargetDrawnByACameraIsSampledByALaterSceneCamera() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-sample");
        Color known = new(0.25f, 0.5f, 0.75f, 1f);
        Mesh targetQuad = harness.CreateQuad(
            "meshes/render-target-quad",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        Assert.True(target.IsHdr);
        Assert.Equal(
            VulkanSampleCountSelection.Select(4, host.Device.Capabilities.SupportedSampleCounts),
            target.SampleCount);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, known, Color.Black),
            samplePass));

        (int centerX, int centerY) = SceneCenter(host);
        AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(known, harness.ReadRenderTargetColor(target, 32, 32), HDR_TOLERANCE);
        Assert.Equal(2, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float, 1)]
    [InlineData(TextureFormat.Rgba16Float, 2)]
    [InlineData(TextureFormat.Rgba16Float, 4)]
    [InlineData(TextureFormat.Rgba16Float, 8)]
    [InlineData(TextureFormat.Rgba8Unorm, 4)]
    [InlineData(TextureFormat.Rgba8Srgb, 4)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthTextureSamplesTheResolvedReverseZValue(TextureFormat format, int requestedSampleCount) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(32, 32, format, Depth: true, SampleCount: requestedSampleCount));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-sample");
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        Mesh targetQuad = harness.CreateQuad(
            "meshes/render-target-depth-quad",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float expectedDepth = ExpectedReverseZDepth(targetCamera);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            depthShader,
            depthTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);

        Assert.Equal(TextureFormat.Depth32Float, depthTexture.Format);
        Assert.False(depthTexture.IsSrgb);
        Assert.Equal(TextureFilter.Nearest, depthTexture.Sampler.Filter);
        Assert.Equal(TextureWrap.Clamp, depthTexture.Sampler.Wrap);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.False(depthTexture.IsProcedural);
        Assert.Equal(1, depthTexture.MipLevels);
        Assert.Equal(target.Width, depthTexture.Width);
        Assert.Equal(target.Height, depthTexture.Height);
        Assert.NotNull(target.ColorTexture);
        Assert.Equal(
            VulkanSampleCountSelection.Select(requestedSampleCount, host.Device.Capabilities.SupportedSampleCounts),
            target.SampleCount);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            samplePass));

        (int centerX, int centerY) = SceneCenter(host);
        (float r, float g, float b) = harness.ReadColor((uint)centerX, (uint)centerY);
        AssertDepthNear(expectedDepth, r, DEPTH_TOLERANCE);
        AssertDepthNear(expectedDepth, g, DEPTH_TOLERANCE);
        AssertDepthNear(expectedDepth, b, DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ReadSceneDepthReadsTheResolvedMultisampledDepth() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: 4, hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        if (host.SceneTarget.SampleCount <= 1) {
            Assert.Skip("The host device supports no multisampled depth attachment.");
        }

        Shader tintShader = harness.CreateShader(
            "shaders/render-target-scene-depth-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Mesh quad = harness.CreateQuad("meshes/render-target-scene-depth", halfWidth: aspect, halfHeight: 1f);
        VulkanDrawTestHarness.DrawSpec draw = new(
            quad,
            tintShader,
            [Matrix4x4.Identity],
            RenderState: RenderState.Opaque with { Cull = CullMode.None },
            Tint: Color.White);
        float expectedDepth = ExpectedReverseZDepth(camera);

        harness.SubmitFrame(1, Color.Black, camera, draw);

        (int centerX, int centerY) = SceneCenter(host);
        AssertDepthNear(expectedDepth, harness.ReadDepth((uint)centerX, (uint)centerY), DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthWriteSampleWriteSampleKeepsBothValues() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-read-write-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-read-write");
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-depth-read-write", halfWidth: 1f, halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float nearDepth = ExpectedReverseZDepth(targetCamera, Vector3.Zero);
        float farDepth = ExpectedReverseZDepth(targetCamera, new Vector3(0f, 0f, 0.5f));
        int sceneWidth = checked((int)host.SceneTarget.Extent.Width);
        int sceneHeight = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = sceneWidth / 2;
        int rightWidth = sceneWidth - leftWidth;
        (VulkanDrawTestHarness.PassSpec leftPass, Mesh leftQuad) = CreateSamplePass(
            harness,
            depthShader,
            depthTexture,
            leftWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (VulkanDrawTestHarness.PassSpec rightPass, Mesh rightQuad) = CreateSamplePass(
            harness,
            depthShader,
            depthTexture,
            rightWidth,
            sceneHeight,
            ClearMode.None,
            Color.Black);
        rightPass = rightPass with { ViewportX = leftWidth };

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            leftPass,
            CreateTargetDrawPass(
                target,
                targetQuad,
                tintShader,
                Color.White,
                Color.Black,
                Matrix4x4.CreateTranslation(0f, 0f, 0.5f),
                ClearMode.ColorAndDepth),
            rightPass));

        int centerY = sceneHeight / 2;
        AssertDepthNear(nearDepth, harness.ReadColor((uint)(leftWidth / 2), (uint)centerY).R, DEPTH_TOLERANCE);
        AssertDepthNear(
            farDepth,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)centerY).R,
            DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthTextureCanBeSampledFromALaterFrameWithoutARewrite() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-later-frame-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-later-frame");
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-depth-later-frame", halfWidth: 1f, halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float expectedDepth = ExpectedReverseZDepth(targetCamera);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            depthShader,
            target.DepthTexture!,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            samplePass));
        AssertDepthNear(expectedDepth, harness.ReadColor((uint)(sceneWidth / 2), (uint)(sceneHeight / 2)).R, DEPTH_TOLERANCE);

        // The target is not rewritten; the sampled target enters the frame in ShaderReadOnlyOptimal and
        // PrepareSampled synchronizes the previous frame's resolve with this frame's fragment reads.
        harness.Submit(harness.CreateFrameWithPasses(2, samplePass));

        AssertDepthNear(expectedDepth, harness.ReadColor((uint)(sceneWidth / 2), (uint)(sceneHeight / 2)).R, DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthResolveOnlyCoversTheCameraViewport() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-viewport-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-viewport");
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-depth-viewport", halfWidth: 1f, halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float drawnDepth = ExpectedReverseZDepth(targetCamera, new Vector3(0f, 0f, 0.5f));
        int halfWidth = target.Width / 2;
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            depthShader,
            target.DepthTexture!,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, Color.Black),
            samplePass));
        AssertDepthNear(0f, harness.ReadColor((uint)(sceneWidth / 2), (uint)(sceneHeight / 2)).R, DEPTH_TOLERANCE);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetClearPass(target, Color.Black),
            CreateTargetDrawPass(
                target,
                targetQuad,
                tintShader,
                Color.White,
                Color.Black,
                Matrix4x4.CreateTranslation(0f, 0f, 0.5f),
                ClearMode.None,
                0,
                0,
                halfWidth,
                target.Height),
            samplePass));

        int centerY = sceneHeight / 2;
        int sceneHalfWidth = sceneWidth / 2;
        AssertDepthNear(drawnDepth, harness.ReadColor((uint)(sceneHalfWidth / 2), (uint)centerY).R, DEPTH_TOLERANCE);
        AssertDepthNear(
            0f,
            harness.ReadColor((uint)(sceneHalfWidth + (sceneWidth - sceneHalfWidth) / 2), (uint)centerY).R,
            DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoDepthTargetsInOneFrameKeepTheirOwnDepth() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture nearTarget = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        using RenderTexture farTarget = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-two-depth-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-two-depth");
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-two-depth", halfWidth: 1f, halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, 32, 32, 2f);
        float nearDepth = ExpectedReverseZDepth(targetCamera);
        float farDepth = ExpectedReverseZDepth(targetCamera, new Vector3(0f, 0f, 0.5f));
        int sceneWidth = checked((int)host.SceneTarget.Extent.Width);
        int sceneHeight = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = sceneWidth / 2;
        int rightWidth = sceneWidth - leftWidth;
        (VulkanDrawTestHarness.PassSpec leftPass, Mesh leftQuad) = CreateSamplePass(
            harness,
            depthShader,
            nearTarget.DepthTexture!,
            leftWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (VulkanDrawTestHarness.PassSpec rightPass, Mesh rightQuad) = CreateSamplePass(
            harness,
            depthShader,
            farTarget.DepthTexture!,
            rightWidth,
            sceneHeight,
            ClearMode.None,
            Color.Black);
        rightPass = rightPass with { ViewportX = leftWidth };

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(nearTarget, targetQuad, tintShader, Color.White, Color.Black),
            CreateTargetDrawPass(
                farTarget,
                targetQuad,
                tintShader,
                Color.White,
                Color.Black,
                Matrix4x4.CreateTranslation(0f, 0f, 0.5f),
                ClearMode.ColorAndDepth),
            leftPass,
            rightPass));

        int centerY = sceneHeight / 2;
        AssertDepthNear(nearDepth, harness.ReadColor((uint)(leftWidth / 2), (uint)centerY).R, DEPTH_TOLERANCE);
        AssertDepthNear(
            farDepth,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)centerY).R,
            DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void DepthClearModeNoneLoadsAndDepthClearSamplesZero() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-clear-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-clear");
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-depth-clear", halfWidth: 1f, halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float drawnDepth = ExpectedReverseZDepth(targetCamera);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            depthShader,
            target.DepthTexture!,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (uint centerX, uint centerY) = ((uint)(sceneWidth / 2), (uint)(sceneHeight / 2));

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            CreateTargetPass(target, ClearMode.None, Color.Black),
            samplePass));
        AssertDepthNear(drawnDepth, harness.ReadColor(centerX, centerY).R, DEPTH_TOLERANCE);

        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            CreateTargetPass(target, ClearMode.Depth, Color.Black),
            samplePass));
        AssertDepthNear(0f, harness.ReadColor(centerX, centerY).R, DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SameTickWriteThenSampleThenWriteThenSampleKeepsBothColors() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-read-write");
        int sceneWidth = checked((int)host.SceneTarget.Extent.Width);
        int sceneHeight = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = sceneWidth / 2;
        int rightWidth = sceneWidth - leftWidth;
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        (VulkanDrawTestHarness.PassSpec leftPass, Mesh leftQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            leftWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (VulkanDrawTestHarness.PassSpec rightPass, Mesh rightQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            rightWidth,
            sceneHeight,
            ClearMode.None,
            Color.Black);
        rightPass = rightPass with { ViewportX = leftWidth };

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, red),
            leftPass,
            CreateTargetClearPass(target, green),
            rightPass));

        AssertColorNear(red, harness.ReadColor((uint)(leftWidth / 2), (uint)(sceneHeight / 2)), HDR_TOLERANCE);
        AssertColorNear(
            green,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)(sceneHeight / 2)),
            HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SamplingTheTargetInTheWritingCameraIsRejected() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-self-sample");
        Mesh quad = harness.CreateQuad("meshes/render-target-self-quad");
        sampleShader.SetUniform("mainTexture", target.ColorTexture);
        Shader modelShader = CreateSampleShader(harness, "shaders/render-target-self-model");
        modelShader.SetUniform("mainTexture", target.ColorTexture);
        using Model model = new(
            new ModelPart("self-sample", quad, modelShader, RenderState.Opaque with { Cull = CullMode.None }));

        FakePlatformWindow window = new(static () => { }) { Width = 64, Height = 64 };
        CameraService cameras = new(window);
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        cameras.BeginFrame(packet, 0.0, 0.0);
        try {
            using (Camera camera = cameras.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                0f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null,
                target)) {
                InvalidOperationException meshException = Assert.Throws<InvalidOperationException>(
                    () => camera.DrawMesh(quad, Transform.Identity, sampleShader));
                Assert.Contains("same pass", meshException.Message, StringComparison.Ordinal);

                // DrawModel records through the part material path; it must run the same check.
                InvalidOperationException modelException = Assert.Throws<InvalidOperationException>(
                    () => camera.DrawModel(model, Transform.Identity));
                Assert.Contains("same pass", modelException.Message, StringComparison.Ordinal);

                // The depth view carries the same target id, so the same-pass check rejects it too.
                sampleShader.SetUniform("mainTexture", target.DepthTexture);
                InvalidOperationException depthMeshException = Assert.Throws<InvalidOperationException>(
                    () => camera.DrawMesh(quad, Transform.Identity, sampleShader));
                Assert.Contains("same pass", depthMeshException.Message, StringComparison.Ordinal);
                InvalidOperationException depthModelException = Assert.Throws<InvalidOperationException>(
                    () => camera.DrawModel(model, Transform.Identity));
                Assert.Contains("same pass", depthModelException.Message, StringComparison.Ordinal);
            }

            // A later camera pass may sample the same depth view; only the writing pass is rejected.
            Mesh sceneQuad = harness.CreateQuad("meshes/render-target-self-scene-quad");
            using (Camera screen = cameras.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                0f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null)) {
                screen.DrawMesh(sceneQuad, Transform.Identity, sampleShader);
            }

            Assert.Equal(2, packet.Passes.Length);
        } finally {
            cameras.AbandonFrame();
            packet.Retention.Release();
        }
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float, 1)]
    [InlineData(TextureFormat.Rgba16Float, 4)]
    [InlineData(TextureFormat.Rgba16Float, 8)]
    [InlineData(TextureFormat.Rgba8Unorm, 1)]
    [InlineData(TextureFormat.Rgba8Unorm, 4)]
    [InlineData(TextureFormat.Rgba8Srgb, 1)]
    [InlineData(TextureFormat.Rgba8Srgb, 4)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ColorFormatsAndSampleCountsSampleTheDocumentedValues(
        TextureFormat format,
        int requestedSampleCount) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-format");
        RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(64, 64, format, Depth: true, SampleCount: requestedSampleCount));
        try {
            int expectedSampleCount = VulkanSampleCountSelection.Select(
                requestedSampleCount,
                host.Device.Capabilities.SupportedSampleCounts);
            Assert.Equal(expectedSampleCount, target.SampleCount);
            Assert.Equal(format == TextureFormat.Rgba16Float, target.IsHdr);
            Assert.Equal(format == TextureFormat.Rgba8Srgb, target.ColorTexture.IsSrgb);
            Color known = new(0.25f, 0.5f, 0.75f, 1f);
            (int sceneWidth, int sceneHeight) = SceneSize(host);
            (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
                harness,
                sampleShader,
                target.ColorTexture,
                sceneWidth,
                sceneHeight,
                ClearMode.ColorAndDepth,
                Color.Black);

            harness.Submit(harness.CreateFrameWithPasses(
                1,
                CreateTargetClearPass(target, known),
                samplePass));

            float tolerance = format == TextureFormat.Rgba16Float ? HDR_TOLERANCE : BYTE_TOLERANCE;
            (int centerX, int centerY) = SceneCenter(host);
            AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), tolerance);
            AssertColorNear(known, harness.ReadRenderTargetColor(target, 32, 32), tolerance);
            Assert.Empty(host.Instance.ConsumeValidationErrors());
        } finally {
            target.Dispose();
            host.Backend.WaitForFrameResourcesIdle();
            host.ProcessRenderTextures();
            sampleShader.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void OnlyTheCameraViewportRegionOfTheTargetIsUpdated() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-viewport");
        Color background = new(0.25f, 0.25f, 0.25f, 1f);
        Color red = new(1f, 0f, 0f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        VulkanDrawTestHarness.CameraView halfCamera = CreateOrthoView(0, 0, 32, 64, 2f);
        VulkanDrawTestHarness.PassSpec partialClear = new(
            halfCamera,
            0,
            0,
            32,
            32,
            ClearMode.ColorAndDepth,
            red,
            [],
            target.TargetId);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, background),
            partialClear,
            samplePass));

        AssertColorNear(red, harness.ReadRenderTargetColor(target, 16, 16), HDR_TOLERANCE);
        AssertColorNear(background, harness.ReadRenderTargetColor(target, 48, 16), HDR_TOLERANCE);
        AssertColorNear(red, harness.ReadColor((uint)(sceneWidth / 4), (uint)(sceneHeight / 4)), HDR_TOLERANCE);
        AssertColorNear(
            background,
            harness.ReadColor((uint)((sceneWidth * 3) / 4), (uint)(sceneHeight / 4)),
            HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoTargetsInOneFrameKeepTheirOwnContent() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture redTarget = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        using RenderTexture greenTarget = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-two");
        int sceneWidth = checked((int)host.SceneTarget.Extent.Width);
        int sceneHeight = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = sceneWidth / 2;
        int rightWidth = sceneWidth - leftWidth;
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        (VulkanDrawTestHarness.PassSpec leftPass, Mesh leftQuad) = CreateSamplePass(
            harness,
            sampleShader,
            redTarget.ColorTexture,
            leftWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (VulkanDrawTestHarness.PassSpec rightPass, Mesh rightQuad) = CreateSamplePass(
            harness,
            sampleShader,
            greenTarget.ColorTexture,
            rightWidth,
            sceneHeight,
            ClearMode.None,
            Color.Black);
        rightPass = rightPass with { ViewportX = leftWidth };

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(redTarget, red),
            CreateTargetClearPass(greenTarget, green),
            leftPass,
            rightPass));

        AssertColorNear(red, harness.ReadColor((uint)(leftWidth / 2), (uint)(sceneHeight / 2)), HDR_TOLERANCE);
        AssertColorNear(
            green,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)(sceneHeight / 2)),
            HDR_TOLERANCE);
        AssertColorNear(red, harness.ReadRenderTargetColor(redTarget, 16, 16), HDR_TOLERANCE);
        AssertColorNear(green, harness.ReadRenderTargetColor(greenTarget, 16, 16), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ARecordedFrameRetainsTheTargetViewAndDisposeReleasesIt() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-retention");
        Color known = new(0.1f, 0.6f, 0.2f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);

        Assert.Equal(string.Empty, target.ColorTexture.Path);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Throws<InvalidOperationException>(target.ColorTexture.Dispose);
        Assert.Throws<InvalidOperationException>(depthTexture.Dispose);
        Assert.False(target.ColorTexture.IsDisposed);
        Assert.False(depthTexture.IsDisposed);

        RenderFramePacket packet = harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, known),
            samplePass);
        // Model the frame ring: the recorded frame holds the target's views until the renderer released
        // the slot, so disposing the target before submission must not destroy it. Both views forward
        // their hold to the same target lifetime.
        packet.Retention.Add(target.ColorTexture.RetentionTarget);
        packet.Retention.Add(depthTexture.RetentionTarget);
        target.Dispose();
        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);
        Assert.True(depthTexture.IsDisposed);

        harness.Submit(packet);

        (int centerX, int centerY) = SceneCenter(host);
        AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out _));

        packet.Retention.Release();
        host.Backend.WaitForFrameResourcesIdle();
        host.ProcessRenderTextures();

        Assert.False(host.Backend.TryGetRenderTarget(target.TargetId, out _));
        target.Dispose();
        Assert.Equal(string.Empty, target.ColorTexture.Path);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ATargetIsReusedAcrossFramesOnlyAfterThePreviousFrameCompleted() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-reuse");
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (int centerX, int centerY) = SceneCenter(host);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, red),
            samplePass));
        AssertColorNear(red, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);

        // The second frame is recorded without waiting for the first frame on the host. The renderer
        // waits for the target's last-use fence before it overwrites the target.
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetClearPass(target, green),
            samplePass));
        AssertColorNear(green, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(green, harness.ReadRenderTargetColor(target, 16, 16), HDR_TOLERANCE);
        Assert.False(target.IsDisposed);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ADepthlessTargetRecordsColorOnlyDraws(int sampleCount) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(32, 32, TextureFormat.Rgba16Float, Depth: false, SampleCount: sampleCount));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depthless",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-depthless-sample");
        Color known = new(0.2f, 0.4f, 0.6f, 1f);
        Mesh targetQuad = harness.CreateQuad(
            "meshes/render-target-depthless-quad",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        // The default opaque state keeps the depth test and depth write enabled. A depthless target
        // records the draw anyway and treats both as no-ops; it does not reject the render state.
        VulkanDrawTestHarness.DrawSpec draw = new(
            targetQuad,
            tintShader,
            [Matrix4x4.Identity],
            RenderState: RenderState.Opaque with { Cull = CullMode.None },
            Tint: known);
        VulkanDrawTestHarness.PassSpec drawPass = new(
            camera,
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            Color.Black,
            [draw],
            target.TargetId);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);

        harness.Submit(harness.CreateFrameWithPasses(1, drawPass, samplePass));

        Assert.Equal(
            VulkanSampleCountSelection.Select(sampleCount, host.Device.Capabilities.SupportedSampleCounts),
            target.SampleCount);
        Assert.Null(target.DepthTexture);
        (int centerX, int centerY) = SceneCenter(host);
        AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(known, harness.ReadRenderTargetColor(target, 16, 16), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ARealCameraTargetsAndSamplesTheRenderTextureEndToEnd() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-real-camera-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-real-camera-sample");
        sampleShader.SetUniform("mainTexture", target.ColorTexture);
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-real-camera-quad");
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        float sceneAspect = sceneWidth / (float)sceneHeight;
        Mesh sceneQuad = harness.CreateQuad(
            "meshes/render-target-real-camera-scene",
            halfWidth: sceneAspect,
            halfHeight: 1f);
        Color known = new(0.3f, 0.6f, 0.9f, 1f);
        CameraService cameras = new(
            new FakePlatformWindow(static () => { }) {
                Width = sceneWidth,
                Height = sceneHeight,
            });
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        cameras.BeginFrame(packet, 0.0, 0.0);
        try {
            using (Camera offscreen = cameras.CreateCamera(
                new Transform(new Vector3(0f, 0f, 1f)),
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                10f,
                null,
                target)) {
                offscreen.DrawMesh(
                    targetQuad,
                    Transform.Identity,
                    tintShader,
                    RenderState.Opaque with { Cull = CullMode.None },
                    known);
            }

            using (Camera screen = cameras.CreateCamera(
                new Transform(new Vector3(0f, 0f, 1f)),
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                10f,
                null)) {
                screen.DrawMesh(
                    sceneQuad,
                    Transform.Identity,
                    sampleShader,
                    RenderState.Opaque with { Cull = CullMode.None });
            }

            cameras.EndFrame();
            Assert.Equal(target.TargetId, packet.Passes[0].TargetId);
            Assert.Equal(0, packet.Passes[1].TargetId);

            // The real camera pass retained the target through the frame's retention set, so disposing
            // the public object before submission must not destroy the images the frame samples.
            target.Dispose();
            harness.Submit(packet);

            (int centerX, int centerY) = SceneCenter(host);
            AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
            Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out _));
            Assert.Empty(host.Instance.ConsumeValidationErrors());

            packet.Retention.Release();
            host.Backend.WaitForFrameResourcesIdle();
            host.ProcessRenderTextures();
            Assert.False(host.Backend.TryGetRenderTarget(target.TargetId, out _));
        } finally {
            cameras.AbandonFrame();
            if (!packet.Retention.IsReleased) {
                packet.Retention.Release();
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ADeviceRecoveryRecreatesALiveRenderTarget() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-recovery");
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (int centerX, int centerY) = SceneCenter(host);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, red),
            samplePass));
        AssertColorNear(red, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);

        host.Backend.SimulateDeviceLossForTests();
        // The next frame runs the recovery and is dropped; the live render texture is recreated from
        // its retained description.
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetClearPass(target, green),
            samplePass));
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        harness.RestoreAssets();

        int expectedSampleCount = VulkanSampleCountSelection.Select(
            4,
            host.Device.Capabilities.SupportedSampleCounts);
        Assert.Equal(expectedSampleCount, target.SampleCount);
        Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? recovered));
        Assert.NotNull(recovered);
        Assert.Equal(target.SampleCount, recovered.SampleCount);

        harness.Submit(harness.CreateFrameWithPasses(
            3,
            CreateTargetClearPass(target, green),
            samplePass));
        AssertColorNear(green, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(green, harness.ReadRenderTargetColor(target, 16, 16), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ADeviceRecoveryRecreatesTheDepthViewAndItsResolvedImage() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-depth-recovery-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-depth-recovery-sample");
        Mesh targetQuad = harness.CreateQuad(
            "meshes/render-target-depth-recovery-quad",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float expectedDepth = ExpectedReverseZDepth(targetCamera);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh sampleQuad) = CreateSamplePass(
            harness,
            depthShader,
            depthTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        (int centerX, int centerY) = SceneCenter(host);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            samplePass));
        (float beforeRed, _, _) = harness.ReadColor((uint)centerX, (uint)centerY);
        AssertDepthNear(expectedDepth, beforeRed, DEPTH_TOLERANCE);
        Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? before));
        Assert.NotNull(before);
        Assert.NotEqual(0ul, before.ResolvedDepthView.Handle);

        host.Backend.SimulateDeviceLossForTests();
        // The next frame runs the recovery and is dropped; the live depth view is recreated from the
        // retained description behind the same public Texture object.
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            samplePass));
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        harness.RestoreAssets();

        Assert.Same(depthTexture, target.DepthTexture);
        Assert.False(depthTexture.IsDisposed);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Equal(TextureFormat.Depth32Float, depthTexture.Format);
        Assert.Equal(
            VulkanSampleCountSelection.Select(4, host.Device.Capabilities.SupportedSampleCounts),
            target.SampleCount);
        Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? recovered));
        Assert.NotNull(recovered);
        Assert.True(recovered.HasDepth);
        Assert.Equal(target.SampleCount, recovered.SampleCount);
        Assert.NotEqual(0ul, recovered.ResolvedDepthView.Handle);

        harness.Submit(harness.CreateFrameWithPasses(
            3,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            samplePass));
        (float afterRed, _, _) = harness.ReadColor((uint)centerX, (uint)centerY);
        AssertDepthNear(expectedDepth, afterRed, DEPTH_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void BackendShutdownDisposesLiveRenderTextures() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);

        host.Backend.Dispose();

        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);
        Assert.True(depthTexture.IsDisposed);
        Assert.False(host.Backend.TryGetRenderTarget(target.TargetId, out _));
        target.Dispose();
        Assert.True(target.IsDisposed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeRecreatesTheTargetAndBothViewsInPlace() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Texture colorTexture = target.ColorTexture;
        Texture depthTexture = Assert.IsType<Texture>(target.DepthTexture);
        long targetId = target.TargetId;
        int sampleCount = target.SampleCount;
        int liveSlots = host.Backend.BindlessTextures.LiveCount;
        Assert.True(host.Backend.TryGetRenderTarget(targetId, out VulkanRenderTarget? before));
        Assert.NotNull(before);
        ulong previousRenderPass = before.RenderPass.Handle;
        ulong previousFramebuffer = before.Framebuffer.Handle;
        ulong previousColorView = before.ResolvedColorView.Handle;
        ulong previousDepthView = before.ResolvedDepthView.Handle;

        host.ResizeRenderTexture(target, 96, 48);

        Assert.Equal(targetId, target.TargetId);
        Assert.Equal(96, target.Width);
        Assert.Equal(48, target.Height);
        Assert.Equal(sampleCount, target.SampleCount);
        Assert.True(target.IsHdr);
        Assert.Same(colorTexture, target.ColorTexture);
        Assert.Same(depthTexture, target.DepthTexture);
        Assert.Equal(96, colorTexture.Width);
        Assert.Equal(48, colorTexture.Height);
        Assert.Equal(96, depthTexture.Width);
        Assert.Equal(48, depthTexture.Height);
        Assert.Equal(TextureFormat.Rgba16Float, colorTexture.Format);
        Assert.Equal(TextureFormat.Depth32Float, depthTexture.Format);
        Assert.Equal(string.Empty, colorTexture.Path);
        Assert.Equal(string.Empty, depthTexture.Path);
        Assert.Equal(liveSlots, host.Backend.BindlessTextures.LiveCount);
        Assert.True(host.Backend.TryGetRenderTarget(targetId, out VulkanRenderTarget? resized));
        Assert.NotNull(resized);
        Assert.NotSame(before, resized);
        Assert.Equal(96u, resized.Extent.Width);
        Assert.Equal(48u, resized.Extent.Height);
        Assert.NotEqual(previousRenderPass, resized.RenderPass.Handle);
        Assert.NotEqual(previousFramebuffer, resized.Framebuffer.Handle);
        Assert.NotEqual(previousColorView, resized.ResolvedColorView.Handle);
        Assert.NotEqual(previousDepthView, resized.ResolvedDepthView.Handle);

        Shader tintShader = harness.CreateShader(
            "shaders/render-target-resize-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-resize-sample");
        Shader depthShader = CreateDepthSampleShader(harness, "shaders/render-target-resize-depth-sample");
        Mesh targetQuad = harness.CreateQuad(
            "meshes/render-target-resize-quad",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec colorPass, Mesh _) = CreateSamplePass(
            harness,
            sampleShader,
            colorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        Color known = new(0.2f, 0.4f, 0.8f, 1f);
        (int centerX, int centerY) = SceneCenter(host);

        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, known, Color.Black),
            colorPass));
        AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(known, harness.ReadRenderTargetColor(target, 48, 24), HDR_TOLERANCE);

        VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        float expectedDepth = ExpectedReverseZDepth(targetCamera);
        (VulkanDrawTestHarness.PassSpec depthPass, Mesh _) = CreateSamplePass(
            harness,
            depthShader,
            depthTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
            depthPass));
        AssertDepthNear(expectedDepth, harness.ReadColor((uint)centerX, (uint)centerY).R, DEPTH_TOLERANCE);

        // An unchanged size recreates nothing: the render pass and the bindless slots stay the same.
        ulong currentRenderPass = resized.RenderPass.Handle;
        target.Resize(96, 48);
        Assert.True(host.Backend.TryGetRenderTarget(targetId, out VulkanRenderTarget? unchanged));
        Assert.NotNull(unchanged);
        Assert.Equal(currentRenderPass, unchanged.RenderPass.Handle);
        Assert.Equal(liveSlots, host.Backend.BindlessTextures.LiveCount);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeIsRejectedWhileACameraOrRecordedFrameHoldsTheTargetThenWorksWhenIdle() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(64, 64));
        Shader tintShader = harness.CreateShader(
            "shaders/render-target-resize-in-use-tint",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-resize-in-use-sample");
        sampleShader.SetUniform("mainTexture", target.ColorTexture);
        Mesh targetQuad = harness.CreateQuad("meshes/render-target-resize-in-use-quad");
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        float sceneAspect = sceneWidth / (float)sceneHeight;
        Mesh sceneQuad = harness.CreateQuad(
            "meshes/render-target-resize-in-use-scene",
            halfWidth: sceneAspect,
            halfHeight: 1f);
        Color known = new(0.3f, 0.6f, 0.9f, 1f);
        CameraService cameras = new(
            new FakePlatformWindow(static () => { }) {
                Width = sceneWidth,
                Height = sceneHeight,
            });
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        cameras.BeginFrame(packet, 0.0, 0.0);
        try {
            using (Camera offscreen = cameras.CreateCamera(
                new Transform(new Vector3(0f, 0f, 1f)),
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                10f,
                null,
                target)) {
                offscreen.DrawMesh(
                    targetQuad,
                    Transform.Identity,
                    tintShader,
                    RenderState.Opaque with { Cull = CullMode.None },
                    known);
                // The current frame holds the target because a camera targets it.
                Assert.Throws<InvalidOperationException>(() => target.Resize(96, 48));
                Assert.Equal(64, target.Width);
            }

            using (Camera screen = cameras.CreateCamera(
                new Transform(new Vector3(0f, 0f, 1f)),
                CameraProjection.Orthographic,
                0f,
                2f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                10f,
                null)) {
                screen.DrawMesh(
                    sceneQuad,
                    Transform.Identity,
                    sampleShader,
                    RenderState.Opaque with { Cull = CullMode.None });
            }

            cameras.EndFrame();
            // The sealed frame still references the target, so the recorded frame's hold rejects a resize.
            Assert.Throws<InvalidOperationException>(() => target.Resize(96, 48));

            harness.Submit(packet);
            (int centerX, int centerY) = SceneCenter(host);
            AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
            Assert.Throws<InvalidOperationException>(() => target.Resize(96, 48));

            // The render loop releases the packet's retention after present; the test owns it here.
            packet.Retention.Release();
            host.Backend.WaitForFrameResourcesIdle();
            host.ResizeRenderTexture(target, 96, 48);
            Assert.Equal(96, target.Width);
            Assert.Equal(48, target.Height);
            Assert.Equal(96, target.ColorTexture.Width);
            Assert.Equal(48, target.ColorTexture.Height);
            Assert.Empty(host.Instance.ConsumeValidationErrors());
        } finally {
            cameras.AbandonFrame();
            if (!packet.Retention.IsReleased) {
                packet.Retention.Release();
            }

            target.Dispose();
        }
    }

    [Theory]
    [InlineData(TextureFormat.Rgba16Float, 1, true)]
    [InlineData(TextureFormat.Rgba16Float, 4, true)]
    [InlineData(TextureFormat.Rgba8Unorm, 2, true)]
    [InlineData(TextureFormat.Rgba8Srgb, 4, true)]
    [InlineData(TextureFormat.Rgba16Float, 4, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ResizeKeepsTheTargetDescription(TextureFormat format, int requestedSampleCount, bool depth) {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(
            new RenderTextureDescription(32, 32, format, depth, requestedSampleCount));
        int effectiveSampleCount = VulkanSampleCountSelection.Select(
            requestedSampleCount,
            host.Device.Capabilities.SupportedSampleCounts);
        Texture colorTexture = target.ColorTexture;
        Texture? depthTexture = target.DepthTexture;

        host.ResizeRenderTexture(target, 40, 24);

        Assert.Equal(40, target.Width);
        Assert.Equal(24, target.Height);
        Assert.Equal(effectiveSampleCount, target.SampleCount);
        Assert.Same(colorTexture, target.ColorTexture);
        Assert.Equal(40, colorTexture.Width);
        Assert.Equal(24, colorTexture.Height);
        Assert.Equal(format, colorTexture.Format);
        Assert.Equal(format == TextureFormat.Rgba8Srgb, colorTexture.IsSrgb);
        Assert.Equal(string.Empty, colorTexture.Path);
        Assert.Equal(1, colorTexture.MipLevels);
        InvalidOperationException colorDispose = Assert.Throws<InvalidOperationException>(colorTexture.Dispose);
        Assert.Contains("RenderTexture", colorDispose.Message, StringComparison.Ordinal);
        if (depth) {
            Texture view = Assert.IsType<Texture>(target.DepthTexture);
            Assert.Same(depthTexture, view);
            Assert.Equal(TextureFormat.Depth32Float, view.Format);
            Assert.Equal(40, view.Width);
            Assert.Equal(24, view.Height);
            Assert.Equal(string.Empty, view.Path);
            InvalidOperationException depthDispose = Assert.Throws<InvalidOperationException>(view.Dispose);
            Assert.Contains("RenderTexture", depthDispose.Message, StringComparison.Ordinal);
        } else {
            Assert.Null(target.DepthTexture);
        }

        Shader tintShader = harness.CreateShader(
            $"shaders/render-target-resize-description-tint-{format}-{depth}",
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });
        Mesh targetQuad = harness.CreateQuad(
            $"meshes/render-target-resize-description-quad-{format}-{depth}",
            halfWidth: target.Width / (float)target.Height,
            halfHeight: 1f);
        Color known = new(0.25f, 0.5f, 0.75f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        Shader colorShader = CreateSampleShader(harness, $"shaders/render-target-resize-description-sample-{format}-{depth}");
        (VulkanDrawTestHarness.PassSpec colorPass, Mesh _) = CreateSamplePass(
            harness,
            colorShader,
            colorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        harness.Submit(harness.CreateFrameWithPasses(
            1,
            CreateTargetDrawPass(target, targetQuad, tintShader, known, Color.Black),
            colorPass));
        (int centerX, int centerY) = SceneCenter(host);
        float tolerance = format == TextureFormat.Rgba16Float ? HDR_TOLERANCE : BYTE_TOLERANCE;
        AssertColorNear(known, harness.ReadColor((uint)centerX, (uint)centerY), tolerance);

        if (depth) {
            Shader depthShader = CreateDepthSampleShader(
                harness,
                $"shaders/render-target-resize-description-depth-{format}");
            (VulkanDrawTestHarness.PassSpec depthPass, Mesh _) = CreateSamplePass(
                harness,
                depthShader,
                target.DepthTexture!,
                sceneWidth,
                sceneHeight,
                ClearMode.ColorAndDepth,
                Color.Black);
            VulkanDrawTestHarness.CameraView targetCamera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
            float expectedDepth = ExpectedReverseZDepth(targetCamera);
            harness.Submit(harness.CreateFrameWithPasses(
                2,
                CreateTargetDrawPass(target, targetQuad, tintShader, Color.White, Color.Black),
                depthPass));
            AssertDepthNear(expectedDepth, harness.ReadColor((uint)centerX, (uint)centerY).R, DEPTH_TOLERANCE);
        }

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ADeviceRecoveryRecreatesAResizedTargetFromTheUpdatedDescription() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        using RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(32, 32));

        host.ResizeRenderTexture(target, 48, 24);
        Assert.Equal(48, target.Width);
        Assert.Equal(24, target.Height);
        Assert.Equal(48, target.ColorTexture.Width);
        Assert.Equal(24, target.ColorTexture.Height);

        host.Backend.SimulateDeviceLossForTests();
        // The next frame runs the recovery and is dropped; the live render texture is recreated from
        // the description the resize updated.
        harness.Submit(harness.CreateFrameWithPasses(1, CreateTargetClearPass(target, Color.Red)));
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        harness.RestoreAssets();

        Assert.Equal(48, target.Width);
        Assert.Equal(24, target.Height);
        Assert.Equal(48, target.ColorTexture.Width);
        Assert.Equal(24, target.ColorTexture.Height);
        Assert.True(host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? recovered));
        Assert.NotNull(recovered);
        Assert.Equal(48u, recovered.Extent.Width);
        Assert.Equal(24u, recovered.Extent.Height);
        Assert.Equal(target.SampleCount, recovered.SampleCount);

        Shader sampleShader = CreateSampleShader(harness, "shaders/render-target-resize-recovery-sample");
        Color green = new(0f, 1f, 0f, 1f);
        (int sceneWidth, int sceneHeight) = SceneSize(host);
        (VulkanDrawTestHarness.PassSpec samplePass, Mesh _) = CreateSamplePass(
            harness,
            sampleShader,
            target.ColorTexture,
            sceneWidth,
            sceneHeight,
            ClearMode.ColorAndDepth,
            Color.Black);
        harness.Submit(harness.CreateFrameWithPasses(
            2,
            CreateTargetClearPass(target, green),
            samplePass));
        (int centerX, int centerY) = SceneCenter(host);
        AssertColorNear(green, harness.ReadColor((uint)centerX, (uint)centerY), HDR_TOLERANCE);
        AssertColorNear(green, harness.ReadRenderTargetColor(target, 24, 12), HDR_TOLERANCE);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static Shader CreateSampleShader(VulkanDrawTestHarness harness, string path) =>
        harness.CreateShader(
            path,
            POSITION_VERTEX_SOURCE,
            SAMPLE_FRAGMENT_SOURCE,
            textures: ["mainTexture"],
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });

    private static Shader CreateDepthSampleShader(VulkanDrawTestHarness harness, string path) =>
        harness.CreateShader(
            path,
            POSITION_VERTEX_SOURCE,
            DEPTH_SAMPLE_FRAGMENT_SOURCE,
            textures: ["mainTexture"],
            defaultRenderState: RenderState.Opaque with { Cull = CullMode.None });

    private static VulkanDrawTestHarness.PassSpec CreateTargetClearPass(
        RenderTexture target,
        Color clearColor) {
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        return new VulkanDrawTestHarness.PassSpec(
            camera,
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clearColor,
            [],
            target.TargetId);
    }

    private static VulkanDrawTestHarness.PassSpec CreateTargetDrawPass(
        RenderTexture target,
        Mesh quad,
        Shader shader,
        Color tint,
        Color clearColor) =>
        CreateTargetDrawPass(
            target,
            quad,
            shader,
            tint,
            clearColor,
            Matrix4x4.Identity,
            ClearMode.ColorAndDepth);

    private static VulkanDrawTestHarness.PassSpec CreateTargetDrawPass(
        RenderTexture target,
        Mesh quad,
        Shader shader,
        Color tint,
        Color clearColor,
        Matrix4x4 world,
        ClearMode clearMode,
        int viewportX = 0,
        int viewportY = 0,
        int viewportWidth = 0,
        int viewportHeight = 0) {
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        VulkanDrawTestHarness.DrawSpec draw = new(
            quad,
            shader,
            [world],
            Tint: tint);
        return new VulkanDrawTestHarness.PassSpec(
            camera,
            viewportX,
            viewportY,
            viewportWidth == 0 ? target.Width : viewportWidth,
            viewportHeight == 0 ? target.Height : viewportHeight,
            clearMode,
            clearColor,
            [draw],
            target.TargetId);
    }

    private static VulkanDrawTestHarness.PassSpec CreateTargetPass(
        RenderTexture target,
        ClearMode clearMode,
        Color clearColor) {
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        return new VulkanDrawTestHarness.PassSpec(
            camera,
            0,
            0,
            target.Width,
            target.Height,
            clearMode,
            clearColor,
            [],
            target.TargetId);
    }


    private static (VulkanDrawTestHarness.PassSpec Pass, Mesh Quad) CreateSamplePass(
        VulkanDrawTestHarness harness,
        Shader shader,
        Texture texture,
        int width,
        int height,
        ClearMode clearMode,
        Color clearColor) {
        float worldHeight = 2f;
        int passWidth = width;
        int passHeight = height;
        if (passWidth == 0 || passHeight == 0) {
            throw new ArgumentOutOfRangeException(nameof(width), "The sample pass needs a positive viewport.");
        }

        float aspect = passWidth / (float)passHeight;
        Mesh quad = harness.CreateQuad(
            $"meshes/render-target-sample-{passWidth}x{passHeight}-{clearMode}",
            halfWidth: worldHeight * aspect * 0.5f,
            halfHeight: worldHeight * 0.5f);
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, passWidth, passHeight, worldHeight);
        VulkanDrawTestHarness.DrawSpec draw = new(
            quad,
            shader,
            [Matrix4x4.Identity],
            TextureBindings: [texture]);
        return (new VulkanDrawTestHarness.PassSpec(
            camera,
            0,
            0,
            passWidth,
            passHeight,
            clearMode,
            clearColor,
            [draw]), quad);
    }

    private static VulkanDrawTestHarness.CameraView CreateOrthoView(
        int x,
        int y,
        int width,
        int height,
        float worldHeight) {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = width / (float)height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(worldHeight, aspect, 0.1f, 10f);
        return new VulkanDrawTestHarness.CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            width,
            height);
    }

    private static (int Width, int Height) SceneSize(BackendTestHost host) => (
        checked((int)host.SceneTarget.Extent.Width),
        checked((int)host.SceneTarget.Extent.Height));

    private static (int X, int Y) SceneCenter(BackendTestHost host) => (
        checked((int)host.SceneTarget.Extent.Width) / 2,
        checked((int)host.SceneTarget.Extent.Height) / 2);

    private static float ExpectedReverseZDepth(VulkanDrawTestHarness.CameraView camera) =>
        ExpectedReverseZDepth(camera, Vector3.Zero);

    private static float ExpectedReverseZDepth(
        VulkanDrawTestHarness.CameraView camera,
        Vector3 worldPoint) {
        Vector4 clip = Vector4.Transform(new Vector4(worldPoint, 1f), camera.ViewProjection);
        return clip.Z / clip.W;
    }

    private static void AssertDepthNear(float expected, float actual, float tolerance) {
        Assert.True(
            MathF.Abs(expected - actual) <= tolerance,
            $"Depth {actual} is not within {tolerance} of {expected}.");
    }

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual, float tolerance) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= tolerance,
            $"Red {actual.R} is not within {tolerance} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= tolerance,
            $"Green {actual.G} is not within {tolerance} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= tolerance,
            $"Blue {actual.B} is not within {tolerance} of {expected.B}.");
    }
}
