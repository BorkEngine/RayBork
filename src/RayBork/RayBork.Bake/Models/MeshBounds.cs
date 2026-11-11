using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// Computes six-float axis-aligned bounds as minimum X/Y/Z followed by maximum X/Y/Z.
/// </summary>
internal static class MeshBounds {
    /// <summary>Computes bounds from mesh-space vertex positions.</summary>
    /// <param name="vertices">The vertices; it must not be empty.</param>
    /// <returns>The six-float bounds.</returns>
    internal static float[] FromVertices(IReadOnlyList<MeshVertex> vertices) {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0) {
            throw new ModelReadException("A mesh has no vertices.");
        }

        Vector3 minimum = vertices[0].Position;
        Vector3 maximum = minimum;
        for (int index = 1; index < vertices.Count; index++) {
            Vector3 position = vertices[index].Position;
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return [minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z];
    }

    /// <summary>Computes bounds from vertex positions transformed by one matrix.</summary>
    /// <param name="vertices">The vertices; it must not be empty.</param>
    /// <param name="transform">The transform applied to every position.</param>
    /// <returns>The six-float bounds.</returns>
    internal static float[] FromTransformedVertices(
        IReadOnlyList<MeshVertex> vertices,
        Matrix4x4 transform) {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0) {
            throw new ModelReadException("A mesh has no vertices.");
        }

        Vector3 minimum = Vector3.Transform(vertices[0].Position, transform);
        Vector3 maximum = minimum;
        for (int index = 1; index < vertices.Count; index++) {
            Vector3 position = Vector3.Transform(vertices[index].Position, transform);
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return [minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z];
    }

    /// <summary>Combines part bounds into one model-space bound.</summary>
    /// <param name="parts">The part bounds; it must not be empty.</param>
    /// <returns>The six-float union.</returns>
    internal static float[] Union(IReadOnlyList<float[]> parts) {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0) {
            throw new ModelReadException("A model has no parts.");
        }

        float[] result = [.. parts[0]];
        for (int index = 1; index < parts.Count; index++) {
            float[] candidate = parts[index];
            for (int axis = 0; axis < 3; axis++) {
                result[axis] = MathF.Min(result[axis], candidate[axis]);
                result[axis + 3] = MathF.Max(result[axis + 3], candidate[axis + 3]);
            }
        }

        return result;
    }
}
