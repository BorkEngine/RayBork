namespace RayBork.Internal;

/// <summary>
/// The exception factories shared by the asset catalog and its loaders.
/// </summary>
/// <remarks>
/// The message text and diagnostic codes are part of the documented asset contract, so the catalog
/// and the loader strategies build every failure through these factories instead of duplicating them.
/// </remarks>
internal static class AssetLoadErrors {
    internal static AssetNotFoundException CreateNotFound(string canonical) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' is not in any mounted pack.",
        DiagnosticCodes.ASSET_NOT_FOUND,
        null);

    internal static AssetTypeMismatchException CreateTypeMismatch(
        string canonical,
        AssetEntryType expected,
        AssetEntryType actual) => new(
            $"The asset '{AssetPath.ToPublic(canonical)}' was baked as {actual}, but it was requested as "
                + $"{expected}.",
            DiagnosticCodes.ASSET_TYPE_MISMATCH,
            null);

    internal static AssetLoadException CreateLoadFailure(string canonical, string reason) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' could not be loaded: {reason}.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    internal static AssetLoadException CreateLoaderQueueFull(string canonical) => new(
        $"The asset '{AssetPath.ToPublic(canonical)}' could not be queued: the asset loader has "
            + $"{AssetCatalog.MAX_PENDING_LOADS} pending requests.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    internal static Exception WrapLoadFailure(string canonical, string what, Exception exception) {
        if (exception is AssetPackException
            or AssetTypeMismatchException
            or AssetNotFoundException) {
            return exception;
        }

        if (exception is AssetLoadException loadFailure
            && (string.Equals(
                    loadFailure.DiagnosticCode,
                    DiagnosticCodes.ASSET_LOAD_FAILED,
                    StringComparison.Ordinal)
                || string.Equals(
                    loadFailure.DiagnosticCode,
                    DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
                    StringComparison.Ordinal))) {
            return loadFailure;
        }

        return new AssetLoadException(
            $"The asset '{AssetPath.ToPublic(canonical)}' {what}: {exception.Message}",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            exception);
    }

    internal static ObjectDisposedException CreateStoppedException() => new(
        nameof(AssetCatalog),
        "The asset catalog is stopped.");
}
