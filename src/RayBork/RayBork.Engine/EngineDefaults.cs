using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The engine-owned default assets: the built-in mesh shader program and the white and error textures.
/// </summary>
/// <remarks>
/// <para>
/// The app preloads these assets after the asset catalog starts and before <see cref="IGame.Setup"/>,
/// and releases the engine's owner references during shutdown. <see cref="EngineContext.Defaults"/> is
/// therefore valid for the whole game lifetime; a game reads it instead of loading the same paths
/// itself.
/// </para>
/// <para>
/// <see cref="White"/> and <see cref="Error"/> are engine-owned textures. Do not dispose them: the
/// engine's reference keeps them alive until shutdown. <see cref="CreateMeshShader"/> returns a new
/// caller-owned material for each call; dispose every material it returns exactly once.
/// </para>
/// <para>
/// Accessing any member after the app released the defaults throws
/// <see cref="ObjectDisposedException"/>; <see cref="IsDisposed"/> stays readable.
/// </para>
/// </remarks>
public sealed class EngineDefaults {
    /// <summary>The canonical path of the built-in mesh shader program.</summary>
    internal const string MESH_SHADER_PATH = "/shaders/mesh";

    /// <summary>The canonical path of the built-in white texture.</summary>
    internal const string WHITE_TEXTURE_PATH = "/textures/white";

    /// <summary>The canonical path of the built-in error texture.</summary>
    internal const string ERROR_TEXTURE_PATH = "/textures/error";

    private readonly ShaderProgram _meshShader;
    private readonly Texture _white;
    private readonly Texture _error;
    private bool _disposed;

    /// <summary>
    /// Initializes the defaults from the already loaded built-in assets. The defaults own the owner
    /// references the loader acquired for them.
    /// </summary>
    /// <param name="meshShader">The built-in mesh shader program.</param>
    /// <param name="white">The built-in white texture.</param>
    /// <param name="error">The built-in error texture.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal EngineDefaults(ShaderProgram meshShader, Texture white, Texture error) {
        ArgumentNullException.ThrowIfNull(meshShader);
        ArgumentNullException.ThrowIfNull(white);
        ArgumentNullException.ThrowIfNull(error);

        _meshShader = meshShader;
        _white = white;
        _error = error;
    }

    /// <summary>
    /// Gets the built-in white texture: an opaque 1x1 white sRGB image. The engine owns it; do not
    /// dispose it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The engine released the defaults.</exception>
    public Texture White {
        get {
            ThrowIfDisposed();
            return _white;
        }
    }

    /// <summary>
    /// Gets the built-in error texture: a 4x4 magenta/black checkerboard with nearest filtering. The
    /// engine owns it; do not dispose it.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The engine released the defaults.</exception>
    public Texture Error {
        get {
            ThrowIfDisposed();
            return _error;
        }
    }

    /// <summary>
    /// Gets whether the engine released the defaults. It stays readable after disposal.
    /// </summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Creates a new material over the preloaded <c>/shaders/mesh</c> program.
    /// </summary>
    /// <returns>
    /// A new caller-owned material with its own uniform cursor and texture bindings. Dispose it once.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The engine released the defaults.</exception>
    public Shader CreateMeshShader() {
        ThrowIfDisposed();
        return _meshShader.CreateMaterial();
    }

    /// <summary>
    /// Preloads the built-in defaults through the catalog.
    /// </summary>
    /// <param name="assets">The started asset catalog.</param>
    /// <returns>The preloaded defaults, which own one owner reference per asset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// A built-in default is missing, was baked as the wrong type, or could not be uploaded.
    /// </exception>
    internal static async Task<EngineDefaults> LoadAsync(IAssetCatalog assets) {
        ArgumentNullException.ThrowIfNull(assets);

        ShaderProgram? meshShader = null;
        Texture? white = null;
        Texture? error = null;
        try {
            meshShader = await assets.Load<ShaderProgram>(MESH_SHADER_PATH).ConfigureAwait(false);
            white = await assets.Load<Texture>(WHITE_TEXTURE_PATH).ConfigureAwait(false);
            error = await assets.Load<Texture>(ERROR_TEXTURE_PATH).ConfigureAwait(false);
            return new EngineDefaults(meshShader, white, error);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            ReleaseQuietly(meshShader);
            ReleaseQuietly(white);
            ReleaseQuietly(error);
            throw new AssetLoadException(
                $"The engine's built-in default assets are missing or corrupt: the mesh shader "
                    + $"program '{MESH_SHADER_PATH}', the white texture '{WHITE_TEXTURE_PATH}', and the "
                    + $"error texture '{ERROR_TEXTURE_PATH}' must all load from the built-in pack.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception);
        }
    }

    /// <summary>
    /// Releases the owner reference for each default asset. Idempotent; called by the app during
    /// shutdown after the game callbacks returned.
    /// </summary>
    internal void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        ReleaseQuietly(_meshShader);
        ReleaseQuietly(_white);
        ReleaseQuietly(_error);
    }

    private static void ReleaseQuietly(ShaderProgram? program) {
        if (program is null) {
            return;
        }

        try {
            program.Lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // The catalog force-disposed the asset during shutdown; its reference is already gone.
        }
    }

    private static void ReleaseQuietly(Texture? texture) {
        if (texture is null) {
            return;
        }

        try {
            texture.Lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // The catalog force-disposed the asset during shutdown; its reference is already gone.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
