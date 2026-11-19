namespace RayBork;

/// <summary>A snapshot of the last completed frame's render statistics.</summary>
/// <remarks>
/// The render thread writes the snapshot and the tick thread reads it through
/// <see cref="EngineContext.Stats"/>. It describes the last completed frame, is not transactional,
/// and is meant for diagnostics, not synchronization.
/// </remarks>
/// <param name="FrameIndex">The tick frame described by this snapshot.</param>
/// <param name="PresentedFrames">The number of successful presentations since startup.</param>
/// <param name="DrawCalls">The number of draw commands submitted.</param>
/// <param name="Instances">The submitted instances that survived culling.</param>
/// <param name="Triangles">The submitted triangle count after instancing.</param>
/// <param name="CulledInstances">The instances rejected by frustum culling.</param>
/// <param name="PipelineCount">The number of distinct pipelines used.</param>
/// <param name="TextureCount">The number of live bindless texture slots after the frame.</param>
/// <param name="SampleCount">The effective MSAA sample count.</param>
/// <param name="HdrOutput">Whether the frame used an HDR-capable swapchain format.</param>
/// <param name="CpuFrameMilliseconds">Render-thread CPU time, excluding waits.</param>
/// <param name="GpuFrameMilliseconds">The GPU timestamp delta, or <c>0</c> if unavailable.</param>
/// <param name="FramesInFlight">The submitted frames not yet retired.</param>
/// <param name="RecoveryCount">The device and swapchain recoveries since startup.</param>
public readonly record struct RenderStats(
    long FrameIndex,
    long PresentedFrames,
    int DrawCalls,
    int Instances,
    int Triangles,
    int CulledInstances,
    int PipelineCount,
    int TextureCount,
    int SampleCount,
    bool HdrOutput,
    double CpuFrameMilliseconds,
    double GpuFrameMilliseconds,
    int FramesInFlight,
    int RecoveryCount
);
