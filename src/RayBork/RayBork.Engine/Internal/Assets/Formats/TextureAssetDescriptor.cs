using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The packed descriptor of a texture asset. Its payload is a KTX2 2.0 file in a sibling raw entry.
/// </summary>
/// <remarks>
/// Paths are canonical pack paths without a leading slash. The descriptor is serialized as MessagePack
/// with a compiled shape and never uses reflection.
/// </remarks>
[GenerateShape]
internal sealed partial class TextureAssetDescriptor {


    /// <summary>Gets the canonical path of the KTX2 payload entry.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the base-level width in pixels.</summary>
    public required uint Width { get; init; }

    /// <summary>Gets the base-level height in pixels.</summary>
    public required uint Height { get; init; }

    /// <summary>Gets the number of mip levels in the payload.</summary>
    public required uint MipLevels { get; init; }

    /// <summary>Gets the stored texture format.</summary>
    public required TextureFormat Format { get; init; }

    /// <summary>Gets whether the samples are sRGB-encoded.</summary>
    public required bool IsSrgb { get; init; }

    /// <summary>Gets the sampler filter.</summary>
    public required TextureFilter Filter { get; init; }

    /// <summary>Gets the sampler address mode.</summary>
    public required TextureWrap Wrap { get; init; }

    /// <summary>Gets the requested maximum anisotropy.</summary>
    public required float MaxAnisotropy { get; init; }
}
