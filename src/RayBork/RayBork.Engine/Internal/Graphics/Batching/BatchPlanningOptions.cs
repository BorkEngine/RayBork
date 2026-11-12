namespace RayBork.Internal;

/// <summary>
/// The batch planner's split limits. A batch is split when it would exceed either limit.
/// </summary>
/// <remarks>
/// The defaults keep a single instanced draw comfortably inside the Vulkan storage-buffer and
/// descriptor limits: 16384 instances and 1 MiB of instance-record bytes. A single instance larger than
/// the byte limit is still submitted as its own batch, because splitting cannot make it smaller.
/// </remarks>
internal sealed class BatchPlanningOptions {
    /// <summary>The default maximum number of instances in one batch.</summary>
    internal const int DEFAULT_MAX_INSTANCES_PER_BATCH = 16384;

    /// <summary>The default maximum number of instance-record bytes in one batch (1 MiB).</summary>
    internal const int DEFAULT_MAX_INSTANCE_BYTES_PER_BATCH = 1024 * 1024;

    /// <summary>
    /// Initializes the default options.
    /// </summary>
    internal BatchPlanningOptions()
        : this(DEFAULT_MAX_INSTANCES_PER_BATCH, DEFAULT_MAX_INSTANCE_BYTES_PER_BATCH) {
    }

    /// <summary>
    /// Initializes custom split limits.
    /// </summary>
    /// <param name="maxInstancesPerBatch">The maximum instances per batch. It must be positive.</param>
    /// <param name="maxInstanceBytesPerBatch">
    /// The maximum instance-record bytes per batch, including the 96-byte instance header. It must be
    /// positive.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is zero or negative.</exception>
    internal BatchPlanningOptions(int maxInstancesPerBatch, int maxInstanceBytesPerBatch) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInstancesPerBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxInstanceBytesPerBatch);
        MaxInstancesPerBatch = maxInstancesPerBatch;
        MaxInstanceBytesPerBatch = maxInstanceBytesPerBatch;
    }

    /// <summary>Gets the shared default options.</summary>
    internal static BatchPlanningOptions Default { get; } = new();

    /// <summary>Gets the maximum number of instances in one batch.</summary>
    internal int MaxInstancesPerBatch { get; }

    /// <summary>Gets the maximum instance-record bytes in one batch, including the instance header.</summary>
    internal int MaxInstanceBytesPerBatch { get; }
}
