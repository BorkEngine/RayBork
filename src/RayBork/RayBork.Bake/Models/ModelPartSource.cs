using System.Numerics;

namespace RayBork.Bake.Models;

/// <summary>
/// One part produced by a model reader, before authoring correction and packaging.
/// </summary>
internal sealed class ModelPartSource {
    /// <summary>Initializes a model part.</summary>
    /// <param name="name">The source mesh or group name, not yet sanitized.</param>
    /// <param name="mesh">The part geometry.</param>
    /// <param name="localTransform">
    /// The composed node-chain transform in source space, or the identity for OBJ.
    /// </param>
    internal ModelPartSource(string? name, MeshData mesh, Matrix4x4 localTransform) {
        Name = name;
        Mesh = mesh;
        LocalTransform = localTransform;
    }

    /// <summary>Gets the source mesh or group name, or <see langword="null"/> when unnamed.</summary>
    internal string? Name { get; }

    /// <summary>Gets the part geometry.</summary>
    internal MeshData Mesh { get; }

    /// <summary>Gets the composed source-space transform.</summary>
    internal Matrix4x4 LocalTransform { get; }
}
