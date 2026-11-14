using System.Numerics;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>
/// Numeric presented-pixel probes for the AgX present path: the SDR image at the default white and
/// contrast and under the runtime exposure and AgX snapshots, neutral gray under scRGB HDR at the
/// default settings and a custom white and contrast, paper white and the display peak at two
/// headrooms, the frame's tone-mapping snapshot, and the direct <see cref="TonemappingMode.None"/>
/// path.
/// </summary>
/// <remarks>
/// The probes read the swapchain image the backend presented, so they assert the complete present
/// path, not only the CPU reference. Every runtime-setting probe sets the frame's
/// <see cref="RenderFramePacket.Tonemapping"/> snapshot, which is the path the tick thread seals into
/// the packet. The HDR cases need the scRGB half-float swapchain path; when the host surface cannot
/// advertise it the test skips with the actual selected format.
/// </remarks>
[Collection("SDL")]
public sealed class HdrPresentGpuTests {
    private const float PAPER_WHITE = 2f;
    private const float PEAK_HEADROOM = 6.25f;
    private const float PEAK = PAPER_WHITE * PEAK_HEADROOM;
    // The shader evaluates the curve in 32-bit floats against the double CPU reference. The
    // R16G16B16A16_SFLOAT swapchain readback quantizes the result again, so the SDR path is stable to
    // 1e-3 and the scRGB path to 2e-3 (one half-float step at the two-unit paper-white range).
    private const float SDR_FLOAT_PATH_TOLERANCE = 1e-3f;
    private const float HDR_FLOAT_PATH_TOLERANCE = 2e-3f;
    private static readonly WindowHdrState _hdr = new(true, PAPER_WHITE, PEAK_HEADROOM);
    private static readonly WindowHdrState _hdrHeadroomTwo = new(true, PAPER_WHITE, 2f);

    private readonly ITestOutputHelper _output;

    public HdrPresentGpuTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrNeutralMatchesTheCpuReference() {
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
            $"HDR neutral 1: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        Assert.Equal(expected.X, presented.R, expected.X * 0.01);
        Assert.Equal(expected.Y, presented.G, expected.Y * 0.01);
        Assert.Equal(expected.Z, presented.B, expected.Z * 0.01);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrMidScenePresentsNearPaperWhite() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);

        (float R, float G, float B) presented = PresentAndProbe(host, new Color(1f, 1f, 1f, 1f));
        double luminance = Luminance(presented);
        _output.WriteLine(FormattableString.Invariant(
            $"HDR scene 1.0: rgb=({presented.R}, {presented.G}, {presented.B}) luminance={luminance}"));

        // Scene 1.0 is near paper white in paper-white units (about 0.92 of it).
        Assert.InRange(luminance, PAPER_WHITE * 0.85, PAPER_WHITE * 1.05);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrAgxWhiteReachesTheReportedPeak() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);
        float scene = AgxToneMap.DEFAULT_AGX_WHITE * PEAK_HEADROOM;

        (float R, float G, float B) presented = PresentAndProbe(
            host,
            new Color(scene, scene, scene, 1f));
        double luminance = Luminance(presented);
        _output.WriteLine(FormattableString.Invariant(
            $"HDR AgX white scene {scene}: rgb=({presented.R}, {presented.G}, {presented.B}) luminance={luminance}"));

        Assert.Equal(PEAK, luminance, PEAK * 0.01);
        Assert.True(luminance > PAPER_WHITE, $"The peak probe stayed at or below paper white ({luminance}).");
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrHeadroomTwoReachesItsOwnPeak() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdrHeadroomTwo);
        RequireScRgbHdr(host.Backend.Info);
        float scene = AgxToneMap.DEFAULT_AGX_WHITE * 2f;

        (float R, float G, float B) presented = PresentAndProbe(
            host,
            new Color(scene, scene, scene, 1f));
        double luminance = Luminance(presented);
        _output.WriteLine(FormattableString.Invariant(
            $"HDR headroom 2 AgX white scene {scene}: rgb=({presented.R}, {presented.G}, {presented.B}) luminance={luminance}"));

        // The headroom is the curve output maximum, so the peak is paper white * 2.
        Assert.Equal(PAPER_WHITE * 2f, luminance, PAPER_WHITE * 2f * 0.01);
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
    public void SdrWindowPresentsTheAgxDefaultImage() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        Vector3 expected = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);

        (float R, float G, float B) presented = PresentAndProbe(host, new Color(1f, 1f, 1f, 1f));
        _output.WriteLine(FormattableString.Invariant(
            $"SDR {host.Backend.Swapchain.Format}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void FrameSnapshotDrivesThePresentPass() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const float exposure = 1f;
        const float agxWhite = 4f;
        const float agxContrast = 2f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(1f, 1f, 1f, 1f));
        packet.Tonemapping = new TonemappingSnapshot(exposure, agxWhite, agxContrast);

        Vector3 expected = AgxToneMap.Present(
            Vector3.One,
            MathF.Pow(2f, exposure),
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr,
            agxWhite: agxWhite,
            agxContrast: agxContrast);
        Vector3 startupDefaults = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Assert.True(
            MathF.Abs(expected.X - startupDefaults.X) > 0.05f,
            "The frame snapshot must differ from the startup render settings for this probe to prove "
                + "that the backend reads the packet.");

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"Frame snapshot exposure={exposure} white={agxWhite} contrast={agxContrast}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SdrDefaultSnapshotPresentsMidGray() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const float scene = 0.5f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(scene, scene, scene, 1f));
        packet.Tonemapping = TonemappingSnapshot.Default;

        Vector3 expected = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"SDR default snapshot scene={scene}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SdrExposureSnapshotRaisesThePresentedValue() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const float exposure = 1f;
        const float scene = 0.5f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(scene, scene, scene, 1f));
        packet.Tonemapping = new TonemappingSnapshot(
            exposure,
            AgxToneMap.DEFAULT_AGX_WHITE,
            AgxToneMap.DEFAULT_AGX_CONTRAST);

        Vector3 expected = AgxToneMap.Present(
            new Vector3(scene),
            MathF.Pow(2f, exposure),
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Vector3 unexposed = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Assert.True(
            MathF.Abs(expected.X - unexposed.X) > 0.1f,
            "The exposure probe must differ from the neutral exposure for the readback to prove that "
                + "the packet snapshot reached the present pass.");

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"SDR snapshot exposure={exposure} scene={scene}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SdrContrastSnapshotChangesThePresentedCurve() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const float agxContrast = 2f;
        const float scene = 0.5f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(scene, scene, scene, 1f));
        packet.Tonemapping = new TonemappingSnapshot(
            0f,
            AgxToneMap.DEFAULT_AGX_WHITE,
            agxContrast);

        Vector3 expected = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr,
            agxContrast: agxContrast);
        Vector3 defaultContrast = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Assert.True(
            MathF.Abs(expected.X - defaultContrast.X) > 0.05f,
            "The contrast probe must differ from the default contrast for the readback to prove that "
                + "the packet snapshot reached the present pass.");

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"SDR snapshot contrast={agxContrast} scene={scene}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void SdrAgxWhiteSnapshotChangesThePresentedCurve() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: WindowHdrState.Sdr);
        const float agxWhite = 2f;
        const float scene = 1f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(scene, scene, scene, 1f));
        packet.Tonemapping = new TonemappingSnapshot(
            0f,
            agxWhite,
            AgxToneMap.DEFAULT_AGX_CONTRAST);

        Vector3 expected = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr,
            agxWhite: agxWhite);
        Vector3 defaultWhite = AgxToneMap.Present(
            new Vector3(scene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Assert.True(
            MathF.Abs(expected.X - defaultWhite.X) > 0.05f,
            "The white probe must differ from the default AgX white for the readback to prove that "
                + "the packet snapshot reached the present pass.");

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"SDR snapshot white={agxWhite} scene={scene}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertSdrPresentationMatches(expected, presented, host.Backend.Swapchain.Format);
        Assert.Empty(host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void HdrSnapshotWithCustomWhiteAndContrastPresentsTheCpuReference() {
        using BackendTestHost host = BackendTestHost.CreateOrSkip(hdr: _hdr);
        RequireScRgbHdr(host.Backend.Info);
        const float agxWhite = 8f;
        const float agxContrast = 1.6f;
        RenderFramePacket packet = BackendPacketTestHelper.CreateClearingPacket(
            host,
            1,
            new Color(1f, 1f, 1f, 1f));
        packet.Tonemapping = new TonemappingSnapshot(0f, agxWhite, agxContrast);

        Vector3 expected = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr,
            agxWhite: agxWhite,
            agxContrast: agxContrast);
        Vector3 defaultSettings = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);
        Assert.True(
            MathF.Abs(expected.X - defaultSettings.X) > 0.1f,
            "The HDR probe must differ from the default AgX white and contrast for the readback to "
                + "prove that the packet snapshot reached the present pass.");

        (float R, float G, float B) presented = PresentAndProbe(host.Backend, packet);
        _output.WriteLine(FormattableString.Invariant(
            $"HDR snapshot white={agxWhite} contrast={agxContrast}: presented=({presented.R}, {presented.G}, {presented.B}) expected=({expected.X}, {expected.Y}, {expected.Z})"));

        AssertHdrPresentationMatches(expected, presented);

        // The custom white is the scene value the curve maps to the display peak.
        float peakScene = agxWhite * PEAK_HEADROOM;
        RenderFramePacket peakPacket = BackendPacketTestHelper.CreateClearingPacket(
            host,
            2,
            new Color(peakScene, peakScene, peakScene, 1f));
        peakPacket.Tonemapping = packet.Tonemapping;
        Vector3 expectedPeak = AgxToneMap.Present(
            new Vector3(peakScene),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr,
            agxWhite: agxWhite,
            agxContrast: agxContrast);

        (float R, float G, float B) peak = PresentAndProbe(host.Backend, peakPacket);
        _output.WriteLine(FormattableString.Invariant(
            $"HDR snapshot white={agxWhite} peak scene={peakScene}: presented=({peak.R}, {peak.G}, {peak.B}) expected=({expectedPeak.X}, {expectedPeak.Y}, {expectedPeak.Z})"));

        AssertHdrPresentationMatches(expectedPeak, peak);
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

    private static void AssertSdrPresentationMatches(
        Vector3 expected,
        (float R, float G, float B) presented,
        Format format) {
        if (IsEightBit(format)) {
            AssertEightBitSrgbNear(expected.X, presented.R);
            AssertEightBitSrgbNear(expected.Y, presented.G);
            AssertEightBitSrgbNear(expected.Z, presented.B);
        } else {
            Assert.Equal(expected.X, presented.R, SDR_FLOAT_PATH_TOLERANCE);
            Assert.Equal(expected.Y, presented.G, SDR_FLOAT_PATH_TOLERANCE);
            Assert.Equal(expected.Z, presented.B, SDR_FLOAT_PATH_TOLERANCE);
        }
    }

    private static void AssertHdrPresentationMatches(Vector3 expected, (float R, float G, float B) presented) {
        Assert.Equal(expected.X, presented.R, HDR_FLOAT_PATH_TOLERANCE);
        Assert.Equal(expected.Y, presented.G, HDR_FLOAT_PATH_TOLERANCE);
        Assert.Equal(expected.Z, presented.B, HDR_FLOAT_PATH_TOLERANCE);
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
