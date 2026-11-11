namespace RayBork.Internal;

/// <summary>
/// Writes a RAYBORKP v1 pack for tooling and tests.
/// </summary>
/// <remarks>
/// <para>
/// Entries are stored in memory and sorted by ordinal canonical path when the pack is built. The
/// builder is not thread-safe. Adding entries after a build is allowed; each build recomputes the
/// stored payloads, so pack bytes are not stable across calls and are not deterministic across
/// Zstandard versions.
/// </para>
/// <para>
/// The builder validates every path, type, alignment, duplicate, entry-count limit, index-length
/// limit, and offset before writing. An invalid entry is a programming error and throws an
/// <see cref="ArgumentException"/>; the reader's corruption diagnostics are separate.
/// </para>
/// </remarks>
internal sealed class PackBuilder {
    private readonly List<PackSourceEntry> _entries = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly PackCompressionPolicy _compression;
    private readonly string _displayPath;

    /// <summary>
    /// Initializes an empty builder.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="compression">The payload compression policy.</param>
    /// <exception cref="ArgumentException"><paramref name="displayPath"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compression"/> is not a known policy.</exception>
    internal PackBuilder(string displayPath, PackCompressionPolicy compression) {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        if (compression is not PackCompressionPolicy.Debug
            and not PackCompressionPolicy.Release) {
            throw new ArgumentOutOfRangeException(
                nameof(compression),
                compression,
                "Unknown pack compression policy.");
        }

        _displayPath = displayPath;
        _compression = compression;
    }

    /// <summary>
    /// Initializes a builder with a queue of entries.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="compression">The payload compression policy.</param>
    /// <param name="entries">The entries to add, in any order.</param>
    /// <exception cref="ArgumentException">An entry is invalid or duplicates an earlier path.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An entry type or alignment is outside its v1 set.</exception>
    internal PackBuilder(
        string displayPath,
        PackCompressionPolicy compression,
        IEnumerable<PackSourceEntry> entries)
        : this(displayPath, compression) {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (PackSourceEntry entry in entries) {
            Add(entry);
        }
    }

    /// <summary>Gets the pack identifier used in diagnostics.</summary>
    internal string DisplayPath => _displayPath;

    /// <summary>Gets the payload compression policy.</summary>
    internal PackCompressionPolicy Compression => _compression;

    /// <summary>Gets the queued entries in insertion order.</summary>
    internal IReadOnlyList<PackSourceEntry> Entries => _entries;

    /// <summary>
    /// Queues one payload.
    /// </summary>
    /// <param name="path">The canonical asset path, without a leading slash.</param>
    /// <param name="type">The entry asset type.</param>
    /// <param name="payload">The uncompressed payload bytes; they are copied.</param>
    /// <param name="alignment">The required payload alignment; one of 1, 16, 64, or 256.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The path is not canonical, or the path is already queued.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The type or alignment is outside its v1 set.
    /// </exception>
    internal void Add(
        string path,
        AssetEntryType type,
        ReadOnlySpan<byte> payload,
        uint alignment = 1) {
        ArgumentNullException.ThrowIfNull(path);
        ValidateNewEntry(path, type, alignment);

        _paths.Add(path);
        _entries.Add(new PackSourceEntry(path, type, payload, alignment));
    }

    /// <summary>
    /// Queues an existing source entry without copying its payload.
    /// </summary>
    /// <param name="entry">The entry to queue.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">The entry path is invalid or already queued.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The entry type or alignment is outside its v1 set.</exception>
    internal void Add(PackSourceEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateNewEntry(entry.Path, entry.Type, entry.Alignment);

        _paths.Add(entry.Path);
        _entries.Add(entry);
    }

    /// <summary>
    /// Writes the complete pack to a stream and leaves the stream open.
    /// </summary>
    /// <param name="destination">The writable destination stream.</param>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is not writable.</exception>
    /// <exception cref="ArgumentException">A queued entry is invalid or the pack exceeds a v1 limit.</exception>
    internal void WriteTo(Stream destination)
        => PackLayout.Create(_entries, _compression, _displayPath).WriteTo(destination);

    /// <summary>
    /// Builds the complete pack in one array.
    /// </summary>
    /// <returns>The pack bytes.</returns>
    /// <exception cref="ArgumentException">A queued entry is invalid or the pack exceeds a v1 limit.</exception>
    /// <exception cref="InvalidOperationException">The pack is too large for one array.</exception>
    internal byte[] Build() => PackLayout.Create(_entries, _compression, _displayPath).ToArray();

    /// <summary>
    /// Writes the complete pack to an overwritten file.
    /// </summary>
    /// <param name="path">The filesystem path; relative paths resolve against the process directory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or not a valid path.</exception>
    /// <exception cref="ArgumentException">A queued entry is invalid or the pack exceeds a v1 limit.</exception>
    internal void Write(string path) {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("The pack path must not be empty or whitespace.", nameof(path));
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(path);
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            throw new ArgumentException(
                $"Invalid pack path '{path}': {exception.Message}",
                nameof(path),
                exception);
        }

        PackLayout layout = PackLayout.Create(_entries, _compression, _displayPath);
        using var stream = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        layout.WriteTo(stream);
    }

    private void ValidateNewEntry(string path, AssetEntryType type, uint alignment) {
        if (!AssetPath.TryValidateCanonical(path, out string? pathReason)) {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' has the non-canonical path '{path}': {pathReason}.",
                nameof(path));
        }

        if (!PackFormat.IsValidEntryType(type)) {
            throw new ArgumentOutOfRangeException(
                nameof(type),
                type,
                $"Asset pack '{_displayPath}' entry '{path}' has an unknown asset type.");
        }

        if (!PackFormat.IsValidAlignment(alignment)) {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                alignment,
                $"Asset pack '{_displayPath}' entry '{path}' has an invalid alignment; v1 accepts "
                    + "1, 16, 64, or 256.");
        }

        if ((uint)_entries.Count >= PackFormat.MAX_ENTRY_COUNT) {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' cannot hold more than "
                    + $"{PackFormat.MAX_ENTRY_COUNT} entries.",
                nameof(path));
        }

        if (_paths.Contains(path)) {
            throw new ArgumentException(
                $"Asset pack '{_displayPath}' already has the path '{path}'.",
                nameof(path));
        }
    }
}
