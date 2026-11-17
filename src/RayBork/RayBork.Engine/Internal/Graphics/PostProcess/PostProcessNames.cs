namespace RayBork.Internal;

/// <summary>
/// The reserved texture-binding names of the custom post-process shader contract.
/// </summary>
/// <remarks>
/// The engine binds <see cref="SOURCE_COLOR"/> to the camera target's current resolved color (or to the
/// previous post-process pass' output) and <see cref="SOURCE_DEPTH"/> to the target's resolved depth.
/// Both names are reserved: a custom effect must declare <see cref="SOURCE_COLOR"/> and may declare
/// <see cref="SOURCE_DEPTH"/>, and the values the material cursor holds for them are always overridden.
/// </remarks>
internal static class PostProcessNames {
    /// <summary>The required texture binding that receives the current source color.</summary>
    internal const string SOURCE_COLOR = "sourceColor";

    /// <summary>
    /// The optional texture binding that receives the target's resolved depth. It is illegal on a
    /// depth-less render target.
    /// </summary>
    internal const string SOURCE_DEPTH = "sourceDepth";
}
