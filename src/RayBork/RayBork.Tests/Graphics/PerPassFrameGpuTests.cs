using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU tests for the shader ABI v2 per-pass frame block: two camera passes in one frame select
/// different <c>RayBorkFrame</c> elements with the pass-index push constant.
/// </summary>
/// <remarks>
/// The test shader writes <c>rbFrame.CameraPosition.xyz</c> to its fragment output, so a single-pixel
/// readback proves which frame block each pass's draws sampled. The frame records two passes with
/// different camera positions and different viewports. With the pre-v2 shared frame block both passes
/// would read the last written pass's values and the first pixel assertion would fail.
/// </remarks>
public sealed class PerPassFrameGpuTests
{
    private const float COLOR_TOLERANCE = 2e-3f;

    private const string VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;

        void main_vertex() {
            gl_Position = rbFrame.ViewProjection * rbInstance.Transform * vec4(aPosition, 1.0);
        }
        """;

    private const string FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = vec4(rbFrame.CameraPosition.xyz, 1.0);
        }
        """;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TwoCameraPassesInOneFrameReadTheirOwnFrameBlock()
    {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        using VulkanDrawTestHarness harness = new(host);
        Shader shader = harness.CreateShader(
            "shaders/per-pass-frame",
            VERTEX_SOURCE,
            FRAGMENT_SOURCE,
            defaultRenderState: RenderState.Opaque with
            {
                Cull = CullMode.None,
            }
        );
        Mesh quad = harness.CreateQuad(halfWidth: 8f, halfHeight: 8f);
        VulkanDrawTestHarness.CameraView camera = VulkanDrawTestHarness.OrthographicCamera(host);
        int width = checked((int)host.SceneTarget.Extent.Width);
        int height = checked((int)host.SceneTarget.Extent.Height);
        int halfWidth = width / 2;
        Vector3 leftPosition = new(0.1f, 0.2f, 0.3f);
        Vector3 rightPosition = new(0.6f, 0.7f, 0.8f);
        VulkanDrawTestHarness.DrawSpec draw = VulkanDrawTestHarness.DrawSpec.Single(quad, shader);

        harness.Submit(
            harness.CreateFrameWithPasses(
                1,
                new VulkanDrawTestHarness.PassSpec(
                    camera with
                    {
                        Position = leftPosition,
                    },
                    0,
                    0,
                    halfWidth,
                    height,
                    ClearMode.ColorAndDepth,
                    new Color(0.05f, 0.1f, 0.2f, 1f),
                    [draw]
                ),
                new VulkanDrawTestHarness.PassSpec(
                    camera with
                    {
                        Position = rightPosition,
                    },
                    halfWidth,
                    0,
                    halfWidth,
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
        AssertColorNear(leftPosition, harness.ReadColor((uint)(width / 4), (uint)(height / 2)));
        AssertColorNear(
            rightPosition,
            harness.ReadColor((uint)((width * 3) / 4), (uint)(height / 2))
        );
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

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
