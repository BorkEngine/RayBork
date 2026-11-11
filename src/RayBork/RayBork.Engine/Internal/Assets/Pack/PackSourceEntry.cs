namespace RayBork.Internal;

/// <summary>
/// One payload queued in a <see cref="PackBuilder"/>.
/// </summary>
/// <remarks>
/// The constructor copies the payload bytes, so a caller may reuse its buffer after the entry is
/// created. The builder validates the path, type, and alignment when the entry is added.
/// </remarks>
internal sealed class PackSourceEntry {
    /// <summary>
    /// Initializes a source entry.
    /// </summary>
    /// <param name="path">The canonical asset path.</param>
    /// <param name="type">The entry asset type.</param>
    /// <param name="payload">The uncompressed payload bytes; they are copied.</param>
    /// <param name="alignment">The required payload alignment.</param>
    internal PackSourceEntry(
        string path,
        AssetEntryType type,
        ReadOnlySpan<byte> payload,
        uint alignment = 1) {
        ArgumentNullException.ThrowIfNull(path);

        Path = path;
        Type = type;
        Payload = payload.ToArray();
        Alignment = alignment;
    }

    /// <summary>Gets the canonical asset path, without a leading slash.</summary>
    internal string Path { get; }

    /// <summary>Gets the entry asset type.</summary>
    internal AssetEntryType Type { get; }

    /// <summary>Gets the uncompressed payload bytes.</summary>
    internal byte[] Payload { get; }

    /// <summary>Gets the required payload alignment in bytes.</summary>
    internal uint Alignment { get; }
}
