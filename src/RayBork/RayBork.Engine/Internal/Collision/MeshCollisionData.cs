using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The immutable mesh-space triangle BVH that accelerates ray/mesh collision queries.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MeshCollisionBuilder"/> creates one instance per mesh from a geometry snapshot. The
/// node array is a full binary tree: node zero is the root, a leaf stores a range of the triangle-id
/// array, and an internal node stores the adjacent indices of its two children. The triangle ids are
/// canonical triangle positions: a triangle list counts groups of three indices, and a triangle strip
/// counts the sliding windows whose first two indices are swapped on odd ids.
/// </para>
/// <para>
/// An instance is immutable after construction. Queries allocate nothing beyond a <c>stackalloc</c>
/// traversal stack, take no locks, and are safe to call from any thread. The builder and the
/// packed-payload decoder are the only producers; the constructor validates every array so a corrupt
/// decoded payload is rejected with <see cref="ArgumentException"/> before a query can see it.
/// </para>
/// </remarks>
internal sealed class MeshCollisionData {
    /// <summary>The deepest node level the builder creates; every node at this depth is a leaf.</summary>
    internal const int MAX_DEPTH = 64;

    private const int TRAVERSAL_STACK_CAPACITY = 2 * (MAX_DEPTH + 1);

    private readonly MeshCollisionNode[] _nodes;
    private readonly int[] _triangleIds;
    private readonly PrimitiveTopology _topology;
    private readonly int _triangleCount;

    /// <summary>
    /// Initializes one collision structure from freshly built arrays. The constructor takes ownership
    /// of <paramref name="nodes"/> and <paramref name="triangleIds"/> without copying them.
    /// </summary>
    /// <param name="nodes">The binary tree in breadth-independent build order; node zero is the root.</param>
    /// <param name="triangleIds">The canonical triangle ids in leaf range order.</param>
    /// <param name="topology">The triangle topology that describes the mesh indices.</param>
    /// <param name="triangleCount">
    /// The canonical triangle count, including degenerate triangles that the builder omitted.
    /// </param>
    /// <exception cref="ArgumentNullException">An array is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// The topology is not a triangle topology, the tree shape is inconsistent, an index is out of
    /// range, a triangle id is outside the canonical range, a node is deeper than
    /// <see cref="MAX_DEPTH"/>, or a bound is non-finite or inverted.
    /// </exception>
    internal MeshCollisionData(
        MeshCollisionNode[] nodes,
        int[] triangleIds,
        PrimitiveTopology topology,
        int triangleCount) {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(triangleIds);

        if (topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw new ArgumentException(
                "Ray-mesh collision supports only triangle topologies; the collision topology is "
                    + $"{topology}.",
                nameof(topology));
        }

        if (triangleCount <= 0) {
            throw new ArgumentException("The canonical triangle count must be positive.", nameof(triangleCount));
        }

        if (nodes.Length == 0 || (nodes.Length & 1) == 0) {
            throw new ArgumentException(
                $"A collision tree has a positive odd node count; the tree has {nodes.Length}.",
                nameof(nodes));
        }

        long leafTriangleCount = 0L;
        for (int index = 0; index < nodes.Length; index++) {
            MeshCollisionNode node = nodes[index];
            ValidateBounds(node.Bounds, index);
            if (node.Count < 0) {
                throw new ArgumentException(
                    $"Collision node {index} has a negative count {node.Count}.",
                    nameof(nodes));
            }

            if (node.Count == 0) {
                if (node.LeftOrFirst < 0 || node.LeftOrFirst >= nodes.Length - 1) {
                    throw new ArgumentException(
                        $"Collision node {index} has an out-of-range child index {node.LeftOrFirst}.",
                        nameof(nodes));
                }
            } else {
                if (node.LeftOrFirst < 0 || node.Count > triangleIds.Length - node.LeftOrFirst) {
                    throw new ArgumentException(
                        $"Collision node {index} has an out-of-range triangle range "
                            + $"[{node.LeftOrFirst}, {node.LeftOrFirst + node.Count}).",
                        nameof(nodes));
                }

                leafTriangleCount += node.Count;
            }
        }

        if (leafTriangleCount != triangleIds.Length) {
            throw new ArgumentException(
                $"The leaves describe {leafTriangleCount} triangles; the triangle-id array has "
                    + $"{triangleIds.Length} entries.",
                nameof(triangleIds));
        }

        ValidateTriangleIds(triangleIds, triangleCount);
        ValidateTree(nodes);
        _nodes = nodes;
        _triangleIds = triangleIds;
        _topology = topology;
        _triangleCount = triangleCount;
    }

    /// <summary>Gets the nodes of the binary tree; node zero is the root.</summary>
    internal ReadOnlySpan<MeshCollisionNode> Nodes => _nodes;

    /// <summary>Gets the canonical triangle ids in leaf range order.</summary>
    internal ReadOnlySpan<int> TriangleIds => _triangleIds;

    /// <summary>Gets the triangle topology of the mesh that produced the structure.</summary>
    internal PrimitiveTopology Topology => _topology;

    /// <summary>
    /// Gets the canonical triangle count, including degenerate triangles that the builder omitted
    /// from the tree.
    /// </summary>
    internal int TriangleCount => _triangleCount;

    /// <summary>Gets the mesh-space bounds of the root node.</summary>
    internal BoundingBox Bounds => _nodes[0].Bounds;

    /// <summary>
    /// Gets the canonical triangle count that an index list of the given topology describes.
    /// </summary>
    /// <param name="topology">The primitive topology.</param>
    /// <param name="indexCount">The number of 32-bit indices.</param>
    /// <returns>The number of triangle windows, including degenerate triangles; zero for a non-triangle topology.</returns>
    internal static int GetTriangleCount(PrimitiveTopology topology, int indexCount) => topology switch {
        PrimitiveTopology.TriangleList => indexCount / 3,
        PrimitiveTopology.TriangleStrip => indexCount >= 3 ? indexCount - 2 : 0,
        _ => 0,
    };

    /// <summary>
    /// Gets the three vertex indices of one canonical triangle. A triangle strip swaps the first two
    /// indices on an odd triangle id, matching the builder and the brute-force query.
    /// </summary>
    /// <param name="topology">The primitive topology.</param>
    /// <param name="indices">The mesh's 32-bit indices.</param>
    /// <param name="triangleId">The canonical triangle position.</param>
    /// <param name="index0">The first vertex index.</param>
    /// <param name="index1">The second vertex index.</param>
    /// <param name="index2">The third vertex index.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="triangleId"/> describes a whole triangle in
    /// <paramref name="indices"/>; otherwise <c>false</c>.
    /// </returns>
    internal static bool TryGetTriangleIndices(
        PrimitiveTopology topology,
        ReadOnlySpan<uint> indices,
        int triangleId,
        out uint index0,
        out uint index1,
        out uint index2) {
        index0 = 0;
        index1 = 0;
        index2 = 0;
        if (triangleId < 0) {
            return false;
        }

        if (topology == PrimitiveTopology.TriangleList) {
            if (triangleId > (indices.Length / 3) - 1) {
                return false;
            }

            int offset = triangleId * 3;
            index0 = indices[offset];
            index1 = indices[offset + 1];
            index2 = indices[offset + 2];
            return true;
        } else if (topology == PrimitiveTopology.TriangleStrip) {
            if (triangleId > indices.Length - 3) {
                return false;
            }

            index0 = indices[triangleId];
            index1 = indices[triangleId + 1];
            index2 = indices[triangleId + 2];
            if ((triangleId & 1) != 0) {
                (index0, index1) = (index1, index0);
            }

            return true;
        } else {
            return false;
        }
    }

    /// <summary>
    /// Finds the nearest triangle of the structure that a mesh-space ray hits.
    /// </summary>
    /// <param name="meshRay">The ray in mesh space.</param>
    /// <param name="vertices">The mesh vertices.</param>
    /// <param name="indices">The mesh's 32-bit indices.</param>
    /// <param name="triangleId">The canonical id of the nearest triangle, or -1 on a miss.</param>
    /// <param name="distance">The mesh-space ray parameter of the nearest hit, or zero on a miss.</param>
    /// <returns><c>true</c> when the ray hits a triangle; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The traversal visits the nearer child first and stops descending into a node whose entry is
    /// farther than the best hit found so far. A miss or a non-finite ray allocates no managed memory
    /// and returns <c>false</c>. Ties keep the lowest canonical triangle id, matching the brute-force
    /// scan order.
    /// </remarks>
    internal bool TryFindNearest(
        Ray meshRay,
        ReadOnlySpan<Vertex> vertices,
        ReadOnlySpan<uint> indices,
        out int triangleId,
        out float distance) =>
        TryFindNearest(meshRay, vertices, indices, out triangleId, out distance, out _);

    /// <summary>
    /// Finds the nearest triangle and reports how many triangle intersections the traversal tested.
    /// The count exists for deterministic test assertions; normal callers use the shorter overload.
    /// </summary>
    /// <param name="meshRay">The ray in mesh space.</param>
    /// <param name="vertices">The mesh vertices.</param>
    /// <param name="indices">The mesh's 32-bit indices.</param>
    /// <param name="triangleId">The canonical id of the nearest triangle, or -1 on a miss.</param>
    /// <param name="distance">The mesh-space ray parameter of the nearest hit, or zero on a miss.</param>
    /// <param name="triangleTests">The number of triangle intersections the traversal tested.</param>
    /// <returns><c>true</c> when the ray hits a triangle; otherwise <c>false</c>.</returns>
    internal bool TryFindNearest(
        Ray meshRay,
        ReadOnlySpan<Vertex> vertices,
        ReadOnlySpan<uint> indices,
        out int triangleId,
        out float distance,
        out int triangleTests) {
        triangleId = -1;
        distance = 0f;
        triangleTests = 0;
        if (!IsFinite(meshRay)) {
            return false;
        }

        Span<int> stack = stackalloc int[TRAVERSAL_STACK_CAPACITY];
        int stackCount = 0;
        stack[stackCount++] = 0;
        float bestDistance = float.PositiveInfinity;
        int bestTriangle = int.MaxValue;
        while (stackCount > 0) {
            int nodeIndex = stack[--stackCount];
            MeshCollisionNode node = _nodes[nodeIndex];
            if (node.Count > 0) {
                for (int index = 0; index < node.Count; index++) {
                    int candidate = _triangleIds[node.LeftOrFirst + index];
                    if (!TryGetTriangleIndices(
                            _topology,
                            indices,
                            candidate,
                            out uint index0,
                            out uint index1,
                            out uint index2)) {
                        continue;
                    }

                    triangleTests++;
                    if (!Collision.TryGetTriangleIntersection(
                            meshRay,
                            vertices[(int)index0].Position,
                            vertices[(int)index1].Position,
                            vertices[(int)index2].Position,
                            out float hitDistance,
                            out _)) {
                        continue;
                    }

                    if (hitDistance < bestDistance
                        || (hitDistance == bestDistance && candidate < bestTriangle)) {
                        bestDistance = hitDistance;
                        bestTriangle = candidate;
                        triangleId = candidate;
                        distance = hitDistance;
                    }
                }

                continue;
            }

            int leftIndex = node.LeftOrFirst;
            int rightIndex = leftIndex + 1;
            bool hitLeft = TryGetBoxEntry(meshRay, _nodes[leftIndex].Bounds, bestDistance, out float leftEntry);
            bool hitRight = TryGetBoxEntry(meshRay, _nodes[rightIndex].Bounds, bestDistance, out float rightEntry);
            if (hitLeft && hitRight) {
                // Push the farther child first so the nearer child is visited next.
                if (leftEntry <= rightEntry) {
                    stack[stackCount++] = rightIndex;
                    stack[stackCount++] = leftIndex;
                } else {
                    stack[stackCount++] = leftIndex;
                    stack[stackCount++] = rightIndex;
                }
            } else if (hitLeft) {
                stack[stackCount++] = leftIndex;
            } else if (hitRight) {
                stack[stackCount++] = rightIndex;
            }
        }

        return bestTriangle != int.MaxValue;
    }

    private static void ValidateBounds(BoundingBox bounds, int index) {
        if (!IsFinite(bounds.Min) || !IsFinite(bounds.Max)) {
            throw new ArgumentException(
                $"Collision node {index} has a non-finite bound.",
                "nodes");
        }

        if (bounds.Min.X > bounds.Max.X || bounds.Min.Y > bounds.Max.Y || bounds.Min.Z > bounds.Max.Z) {
            throw new ArgumentException(
                $"Collision node {index} has an inverted bound.",
                "nodes");
        }
    }

    private static void ValidateTriangleIds(int[] triangleIds, int triangleCount) {
        int[] sorted = new int[triangleIds.Length];
        triangleIds.CopyTo(sorted, 0);
        Array.Sort(sorted);
        for (int index = 0; index < sorted.Length; index++) {
            int triangleId = sorted[index];
            if ((uint)triangleId >= (uint)triangleCount) {
                throw new ArgumentException(
                    $"The triangle id {triangleId} is outside the canonical range [0, {triangleCount}).",
                    nameof(triangleIds));
            }

            if (index > 0 && triangleId == sorted[index - 1]) {
                throw new ArgumentException(
                    $"The triangle id {triangleId} appears more than once.",
                    nameof(triangleIds));
            }
        }
    }

    private static void ValidateTree(MeshCollisionNode[] nodes) {
        int[] stack = new int[nodes.Length];
        int[] depths = new int[nodes.Length];
        bool[] visited = new bool[nodes.Length];
        int stackCount = 0;
        stack[stackCount] = 0;
        depths[stackCount] = 0;
        stackCount++;
        int visitedCount = 0;
        while (stackCount > 0) {
            stackCount--;
            int index = stack[stackCount];
            int depth = depths[stackCount];
            if (visited[index]) {
                throw new ArgumentException(
                    $"Collision node {index} is reached more than once.",
                    nameof(nodes));
            }

            visited[index] = true;
            visitedCount++;
            if (depth > MAX_DEPTH) {
                throw new ArgumentException(
                    $"Collision node {index} is deeper than the maximum depth {MAX_DEPTH}.",
                    nameof(nodes));
            }

            MeshCollisionNode node = nodes[index];
            if (node.Count == 0) {
                stack[stackCount] = node.LeftOrFirst;
                depths[stackCount] = depth + 1;
                stackCount++;
                stack[stackCount] = node.LeftOrFirst + 1;
                depths[stackCount] = depth + 1;
                stackCount++;
            }
        }

        if (visitedCount != nodes.Length) {
            throw new ArgumentException(
                $"The collision tree reaches {visitedCount} of {nodes.Length} nodes; every node must "
                    + "be reachable from the root exactly once.",
                nameof(nodes));
        }
    }

    private static bool TryGetBoxEntry(
        Ray ray,
        BoundingBox bounds,
        float maximumDistance,
        out float entry) {
        entry = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        if (!IntersectSlab(ray.Origin.X, ray.Direction.X, bounds.Min.X, bounds.Max.X, ref entry, ref exit)) {
            return false;
        }

        if (!IntersectSlab(ray.Origin.Y, ray.Direction.Y, bounds.Min.Y, bounds.Max.Y, ref entry, ref exit)) {
            return false;
        }

        if (!IntersectSlab(ray.Origin.Z, ray.Direction.Z, bounds.Min.Z, bounds.Max.Z, ref entry, ref exit)) {
            return false;
        }

        if (exit < 0f) {
            return false;
        }

        entry = entry < 0f ? 0f : entry;
        return entry <= maximumDistance;
    }

    private static bool IntersectSlab(
        float origin,
        float direction,
        float minimum,
        float maximum,
        ref float entry,
        ref float exit) {
        if (direction == 0f) {
            return origin >= minimum && origin <= maximum;
        }

        float inverse = 1f / direction;
        float near = (minimum - origin) * inverse;
        float far = (maximum - origin) * inverse;
        if (direction < 0f) {
            (near, far) = (far, near);
        }

        if (near > entry) {
            entry = near;
        }

        if (far < exit) {
            exit = far;
        }

        return entry <= exit;
    }

    private static bool IsFinite(Ray ray) => IsFinite(ray.Origin) && IsFinite(ray.Direction);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
