using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Builds the deterministic binary BVH that <see cref="MeshCollisionData"/> exposes for ray/mesh
/// collision queries.
/// </summary>
/// <remarks>
/// <para>
/// The builder enumerates the canonical triangles of a triangle list or strip exactly like
/// <see cref="Collision.GetRayMesh"/>: a list advances in groups of three, and a strip uses the
/// sliding window with the first two indices swapped on odd ids. Zero-area triangles are skipped. The
/// tree splits the largest centroid extent into 12 bins and chooses the binned surface-area heuristic
/// split with the lowest cost; a range that no bin split separates falls back to a median split on the
/// largest centroid axis, and the recursion forces a leaf at
/// <see cref="MeshCollisionData.MAX_DEPTH"/>. Leaves hold at most four triangles.
/// </para>
/// <para>
/// The build is pure CPU and deterministic: no randomness, clock, or environment state is read, and
/// two builds of the same geometry produce identical arrays. The work is done on caller-provided
/// spans and temporary arrays; the result is frozen into arrays before it is returned.
/// </para>
/// </remarks>
internal static class MeshCollisionBuilder {
    private const int BIN_COUNT = 12;
    private const int LEAF_SIZE = 4;

    private static readonly BoundingBox _emptyBounds = new(
        new Vector3(float.PositiveInfinity),
        new Vector3(float.NegativeInfinity));

    private static readonly CentroidComparer _centroidXComparer = new(0);
    private static readonly CentroidComparer _centroidYComparer = new(1);
    private static readonly CentroidComparer _centroidZComparer = new(2);

    /// <summary>
    /// Builds one collision structure from a geometry snapshot.
    /// </summary>
    /// <param name="positions">The mesh-space vertex positions, in vertex order.</param>
    /// <param name="indices">The 32-bit indices of the same snapshot.</param>
    /// <param name="topology">The triangle topology of the index list.</param>
    /// <returns>
    /// The frozen structure, or <c>null</c> when the snapshot has no non-degenerate triangle.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The topology is not a triangle topology, or an index is outside <paramref name="positions"/>.
    /// </exception>
    internal static MeshCollisionData? Build(
        ReadOnlySpan<Vector3> positions,
        ReadOnlySpan<uint> indices,
        PrimitiveTopology topology) {
        if (topology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw new ArgumentException(
                $"Ray-mesh collision supports only triangle topologies; the collision topology is {topology}.",
                nameof(topology));
        }

        int triangleCount = MeshCollisionData.GetTriangleCount(topology, indices.Length);
        if (triangleCount == 0) {
            return null;
        }

        BuildTriangle[] triangles = new BuildTriangle[triangleCount];
        int triangleTotal = 0;
        for (int triangleId = 0; triangleId < triangleCount; triangleId++) {
            if (!MeshCollisionData.TryGetTriangleIndices(
                    topology,
                    indices,
                    triangleId,
                    out uint index0,
                    out uint index1,
                    out uint index2)) {
                continue;
            }

            if (index0 >= (uint)positions.Length
                || index1 >= (uint)positions.Length
                || index2 >= (uint)positions.Length) {
                throw new ArgumentException(
                    $"Triangle {triangleId} uses an index outside the {positions.Length} positions.",
                    nameof(indices));
            }

            Vector3 a = positions[(int)index0];
            Vector3 b = positions[(int)index1];
            Vector3 c = positions[(int)index2];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (!(normal.LengthSquared() > 0f)) {
                continue;
            }

            triangles[triangleTotal++] = new BuildTriangle(triangleId, a, b, c);
        }

        if (triangleTotal == 0) {
            return null;
        }

        if (triangleTotal < triangles.Length) {
            Array.Resize(ref triangles, triangleTotal);
        }

        BuildTriangle[] scratch = new BuildTriangle[triangleTotal];
        List<MeshCollisionNode> nodes = new((2 * triangleTotal) - 1);
        nodes.Add(default);
        BuildNode(triangles, scratch, 0, triangleTotal, 0, 0, nodes);

        MeshCollisionNode[] nodeArray = [.. nodes];
        int[] triangleIds = new int[triangleTotal];
        for (int index = 0; index < triangleTotal; index++) {
            triangleIds[index] = triangles[index].TriangleId;
        }

        return new MeshCollisionData(nodeArray, triangleIds, topology, triangleCount);
    }

    private static void BuildNode(
        BuildTriangle[] triangles,
        BuildTriangle[] scratch,
        int start,
        int count,
        int depth,
        int nodeIndex,
        List<MeshCollisionNode> nodes) {
        BoundingBox bounds = triangles[start].Bounds;
        Vector3 centroidMin = triangles[start].Centroid;
        Vector3 centroidMax = centroidMin;
        for (int index = start + 1; index < start + count; index++) {
            BuildTriangle triangle = triangles[index];
            bounds = Union(bounds, triangle.Bounds);
            centroidMin = Vector3.Min(centroidMin, triangle.Centroid);
            centroidMax = Vector3.Max(centroidMax, triangle.Centroid);
        }

        if (count <= LEAF_SIZE || depth >= MeshCollisionData.MAX_DEPTH) {
            nodes[nodeIndex] = new MeshCollisionNode(bounds, start, count);
            return;
        }

        if (!TrySplitByBins(triangles, scratch, start, count, centroidMin, centroidMax, out int split)) {
            split = SplitByCentroid(triangles, start, count, centroidMin, centroidMax);
        }

        int leftIndex = nodes.Count;
        nodes.Add(default);
        nodes.Add(default);
        BuildNode(triangles, scratch, start, split - start, depth + 1, leftIndex, nodes);
        BuildNode(triangles, scratch, split, start + count - split, depth + 1, leftIndex + 1, nodes);
        nodes[nodeIndex] = new MeshCollisionNode(bounds, leftIndex, 0);
    }

    private static bool TrySplitByBins(
        BuildTriangle[] triangles,
        BuildTriangle[] scratch,
        int start,
        int count,
        Vector3 centroidMin,
        Vector3 centroidMax,
        out int split) {
        split = 0;
        int axis = GetLargestCentroidAxis(centroidMin, centroidMax, out float extent);
        if (!(extent > 0f)) {
            return false;
        }

        float axisMinimum = axis switch {
            0 => centroidMin.X,
            1 => centroidMin.Y,
            _ => centroidMin.Z,
        };
        Span<int> binCounts = stackalloc int[BIN_COUNT];
        Span<BoundingBox> binBounds = stackalloc BoundingBox[BIN_COUNT];
        binBounds.Fill(_emptyBounds);
        for (int index = start; index < start + count; index++) {
            BuildTriangle triangle = triangles[index];
            int bin = GetBin(triangle.Centroid, axis, axisMinimum, extent);
            binCounts[bin]++;
            binBounds[bin] = Union(binBounds[bin], triangle.Bounds);
        }

        Span<int> rightCounts = stackalloc int[BIN_COUNT];
        Span<BoundingBox> rightBounds = stackalloc BoundingBox[BIN_COUNT];
        BoundingBox suffixBounds = _emptyBounds;
        int suffixCount = 0;
        for (int bin = BIN_COUNT - 1; bin > 0; bin--) {
            suffixBounds = Union(suffixBounds, binBounds[bin]);
            suffixCount += binCounts[bin];
            rightBounds[bin] = suffixBounds;
            rightCounts[bin] = suffixCount;
        }

        BoundingBox prefixBounds = _emptyBounds;
        int prefixCount = 0;
        float bestCost = float.PositiveInfinity;
        int bestBin = -1;
        for (int bin = 0; bin < BIN_COUNT - 1; bin++) {
            prefixBounds = Union(prefixBounds, binBounds[bin]);
            prefixCount += binCounts[bin];
            int rightCount = rightCounts[bin + 1];
            if (prefixCount == 0 || rightCount == 0) {
                continue;
            }

            float cost = (SurfaceArea(prefixBounds) * prefixCount)
                + (SurfaceArea(rightBounds[bin + 1]) * rightCount);
            if (cost < bestCost) {
                bestCost = cost;
                bestBin = bin;
            }
        }

        if (bestBin < 0) {
            return false;
        }

        split = PartitionByBin(triangles, scratch, start, count, axis, axisMinimum, extent, bestBin);
        return true;
    }

    private static int PartitionByBin(
        BuildTriangle[] triangles,
        BuildTriangle[] scratch,
        int start,
        int count,
        int axis,
        float axisMinimum,
        float extent,
        int splitBin) {
        int leftCount = 0;
        for (int index = start; index < start + count; index++) {
            if (GetBin(triangles[index].Centroid, axis, axisMinimum, extent) <= splitBin) {
                leftCount++;
            }
        }

        int left = 0;
        int right = leftCount;
        for (int index = start; index < start + count; index++) {
            BuildTriangle triangle = triangles[index];
            if (GetBin(triangle.Centroid, axis, axisMinimum, extent) <= splitBin) {
                scratch[left++] = triangle;
            } else {
                scratch[right++] = triangle;
            }
        }

        Array.Copy(scratch, 0, triangles, start, count);
        return start + leftCount;
    }

    private static int SplitByCentroid(
        BuildTriangle[] triangles,
        int start,
        int count,
        Vector3 centroidMin,
        Vector3 centroidMax) {
        int axis = GetLargestCentroidAxis(centroidMin, centroidMax, out _);
        CentroidComparer comparer = axis switch {
            0 => _centroidXComparer,
            1 => _centroidYComparer,
            _ => _centroidZComparer,
        };
        Array.Sort(triangles, start, count, comparer);
        return start + (count / 2);
    }

    private static int GetLargestCentroidAxis(Vector3 centroidMin, Vector3 centroidMax, out float extent) {
        Vector3 size = centroidMax - centroidMin;
        if (size.X >= size.Y && size.X >= size.Z) {
            extent = size.X;
            return 0;
        } else if (size.Y >= size.Z) {
            extent = size.Y;
            return 1;
        }

        extent = size.Z;
        return 2;
    }

    private static int GetBin(Vector3 centroid, int axis, float axisMinimum, float extent) {
        float value = axis switch {
            0 => centroid.X,
            1 => centroid.Y,
            _ => centroid.Z,
        };
        int bin = (int)(((value - axisMinimum) / extent) * BIN_COUNT);
        return Math.Clamp(bin, 0, BIN_COUNT - 1);
    }

    private static float SurfaceArea(BoundingBox bounds) {
        Vector3 size = bounds.Max - bounds.Min;
        return 2f * ((size.X * size.Y) + (size.Y * size.Z) + (size.Z * size.X));
    }

    private static BoundingBox Union(BoundingBox a, BoundingBox b) => new(
        Vector3.Min(a.Min, b.Min),
        Vector3.Max(a.Max, b.Max));

    private struct BuildTriangle {
        internal int TriangleId;
        internal BoundingBox Bounds;
        internal Vector3 Centroid;

        internal BuildTriangle(int triangleId, Vector3 a, Vector3 b, Vector3 c) {
            TriangleId = triangleId;
            Bounds = new BoundingBox(
                Vector3.Min(a, Vector3.Min(b, c)),
                Vector3.Max(a, Vector3.Max(b, c)));
            Centroid = (a + b + c) / 3f;
        }
    }

    private sealed class CentroidComparer : IComparer<BuildTriangle> {
        private readonly int _axis;

        internal CentroidComparer(int axis) {
            _axis = axis;
        }

        public int Compare(BuildTriangle x, BuildTriangle y) {
            float left = _axis switch {
                0 => x.Centroid.X,
                1 => x.Centroid.Y,
                _ => x.Centroid.Z,
            };
            float right = _axis switch {
                0 => y.Centroid.X,
                1 => y.Centroid.Y,
                _ => y.Centroid.Z,
            };
            int comparison = left.CompareTo(right);
            return comparison != 0 ? comparison : x.TriangleId.CompareTo(y.TriangleId);
        }
    }
}
