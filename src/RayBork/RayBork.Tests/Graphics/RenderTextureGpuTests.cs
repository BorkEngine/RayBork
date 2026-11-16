using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the public render texture color path: a camera routing into a runtime target, the
/// resolved color view sampled by a later camera, the per-target image barriers, the render target
/// formats and sample counts, and the target lifetime.
/// </summary>
/// <remarks>
/// The tests read the scene target and the render target's resolved color through the diagnostic
/// readback probes and assert numeric tolerances. They also assert validation-layer cleanliness. The
/// test host requests core validation, not synchronization validation, so the access masks are proven
/// by the rendered result and the clean core validation, not by the synchronization model.
/// </remarks>
public sealed class RenderTextureGpuTests {
    private const float HDR_TOLERANCE = 3e-3f;
    private const float BYTE_TOLERANCE = 6e-3f;

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
            using Camera camera = cameras.CreateCamera(
                Transform.Identity,
                CameraProjection.Perspective,
                60f,
                0f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null,
                target);
            InvalidOperationException meshException = Assert.Throws<InvalidOperationException>(
                () => camera.DrawMesh(quad, Transform.Identity, sampleShader));
            Assert.Contains("same pass", meshException.Message, StringComparison.Ordinal);

            // DrawModel records through the part material path; it must run the same check.
            InvalidOperationException modelException = Assert.Throws<InvalidOperationException>(
                () => camera.DrawModel(model, Transform.Identity));
            Assert.Contains("same pass", modelException.Message, StringComparison.Ordinal);
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
        Assert.Throws<InvalidOperationException>(target.ColorTexture.Dispose);
        Assert.False(target.ColorTexture.IsDisposed);

        RenderFramePacket packet = harness.CreateFrameWithPasses(
            1,
            CreateTargetClearPass(target, known),
            samplePass);
        // Model the frame ring: the recorded frame holds the target's view until the renderer released
        // the slot, so disposing the target before submission must not destroy it.
        packet.Retention.Add(target.ColorTexture.RetentionTarget);
        target.Dispose();
        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);

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
    public void BackendShutdownDisposesLiveRenderTextures() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        RenderTexture target = host.CreateRenderTexture(new RenderTextureDescription(16, 16));

        host.Backend.Dispose();

        Assert.True(target.IsDisposed);
        Assert.True(target.ColorTexture.IsDisposed);
        target.Dispose();
        Assert.True(target.IsDisposed);
    }

    private static Shader CreateSampleShader(VulkanDrawTestHarness harness, string path) =>
        harness.CreateShader(
            path,
            POSITION_VERTEX_SOURCE,
            SAMPLE_FRAGMENT_SOURCE,
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
        Color clearColor) {
        VulkanDrawTestHarness.CameraView camera = CreateOrthoView(0, 0, target.Width, target.Height, 2f);
        VulkanDrawTestHarness.DrawSpec draw = new(
            quad,
            shader,
            [Matrix4x4.Identity],
            Tint: tint);
        return new VulkanDrawTestHarness.PassSpec(
            camera,
            0,
            0,
            target.Width,
            target.Height,
            ClearMode.ColorAndDepth,
            clearColor,
            [draw],
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
