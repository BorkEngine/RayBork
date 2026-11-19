using System.IO.Compression;
using System.IO.Hashing;

namespace RayBork.Internal;

/// <summary>Zstandard codec and XXH hash helpers shared by the pack reader and writer.</summary>
/// <remarks>
/// Compressed payloads use maximum-quality Zstandard with no checksum; integrity comes from the
/// XXH3-64 hash in the index.
/// </remarks>
internal static class PackCompression
{
    /// <summary>The initial decompression buffer size, grown as bytes arrive.</summary>
    private const int INITIAL_DECOMPRESSION_BUFFER_LENGTH = 64 * 1024;

    /// <summary>Compresses a payload with maximum-quality Zstandard and no checksum.</summary>
    internal static byte[] Compress(ReadOnlySpan<byte> payload)
    {
        using var output = new MemoryStream();
        using (
            var compressor = new ZstandardStream(
                output,
                new ZstandardCompressionOptions
                {
                    Quality = ZstandardCompressionOptions.MaxQuality,
                    AppendChecksum = false,
                },
                leaveOpen: true
            )
        )
        {
            compressor.Write(payload);
        }

        return output.ToArray();
    }

    /// <summary>Wraps a bounded stored stream in a decompressor that owns it.</summary>
    internal static Stream OpenDecompressionStream(Stream source) =>
        new ZstandardStream(source, CompressionMode.Decompress, leaveOpen: false);

    /// <summary>Computes the XXH32 hash (seed 0) of the index bytes.</summary>
    internal static uint ComputeIndexHash(ReadOnlySpan<byte> index) =>
        XxHash32.HashToUInt32(index, 0);

    /// <summary>Computes the XXH3-64 hash (seed 0) of the stored payload bytes.</summary>
    internal static ulong ComputePayloadHash(ReadOnlySpan<byte> storedPayload) =>
        XxHash3.HashToUInt64(storedPayload, 0);

    /// <summary>Decompresses a stored payload to exactly the declared byte count.</summary>
    /// <remarks>
    /// The output buffer grows only as bytes arrive and never exceeds the declared size. A stream that
    /// ends early or yields one byte more than declared is corruption.
    /// </remarks>
    /// <exception cref="InvalidDataException">The data is invalid or the size differs.</exception>
    internal static byte[] DecompressExactly(byte[] storedPayload, int expectedSize)
    {
        using var source = new MemoryStream(storedPayload, writable: false);
        using Stream decoder = OpenDecompressionStream(source);

        if (expectedSize == 0)
        {
            Span<byte> emptyProbe = stackalloc byte[1];
            if (decoder.Read(emptyProbe) != 0)
            {
                throw new InvalidDataException(
                    "the payload decompressed to more than the declared 0 bytes"
                );
            }

            return [];
        }

        byte[] result = new byte[Math.Min(expectedSize, INITIAL_DECOMPRESSION_BUFFER_LENGTH)];
        int total = 0;
        while (total < expectedSize)
        {
            if (total == result.Length)
            {
                int grown = (int)Math.Min(expectedSize, (long)result.Length * 2);
                Array.Resize(ref result, grown);
            }

            int read = decoder.Read(result, total, result.Length - total);
            if (read == 0)
            {
                throw new InvalidDataException(
                    $"the payload decompressed to {total} of the declared {expectedSize} bytes"
                );
            }

            total += read;
        }

        Span<byte> probe = stackalloc byte[1];
        if (decoder.Read(probe) != 0)
        {
            throw new InvalidDataException(
                $"the payload decompressed to more than the declared {expectedSize} bytes"
            );
        }

        return result;
    }
}
