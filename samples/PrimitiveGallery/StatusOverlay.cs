using System.Diagnostics;
using RayBork;

namespace PrimitiveGallery;

/// <summary>
/// Builds the gallery status line: the presented frames per second over a one-second window, the tick
/// rate, the render CPU time, and the last completed frame's draw and instance counts.
/// </summary>
/// <remarks>
/// The presented rate is a delta of <see cref="RenderStats.PresentedFrames"/>, which is cumulative
/// and advances only when a frame reaches the presentation engine. It is not
/// <see cref="EngineContext.Fps"/>, which is the tick rate. The helper keeps one sample window, so an
/// instance is per sample. The sample draws the returned line in
/// <see cref="EngineDefaults.DefaultMonoFont"/>, so the changing counters keep their positions.
/// </remarks>
internal sealed class StatusOverlay {
    private const double SAMPLE_SECONDS = 1.0;

    private long _sampleStartedAt;
    private long _presentedAtSampleStart;
    private double _presentedFramesPerSecond;

    /// <summary>
    /// Returns the status line for the current tick and advances the one-second presentation sample.
    /// </summary>
    /// <param name="context">The tick context that supplies the tick rate and the render statistics.</param>
    internal string Build(EngineContext context) {
        RenderStats stats = context.Stats;
        long now = Stopwatch.GetTimestamp();
        if (_sampleStartedAt == 0) {
            // The first tick only anchors the window; a presented rate needs a completed interval.
            _sampleStartedAt = now;
            _presentedAtSampleStart = stats.PresentedFrames;
        } else {
            double elapsed = Stopwatch.GetElapsedTime(_sampleStartedAt, now).TotalSeconds;
            if (elapsed >= SAMPLE_SECONDS) {
                _presentedFramesPerSecond = (stats.PresentedFrames - _presentedAtSampleStart) / elapsed;
                _sampleStartedAt = now;
                _presentedAtSampleStart = stats.PresentedFrames;
            }
        }

        return FormattableString.Invariant(
            $"presented {_presentedFramesPerSecond:0.0} fps   tick {context.Fps:0.0}   render {stats.CpuFrameMilliseconds:0.0} ms   draw calls {stats.DrawCalls}   instances {stats.Instances}");
    }
}
