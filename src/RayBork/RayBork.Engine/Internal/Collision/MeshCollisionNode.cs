using System.Runtime.InteropServices;

namespace RayBork.Internal;

/// <summary>
/// One node of the mesh collision BVH: bounds, then child indices or a triangle range.
/// </summary>
/// <remarks>
/// A 32-byte node is a leaf when <see cref="Count"/> is positive; otherwise its children are the
/// adjacent nodes <see cref="LeftOrFirst"/> and <see cref="LeftOrFirst"/> + 1.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MeshCollisionNode
{
    /// <summary>Gets the node bounds in mesh space.</summary>
    internal BoundingBox Bounds { get; }

    /// <summary>The first child index for internal nodes, or a leaf's first triangle id.</summary>
    internal int LeftOrFirst { get; }

    /// <summary>Zero for an internal node, else the leaf triangle count.</summary>
    internal int Count { get; }

    /// <summary>Initializes a node from its bounds and its child or triangle range.</summary>
    internal MeshCollisionNode(BoundingBox bounds, int leftOrFirst, int count)
    {
        Bounds = bounds;
        LeftOrFirst = leftOrFirst;
        Count = count;
    }
}
