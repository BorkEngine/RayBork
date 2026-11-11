using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// One vertex in the fixed 88-byte engine mesh layout.
/// </summary>
/// <remarks>
/// The field order is the payload order: position, normal, uv, uv1, uv2, uv3, color, and tangent.
/// <see cref="MeshVertex.Create"/> supplies the documented defaults for absent source attributes.
/// </remarks>
internal struct MeshVertex {
    /// <summary>The position, in mesh space.</summary>
    internal Vector3 Position;

    /// <summary>The normal, in mesh space.</summary>
    internal Vector3 Normal;

    /// <summary>The base UV set.</summary>
    internal Vector2 Uv;

    /// <summary>The second UV set.</summary>
    internal Vector2 Uv1;

    /// <summary>The third UV set.</summary>
    internal Vector2 Uv2;

    /// <summary>The fourth UV set.</summary>
    internal Vector2 Uv3;

    /// <summary>The vertex color.</summary>
    internal Vector4 Color;

    /// <summary>The tangent as XYZ plus handedness in W.</summary>
    internal Vector4 Tangent;

    /// <summary>Creates a vertex with the documented attribute defaults.</summary>
    /// <param name="position">The mesh-space position.</param>
    /// <returns>The defaulted vertex.</returns>
    internal static MeshVertex Create(Vector3 position)
        => new() {
            Position = position,
            Normal = Vector3.UnitY,
            Color = Vector4.One,
        };
}
