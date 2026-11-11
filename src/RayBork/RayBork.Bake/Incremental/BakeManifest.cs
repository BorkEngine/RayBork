using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using RayBork.Bake.Baking;

namespace RayBork.Bake.Incremental;

/// <summary>The schema-1 incremental manifest data model.</summary>
internal sealed class BakeManifest {
    /// <summary>The manifest schema written and read by this tool.</summary>
    internal const int CURRENT_VERSION = 1;

    /// <summary>Initializes an empty manifest for JSON deserialization.</summary>
    public BakeManifest() {
    }

    /// <summary>Gets or sets the schema version.</summary>
    public int Schema { get; set; } = CURRENT_VERSION;

    /// <summary>Gets or sets the bake tool version.</summary>
    public string ToolVersion { get; set; } = string.Empty;

    /// <summary>Gets or sets the configuration hash.</summary>
    public string ConfigurationHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the compression mode name.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-pack records.</summary>
    public List<BakeManifestPack> Packs { get; set; } = [];
}

/// <summary>One pack's incremental state.</summary>
internal sealed class BakeManifestPack {
    /// <summary>Initializes an empty pack record for JSON deserialization.</summary>
    public BakeManifestPack() {
    }

    /// <summary>Gets or sets the output-root-relative pack path.</summary>
    public string PackPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the project-relative asset root.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Gets or sets the logical layout hash of every entry.</summary>
    public string LayoutHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the physical pack file hash.</summary>
    public string PackHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-source records.</summary>
    public List<BakeManifestSource> Sources { get; set; } = [];
}

/// <summary>One source's incremental state.</summary>
internal sealed class BakeManifestSource {
    /// <summary>Initializes an empty source record for JSON deserialization.</summary>
    public BakeManifestSource() {
    }

    /// <summary>Gets or sets the stable source identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the source kind.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Gets or sets the processor name, or <see langword="null"/> for data.</summary>
    public string? Processor { get; set; }

    /// <summary>Gets or sets the tracked inputs.</summary>
    public List<BakeManifestInput> Inputs { get; set; } = [];

    /// <summary>Gets or sets the output entries.</summary>
    public List<BakeManifestOutput> Outputs { get; set; } = [];
}

/// <summary>One tracked input path and content hash.</summary>
internal sealed class BakeManifestInput {
    /// <summary>Initializes an empty input record for JSON deserialization.</summary>
    public BakeManifestInput() {
    }

    /// <summary>Gets or sets the project-relative path with <c>/</c> separators.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the content hash.</summary>
    public string Hash { get; set; } = string.Empty;
}

/// <summary>One output entry's path, type, alignment, and payload hash.</summary>
internal sealed class BakeManifestOutput {
    /// <summary>Initializes an empty output record for JSON deserialization.</summary>
    public BakeManifestOutput() {
    }

    /// <summary>Gets or sets the canonical path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the numeric <c>AssetEntryType</c>.</summary>
    public int Type { get; set; }

    /// <summary>Gets or sets the payload alignment.</summary>
    public uint Alignment { get; set; }

    /// <summary>Gets or sets the payload hash.</summary>
    public string Hash { get; set; } = string.Empty;
}

/// <summary>Computes the pack logical-layout hash.</summary>
internal static class BakeLayoutHash {
    /// <summary>
    /// Hashes the ordinal entry set by path, type, alignment, and payload hash. Compression and
    /// physical offsets are deliberately excluded.
    /// </summary>
    /// <param name="entries">The entries in any order.</param>
    /// <returns>The 64-character digest.</returns>
    internal static string Compute(IReadOnlyList<BakedEntry> entries) {
        ArgumentNullException.ThrowIfNull(entries);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(long)];
        foreach (BakedEntry entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal)) {
            Append(hash, length, entry.Path);
            Append(hash, length, ((ushort)entry.Type).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, length, entry.Alignment.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, length, BakeHash.HashBytes(entry.Payload));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, Span<byte> length, string value) {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
