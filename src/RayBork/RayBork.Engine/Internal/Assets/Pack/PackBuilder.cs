namespace RayBork.Internal;

/// <summary>Writes a RAYBORKP v1 pack for tooling and tests.</summary>
/// <remarks>
/// Entries are sorted by ordinal canonical path at build time and the builder is not thread-safe.
/// Invalid entries throw <see cref="ArgumentException"/>; pack bytes are not byte-stable.
/// </remarks>
internal sealed class PackBuilder
{
    private readonly List<PackSourceEntry> _entries = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly PackCompressionPolicy _compression;
    private readonly string _displayPath;

    /// <summary>Initializes an empty builder.</summary>
    internal PackBuilder(string displayPath, PackCompressionPolicy compression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        if (compression is not PackCompressionPolicy.Debug and not PackCompressionPolicy.Release)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compression),
                compression,
                "Unknown pack compression policy."
            );
        }

        _displayPath = displayPath;
        _compression = compression;
    }

    /// <summary>Initializes a builder with queued entries.</summary>
    internal PackBuilder(
        string displayPath,
        PackCompressionPolicy compression,
        IEnumerable<PackSourceEntry> entries
    )
        : this(displayPath, compression)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (PackSourceEntry entry in entries)
        {
            Add(entry);
        }
    }

    /// <summary>Gets the pack identifier used in diagnostics.</summary>
    internal string DisplayPath => _displayPath;

    /// <summary>Gets the payload compression policy.</summary>
    internal PackCompressionPolicy Compression => _compression;

    /// <summary>Gets the queued entries in insertion order.</summary>
    internal IReadOnlyList<PackSourceEntry> Entries => _entries;

    /// <summary>Queues one payload and copies its bytes.</summary>
    /// <remarks>
    /// The path must be canonical without a leading slash; alignment is 1, 16, 64, or 256.
    /// </remarks>
    internal void Add(
        string path,
        AssetEntryType type,
        ReadOnlySpan<byte> payload,
        uint alignment = 1
    )
    {
        ArgumentNullException.ThrowIfNull(path);
        ValidateNewEntry(path, type, alignment);

        _paths.Add(path);
        _entries.Add(new PackSourceEntry(path, type, payload, alignment));
    }

    /// <summary>Queues an existing source entry without copying its payload.</summary>
    internal void Add(PackSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateNewEntry(entry.Path, entry.Type, entry.Alignment);

        _paths.Add(entry.Path);
        _entries.Add(entry);
    }

    /// <summary>Writes the complete pack to a stream and leaves the stream open.</summary>
    internal void WriteTo(Stream destination) =>
        PackLayout.Create(_entries, _compression, _displayPath).WriteTo(destination);

    /// <summary>Builds the complete pack in one array.</summary>
    /// <exception cref="InvalidOperationException">The pack is too large for one array.</exception>
    internal byte[] Build() => PackLayout.Create(_entries, _compression, _displayPath).ToArray();

    /// <summary>Writes the complete pack to an overwritten file.</summary>
    /// <param name="path">
    /// The filesystem path; relative paths resolve against the process directory.
    /// </param>
    internal void Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "The pack path must not be empty or whitespace.",
                nameof(path)
            );
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid pack path '{path}': {exception.Message}",
                nameof(path),
                exception
            );
        }

        PackLayout layout = PackLayout.Create(_entries, _compression, _displayPath);
        using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan
        );
        layout.WriteTo(stream);
    }

    private void ValidateNewEntry(string path, AssetEntryType type, uint alignment)
    {
        if (!AssetPath.TryValidateCanonical(path, out string? pathReason))
        {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' has the non-canonical path '{path}': {pathReason}.",
                nameof(path)
            );
        }

        if (!PackFormat.IsValidEntryType(type))
        {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                $"Asset pack '{_displayPath}' entry '{path}' has an unknown asset type."
            );
        }

        if (!PackFormat.IsValidAlignment(alignment))
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                $"Asset pack '{_displayPath}' entry '{path}' has an invalid alignment; v1 accepts "
                    + "1, 16, 64, or 256."
            );
        }

        if ((uint)_entries.Count >= PackFormat.MAX_ENTRY_COUNT)
        {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' cannot hold more than "
                    + $"{PackFormat.MAX_ENTRY_COUNT} entries.",
                nameof(path)
            );
        }

        if (_paths.Contains(path))
        {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' already has the path '{path}'.",
                nameof(path)
            );
        }
    }
}
