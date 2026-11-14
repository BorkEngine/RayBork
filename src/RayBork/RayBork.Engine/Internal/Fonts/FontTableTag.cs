using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// A four-byte sfnt table tag, compared as its big-endian tag value.
/// </summary>
/// <remarks>
/// Tags are stored without allocating a string. <see cref="ToName"/> materializes a printable name
/// only for diagnostics, replacing bytes outside the printable ASCII range.
/// </remarks>
internal readonly struct FontTableTag : IEquatable<FontTableTag> {
    /// <summary>Initializes a tag from its big-endian value.</summary>
    /// <param name="value">The four tag bytes as one big-endian value.</param>
    internal FontTableTag(uint value) {
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

    /// <summary>Builds a tag from its four big-endian bytes.</summary>
    /// <param name="bytes">Exactly the four tag bytes.</param>
    /// <returns>The tag.</returns>
    /// <exception cref="ArgumentException"><paramref name="bytes"/> is not four bytes long.</exception>
    internal static FontTableTag FromAscii(ReadOnlySpan<byte> bytes) {
        if (bytes.Length != 4) {
            throw new ArgumentException("A table tag is exactly four bytes.", nameof(bytes));
        }

        return new FontTableTag(BinaryPrimitives.ReadUInt32BigEndian(bytes));
    }

    /// <summary>Gets the four tag characters, with non-printable bytes replaced by <c>?</c>.</summary>
    /// <returns>The printable tag name.</returns>
    internal string ToName() {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, Value);
        Span<char> chars = stackalloc char[4];
        for (int index = 0; index < chars.Length; index++) {
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
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> when both tags hold the same bytes.</returns>
    public static bool operator ==(FontTableTag left, FontTableTag right) => left.Equals(right);

    /// <summary>Compares two tags for inequality.</summary>
    /// <param name="left">The first tag.</param>
    /// <param name="right">The second tag.</param>
    /// <returns><see langword="true"/> when the tags differ.</returns>
    public static bool operator !=(FontTableTag left, FontTableTag right) => !left.Equals(right);
}
