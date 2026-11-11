using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.MemoryMappedFiles;

namespace RayBork.Internal;

/// <summary>
/// An opened, validated RAYBORKP v1 pack.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open(string)"/> memory-maps the file read-only and keeps the mapping alive until
/// <see cref="Dispose"/>; the runtime mounts packs for the process lifetime and normally does not
/// dispose them. <see cref="Open(ReadOnlyMemory{byte}, string)"/> serves the embedded built-in pack
/// without copying its bytes and uses the display path in every diagnostic.
/// </para>
/// <para>
/// Structural validation runs once at open. Payload hashes and decompression are lazy: each
/// <see cref="ReadPayload"/> copies the stored bytes under an I/O lock and then verifies and
/// decompresses outside it, so concurrent readers are safe and a failed read leaves the archive
/// usable. Disposal is idempotent; a payload read or entry lookup after disposal throws
/// <see cref="ObjectDisposedException"/>.
/// </para>
/// </remarks>
internal sealed class PackArchive : IDisposable {
    private readonly string _displayPath;
    private readonly long _length;
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly MemoryMappedFile? _mappedFile;
    private readonly MemoryMappedViewAccessor? _view;
    private readonly Lock _ioLock = new();
    private PackEntry[] _entries = [];
    private Dictionary<string, PackEntry> _entriesByPath = new(StringComparer.Ordinal);
    private unsafe byte* _pointer;
    private bool _pointerAcquired;
    private bool _disposed;

    private PackArchive(
        ReadOnlyMemory<byte> memory,
        MemoryMappedFile? mappedFile,
        MemoryMappedViewAccessor? view,
        long length,
        string displayPath) {
        _memory = memory;
        _mappedFile = mappedFile;
        _view = view;
        _length = length;
        _displayPath = displayPath;
    }

    /// <summary>Gets the pack identifier used in diagnostics.</summary>
    internal string DisplayPath => _displayPath;

    /// <summary>Gets the pack length in bytes.</summary>
    internal long Length => _length;

    /// <summary>Gets the entries in ordinal path order.</summary>
    internal IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>
    /// Opens and validates a pack file read-only through a memory map.
    /// </summary>
    /// <param name="path">The filesystem path; relative paths resolve against the process directory.</param>
    /// <returns>The validated archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, whitespace, or invalid.</exception>
    /// <exception cref="AssetPackException">
    /// The file is missing, unreadable, truncated, or not a valid v1 pack.
    /// </exception>
    internal static PackArchive Open(string path) {
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

        FileStream stream;
        try {
            stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            throw PackFailure.ReadFailed(fullPath, "the file could not be opened", exception);
        }

        long length;
        try {
            length = stream.Length;
        } catch (Exception exception) when (exception is IOException or NotSupportedException
            or ObjectDisposedException) {
            stream.Dispose();
            throw PackFailure.ReadFailed(fullPath, "the file length could not be read", exception);
        }

        if (length < PackFormat.PREFIX_LENGTH) {
            stream.Dispose();
            throw PackFailure.ReadFailed(
                fullPath,
                $"the file is {length} bytes, shorter than the {PackFormat.PREFIX_LENGTH}-byte "
                    + "header");
        }

        MemoryMappedFile mappedFile;
        try {
            mappedFile = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read,
                HandleInheritability.None,
                leaveOpen: false);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            stream.Dispose();
            throw PackFailure.ReadFailed(fullPath, "the file could not be memory-mapped", exception);
        }

        MemoryMappedViewAccessor view;
        try {
            view = mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException) {
            mappedFile.Dispose();
            throw PackFailure.ReadFailed(fullPath, "the file could not be memory-mapped", exception);
        }

        return Create(default, mappedFile, view, length, fullPath);
    }

    /// <summary>
    /// Opens and validates an embedded pack from memory.
    /// </summary>
    /// <param name="pack">The complete pack bytes, not copied or owned.</param>
    /// <param name="displayPath">The non-empty pack identifier used in diagnostics.</param>
    /// <returns>The validated archive.</returns>
    /// <exception cref="ArgumentException"><paramref name="displayPath"/> is empty or whitespace.</exception>
    /// <exception cref="AssetPackException">The bytes are truncated or not a valid v1 pack.</exception>
    internal static PackArchive Open(ReadOnlyMemory<byte> pack, string displayPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        return Create(pack, null, null, pack.Length, displayPath);
    }

    /// <summary>
    /// Looks up an entry by its complete canonical path.
    /// </summary>
    /// <param name="path">The canonical path, without a leading slash.</param>
    /// <param name="entry">The matching entry, when found.</param>
    /// <returns><see langword="true"/> when the path exists in this pack.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ObjectDisposedException">The archive is disposed.</exception>
    internal bool TryGetEntry(string path, [NotNullWhen(true)] out PackEntry? entry) {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfDisposed();
        return _entriesByPath.TryGetValue(path, out entry);
    }

    /// <summary>
    /// Reads, verifies, and decompresses one payload.
    /// </summary>
    /// <param name="entry">An entry returned by this archive.</param>
    /// <returns>A fresh array holding the decompressed payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is null.</exception>
    /// <exception cref="ArgumentException">The entry belongs to another archive.</exception>
    /// <exception cref="ObjectDisposedException">The archive is disposed.</exception>
    /// <exception cref="AssetLoadException">
    /// The stored payload hash does not match, or the payload cannot be decompressed to exactly its
    /// declared size.
    /// </exception>
    internal byte[] ReadPayload(PackEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);

        byte[] stored;
        lock (_ioLock) {
            ThrowIfDisposed();
            stored = CopyStoredBytes(entry);
        }

        ulong actualHash = PackCompression.ComputePayloadHash(stored);
        if (actualHash != entry.StoredHash) {
            throw PackFailure.PayloadHashMismatch(
                _displayPath,
                entry.Path,
                entry.StoredHash,
                actualHash);
        }

        if (!entry.IsCompressed) {
            return stored;
        }

        try {
            return PackCompression.DecompressExactly(stored, checked((int)entry.UncompressedSize));
        } catch (Exception exception) when (exception is InvalidDataException or IOException
            or NotSupportedException) {
            throw PackFailure.PayloadDecodeFailed(
                _displayPath,
                entry.Path,
                exception.Message,
                exception);
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        lock (_ioLock) {
            if (_disposed) {
                return;
            }

            _disposed = true;

            unsafe {
                if (_pointerAcquired) {
                    _view!.SafeMemoryMappedViewHandle.ReleasePointer();
                    _pointerAcquired = false;
                    _pointer = null;
                }
            }

            _view?.Dispose();
            _mappedFile?.Dispose();
        }
    }

    private static PackArchive Create(
        ReadOnlyMemory<byte> memory,
        MemoryMappedFile? mappedFile,
        MemoryMappedViewAccessor? view,
        long length,
        string displayPath) {
        PackArchive? archive = null;
        try {
            archive = new PackArchive(memory, mappedFile, view, length, displayPath);
            archive.AcquirePointer();
            archive.LoadHeaderAndIndex();
            return archive;
        } catch {
            if (archive is null) {
                view?.Dispose();
                mappedFile?.Dispose();
            } else {
                archive.Dispose();
            }

            throw;
        }
    }

    private unsafe void AcquirePointer() {
        if (_view is null) {
            return;
        }

        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
        _pointer += _view.PointerOffset;
        _pointerAcquired = true;
    }

    private void LoadHeaderAndIndex() {
        if (_length < PackFormat.PREFIX_LENGTH) {
            throw PackFailure.ReadFailed(
                _displayPath,
                $"the pack is {_length} bytes, shorter than the {PackFormat.PREFIX_LENGTH}-byte "
                    + "header");
        }

        ReadOnlySpan<byte> header = GetOpenSpan(0, PackFormat.PREFIX_LENGTH);
        if (!header[..PackFormat.MAGIC_LENGTH].SequenceEqual(PackFormat.MagicBytes)) {
            throw PackFailure.UnsupportedFormat(
                _displayPath,
                $"the magic is not '{PackFormat.MAGIC_TEXT}'");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[PackFormat.VERSION_OFFSET..]);
        if (version != PackFormat.CURRENT_VERSION) {
            throw PackFailure.UnsupportedFormat(
                _displayPath,
                $"the version is {version}; only version {PackFormat.CURRENT_VERSION} is supported");
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(header[PackFormat.FLAGS_OFFSET..]);
        if (flags != 0) {
            throw PackFailure.UnsupportedFormat(
                _displayPath,
                $"the header flags are 0x{flags:x8}; v1 defines no flags");
        }

        uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(
            header[PackFormat.ENTRY_COUNT_OFFSET..]);
        if (entryCount > PackFormat.MAX_ENTRY_COUNT) {
            throw PackFailure.InvalidIndex(
                _displayPath,
                $"the header declares {entryCount} entries; the v1 limit is "
                    + $"{PackFormat.MAX_ENTRY_COUNT}");
        }

        ulong indexOffset = BinaryPrimitives.ReadUInt64LittleEndian(
            header[PackFormat.INDEX_OFFSET_OFFSET..]);
        if (indexOffset != PackFormat.PREFIX_LENGTH) {
            throw PackFailure.InvalidIndex(
                _displayPath,
                $"the index offset is {indexOffset}; v1 requires {PackFormat.PREFIX_LENGTH}");
        }

        uint indexLength = BinaryPrimitives.ReadUInt32LittleEndian(
            header[PackFormat.INDEX_LENGTH_OFFSET..]);
        if (indexLength > PackFormat.MAX_INDEX_LENGTH) {
            throw PackFailure.InvalidIndex(
                _displayPath,
                $"the index is {indexLength} bytes; the v1 limit is "
                    + $"{PackFormat.MAX_INDEX_LENGTH} bytes");
        }

        uint storedIndexHash = BinaryPrimitives.ReadUInt32LittleEndian(
            header[PackFormat.INDEX_HASH_OFFSET..]);
        uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(
            header[PackFormat.RESERVED_OFFSET..]);
        if (reserved != 0) {
            throw PackFailure.UnsupportedFormat(
                _displayPath,
                $"the reserved header field is 0x{reserved:x8}; v1 requires zero");
        }

        if ((ulong)indexLength > (ulong)_length - PackFormat.PREFIX_LENGTH) {
            throw PackFailure.ReadFailed(
                _displayPath,
                $"the {indexLength}-byte index extends past the {_length}-byte pack");
        }

        ReadOnlySpan<byte> index = GetOpenSpan(PackFormat.PREFIX_LENGTH, (int)indexLength);
        uint actualIndexHash = PackCompression.ComputeIndexHash(index);
        if (actualIndexHash != storedIndexHash) {
            throw PackFailure.IndexHashMismatch(_displayPath, storedIndexHash, actualIndexHash);
        }

        long payloadStart = PackFormat.PREFIX_LENGTH + (long)indexLength;
        _entries = PackIndex.Parse(index, _displayPath, entryCount, _length, payloadStart);
        _entriesByPath = new Dictionary<string, PackEntry>(_entries.Length, StringComparer.Ordinal);
        foreach (PackEntry entry in _entries) {
            _entriesByPath.Add(entry.Path, entry);
        }
    }

    private unsafe ReadOnlySpan<byte> GetOpenSpan(long offset, int length) {
        if (_view is not null) {
            return new ReadOnlySpan<byte>(_pointer + offset, length);
        }

        return _memory.Span.Slice(checked((int)offset), length);
    }

    private unsafe byte[] CopyStoredBytes(PackEntry entry) {
        int length = checked((int)entry.StoredLength);
        byte[] stored = new byte[length];
        if (length == 0) {
            return stored;
        }

        if (_view is not null) {
            new ReadOnlySpan<byte>(_pointer + (long)entry.Offset, length).CopyTo(stored);
        } else {
            _memory.Span.Slice(checked((int)entry.Offset), length).CopyTo(stored);
        }

        return stored;
    }

    private void ValidateEntry(PackEntry entry) {
        ThrowIfDisposed();
        if (!_entriesByPath.TryGetValue(entry.Path, out PackEntry? known)
            || !ReferenceEquals(entry, known)) {
            throw new ArgumentException(
                "The entry does not belong to this pack archive.",
                nameof(entry));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
