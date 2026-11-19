using System.Numerics;

namespace RayBork.Internal;

/// <summary>The immutable CPU geometry of one engine primitive mesh.</summary>
/// <remarks>The factory returns fresh arrays, and the asset builder snapshots them.</remarks>
/// <param name="Vertices">The vertices in mesh order.</param>
/// <param name="Indices">The 32-bit indices in primitive order.</param>
/// <param name="Topology">The topology that describes the index list.</param>
/// <param name="Bounds">The mesh-space bounds computed from <paramref name="Vertices"/>.</param>
internal readonly record struct PrimitiveMeshData(
    Vertex[] Vertices,
    uint[] Indices,
    PrimitiveTopology Topology,
    BoundingBox Bounds
);
