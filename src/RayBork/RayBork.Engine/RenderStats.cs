namespace RayBork;

/// <summary>
/// A snapshot of the last completed frame's render statistics.
/// </summary>
/// <remarks>
/// The render thread writes the snapshot and the tick thread reads it through
/// <see cref="EngineContext.Stats"/>. It describes the last completed frame, not the frame currently
/// being recorded, and its fields are not a consistent transactional snapshot. Use it for
/// diagnostics and overlays, not for synchronization.
/// </remarks>
/// <param name="FrameIndex">The tick frame this snapshot describes.</param>
/// <param name="DrawCalls">The number of draw commands submitted for the frame.</param>
/// <param name="Instances">The number of instances that survived culling and were submitted.</param>
/// <param name="Triangles">The submitted triangle count after instancing.</param>
/// <param name="CulledInstances">The number of instances rejected by frustum culling.</param>
/// <param name="PipelineCount">The number of distinct pipelines used by the frame.</param>
/// <param name="TextureCount">The number of bindless texture slots live after the frame.</param>
/// <param name="SampleCount">The effective MSAA sample count for the frame.</param>
/// <param name="HdrOutput">
/// Whether the frame presented through an HDR-capable swapchain format.
/// </param>
/// <param name="CpuFrameMilliseconds">
/// The render-thread CPU time spent planning and recording the frame.
/// </param>
/// <param name="GpuFrameMilliseconds">
/// The GPU timestamp delta for the frame, or <c>0</c> when timestamps are unavailable.
/// </param>
/// <param name="FramesInFlight">
/// The number of frames submitted but not yet retired at snapshot time.
/// </param>
/// <param name="RecoveryCount">
/// The number of device and swapchain recoveries completed since startup.
/// </param>
public readonly record struct RenderStats(
    long FrameIndex,
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
    int RecoveryCount);
