namespace RayBork.Internal;

/// <summary>The engine-owned bloom shader programs loaded from the built-in pack.</summary>
/// <remarks>
/// The four programs (prefilter, downsample, upsample, composite) share one committed fullscreen
/// vertex payload and load before <c>IGame.Setup</c>. Disposal leaves a retained snapshot valid.
/// </remarks>
internal sealed class BloomAssets : IDisposable
{
    private readonly ShaderProgram _prefilter;
    private readonly ShaderProgram _downsample;
    private readonly ShaderProgram _upsample;
    private readonly ShaderProgram _composite;
    private bool _disposed;

    private BloomAssets(
        ShaderProgram prefilter,
        ShaderProgram downsample,
        ShaderProgram upsample,
        ShaderProgram composite
    )
    {
        _prefilter = prefilter;
        _downsample = downsample;
        _upsample = upsample;
        _composite = composite;
    }

    /// <summary>Gets whether the engine released the bloom assets.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Loads and validates every bloom built-in asset from the started catalog.</summary>
    /// <exception cref="AssetLoadException">
    /// A built-in bloom asset is missing, invalid, or cannot upload.
    /// </exception>
    internal static async Task<BloomAssets> LoadAsync(IAssetCatalog assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        ShaderProgram? prefilter = null;
        ShaderProgram? downsample = null;
        ShaderProgram? upsample = null;
        ShaderProgram? composite = null;

        try
        {
            prefilter = await assets
                .Load<ShaderProgram>(BloomShaderContract.PREFILTER_PATH)
                .ConfigureAwait(false);
            downsample = await assets
                .Load<ShaderProgram>(BloomShaderContract.DOWNSAMPLE_PATH)
                .ConfigureAwait(false);
            upsample = await assets
                .Load<ShaderProgram>(BloomShaderContract.UPSAMPLE_PATH)
                .ConfigureAwait(false);
            composite = await assets
                .Load<ShaderProgram>(BloomShaderContract.COMPOSITE_PATH)
                .ConfigureAwait(false);

            ValidateProgram(
                prefilter,
                BloomShaderContract.PREFILTER_PATH,
                BloomShaderContract.PREFILTER_UNIFORMS,
                BloomShaderContract.PREFILTER_TEXTURES
            );
            ValidateProgram(
                downsample,
                BloomShaderContract.DOWNSAMPLE_PATH,
                BloomShaderContract.DOWNSAMPLE_UNIFORMS,
                BloomShaderContract.DOWNSAMPLE_TEXTURES
            );
            ValidateProgram(
                upsample,
                BloomShaderContract.UPSAMPLE_PATH,
                BloomShaderContract.UPSAMPLE_UNIFORMS,
                BloomShaderContract.UPSAMPLE_TEXTURES
            );
            ValidateProgram(
                composite,
                BloomShaderContract.COMPOSITE_PATH,
                BloomShaderContract.COMPOSITE_UNIFORMS,
                BloomShaderContract.COMPOSITE_TEXTURES
            );

            return new BloomAssets(prefilter, downsample, upsample, composite);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReleaseQuietly(prefilter);
            ReleaseQuietly(downsample);
            ReleaseQuietly(upsample);
            ReleaseQuietly(composite);
            if (exception is AssetLoadException)
            {
                throw;
            }

            throw new AssetLoadException(
                "The engine's built-in bloom assets are missing or corrupt: the prefilter, downsample, "
                    + "upsample, and composite shader programs must all load from the built-in pack with "
                    + "the documented shader ABI.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception
            );
        }
    }

    /// <summary>Gets the threshold and soft-knee prefilter program.</summary>
    internal ShaderProgram PrefilterProgram
    {
        get
        {
            ThrowIfDisposed();
            return _prefilter;
        }
    }

    /// <summary>Gets the downsample program.</summary>
    internal ShaderProgram DownsampleProgram
    {
        get
        {
            ThrowIfDisposed();
            return _downsample;
        }
    }

    /// <summary>Gets the tent upsample program.</summary>
    internal ShaderProgram UpsampleProgram
    {
        get
        {
            ThrowIfDisposed();
            return _upsample;
        }
    }

    /// <summary>Gets the composite program.</summary>
    internal ShaderProgram CompositeProgram
    {
        get
        {
            ThrowIfDisposed();
            return _composite;
        }
    }

    /// <summary>Releases every owner reference. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseQuietly(_prefilter);
        ReleaseQuietly(_downsample);
        ReleaseQuietly(_upsample);
        ReleaseQuietly(_composite);
    }

    private static void ValidateProgram(
        ShaderProgram program,
        string path,
        ShaderUniformAsset[] uniforms,
        string[] textures
    )
    {
        ObjectDisposedException.ThrowIf(program.IsDisposed, program);
        const ShaderStageMask required = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if ((program.Stages & required) != required)
        {
            throw new InvalidOperationException(
                $"The built-in bloom shader '{path}' must declare both the vertex and fragment stages; "
                    + $"it declares {program.Stages}."
            );
        }

        if (program.Uniforms.Count != uniforms.Length || program.Textures.Count != textures.Length)
        {
            throw new InvalidOperationException(
                $"The built-in bloom shader '{path}' has {program.Uniforms.Count} uniform(s) and "
                    + $"{program.Textures.Count} texture(s); expected {uniforms.Length} and "
                    + $"{textures.Length}."
            );
        }

        for (int index = 0; index < uniforms.Length; index++)
        {
            ShaderUniform uniform = program.Uniforms[index];
            if (
                !string.Equals(uniform.Name, uniforms[index].Name, StringComparison.Ordinal)
                || uniform.Type != uniforms[index].Type
            )
            {
                throw new InvalidOperationException(
                    $"The built-in bloom shader '{path}' declares uniform {index} as "
                        + $"'{uniform.Name}' ({uniform.Type}); expected '{uniforms[index].Name}' "
                        + $"({uniforms[index].Type})."
                );
            }
        }

        for (int index = 0; index < textures.Length; index++)
        {
            string name = program.Textures[index].Name;
            if (!string.Equals(name, textures[index], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The built-in bloom shader '{path}' declares texture {index} as '{name}'; "
                        + $"expected '{textures[index]}'."
                );
            }
        }
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
