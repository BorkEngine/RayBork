namespace RayBork.Internal;

/// <summary>
/// The inputs for one engine asset decode: the canonical path, the descriptor bytes, and the visible
/// pack stack used to resolve sibling payload entries.
/// </summary>
/// <remarks>
/// The context is immutable and only valid for the duration of one decode on the catalog loader
/// thread. The visible stack is the catalog's mounted snapshot; it is published atomically on start
/// and never mutated by a loader, so a decode may read it without locking.
/// </remarks>
internal sealed class AssetFormatContext {
    private readonly Dictionary<string, VisibleAsset> _visibleStack;

    /// <summary>
    /// Initializes a decode context.
    /// </summary>
    /// <param name="canonicalPath">The canonical descriptor path, without a leading slash.</param>
    /// <param name="descriptorPayload">The complete descriptor payload bytes.</param>
    /// <param name="visibleStack">The visible pack stack that resolves sibling payload entries.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal AssetFormatContext(
        string canonicalPath,
        byte[] descriptorPayload,
        Dictionary<string, VisibleAsset> visibleStack) {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(descriptorPayload);
        ArgumentNullException.ThrowIfNull(visibleStack);

        CanonicalPath = canonicalPath;
        DescriptorPayload = descriptorPayload;
        _visibleStack = visibleStack;
    }

    /// <summary>
    /// Gets the canonical descriptor path, without a leading slash.
    /// </summary>
    internal string CanonicalPath { get; }

    /// <summary>
    /// Gets the complete descriptor payload bytes. A format must treat the array as read-only.
    /// </summary>
    internal byte[] DescriptorPayload { get; }

    /// <summary>
    /// Reads one sibling payload entry from the visible pack stack.
    /// </summary>
    /// <remarks>
    /// The path must be canonical, resolve in the visible stack, and name a raw entry. Every rejection
    /// is an <see cref="AssetLoadException"/> naming the descriptor path and the payload kind.
    /// </remarks>
    /// <param name="payloadPath">The payload path named by the descriptor.</param>
    /// <param name="payloadKind">The payload kind used in diagnostics, such as <c>mesh</c>.</param>
    /// <returns>The decompressed payload bytes.</returns>
    internal byte[] ReadRawPayload(string payloadPath, string payloadKind) {
        if (!AssetPath.TryValidateCanonical(payloadPath, out string? reason)) {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload path '{payloadPath}' is not canonical: {reason}");
        }

        if (!_visibleStack.TryGetValue(payloadPath, out VisibleAsset payload)) {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload '{payloadPath}' is not in the visible pack stack");
        }

        if (payload.Entry.Type != AssetEntryType.Raw) {
            throw AssetLoadErrors.CreateLoadFailure(
                CanonicalPath,
                $"the {payloadKind} payload '{payloadPath}' is a {payload.Entry.Type} entry, not raw");
        }

        return payload.Archive.ReadPayload(payload.Entry);
    }
}
