using RayBork.Internal;
namespace RayBork;

/// <summary>
/// One loaded shader program: a vertex and fragment stage pair with baked metadata and a default
/// render state.
/// </summary>
/// <remarks>
/// <para>
/// M3 exposes the loading and metadata surface. <see cref="Stages"/>, <see cref="Uniforms"/>, and
/// <see cref="Textures"/> come from the bake sidecar metadata, not from runtime SPIR-V reflection.
/// <c>SetUniform</c>, <c>ResetUniforms</c>, and the draw API arrive in M4.
/// </para>
/// <para>
/// <see cref="Path"/> and <see cref="IsDisposed"/> stay readable after disposal. Every other member
/// throws <see cref="ObjectDisposedException"/> once the asset is disposed.
/// </para>
/// </remarks>
public sealed class Shader : IAsset {
    private readonly EngineAsset _asset;
    private readonly ShaderStageMask _stages;
    private readonly RenderState _defaultRenderState;
    private readonly IReadOnlyList<ShaderUniform> _uniforms;
    private readonly IReadOnlyList<ShaderTextureBinding> _textures;

    /// <summary>
    /// Initializes a shader from its uploaded lifetime state and packed descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="descriptor">The packed shader descriptor.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Shader(EngineAsset asset, ShaderAssetDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);

        _asset = asset;
        _stages = descriptor.Stages;
        _defaultRenderState = descriptor.DefaultRenderState;

        ShaderUniform[] uniforms = new ShaderUniform[descriptor.Uniforms.Length];
        for (int i = 0; i < uniforms.Length; i++) {
            uniforms[i] = new ShaderUniform(descriptor.Uniforms[i].Name, descriptor.Uniforms[i].Type);
        }

        ShaderTextureBinding[] textures = new ShaderTextureBinding[descriptor.Textures.Length];
        for (int i = 0; i < textures.Length; i++) {
            textures[i] = new ShaderTextureBinding(descriptor.Textures[i], i);
        }

        _uniforms = Array.AsReadOnly(uniforms);
        _textures = Array.AsReadOnly(textures);
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>
    /// Gets the stages present in the program.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    public ShaderStageMask Stages {
        get {
            ThrowIfDisposed();
            return _stages;
        }
    }

    /// <summary>
    /// Gets the default render state baked for the shader. M4 uses it when a draw does not override the
    /// state.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    public RenderState DefaultRenderState {
        get {
            ThrowIfDisposed();
            return _defaultRenderState;
        }
    }

    /// <summary>
    /// Gets the reflected uniform declarations. The list is read-only and never reflects the current
    /// uniform values.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    public IReadOnlyList<ShaderUniform> Uniforms {
        get {
            ThrowIfDisposed();
            return _uniforms;
        }
    }

    /// <summary>
    /// Gets the texture bindings in bindless slot order. The list is read-only and never reflects the
    /// current texture assignments.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    public IReadOnlyList<ShaderTextureBinding> Textures {
        get {
            ThrowIfDisposed();
            return _textures;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the M4 draw recorder will retain per frame. The catalog owns
    /// the asset and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    public void Dispose() {
        if (_asset.IsDisposed) {
            throw new ObjectDisposedException(nameof(Shader));
        }

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
