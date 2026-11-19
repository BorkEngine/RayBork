namespace RayBork.Internal;

/// <summary>One validated RAYBORKP v1 index entry from an archive.</summary>
/// <remarks>
/// Instances are immutable and compare by reference. <see cref="PackArchive.ReadPayload"/> accepts only
/// an entry that belongs to its own archive.
/// </remarks>
internal sealed class PackEntry
{
    /// <summary>Initializes an index entry from validated header and index fields.</summary>
    internal PackEntry(
        string path,
        AssetEntryType type,
        ushort flags,
        ulong offset,
        uint storedLength,
        uint uncompressedSize,
        uint alignment,
        ulong storedHash
    )
    {
        Path = path;
        Type = type;
        Flags = flags;
        Offset = offset;
        StoredLength = storedLength;
        UncompressedSize = uncompressedSize;
        Alignment = alignment;
        StoredHash = storedHash;
    }

    /// <summary>Gets the canonical asset path, without a leading slash.</summary>
    internal string Path { get; }

    /// <summary>Gets the entry asset type.</summary>
    internal AssetEntryType Type { get; }

    /// <summary>Gets the raw entry flag bits.</summary>
    internal ushort Flags { get; }

    /// <summary>Gets a value indicating whether the stored payload is Zstandard-compressed.</summary>
    internal bool IsCompressed => (Flags & PackFormat.ENTRY_FLAG_COMPRESSED) != 0;

    /// <summary>Gets a value indicating whether the payload requires alignment beyond one byte.</summary>
    internal bool IsAligned => (Flags & PackFormat.ENTRY_FLAG_ALIGNED) != 0;

    /// <summary>Gets the absolute file offset of the stored payload.</summary>
    internal ulong Offset { get; }

    /// <summary>Gets the stored payload byte length.</summary>
    internal uint StoredLength { get; }

    /// <summary>Gets the payload byte length after decompression.</summary>
    internal uint UncompressedSize { get; }

    /// <summary>Gets the payload alignment in bytes.</summary>
    internal uint Alignment { get; }

    /// <summary>Gets the XXH3-64 hash of the stored payload bytes.</summary>
    internal ulong StoredHash { get; }
}
