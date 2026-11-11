namespace RayBork.Bake.Baking;

/// <summary>Payload compression mode for one bake invocation.</summary>
internal enum BakeMode {
    /// <summary>Store payloads uncompressed.</summary>
    Debug,

    /// <summary>Compress a payload only when the compressed bytes are strictly smaller.</summary>
    Release,
}

/// <summary>Parsing and naming helpers for <see cref="BakeMode"/>.</summary>
internal static class BakeModeExtensions {
    /// <summary>Maps a mode to the pack writer's compression policy.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The matching pack policy.</returns>
    internal static RayBork.Internal.PackCompressionPolicy ToCompressionPolicy(this BakeMode mode)
        => mode == BakeMode.Release
            ? RayBork.Internal.PackCompressionPolicy.Release
            : RayBork.Internal.PackCompressionPolicy.Debug;

    /// <summary>Gets the lowercase CLI name of a mode.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns><c>debug</c> or <c>release</c>.</returns>
    internal static string ToName(this BakeMode mode) => mode == BakeMode.Release ? "release" : "debug";

    /// <summary>Parses a <c>--mode</c> value case-insensitively.</summary>
    /// <param name="value">The value.</param>
    /// <param name="mode">The parsed mode.</param>
    /// <returns><see langword="true"/> for <c>debug</c> or <c>release</c>.</returns>
    internal static bool TryParse(string? value, out BakeMode mode) {
        if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase)) {
            mode = BakeMode.Debug;
            return true;
        }

        if (string.Equals(value, "release", StringComparison.OrdinalIgnoreCase)) {
            mode = BakeMode.Release;
            return true;
        }

        mode = BakeMode.Debug;
        return false;
    }
}
