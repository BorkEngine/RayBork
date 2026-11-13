namespace RayBork.Internal;

/// <summary>
/// A built-in engine asset type registration backed by an <see cref="IEngineAssetFormat{T}"/>.
/// </summary>
/// <remarks>
/// The registration is immutable, and the format it holds is stateless and thread-safe, so the catalog
/// can decode every load of the type on its loader thread. The catalog rejects a second registration
/// for the same CLR type when the registry is built.
/// </remarks>
/// <typeparam name="T">The registered engine asset type.</typeparam>
internal sealed class EngineAssetTypeRegistration<T> : AssetTypeRegistration {
    /// <summary>
    /// Initializes a registration from an engine asset format.
    /// </summary>
    /// <param name="format">The format that decodes the registered type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is <c>null</c>.</exception>
    internal EngineAssetTypeRegistration(IEngineAssetFormat<T> format)
        : base(typeof(T), GetEntryType(format)) {
        Format = format;
    }

    /// <summary>
    /// Gets the format that decodes the registered type.
    /// </summary>
    internal IEngineAssetFormat<T> Format { get; }

    private static AssetEntryType GetEntryType(IEngineAssetFormat<T> format) {
        ArgumentNullException.ThrowIfNull(format);
        return format.EntryType;
    }
}
