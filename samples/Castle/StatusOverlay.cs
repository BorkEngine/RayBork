using System.Diagnostics;
using RayBork;

namespace Castle;

/// <summary>
/// Builds the Castle status line: presented FPS, tick rate, render time, and draw totals.
/// </summary>
/// <remarks>
/// The presented rate is a <see cref="RenderStats.PresentedFrames"/> delta, not the tick rate in
/// <see cref="EngineContext.Fps"/>.
/// </remarks>
internal sealed class StatusOverlay
{
    private const double SAMPLE_SECONDS = 1.0;

    private long _sampleStartedAt;
    private long _presentedAtSampleStart;
    private double _presentedFramesPerSecond;

    /// <summary>Returns the status line and advances the one-second presentation sample.</summary>
    internal string Build(EngineContext context)
    {
        RenderStats stats = context.Stats;
        long now = Stopwatch.GetTimestamp();
        if (_sampleStartedAt == 0)
        {
            // The first tick only anchors the window; a presented rate needs a completed interval.
            _sampleStartedAt = now;
            _presentedAtSampleStart = stats.PresentedFrames;
        }
        else
        {
            double elapsed = Stopwatch.GetElapsedTime(_sampleStartedAt, now).TotalSeconds;
            if (elapsed >= SAMPLE_SECONDS)
            {
                _presentedFramesPerSecond =
                    (stats.PresentedFrames - _presentedAtSampleStart) / elapsed;
                _sampleStartedAt = now;
                _presentedAtSampleStart = stats.PresentedFrames;
            }
        }

        return FormattableString.Invariant(
            $"presented {_presentedFramesPerSecond:0.0} fps   tick {context.Fps:0.0}   render {stats.CpuFrameMilliseconds:0.0} ms   draw calls {stats.DrawCalls}   instances {stats.Instances}"
        );
    }
}
