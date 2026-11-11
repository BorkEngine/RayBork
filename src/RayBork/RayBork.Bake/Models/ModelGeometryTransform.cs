using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// Applies the sidecar's scale and front-axis correction to source geometry, and flattens a node
/// transform that cannot be represented as TRS.
/// </summary>
internal static class ModelGeometryTransform {
    /// <summary>Applies the uniform scale and orientation correction to every vertex.</summary>
    /// <param name="mesh">The source mesh; it is not modified.</param>
    /// <param name="rotation">The correction rotation.</param>
    /// <param name="scale">The positive uniform scale.</param>
    /// <returns>The corrected mesh.</returns>
    /// <exception cref="ModelReadException">A corrected position or attribute is not finite.</exception>
    internal static MeshData ApplyAuthoring(MeshData mesh, Quaternion rotation, float scale) {
        ArgumentNullException.ThrowIfNull(mesh);

        Vector3 defaultNormal = Vector3.Transform(Vector3.UnitY, rotation);
        var result = new MeshData(mesh.Topology);
        foreach (MeshVertex source in mesh.Vertices) {
            Vector3 position = Vector3.Transform(source.Position, rotation) * scale;
            Vector3 normal = Vector3.Transform(source.Normal, rotation);
            Vector3 tangent = Vector3.Transform(new Vector3(source.Tangent.X, source.Tangent.Y, source.Tangent.Z), rotation);

            if (!IsFinite(position) || !IsFinite(normal) || !IsFinite(tangent)) {
                throw new ModelReadException("A mesh contains a non-finite position, normal, or tangent.");
            }

            if (normal.LengthSquared() < 1e-12f) {
                normal = defaultNormal;
            } else {
                normal = Vector3.Normalize(normal);
            }

            result.Vertices.Add(new MeshVertex {
                Position = position,
                Normal = normal,
                Uv = source.Uv,
                Uv1 = source.Uv1,
                Uv2 = source.Uv2,
                Uv3 = source.Uv3,
                Color = source.Color,
                Tangent = new Vector4(tangent, source.Tangent.W),
            });
        }

        result.Indices.AddRange(mesh.Indices);
        return result;
    }

    /// <summary>Tries to represent a transform as position, rotation, and scale floats.</summary>
    /// <param name="transform">The source-space transform.</param>
    /// <param name="localTransform">The ten floats when the transform decomposes.</param>
    /// <returns><see langword="true"/> when the transform is a decomposition-friendly TRS.</returns>
    internal static bool TryDecompose(Matrix4x4 transform, out float[] localTransform) {
        if (Matrix4x4.Decompose(transform, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
            && IsFinite(scale)
            && IsFinite(rotation)
            && IsFinite(translation)) {
            rotation = Quaternion.Normalize(rotation);
            localTransform = [
                translation.X,
                translation.Y,
                translation.Z,
                rotation.X,
                rotation.Y,
                rotation.Z,
                rotation.W,
                scale.X,
                scale.Y,
                scale.Z,
            ];
            return true;
        }

        localTransform = IdentityTransform();
        return false;
    }

    /// <summary>Gets the ten-float identity transform.</summary>
    /// <returns>Position zero, identity rotation, and unit scale.</returns>
    internal static float[] IdentityTransform()
        => [0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f];

    /// <summary>
    /// Flattens a non-TRS node transform into the mesh, leaving an identity part transform.
    /// </summary>
    /// <param name="mesh">The corrected mesh; it is not modified.</param>
    /// <param name="transform">The source-space node-chain transform.</param>
    /// <returns>The model-space mesh.</returns>
    /// <exception cref="ModelReadException">The transform is singular or produces non-finite values.</exception>
    internal static MeshData ApplyToModelSpace(MeshData mesh, Matrix4x4 transform) {
        ArgumentNullException.ThrowIfNull(mesh);

        if (!Matrix4x4.Invert(transform, out Matrix4x4 inverse)) {
            throw new ModelReadException(
                "A node transform is singular and cannot be baked into its mesh.");
        }

        Matrix4x4 normalMatrix = Matrix4x4.Transpose(inverse);
        var result = new MeshData(mesh.Topology);
        foreach (MeshVertex source in mesh.Vertices) {
            Vector3 position = Vector3.Transform(source.Position, transform);
            Vector3 normal = Vector3.TransformNormal(source.Normal, normalMatrix);
            Vector3 tangent = Vector3.TransformNormal(
                new Vector3(source.Tangent.X, source.Tangent.Y, source.Tangent.Z),
                normalMatrix);

            if (!IsFinite(position) || !IsFinite(normal) || !IsFinite(tangent)) {
                throw new ModelReadException("A node transform produces a non-finite mesh value.");
            }

            if (normal.LengthSquared() < 1e-12f) {
                normal = Vector3.UnitY;
            } else {
                normal = Vector3.Normalize(normal);
            }

            result.Vertices.Add(new MeshVertex {
                Position = position,
                Normal = normal,
                Uv = source.Uv,
                Uv1 = source.Uv1,
                Uv2 = source.Uv2,
                Uv3 = source.Uv3,
                Color = source.Color,
                Tangent = new Vector4(tangent, source.Tangent.W),
            });
        }

        result.Indices.AddRange(mesh.Indices);
        return result;
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
