namespace RayBork.Internal;

/// <summary>The generated RAYBORKP v1 built-in pack embedded in the engine.</summary>
/// <remarks>
/// The pack is generated at build time and mounted before every user pack. Empty bytes mean it
/// contributes no entries, not that it is skipped.
/// </remarks>
internal static class BuiltInAssetPack
{
    /// <summary>Gets the generated built-in RAYBORKP v1 pack bytes.</summary>
    internal static ReadOnlyMemory<byte> Bytes => BuiltInPack.Bytes;
}
