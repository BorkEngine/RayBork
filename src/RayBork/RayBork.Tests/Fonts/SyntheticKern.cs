using System.Buffers.Binary;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Encodes a synthetic version 0 <c>kern</c> table with any number of subtables, including
/// unsupported subtable versions and coverage flags for the filtering tests.
/// </summary>
/// <remarks>
/// A subtable whose version is not 0 or whose coverage format is not the horizontal format 0 is
/// encoded as a header-only subtable: the parser skips it because those variants are unsupported, and
/// a real font can carry them.
/// </remarks>
internal static class SyntheticKern
{
    internal static byte[] Build(params SyntheticKernSubtable[] subtables)
    {
        byte[][] bodies = new byte[subtables.Length][];
        int totalLength = 4;
        for (int index = 0; index < subtables.Length; index++)
        {
            bodies[index] = BuildSubtable(subtables[index]);
            totalLength += bodies[index].Length;
        }

        byte[] kern = new byte[totalLength];
        BinaryPrimitives.WriteUInt16BigEndian(kern, 0);
        BinaryPrimitives.WriteUInt16BigEndian(kern.AsSpan(2), (ushort)subtables.Length);
        int offset = 4;
        foreach (byte[] body in bodies)
        {
            body.CopyTo(kern, offset);
            offset += body.Length;
        }

        return kern;
    }

    private static byte[] BuildSubtable(SyntheticKernSubtable subtable)
    {
        bool supported = subtable.Version == 0 && (subtable.Coverage >> 8) == 0;
        if (!supported)
        {
            byte[] header = new byte[6];
            BinaryPrimitives.WriteUInt16BigEndian(header, (ushort)subtable.Version);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(2), 6);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)subtable.Coverage);
            return header;
        }

        (int Left, int Right, int Value)[] pairs = [.. subtable.Pairs];
        Array.Sort(pairs, static (first, second) =>
        {
            int byLeft = first.Left.CompareTo(second.Left);
            return byLeft != 0 ? byLeft : first.Right.CompareTo(second.Right);
        });
        int length = 14 + (6 * pairs.Length);
        byte[] body = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(body, (ushort)subtable.Version);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(4), (ushort)subtable.Coverage);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(6), (ushort)pairs.Length);
        int searchRange = 6;
        int entrySelector = 0;
        while ((searchRange * 2) <= (pairs.Length * 6))
        {
            searchRange *= 2;
            entrySelector++;
        }

        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(8), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(10), (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(
            body.AsSpan(12),
            (ushort)((pairs.Length * 6) - searchRange)
        );
        for (int index = 0; index < pairs.Length; index++)
        {
            int offset = 14 + (6 * index);
            BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(offset), (ushort)pairs[index].Left);
            BinaryPrimitives.WriteUInt16BigEndian(
                body.AsSpan(offset + 2),
                (ushort)pairs[index].Right
            );
            BinaryPrimitives.WriteInt16BigEndian(
                body.AsSpan(offset + 4),
                (short)pairs[index].Value
            );
        }

        return body;
    }
}

/// <summary>One synthetic <c>kern</c> subtable.</summary>
internal readonly struct SyntheticKernSubtable
{
    internal SyntheticKernSubtable(
        int version,
        int coverage,
        params (int Left, int Right, int Value)[] pairs
    )
    {
        Version = version;
        Coverage = coverage;
        Pairs = pairs;
    }

    internal int Version { get; }

    internal int Coverage { get; }

    internal (int Left, int Right, int Value)[] Pairs { get; }
}
