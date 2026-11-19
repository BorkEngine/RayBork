namespace RayBork.Bake.Textures;

/// <summary>The shared texture size policy of the bake.</summary>
/// <remarks>
/// The limit bounds the transient decode, mip-chain, and encode memory of one source and stays far
/// below the 32-bit KTX2 field range.
/// </remarks>
internal static class TextureLimits
{
    /// <summary>The largest accepted base-level width or height in pixels.</summary>
    internal const int MAX_DIMENSION = 16384;

    /// <summary>Tests whether a decoded or declared size is accepted.</summary>
    /// <returns><see langword="true"/> when both dimensions are valid.</returns>
    internal static bool IsSupportedSize(long width, long height) =>
        width is > 0 and <= MAX_DIMENSION && height is > 0 and <= MAX_DIMENSION;
}
