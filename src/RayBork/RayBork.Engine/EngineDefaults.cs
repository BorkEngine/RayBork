using RayBork.Internal;

namespace RayBork;

/// <summary>The engine-owned default assets: mesh shader, textures, and fonts.</summary>
/// <remarks>
/// The app preloads these assets before <see cref="IGame.Setup"/> and releases them at shutdown;
/// they are valid for the whole game lifetime. Accessing a released member throws
/// <see cref="ObjectDisposedException"/>; never dispose an engine-owned asset.
/// </remarks>
public sealed class EngineDefaults
{
    /// <summary>The canonical path of the built-in mesh shader program.</summary>
    internal const string MESH_SHADER_PATH = "/shaders/mesh";

    /// <summary>The canonical path of the built-in white texture.</summary>
    internal const string WHITE_TEXTURE_PATH = "/textures/white";

    /// <summary>The canonical path of the built-in error texture.</summary>
    internal const string ERROR_TEXTURE_PATH = "/textures/error";

    /// <summary>The canonical path of the built-in default font.</summary>
    internal const string DEFAULT_FONT_PATH = "/fonts/fira-sans";

    /// <summary>The canonical path of the built-in monospaced font.</summary>
    internal const string DEFAULT_MONO_FONT_PATH = "/fonts/jetbrains-mono";

    private readonly ShaderProgram _meshShader;
    private readonly Texture _white;
    private readonly Texture _error;
    private readonly Font _defaultFont;
    private readonly Font _defaultMonoFont;
    private bool _disposed;

    /// <summary>Initializes the defaults and owns the loader's references to them.</summary>
    internal EngineDefaults(
        ShaderProgram meshShader,
        Texture white,
        Texture error,
        Font defaultFont,
        Font defaultMonoFont
    )
    {
        ArgumentNullException.ThrowIfNull(meshShader);
        ArgumentNullException.ThrowIfNull(white);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(defaultFont);
        ArgumentNullException.ThrowIfNull(defaultMonoFont);

        _meshShader = meshShader;
        _white = white;
        _error = error;
        _defaultFont = defaultFont;
        _defaultMonoFont = defaultMonoFont;
    }

    /// <summary>Gets the engine-owned opaque 1x1 white sRGB texture; do not dispose it.</summary>
    public Texture White
    {
        get
        {
            ThrowIfDisposed();
            return _white;
        }
    }

    /// <summary>Gets the engine-owned 4x4 magenta/black error texture; do not dispose it.</summary>
    public Texture Error
    {
        get
        {
            ThrowIfDisposed();
            return _error;
        }
    }

    /// <summary>
    /// Gets the engine-owned Fira Sans Regular font with an on-demand glyph cache; do not dispose it.
    /// </summary>
    public Font DefaultFont
    {
        get
        {
            ThrowIfDisposed();
            return _defaultFont;
        }
    }

    /// <summary>
    /// Gets the engine-owned JetBrains Mono font with an on-demand glyph cache; do not dispose it.
    /// </summary>
    public Font DefaultMonoFont
    {
        get
        {
            ThrowIfDisposed();
            return _defaultMonoFont;
        }
    }

    /// <summary>Gets whether the engine released the defaults; stays readable after disposal.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Creates a new caller-owned material over the preloaded mesh shader; dispose it exactly once.
    /// </summary>
    public Shader CreateMeshShader()
    {
        ThrowIfDisposed();
        return _meshShader.CreateMaterial();
    }

    /// <summary>Preloads the built-in defaults, owning one reference per asset.</summary>
    /// <exception cref="AssetLoadException">
    /// A built-in default is missing, corrupt, or has the wrong type.
    /// </exception>
    internal static async Task<EngineDefaults> LoadAsync(IAssetCatalog assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ShaderProgram? meshShader = null;
        Texture? white = null;
        Texture? error = null;
        Font? defaultFont = null;
        Font? defaultMonoFont = null;
        try
        {
            meshShader = await assets.Load<ShaderProgram>(MESH_SHADER_PATH).ConfigureAwait(false);
            white = await assets.Load<Texture>(WHITE_TEXTURE_PATH).ConfigureAwait(false);
            error = await assets.Load<Texture>(ERROR_TEXTURE_PATH).ConfigureAwait(false);
            defaultFont = await assets.Load<Font>(DEFAULT_FONT_PATH).ConfigureAwait(false);
            defaultMonoFont = await assets.Load<Font>(DEFAULT_MONO_FONT_PATH).ConfigureAwait(false);
            return new EngineDefaults(meshShader, white, error, defaultFont, defaultMonoFont);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReleaseQuietly(meshShader);
            ReleaseQuietly(white);
            ReleaseQuietly(error);
            ReleaseQuietly(defaultFont);
            ReleaseQuietly(defaultMonoFont);
            throw new AssetLoadException(
                $"The engine's built-in default assets are missing or corrupt: the mesh shader "
                    + $"program '{MESH_SHADER_PATH}', the white texture '{WHITE_TEXTURE_PATH}', the "
                    + $"error texture '{ERROR_TEXTURE_PATH}', the default font '{DEFAULT_FONT_PATH}', "
                    + $"and the monospaced font '{DEFAULT_MONO_FONT_PATH}' must all load from the "
                    + "built-in pack.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception
            );
        }
    }

    /// <summary>Releases the owner reference for each default asset; idempotent.</summary>
    internal void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseQuietly(_meshShader);
        ReleaseQuietly(_white);
        ReleaseQuietly(_error);
        ReleaseQuietly(_defaultFont);
        ReleaseQuietly(_defaultMonoFont);
    }

    private static void ReleaseQuietly(ShaderProgram? program)
    {
        if (program is null)
        {
            return;
        }

        try
        {
            program.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the asset during shutdown; its reference is already gone.
        }
    }

    private static void ReleaseQuietly(Texture? texture)
    {
        if (texture is null)
        {
            return;
        }

        try
        {
            texture.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the asset during shutdown; its reference is already gone.
        }
    }

    private static void ReleaseQuietly(Font? font)
    {
        if (font is null)
        {
            return;
        }

        try
        {
            font.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the asset during shutdown; its reference is already gone.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
