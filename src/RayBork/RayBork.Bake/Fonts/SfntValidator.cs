using System.Buffers.Binary;
using System.Text;

namespace RayBork.Bake.Fonts;

/// <summary>
/// Validates the sfnt container and table directory of a TTF or OTF face.
/// </summary>
/// <remarks>
/// <para>
/// The accepted magic values are <c>0x00010000</c> and <c>true</c> for TrueType outlines, and
/// <c>OTTO</c> for CFF outlines. A TrueType Collection (<c>ttcf</c>) is rejected because the pack
/// stores one face. The declared format is derived from the container, not the source extension.
/// </para>
/// <para>
/// Every table directory record must lie inside the file. A TrueType face must contain <c>head</c>,
/// <c>maxp</c>, <c>hhea</c>, <c>hmtx</c>, <c>cmap</c>, and <c>glyf</c>; a CFF face replaces
/// <c>glyf</c> with <c>CFF </c> or <c>CFF2</c>. M3 does not parse glyphs or tables beyond their
/// bounds.
/// </para>
/// </remarks>
internal static class SfntValidator {
    private const uint TRUETYPE_MAGIC = 0x0001_0000;
    private const uint TRUE_MAGIC = 0x7472_7565;
    private const uint OTTO_MAGIC = 0x4F54_544F;
    private const uint TTC_MAGIC = 0x7474_6366;
    private const int OFFSET_TABLE_LENGTH = 12;
    private const int TABLE_RECORD_LENGTH = 16;

    /// <summary>Validates a complete face byte array.</summary>
    /// <param name="face">The original face bytes.</param>
    /// <param name="format">The container format, <c>ttf</c> or <c>otf</c>.</param>
    /// <param name="reason">The failure reason when the face is invalid.</param>
    /// <returns><see langword="true"/> when the container and table directory are valid.</returns>
    internal static bool TryValidate(
        ReadOnlySpan<byte> face,
        out string format,
        out string? reason) {
        format = string.Empty;
        if (face.Length < OFFSET_TABLE_LENGTH) {
            reason = "the file is shorter than the 12-byte sfnt offset table";
            return false;
        }

        uint magic = BinaryPrimitives.ReadUInt32BigEndian(face);
        switch (magic) {
            case TRUETYPE_MAGIC:
            case TRUE_MAGIC:
                format = "ttf";
                break;

            case OTTO_MAGIC:
                format = "otf";
                break;

            case TTC_MAGIC:
                reason = "the file is a TrueType Collection (ttcf); a single face is required";
                return false;

            default:
                reason = $"the file magic 0x{magic:X8} is not a supported TrueType or OpenType face";
                return false;
        }

        ushort tableCount = BinaryPrimitives.ReadUInt16BigEndian(face[4..]);
        int directoryLength = OFFSET_TABLE_LENGTH + (tableCount * TABLE_RECORD_LENGTH);
        if (directoryLength > face.Length) {
            reason = $"the {tableCount}-entry table directory extends past the {face.Length}-byte file";
            return false;
        }

        bool foundHead = false;
        bool foundMaxp = false;
        bool foundHhea = false;
        bool foundHmtx = false;
        bool foundCmap = false;
        bool foundGlyf = false;
        bool foundCff = false;
        bool foundCff2 = false;

        for (int index = 0; index < tableCount; index++) {
            int recordOffset = OFFSET_TABLE_LENGTH + (index * TABLE_RECORD_LENGTH);
            ReadOnlySpan<byte> record = face.Slice(recordOffset, TABLE_RECORD_LENGTH);
            string tag = Encoding.ASCII.GetString(record[..4]);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if ((ulong)offset + length > (ulong)face.Length) {
                reason = $"the '{tag}' table at offset {offset} with length {length} extends past "
                    + $"the {face.Length}-byte file";
                return false;
            }

            switch (tag) {
                case "head":
                    foundHead = true;
                    break;

                case "maxp":
                    foundMaxp = true;
                    break;

                case "hhea":
                    foundHhea = true;
                    break;

                case "hmtx":
                    foundHmtx = true;
                    break;

                case "cmap":
                    foundCmap = true;
                    break;

                case "glyf":
                    foundGlyf = true;
                    break;

                case "CFF ":
                    foundCff = true;
                    break;

                case "CFF2":
                    foundCff2 = true;
                    break;
            }
        }

        string? missing = null;
        if (!foundHead) {
            missing = "head";
        } else if (!foundMaxp) {
            missing = "maxp";
        } else if (!foundHhea) {
            missing = "hhea";
        } else if (!foundHmtx) {
            missing = "hmtx";
        } else if (!foundCmap) {
            missing = "cmap";
        } else if (format == "ttf" && !foundGlyf) {
            missing = "glyf";
        } else if (format == "otf" && !foundCff && !foundCff2) {
            missing = "CFF or CFF2";
        }

        if (missing is not null) {
            reason = $"the {format} face is missing the required '{missing}' table";
            return false;
        }

        reason = null;
        return true;
    }
}
