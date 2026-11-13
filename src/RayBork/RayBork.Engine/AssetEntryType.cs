namespace RayBork;

/// <summary>
/// The asset type identifier stored in a pack index entry.
/// </summary>
/// <remarks>
/// <para>
/// The catalog uses the identifier to select the loader for a path. <see cref="Raw"/> entries are
/// opaque payloads read through <see cref="IAssetCatalog.LoadData"/>; <see cref="Data"/> entries
/// carry MessagePack custom data for a type registered with
/// <see cref="RayBorkAppBuilder.WithAssetType{T}"/>; every other value carries the descriptor for
/// the matching built-in engine asset format.
/// </para>
/// <para>
/// The numeric values are part of the RAYBORKP v1 wire format and never change within pack major
/// version 1.
/// </para>
/// </remarks>
public enum AssetEntryType : ushort {
    /// <summary>An opaque payload with no descriptor.</summary>
    Raw = 0,

    /// <summary>A mesh descriptor entry.</summary>
    Mesh = 1,

    /// <summary>A model descriptor entry.</summary>
    Model = 2,

    /// <summary>A texture descriptor entry.</summary>
    Texture = 3,

    /// <summary>A shader descriptor entry.</summary>
    Shader = 4,

    /// <summary>A font descriptor entry.</summary>
    Font = 5,

    /// <summary>A MessagePack custom-data entry.</summary>
    Data = 6,
}
