using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class AgxToneMapTests {
    private const string FRAGMENT_SOURCE_FILE = "Present.frag.glsl";
    private const float PAPER_WHITE = 2f;
    private const float PEAK_HEADROOM = 6.25f;
    private const float PEAK = PAPER_WHITE * PEAK_HEADROOM;
    private static readonly WindowHdrState _hdr = new(true, PAPER_WHITE, PEAK_HEADROOM);
    private static readonly WindowHdrState _hdrHeadroomTwo = new(true, PAPER_WHITE, 2f);
    private static readonly AgxCurveParameters _sdrParameters = AgxCurveParameters.Create(
        AgxToneMap.DEFAULT_AGX_WHITE,
        AgxToneMap.DEFAULT_AGX_CONTRAST,
        1f);

    // The combined matrices are the Godot 4.7 AgX matrices and are copied into Present.frag.glsl.
    private static readonly string[] _shaderMatrixLiterals = [
        "0.544814746488245", "0.140416948464053", "0.0888104196149096",
        "0.373787398372697", "0.754137554567394", "0.178871756420858",
        "0.0813978551390581", "0.105445496968552", "0.732317823964232",
        "1.96488741169489", "-0.299313364904742", "-0.164352742528393",
        "-0.855988495690215", "1.32639796461980", "-0.238183969428088",
        "-0.108898916004672", "-0.0270845997150571", "1.40253671195648",
    ];

    [Theory]
    [InlineData(0.05f)]
    [InlineData(0.18f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(4f)]
    [InlineData(16.29f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrNeutralGrayStaysNeutral(float value) {
        Vector3 present = AgxToneMap.Present(
            new Vector3(value),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr);

        Assert.Equal(present.X, present.Y, 6);
        Assert.Equal(present.Y, present.Z, 6);
    }

    [Theory]
    [InlineData(0.18f)]
    [InlineData(1f)]
    [InlineData(10f)]
    [InlineData(101.8125f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrNeutralGrayStaysNeutral(float value) {
        Vector3 present = AgxToneMap.Present(
            new Vector3(value),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);

        Assert.Equal(present.X, present.Y, 6);
        Assert.Equal(present.Y, present.Z, 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CrossoverPointMapsToItselfAndTheCurveIsContinuous() {
        const float epsilon = 1e-5f;
        Vector3 at = AgxToneMap.Agx(new Vector3(0.18f), _sdrParameters);
        Vector3 below = AgxToneMap.Agx(new Vector3(0.18f - epsilon), _sdrParameters);
        Vector3 above = AgxToneMap.Agx(new Vector3(0.18f + epsilon), _sdrParameters);

        Assert.Equal(0.18, at.X, 6);
        Assert.InRange(MathF.Abs(below.X - at.X), 0f, 2e-5f);
        Assert.InRange(MathF.Abs(above.X - at.X), 0f, 2e-5f);

        // The crossover is independent of the output maximum, so the HDR curve is continuous there
        // too.
        AgxCurveParameters hdrParameters = AgxCurveParameters.Create(
            AgxToneMap.DEFAULT_AGX_WHITE,
            AgxToneMap.DEFAULT_AGX_CONTRAST,
            PEAK_HEADROOM);
        Assert.Equal(0.18, AgxToneMap.Agx(new Vector3(0.18f), hdrParameters).X, 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SdrDefaultResponseMatchesThePinnedReference() {
        AssertChannelsNear(
            AgxToneMap.Agx(Vector3.One, _sdrParameters),
            0.5961108490773503,
            0.5961108490773481,
            0.5961108490773473);
        AssertChannelsNear(
            AgxToneMap.Agx(new Vector3(4f), _sdrParameters),
            0.8659922348335485,
            0.8659922348335468,
            0.8659922348335449);
        AssertChannelsNear(AgxToneMap.Agx(new Vector3(0.18f), _sdrParameters), 0.18, 0.18, 0.18);

        // The AgX white and everything above it clamp to the curve output maximum.
        Assert.Equal(1.0, AgxToneMap.Agx(new Vector3(AgxToneMap.DEFAULT_AGX_WHITE), _sdrParameters).X, 6);
        Assert.Equal(1.0, AgxToneMap.Agx(new Vector3(65504f), _sdrParameters).X, 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CurveParametersMatchTheGodotFormulas() {
        AgxCurveParameters sdr = AgxCurveParameters.Create(
            AgxToneMap.DEFAULT_AGX_WHITE,
            AgxToneMap.DEFAULT_AGX_CONTRAST,
            1f);

        Assert.Equal(1.0, sdr.OutputMax, 12);
        Assert.Equal((double)AgxToneMap.DEFAULT_AGX_WHITE, sdr.White, 12);
        Assert.Equal(1.25, sdr.Contrast, 12);
        Assert.Equal(0.534111561194757, sdr.ToeA, 1e-9);
        Assert.Equal(1.0250000000000004, sdr.Slope, 1e-9);
        Assert.Equal(324.41516187286487, sdr.W, 1e-6);

        AgxCurveParameters hdr = AgxCurveParameters.Create(
            AgxToneMap.DEFAULT_AGX_WHITE,
            AgxToneMap.DEFAULT_AGX_CONTRAST,
            PEAK_HEADROOM);
        Assert.Equal((double)PEAK_HEADROOM, hdr.OutputMax, 12);
        Assert.Equal(101.8125057220459, hdr.White, 1e-6);
        Assert.Equal(1744.2167009596765, hdr.W, 1e-6);

        // Godot raises the AgX white to 2.0 because the curve misbehaves below it.
        Assert.Equal(2.0, AgxCurveParameters.Create(1f, AgxToneMap.DEFAULT_AGX_CONTRAST, 1f).White, 12);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AgxWhiteMovesTheCurveWhitePoint() {
        AgxCurveParameters lowWhite = AgxCurveParameters.Create(2f, AgxToneMap.DEFAULT_AGX_CONTRAST, 1f);
        Vector3 low = AgxToneMap.Agx(Vector3.One, lowWhite);
        Vector3 high = AgxToneMap.Agx(Vector3.One, _sdrParameters);

        // A lower white clamps the shoulder sooner, so the same scene value is brighter.
        Assert.True(low.X > high.X + 0.05f, $"The low-white response {low.X} is not brighter than {high.X}.");
        Assert.Equal(low.X, low.Y, 6);
        Assert.Equal(low.Y, low.Z, 6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AgxContrastExpandsTheCurveAroundTheCrossover() {
        Vector3 lowContrastShoulder = AgxToneMap.Agx(
            Vector3.One,
            AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 1f, 1f));
        Vector3 highContrastShoulder = AgxToneMap.Agx(
            Vector3.One,
            AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 2f, 1f));
        Vector3 lowContrastToe = AgxToneMap.Agx(
            new Vector3(0.05f),
            AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 1f, 1f));
        Vector3 highContrastToe = AgxToneMap.Agx(
            new Vector3(0.05f),
            AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 2f, 1f));

        // A higher contrast brightens the shoulder and darkens the toe.
        Assert.True(highContrastShoulder.X > lowContrastShoulder.X);
        Assert.True(highContrastToe.X < lowContrastToe.X);

        // The crossover is fixed, so both contrasts agree there.
        Assert.Equal(
            AgxToneMap.Agx(new Vector3(0.18f), AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 1f, 1f)).X,
            AgxToneMap.Agx(new Vector3(0.18f), AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 2f, 1f)).X,
            6);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentForwardsTheAgxWhiteAndContrast() {
        Vector3 withWhite = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr,
            agxWhite: 2f);
        Vector3 expectedWhite = Vector3.Clamp(
            AgxToneMap.Agx(Vector3.One, AgxCurveParameters.Create(2f, AgxToneMap.DEFAULT_AGX_CONTRAST, 1f)),
            Vector3.Zero,
            Vector3.One);
        Assert.Equal(expectedWhite, withWhite);

        Vector3 withContrast = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.LinearClamp,
            WindowHdrState.Sdr,
            agxContrast: 2f);
        Vector3 expectedContrast = Vector3.Clamp(
            AgxToneMap.Agx(Vector3.One, AgxCurveParameters.Create(AgxToneMap.DEFAULT_AGX_WHITE, 2f, 1f)),
            Vector3.Zero,
            Vector3.One);
        Assert.Equal(expectedContrast, withContrast);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrHeadroomTwoMapsWhiteToTwicePaperWhite() {
        Vector3 mid = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdrHeadroomTwo);
        Vector3 peak = AgxToneMap.Present(
            new Vector3(AgxToneMap.DEFAULT_AGX_WHITE * 2f),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdrHeadroomTwo);

        Assert.Equal(1.5115366907143337, AgxToneMap.Luminance(mid), 1e-4);
        Assert.Equal(PAPER_WHITE * 2f, AgxToneMap.Luminance(peak), 1e-3);
        Assert.True(AgxToneMap.Luminance(mid) < PAPER_WHITE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HdrHeadroomSixPointTwoFiveReachesTheDisplayPeak() {
        Vector3 mid = AgxToneMap.Present(
            Vector3.One,
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);
        Vector3 peak = AgxToneMap.Present(
            new Vector3(AgxToneMap.DEFAULT_AGX_WHITE * PEAK_HEADROOM),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);
        Vector3 clamped = AgxToneMap.Present(
            new Vector3(65504f),
            1f,
            TonemappingMode.AgX,
            PresentEncoding.ScRgbHdr,
            _hdr);

        // Scene 1.0 is near paper white in paper-white units (about 0.92), and the AgX white is the
        // display peak.
        Assert.Equal(1.8372399973404481, AgxToneMap.Luminance(mid), 1e-4);
        Assert.InRange(AgxToneMap.Luminance(mid), PAPER_WHITE * 0.85, PAPER_WHITE * 1.05);
        Assert.Equal(PEAK, AgxToneMap.Luminance(peak), 1e-3);
        Assert.Equal(PEAK, AgxToneMap.Luminance(clamped), 1e-3);
        Assert.True(AgxToneMap.Luminance(peak) > PAPER_WHITE);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PresentIsMonotonicInSceneLuminance(bool hdrPath) {
        WindowHdrState hdr = hdrPath ? _hdr : WindowHdrState.Sdr;
        PresentEncoding encoding = hdrPath ? PresentEncoding.ScRgbHdr : PresentEncoding.LinearClamp;
        double previous = 0.0;
        for (int step = 0; step <= 160; step++) {
            float sceneValue = 0.01f * MathF.Pow(2f, step / 12f);
            Vector3 present = AgxToneMap.Present(
                new Vector3(sceneValue),
                1f,
                TonemappingMode.AgX,
                encoding,
                hdr);
            double luminance = AgxToneMap.Luminance(present);
            Assert.True(
                luminance >= previous - 1e-6,
                $"Luminance {luminance} at scene value {sceneValue} is below the previous luminance {previous}.");
            previous = luminance;
        }

        Assert.Equal(hdrPath ? PEAK : 1.0, previous, 3);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TonemappingNoneKeepsTheDirectPath() {
        Vector3 scene = new(4f, 2f, 0.5f);
        Vector3 hdr = AgxToneMap.Present(scene, 1f, TonemappingMode.None, PresentEncoding.ScRgbHdr, _hdr);
        Vector3 exposed = AgxToneMap.Present(scene, 2f, TonemappingMode.None, PresentEncoding.ScRgbHdr, _hdr);
        Vector3 sdr = AgxToneMap.Present(scene, 1f, TonemappingMode.None, PresentEncoding.LinearClamp, WindowHdrState.Sdr);
        Vector3 manual = AgxToneMap.Present(
            scene,
            1f,
            TonemappingMode.None,
            PresentEncoding.ManualSrgb,
            WindowHdrState.Sdr);

        // None multiplies by paper white and keeps the display headroom literal.
        Assert.Equal(new Vector3(8f, 4f, 1f), hdr);
        Assert.Equal(new Vector3(16f, 8f, 2f), exposed);
        Assert.Equal(Vector3.Clamp(scene, Vector3.Zero, Vector3.One), sdr);
        Assert.Equal(Vector3.Clamp(AgxToneMap.SrgbEncode(scene), Vector3.Zero, Vector3.One), manual);
        Assert.True(AgxToneMap.Luminance(hdr) > PAPER_WHITE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExposureIsAppliedBeforeTheCurve() {
        Vector3 scene = new(0.4f, 1f, 2f);
        WindowHdrState[] states = [WindowHdrState.Sdr, _hdr];
        PresentEncoding[] encodings = [PresentEncoding.LinearClamp, PresentEncoding.ScRgbHdr];
        for (int index = 0; index < states.Length; index++) {
            Vector3 doubled = AgxToneMap.Present(
                scene,
                2f,
                TonemappingMode.AgX,
                encodings[index],
                states[index]);
            Vector3 scaledScene = AgxToneMap.Present(
                scene * 2f,
                1f,
                TonemappingMode.AgX,
                encodings[index],
                states[index]);
            Assert.Equal(scaledScene, doubled);
        }
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
    public void ShaderDeclaresTheCombinedAgxMatricesAndCurve() {
        string source = File.ReadAllText(
            RepositoryLayout.PathOf("src", "RayBork", "RayBork.Engine", "Shaders", FRAGMENT_SOURCE_FILE));

        foreach (string literal in _shaderMatrixLiterals) {
            Assert.Contains(literal, source, StringComparison.Ordinal);
        }

        Assert.Contains("const float AWP_CROSSOVER_POINT = 0.18;", source, StringComparison.Ordinal);
        Assert.Contains("AllenwpCurve", source, StringComparison.Ordinal);
        Assert.Contains("pc.awpToeA", source, StringComparison.Ordinal);
        Assert.Contains("pc.awpSlope", source, StringComparison.Ordinal);
        Assert.Contains("pc.awpW", source, StringComparison.Ordinal);

        // The M4 log-range AgX and the HDR highlight extension are gone.
        Assert.DoesNotContain("AGX_MIN_EV", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AGX_MAX_EV", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AGX_WHITE_SCENE_LUMINANCE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AGX_OUTPUT_WHITE_LUMINANCE", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgXDefaultContrastApprox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("hdrHighlight", source, StringComparison.Ordinal);
        Assert.DoesNotContain("agxWhiteSceneLuminance", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultRenderSettingsUseTheGodotAgxValues() {
        RenderSettings settings = new() {
            PresentMode = PresentMode.Mailbox,
            SampleCount = 4,
            Tonemapping = TonemappingMode.AgX,
            Exposure = 0f,
            ValidationEnabled = false,
        };

        Assert.Equal(16.29f, settings.AgxWhite);
        Assert.Equal(1.25f, settings.AgxContrast);
    }

    private static void AssertChannelsNear(Vector3 actual, double r, double g, double b) {
        Assert.Equal(r, actual.X, 1e-6);
        Assert.Equal(g, actual.Y, 1e-6);
        Assert.Equal(b, actual.Z, 1e-6);
    }
}
