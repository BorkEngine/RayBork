namespace RayBork.Internal;

/// <summary>
/// The generated built-in pack bytes compiled into the engine assembly.
/// </summary>
/// <remarks>
/// <para>
/// This is the complete RAYBORKP v1 pack that <see cref="AssetCatalog"/> mounts below every user pack.
/// The bytes are generated at engine build time by the JIT-only built-in pack generator from
/// <c>src/RayBork/RayBork.Engine/BuiltInAssets/Assets</c>, compiled into <see cref="BuiltInPack"/> by
/// the engine's build target, and never read from a loose file at run time.
/// </para>
/// <para>
/// An empty built-in pack means "the built-in pack contributes no entries", not "skip mounting": the
/// catalog always consults this member first and mounts it before any user pack when it has content.
/// Empty bytes are the documented input of a build with the generator disabled, while a non-empty
/// built-in pack is validated exactly like a user pack.
/// </para>
/// </remarks>
internal static class BuiltInAssetPack {
    /// <summary>Gets the generated built-in RAYBORKP v1 pack bytes.</summary>
    internal static ReadOnlyMemory<byte> Bytes => BuiltInPack.Bytes;
}
