using System.Buffers.Binary;
using System.Text;

namespace RayBork.Internal;

/// <summary>
/// Serializes and parses the RAYBORKP v1 binary index.
/// </summary>
/// <remarks>
/// <para>
/// Every field is little-endian. An entry is a <c>u16</c> UTF-8 path length, the path bytes, the
/// asset type, the flag bits, the absolute payload offset, the stored and uncompressed lengths, the
/// alignment, and the XXH3-64 hash of the stored bytes.
/// </para>
/// <para>
/// The writer stores entries in ordinal path order. The reader rejects a non-canonical path, an
/// unknown type, an undefined flag bit, a flag/alignment disagreement, an out-of-bounds payload, a
/// bad alignment, a non-strict order, and a duplicate path with <c>RB1003</c>.
/// </para>
/// </remarks>
internal static class PackIndex {
    /// <summary>
    /// The strict UTF-8 decoder. A malformed path must be rejected, not silently replaced.
    /// </summary>
    private static readonly UTF8Encoding _strictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The initial entry-list capacity. A corrupt header entry count must not force a large
    /// allocation before the index length has been validated.
    /// </summary>
    private const int INITIAL_ENTRY_CAPACITY = 1024;

    /// <summary>
    /// Gets the serialized length of one entry, including its path bytes.
    /// </summary>
    /// <param name="path">The canonical asset path.</param>
    /// <returns>The entry length in bytes.</returns>
    internal static int GetEntryLength(string path)
        => checked(PackFormat.INDEX_ENTRY_FIXED_LENGTH + Encoding.UTF8.GetByteCount(path));

    /// <summary>
    /// Serializes validated entries in their current order.
    /// </summary>
    /// <param name="entries">The entries, already sorted by ordinal path order.</param>
    /// <returns>The index bytes.</returns>
    /// <exception cref="InvalidOperationException">The index exceeds the v1 length limit.</exception>
    internal static byte[] Serialize(IReadOnlyList<PackEntry> entries) {
        long length = 0;
        for (int i = 0; i < entries.Count; i++) {
            length = checked(length + GetEntryLength(entries[i].Path));
        }

        if (length > PackFormat.MAX_INDEX_LENGTH) {
            throw new InvalidOperationException(
                $"The pack index is {length} bytes and exceeds the v1 limit of "
                    + $"{PackFormat.MAX_INDEX_LENGTH} bytes.");
        }

        byte[] index = new byte[(int)length];
        int position = 0;
        for (int i = 0; i < entries.Count; i++) {
            PackEntry entry = entries[i];
            int pathLength = Encoding.UTF8.GetByteCount(entry.Path);
            if (pathLength > ushort.MaxValue) {
                throw new InvalidOperationException(
                    $"The asset path '{entry.Path}' is {pathLength} bytes and exceeds the v1 "
                        + "path-length field.");
            }

            BinaryPrimitives.WriteUInt16LittleEndian(index.AsSpan(position), (ushort)pathLength);
            position += 2;
            Encoding.UTF8.GetBytes(entry.Path, index.AsSpan(position));
            position += pathLength;
            BinaryPrimitives.WriteUInt16LittleEndian(index.AsSpan(position), (ushort)entry.Type);
            position += 2;
            BinaryPrimitives.WriteUInt16LittleEndian(index.AsSpan(position), entry.Flags);
            position += 2;
            BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(position), entry.Offset);
            position += 8;
            BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(position), entry.StoredLength);
            position += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(position), entry.UncompressedSize);
            position += 4;
            BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(position), entry.Alignment);
            position += 4;
            BinaryPrimitives.WriteUInt64LittleEndian(index.AsSpan(position), entry.StoredHash);
            position += 8;
        }

        return index;
    }

    /// <summary>
    /// Parses and fully validates an index.
    /// </summary>
    /// <param name="index">The complete index bytes.</param>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="entryCount">The entry count declared by the header.</param>
    /// <param name="fileLength">The complete pack length.</param>
    /// <param name="payloadStart">The first byte after the index; payloads may not start earlier.</param>
    /// <returns>The entries in sorted order.</returns>
    /// <exception cref="AssetPackException">The index is structurally invalid.</exception>
    internal static PackEntry[] Parse(
        ReadOnlySpan<byte> index,
        string displayPath,
        uint entryCount,
        long fileLength,
        long payloadStart) {
        int capacity = (int)Math.Min(entryCount, INITIAL_ENTRY_CAPACITY);
        var entries = new List<PackEntry>(capacity);
        int position = 0;
        string? previousPath = null;

        for (uint i = 0; i < entryCount; i++) {
            if (index.Length - position < PackFormat.INDEX_ENTRY_FIXED_LENGTH) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"the index ends inside entry {i} of the {entryCount} declared by the header");
            }

            int pathLength = BinaryPrimitives.ReadUInt16LittleEndian(index[position..]);
            position += 2;
            if (pathLength > PackFormat.MAX_PATH_LENGTH) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry {i} declares a {pathLength}-byte path; the v1 limit is "
                        + $"{PackFormat.MAX_PATH_LENGTH} bytes");
            }

            int required = pathLength + (PackFormat.INDEX_ENTRY_FIXED_LENGTH - 2);
            if (index.Length - position < required) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"the index ends inside entry {i} of the {entryCount} declared by the header");
            }

            ReadOnlySpan<byte> pathBytes = index.Slice(position, pathLength);
            position += pathLength;
            string path;
            try {
                path = _strictUtf8.GetString(pathBytes);
            } catch (DecoderFallbackException exception) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry {i} has a path that is not valid UTF-8",
                    exception);
            }

            if (!AssetPath.TryValidateCanonical(path, out string? pathReason)) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry {i} path '{path}' is not canonical: {pathReason}");
            }

            if (previousPath is not null) {
                int order = string.CompareOrdinal(previousPath, path);
                if (order == 0) {
                    throw PackFailure.InvalidIndex(
                        displayPath,
                        $"duplicate asset path '{path}'");
                }

                if (order > 0) {
                    throw PackFailure.InvalidIndex(
                        displayPath,
                        $"entry '{path}' sorts before the previous entry '{previousPath}'");
                }
            }

            previousPath = path;

            AssetEntryType type = (AssetEntryType)BinaryPrimitives.ReadUInt16LittleEndian(
                index[position..]);
            position += 2;
            if (!PackFormat.IsValidEntryType(type)) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has the unknown asset type {(ushort)type}");
            }

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(index[position..]);
            position += 2;
            if ((flags & ~PackFormat.ENTRY_FLAG_MASK) != 0) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has undefined flag bits 0x{flags & ~PackFormat.ENTRY_FLAG_MASK:x4}");
            }

            ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(index[position..]);
            position += 8;
            uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(index[position..]);
            position += 4;
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(index[position..]);
            position += 4;
            uint alignment = BinaryPrimitives.ReadUInt32LittleEndian(index[position..]);
            position += 4;
            ulong storedHash = BinaryPrimitives.ReadUInt64LittleEndian(index[position..]);
            position += 8;

            if (!PackFormat.IsValidAlignment(alignment)) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has the invalid alignment {alignment}; v1 accepts 1, 16, 64, or 256");
            }

            bool isCompressed = (flags & PackFormat.ENTRY_FLAG_COMPRESSED) != 0;
            bool isAligned = (flags & PackFormat.ENTRY_FLAG_ALIGNED) != 0;
            if (isAligned != (alignment > 1)) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has the aligned flag {(isAligned ? "set" : "clear")} "
                        + $"with alignment {alignment}");
            }

            if (!isCompressed && storedLength != uncompressedSize) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"uncompressed entry '{path}' has stored length {storedLength} but "
                        + $"uncompressed size {uncompressedSize}");
            }

            if (storedLength > PackFormat.MAX_PAYLOAD_LENGTH) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has a stored length of {storedLength} bytes; the v1 limit is "
                        + $"{PackFormat.MAX_PAYLOAD_LENGTH} bytes");
            }

            if (uncompressedSize > PackFormat.MAX_PAYLOAD_LENGTH) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' has an uncompressed size of {uncompressedSize} bytes; the v1 "
                        + $"limit is {PackFormat.MAX_PAYLOAD_LENGTH} bytes");
            }

            if (offset < (ulong)payloadStart) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' offset {offset} is before the payload data start "
                        + $"{payloadStart}");
            }

            if (offset % alignment != 0) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' offset {offset} is not a multiple of its alignment "
                        + $"{alignment}");
            }

            if (offset > (ulong)fileLength) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' offset {offset} is past the pack length {fileLength}");
            }

            if ((ulong)storedLength > (ulong)fileLength - offset) {
                throw PackFailure.InvalidIndex(
                    displayPath,
                    $"entry '{path}' ends at {offset + storedLength}, past the pack length "
                        + $"{fileLength}");
            }

            entries.Add(new PackEntry(
                path,
                type,
                flags,
                offset,
                storedLength,
                uncompressedSize,
                alignment,
                storedHash));
        }

        if (position != index.Length) {
            throw PackFailure.InvalidIndex(
                displayPath,
                $"the index has {index.Length - position} trailing bytes after the "
                    + $"{entryCount} declared entries");
        }

        return entries.ToArray();
    }
}
