using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// One internal GPU shader program: its vertex and fragment stages, reflected metadata, default render
/// state, and immutable bake defaults.
/// </summary>
/// <remarks>
/// The catalog caches and reference-counts the program; every <see cref="Shader"/> holds one owner
/// reference. Metadata and bake defaults are immutable after construction, so materials read them
/// from the tick thread without locking.
/// </remarks>
internal sealed class ShaderProgram
{
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

    /// <summary>Initializes the program from its lifetime state and packed descriptor.</summary>
    /// <exception cref="ArgumentException">The descriptor metadata is invalid.</exception>
    internal ShaderProgram(EngineAsset asset, ShaderAssetDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);

        _asset = asset;
        _stages = descriptor.Stages;
        _defaultRenderState = descriptor.DefaultRenderState;

        ShaderUniform[] uniforms = new ShaderUniform[descriptor.Uniforms.Length];
        for (int index = 0; index < uniforms.Length; index++)
        {
            uniforms[index] = new ShaderUniform(
                descriptor.Uniforms[index].Name,
                descriptor.Uniforms[index].Type
            );
        }

        ShaderTextureBinding[] textures = new ShaderTextureBinding[descriptor.Textures.Length];
        for (int index = 0; index < textures.Length; index++)
        {
            textures[index] = new ShaderTextureBinding(descriptor.Textures[index], index);
        }

        _uniforms = Array.AsReadOnly(uniforms);
        _textures = Array.AsReadOnly(textures);
        _layout = new ShaderAbiLayout(descriptor.Uniforms, descriptor.Textures);
        _defaultInstanceData = new byte[_layout.InstanceDataSize];
        _defaultTextureValues = new Texture?[descriptor.Textures.Length];

        _uniformsByName = new Dictionary<string, ShaderAbiUniform>(
            _layout.Uniforms.Count,
            StringComparer.Ordinal
        );
        foreach (ShaderAbiUniform uniform in _layout.Uniforms)
        {
            _uniformsByName[uniform.Name] = uniform;
        }

        _texturesByName = new Dictionary<string, ShaderAbiTexture>(
            _layout.Textures.Count,
            StringComparer.Ordinal
        );
        foreach (ShaderAbiTexture texture in _layout.Textures)
        {
            _texturesByName[texture.Name] = texture;
        }

        for (int index = 0; index < descriptor.Uniforms.Length; index++)
        {
            ShaderUniformAsset uniform = descriptor.Uniforms[index];
            ShaderAbiUniform member = _layout.Uniforms[index];
            if (uniform.Default.Length != 0 && uniform.Default.Length != member.ComponentCount)
            {
                throw new ArgumentException(
                    $"The uniform '{uniform.Name}' has {uniform.Default.Length} default component(s); "
                        + $"type '{uniform.Type}' requires {member.ComponentCount}.",
                    nameof(descriptor)
                );
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

    /// <summary>Gets the reflected uniforms in sidecar declaration order.</summary>
    internal IReadOnlyList<ShaderUniform> Uniforms => _uniforms;

    /// <summary>Gets the texture bindings in bindless slot order.</summary>
    internal IReadOnlyList<ShaderTextureBinding> Textures => _textures;

    /// <summary>Gets whether the program is disposed; it stays readable after disposal.</summary>
    internal bool IsDisposed => _asset.IsDisposed;

    /// <summary>Gets the retention state the catalog owns and draws retain per frame.</summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>Gets the per-instance layout derived from the sidecar order.</summary>
    internal ShaderAbiLayout Abi => _layout;

    /// <summary>Gets the full per-instance record size, including the 96-byte header.</summary>
    internal int InstanceDataSize => _layout.InstanceRecordSize;

    /// <summary>Gets the immutable bake-default instance template.</summary>
    /// <remarks>
    /// The template starts at the instance header and is stable for the program lifetime; texture
    /// slots stay zero until the draw resolves them from the material bindings.
    /// </remarks>
    internal ReadOnlySpan<byte> DefaultInstanceData => _defaultInstanceData;

    /// <summary>Gets the immutable default texture bindings, one per declared texture.</summary>
    internal ReadOnlySpan<Texture?> DefaultTextureValues => _defaultTextureValues;

    /// <summary>Gets the reflected uniforms indexed by name; the dictionary is immutable.</summary>
    internal IReadOnlyDictionary<string, ShaderAbiUniform> UniformsByName => _uniformsByName;

    /// <summary>Gets the texture bindings indexed by name; the dictionary is immutable.</summary>
    internal IReadOnlyDictionary<string, ShaderAbiTexture> TexturesByName => _texturesByName;

    /// <summary>Creates a new independent material; the caller disposes it once.</summary>
    /// <exception cref="ObjectDisposedException">The program is disposed.</exception>
    internal Shader CreateMaterial()
    {
        _asset.AddOwnerReference();
        try
        {
            return new Shader(this);
        }
        catch
        {
            _asset.ReleaseOwnerReference();
            throw;
        }
    }

    private static void WriteDefault(byte[] destination, ShaderAbiUniform uniform, float[] defaults)
    {
        int start = uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE;
        Span<byte> target = destination.AsSpan(start, uniform.Size);
        if (defaults.Length == 0)
        {
            target.Clear();
            return;
        }

        for (int index = 0; index < uniform.ComponentCount; index++)
        {
            float value = defaults[index];
            Span<byte> component = target.Slice(index * sizeof(float), sizeof(float));
            switch (uniform.Type)
            {
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
