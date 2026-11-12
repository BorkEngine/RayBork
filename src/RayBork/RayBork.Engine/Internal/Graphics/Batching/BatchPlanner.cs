using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Builds deterministic per-pass batch plans from a sealed <see cref="RenderFramePacket"/>.
/// </summary>
/// <remarks>
/// <para>
/// The batch key is <c>(pass, mesh, shader, effective render state, topology)</c>. Uniform values and
/// texture bindings are per-instance data and never split a batch. Within one camera pass every
/// instance is frustum-culled against the pass view-projection using the mesh-space bounds transformed
/// to a world-space AABB; a draw whose record was recorded with a model part transform already carries
/// the combined world matrix, so the same test covers every draw kind.
/// </para>
/// <para>
/// Submission order is stable. Opaque batches keep the order in which their first instance was
/// recorded. Alpha, additive, and multiply batches are ordered back-to-front by their nearest instance
/// depth, and the instances inside them are sorted back-to-front; equal depths keep their recording
/// order. A pass never reorders into another pass, so cameras stay sequential. A batch that exceeds
/// <see cref="BatchPlanningOptions.MaxInstancesPerBatch"/> or
/// <see cref="BatchPlanningOptions.MaxInstanceBytesPerBatch"/> is split into several batches that
/// preserve the instance order and count as separate draw calls.
/// </para>
/// <para>
/// The planner allocates per-frame lists; it does not allocate or box per instance. The plan references
/// the packet's pooled records, so it is valid only while the render thread holds the frame slot.
/// </para>
/// </remarks>
internal sealed class BatchPlanner {
    private readonly BatchPlanningOptions _options;

    /// <summary>
    /// Initializes a planner with the default split limits.
    /// </summary>
    internal BatchPlanner()
        : this(BatchPlanningOptions.Default) {
    }

    /// <summary>
    /// Initializes a planner with custom split limits.
    /// </summary>
    /// <param name="options">The split limits. The planner keeps the instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <c>null</c>.</exception>
    internal BatchPlanner(BatchPlanningOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Gets the split limits used by the planner.</summary>
    internal BatchPlanningOptions Options => _options;

    /// <summary>
    /// Plans one sealed frame.
    /// </summary>
    /// <param name="frame">The sealed frame packet.</param>
    /// <returns>The per-pass plans and submission statistics.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is <c>null</c>.</exception>
    internal FramePlan Plan(RenderFramePacket frame) {
        ArgumentNullException.ThrowIfNull(frame);

        List<PassPlan> passes = new(frame.PassCount);
        int drawCalls = 0;
        int instances = 0;
        int triangles = 0;
        int culledInstances = 0;
        foreach (CameraPass pass in frame.Passes) {
            PassPlan passPlan = PlanPass(
                pass,
                out int passDrawCalls,
                out int passInstances,
                out int passTriangles,
                out int passCulledInstances);
            passes.Add(passPlan);
            drawCalls += passDrawCalls;
            instances += passInstances;
            triangles += passTriangles;
            culledInstances += passCulledInstances;
        }

        return new FramePlan(frame.FrameIndex, passes, drawCalls, instances, triangles, culledInstances);
    }

    private PassPlan PlanPass(
        CameraPass pass,
        out int drawCalls,
        out int instances,
        out int triangles,
        out int culledInstances) {
        BoundingFrustum frustum = new(pass.ViewProjection);
        List<PendingBatch> pending = [];
        Dictionary<BatchKey, PendingBatch> batchesByKey = [];
        culledInstances = 0;

        foreach (RenderDrawRecord record in pass.Draws) {
            int bytesPerInstance = ShaderAbiLayout.INSTANCE_HEADER_SIZE + record.InstanceTemplate.Length;
            int trianglesPerInstance = CountTriangles(record.Topology, record.IndexCount);
            ReadOnlySpan<Matrix4x4> worldMatrices = record.GetWorldMatrices();
            for (int instanceIndex = 0; instanceIndex < worldMatrices.Length; instanceIndex++) {
                Matrix4x4 worldMatrix = worldMatrices[instanceIndex];
                if (!frustum.Intersects(TransformBounds(record.MeshBounds, worldMatrix))) {
                    culledInstances++;
                    continue;
                }

                BatchKey key = new(record.Mesh, record.Shader, record.RenderState, record.Topology);
                if (!batchesByKey.TryGetValue(key, out PendingBatch? batch)) {
                    batch = new PendingBatch(key, bytesPerInstance, trianglesPerInstance);
                    batchesByKey.Add(key, batch);
                    pending.Add(batch);
                }

                batch.Instances.Add(new PlannedInstance(
                    record,
                    instanceIndex,
                    ComputeDepth(pass.View, worldMatrix)));
            }
        }

        List<PlannedBatch> opaque = [];
        List<PlannedBatch> transparent = [];
        drawCalls = 0;
        instances = 0;
        triangles = 0;
        foreach (PendingBatch batch in pending) {
            List<PlannedInstance> ordered = batch.IsOpaque
                ? batch.Instances
                : [.. batch.Instances.OrderByDescending(static instance => instance.Depth)];
            int start = 0;
            while (start < ordered.Count) {
                int count = DetermineChunkSize(ordered.Count - start, batch.BytesPerInstance);
                List<PlannedInstance> chunk = ordered.GetRange(start, count);
                float nearestDepth = float.PositiveInfinity;
                foreach (PlannedInstance instance in chunk) {
                    nearestDepth = MathF.Min(nearestDepth, instance.Depth);
                }

                PlannedBatch planned = new(
                    batch.Key.Mesh,
                    batch.Key.Shader,
                    batch.Key.RenderState,
                    batch.Key.Topology,
                    chunk,
                    nearestDepth);
                if (batch.IsOpaque) {
                    opaque.Add(planned);
                } else {
                    transparent.Add(planned);
                }

                drawCalls++;
                instances += count;
                triangles += batch.TrianglesPerInstance * count;
                start += count;
            }
        }

        List<PlannedBatch> submissionOrder = new(opaque.Count + transparent.Count);
        submissionOrder.AddRange(opaque);
        if (transparent.Count > 1) {
            // LINQ's ordering is stable, so equal nearest depths keep first-record order.
            submissionOrder.AddRange(transparent.OrderByDescending(static batch => batch.NearestDepth));
        } else {
            submissionOrder.AddRange(transparent);
        }

        return new PassPlan(pass, submissionOrder);
    }

    private int DetermineChunkSize(int remaining, int bytesPerInstance) {
        if (bytesPerInstance >= _options.MaxInstanceBytesPerBatch) {
            // A single instance that exceeds the byte limit cannot be split further.
            return 1;
        }

        int maxByBytes = _options.MaxInstanceBytesPerBatch / bytesPerInstance;
        return Math.Min(remaining, Math.Min(_options.MaxInstancesPerBatch, maxByBytes));
    }

    private static int CountTriangles(PrimitiveTopology topology, int indexCount) => topology switch {
        PrimitiveTopology.TriangleList => indexCount / 3,
        PrimitiveTopology.TriangleStrip => Math.Max(indexCount - 2, 0),
        _ => 0,
    };

    private static BoundingBox TransformBounds(BoundingBox bounds, Matrix4x4 worldMatrix) {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++) {
            Vector3 corner = new(
                (cornerIndex & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (cornerIndex & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (cornerIndex & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            Vector3 transformed = Vector3.Transform(corner, worldMatrix);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }

        return new BoundingBox(minimum, maximum);
    }

    private static float ComputeDepth(Matrix4x4 view, Matrix4x4 worldMatrix) {
        Vector3 worldPosition = new(worldMatrix.M41, worldMatrix.M42, worldMatrix.M43);
        return -Vector3.Transform(worldPosition, view).Z;
    }

    private readonly record struct BatchKey(
        EngineAsset Mesh,
        EngineAsset Shader,
        RenderState RenderState,
        PrimitiveTopology Topology);

    private sealed class PendingBatch {
        internal PendingBatch(BatchKey key, int bytesPerInstance, int trianglesPerInstance) {
            Key = key;
            BytesPerInstance = bytesPerInstance;
            TrianglesPerInstance = trianglesPerInstance;
        }

        internal int BytesPerInstance { get; }

        internal int TrianglesPerInstance { get; }

        internal BatchKey Key { get; }

        internal List<PlannedInstance> Instances { get; } = [];

        internal bool IsOpaque => Key.RenderState.Blend == BlendMode.Opaque;
    }
}
