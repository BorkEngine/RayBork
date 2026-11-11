namespace RayBork.Internal;

/// <summary>
/// Provides the SPIR-V modules for the engine's built-in present pass.
/// </summary>
/// <remarks>
/// The modules are compiled ahead of time from the GLSL sources in <c>Shaders/</c> by
/// <c>scripts/compile-shaders.ps1</c> and committed next to them. The runtime never compiles
/// shaders; the committed bytes are the only input. Each module is read once from the assembly
/// manifest into an immutable array, and the spans returned by this type reference that array, so
/// they stay valid for the process lifetime and may be cached by the renderer.
/// </remarks>
internal static class ShaderAssets {
    /// <summary>The vertex entry point name compiled into <c>Present.vert.spv</c>.</summary>
    internal const string PRESENT_VERTEX_ENTRY_POINT = "main";

    /// <summary>The fragment entry point name compiled into <c>Present.frag.spv</c>.</summary>
    internal const string PRESENT_FRAGMENT_ENTRY_POINT = "main";

    /// <summary>
    /// The logical resource name of <c>Shaders/Present.vert.spv</c>. The name is fixed by the
    /// <c>EmbeddedResource</c> item in <c>RayBork.Engine.csproj</c>.
    /// </summary>
    private const string PRESENT_VERTEX_RESOURCE_NAME = "RayBork.Shaders.Present.vert.spv";

    /// <summary>
    /// The logical resource name of <c>Shaders/Present.frag.spv</c>. The name is fixed by the
    /// <c>EmbeddedResource</c> item in <c>RayBork.Engine.csproj</c>.
    /// </summary>
    private const string PRESENT_FRAGMENT_RESOURCE_NAME = "RayBork.Shaders.Present.frag.spv";

    private static readonly byte[] _presentVertex = Load(PRESENT_VERTEX_RESOURCE_NAME);
    private static readonly byte[] _presentFragment = Load(PRESENT_FRAGMENT_RESOURCE_NAME);

    /// <summary>
    /// Gets the SPIR-V module for the fullscreen present vertex shader. The returned span is backed
    /// by the loaded assembly resource and is valid for the process lifetime.
    /// </summary>
    internal static ReadOnlySpan<byte> PresentVertex => _presentVertex;

    /// <summary>
    /// Gets the SPIR-V module for the present fragment shader, which samples the resolved linear
    /// HDR scene color and writes the display-referred output. The returned span is backed by the
    /// loaded assembly resource and is valid for the process lifetime.
    /// </summary>
    internal static ReadOnlySpan<byte> PresentFragment => _presentFragment;

    /// <summary>
    /// Loads one embedded SPIR-V module by its fixed logical resource name.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing from the assembly. The message names the resource and the script
    /// that regenerates the committed blobs. The failure surfaces from the type initializer as a
    /// <see cref="TypeInitializationException"/> on the first access.
    /// </exception>
    private static byte[] Load(string resourceName) {
        using Stream? resource = typeof(ShaderAssets).Assembly.GetManifestResourceStream(resourceName);
        if (resource is null) {
            throw new InvalidOperationException(
                $"Embedded shader resource '{resourceName}' was not found in assembly " +
                $"'{typeof(ShaderAssets).Assembly.GetName().Name}'. The committed SPIR-V files are the " +
                "build inputs; run 'scripts/compile-shaders.ps1' to regenerate them and rebuild.");
        }

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
