using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The immutable CPU geometry of one engine primitive mesh: the vertex and index arrays, the
/// primitive topology, and the mesh-space bounds.
/// </summary>
/// <remarks>
/// <see cref="PrimitiveMeshFactory"/> creates new arrays on every call, so two builds never share
/// mutable state. <see cref="PrimitiveAssets"/> copies the arrays into one <see cref="MeshBuilder"/>
/// per mesh, and the builder snapshots them for the built asset.
/// </remarks>
/// <param name="Vertices">The vertices in mesh order.</param>
/// <param name="Indices">The 32-bit indices in primitive order.</param>
/// <param name="Topology">The primitive topology that describes the index list.</param>
/// <param name="Bounds">The mesh-space bounds computed from <paramref name="Vertices"/>.</param>
internal readonly record struct PrimitiveMeshData(
    Vertex[] Vertices,
    uint[] Indices,
    PrimitiveTopology Topology,
    BoundingBox Bounds);
