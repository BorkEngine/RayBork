namespace RayBork.Bake.Models;

/// <summary>
/// The vertices, indices, and topology of one source mesh primitive.
/// </summary>
internal sealed class MeshData {
    /// <summary>Initializes an empty mesh with one topology.</summary>
    /// <param name="topology">The primitive topology.</param>
    internal MeshData(PrimitiveTopology topology) => Topology = topology;

    /// <summary>Gets the mutable vertex list.</summary>
    internal List<MeshVertex> Vertices { get; } = [];

    /// <summary>Gets the mutable zero-based index list.</summary>
    internal List<uint> Indices { get; } = [];

    /// <summary>Gets the primitive topology.</summary>
    internal PrimitiveTopology Topology { get; }
}
