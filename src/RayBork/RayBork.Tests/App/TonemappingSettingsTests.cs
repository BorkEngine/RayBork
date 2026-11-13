using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Tests the app-lifetime runtime tone-mapping settings behind <see cref="EngineContext.Tonemapping"/>:
/// the internal startup constructor, the public validation and tick-thread rules, and the frame
/// snapshot.
/// </summary>
public sealed class TonemappingSettingsTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalConstructorStoresTheStartupValuesWithoutTheTickThread() {
        Assert.False(TickThread.IsCurrent);

        TonemappingSettings settings = new(1.5f, 4f, 2f);

        Assert.Equal(1.5f, settings.Exposure);
        Assert.Equal(4f, settings.AgxWhite);
        Assert.Equal(2f, settings.AgxContrast);
        Assert.Equal(new TonemappingSnapshot(1.5f, 4f, 2f), settings.Snapshot());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SnapshotDefaultUsesTheGodotAgxValues() {
        TonemappingSnapshot defaults = TonemappingSnapshot.Default;

        Assert.Equal(0f, defaults.Exposure);
        Assert.Equal(16.29f, defaults.AgxWhite);
        Assert.Equal(1.25f, defaults.AgxContrast);
        Assert.Equal(AgxToneMap.DEFAULT_AGX_WHITE, defaults.AgxWhite);
        Assert.Equal(AgxToneMap.DEFAULT_AGX_CONTRAST, defaults.AgxContrast);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalConstructorRejectsNonFiniteExposure(float exposure) {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TonemappingSettings(exposure, AgxToneMap.DEFAULT_AGX_WHITE, AgxToneMap.DEFAULT_AGX_CONTRAST));

        Assert.Equal(nameof(TonemappingSettings.Exposure), thrown.ParamName);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(1.99f)]
    [InlineData(0f)]
    [InlineData(-4f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalConstructorRejectsAnAgxWhiteBelowTwo(float agxWhite) {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TonemappingSettings(0f, agxWhite, AgxToneMap.DEFAULT_AGX_CONTRAST));

        Assert.Equal(nameof(TonemappingSettings.AgxWhite), thrown.ParamName);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0.99f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalConstructorRejectsAnAgxContrastBelowOne(float agxContrast) {
        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => new TonemappingSettings(0f, AgxToneMap.DEFAULT_AGX_WHITE, agxContrast));

        Assert.Equal(nameof(TonemappingSettings.AgxContrast), thrown.ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalConstructorAcceptsTheValidationBoundaries() {
        TonemappingSettings settings = new(0f, 2f, 1f);

        Assert.Equal(2f, settings.AgxWhite);
        Assert.Equal(1f, settings.AgxContrast);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettersUpdateTheSnapshotOnTheTickThread() {
        TonemappingSettings settings = new(0f, AgxToneMap.DEFAULT_AGX_WHITE, AgxToneMap.DEFAULT_AGX_CONTRAST);

        TickThread.Attach();
        try {
            settings.Exposure = -2.5f;
            settings.AgxWhite = 2f;
            settings.AgxContrast = 3f;
        } finally {
            TickThread.Detach();
        }

        Assert.Equal(-2.5f, settings.Exposure);
        Assert.Equal(2f, settings.AgxWhite);
        Assert.Equal(3f, settings.AgxContrast);
        Assert.Equal(new TonemappingSnapshot(-2.5f, 2f, 3f), settings.Snapshot());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AValidWriteFromAnotherThreadThrowsTheTickThreadMessage() {
        TonemappingSettings settings = new(0f, AgxToneMap.DEFAULT_AGX_WHITE, AgxToneMap.DEFAULT_AGX_CONTRAST);
        Exception? failure = null;

        TickThread.Attach();
        try {
            Thread worker = new(() => {
                try {
                    settings.Exposure = 1f;
                } catch (Exception exception) {
                    failure = exception;
                }
            }) {
                IsBackground = true,
                Name = "RayBork.Tests.TonemappingWriter",
            };
            worker.Start();
            worker.Join();
        } finally {
            TickThread.Detach();
        }

        InvalidOperationException thrown = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains("tick-thread only", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0f, settings.Exposure);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettersValidateBeforeCheckingTheTickThread() {
        TonemappingSettings settings = new(0f, AgxToneMap.DEFAULT_AGX_WHITE, AgxToneMap.DEFAULT_AGX_CONTRAST);

        Assert.False(TickThread.IsCurrent);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Exposure = float.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.AgxWhite = 1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.AgxContrast = 0.5f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsAreAllowedWithoutTheTickThread() {
        TonemappingSettings settings = new(1f, 3f, 2f);

        Assert.False(TickThread.IsCurrent);
        Assert.Equal(1f, settings.Exposure);
        Assert.Equal(3f, settings.AgxWhite);
        Assert.Equal(2f, settings.AgxContrast);
    }
}
