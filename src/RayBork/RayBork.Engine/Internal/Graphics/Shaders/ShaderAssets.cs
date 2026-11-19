namespace RayBork.Internal;

/// <summary>Provides the committed SPIR-V modules for the built-in present and post-store passes.</summary>
/// <remarks>The modules are loaded once from embedded resources and never compiled at runtime. The
/// returned spans reference immutable arrays and stay valid for the process lifetime.</remarks>
internal static class ShaderAssets
{
    /// <summary>The vertex entry point name compiled into <c>Present.vert.spv</c>.</summary>
    internal const string PRESENT_VERTEX_ENTRY_POINT = "main";

    /// <summary>The fragment entry point name compiled into <c>Present.frag.spv</c>.</summary>
    internal const string PRESENT_FRAGMENT_ENTRY_POINT = "main";

    /// <summary>The vertex entry point name compiled into <c>PostStore.vert.spv</c>.</summary>
    internal const string POST_STORE_VERTEX_ENTRY_POINT = "main";

    /// <summary>The fragment entry point name compiled into <c>PostStore.frag.spv</c>.</summary>
    internal const string POST_STORE_FRAGMENT_ENTRY_POINT = "main";

    /// <summary>The embedded resource name of <c>Shaders/Present.vert.spv</c>.</summary>
    private const string PRESENT_VERTEX_RESOURCE_NAME = "RayBork.Shaders.Present.vert.spv";

    /// <summary>The embedded resource name of <c>Shaders/Present.frag.spv</c>.</summary>
    private const string PRESENT_FRAGMENT_RESOURCE_NAME = "RayBork.Shaders.Present.frag.spv";

    /// <summary>The embedded resource name of <c>Shaders/PostStore.vert.spv</c>.</summary>
    private const string POST_STORE_VERTEX_RESOURCE_NAME = "RayBork.Shaders.PostStore.vert.spv";

    /// <summary>The embedded resource name of <c>Shaders/PostStore.frag.spv</c>.</summary>
    private const string POST_STORE_FRAGMENT_RESOURCE_NAME = "RayBork.Shaders.PostStore.frag.spv";

    private static readonly byte[] _presentVertex = Load(PRESENT_VERTEX_RESOURCE_NAME);
    private static readonly byte[] _presentFragment = Load(PRESENT_FRAGMENT_RESOURCE_NAME);
    private static readonly byte[] _postStoreVertex = Load(POST_STORE_VERTEX_RESOURCE_NAME);
    private static readonly byte[] _postStoreFragment = Load(POST_STORE_FRAGMENT_RESOURCE_NAME);

    /// <summary>Gets the SPIR-V module for the fullscreen present vertex shader.</summary>
    internal static ReadOnlySpan<byte> PresentVertex => _presentVertex;

    /// <summary>Gets the SPIR-V module for the present fragment shader.</summary>
    internal static ReadOnlySpan<byte> PresentFragment => _presentFragment;

    /// <summary>Gets the SPIR-V module for the post-store vertex shader.</summary>
    internal static ReadOnlySpan<byte> PostStoreVertex => _postStoreVertex;

    /// <summary>Gets the SPIR-V module for the post-store fragment shader.</summary>
    internal static ReadOnlySpan<byte> PostStoreFragment => _postStoreFragment;

    /// <summary>Loads one embedded SPIR-V module by its fixed resource name.</summary>
    /// <param name="resourceName">The manifest resource name.</param>
    /// <exception cref="InvalidOperationException">The resource is missing; the type initializer wraps
    /// the failure in a <see cref="TypeInitializationException"/>.</exception>
    private static byte[] Load(string resourceName)
    {
        using Stream? resource = typeof(ShaderAssets).Assembly.GetManifestResourceStream(
            resourceName
        );
        if (resource is null)
        {
            throw new InvalidOperationException(
                $"Embedded shader resource '{resourceName}' was not found in assembly "
                    + $"'{typeof(ShaderAssets).Assembly.GetName().Name}'. The committed SPIR-V files are the "
                    + "build inputs; run 'scripts/compile-shaders.ps1' to regenerate them and rebuild."
            );
        }

        using MemoryStream buffer = new();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
