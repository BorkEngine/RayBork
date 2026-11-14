using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Glyph;

/// <summary>
/// Starts one app-lifetime generation service and creates pipelines over one fake texture factory.
/// <see cref="Dispose"/> follows the required shutdown order: stop the service, then dispose every
/// pipeline. A test may dispose a pipeline early; the second dispose is a no-op.
/// </summary>
internal sealed class GlyphPipelineTestHarness : IDisposable {
    /// <summary>The timeout that guards every wait on the font thread.</summary>
    internal static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(30);

    private readonly List<FontGlyphPipeline> _pipelines = [];

    /// <summary>Starts a service over a fresh logger factory and fake texture factory.</summary>
    internal GlyphPipelineTestHarness() {
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
    /// <param name="face">The parsed face.</param>
    /// <param name="settings">The MSDF settings, or the face's defaults when omitted.</param>
    /// <param name="textureBudget">The cache budget, or the production budget when omitted.</param>
    /// <returns>The pipeline.</returns>
    internal FontGlyphPipeline CreatePipeline(
        SfntFace face,
        FontGlyphSettings? settings = null,
        int? textureBudget = null) {
        ArgumentNullException.ThrowIfNull(face);

        FontGlyphSettings resolved = settings ?? FontGlyphSettings.CreateDefault(face.UnitsPerEm);
        ILogger logger = LoggerFactory.CreateLogger(GlyphGenerationService.LOGGER_CATEGORY);
        FontGlyphPipeline pipeline = textureBudget is null
            ? new FontGlyphPipeline(face, resolved, Factory, Service, logger)
            : new FontGlyphPipeline(face, resolved, Factory, Service, textureBudget.Value, logger);
        _pipelines.Add(pipeline);
        return pipeline;
    }

    /// <summary>
    /// Begins one frame, requests one glyph, and waits until it settles. The probe acquisition holds no
    /// frame and releases the empty retention set before it waits.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="frameIndex">The frame index for the request.</param>
    /// <returns>A task that completes when the glyph is terminal.</returns>
    internal static async Task RequestAndWaitAsync(
        FontGlyphPipeline pipeline,
        int glyphId,
        long frameIndex) {
        pipeline.BeginFrame(frameIndex);
        FrameAssetRetention probe = new();
        Assert.Null(pipeline.Acquire(glyphId, probe));
        probe.Release();
        await pipeline.WhenGlyphSettledAsync(glyphId)
            .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Begins one frame, requests one glyph, waits until it is ready, and returns the cached texture.
    /// The acquisition's frame hold is released, simulating a frame that completed. A glyph that is
    /// already cached is returned without a second generation.
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <param name="frameIndex">The frame index for the request.</param>
    /// <returns>The generated texture.</returns>
    internal static async Task<Texture> GenerateAsync(
        FontGlyphPipeline pipeline,
        int glyphId,
        long frameIndex) {
        pipeline.BeginFrame(frameIndex);
        FrameAssetRetention probe = new();
        Texture? texture;
        try {
            texture = pipeline.Acquire(glyphId, probe);
        } finally {
            probe.Release();
        }

        if (texture is null) {
            await pipeline.WhenGlyphSettledAsync(glyphId)
                .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken);
            texture = AcquireOnce(pipeline, glyphId);
        }

        Assert.NotNull(texture);
        return texture!;
    }

    /// <summary>Acquires one glyph and immediately releases the frame hold.</summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="glyphId">The glyph id.</param>
    /// <returns>The cached texture, or <see langword="null"/> on a miss.</returns>
    internal static Texture? AcquireOnce(FontGlyphPipeline pipeline, int glyphId) {
        FrameAssetRetention retention = new();
        try {
            return pipeline.Acquire(glyphId, retention);
        } finally {
            retention.Release();
        }
    }

    /// <summary>
    /// Stops a service on a pool thread with a bounded wait, so a broken join fails the test instead of
    /// hanging it.
    /// </summary>
    /// <param name="service">The service to stop.</param>
    internal static void StopService(GlyphGenerationService service) {
        ArgumentNullException.ThrowIfNull(service);

        Task.Run(service.Stop, TestContext.Current.CancellationToken)
            .WaitAsync(TIMEOUT, TestContext.Current.CancellationToken)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc/>
    public void Dispose() {
        StopService(Service);
        foreach (FontGlyphPipeline pipeline in _pipelines) {
            pipeline.Dispose();
        }

        LoggerFactory.Dispose();
    }
}
