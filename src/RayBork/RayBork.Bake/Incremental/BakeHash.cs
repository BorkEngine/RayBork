using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RayBork.Bake.Incremental;

/// <summary>SHA-256 hashing helpers for the incremental manifest.</summary>
internal static class BakeHash
{
    /// <summary>Hashes bytes to a 64-character uppercase hex digest.</summary>
    internal static string HashBytes(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));

    /// <summary>Hashes a file's contents to a 64-character uppercase hex digest.</summary>
    internal static string HashFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan
        );
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Hashes ordered UTF-8 fields with explicit length framing.</summary>
    internal static string HashTextFields(params string[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (string field in fields)
        {
            ArgumentNullException.ThrowIfNull(field);
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt64LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
