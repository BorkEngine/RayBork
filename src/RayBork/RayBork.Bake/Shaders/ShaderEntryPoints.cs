namespace RayBork.Bake.Shaders;

/// <summary>
/// The fixed M3 shader entry points.
/// </summary>
/// <remarks>
/// The shader ABI contract fixes the vertex and fragment entry points. A sidecar may restate them for
/// clarity, but it cannot rename them in M3.
/// </remarks>
internal static class ShaderEntryPoints {
    /// <summary>The fixed vertex-stage entry point.</summary>
    internal const string VERTEX = "main_vertex";

    /// <summary>The fixed fragment-stage entry point.</summary>
    internal const string FRAGMENT = "main_fragment";
}
