namespace RayBork.Bake.Shaders;

/// <summary>The fixed M3 shader entry points.</summary>
/// <remarks>The vertex and fragment ABI names are fixed; a sidecar cannot rename them.</remarks>
internal static class ShaderEntryPoints
{
    /// <summary>The fixed vertex-stage entry point.</summary>
    internal const string VERTEX = "main_vertex";

    /// <summary>The fixed fragment-stage entry point.</summary>
    internal const string FRAGMENT = "main_fragment";
}
