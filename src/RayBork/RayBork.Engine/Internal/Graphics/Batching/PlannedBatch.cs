namespace RayBork.Internal;

/// <summary>One submission-ready batch sharing mesh, program, render state, topology, and polygon
/// mode.</summary>
/// <remarks>Batches follow pass submission order, and split batches preserve instance order.</remarks>
internal sealed class PlannedBatch
{
    private readonly List<PlannedInstance> _instances;

    /// <summary>Initializes a planned batch.</summary>
    /// <param name="mesh">The mesh asset shared by every instance.</param>
    /// <param name="program">The internal shader program asset shared by every instance.</param>
    /// <param name="renderState">The effective render state shared by every instance.</param>
    /// <param name="topology">The mesh topology.</param>
    /// <param name="wireframe">Whether the batch uses the polygon-mode-line pipeline.</param>
    /// <param name="instances">The ordered instances, already in submission order.</param>
    /// <param name="nearestDepth">The depth of the nearest instance in the batch.</param>
    /// <exception cref="ArgumentNullException">An asset or instance list is <c>null</c>.</exception>
    internal PlannedBatch(
        EngineAsset mesh,
        EngineAsset program,
        RenderState renderState,
        PrimitiveTopology topology,
        bool wireframe,
        List<PlannedInstance> instances,
        float nearestDepth
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(instances);

        Mesh = mesh;
        Program = program;
        RenderState = renderState;
        Topology = topology;
        Wireframe = wireframe;
        _instances = instances;
        NearestDepth = nearestDepth;
    }

    /// <summary>Gets the mesh asset; read its GPU resource from the asset lifetime for the draw.</summary>
    internal EngineAsset Mesh { get; }

    /// <summary>Gets the internal shader program asset shared by the batch.</summary>
    internal EngineAsset Program { get; }

    /// <summary>Gets the effective render state shared by the batch.</summary>
    internal RenderState RenderState { get; }

    /// <summary>Gets the mesh topology.</summary>
    internal PrimitiveTopology Topology { get; }

    /// <summary>Gets whether the batch uses the polygon-mode-line pipeline instead of the filled one.</summary>
    internal bool Wireframe { get; }

    /// <summary>Gets the number of instances in this batch.</summary>
    internal int InstanceCount => _instances.Count;

    /// <summary>Gets the instances in submission order.</summary>
    internal IReadOnlyList<PlannedInstance> Instances => _instances;

    /// <summary>Gets the depth of the nearest instance, used to order transparent batches.</summary>
    internal float NearestDepth { get; }

    /// <summary>Gets whether the batch uses the opaque blend preset.</summary>
    internal bool IsOpaque => RenderState.Blend == BlendMode.Opaque;
}
