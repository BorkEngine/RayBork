namespace RayBork.Internal;

/// <summary>
/// The fixed asset type identifiers stored in a pack index.
/// </summary>
/// <remarks>
/// Values are part of the RAYBORKP v1 wire format and never change within pack major version 1. Raw
/// entries hold opaque payloads; <see cref="Data"/> entries hold MessagePack custom data.
/// </remarks>
internal enum AssetEntryType : ushort {
    /// <summary>An opaque payload with no descriptor.</summary>
    Raw = 0,

    /// <summary>A <see cref="MeshAssetDescriptor"/> entry.</summary>
    Mesh = 1,

    /// <summary>A <see cref="ModelAssetDescriptor"/> entry.</summary>
    Model = 2,

    /// <summary>A <see cref="TextureAssetDescriptor"/> entry.</summary>
    Texture = 3,

    /// <summary>A <see cref="ShaderAssetDescriptor"/> entry.</summary>
    Shader = 4,

    /// <summary>A <see cref="FontAssetDescriptor"/> entry.</summary>
    Font = 5,

    /// <summary>A MessagePack custom-data entry.</summary>
    Data = 6,
}
