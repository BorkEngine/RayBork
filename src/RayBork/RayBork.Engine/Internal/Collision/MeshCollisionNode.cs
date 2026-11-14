using System.Runtime.InteropServices;

namespace RayBork.Internal;

/// <summary>
/// One node of the mesh collision BVH: an axis-aligned bound plus either two child indices or a leaf
/// triangle range.
/// </summary>
/// <remarks>
/// <para>
/// The layout is fixed at 32 bytes: a 24-byte <see cref="BoundingBox"/> followed by two 32-bit
/// integers. A node is a leaf when <see cref="Count"/> is positive; otherwise it is an internal node
/// whose two children are the adjacent node indices <see cref="LeftOrFirst"/> and
/// <see cref="LeftOrFirst"/> + 1. The builder fixes that adjacency so a 32-byte node carries both
/// children without a second index.
/// </para>
/// <para>
/// A node is immutable. <see cref="MeshCollisionData"/> validates every index and bound before it
/// exposes nodes to a query.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MeshCollisionNode {
    /// <summary>Gets the node bounds in mesh space.</summary>
    internal BoundingBox Bounds { get; }

    /// <summary>
    /// Gets the index of the first child for an internal node, or the first index into the collision
    /// triangle-id array for a leaf.
    /// </summary>
    internal int LeftOrFirst { get; }

    /// <summary>
    /// Gets zero for an internal node, or the number of triangles stored from
    /// <see cref="LeftOrFirst"/> for a leaf.
    /// </summary>
    internal int Count { get; }

    /// <summary>Initializes a node from its bounds and its child or triangle range.</summary>
    /// <param name="bounds">The node bounds in mesh space.</param>
    /// <param name="leftOrFirst">
    /// The first child index for an internal node, or the first triangle-id index for a leaf.
    /// </param>
    /// <param name="count">Zero for an internal node, or the leaf triangle count.</param>
    internal MeshCollisionNode(BoundingBox bounds, int leftOrFirst, int count) {
        Bounds = bounds;
        LeftOrFirst = leftOrFirst;
        Count = count;
    }
}
