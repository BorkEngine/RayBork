using RayBork.Internal;

namespace RayBork.Tests;

public sealed class RendererContractTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EngineInfoSupportsValueEqualityWithAndDeconstruction() {
        EngineInfo info = CreateEngineInfo();

        EngineInfo copy = info with { };
        EngineInfo changed = info with { SampleCount = 8 };

        Assert.Equal(info, copy);
        Assert.Equal(info.GetHashCode(), copy.GetHashCode());
        Assert.True(info == copy);
        Assert.False(info != copy);
        Assert.NotEqual(info, changed);
        Assert.True(info != changed);

        var (name, version, rendererName, gpuName, vulkanApiVersion, swapchainFormat, hdrOutput, sampleCount, presentMode, validationEnabled) = info;

        Assert.Equal("RayBork", name);
        Assert.Equal(new Version(0, 1, 0, 0), version);
        Assert.Equal("Vulkan 1.2", rendererName);
        Assert.Equal("Test GPU", gpuName);
        Assert.Equal(4202496u, vulkanApiVersion);
        Assert.Equal("B8G8R8A8_SRGB", swapchainFormat);
        Assert.False(hdrOutput);
        Assert.Equal(4, sampleCount);
        Assert.Equal(PresentMode.Mailbox, presentMode);
        Assert.True(validationEnabled);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RenderStatsSupportsValueEqualityWithAndDeconstruction() {
        RenderStats stats = CreateRenderStats();

        RenderStats copy = stats with { };
        RenderStats changed = stats with { DrawCalls = 8 };

        Assert.Equal(stats, copy);
        Assert.Equal(stats.GetHashCode(), copy.GetHashCode());
        Assert.True(stats == copy);
        Assert.False(stats != copy);
        Assert.NotEqual(stats, changed);
        Assert.True(stats != changed);

        var (frameIndex, drawCalls, instances, triangles, culledInstances, pipelineCount, textureCount, sampleCount, hdrOutput, cpuFrameMilliseconds, gpuFrameMilliseconds, framesInFlight, recoveryCount) = stats;

        Assert.Equal(42L, frameIndex);
        Assert.Equal(7, drawCalls);
        Assert.Equal(9, instances);
        Assert.Equal(1200, triangles);
        Assert.Equal(3, culledInstances);
        Assert.Equal(2, pipelineCount);
        Assert.Equal(5, textureCount);
        Assert.Equal(4, sampleCount);
        Assert.False(hdrOutput);
        Assert.Equal(1.5, cpuFrameMilliseconds);
        Assert.Equal(2.5, gpuFrameMilliseconds);
        Assert.Equal(2, framesInFlight);
        Assert.Equal(1, recoveryCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GraphicsExceptionsFormTheDocumentedHierarchy() {
        Assert.False(typeof(GraphicsException).IsSealed);
        Assert.True(typeof(GraphicsException).IsSubclassOf(typeof(RayBorkException)));

        Assert.True(typeof(GraphicsDeviceLostException).IsSealed);
        Assert.True(typeof(GraphicsDeviceLostException).IsSubclassOf(typeof(GraphicsException)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GraphicsExceptionPublicConstructorsPreserveMessageAndInnerException() {
        InvalidOperationException inner = new("inner");

        GraphicsException withMessage = new("boom");
        GraphicsException withInner = new("boom", inner);

        Assert.Equal("boom", withMessage.Message);
        Assert.Null(withMessage.InnerException);
        Assert.Null(withMessage.DiagnosticCode);
        Assert.Equal("boom", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
        Assert.Null(withInner.DiagnosticCode);
        Assert.Null(new GraphicsException().DiagnosticCode);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GraphicsDeviceLostExceptionPublicConstructorsPreserveMessageAndInnerException() {
        InvalidOperationException inner = new("inner");

        GraphicsDeviceLostException withMessage = new("boom");
        GraphicsDeviceLostException withInner = new("boom", inner);

        Assert.Equal("boom", withMessage.Message);
        Assert.Null(withMessage.InnerException);
        Assert.Null(withMessage.DiagnosticCode);
        Assert.Equal("boom", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
        Assert.Null(withInner.DiagnosticCode);
        Assert.Null(new GraphicsDeviceLostException().DiagnosticCode);

        GraphicsException asBase = withInner;
        Assert.IsType<GraphicsDeviceLostException>(asBase);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GraphicsExceptionInternalConstructorSetsDiagnosticCode() {
        InvalidOperationException inner = new("inner");

        Assert.Equal(
            DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
            new GraphicsException("boom", DiagnosticCodes.GRAPHICS_UNRECOVERABLE, inner).DiagnosticCode);
        Assert.Equal(
            DiagnosticCodes.GRAPHICS_DEVICE_LOST,
            new GraphicsDeviceLostException("boom", DiagnosticCodes.GRAPHICS_DEVICE_LOST, inner).DiagnosticCode);
        Assert.Same(
            inner,
            new GraphicsDeviceLostException("boom", DiagnosticCodes.GRAPHICS_DEVICE_LOST, inner).InnerException);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void GraphicsDiagnosticCodesAreDistinctAndRbPrefixed() {
        string[] codes = [
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            DiagnosticCodes.GRAPHICS_DEVICE_LOST,
            DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
        ];

        Assert.Equal(codes.Length, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.True(
            code.StartsWith("RB", StringComparison.Ordinal),
            $"Diagnostic code '{code}' must start with 'RB'."));
    }

    private static EngineInfo CreateEngineInfo() => new(
        "RayBork",
        new Version(0, 1, 0, 0),
        "Vulkan 1.2",
        "Test GPU",
        4202496u,
        "B8G8R8A8_SRGB",
        false,
        4,
        PresentMode.Mailbox,
        true);

    private static RenderStats CreateRenderStats() => new(
        42L,
        7,
        9,
        1200,
        3,
        2,
        5,
        4,
        false,
        1.5,
        2.5,
        2,
        1);
}
