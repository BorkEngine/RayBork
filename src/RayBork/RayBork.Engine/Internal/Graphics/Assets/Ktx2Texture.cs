namespace RayBork.Internal;

/// <summary>One parsed KTX2 level index entry: offset and stored byte length.</summary>
internal readonly record struct Ktx2Level(ulong ByteOffset, ulong ByteLength);

/// <summary>
/// The parsed narrow KTX2 2.0 profile of one packed texture payload.
/// </summary>
/// <remarks>
/// Immutable and parsed once per upload; it carries the format, dimensions, sRGB flag, and level
/// index, never a copy of the level bytes.
/// </remarks>
internal sealed class Ktx2Texture
{
    /// <summary>Initializes a parsed texture payload.</summary>
    internal Ktx2Texture(
        TextureFormat format,
        bool isSrgb,
        uint width,
        uint height,
        Ktx2Level[] levels
    )
    {
        Format = format;
        IsSrgb = isSrgb;
        Width = width;
        Height = height;
        Levels = levels;
    }

    /// <summary>Gets the packed texture format the payload was validated against.</summary>
    internal TextureFormat Format { get; }

    /// <summary>Gets whether the payload samples through the sRGB transfer function.</summary>
    internal bool IsSrgb { get; }

    /// <summary>Gets the base-level pixel width.</summary>
    internal uint Width { get; }

    /// <summary>Gets the base-level pixel height.</summary>
    internal uint Height { get; }

    /// <summary>Gets the level index entries, base level first.</summary>
    internal IReadOnlyList<Ktx2Level> Levels { get; }

    /// <summary>Gets the number of mip levels.</summary>
    internal int MipLevels => Levels.Count;
}
