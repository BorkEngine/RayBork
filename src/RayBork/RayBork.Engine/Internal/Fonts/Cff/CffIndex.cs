namespace RayBork.Internal;

/// <summary>One CFF INDEX: a count, an offset size, offsets, and object data.</summary>
/// <remarks>
/// Offsets and object boundaries are validated against the retained table by <see cref="Parse"/>;
/// object bytes are slice views and are never copied, so the caller must keep the table alive. An
/// empty INDEX is a valid two-byte structure, and the instance is immutable.
/// </remarks>
internal sealed class CffIndex
{
    /// <summary>The shared empty INDEX.</summary>
    internal static CffIndex Empty { get; } =
        new(ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<int>.Empty);

    private const int COUNT_LENGTH = 2;
    private const int OFF_SIZE_LENGTH = 1;
    private const int MIN_OFF_SIZE = 1;
    private const int MAX_OFF_SIZE = 4;

    private readonly ReadOnlyMemory<byte> _objects;
    private readonly ReadOnlyMemory<int> _offsets;

    private CffIndex(ReadOnlyMemory<byte> objects, ReadOnlyMemory<int> offsets)
    {
        _objects = objects;
        _offsets = offsets;
    }

    /// <summary>Gets the number of objects.</summary>
    internal int Count => _offsets.IsEmpty ? 0 : _offsets.Length - 1;

    /// <summary>Parses one CFF INDEX at an offset inside a table.</summary>
    internal static CffIndex Parse(
        ReadOnlyMemory<byte> table,
        int offset,
        string context,
        out int end
    )
    {
        SfntReader reader = new(table.Span, context);
        reader.Require(offset, COUNT_LENGTH);
        int count = reader.ReadUInt16(offset);
        if (count == 0)
        {
            end = offset + COUNT_LENGTH;
            return Empty;
        }

        reader.Require(offset + COUNT_LENGTH, OFF_SIZE_LENGTH);
        int offSize = reader.ReadByte(offset + COUNT_LENGTH);
        if (offSize is < MIN_OFF_SIZE or > MAX_OFF_SIZE)
        {
            throw new FontFormatException(
                $"{context} uses the offset size {offSize}; the supported range is {MIN_OFF_SIZE} to {MAX_OFF_SIZE}"
            );
        }

        int offsetsOffset = offset + COUNT_LENGTH + OFF_SIZE_LENGTH;
        long offsetsLength = (long)(count + 1) * offSize;
        if (!reader.Fits(offsetsOffset, offsetsLength))
        {
            throw new FontFormatException(
                $"{context} is truncated: the {count + 1} {offSize}-byte offsets at offset {offsetsOffset} are outside the {reader.Length}-byte table"
            );
        }

        int dataOffset = offsetsOffset + (int)offsetsLength;
        long lastOffset = ReadOffset(reader, offsetsOffset + (count * offSize), offSize);
        if (lastOffset < 1)
        {
            throw new FontFormatException(
                $"{context} declares the final object offset {lastOffset}; INDEX offsets are one-based"
            );
        }

        long dataLength = lastOffset - 1;
        if (dataLength > reader.Length - dataOffset)
        {
            throw new FontFormatException(
                $"{context} declares {dataLength} byte(s) of object data at offset {dataOffset}; the table has {reader.Length - dataOffset} byte(s) left"
            );
        }

        int[] offsets = new int[count + 1];
        long previous = MIN_OFF_SIZE;
        for (int index = 0; index <= count; index++)
        {
            long value = ReadOffset(reader, offsetsOffset + (index * offSize), offSize);
            if (index == 0 && value != MIN_OFF_SIZE)
            {
                throw new FontFormatException(
                    $"{context} starts its object data at offset {value}; the first offset must be 1"
                );
            }

            if (value < MIN_OFF_SIZE || value > lastOffset)
            {
                throw new FontFormatException(
                    $"{context} has the object offset {value} at entry {index}; offsets must be in 1..{lastOffset}"
                );
            }

            if (value < previous)
            {
                throw new FontFormatException(
                    $"{context} has decreasing object offsets at entry {index} ({value} after {previous})"
                );
            }

            previous = value;
            offsets[index] = (int)(value - 1);
        }

        end = dataOffset + (int)dataLength;
        return new CffIndex(table.Slice(dataOffset, (int)dataLength), offsets);
    }

    /// <summary>Gets one object's bytes.</summary>
    internal ReadOnlyMemory<byte> Get(int index)
    {
        if ((uint)index >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The INDEX has {Count} object(s)."
            );
        }

        ReadOnlySpan<int> offsets = _offsets.Span;
        int start = offsets[index];
        return _objects.Slice(start, offsets[index + 1] - start);
    }

    private static long ReadOffset(SfntReader reader, int offset, int size)
    {
        long value = 0;
        for (int index = 0; index < size; index++)
        {
            value = (value << 8) | reader.ReadByte(offset + index);
        }

        return value;
    }
}
