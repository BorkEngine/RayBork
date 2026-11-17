namespace RayBork.Internal;

/// <summary>
/// The engine-owned SSAO shader programs and blue-noise texture loaded from the built-in pack before
/// <c>IGame.Setup</c>.
/// </summary>
/// <remarks>
/// <para>
/// The app owns one instance for its lifetime and attaches it to <see cref="CameraService"/> so a
/// camera can snapshot an <see cref="SsaoEffect"/> without loading an asset on the render thread. The
/// seven shader programs are the three AO variants, the three denoise variants, and the single
/// composite shader; the texture is the N8AO 2.1.0 128x128 RGBA8 blue-noise image.
/// </para>
/// <para>
/// <see cref="Dispose"/> releases the owner reference for every loaded asset. A snapshot that retained
/// a program or the texture keeps its own frame hold, so disposal after the tick thread stopped does not
/// invalidate a recorded frame.
/// </para>
/// </remarks>
internal sealed class SsaoAssets : IDisposable {
    private readonly ShaderProgram _ao8;
    private readonly ShaderProgram _ao16;
    private readonly ShaderProgram _ao64;
    private readonly ShaderProgram _denoise4;
    private readonly ShaderProgram _denoise8;
    private readonly ShaderProgram _denoise16;
    private readonly ShaderProgram _composite;
    private readonly Texture _blueNoise;
    private bool _disposed;

    private SsaoAssets(
        ShaderProgram ao8,
        ShaderProgram ao16,
        ShaderProgram ao64,
        ShaderProgram denoise4,
        ShaderProgram denoise8,
        ShaderProgram denoise16,
        ShaderProgram composite,
        Texture blueNoise) {
        _ao8 = ao8;
        _ao16 = ao16;
        _ao64 = ao64;
        _denoise4 = denoise4;
        _denoise8 = denoise8;
        _denoise16 = denoise16;
        _composite = composite;
        _blueNoise = blueNoise;
    }

    /// <summary>Gets whether the engine released the SSAO assets.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Loads and validates every SSAO built-in asset from the started catalog.
    /// </summary>
    /// <param name="assets">The started asset catalog.</param>
    /// <returns>The loaded assets, which own one owner reference per shader program and the texture.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <exception cref="AssetLoadException">
    /// A built-in SSAO asset is missing, has the wrong shader ABI contract, or cannot upload.
    /// </exception>
    internal static async Task<SsaoAssets> LoadAsync(IAssetCatalog assets) {
        ArgumentNullException.ThrowIfNull(assets);

        ShaderProgram? ao8 = null;
        ShaderProgram? ao16 = null;
        ShaderProgram? ao64 = null;
        ShaderProgram? denoise4 = null;
        ShaderProgram? denoise8 = null;
        ShaderProgram? denoise16 = null;
        ShaderProgram? composite = null;
        Texture? blueNoise = null;

        try {
            ao8 = await assets.Load<ShaderProgram>(SsaoShaderContract.AO_8_PATH).ConfigureAwait(false);
            ao16 = await assets.Load<ShaderProgram>(SsaoShaderContract.AO_16_PATH).ConfigureAwait(false);
            ao64 = await assets.Load<ShaderProgram>(SsaoShaderContract.AO_64_PATH).ConfigureAwait(false);
            denoise4 = await assets.Load<ShaderProgram>(SsaoShaderContract.DENOISE_4_PATH).ConfigureAwait(false);
            denoise8 = await assets.Load<ShaderProgram>(SsaoShaderContract.DENOISE_8_PATH).ConfigureAwait(false);
            denoise16 = await assets.Load<ShaderProgram>(SsaoShaderContract.DENOISE_16_PATH).ConfigureAwait(false);
            composite = await assets.Load<ShaderProgram>(SsaoShaderContract.COMPOSITE_PATH).ConfigureAwait(false);
            blueNoise = await assets.Load<Texture>(SsaoShaderContract.BLUE_NOISE_PATH).ConfigureAwait(false);

            ValidateProgram(ao8, SsaoShaderContract.AO_8_PATH, SsaoShaderContract.AO_UNIFORMS, SsaoShaderContract.AO_TEXTURES);
            ValidateProgram(ao16, SsaoShaderContract.AO_16_PATH, SsaoShaderContract.AO_UNIFORMS, SsaoShaderContract.AO_TEXTURES);
            ValidateProgram(ao64, SsaoShaderContract.AO_64_PATH, SsaoShaderContract.AO_UNIFORMS, SsaoShaderContract.AO_TEXTURES);
            ValidateProgram(
                denoise4,
                SsaoShaderContract.DENOISE_4_PATH,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES);
            ValidateProgram(
                denoise8,
                SsaoShaderContract.DENOISE_8_PATH,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES);
            ValidateProgram(
                denoise16,
                SsaoShaderContract.DENOISE_16_PATH,
                SsaoShaderContract.DENOISE_UNIFORMS,
                SsaoShaderContract.DENOISE_TEXTURES);
            ValidateProgram(
                composite,
                SsaoShaderContract.COMPOSITE_PATH,
                SsaoShaderContract.COMPOSITE_UNIFORMS,
                SsaoShaderContract.COMPOSITE_TEXTURES);
            ValidateBlueNoise(blueNoise);

            return new SsaoAssets(ao8, ao16, ao64, denoise4, denoise8, denoise16, composite, blueNoise);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            ReleaseQuietly(ao8);
            ReleaseQuietly(ao16);
            ReleaseQuietly(ao64);
            ReleaseQuietly(denoise4);
            ReleaseQuietly(denoise8);
            ReleaseQuietly(denoise16);
            ReleaseQuietly(composite);
            ReleaseQuietly(blueNoise);
            if (exception is AssetLoadException) {
                throw;
            }

            throw new AssetLoadException(
                "The engine's built-in SSAO assets are missing or corrupt: the three AO shader "
                    + "variants, the three denoise shader variants, the composite shader, and the "
                    + $"blue-noise texture '{SsaoShaderContract.BLUE_NOISE_PATH}' must all load from "
                    + "the built-in pack with the documented shader ABI.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception);
        }
    }

    /// <summary>Gets the AO program for a quality variant.</summary>
    /// <param name="quality">The defined quality variant.</param>
    /// <returns>The loaded AO program.</returns>
    /// <exception cref="ObjectDisposedException">The assets are disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is not defined.</exception>
    internal ShaderProgram AoProgram(SsaoQuality quality) {
        ThrowIfDisposed();
        return quality switch {
            SsaoQuality.Performance => _ao8,
            SsaoQuality.Low or SsaoQuality.Medium => _ao16,
            SsaoQuality.High or SsaoQuality.Ultra => _ao64,
            _ => throw new ArgumentOutOfRangeException(
                nameof(quality),
                quality,
                "The SSAO quality is not a defined value."),
        };
    }

    /// <summary>Gets the denoise program for a quality variant.</summary>
    /// <param name="quality">The defined quality variant.</param>
    /// <returns>The loaded denoise program.</returns>
    /// <exception cref="ObjectDisposedException">The assets are disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> is not defined.</exception>
    internal ShaderProgram DenoiseProgram(SsaoQuality quality) {
        ThrowIfDisposed();
        return quality switch {
            SsaoQuality.Performance or SsaoQuality.Low => _denoise4,
            SsaoQuality.Medium or SsaoQuality.High => _denoise8,
            SsaoQuality.Ultra => _denoise16,
            _ => throw new ArgumentOutOfRangeException(
                nameof(quality),
                quality,
                "The SSAO quality is not a defined value."),
        };
    }

    /// <summary>Gets the single composite program.</summary>
    /// <exception cref="ObjectDisposedException">The assets are disposed.</exception>
    internal ShaderProgram CompositeProgram {
        get {
            ThrowIfDisposed();
            return _composite;
        }
    }

    /// <summary>Gets the 128x128 RGBA8 blue-noise texture.</summary>
    /// <exception cref="ObjectDisposedException">The assets are disposed.</exception>
    internal Texture BlueNoise {
        get {
            ThrowIfDisposed();
            return _blueNoise;
        }
    }

    /// <summary>Releases every owner reference. Idempotent.</summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        ReleaseQuietly(_ao8);
        ReleaseQuietly(_ao16);
        ReleaseQuietly(_ao64);
        ReleaseQuietly(_denoise4);
        ReleaseQuietly(_denoise8);
        ReleaseQuietly(_denoise16);
        ReleaseQuietly(_composite);
        ReleaseQuietly(_blueNoise);
    }

    private static void ValidateProgram(
        ShaderProgram program,
        string path,
        ShaderUniformAsset[] uniforms,
        string[] textures) {
        ObjectDisposedException.ThrowIf(program.IsDisposed, program);
        const ShaderStageMask required = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if ((program.Stages & required) != required) {
            throw new InvalidOperationException(
                $"The built-in SSAO shader '{path}' must declare both the vertex and fragment stages; "
                    + $"it declares {program.Stages}.");
        }

        if (program.Uniforms.Count != uniforms.Length || program.Textures.Count != textures.Length) {
            throw new InvalidOperationException(
                $"The built-in SSAO shader '{path}' has {program.Uniforms.Count} uniform(s) and "
                    + $"{program.Textures.Count} texture(s); expected {uniforms.Length} and "
                    + $"{textures.Length}.");
        }

        for (int index = 0; index < uniforms.Length; index++) {
            ShaderUniform uniform = program.Uniforms[index];
            if (!string.Equals(uniform.Name, uniforms[index].Name, StringComparison.Ordinal)
                || uniform.Type != uniforms[index].Type) {
                throw new InvalidOperationException(
                    $"The built-in SSAO shader '{path}' declares uniform {index} as "
                        + $"'{uniform.Name}' ({uniform.Type}); expected '{uniforms[index].Name}' "
                        + $"({uniforms[index].Type}).");
            }
        }

        for (int index = 0; index < textures.Length; index++) {
            string name = program.Textures[index].Name;
            if (!string.Equals(name, textures[index], StringComparison.Ordinal)) {
                throw new InvalidOperationException(
                    $"The built-in SSAO shader '{path}' declares texture {index} as '{name}'; "
                        + $"expected '{textures[index]}'.");
            }
        }
    }

    private static void ValidateBlueNoise(Texture texture) {
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (texture.Width != 128
            || texture.Height != 128
            || texture.Format != TextureFormat.Rgba8Unorm
            || texture.IsSrgb
            || texture.Sampler != new SamplerState(TextureFilter.Nearest, TextureWrap.Repeat, 1f)) {
            throw new InvalidOperationException(
                $"The built-in SSAO blue-noise texture '{texture.Path}' must be a 128x128 linear "
                    + "Rgba8Unorm image with nearest filtering and repeat wrapping.");
        }
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
