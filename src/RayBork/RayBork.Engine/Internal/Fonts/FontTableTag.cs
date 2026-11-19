using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>A four-byte sfnt table tag, compared as its big-endian value.</summary>
/// <remarks>
/// A tag is stored as a <see cref="uint"/> without allocating; <see cref="ToName"/> builds a printable
/// name only for diagnostics.
/// </remarks>
internal readonly struct FontTableTag : IEquatable<FontTableTag>
{
    /// <summary>Initializes a tag from its packed four-byte value.</summary>
    internal FontTableTag(uint value)
    {
        Value = value;
    }

    /// <summary>Gets the <c>head</c> tag.</summary>
    internal static FontTableTag Head { get; } = new(0x6865_6164);

    /// <summary>Gets the <c>maxp</c> tag.</summary>
    internal static FontTableTag Maxp { get; } = new(0x6D61_7870);

    /// <summary>Gets the <c>hhea</c> tag.</summary>
    internal static FontTableTag Hhea { get; } = new(0x6868_6561);

    /// <summary>Gets the <c>hmtx</c> tag.</summary>
    internal static FontTableTag Hmtx { get; } = new(0x686D_7478);

    /// <summary>Gets the <c>cmap</c> tag.</summary>
    internal static FontTableTag Cmap { get; } = new(0x636D_6170);

    /// <summary>Gets the <c>loca</c> tag.</summary>
    internal static FontTableTag Loca { get; } = new(0x6C6F_6361);

    /// <summary>Gets the <c>glyf</c> tag.</summary>
    internal static FontTableTag Glyf { get; } = new(0x676C_7966);

    /// <summary>Gets the <c>kern</c> tag.</summary>
    internal static FontTableTag Kern { get; } = new(0x6B65_726E);

    /// <summary>Gets the <c>GPOS</c> tag.</summary>
    internal static FontTableTag Gpos { get; } = new(0x4750_4F53);

    /// <summary>Gets the <c>latn</c> script tag.</summary>
    internal static FontTableTag Latn { get; } = new(0x6C61_746E);

    /// <summary>Gets the <c>DFLT</c> script tag.</summary>
    internal static FontTableTag Dflt { get; } = new(0x4446_4C54);

    /// <summary>Gets the <c>CFF </c> tag.</summary>
    internal static FontTableTag Cff { get; } = new(0x4346_4620);

    /// <summary>Gets the <c>CFF2</c> tag.</summary>
    internal static FontTableTag Cff2 { get; } = new(0x4346_4632);

    /// <summary>Gets the four tag bytes as one big-endian value.</summary>
    internal uint Value { get; }

    /// <summary>Builds a tag from exactly four ASCII bytes; any other length is rejected.</summary>
    internal static FontTableTag FromAscii(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 4)
        {
            throw new ArgumentException("A table tag is exactly four bytes.", nameof(bytes));
        }

        return new FontTableTag(BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    /// <summary>Gets a printable four-character name, replacing non-printable bytes.</summary>
    internal string ToName()
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, Value);
        Span<char> chars = stackalloc char[4];
        for (int index = 0; index < chars.Length; index++)
        {
            byte value = bytes[index];
            chars[index] = value is >= 0x20 and <= 0x7E ? (char)value : '?';
        }

        return new string(chars);
    }

    /// <inheritdoc/>
    public bool Equals(FontTableTag other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FontTableTag other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (int)Value;

    /// <inheritdoc/>
    public override string ToString() => ToName();

    /// <summary>Compares two tags for equality.</summary>
    public static bool operator ==(FontTableTag left, FontTableTag right) => left.Equals(right);

    /// <summary>Compares two tags for inequality.</summary>
    public static bool operator !=(FontTableTag left, FontTableTag right) => !left.Equals(right);
}
