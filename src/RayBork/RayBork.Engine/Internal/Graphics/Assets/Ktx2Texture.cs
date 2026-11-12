namespace RayBork.Internal;

/// <summary>
/// One parsed KTX2 level index entry: the level data's absolute offset and stored byte length.
/// </summary>
/// <param name="ByteOffset">The level data's absolute offset in the payload.</param>
/// <param name="ByteLength">The stored byte length, without KTX2 alignment padding.</param>
internal readonly record struct Ktx2Level(ulong ByteOffset, ulong ByteLength);

/// <summary>
/// The parsed narrow KTX2 2.0 profile of one packed texture payload.
/// </summary>
/// <remarks>
/// The instance is immutable. Parsing runs once per texture upload, never per frame, and the object
/// carries only the descriptor format, base dimensions, sRGB flag, and the per-level offsets and
/// lengths the upload needs. It never holds a second copy of the level bytes or block-decoded pixels.
/// </remarks>
internal sealed class Ktx2Texture {
    /// <summary>
    /// Initializes a parsed texture payload.
    /// </summary>
    /// <param name="format">The validated packed texture format.</param>
    /// <param name="isSrgb">Whether the payload and descriptor use the sRGB transfer function.</param>
    /// <param name="width">The base-level pixel width.</param>
    /// <param name="height">The base-level pixel height.</param>
    /// <param name="levels">The level index entries, base level first.</param>
    internal Ktx2Texture(TextureFormat format, bool isSrgb, uint width, uint height, Ktx2Level[] levels) {
        Format = format;
        IsSrgb = isSrgb;
        Width = width;
        Height = height;
        Levels = levels;
    }

    /// <summary>
    /// Gets the packed texture format the payload was validated against.
    /// </summary>
    internal TextureFormat Format { get; }

    /// <summary>
    /// Gets whether the payload samples through the sRGB transfer function.
    /// </summary>
    internal bool IsSrgb { get; }

    /// <summary>
    /// Gets the base-level pixel width.
    /// </summary>
    internal uint Width { get; }

    /// <summary>
    /// Gets the base-level pixel height.
    /// </summary>
    internal uint Height { get; }

    /// <summary>
    /// Gets the level index entries, base level first. The count is the complete mip chain.
    /// </summary>
    internal IReadOnlyList<Ktx2Level> Levels { get; }

    /// <summary>
    /// Gets the number of mip levels.
    /// </summary>
    internal int MipLevels => Levels.Count;
}
