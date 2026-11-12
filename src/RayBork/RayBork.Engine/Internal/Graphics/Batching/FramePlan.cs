namespace RayBork.Internal;

/// <summary>
/// The deterministic batch plan for one sealed frame, with its submission statistics.
/// </summary>
/// <remarks>
/// The plan is built on the render thread from a sealed <see cref="RenderFramePacket"/>. It stays valid
/// only until the render thread releases the frame slot: its batches and instances reference the
/// packet's pooled draw records, not copies. <see cref="DrawCalls"/> counts submissions after batch
/// splitting; <see cref="Triangles"/> counts triangle-list and triangle-strip primitives and is <c>0</c>
/// for line and point topologies.
/// </remarks>
internal sealed class FramePlan {
    private readonly List<PassPlan> _passes;

    /// <summary>
    /// Initializes a frame plan.
    /// </summary>
    /// <param name="frameIndex">The planned frame index.</param>
    /// <param name="passes">The pass plans in camera creation order.</param>
    /// <param name="drawCalls">The number of submissions after batch splitting.</param>
    /// <param name="instances">The number of instances submitted.</param>
    /// <param name="triangles">The number of triangles submitted.</param>
    /// <param name="culledInstances">The number of instances removed by frustum culling.</param>
    /// <exception cref="ArgumentNullException"><paramref name="passes"/> is <c>null</c>.</exception>
    internal FramePlan(
        long frameIndex,
        List<PassPlan> passes,
        int drawCalls,
        int instances,
        int triangles,
        int culledInstances) {
        ArgumentNullException.ThrowIfNull(passes);

        FrameIndex = frameIndex;
        _passes = passes;
        DrawCalls = drawCalls;
        Instances = instances;
        Triangles = triangles;
        CulledInstances = culledInstances;
    }

    /// <summary>Gets the planned frame index.</summary>
    internal long FrameIndex { get; }

    /// <summary>Gets the pass plans in camera creation order.</summary>
    internal IReadOnlyList<PassPlan> Passes => _passes;

    /// <summary>Gets the number of draw calls after batch splitting.</summary>
    internal int DrawCalls { get; }

    /// <summary>Gets the number of instances submitted after culling.</summary>
    internal int Instances { get; }

    /// <summary>Gets the number of triangles submitted.</summary>
    internal int Triangles { get; }

    /// <summary>Gets the number of instances removed by frustum culling.</summary>
    internal int CulledInstances { get; }
}
