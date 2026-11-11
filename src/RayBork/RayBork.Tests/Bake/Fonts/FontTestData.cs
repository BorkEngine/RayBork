using System.Buffers.Binary;
using System.Text;

namespace RayBork.Tests.Bake.Fonts;

/// <summary>Builds synthetic sfnt faces for the font processor tests.</summary>
internal static class FontTestData {
    private const uint TRUETYPE_MAGIC = 0x0001_0000;
    private const uint TRUE_MAGIC = 0x7472_7565;
    private const uint OTTO_MAGIC = 0x4F54_544F;
    private const uint TTC_MAGIC = 0x7474_6366;
    private const int DIRECTORY_OFFSET = 12;
    private const int RECORD_LENGTH = 16;

    internal static byte[] BuildTtf(params string[] omissions) => BuildFace(
        TRUETYPE_MAGIC,
        ["head", "maxp", "hhea", "hmtx", "cmap", "glyf"],
        omissions);

    internal static byte[] BuildOtf() => BuildFace(
        OTTO_MAGIC,
        ["head", "maxp", "hhea", "hmtx", "cmap", "CFF "],
        []);

    internal static byte[] BuildTrueMagicTtf() => BuildFace(
        TRUE_MAGIC,
        ["head", "maxp", "hhea", "hmtx", "cmap", "glyf"],
        []);

    internal static byte[] BuildTtc() {
        var face = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(face, TTC_MAGIC);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(4), 1);
        return face;
    }

    internal static byte[] WithBadMagic(byte[] face) {
        byte[] copy = [.. face];
        BinaryPrimitives.WriteUInt32BigEndian(copy, 0x1234_5678);
        return copy;
    }

    internal static byte[] WithTruncatedDirectory(byte[] face) {
        ushort tableCount = BinaryPrimitives.ReadUInt16BigEndian(face.AsSpan(4));
        int length = DIRECTORY_OFFSET + (tableCount * RECORD_LENGTH) - 4;
        return [.. face.AsSpan(0, length)];
    }

    internal static byte[] WithOutOfBoundsTable(byte[] face) {
        byte[] copy = [.. face];
        BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(DIRECTORY_OFFSET + 8), (uint)copy.Length);
        return copy;
    }

    private static byte[] BuildFace(
        uint magic,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> omissions) {
        var present = new List<(string Tag, int Length)>();
        foreach (string tag in tags) {
            if (!omissions.Contains(tag, StringComparer.Ordinal)) {
                present.Add((tag, TableLength(tag)));
            }
        }

        int directoryLength = DIRECTORY_OFFSET + (present.Count * RECORD_LENGTH);
        var offsets = new int[present.Count];
        int offset = directoryLength;
        int totalLength = directoryLength;
        for (int index = 0; index < present.Count; index++) {
            offsets[index] = offset;
            offset += Align4(present[index].Length);
            totalLength = offset;
        }

        var face = new byte[totalLength];
        BinaryPrimitives.WriteUInt32BigEndian(face, magic);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(4), (ushort)present.Count);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(8), 0);
        BinaryPrimitives.WriteUInt16BigEndian(face.AsSpan(10), 0);

        for (int index = 0; index < present.Count; index++) {
            int recordOffset = DIRECTORY_OFFSET + (index * RECORD_LENGTH);
            Encoding.ASCII.GetBytes(present[index].Tag).CopyTo(face, recordOffset);
            BinaryPrimitives.WriteUInt32BigEndian(face.AsSpan(recordOffset + 4), 0);
            BinaryPrimitives.WriteUInt32BigEndian(
                face.AsSpan(recordOffset + 8),
                (uint)offsets[index]);
            BinaryPrimitives.WriteUInt32BigEndian(
                face.AsSpan(recordOffset + 12),
                (uint)present[index].Length);
        }

        return face;
    }

    private static int TableLength(string tag) => tag switch {
        "head" => 54,
        "maxp" => 6,
        "hhea" => 36,
        "hmtx" => 4,
        "cmap" => 12,
        "glyf" => 8,
        "CFF " => 8,
        _ => 4,
    };

    private static int Align4(int value) => (value + 3) & ~3;
}
