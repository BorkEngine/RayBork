using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The packed descriptor of a multi-part model asset.
/// </summary>
/// <remarks>
/// Each part references a separately packed <see cref="MeshAssetDescriptor"/> entry. The model holds
/// the part references at runtime when model loading lands in M4. M3 bakes and validates the entry.
/// </remarks>
[GenerateShape]
internal sealed partial class ModelAssetDescriptor {


    /// <summary>Gets the flat part list.</summary>
    public required ModelPartAsset[] Parts { get; init; }

    /// <summary>Gets the model-space bounds as minimum X/Y/Z and maximum X/Y/Z.</summary>
    public required float[] Bounds { get; init; }
}
