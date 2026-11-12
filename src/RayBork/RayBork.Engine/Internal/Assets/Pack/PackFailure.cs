namespace RayBork.Internal;

/// <summary>
/// Creates the coded exceptions the pack reader reports, with messages that always name the pack
/// display path and, where available, the offending entry path.
/// </summary>
internal static class PackFailure {
    /// <summary>
    /// A pack is missing, unreadable, or truncated before its header or index is complete.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="reason">The concrete failure.</param>
    /// <param name="innerException">The underlying I/O failure, when there is one.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetPackException ReadFailed(
        string displayPath,
        string reason,
        Exception? innerException = null)
        => new(
            $"Asset pack '{displayPath}' could not be read: {reason}.",
            DiagnosticCodes.ASSET_PACK_READ_FAILED,
            innerException);

    /// <summary>
    /// A pack has an unsupported magic, version, or header flag.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="reason">The concrete failure.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetPackException UnsupportedFormat(string displayPath, string reason)
        => new(
            $"Asset pack '{displayPath}' uses an unsupported format: {reason}.",
            DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION,
            null);

    /// <summary>
    /// A pack index is structurally invalid.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="reason">The concrete failure.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetPackException InvalidIndex(
        string displayPath,
        string reason,
        Exception? innerException = null)
        => new(
            $"Asset pack '{displayPath}' has an invalid index: {reason}.",
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX,
            innerException);

    /// <summary>
    /// The index hash does not match the index bytes.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="storedHash">The hash stored in the header.</param>
    /// <param name="actualHash">The hash computed over the index bytes.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetPackException IndexHashMismatch(
        string displayPath,
        uint storedHash,
        uint actualHash)
        => new(
            $"Asset pack '{displayPath}' has an index hash mismatch: "
                + $"the header stores 0x{storedHash:x8} but the index hashes to 0x{actualHash:x8}.",
            DiagnosticCodes.ASSET_PACK_INDEX_HASH_MISMATCH,
            null);

    /// <summary>
    /// A stored payload hash does not match the index.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="entryPath">The canonical entry path.</param>
    /// <param name="storedHash">The hash stored in the index.</param>
    /// <param name="actualHash">The hash computed over the stored bytes.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetLoadException PayloadHashMismatch(
        string displayPath,
        string entryPath,
        ulong storedHash,
        ulong actualHash)
        => new(
            $"Asset pack '{displayPath}' entry '{entryPath}' has a payload hash mismatch: "
                + $"the index stores 0x{storedHash:x16} but the payload hashes to 0x{actualHash:x16}.",
            DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH,
            null);

    /// <summary>
    /// A stored payload could not be decompressed to its declared size.
    /// </summary>
    /// <param name="displayPath">The pack identifier used in diagnostics.</param>
    /// <param name="entryPath">The canonical entry path.</param>
    /// <param name="reason">The concrete failure.</param>
    /// <param name="innerException">The decoder failure, when there is one.</param>
    /// <returns>The exception to throw.</returns>
    internal static AssetLoadException PayloadDecodeFailed(
        string displayPath,
        string entryPath,
        string reason,
        Exception? innerException = null)
        => new(
            $"Asset pack '{displayPath}' entry '{entryPath}' could not be decoded: {reason}.",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            innerException);
}
