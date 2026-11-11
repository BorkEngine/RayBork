using PolyType;

namespace RayBork.Internal;

/// <summary>
/// One part of a packed model.
/// </summary>
/// <remarks>
/// <see cref="LocalTransform"/> carries ten floats: position X/Y/Z, rotation X/Y/Z/W, and scale
/// X/Y/Z. <see cref="Bounds"/> carries six floats: minimum X/Y/Z and maximum X/Y/Z.
/// </remarks>
[GenerateShape]
internal sealed partial class ModelPartAsset {
    /// <summary>Gets the part name from the source scene.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the canonical path of the part mesh asset.</summary>
    public required string MeshPath { get; init; }

    /// <summary>Gets the canonical path of the part shader asset, or <see langword="null"/> when unset.</summary>
    public string? ShaderPath { get; init; }

    /// <summary>Gets the part-local transform as position, rotation, and scale floats.</summary>
    public required float[] LocalTransform { get; init; }

    /// <summary>Gets the model-space bounds as minimum X/Y/Z and maximum X/Y/Z.</summary>
    public required float[] Bounds { get; init; }
}
