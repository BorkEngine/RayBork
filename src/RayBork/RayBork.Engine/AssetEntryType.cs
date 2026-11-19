namespace RayBork;

/// <summary>The payload kind stored in a pack index entry.</summary>
/// <remarks>
/// <see cref="Raw"/> payloads are opaque. <see cref="Data"/> payloads carry MessagePack data for a
/// type registered with <see cref="RayBorkAppBuilder.WithAssetType{T}"/>.
/// </remarks>
public enum AssetEntryType : ushort
{
    /// <summary>An opaque payload with no descriptor.</summary>
    Raw = 0,

    /// <summary>A mesh descriptor.</summary>
    Mesh = 1,

    /// <summary>A model descriptor.</summary>
    Model = 2,

    /// <summary>A texture descriptor.</summary>
    Texture = 3,

    /// <summary>A shader descriptor.</summary>
    Shader = 4,

    /// <summary>A font descriptor.</summary>
    Font = 5,

    /// <summary>A MessagePack custom-data payload.</summary>
    Data = 6,
}
