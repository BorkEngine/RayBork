using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// A fully computed RAYBORKP v1 layout: sorted entries with absolute payload offsets, the serialized
/// index, and the stored payload bytes.
/// </summary>
/// <remarks>
/// The writer compresses and lays out payloads once per <see cref="PackBuilder"/> build. Offsets are
/// absolute and alignment-correct; zero padding fills the gaps. The layout is immutable after
/// creation, so one layout can be written to several streams.
/// </remarks>
internal sealed class PackLayout {
    private readonly PackEntry[] _entries;
    private readonly byte[][] _storedPayloads;
    private readonly byte[] _index;
    private readonly uint _indexHash;
    private readonly long _totalLength;

    private PackLayout(
        PackEntry[] entries,
        byte[][] storedPayloads,
        byte[] index,
        uint indexHash,
        long totalLength) {
        _entries = entries;
        _storedPayloads = storedPayloads;
        _index = index;
        _indexHash = indexHash;
        _totalLength = totalLength;
    }

    /// <summary>Gets the total pack byte length.</summary>
    internal long TotalLength => _totalLength;

    /// <summary>Gets the sorted entries, including their absolute payload offsets.</summary>
    internal IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>
    /// Computes a pack layout from the queued entries.
    /// </summary>
    /// <param name="source">The entries to write, in any order.</param>
    /// <param name="policy">The payload compression policy.</param>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <returns>The computed layout.</returns>
    /// <exception cref="ArgumentException">
    /// A path is not canonical, a path is duplicated, an entry is invalid, or the pack exceeds a v1
    /// field width.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A type, alignment, or compression policy is outside its v1 set.
    /// </exception>
    internal static PackLayout Create(
        IReadOnlyList<PackSourceEntry> source,
        PackCompressionPolicy policy,
        string displayPath) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);

        if (policy is not PackCompressionPolicy.Debug and not PackCompressionPolicy.Release) {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy,
                "Unknown pack compression policy.");
        }

        if ((uint)source.Count > PackFormat.MAX_ENTRY_COUNT) {
            throw new ArgumentException(
                $"Asset pack '{displayPath}' has {source.Count} entries; the v1 limit is "
                    + $"{PackFormat.MAX_ENTRY_COUNT}.",
                nameof(source));
        }

        PackSourceEntry[] ordered = source
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        long indexLength = 0;
        for (int i = 0; i < ordered.Length; i++) {
            PackSourceEntry entry = ordered[i];
            if (!AssetPath.TryValidateCanonical(entry.Path, out string? pathReason)) {
                throw new ArgumentException(
                    $"Asset pack '{displayPath}' has the non-canonical path '{entry.Path}': "
                        + $"{pathReason}.",
                    nameof(source));
            }

            if (i > 0 && string.Equals(ordered[i - 1].Path, entry.Path, StringComparison.Ordinal)) {
                throw new ArgumentException(
                    $"Asset pack '{displayPath}' has the duplicate path '{entry.Path}'.",
                    nameof(source));
            }

            if (!PackFormat.IsValidEntryType(entry.Type)) {
                throw new ArgumentOutOfRangeException(
                    nameof(source),
                    entry.Type,
                    $"Asset pack '{displayPath}' entry '{entry.Path}' has an unknown asset type.");
            }

            if (!PackFormat.IsValidAlignment(entry.Alignment)) {
                throw new ArgumentOutOfRangeException(
                    nameof(source),
                    entry.Alignment,
                    $"Asset pack '{displayPath}' entry '{entry.Path}' has an invalid alignment; "
                        + "v1 accepts 1, 16, 64, or 256.");
            }

            indexLength = checked(indexLength + PackIndex.GetEntryLength(entry.Path));
        }

        if (indexLength > PackFormat.MAX_INDEX_LENGTH) {
            throw new ArgumentException(
                $"Asset pack '{displayPath}' would have a {indexLength}-byte index; the v1 limit is "
                    + $"{PackFormat.MAX_INDEX_LENGTH} bytes.",
                nameof(source));
        }

        var storedPayloads = new byte[ordered.Length][];
        var flags = new ushort[ordered.Length];
        for (int i = 0; i < ordered.Length; i++) {
            PackSourceEntry entry = ordered[i];
            byte[] payload = entry.Payload;
            byte[] stored = payload;
            ushort entryFlags = entry.Alignment > 1 ? PackFormat.ENTRY_FLAG_ALIGNED : (ushort)0;

            if (policy == PackCompressionPolicy.Release && payload.Length > 0) {
                byte[] compressed = PackCompression.Compress(payload);
                if (compressed.Length < payload.Length) {
                    stored = compressed;
                    entryFlags |= PackFormat.ENTRY_FLAG_COMPRESSED;
                }
            }

            if ((uint)stored.Length > PackFormat.MAX_PAYLOAD_LENGTH) {
                throw new ArgumentException(
                    $"Asset pack '{displayPath}' entry '{entry.Path}' stores "
                        + $"{stored.Length} bytes; the v1 limit is {PackFormat.MAX_PAYLOAD_LENGTH}.",
                    nameof(source));
            }

            storedPayloads[i] = stored;
            flags[i] = entryFlags;
        }

        try {
            long payloadOffset = PackFormat.PREFIX_LENGTH + indexLength;
            var entries = new PackEntry[ordered.Length];
            for (int i = 0; i < ordered.Length; i++) {
                PackSourceEntry entry = ordered[i];
                byte[] stored = storedPayloads[i];
                long alignedOffset = PackFormat.Align(payloadOffset, entry.Alignment);

                entries[i] = new PackEntry(
                    entry.Path,
                    entry.Type,
                    flags[i],
                    (ulong)alignedOffset,
                    (uint)stored.Length,
                    (uint)entry.Payload.Length,
                    entry.Alignment,
                    PackCompression.ComputePayloadHash(stored));

                payloadOffset = checked(alignedOffset + stored.Length);
            }

            byte[] index = PackIndex.Serialize(entries);
            return new PackLayout(
                entries,
                storedPayloads,
                index,
                PackCompression.ComputeIndexHash(index),
                payloadOffset);
        } catch (OverflowException exception) {
            throw new ArgumentException(
                $"Asset pack '{displayPath}' exceeds the v1 offset or length range.",
                nameof(source),
                exception);
        }
    }

    /// <summary>
    /// Writes the complete pack to a stream and leaves the stream open.
    /// </summary>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    internal void WriteTo(Stream destination) {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite) {
            throw new ArgumentException("The pack destination must be writable.", nameof(destination));
        }

        Span<byte> header = stackalloc byte[PackFormat.PREFIX_LENGTH];
        PackFormat.MagicBytes.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header[PackFormat.VERSION_OFFSET..],
            PackFormat.CURRENT_VERSION);
        BinaryPrimitives.WriteUInt32LittleEndian(header[PackFormat.FLAGS_OFFSET..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header[PackFormat.ENTRY_COUNT_OFFSET..],
            (uint)_entries.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(
            header[PackFormat.INDEX_OFFSET_OFFSET..],
            PackFormat.PREFIX_LENGTH);
        BinaryPrimitives.WriteUInt32LittleEndian(
            header[PackFormat.INDEX_LENGTH_OFFSET..],
            (uint)_index.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(header[PackFormat.INDEX_HASH_OFFSET..], _indexHash);
        BinaryPrimitives.WriteUInt32LittleEndian(header[PackFormat.RESERVED_OFFSET..], 0);

        destination.Write(header);
        destination.Write(_index);

        long position = PackFormat.PREFIX_LENGTH + _index.Length;
        for (int i = 0; i < _entries.Length; i++) {
            long padding = (long)_entries[i].Offset - position;
            if (padding < 0) {
                throw new InvalidOperationException(
                    "The pack layout payload offsets are not strictly ordered.");
            }

            WriteZeroes(destination, padding);
            destination.Write(_storedPayloads[i]);
            position = checked((long)_entries[i].Offset + _storedPayloads[i].Length);
        }
    }

    /// <summary>
    /// Materializes the complete pack in one array.
    /// </summary>
    /// <returns>The pack bytes.</returns>
    /// <exception cref="InvalidOperationException">The pack is too large for one array.</exception>
    internal byte[] ToArray() {
        if (_totalLength > Array.MaxLength) {
            throw new InvalidOperationException(
                $"The pack is {_totalLength} bytes and exceeds Array.MaxLength; use WriteTo instead.");
        }

        byte[] buffer = new byte[(int)_totalLength];
        using var stream = new MemoryStream(buffer, 0, buffer.Length, writable: true);
        WriteTo(stream);
        return buffer;
    }

    private static void WriteZeroes(Stream destination, long count) {
        if (count <= 0) {
            return;
        }

        Span<byte> zeroes = stackalloc byte[256];
        zeroes.Clear();
        while (count > 0) {
            int chunk = (int)Math.Min(zeroes.Length, count);
            destination.Write(zeroes[..chunk]);
            count -= chunk;
        }
    }
}
