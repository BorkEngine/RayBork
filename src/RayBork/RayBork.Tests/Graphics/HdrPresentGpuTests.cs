using System.Numerics;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// Numeric presented-pixel probes for the M4 AgX HDR highlight extension: paper white at the AgX
/// knee, the reported peak above the knee, the normalized AgX response below the knee, the direct
/// <see cref="TonemappingMode.None"/> path, and the unchanged M2 image on an SDR window.
/// </summary>
/// <remarks>
/// The probes read the swapchain image the backend presented, so they assert the complete present
/// path, not only the CPU reference. The HDR cases need the scRGB half-float swapchain path; when the
/// host surface cannot advertise it the test skips with the actual selected format.
/// </remarks>
[Collection("SDL")]
public sealed class HdrPresentGpuTests {
    private const float PAPER_WHITE = 2f;
    private const float PEAK = 12.5f;
    private static readonly WindowHdrState _hdr = new(true, PAPER_WHITE, PEAK / PAPER_WHITE);

    private readonly ITestOutputHelper _output;

    public HdrPresentGpuTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AgxWhiteKneePresentsAtPaperWhite() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);
        float knee = (float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE;

        (float R, float G, float B) presented = PresentAndProbe(
            host,
            new Color(knee, knee, knee, 1f));
        _output.WriteLine(FormattableString.Invariant(
            $"AgX knee {knee}: rgb=({presented.R}, {presented.G}, {presented.B}) luminance={Luminance(presented)}"));

        Assert.Equal(PAPER_WHITE, Luminance(presented), PAPER_WHITE * 0.01);
        Vector3 expected = AgxToneMap.Agx(new Vector3(knee)) / (float)AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE;
        AssertChannelRatios(expected, presented);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void AboveWhiteSceneReachesTheReportedPeak() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);
        float scene = (float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE * (PEAK / PAPER_WHITE);

        (float R, float G, float B) presented = PresentAndProbe(
            host,
            new Color(scene, scene, scene, 1f));
        _output.WriteLine(FormattableString.Invariant(
            $"Above-white scene {scene}: rgb=({presented.R}, {presented.G}, {presented.B}) luminance={Luminance(presented)}"));

        double luminance = Luminance(presented);
        Assert.Equal(PEAK, luminance, PEAK * 0.01);
        Assert.True(luminance > PAPER_WHITE, $"The peak probe stayed at or below paper white ({luminance}).");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrNeutralBelowTheKneeMatchesTheCpuReference() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);

        (float R, float G, float B) presented = PresentAndProbe(host, new Color(1f, 1f, 1f, 1f));
        Vector3 expected = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);
        _output.WriteLine(FormattableString.Invariant(
            $"Neutral 1: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        Assert.Equal(expected.X, presented.R, expected.X * 0.01);
        Assert.Equal(expected.Y, presented.G, expected.Y * 0.01);
        Assert.Equal(expected.Z, presented.B, expected.Z * 0.01);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TonemappingNoneWritesTheDirectHdrValue() {
        using HdrPresentTestHost host = HdrPresentTestHost.CreateOrSkip(TonemappingMode.None, _hdr);
        RequireScRgbHdr(host.Backend.Info);
        Extent2D extent = host.Backend.SceneTarget.Extent;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            frameIndex: 1,
            clearColor: new Color(4f, 2f, 0.5f, 1f),
            width: checked((int)extent.Width),
            height: checked((int)extent.Height));

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"None tone mapping: rgb=({presented.R}, {presented.G}, {presented.B})"));

        Assert.Equal(8f, presented.R, 8f * 0.01);
        Assert.Equal(4f, presented.G, 4f * 0.01);
        Assert.Equal(1f, presented.B, 1f * 0.01);
        Assert.True(presented.R > PAPER_WHITE, "Direct HDR authoring must not be tone-mapped to paper white.");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SdrWindowKeepsTheM2AgxImage() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Vector3 expected = Vector3.Clamp(AgxToneMap.Agx(Vector3.One), Vector3.Zero, Vector3.One);

        (float R, float G, float B) presented = PresentAndProbe(host, new Color(1f, 1f, 1f, 1f));
        _output.WriteLine(FormattableString.Invariant(
            $"SDR {host.Backend.Swapchain.Format}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        if (IsEightBit(host.Backend.Swapchain.Format)) {
            AssertEightBitSrgbNear(expected.X, presented.R);
            AssertEightBitSrgbNear(expected.Y, presented.G);
            AssertEightBitSrgbNear(expected.Z, presented.B);
        } else {
            Assert.Equal(expected.X, presented.R, 1e-3);
            Assert.Equal(expected.Y, presented.G, 1e-3);
            Assert.Equal(expected.Z, presented.B, 1e-3);
        }

        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    private static (float R, float G, float B) PresentAndProbe(BackendTestHost host, Color clearColor) =>
        PresentAndProbe(host.Backend, BackendPacketTestHelper.CreateClearingPacket(host, 1, clearColor));

    private static (float R, float G, float B) PresentAndProbe(Vulkan12Backend backend, RenderFramePacket packet) {
        if (!backend.Swapchain.SupportsPresentReadback) {
            Assert.Skip(
                "The swapchain images were not created with VK_IMAGE_USAGE_TRANSFER_SRC_BIT, so " +
                "the presented-pixel probes cannot read the image this surface presented.");
        }

        backend.Submit(packet);
        VulkanSwapchain swapchain = backend.Swapchain;
        uint imageIndex = backend.PendingPresentImageIndex;
        Extent2D extent = swapchain.Extent;
        VulkanReadback.ReadPresentedColorPixel(
            backend.Device,
            swapchain,
            imageIndex,
            extent.Width / 2,
            extent.Height / 2,
            out float r,
            out float g,
            out float b);
        backend.Present();
        Assert.Equal(imageIndex, backend.LastPresentedImageIndex);
        return (r, g, b);
    }

    private static void RequireScRgbHdr(EngineInfo info) {
        if (!info.HdrOutput) {
            Assert.Skip(
                $"The window surface selected {info.SwapchainFormat} instead of the scRGB " +
                "R16G16B16A16_SFLOAT pair, so the HDR present probes cannot run.");
        }
    }

    private static double Luminance((float R, float G, float B) color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static void AssertChannelRatios(Vector3 expected, (float R, float G, float B) actual) {
        double expectedSum = expected.X + expected.Y + expected.Z;
        double actualSum = actual.R + actual.G + actual.B;
        Assert.Equal(expected.X / expectedSum, actual.R / actualSum, (expected.X / expectedSum) * 0.01);
        Assert.Equal(expected.Y / expectedSum, actual.G / actualSum, (expected.Y / expectedSum) * 0.01);
        Assert.Equal(expected.Z / expectedSum, actual.B / actualSum, (expected.Z / expectedSum) * 0.01);
    }

    private static bool IsEightBit(Format format) =>
        format is Format.B8G8R8A8Srgb
            or Format.B8G8R8A8Unorm
            or Format.R8G8B8A8Srgb
            or Format.R8G8B8A8Unorm;

    private static void AssertEightBitSrgbNear(float expected, float actual) {
        float tolerance = OneByteSrgbStep(expected);
        Assert.True(
            MathF.Abs(actual - expected) <= tolerance,
            $"The decoded channel {actual} is more than one 8-bit sRGB step ({tolerance}) from the expected {expected}.");
    }

    private static float OneByteSrgbStep(float linear) {
        float encoded = AgxToneMap.SrgbEncodeChannel(linear);
        int byteValue = (int)MathF.Round(Math.Clamp(encoded, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
        float lower = SrgbDecode(Math.Max(0, byteValue - 1) / 255f);
        float upper = SrgbDecode(Math.Min(255, byteValue + 1) / 255f);
        float center = SrgbDecode(byteValue / 255f);
        return MathF.Max(center - lower, upper - center);
    }

    private static float SrgbDecode(float encoded) =>
        encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}
