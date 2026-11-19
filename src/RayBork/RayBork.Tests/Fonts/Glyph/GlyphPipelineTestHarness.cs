using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Starts one app-lifetime generation service and creates pipelines over one fake texture factory;
/// dispose stops the service before the pipelines.
/// </summary>
internal sealed class GlyphPipelineTestHarness : IDisposable
{
    /// <summary>The timeout that guards every wait on the font thread.</summary>
    internal static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);

    private readonly List<FontGlyphPipeline> _pipelines = [];

    /// <summary>Starts a service over a fresh logger factory and fake texture factory.</summary>
    internal GlyphPipelineTestHarness()
    {
        LoggerFactory = new TrackingLoggerFactory();
        Factory = new FakeGlyphTextureFactory();
        Service = new GlyphGenerationService(LoggerFactory);
        Service.Start();
    }

    /// <summary>Gets the logger factory that records the pipeline and service diagnostics.</summary>
    internal TrackingLoggerFactory LoggerFactory { get; }

    /// <summary>Gets the fake texture factory.</summary>
    internal FakeGlyphTextureFactory Factory { get; }

    /// <summary>Gets the started generation service.</summary>
    internal GlyphGenerationService Service { get; }

    /// <summary>Creates and registers a pipeline over one face.</summary>
    internal FontGlyphPipeline CreatePipeline(
        SfntFace face,
        FontGlyphSettings? settings = null,
        int? textureBudget = null
    )
    {
        ArgumentNullException.ThrowIfNull(face);

        FontGlyphSettings resolved = settings ?? FontGlyphSettings.CreateDefault(face.UnitsPerEm);
        ILogger logger = LoggerFactory.CreateLogger(GlyphGenerationService.LOGGER_CATEGORY);
        FontGlyphPipeline pipeline = textureBudget is null
            ? new FontGlyphPipeline(face, resolved, Factory, Service, logger)
            : new FontGlyphPipeline(face, resolved, Factory, Service, textureBudget.Value, logger);
        _pipelines.Add(pipeline);
        return pipeline;
    }

    /// <summary>Begins a frame, requests one glyph, and waits until it settles.</summary>
    internal static async Task RequestAndWaitAsync(
        FontGlyphPipeline pipeline,
        int glyphId,
        long frameIndex
    )
    {
        pipeline.BeginFrame(frameIndex);
        FrameAssetRetention probe = new();
        Assert.Null(pipeline.Acquire(glyphId, probe));
        probe.Release();
        await pipeline
            .WhenGlyphSettledAsync(glyphId)
            .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken);
    }

    /// <summary>Begins a frame, requests one glyph, waits, and returns the texture.</summary>
    internal static async Task<Texture> GenerateAsync(
        FontGlyphPipeline pipeline,
        int glyphId,
        long frameIndex
    )
    {
        pipeline.BeginFrame(frameIndex);
        FrameAssetRetention probe = new();
        Texture? texture;
        try
        {
            texture = pipeline.Acquire(glyphId, probe);
        }
        finally
        {
            probe.Release();
        }

        if (texture is null)
        {
            await pipeline
                .WhenGlyphSettledAsync(glyphId)
                .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken);
            texture = AcquireOnce(pipeline, glyphId);
        }

        Assert.NotNull(texture);
        return texture!;
    }

    /// <summary>Acquires one glyph and immediately releases the frame hold.</summary>
    internal static Texture? AcquireOnce(FontGlyphPipeline pipeline, int glyphId)
    {
        FrameAssetRetention retention = new();
        try
        {
            return pipeline.Acquire(glyphId, retention);
        }
        finally
        {
            retention.Release();
        }
    }

    /// <summary>Stops a service with a bounded wait, so a broken join fails the test.</summary>
    internal static void StopService(GlyphGenerationService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        Task.Run(service.Stop, TestContext.Current.CancellationToken)
            .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopService(Service);
        foreach (FontGlyphPipeline pipeline in _pipelines)
        {
            pipeline.Dispose();
        }

        LoggerFactory.Dispose();
    }
}
