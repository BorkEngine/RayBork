namespace RayBork.Internal;

/// <summary>
/// Decodes one built-in engine asset format from its packed descriptor and sibling payload entries.
/// </summary>
/// <remarks>
/// Implementations are stateless and thread-safe: the catalog calls <see cref="Decode"/> on its loader
/// thread for every cache miss. A format validates the decoded payload and returns the CPU-side GPU
/// upload together with the factory that creates the public asset once the upload completes.
/// </remarks>
/// <typeparam name="T">The engine asset type the format produces.</typeparam>
internal interface IEngineAssetFormat<T> {
    /// <summary>
    /// Gets the pack entry type the format decodes.
    /// </summary>
    AssetEntryType EntryType { get; }

    /// <summary>
    /// Decodes one asset from the supplied context.
    /// </summary>
    /// <param name="context">The canonical path, descriptor bytes, and visible pack stack.</param>
    /// <returns>The GPU upload to run and the factory for the public asset over its lifetime.</returns>
    DecodedAsset<T> Decode(AssetFormatContext context);
}
