using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the per-camera scene render pass: one render pass per camera pass with a
/// viewport-sized render area, viewport-scoped clears, load/store between passes, and the resolved
/// color transition for the present pass.
/// </summary>
/// <remarks>
/// <para>
/// The tests read single pixels of the resolved scene color, and the depth test reads the
/// single-sample depth attachment, so they prove the attachment load/store and resolve semantics
/// rather than only the recorded draw statistics.
/// </para>
/// <para>
/// The test host requests core validation, not synchronization validation, so these tests prove the
/// rendered result and validation-layer cleanliness; they do not prove the access masks against the
/// validation layer's synchronization model. That model is a later hardening task.
/// </para>
/// </remarks>
public sealed class CameraRenderPassGpuTests
{
    private const float COLOR_TOLERANCE = 2e-3f;
    private const float DEPTH_TOLERANCE = 2e-3f;
    private const string MULTISAMPLE_REQUIRED =
        "The test needs a multisampled scene target to exercise the resolve path.";

    private const string POSITION_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;

        void main_vertex() {
            gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(aPosition, 1.0);
        }
        """;

    private const string CAMERA_POSITION_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = vec4(rbFrame.CameraPosition.xyz, 1.0);
        }
        """;

    private const string TINT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = rbInstance.Color;
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoCameraPassesRenderSplitScreenViewsInTheirOwnViewports()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Assert.True(host.SceneTarget.SampleCount > 1, MULTISAMPLE_REQUIRED);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = harness.CreateShader(
            "shaders/camera-split-screen",
            POSITION_VERTEX_SOURCE,
            CAMERA_POSITION_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        Mesh quad = harness.CreateQuad(halfWidth: 8f, halfHeight: 8f);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = width / 2;
        int rightWidth = width - leftWidth;
        Vector3 leftPosition = new(0.1f, 0.2f, 0.3f);
        Vector3 rightPosition = new(0.6f, 0.7f, 0.8f);
        VulkanDrawTestHarness.CameraView leftCamera = CreateCameraView(
            leftPosition,
            CameraProjection.Orthographic,
            fieldOfView: 0f,
            orthographicHeight: 2f,
            leftWidth,
            height
        );
        VulkanDrawTestHarness.CameraView rightCamera = CreateCameraView(
            rightPosition,
            CameraProjection.Perspective,
            fieldOfView: 60f,
            orthographicHeight: 0f,
            rightWidth,
            height
        );
        VulkanDrawTestHarness.DrawSpec draw = VulkanDrawTestHarness.DrawSpec.Single(quad, shader);

        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    leftCamera,
                    0,
                    0,
                    leftWidth,
                    height,
                    ClearMode.ColorAndDepth,
                    new Color(0.05f, 0.1f, 0.2f, 1f),
                    [draw]
                ),
                new VulkanDrawTestHarness.PassSpec(
                    rightCamera,
                    leftWidth,
                    0,
                    rightWidth,
                    height,
                    ClearMode.ColorAndDepth,
                    new Color(0.2f, 0.1f, 0.05f, 1f),
                    [draw]
                )
            )
        );

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(2, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        AssertColorNear(leftPosition, harness.ReadColor((uint)(leftWidth / 2), (uint)(height / 2)));
        AssertColorNear(
            rightPosition,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)(height / 2))
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void LaterCameraWithClearModeNoneLoadsTheEarlierPassColor()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Assert.True(host.SceneTarget.SampleCount > 1, MULTISAMPLE_REQUIRED);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTintShader(harness, "shaders/camera-load");
        Mesh quad = harness.CreateQuad(halfWidth: 0.2f, halfHeight: 0.2f);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);

        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    ClearMode.ColorAndDepth,
                    red,
                    []
                ),
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    ClearMode.None,
                    Color.Black,
                    [
                        new VulkanDrawTestHarness.DrawSpec(
                            quad,
                            shader,
                            [Matrix4x4.Identity],
                            Tint: green
                        ),
                    ]
                )
            )
        );

        AssertColorNear(green, harness.ReadColor((uint)(width / 2), (uint)(height / 2)));
        AssertColorNear(red, harness.ReadColor((uint)(width / 8), (uint)(height / 2)));
        Assert.Equal(1, host.Backend.Stats.DrawCalls);
        Assert.Equal(1, host.Backend.Stats.Instances);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void LaterCameraDepthClearReplacesTheEarlierDepthAndGeometry()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(
            sampleCount: 1,
            hdr: WindowHdrState.Sdr
        );
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTintShader(harness, "shaders/camera-depth");
        Mesh quad = harness.CreateQuad(halfWidth: 0.5f, halfHeight: 0.5f);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        uint centerX = (uint)(width / 2);
        uint centerY = (uint)(height / 2);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        Color background = new(0.25f, 0.25f, 0.25f, 1f);
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        VulkanDrawTestHarness.DrawSpec nearRed = new(quad, shader, [Matrix4x4.Identity], Tint: red);
        VulkanDrawTestHarness.DrawSpec farGreen = new(
            quad,
            shader,
            [Matrix4x4.CreateTranslation(0f, 0f, -1f)],
            Tint: green
        );

        // The second camera clears its viewport's depth, so the farther green quad passes the
        // reverse-Z test and wins over the earlier red quad.
        SubmitDepthComparisonFrame(
            harness,
            camera,
            width,
            height,
            background,
            nearRed,
            farGreen,
            ClearMode.Depth,
            frameIndex: 1
        );
        AssertColorNear(green, harness.ReadColor(centerX, centerY));
        Assert.True(
            MathF.Abs(harness.ReadDepth(centerX, centerY) - (8f / 9.9f)) <= DEPTH_TOLERANCE,
            "The second camera's depth clear did not store the farther green quad's depth."
        );

        // Without the depth clear the second camera loads the earlier depth, so the farther green
        // quad fails the reverse-Z test and the earlier red quad stays visible.
        SubmitDepthComparisonFrame(
            harness,
            camera,
            width,
            height,
            background,
            nearRed,
            farGreen,
            ClearMode.None,
            frameIndex: 2
        );
        AssertColorNear(red, harness.ReadColor(centerX, centerY));
        Assert.True(
            MathF.Abs(harness.ReadDepth(centerX, centerY) - (9f / 9.9f)) <= DEPTH_TOLERANCE,
            "The later camera's loaded depth did not keep the nearer red quad's depth."
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void MultisampledDepthClearAndLoadFollowTheLaterCameraClearMode()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Assert.True(host.SceneTarget.SampleCount > 1, MULTISAMPLE_REQUIRED);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = CreateTintShader(harness, "shaders/camera-depth-multisampled");
        Mesh quad = harness.CreateQuad(halfWidth: 0.5f, halfHeight: 0.5f);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        uint centerX = (uint)(width / 2);
        uint centerY = (uint)(height / 2);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        Color background = new(0.25f, 0.25f, 0.25f, 1f);
        Color red = new(1f, 0f, 0f, 1f);
        Color green = new(0f, 1f, 0f, 1f);
        VulkanDrawTestHarness.DrawSpec nearRed = new(quad, shader, [Matrix4x4.Identity], Tint: red);
        VulkanDrawTestHarness.DrawSpec farGreen = new(
            quad,
            shader,
            [Matrix4x4.CreateTranslation(0f, 0f, -1f)],
            Tint: green
        );

        // The multisampled depth attachment is loaded and stored between the two render passes; a
        // depth clear lets the farther green quad win, while ClearMode.None keeps the earlier depth
        // and the nearer red quad stays visible.
        SubmitDepthComparisonFrame(
            harness,
            camera,
            width,
            height,
            background,
            nearRed,
            farGreen,
            ClearMode.Depth,
            frameIndex: 1
        );
        AssertColorNear(green, harness.ReadColor(centerX, centerY));
        SubmitDepthComparisonFrame(
            harness,
            camera,
            width,
            height,
            background,
            nearRed,
            farGreen,
            ClearMode.None,
            frameIndex: 2
        );
        AssertColorNear(red, harness.ReadColor(centerX, centerY));
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FrameWithoutCameraPassesPresentsWithZeroSceneStats()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        packet.Seal();

        harness.Submit(packet);

        RenderStats stats = host.Backend.Stats;
        Assert.Equal(0, stats.DrawCalls);
        Assert.Equal(0, stats.Instances);
        Assert.Equal(0, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EachCameraClearOnlyTouchesItsOwnViewport()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Assert.True(host.SceneTarget.SampleCount > 1, MULTISAMPLE_REQUIRED);
        using VulkanDrawTestHarness harness = new(host);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        int leftWidth = width / 2;
        int rightWidth = width - leftWidth;
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        Color leftRed = new(1f, 0f, 0f, 1f);
        Color rightBlue = new(0f, 0f, 1f, 1f);

        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    leftWidth,
                    height,
                    ClearMode.ColorAndDepth,
                    leftRed,
                    []
                ),
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    leftWidth,
                    0,
                    rightWidth,
                    height,
                    ClearMode.ColorAndDepth,
                    rightBlue,
                    []
                )
            )
        );

        AssertColorNear(leftRed, harness.ReadColor((uint)(leftWidth / 2), (uint)(height / 2)));
        AssertColorNear(
            rightBlue,
            harness.ReadColor((uint)(leftWidth + (rightWidth / 2)), (uint)(height / 2))
        );
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FirstCameraWithClearModeNoneDoesNotFailValidation()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);

        // The frame-start transition discards the target, so a first camera with ClearMode.None
        // loads undefined content. It must still record and pass validation without a clear.
        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    ClearMode.None,
                    Color.Black,
                    []
                )
            )
        );

        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void ClampedViewportScopesTheRenderAreaAndClear()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        int clampedWidth = (width * 3) / 4;
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        Color background = new(0.25f, 0.25f, 0.25f, 1f);
        Color red = new(1f, 0f, 0f, 1f);

        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    ClearMode.ColorAndDepth,
                    background,
                    []
                ),
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    -width / 4,
                    -height / 4,
                    (width * 3) / 4,
                    height + (height / 4),
                    ClearMode.ColorAndDepth,
                    red,
                    []
                )
            )
        );

        // The negative offsets clamp to zero and the oversized extent clamps to the target, so the
        // second camera clears only the left three quarters and the earlier background stays.
        AssertColorNear(red, harness.ReadColor((uint)(clampedWidth / 2), (uint)(height / 2)));
        AssertColorNear(
            background,
            harness.ReadColor(
                (uint)(clampedWidth + ((width - clampedWidth) / 2)),
                (uint)(height / 2)
            )
        );
        Assert.Equal(0, host.Backend.Stats.DrawCalls);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static Shader CreateTintShader(VulkanDrawTestHarness harness, string path) =>
        harness.CreateShader(
            path,
            POSITION_VERTEX_SOURCE,
            TINT_FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );

    private static void SubmitDepthComparisonFrame(
        VulkanDrawTestHarness harness,
        VulkanDrawTestHarness.CameraView camera,
        int width,
        int height,
        Color background,
        VulkanDrawTestHarness.DrawSpec nearRed,
        VulkanDrawTestHarness.DrawSpec farGreen,
        ClearMode laterClearMode,
        long frameIndex
    )
    {
        harness.Submit(
            harness.CreateFrameWithPasses(
                frameIndex,
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    ClearMode.ColorAndDepth,
                    background,
                    [nearRed]
                ),
                new VulkanDrawTestHarness.PassSpec(
                    camera,
                    0,
                    0,
                    width,
                    height,
                    laterClearMode,
                    Color.Black,
                    [farGreen]
                )
            )
        );
    }

    private static VulkanDrawTestHarness.CameraView CreateCameraView(
        Vector3 position,
        CameraProjection projection,
        float fieldOfView,
        float orthographicHeight,
        int viewportWidth,
        int viewportHeight
    )
    {
        Transform transform = new(position);
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = viewportWidth / (float)viewportHeight;
        Matrix4x4 projectionMatrix =
            projection == CameraProjection.Perspective
                ? CameraMath.CreatePerspectiveProjection(
                    fieldOfView,
                    aspect,
                    0.01f,
                    float.PositiveInfinity
                )
                : CameraMath.CreateOrthographicProjection(orthographicHeight, aspect, 0.1f, 10f);
        return new VulkanDrawTestHarness.CameraView(
            view,
            projectionMatrix,
            view * projectionMatrix,
            position,
            viewportWidth,
            viewportHeight
        );
    }

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual) =>
        AssertColorNear(new Vector3(expected.R, expected.G, expected.B), actual);

    private static void AssertColorNear(Vector3 expected, (float R, float G, float B) actual)
    {
        Assert.True(
            MathF.Abs(expected.X - actual.R) <= COLOR_TOLERANCE,
            $"Red {actual.R} is not within {COLOR_TOLERANCE} of {expected.X}."
        );
        Assert.True(
            MathF.Abs(expected.Y - actual.G) <= COLOR_TOLERANCE,
            $"Green {actual.G} is not within {COLOR_TOLERANCE} of {expected.Y}."
        );
        Assert.True(
            MathF.Abs(expected.Z - actual.B) <= COLOR_TOLERANCE,
            $"Blue {actual.B} is not within {COLOR_TOLERANCE} of {expected.Z}."
        );
    }
}
