using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class BuilderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildUsesDocumentedDefaults() {
        ScriptedGame game = new();
        RayBorkAppBuilder builder = new RayBorkAppBuilder().WithGame(game);

        using RayBorkApp app = BuildWithFakePlatform(builder, out FakeEnginePlatform platform);
        AppConfiguration configuration = app.Configuration;

        Assert.Same(game, configuration.Game);
        Assert.Equal("RayBork", configuration.Title);
        Assert.Equal(1280, configuration.Width);
        Assert.Equal(720, configuration.Height);
        Assert.False(configuration.Resizable);
        Assert.Equal(0.25, configuration.MaxDeltaTime);
        Assert.Equal(0, configuration.TargetFps);
        Assert.Equal(WindowState.Normal, configuration.WindowState);
        Assert.Null(configuration.LoggerFactory);
        Assert.Equal(LogLevel.Information, configuration.LogLevel);
        Assert.Equal(0, platform.InitializeCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildUsesConfiguredOverrides() {
        ScriptedGame game = new();
        TrackingLoggerFactory loggerFactory = new();

        RayBorkAppBuilder builder = new RayBorkAppBuilder()
            .WithGame(game)
            .WithTitle("RayBork Test")
            .WithSize(800, 600)
            .WithResizable()
            .WithMaxDeltaTime(0.5)
            .WithTargetFps(120)
            .WithWindowState(WindowState.Maximized)
            .WithLoggerFactory(loggerFactory)
            .WithLogLevel(LogLevel.Debug);

        using RayBorkApp app = BuildWithFakePlatform(builder, out _);
        AppConfiguration configuration = app.Configuration;

        Assert.Same(game, configuration.Game);
        Assert.Equal("RayBork Test", configuration.Title);
        Assert.Equal(800, configuration.Width);
        Assert.Equal(600, configuration.Height);
        Assert.True(configuration.Resizable);
        Assert.Equal(0.5, configuration.MaxDeltaTime);
        Assert.Equal(120, configuration.TargetFps);
        Assert.Equal(WindowState.Maximized, configuration.WindowState);
        Assert.Same(loggerFactory, configuration.LoggerFactory);
        Assert.Equal(LogLevel.Debug, configuration.LogLevel);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithGameRejectsNull() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithGame(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithTitleRejectsNull() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithTitle(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithTitleRejectsEmptyOrWhitespace(string title) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentException>(() => builder.WithTitle(title));
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(-1, 720)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithSizeRejectsNonPositiveWidth(int width, int height) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSize(width, height));
    }

    [Theory]
    [InlineData(1280, 0)]
    [InlineData(1280, -1)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithSizeRejectsNonPositiveHeight(int width, int height) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithSize(width, height));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.25)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithMaxDeltaTimeRejectsInvalidValues(double seconds) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMaxDeltaTime(seconds));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithTargetFpsRejectsNegativeValues(int framesPerSecond) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTargetFps(framesPerSecond));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithLoggerFactoryRejectsNull() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentNullException>(() => builder.WithLoggerFactory(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithWindowStateRejectsUndefinedValues() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithWindowState((WindowState)int.MaxValue));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithLogLevelRejectsUndefinedValues() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithLogLevel((LogLevel)int.MaxValue));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithPresentModeRejectsUndefinedValues() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithPresentMode((PresentMode)int.MaxValue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithMSAARejectsNonPositiveValues(int sampleCount) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithMSAA(sampleCount));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithMSAAAcceptsPositiveUnsupportedValues() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Same(builder, builder.WithMSAA(3));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithTonemappingRejectsUndefinedValues() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithTonemapping((TonemappingMode)int.MaxValue));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithExposureRejectsNonFiniteValues(float ev) {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithExposure(ev));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithClearColorRejectsNonFiniteComponents() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Equal(
            "color",
            Assert.Throws<ArgumentException>(() => builder.WithClearColor(new Color(float.NaN, 0f, 0f, 1f))).ParamName);
        Assert.Equal(
            "color",
            Assert.Throws<ArgumentException>(() => builder.WithClearColor(new Color(0f, float.PositiveInfinity, 0f, 1f))).ParamName);
        Assert.Equal(
            "color",
            Assert.Throws<ArgumentException>(() => builder.WithClearColor(new Color(0f, 0f, float.NegativeInfinity, 1f))).ParamName);
        Assert.Equal(
            "color",
            Assert.Throws<ArgumentException>(() => builder.WithClearColor(new Color(0f, 0f, 0f, float.NaN))).ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildWithoutGameThrows() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildIsSingleUse() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder().WithGame(new ScriptedGame());
        using RayBorkApp app = BuildWithFakePlatform(builder, out _);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => BuildWithFakePlatform(builder, out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildUsesDocumentedRenderDefaults() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder().WithGame(new ScriptedGame());

        using RayBorkApp app = BuildWithFakePlatform(builder, out _);
        RenderSettings settings = app.Configuration.RenderSettings;

        Assert.Equal(PresentMode.Mailbox, settings.PresentMode);
        Assert.Equal(4, settings.SampleCount);
        Assert.Equal(TonemappingMode.AgX, settings.Tonemapping);
        Assert.Equal(0f, settings.Exposure);
        Assert.Equal(Color.Black, settings.ClearColor);

#if DEBUG
        bool expectedValidation = true;
#else
        bool expectedValidation = false;
#endif
        Assert.Equal(expectedValidation, settings.ValidationEnabled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildUsesConfiguredRenderOverrides() {
        Color clearColor = new(0.1f, 0.2f, 0.3f, 0.4f);
        RayBorkAppBuilder builder = new RayBorkAppBuilder()
            .WithGame(new ScriptedGame())
            .WithPresentMode(PresentMode.Immediate)
            .WithMSAA(8)
            .WithTonemapping(TonemappingMode.None)
            .WithExposure(2.5f)
            .WithClearColor(clearColor)
            .WithValidation(false);

        using RayBorkApp app = BuildWithFakePlatform(builder, out _);
        RenderSettings settings = app.Configuration.RenderSettings;

        Assert.Equal(PresentMode.Immediate, settings.PresentMode);
        Assert.Equal(8, settings.SampleCount);
        Assert.Equal(TonemappingMode.None, settings.Tonemapping);
        Assert.Equal(2.5f, settings.Exposure);
        Assert.Equal(clearColor, settings.ClearColor);
        Assert.False(settings.ValidationEnabled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithMethodsAfterBuildThrow() {
        ScriptedGame game = new();
        TrackingLoggerFactory loggerFactory = new();
        RayBorkAppBuilder builder = new RayBorkAppBuilder().WithGame(game);
        using RayBorkApp app = BuildWithFakePlatform(builder, out _);

        Assert.Throws<InvalidOperationException>(() => builder.WithGame(game));
        Assert.Throws<InvalidOperationException>(() => builder.WithTitle("Late"));
        Assert.Throws<InvalidOperationException>(() => builder.WithSize(640, 480));
        Assert.Throws<InvalidOperationException>(() => builder.WithResizable());
        Assert.Throws<InvalidOperationException>(() => builder.WithMaxDeltaTime(0.5));
        Assert.Throws<InvalidOperationException>(() => builder.WithPresentMode(PresentMode.Fifo));
        Assert.Throws<InvalidOperationException>(() => builder.WithMSAA(8));
        Assert.Throws<InvalidOperationException>(() => builder.WithTonemapping(TonemappingMode.None));
        Assert.Throws<InvalidOperationException>(() => builder.WithExposure(1f));
        Assert.Throws<InvalidOperationException>(() => builder.WithClearColor(Color.White));
        Assert.Throws<InvalidOperationException>(() => builder.WithValidation(false));
        Assert.Throws<InvalidOperationException>(() => builder.WithTargetFps(60));
        Assert.Throws<InvalidOperationException>(() => builder.WithWindowState(WindowState.Maximized));
        Assert.Throws<InvalidOperationException>(() => builder.WithLoggerFactory(loggerFactory));
        Assert.Throws<InvalidOperationException>(() => builder.WithLogLevel(LogLevel.Debug));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalBuildRejectsNullDependencies() {
        RayBorkAppBuilder builder = new RayBorkAppBuilder().WithGame(new ScriptedGame());

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, static () => new FakeRendererBackend(), TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => builder.Build(_ => new FakeEnginePlatform(), null!, TimeProvider.System));
        Assert.Throws<ArgumentNullException>(() => builder.Build(_ => new FakeEnginePlatform(), static () => new FakeRendererBackend(), null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuildDoesNotCreateAPlatformOrALogger() {
        bool platformRequested = false;
        TrackingLoggerFactory loggerFactory = new();
        RayBorkAppBuilder builder = new RayBorkAppBuilder()
            .WithGame(new ScriptedGame())
            .WithLoggerFactory(loggerFactory);

        using RayBorkApp app = builder.Build(
            _ => {
                platformRequested = true;
                return new FakeEnginePlatform();
            },
            static () => new FakeRendererBackend(),
            TimeProvider.System);

        Assert.False(platformRequested);
        Assert.Empty(loggerFactory.Categories);
        Assert.Equal(0, loggerFactory.DisposeCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublicBuildReturnsAnAppThatCanBeDisposedWithoutRunning() {
        RayBorkApp app = new RayBorkAppBuilder()
            .WithGame(new ScriptedGame())
            .Build();

        Assert.False(app.IsRunning);
        Assert.False(app.IsDisposed);
        Assert.Null(app.TickThread);
        Assert.Null(app.RenderThread);

        app.Dispose();

        Assert.True(app.IsDisposed);
    }

    private static RayBorkApp BuildWithFakePlatform(RayBorkAppBuilder builder, out FakeEnginePlatform platform) {
        platform = new FakeEnginePlatform();
        FakeEnginePlatform captured = platform;
        return builder.Build(_ => captured, static () => new FakeRendererBackend(), TimeProvider.System);
    }
}
