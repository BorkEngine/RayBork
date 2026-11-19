namespace RayBork.Internal;

/// <summary>The horizontal glyph pairs of a version 0 <c>kern</c> table.</summary>
/// <remarks>
/// Only horizontal format 0 subtables contribute, and the last applicable subtable wins a
/// duplicate pair. Lookups are binary searches over one sorted key array; values are in font units
/// and the instance is immutable.
/// </remarks>
internal sealed class KernPairs
{
    /// <summary>The maximum number of horizontal pairs the legacy table may declare.</summary>
    internal const int MAX_PAIR_COUNT = 1_000_000;

    private readonly uint[] _keys;
    private readonly int[] _values;

    private KernPairs(uint[] keys, int[] values)
    {
        _keys = keys;
        _values = values;
    }

    /// <summary>Parses the horizontal format 0 kerning pairs of a <c>kern</c> table.</summary>
    internal static KernPairs? TryParse(ReadOnlyMemory<byte> kern, int glyphCount)
    {
        SfntReader reader = new(kern.Span, "the 'kern' table");
        if (reader.Length < 4)
        {
            throw new FontFormatException(
                $"the 'kern' table is {reader.Length} byte(s) long; the version and subtable count are required"
            );
        }

        if (reader.ReadUInt16(0) != 0)
        {
            return null;
        }

        int subtableCount = reader.ReadUInt16(2);
        int position = 4;
        long pairCount = 0;
        for (int index = 0; index < subtableCount; index++)
        {
            reader.Require(position, 6);
            int subtableVersion = reader.ReadUInt16(position);
            int length = reader.ReadUInt16(position + 2);
            int coverage = reader.ReadUInt16(position + 4);
            if (length < 6)
            {
                throw new FontFormatException(
                    $"the 'kern' subtable {index} declares only {length} byte(s)"
                );
            }

            reader.Require(position, length);
            if (IsHorizontalFormat0(subtableVersion, coverage))
            {
                if (length < 14)
                {
                    throw new FontFormatException(
                        $"the 'kern' subtable {index} declares only {length} byte(s); the format 0 header needs 14"
                    );
                }

                int pairs = reader.ReadUInt16(position + 6);
                if (14L + (6L * pairs) > length)
                {
                    throw new FontFormatException(
                        $"the 'kern' subtable {index} declares {pairs} pair(s) but only {length} byte(s)"
                    );
                }

                pairCount += pairs;
                if (pairCount > MAX_PAIR_COUNT)
                {
                    throw new FontUnsupportedException(
                        $"the 'kern' table declares more than {MAX_PAIR_COUNT} pairs"
                    );
                }
            }

            position += length;
        }

        if (pairCount == 0)
        {
            return null;
        }

        KernPairRecord[] records = new KernPairRecord[(int)pairCount];
        int order = 0;
        position = 4;
        for (int index = 0; index < subtableCount; index++)
        {
            int subtableVersion = reader.ReadUInt16(position);
            int length = reader.ReadUInt16(position + 2);
            int coverage = reader.ReadUInt16(position + 4);
            if (IsHorizontalFormat0(subtableVersion, coverage))
            {
                int pairs = reader.ReadUInt16(position + 6);
                int pairOffset = position + 14;
                for (int pair = 0; pair < pairs; pair++)
                {
                    int record = pairOffset + (6 * pair);
                    int left = reader.ReadUInt16(record);
                    int right = reader.ReadUInt16(record + 2);
                    if (left >= glyphCount || right >= glyphCount)
                    {
                        throw new FontFormatException(
                            $"the 'kern' table pair ({left}, {right}) references a glyph outside the face's {glyphCount} glyph(s)"
                        );
                    }

                    records[order] = new KernPairRecord(
                        ((uint)left << 16) | (uint)right,
                        reader.ReadInt16(record + 4),
                        order
                    );
                    order++;
                }
            }

            position += length;
        }

        Array.Sort(records, static (first, second) =>
        {
            int byKey = first.Key.CompareTo(second.Key);
            return byKey != 0 ? byKey : first.Order.CompareTo(second.Order);
        });

        int unique = 0;
        int recordIndex = 0;
        while (recordIndex < records.Length)
        {
            int last = recordIndex;
            while (last + 1 < records.Length && records[last + 1].Key == records[recordIndex].Key)
            {
                last++;
            }

            records[unique++] = records[last];
            recordIndex = last + 1;
        }

        uint[] keys = new uint[unique];
        int[] values = new int[unique];
        for (int index = 0; index < unique; index++)
        {
            keys[index] = records[index].Key;
            values[index] = records[index].Value;
        }

        return new KernPairs(keys, values);
    }

    /// <summary>Finds one horizontal pair and returns its adjustment in font units.</summary>
    /// <returns><see langword="true"/> when the table declares the pair.</returns>
    internal bool TryGet(int leftGlyphId, int rightGlyphId, out int value)
    {
        uint key = ((uint)leftGlyphId << 16) | (uint)rightGlyphId;
        int low = 0;
        int high = _keys.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            if (_keys[middle] < key)
            {
                low = middle + 1;
            }
            else if (_keys[middle] > key)
            {
                high = middle - 1;
            }
            else
            {
                value = _values[middle];
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool IsHorizontalFormat0(int subtableVersion, int coverage) =>
        subtableVersion == 0 && (coverage & 0x0007) == 0x0001 && (coverage >> 8) == 0;

    private readonly struct KernPairRecord
    {
        internal KernPairRecord(uint key, int value, int order)
        {
            Key = key;
            Value = value;
            Order = order;
        }

        internal uint Key { get; }

        internal int Value { get; }

        internal int Order { get; }
    }
}
