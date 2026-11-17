using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// A bounds-checked big-endian reader over one slice of a font face.
/// </summary>
/// <remarks>
/// <para>
/// Every read that would leave the slice throws <see cref="FontFormatException"/> naming the context
/// (for example <c>the 'head' table</c>) and the offending offset, so corrupt input can never escape
/// as an indexing or arithmetic exception. Readers are short-lived stack values over the retained
/// face bytes; constructing one allocates nothing.
/// </para>
/// <para>
/// The non-throwing <c>TryRead</c> members exist for cmap mappings, where an indirect reference that
/// points outside the subtable is treated as an unmapped codepoint rather than a parse failure.
/// </para>
/// </remarks>
internal readonly ref struct SfntReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _context;

    /// <summary>Initializes a reader over one slice with a diagnostic context.</summary>
    /// <param name="data">The bytes to read.</param>
    /// <param name="context">A description such as <c>the 'glyf' table</c> used in failure reasons.</param>
    internal SfntReader(ReadOnlySpan<byte> data, string context)
    {
        _data = data;
        _context = context;
    }

    /// <summary>Gets the slice length in bytes.</summary>
    internal int Length => _data.Length;

    /// <summary>Reads one unsigned byte.</summary>
    /// <param name="offset">The offset inside the slice.</param>
    /// <returns>The byte.</returns>
    internal byte ReadByte(int offset)
    {
        Require(offset, sizeof(byte));
        return _data[offset];
    }

    /// <summary>Reads one big-endian unsigned 16-bit value.</summary>
    /// <param name="offset">The offset inside the slice.</param>
    /// <returns>The value.</returns>
    internal ushort ReadUInt16(int offset)
    {
        Require(offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(_data[offset..]);
    }

    /// <summary>Reads one big-endian signed 16-bit value.</summary>
    /// <param name="offset">The offset inside the slice.</param>
    /// <returns>The value.</returns>
    internal short ReadInt16(int offset)
    {
        Require(offset, sizeof(short));
        return BinaryPrimitives.ReadInt16BigEndian(_data[offset..]);
    }

    /// <summary>Reads one big-endian unsigned 32-bit value.</summary>
    /// <param name="offset">The offset inside the slice.</param>
    /// <returns>The value.</returns>
    internal uint ReadUInt32(int offset)
    {
        Require(offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32BigEndian(_data[offset..]);
    }

    /// <summary>Reads one big-endian unsigned 16-bit value when it is inside the slice.</summary>
    /// <param name="offset">The offset inside the slice.</param>
    /// <param name="value">The value, or zero when the read is outside the slice.</param>
    /// <returns><see langword="true"/> when the read is inside the slice.</returns>
    internal bool TryReadUInt16(int offset, out ushort value)
    {
        if (offset < 0 || offset > _data.Length - sizeof(ushort))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16BigEndian(_data[offset..]);
        return true;
    }

    /// <summary>Checks that a range is inside the slice.</summary>
    /// <param name="offset">The range offset inside the slice.</param>
    /// <param name="length">The range length in bytes.</param>
    /// <returns><see langword="true"/> when the whole range is inside the slice.</returns>
    internal bool Fits(int offset, long length) =>
        offset >= 0 && length >= 0 && offset <= _data.Length && length <= _data.Length - offset;

    /// <summary>Slices a range of the reader after checking its bounds.</summary>
    /// <param name="offset">The range offset inside the slice.</param>
    /// <param name="length">The range length in bytes.</param>
    /// <returns>The checked slice.</returns>
    internal ReadOnlySpan<byte> Slice(int offset, int length)
    {
        Require(offset, length);
        return _data.Slice(offset, length);
    }

    /// <summary>Throws when a range is not inside the slice.</summary>
    /// <param name="offset">The range offset inside the slice.</param>
    /// <param name="length">The range length in bytes.</param>
    internal void Require(int offset, long length)
    {
        if (!Fits(offset, length))
        {
            throw new FontFormatException(
                $"{_context} is truncated: the {length}-byte range at offset {offset} is outside the "
                    + $"{_data.Length}-byte slice"
            );
        }
    }
}
