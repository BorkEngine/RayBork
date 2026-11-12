namespace RayBork.Internal;

/// <summary>
/// The submission plan for one camera pass.
/// </summary>
/// <remarks>
/// <see cref="Pass"/> carries the pass value snapshot (matrices, camera position, viewport, clear mode,
/// and clear color). <see cref="Batches"/> is in submission order: opaque batches first in
/// first-record order, then transparent batches sorted back-to-front.
/// </remarks>
internal sealed class PassPlan {
    private readonly List<PlannedBatch> _batches;

    /// <summary>
    /// Initializes a pass plan.
    /// </summary>
    /// <param name="pass">The recorded pass the plan consumes.</param>
    /// <param name="batches">The batches in submission order.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal PassPlan(CameraPass pass, List<PlannedBatch> batches) {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(batches);

        Pass = pass;
        _batches = batches;
    }

    /// <summary>Gets the recorded pass value snapshot.</summary>
    internal CameraPass Pass { get; }

    /// <summary>Gets the batches in submission order.</summary>
    internal IReadOnlyList<PlannedBatch> Batches => _batches;
}
