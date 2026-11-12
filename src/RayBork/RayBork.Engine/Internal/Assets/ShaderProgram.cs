using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// One internal GPU shader program: the vertex and fragment stage pair, its baked metadata, its default
/// render state, and the immutable bake defaults that every material over the program starts from.
/// </summary>
/// <remarks>
/// <para>
/// The public <see cref="Shader"/> type is a material instance, not a program. The catalog caches this
/// program by <c>(canonical path, typeof(ShaderProgram))</c> and reference-counts it like any other
/// engine asset; every <see cref="Shader"/> material acquired from it holds one owner reference and
/// releases it on <c>Dispose</c>. Two materials over the same program share the GPU program, the
/// metadata, and the bake defaults, while each keeps its own uniform cursor and texture bindings.
/// </para>
/// <para>
/// The program is an <see cref="EngineAsset"/> owner: the catalog holds the initial reference and the
/// GPU destruction is queued when the last material reference and every recorded frame hold are gone.
/// The metadata collections and the bake-default arrays are immutable after construction, so any
/// material may read them from the tick thread without locking.
/// </para>
/// </remarks>
internal sealed class ShaderProgram {
    private readonly EngineAsset _asset;
    private readonly ShaderStageMask _stages;
    private readonly RenderState _defaultRenderState;
    private readonly IReadOnlyList<ShaderUniform> _uniforms;
    private readonly IReadOnlyList<ShaderTextureBinding> _textures;
    private readonly ShaderAbiLayout _layout;
    private readonly Dictionary<string, ShaderAbiUniform> _uniformsByName;
    private readonly Dictionary<string, ShaderAbiTexture> _texturesByName;
    private readonly byte[] _defaultInstanceData;
    private readonly Texture?[] _defaultTextureValues;

    /// <summary>
    /// Initializes a program from its uploaded lifetime state and packed descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="descriptor">The packed shader descriptor.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The descriptor has invalid uniform metadata.</exception>
    /// <exception cref="InvalidOperationException">
    /// The per-instance record exceeds the shader ABI size limit.
    /// </exception>
    internal ShaderProgram(EngineAsset asset, ShaderAssetDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);

        _asset = asset;
        _stages = descriptor.Stages;
        _defaultRenderState = descriptor.DefaultRenderState;

        ShaderUniform[] uniforms = new ShaderUniform[descriptor.Uniforms.Length];
        for (int index = 0; index < uniforms.Length; index++) {
            uniforms[index] = new ShaderUniform(descriptor.Uniforms[index].Name, descriptor.Uniforms[index].Type);
        }

        ShaderTextureBinding[] textures = new ShaderTextureBinding[descriptor.Textures.Length];
        for (int index = 0; index < textures.Length; index++) {
            textures[index] = new ShaderTextureBinding(descriptor.Textures[index], index);
        }

        _uniforms = Array.AsReadOnly(uniforms);
        _textures = Array.AsReadOnly(textures);
        _layout = new ShaderAbiLayout(descriptor.Uniforms, descriptor.Textures);
        _defaultInstanceData = new byte[_layout.InstanceDataSize];
        _defaultTextureValues = new Texture?[descriptor.Textures.Length];

        _uniformsByName = new Dictionary<string, ShaderAbiUniform>(
            _layout.Uniforms.Count,
            StringComparer.Ordinal);
        foreach (ShaderAbiUniform uniform in _layout.Uniforms) {
            _uniformsByName[uniform.Name] = uniform;
        }

        _texturesByName = new Dictionary<string, ShaderAbiTexture>(
            _layout.Textures.Count,
            StringComparer.Ordinal);
        foreach (ShaderAbiTexture texture in _layout.Textures) {
            _texturesByName[texture.Name] = texture;
        }

        for (int index = 0; index < descriptor.Uniforms.Length; index++) {
            ShaderUniformAsset uniform = descriptor.Uniforms[index];
            ShaderAbiUniform member = _layout.Uniforms[index];
            if (uniform.Default.Length != 0 && uniform.Default.Length != member.ComponentCount) {
                throw new ArgumentException(
                    $"The uniform '{uniform.Name}' has {uniform.Default.Length} default component(s); "
                        + $"type '{uniform.Type}' requires {member.ComponentCount}.",
                    nameof(descriptor));
            }

            WriteDefault(_defaultInstanceData, member, uniform.Default);
        }
    }

    /// <summary>Gets the canonical public path with a leading slash.</summary>
    internal string Path => _asset.Path;

    /// <summary>Gets the stages present in the program.</summary>
    internal ShaderStageMask Stages => _stages;

    /// <summary>Gets the default render state baked for the program.</summary>
    internal RenderState DefaultRenderState => _defaultRenderState;

    /// <summary>Gets the reflected uniform declarations in sidecar declaration order.</summary>
    internal IReadOnlyList<ShaderUniform> Uniforms => _uniforms;

    /// <summary>Gets the texture bindings in bindless slot order.</summary>
    internal IReadOnlyList<ShaderTextureBinding> Textures => _textures;

    /// <summary>Gets whether the program was disposed. It stays readable after disposal.</summary>
    internal bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the catalog owns and the draw recorder retains per frame.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>Gets the per-instance layout derived from the sidecar declaration order.</summary>
    internal ShaderAbiLayout Abi => _layout;

    /// <summary>Gets the complete per-instance record size in bytes, including the 96-byte header.</summary>
    internal int InstanceDataSize => _layout.InstanceRecordSize;

    /// <summary>
    /// Gets the immutable bake-default instance template that starts at instance offset
    /// <see cref="ShaderAbiLayout.INSTANCE_HEADER_SIZE"/>.
    /// </summary>
    /// <remarks>
    /// The data is stable for the program lifetime. A new material copies it into its own cursor, and a
    /// draw that must use the baked defaults copies it directly. The texture index slots at the start
    /// stay zero because the draw recorder resolves them from the material's texture bindings.
    /// </remarks>
    internal ReadOnlySpan<byte> DefaultInstanceData => _defaultInstanceData;

    /// <summary>
    /// Gets the immutable bake-default texture bindings, one <see langword="null"/> per sidecar texture.
    /// </summary>
    internal ReadOnlySpan<Texture?> DefaultTextureValues => _defaultTextureValues;

    /// <summary>
    /// Gets the reflected uniforms indexed by name. The dictionary is immutable and shared by every
    /// material over the program.
    /// </summary>
    internal IReadOnlyDictionary<string, ShaderAbiUniform> UniformsByName => _uniformsByName;

    /// <summary>
    /// Gets the texture bindings indexed by name. The dictionary is immutable and shared by every
    /// material over the program.
    /// </summary>
    internal IReadOnlyDictionary<string, ShaderAbiTexture> TexturesByName => _texturesByName;

    /// <summary>
    /// Acquires one owner reference and returns a new independent material over this program.
    /// </summary>
    /// <returns>The material. The caller owns it and must dispose it exactly once.</returns>
    /// <exception cref="ObjectDisposedException">The program is disposed.</exception>
    internal Shader CreateMaterial() {
        _asset.AddOwnerReference();
        try {
            return new Shader(this);
        } catch {
            _asset.ReleaseOwnerReference();
            throw;
        }
    }

    private static void WriteDefault(byte[] destination, ShaderAbiUniform uniform, float[] defaults) {
        int start = uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE;
        Span<byte> target = destination.AsSpan(start, uniform.Size);
        if (defaults.Length == 0) {
            target.Clear();
            return;
        }

        for (int index = 0; index < uniform.ComponentCount; index++) {
            float value = defaults[index];
            Span<byte> component = target.Slice(index * sizeof(float), sizeof(float));
            switch (uniform.Type) {
                case ShaderUniformType.Float:
                    BinaryPrimitives.WriteSingleLittleEndian(component, value);
                    break;

                case ShaderUniformType.Int:
                    BinaryPrimitives.WriteInt32LittleEndian(component, (int)value);
                    break;

                case ShaderUniformType.UInt:
                    BinaryPrimitives.WriteUInt32LittleEndian(component, (uint)value);
                    break;

                case ShaderUniformType.Bool:
                    BinaryPrimitives.WriteUInt32LittleEndian(component, value != 0f ? 1u : 0u);
                    break;

                default:
                    BinaryPrimitives.WriteSingleLittleEndian(component, value);
                    break;
            }
        }
    }
}
