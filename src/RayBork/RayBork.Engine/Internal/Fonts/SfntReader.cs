using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>A bounds-checked big-endian reader over one slice of a font face.</summary>
/// <remarks>
/// A read outside the slice throws <see cref="FontFormatException"/> with the context and offset;
/// corrupt input never escapes as an index or arithmetic exception. Readers are short-lived stack
/// values over the retained face bytes, and <c>TryRead</c> members serve cmap mappings, where an
/// out-of-range indirect reference means unmapped.
/// </remarks>
internal readonly ref struct SfntReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _context;

    /// <summary>Initializes a reader over one slice with a diagnostic context.</summary>
    internal SfntReader(ReadOnlySpan<byte> data, string context)
    {
        _data = data;
        _context = context;
    }

    /// <summary>Gets the slice length in bytes.</summary>
    internal int Length => _data.Length;

    /// <summary>Reads one unsigned byte at a byte offset inside the slice.</summary>
    internal byte ReadByte(int offset)
    {
        Require(offset, sizeof(byte));
        return _data[offset];
    }

    /// <summary>Reads one big-endian unsigned 16-bit value.</summary>
    internal ushort ReadUInt16(int offset)
    {
        Require(offset, sizeof(ushort));
        return BinaryPrimitives.ReadUInt16BigEndian(_data[offset..]);
    }

    /// <summary>Reads one big-endian signed 16-bit value.</summary>
    internal short ReadInt16(int offset)
    {
        Require(offset, sizeof(short));
        return BinaryPrimitives.ReadInt16BigEndian(_data[offset..]);
    }

    /// <summary>Reads one big-endian unsigned 32-bit value.</summary>
    internal uint ReadUInt32(int offset)
    {
        Require(offset, sizeof(uint));
        return BinaryPrimitives.ReadUInt32BigEndian(_data[offset..]);
    }

    /// <summary>Reads a big-endian unsigned 16-bit value if it fits in the slice.</summary>
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

    /// <summary>Checks that a byte range is inside the slice.</summary>
    internal bool Fits(int offset, long length) =>
        offset >= 0 && length >= 0 && offset <= _data.Length && length <= _data.Length - offset;

    /// <summary>Slices a checked byte range of the reader.</summary>
    internal ReadOnlySpan<byte> Slice(int offset, int length)
    {
        Require(offset, length);
        return _data.Slice(offset, length);
    }

    /// <summary>Throws when a byte range is not inside the slice.</summary>
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
