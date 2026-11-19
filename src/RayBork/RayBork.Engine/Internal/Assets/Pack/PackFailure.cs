namespace RayBork.Internal;

/// <summary>Creates the coded exceptions reported by the pack reader.</summary>
internal static class PackFailure
{
    /// <summary>A pack is missing, unreadable, or truncated before its header or index.</summary>
    internal static AssetPackException ReadFailed(
        string displayPath,
        string reason,
        Exception? innerException = null
    ) =>
        new(
            $"Asset pack '{displayPath}' could not be read: {reason}.",
            DiagnosticCodes.ASSET_PACK_READ_FAILED,
            innerException
        );

    /// <summary>A pack has an unsupported magic, version, or header flag.</summary>
    internal static AssetPackException UnsupportedFormat(string displayPath, string reason) =>
        new(
            $"Asset pack '{displayPath}' uses an unsupported format: {reason}.",
            DiagnosticCodes.ASSET_PACK_UNSUPPORTED_VERSION,
            null
        );

    /// <summary>A pack index is structurally invalid.</summary>
    internal static AssetPackException InvalidIndex(
        string displayPath,
        string reason,
        Exception? innerException = null
    ) =>
        new(
            $"Asset pack '{displayPath}' has an invalid index: {reason}.",
            DiagnosticCodes.ASSET_PACK_INVALID_INDEX,
            innerException
        );

    /// <summary>The index hash does not match the index bytes.</summary>
    internal static AssetPackException IndexHashMismatch(
        string displayPath,
        uint storedHash,
        uint actualHash
    ) =>
        new(
            $"Asset pack '{displayPath}' has an index hash mismatch: "
                + $"the header stores 0x{storedHash:x8} but the index hashes to 0x{actualHash:x8}.",
            DiagnosticCodes.ASSET_PACK_INDEX_HASH_MISMATCH,
            null
        );

    /// <summary>A stored payload hash does not match the index.</summary>
    internal static AssetLoadException PayloadHashMismatch(
        string displayPath,
        string entryPath,
        ulong storedHash,
        ulong actualHash
    ) =>
        new(
            $"Asset pack '{displayPath}' entry '{entryPath}' has a payload hash mismatch: "
                + $"the index stores 0x{storedHash:x16} but the payload hashes to 0x{actualHash:x16}.",
            DiagnosticCodes.ASSET_PACK_PAYLOAD_HASH_MISMATCH,
            null
        );

    /// <summary>A stored payload could not be decompressed to its declared size.</summary>
    internal static AssetLoadException PayloadDecodeFailed(
        string displayPath,
        string entryPath,
        string reason,
        Exception? innerException = null
    ) =>
        new(
            $"Asset pack '{displayPath}' entry '{entryPath}' could not be decoded: {reason}.",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            innerException
        );
}
