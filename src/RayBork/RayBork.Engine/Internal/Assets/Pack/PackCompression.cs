using System.IO.Compression;
using System.IO.Hashing;

namespace RayBork.Internal;

/// <summary>
/// The Zstandard codec and XXH hash helpers shared by the pack reader and writer.
/// </summary>
/// <remarks>
/// Compressed payloads use maximum-quality Zstandard with no trailing checksum; integrity comes from
/// the XXH3-64 hash in the index. No Zstandard type appears in a public signature.
/// </remarks>
internal static class PackCompression {
    /// <summary>
    /// The initial decompression buffer size. It grows only as decompressed bytes actually arrive,
    /// so a corrupt declared size cannot force a large up-front allocation.
    /// </summary>
    private const int INITIAL_DECOMPRESSION_BUFFER_LENGTH = 64 * 1024;

    /// <summary>
    /// Compresses a payload with maximum-quality Zstandard and no checksum.
    /// </summary>
    /// <param name="payload">The uncompressed bytes.</param>
    /// <returns>The compressed bytes.</returns>
    internal static byte[] Compress(ReadOnlySpan<byte> payload) {
        using var output = new MemoryStream();
        using (var compressor = new ZstandardStream(
            output,
            new ZstandardCompressionOptions {
                Quality = ZstandardCompressionOptions.MaxQuality,
                AppendChecksum = false,
            },
            leaveOpen: true)) {
            compressor.Write(payload);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Wraps a bounded stored-payload stream in a Zstandard decompressor.
    /// </summary>
    /// <param name="source">The stored bytes; the returned stream owns it.</param>
    /// <returns>A decompressing stream that owns <paramref name="source"/>.</returns>
    internal static Stream OpenDecompressionStream(Stream source)
        => new ZstandardStream(source, CompressionMode.Decompress, leaveOpen: false);

    /// <summary>
    /// Computes the XXH32 hash (seed <c>0</c>) of the index bytes.
    /// </summary>
    /// <param name="index">The serialized index.</param>
    /// <returns>The 32-bit index hash.</returns>
    internal static uint ComputeIndexHash(ReadOnlySpan<byte> index)
        => XxHash32.HashToUInt32(index, 0);

    /// <summary>
    /// Computes the XXH3-64 hash (seed <c>0</c>) of the stored payload bytes.
    /// </summary>
    /// <param name="storedPayload">The bytes as stored in the pack.</param>
    /// <returns>The 64-bit payload hash.</returns>
    internal static ulong ComputePayloadHash(ReadOnlySpan<byte> storedPayload)
        => XxHash3.HashToUInt64(storedPayload, 0);

    /// <summary>
    /// Decompresses a stored payload to exactly the declared byte count.
    /// </summary>
    /// <remarks>
    /// The output buffer grows only as bytes arrive and never exceeds the declared size, so a
    /// corrupt size cannot force an unbounded allocation. A stream that ends early or yields one
    /// byte more than declared is corruption.
    /// </remarks>
    /// <param name="storedPayload">The stored Zstandard bytes.</param>
    /// <param name="expectedSize">The declared decompressed byte count.</param>
    /// <returns>The decompressed bytes.</returns>
    /// <exception cref="InvalidDataException">
    /// The stored bytes are not valid Zstandard, or the decompressed size differs.
    /// </exception>
    internal static byte[] DecompressExactly(byte[] storedPayload, int expectedSize) {
        using var source = new MemoryStream(storedPayload, writable: false);
        using Stream decoder = OpenDecompressionStream(source);

        if (expectedSize == 0) {
            Span<byte> emptyProbe = stackalloc byte[1];
            if (decoder.Read(emptyProbe) != 0) {
                throw new InvalidDataException(
                    "the payload decompressed to more than the declared 0 bytes");
            }

            return [];
        }

        byte[] result = new byte[Math.Min(expectedSize, INITIAL_DECOMPRESSION_BUFFER_LENGTH)];
        int total = 0;
        while (total < expectedSize) {
            if (total == result.Length) {
                int grown = (int)Math.Min(expectedSize, (long)result.Length * 2);
                Array.Resize(ref result, grown);
            }

            int read = decoder.Read(result, total, result.Length - total);
            if (read == 0) {
                throw new InvalidDataException(
                    $"the payload decompressed to {total} of the declared {expectedSize} bytes");
            }

            total += read;
        }

        Span<byte> probe = stackalloc byte[1];
        if (decoder.Read(probe) != 0) {
            throw new InvalidDataException(
                $"the payload decompressed to more than the declared {expectedSize} bytes");
        }

        return result;
    }
}
