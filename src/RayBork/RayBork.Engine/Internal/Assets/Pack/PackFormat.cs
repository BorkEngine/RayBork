namespace RayBork.Internal;

/// <summary>
/// The fixed RAYBORKP v1 prefix, index, and payload constants.
/// </summary>
/// <remarks>
/// The pack is little-endian. The prefix is 40 bytes, followed by the index, followed by
/// alignment-padded payloads. Paths are canonical asset paths without a leading slash. The index is
/// sorted by ordinal path order; its hash and every payload hash are verified before use.
/// </remarks>
internal static class PackFormat {
    /// <summary>The ASCII pack magic, <c>RAYBORKP</c>.</summary>
    internal const string MAGIC_TEXT = "RAYBORKP";

    /// <summary>The magic byte length.</summary>
    internal const int MAGIC_LENGTH = 8;

    /// <summary>The only supported pack format version.</summary>
    internal const uint CURRENT_VERSION = 1;

    /// <summary>The fixed prefix length in bytes.</summary>
    internal const int PREFIX_LENGTH = 40;

    /// <summary>The largest supported index length in bytes.</summary>
    internal const uint MAX_INDEX_LENGTH = 64 * 1024 * 1024;

    /// <summary>The largest supported entry count.</summary>
    internal const uint MAX_ENTRY_COUNT = 1_000_000;

    /// <summary>The largest supported path length in bytes.</summary>
    internal const int MAX_PATH_LENGTH = AssetPath.MAX_PATH_BYTES;

    /// <summary>The largest supported payload size in bytes.</summary>
    internal const uint MAX_PAYLOAD_LENGTH = int.MaxValue;

    /// <summary>The prefix offset of the version field.</summary>
    internal const int VERSION_OFFSET = 8;

    /// <summary>The prefix offset of the flags field.</summary>
    internal const int FLAGS_OFFSET = 12;

    /// <summary>The prefix offset of the entry count field.</summary>
    internal const int ENTRY_COUNT_OFFSET = 16;

    /// <summary>The prefix offset of the index offset field.</summary>
    internal const int INDEX_OFFSET_OFFSET = 20;

    /// <summary>The prefix offset of the index length field.</summary>
    internal const int INDEX_LENGTH_OFFSET = 28;

    /// <summary>The prefix offset of the index hash field.</summary>
    internal const int INDEX_HASH_OFFSET = 32;

    /// <summary>The prefix offset of the reserved field.</summary>
    internal const int RESERVED_OFFSET = 36;

    /// <summary>The entry flag for a Zstandard-compressed payload.</summary>
    internal const ushort ENTRY_FLAG_COMPRESSED = 1 << 0;

    /// <summary>The entry flag for an aligned payload.</summary>
    internal const ushort ENTRY_FLAG_ALIGNED = 1 << 1;

    /// <summary>The allowed payload alignment values.</summary>
    internal static ReadOnlySpan<uint> Alignments => [1u, 16u, 64u, 256u];

    /// <summary>
    /// Validates a payload alignment.
    /// </summary>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <returns><see langword="true"/> when the alignment is one of the v1 values.</returns>
    internal static bool IsValidAlignment(uint alignment)
        => alignment is 1 or 16 or 64 or 256;
}
