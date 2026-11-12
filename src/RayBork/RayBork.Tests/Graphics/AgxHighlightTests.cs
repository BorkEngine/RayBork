using System.Globalization;
using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class AgxHighlightTests {
    private const string FRAGMENT_SOURCE_FILE = "Present.frag.glsl";
    private const float PAPER_WHITE = 2f;
    private const float PEAK = 12.5f;
    private static readonly WindowHdrState _hdr = new(true, PAPER_WHITE, PEAK / PAPER_WHITE);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AgxNeutralColorMatchesThePinnedReference() {
        Vector3 tone = AgxToneMap.Agx(Vector3.One);

        Assert.Equal(0.858491866854052, tone.X, 1e-6);
        Assert.Equal(0.7520998705053822, tone.Y, 1e-6);
        Assert.Equal(0.7499293214016827, tone.Z, 1e-6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AgxConstantsMatchTheShaderPins() {
        Assert.Equal(-12.47393, AgxToneMap.AGX_MIN_EV);
        Assert.Equal(4.026069, AgxToneMap.AGX_MAX_EV);
        Assert.Equal(16.291742365295857, AgxToneMap.AGX_WHITE_SCENE_LUMINANCE);
        Assert.Equal(0.9829795037669735, AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE);
        Assert.Equal(AgxToneMap.AGX_WHITE_SCENE_LUMINANCE, Math.Pow(2.0, AgxToneMap.AGX_MAX_EV), 12);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AgxOutputWhiteLuminanceIsTheNeutralWhiteResponse() {
        Vector3 white = AgxToneMap.Agx(new Vector3(1e30f));
        double luminance = AgxToneMap.Luminance(white);

        // The pinned constant is the double-precision response; the Vector3 result is rounded to
        // float, so allow the last two digits of the luminance.
        Assert.Equal(AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE, luminance, 7);
        Assert.Equal(1.08946128, white.X, 1e-6);
        Assert.Equal(0.95448151, white.Y, 1e-6);
        Assert.Equal(0.95172937, white.Z, 1e-6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ShaderDeclaresTheCpuOutputWhiteConstant() {
        string source = File.ReadAllText(
            RepositoryLayout.PathOf("src", "RayBork", "RayBork.Engine", "Shaders", FRAGMENT_SOURCE_FILE));
        string constant = AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE.ToString("R", CultureInfo.InvariantCulture);

        Assert.Contains(
            $"const float AGX_OUTPUT_WHITE_LUMINANCE = {constant};",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LuminanceUsesTheRec709Weights() {
        Assert.Equal(0.2126, AgxToneMap.Luminance(Vector3.UnitX), 12);
        Assert.Equal(0.7152, AgxToneMap.Luminance(Vector3.UnitY), 12);
        Assert.Equal(0.0722, AgxToneMap.Luminance(Vector3.UnitZ), 12);
        Assert.Equal(1.0, AgxToneMap.Luminance(Vector3.One), 12);
        Assert.Equal(
            (0.2126 * 4.0) + (0.7152 * 2.0) + (0.0722 * 0.5),
            AgxToneMap.Luminance(new Vector3(4f, 2f, 0.5f)),
            12);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HighlightExtensionIsContinuousAcrossTheAgxWhiteKnee() {
        float knee = (float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE;
        Vector3 below = AgxToneMap.Present(new Vector3(knee), 1f, TonemappingMode.AgX, PresentEncoding.ScRgbHdr, _hdr);
        Vector3 above = AgxToneMap.Present(
            new Vector3(MathF.BitIncrement(knee)),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);

        double belowLuminance = AgxToneMap.Luminance(below);
        double aboveLuminance = AgxToneMap.Luminance(above);

        // The extension is exact at the knee: switching it on adds only the luminance the normalized
        // AgX response is missing, which is about 1.4e-6 relative for a neutral scene.
        Assert.InRange(belowLuminance, PAPER_WHITE * 0.9999f, PAPER_WHITE * 1.0001f);
        Assert.InRange(aboveLuminance, PAPER_WHITE * 0.9999f, PAPER_WHITE * 1.0001f);
        Assert.InRange(Math.Abs(aboveLuminance - belowLuminance), 0.0, 1e-4);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HighlightExtensionReachesAndClampsAtThePeak() {
        float atPeak = (float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE * (PEAK / PAPER_WHITE);
        float beyondPeak = (float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE * 100f;
        Vector3 peak = AgxToneMap.Present(new Vector3(atPeak), 1f, TonemappingMode.AgX, PresentEncoding.ScRgbHdr, _hdr);
        Vector3 clamped = AgxToneMap.Present(
            new Vector3(beyondPeak),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);

        Assert.Equal(PEAK, AgxToneMap.Luminance(peak), 4);
        Assert.Equal(PEAK, AgxToneMap.Luminance(clamped), 4);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HighlightLuminanceIsMonotonicInSceneLuminance() {
        double previous = 0.0;
        for (int step = 0; step <= 160; step++) {
            float sceneValue = 0.01f * MathF.Pow(2f, step / 12f);
            Vector3 present = AgxToneMap.Present(
                new Vector3(sceneValue),
                1f,
                TonemappingMode.AgX,
                PresentEncoding.ScRgbHdr,
                _hdr);
            double luminance = AgxToneMap.Luminance(present);
            Assert.True(
                luminance >= previous - 1e-6,
                $"Luminance {luminance} at scene value {sceneValue} is below the previous luminance {previous}.");
            previous = luminance;
        }

        Assert.True(previous > PAPER_WHITE, $"The sweep never exceeded paper white; the last luminance was {previous}.");
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HighlightExtensionPreservesChromaticity() {
        Vector3 scene = new((float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE * 3f);
        Vector3 expected = AgxToneMap.Agx(scene) / (float)AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE;
        Vector3 actual = AgxToneMap.Present(scene, 1f, TonemappingMode.AgX, PresentEncoding.ScRgbHdr, _hdr);

        Assert.Equal(expected.X / expected.Y, actual.X / actual.Y, 4);
        Assert.Equal(expected.Y / expected.Z, actual.Y / actual.Z, 4);
        Assert.Equal(expected.X / expected.Z, actual.X / actual.Z, 4);
        Assert.True(AgxToneMap.Luminance(actual) > PAPER_WHITE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HighlightExtensionIsDisabledWithoutHeadroomOrOnOtherPaths() {
        WindowHdrState noHeadroom = new(true, PAPER_WHITE, 1f);
        Assert.False(AgxToneMap.IsHdrHighlightActive(PresentEncoding.ScRgbHdr, TonemappingMode.AgX, noHeadroom));
        Assert.False(AgxToneMap.IsHdrHighlightActive(PresentEncoding.ScRgbHdr, TonemappingMode.AgX, WindowHdrState.Sdr));
        Assert.False(AgxToneMap.IsHdrHighlightActive(PresentEncoding.LinearClamp, TonemappingMode.AgX, _hdr));
        Assert.False(AgxToneMap.IsHdrHighlightActive(PresentEncoding.ManualSrgb, TonemappingMode.AgX, _hdr));
        Assert.False(AgxToneMap.IsHdrHighlightActive(PresentEncoding.ScRgbHdr, TonemappingMode.None, _hdr));

        Vector3 present = AgxToneMap.Present(
            new Vector3((float)AgxToneMap.AGX_WHITE_SCENE_LUMINANCE * 4f),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            noHeadroom);

        Assert.InRange(AgxToneMap.Luminance(present), PAPER_WHITE * 0.9999f, PAPER_WHITE * 1.0001f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrEncodingsKeepTheRawAgxClamp() {
        Vector3 raw = AgxToneMap.Agx(new Vector3(4f, 0.5f, 2f));
        Vector3 linear = AgxToneMap.Present(
            new Vector3(4f, 0.5f, 2f),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);
        Vector3 manual = AgxToneMap.Present(
            new Vector3(4f, 0.5f, 2f),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ManualSrgb,
            WindowHdrState.Sdr);

        Assert.Equal(Vector3.Clamp(raw, Vector3.Zero, Vector3.One), linear);
        Assert.Equal(Vector3.Clamp(AgxToneMap.SrgbEncode(raw), Vector3.Zero, Vector3.One), manual);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TonemappingNoneKeepsTheDirectHdrPath() {
        Vector3 scene = new(4f, 2f, 0.5f);
        Vector3 hdr = AgxToneMap.Present(scene, 1f, TonemappingMode.None, PresentEncoding.ScRgbHdr, _hdr);
        Vector3 sdr = AgxToneMap.Present(scene, 1f, TonemappingMode.None, PresentEncoding.LinearClamp, WindowHdrState.Sdr);
        Vector3 manual = AgxToneMap.Present(
            scene,
            1f,
            TonemappingMode.None,
            PresentEncoding.ManualSrgb,
            WindowHdrState.Sdr);

        Assert.Equal(new Vector3(8f, 4f, 1f), hdr);
        Assert.Equal(Vector3.Clamp(scene, Vector3.Zero, Vector3.One), sdr);
        Assert.Equal(Vector3.Clamp(AgxToneMap.SrgbEncode(scene), Vector3.Zero, Vector3.One), manual);
        Assert.True(AgxToneMap.Luminance(hdr) > PAPER_WHITE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScRgbAgxBelowTheKneeUsesNormalizedPaperWhite() {
        Vector3 tone = AgxToneMap.Agx(Vector3.One);
        Vector3 expected = tone / (float)AgxToneMap.AGX_OUTPUT_WHITE_LUMINANCE * PAPER_WHITE;
        Vector3 present = AgxToneMap.Present(Vector3.One, 1f, TonemappingMode.AgX, PresentEncoding.ScRgbHdr, _hdr);

        Assert.Equal(expected.X, present.X, 6);
        Assert.Equal(expected.Y, present.Y, 6);
        Assert.Equal(expected.Z, present.Z, 6);
        Assert.True(AgxToneMap.Luminance(present) < PAPER_WHITE);
    }
}
