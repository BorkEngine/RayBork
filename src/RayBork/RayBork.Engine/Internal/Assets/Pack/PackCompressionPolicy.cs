namespace RayBork.Internal;

/// <summary>
/// Selects how a pack writer stores payloads.
/// </summary>
internal enum PackCompressionPolicy {
    /// <summary>
    /// Store every payload uncompressed. Debug packs trade size for fast iteration.
    /// </summary>
    Debug = 0,

    /// <summary>
    /// Compress a payload with Zstandard only when the compressed bytes are strictly smaller than
    /// the original; otherwise store it uncompressed.
    /// </summary>
    Release = 1,
}
