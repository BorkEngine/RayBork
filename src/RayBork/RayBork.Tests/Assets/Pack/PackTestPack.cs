using System.Buffers.Binary;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Assets;

/// <summary>
/// The mutable fields of one parsed test index entry.
/// </summary>
internal enum PackTestEntryField {
    Type,
    Flags,
    Offset,
    StoredLength,
    UncompressedSize,
    Alignment,
    StoredHash,
}

/// <summary>
/// Absolute offsets of one parsed test index entry. All offsets address the complete pack byte array.
/// </summary>
internal readonly struct PackTestEntryLocation {
    internal PackTestEntryLocation(
        int pathOffset,
        int pathLength,
        int typeOffset,
        int flagsOffset,
        int offsetOffset,
        int storedLengthOffset,
        int uncompressedSizeOffset,
        int alignmentOffset,
        int storedHashOffset) {
        PathOffset = pathOffset;
        PathLength = pathLength;
        TypeOffset = typeOffset;
        FlagsOffset = flagsOffset;
        OffsetOffset = offsetOffset;
        StoredLengthOffset = storedLengthOffset;
        UncompressedSizeOffset = uncompressedSizeOffset;
        AlignmentOffset = alignmentOffset;
        StoredHashOffset = storedHashOffset;
    }

    internal int PathOffset { get; }

    internal int PathLength { get; }

    internal int TypeOffset { get; }

    internal int FlagsOffset { get; }

    internal int OffsetOffset { get; }

    internal int StoredLengthOffset { get; }

    internal int UncompressedSizeOffset { get; }

    internal int AlignmentOffset { get; }

    internal int StoredHashOffset { get; }
}

/// <summary>
/// Parses and edits a valid pack for targeted corruption tests.
/// </summary>
/// <remarks>
/// The editor patches fields in place and rehashes the index after every semantic edit, so a test
/// reaches the structural validation it targets instead of stopping at <c>RB1004</c>. It never
/// repairs payload hashes unless a test asks it to.
/// </remarks>
internal sealed class PackTestPack {
    internal PackTestPack(byte[] bytes) => Bytes = bytes;

    internal byte[] Bytes { get; }

    internal int IndexOffset => PackFormat.PREFIX_LENGTH;

    internal int IndexLength => checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        Bytes.AsSpan(PackFormat.INDEX_LENGTH_OFFSET)));

    internal int PayloadStart => IndexOffset + IndexLength;

    internal uint EntryCount => BinaryPrimitives.ReadUInt32LittleEndian(
        Bytes.AsSpan(PackFormat.ENTRY_COUNT_OFFSET));

    internal ReadOnlySpan<byte> Index => Bytes.AsSpan(IndexOffset, IndexLength);

    internal uint ComputeIndexHash() => PackCompression.ComputeIndexHash(Index);

    internal void RehashIndex() => BinaryPrimitives.WriteUInt32LittleEndian(
        Bytes.AsSpan(PackFormat.INDEX_HASH_OFFSET),
        ComputeIndexHash());

    internal void CorruptIndexHash() {
        uint storedHash = BinaryPrimitives.ReadUInt32LittleEndian(
            Bytes.AsSpan(PackFormat.INDEX_HASH_OFFSET));
        BinaryPrimitives.WriteUInt32LittleEndian(
            Bytes.AsSpan(PackFormat.INDEX_HASH_OFFSET),
            storedHash ^ 1u);
    }

    internal PackTestEntryLocation FindEntry(string path) {
        int position = IndexOffset;
        for (uint i = 0; i < EntryCount; i++) {
            int pathOffset = position;
            int pathLength = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(position));
            position += 2;
            string entryPath = Encoding.UTF8.GetString(Bytes.AsSpan(position, pathLength));
            position += pathLength;

            var location = new PackTestEntryLocation(
                pathOffset,
                pathLength,
                typeOffset: position,
                flagsOffset: position + 2,
                offsetOffset: position + 4,
                storedLengthOffset: position + 12,
                uncompressedSizeOffset: position + 16,
                alignmentOffset: position + 20,
                storedHashOffset: position + 24);

            if (string.Equals(entryPath, path, StringComparison.Ordinal)) {
                return location;
            }

            position += 32;
        }

        throw new InvalidOperationException($"The test pack has no entry for '{path}'.");
    }

    internal void SetPath(string path, string replacementPath) {
        PackTestEntryLocation location = FindEntry(path);
        byte[] replacement = Encoding.UTF8.GetBytes(replacementPath);
        if (replacement.Length != location.PathLength) {
            throw new InvalidOperationException(
                "A test path replacement must keep the same UTF-8 byte length.");
        }

        replacement.CopyTo(Bytes.AsSpan(location.PathOffset + 2));
        RehashIndex();
    }

    internal ulong GetField(string path, PackTestEntryField field) {
        PackTestEntryLocation location = FindEntry(path);
        return field switch {
            PackTestEntryField.Type => BinaryPrimitives.ReadUInt16LittleEndian(
                Bytes.AsSpan(location.TypeOffset)),
            PackTestEntryField.Flags => BinaryPrimitives.ReadUInt16LittleEndian(
                Bytes.AsSpan(location.FlagsOffset)),
            PackTestEntryField.Offset => BinaryPrimitives.ReadUInt64LittleEndian(
                Bytes.AsSpan(location.OffsetOffset)),
            PackTestEntryField.StoredLength => BinaryPrimitives.ReadUInt32LittleEndian(
                Bytes.AsSpan(location.StoredLengthOffset)),
            PackTestEntryField.UncompressedSize => BinaryPrimitives.ReadUInt32LittleEndian(
                Bytes.AsSpan(location.UncompressedSizeOffset)),
            PackTestEntryField.Alignment => BinaryPrimitives.ReadUInt32LittleEndian(
                Bytes.AsSpan(location.AlignmentOffset)),
            PackTestEntryField.StoredHash => BinaryPrimitives.ReadUInt64LittleEndian(
                Bytes.AsSpan(location.StoredHashOffset)),
            _ => throw new InvalidOperationException($"Unknown test field '{field}'."),
        };
    }

    internal void SetField(string path, PackTestEntryField field, ulong value) {
        PackTestEntryLocation location = FindEntry(path);
        switch (field) {
            case PackTestEntryField.Type:
                BinaryPrimitives.WriteUInt16LittleEndian(
                    Bytes.AsSpan(location.TypeOffset),
                    checked((ushort)value));
                break;
            case PackTestEntryField.Flags:
                BinaryPrimitives.WriteUInt16LittleEndian(
                    Bytes.AsSpan(location.FlagsOffset),
                    checked((ushort)value));
                break;
            case PackTestEntryField.Offset:
                BinaryPrimitives.WriteUInt64LittleEndian(
                    Bytes.AsSpan(location.OffsetOffset),
                    value);
                break;
            case PackTestEntryField.StoredLength:
                BinaryPrimitives.WriteUInt32LittleEndian(
                    Bytes.AsSpan(location.StoredLengthOffset),
                    checked((uint)value));
                break;
            case PackTestEntryField.UncompressedSize:
                BinaryPrimitives.WriteUInt32LittleEndian(
                    Bytes.AsSpan(location.UncompressedSizeOffset),
                    checked((uint)value));
                break;
            case PackTestEntryField.Alignment:
                BinaryPrimitives.WriteUInt32LittleEndian(
                    Bytes.AsSpan(location.AlignmentOffset),
                    checked((uint)value));
                break;
            case PackTestEntryField.StoredHash:
                BinaryPrimitives.WriteUInt64LittleEndian(
                    Bytes.AsSpan(location.StoredHashOffset),
                    value);
                break;
            default:
                throw new InvalidOperationException($"Unknown test field '{field}'.");
        }

        RehashIndex();
    }

    internal void FlipPayloadByte(string path, int index) {
        PackTestEntryLocation location = FindEntry(path);
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(
            Bytes.AsSpan(location.OffsetOffset));
        uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(
            Bytes.AsSpan(location.StoredLengthOffset));
        if ((uint)index >= storedLength) {
            throw new InvalidOperationException("The test payload index is outside the entry.");
        }

        Bytes[checked((int)offset) + index] ^= 0xFF;
    }

    internal void SetPayloadByte(string path, int index, byte value) {
        PackTestEntryLocation location = FindEntry(path);
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(
            Bytes.AsSpan(location.OffsetOffset));
        uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(
            Bytes.AsSpan(location.StoredLengthOffset));
        if ((uint)index >= storedLength) {
            throw new InvalidOperationException("The test payload index is outside the entry.");
        }

        Bytes[checked((int)offset) + index] = value;
    }

    internal void RepairPayloadHash(string path) {
        PackTestEntryLocation location = FindEntry(path);
        ulong offset = BinaryPrimitives.ReadUInt64LittleEndian(
            Bytes.AsSpan(location.OffsetOffset));
        uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(
            Bytes.AsSpan(location.StoredLengthOffset));
        ulong hash = PackCompression.ComputePayloadHash(
            Bytes.AsSpan(checked((int)offset), checked((int)storedLength)));
        BinaryPrimitives.WriteUInt64LittleEndian(
            Bytes.AsSpan(location.StoredHashOffset),
            hash);
        RehashIndex();
    }

    internal void SetHeaderUInt32(int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(offset), value);

    internal void SetHeaderUInt64(int offset, ulong value)
        => BinaryPrimitives.WriteUInt64LittleEndian(Bytes.AsSpan(offset), value);
}
