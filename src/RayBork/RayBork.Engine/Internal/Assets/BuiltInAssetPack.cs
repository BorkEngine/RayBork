namespace RayBork.Internal;

/// <summary>
/// The built-in pack bytes compiled into the engine assembly.
/// </summary>
/// <remarks>
/// <para>
/// This is the complete RAYBORKP v1 pack that <see cref="AssetCatalog"/> mounts below every user pack.
/// The bytes are produced at engine build time by the JIT-only built-in pack generator and compiled
/// into this assembly; they are never read from a loose file at run time. When the built-in pack
/// lands in a later task this member returns its bytes.
/// </para>
/// <para>
/// The M3 placeholder is empty. An empty built-in pack means "the built-in pack contributes no
/// entries", not "skip mounting": the catalog always consults this member first and mounts it before
/// any user pack when it has content. Empty bytes are therefore a valid built-in input, while a
/// non-empty built-in pack is validated exactly like a user pack.
/// </para>
/// </remarks>
internal static class BuiltInAssetPack {
    /// <summary>
    /// Gets the built-in RAYBORKP v1 pack bytes, or an empty memory when the generated pack is not
    /// compiled in yet.
    /// </summary>
    internal static ReadOnlyMemory<byte> Bytes => ReadOnlyMemory<byte>.Empty;
}
