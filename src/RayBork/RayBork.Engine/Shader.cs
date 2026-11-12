using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One loaded shader program: a vertex and fragment stage pair with baked metadata, a default render
/// state, and a mutable uniform cursor.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Stages"/>, <see cref="Uniforms"/>, and <see cref="Textures"/> come from the bake sidecar
/// metadata, not from runtime SPIR-V reflection. <see cref="SetUniform(string, float)"/> and its
/// overloads write the cursor that the next draw snapshots; values are stored in the per-instance
/// shader ABI record, and the renderer resolves texture bindings to bindless indices.
/// </para>
/// <para>
/// The cursor is tick-thread affine: <see cref="SetUniform(string, float)"/> and its overloads plus
/// <see cref="ResetUniforms"/> throw <see cref="InvalidOperationException"/> from any other thread. Cursor mutation is safe against a
/// concurrent <see cref="Dispose"/> from another thread. A texture set into the cursor acquires an
/// engine owner reference, so disposing the public texture afterwards does not invalidate the cursor;
/// overwriting the binding, <see cref="ResetUniforms"/>, or disposing the shader releases it.
/// </para>
/// <para>
/// <see cref="Path"/> and <see cref="IsDisposed"/> stay readable after disposal. Every other public
/// member throws <see cref="ObjectDisposedException"/> once the asset is disposed.
/// </para>
/// </remarks>
public sealed class Shader : IAsset {
    private readonly EngineAsset _asset;
    private readonly ShaderStageMask _stages;
    private readonly RenderState _defaultRenderState;
    private readonly IReadOnlyList<ShaderUniform> _uniforms;
    private readonly IReadOnlyList<ShaderTextureBinding> _textures;
    private readonly ShaderAbiLayout _layout;
    private readonly Dictionary<string, ShaderAbiUniform> _uniformsByName;
    private readonly Dictionary<string, ShaderAbiTexture> _texturesByName;
    private readonly byte[] _instanceData;
    private readonly byte[] _defaultInstanceData;
    private readonly Texture?[] _textureValues;
    private readonly Texture?[] _defaultTextureValues;
    private readonly Lock _cursorGate = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a shader from its uploaded lifetime state and packed descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="descriptor">The packed shader descriptor.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The descriptor has invalid uniform metadata.</exception>
    /// <exception cref="InvalidOperationException">
    /// The per-instance record exceeds the shader ABI size limit.
    /// </exception>
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
        _layout = new ShaderAbiLayout(descriptor.Uniforms, descriptor.Textures);
        _instanceData = new byte[_layout.InstanceDataSize];
        _defaultInstanceData = new byte[_layout.InstanceDataSize];
        _textureValues = new Texture?[descriptor.Textures.Length];
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

        Array.Copy(_defaultInstanceData, _instanceData, _defaultInstanceData.Length);
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
    /// Gets the default render state baked for the shader. The draw recorder uses it when a draw does
    /// not override the state.
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
    /// Gets the internal retention state the draw recorder retains per frame. The catalog owns the
    /// asset and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>
    /// Gets the per-instance layout derived from the sidecar declaration order.
    /// </summary>
    internal ShaderAbiLayout Abi => _layout;

    /// <summary>Gets the complete per-instance record size in bytes, including the 96-byte header.</summary>
    internal int InstanceDataSize => _layout.InstanceRecordSize;

    /// <summary>
    /// Gets the reusable little-endian cursor template that starts at instance offset
    /// <see cref="ShaderAbiLayout.INSTANCE_HEADER_SIZE"/>.
    /// </summary>
    /// <remarks>
    /// The span is exactly <see cref="InstanceDataSize"/> minus the 96-byte header. Uniform values are
    /// stored at their layout offsets relative to the span; texture index slots stay zero because the
    /// draw recorder resolves them from <see cref="TextureValues"/>. The data is valid until the next
    /// cursor mutation (<see cref="SetUniform(string, float)"/> and its overloads or
    /// <see cref="ResetUniforms"/>) and is read synchronously by the draw recorder on the tick thread.
    /// The span does not allocate per call.
    /// </remarks>
    internal ReadOnlySpan<byte> InstanceData => _instanceData;

    /// <summary>
    /// Gets the current texture binding values in sidecar declaration order, or <see langword="null"/>
    /// for a binding that uses the built-in white fallback.
    /// </summary>
    /// <remarks>
    /// The span is valid until the next cursor mutation. The draw recorder copies it synchronously on
    /// the tick thread and retains the referenced textures for the frame.
    /// </remarks>
    internal ReadOnlySpan<Texture?> TextureValues => _textureValues;

    /// <summary>
    /// Gets the immutable bake-default instance template that starts at instance offset
    /// <see cref="ShaderAbiLayout.INSTANCE_HEADER_SIZE"/>.
    /// </summary>
    /// <remarks>
    /// This is the same data shape as <see cref="InstanceData"/>, but it always holds the bake defaults
    /// and is never changed by the cursor. A draw path that must use the shader's baked defaults, such
    /// as a model draw that has no per-draw override, copies this span. It is stable for the shader
    /// lifetime and allocates nothing.
    /// </remarks>
    internal ReadOnlySpan<byte> DefaultInstanceData => _defaultInstanceData;

    /// <summary>
    /// Gets the immutable bake-default texture bindings, one <see langword="null"/> per sidecar texture.
    /// </summary>
    /// <remarks>
    /// A draw path that must use the shader's baked defaults uses this span, so the renderer resolves
    /// every binding to the built-in white fallback. It is stable for the shader lifetime and acquires
    /// no owner references.
    /// </remarks>
    internal ReadOnlySpan<Texture?> DefaultTextureValues => _defaultTextureValues;

    /// <summary>
    /// Sets a <c>float</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a float uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, float value) {
        Span<byte> bytes = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.Float, bytes);
    }

    /// <summary>
    /// Sets an <c>int</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not an int uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, int value) {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.Int, bytes);
    }

    /// <summary>
    /// Sets a <c>uint</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a uint uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, uint value) {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.UInt, bytes);
    }

    /// <summary>
    /// Sets a <c>bool</c> uniform by name. The value is stored as the <c>uint</c> <c>0</c> or <c>1</c>.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a bool uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, bool value) {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value ? 1u : 0u);
        AssignUniform(name, ShaderUniformType.Bool, bytes);
    }

    /// <summary>
    /// Sets a <c>vec2</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a vec2 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Vector2 value) {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(bytes[..4], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(4, 4), value.Y);
        AssignUniform(name, ShaderUniformType.Vector2, bytes);
    }

    /// <summary>
    /// Sets a <c>vec3</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a vec3 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Vector3 value) {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes[..4], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(4, 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(8, 4), value.Z);
        AssignUniform(name, ShaderUniformType.Vector3, bytes);
    }

    /// <summary>
    /// Sets a <c>vec4</c> uniform by name.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a vec4 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Vector4 value) {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.X, value.Y, value.Z, value.W);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>
    /// Sets a <c>vec4</c> uniform by name from the quaternion's <c>x</c>, <c>y</c>, <c>z</c>, and
    /// <c>w</c> components.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a vec4 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Quaternion value) {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.X, value.Y, value.Z, value.W);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>
    /// Sets a <c>mat4</c> uniform by name. The sixteen components are stored in row-major order.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a mat4 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Matrix4x4 value) {
        Span<byte> bytes = stackalloc byte[64];
        WriteSingle(bytes, 0, value.M11);
        WriteSingle(bytes, 4, value.M12);
        WriteSingle(bytes, 8, value.M13);
        WriteSingle(bytes, 12, value.M14);
        WriteSingle(bytes, 16, value.M21);
        WriteSingle(bytes, 20, value.M22);
        WriteSingle(bytes, 24, value.M23);
        WriteSingle(bytes, 28, value.M24);
        WriteSingle(bytes, 32, value.M31);
        WriteSingle(bytes, 36, value.M32);
        WriteSingle(bytes, 40, value.M33);
        WriteSingle(bytes, 44, value.M34);
        WriteSingle(bytes, 48, value.M41);
        WriteSingle(bytes, 52, value.M42);
        WriteSingle(bytes, 56, value.M43);
        WriteSingle(bytes, 60, value.M44);
        AssignUniform(name, ShaderUniformType.Matrix4x4, bytes);
    }

    /// <summary>
    /// Sets a <c>vec4</c> uniform by name from the color's red, green, blue, and alpha components.
    /// </summary>
    /// <param name="name">The baked uniform name.</param>
    /// <param name="value">The new value.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a vec4 uniform.</exception>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Color value) {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.R, value.G, value.B, value.A);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>
    /// Sets a bindless texture binding by name.
    /// </summary>
    /// <param name="name">The baked texture binding name.</param>
    /// <param name="texture">
    /// The new texture, or <see langword="null"/> to use the built-in white fallback.
    /// </param>
    /// <remarks>
    /// A non-null texture acquires an engine owner reference that keeps it alive until the binding is
    /// overwritten, <see cref="ResetUniforms"/> runs, or the shader is disposed.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="name"/> is unknown or not a texture binding.</exception>
    /// <exception cref="ObjectDisposedException">The shader or <paramref name="texture"/> is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void SetUniform(string name, Texture? texture) {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(SetUniform));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_texturesByName.TryGetValue(name, out ShaderAbiTexture binding)) {
            if (_uniformsByName.ContainsKey(name)) {
                throw new ArgumentException(
                    $"The uniform '{name}' of shader '{Path}' is not a texture binding.",
                    nameof(name));
            }

            throw new ArgumentException(
                $"The shader '{Path}' has no texture binding named '{name}'.",
                nameof(name));
        }

        if (texture is not null) {
            texture.Lifetime.AddOwnerReference();
        }

        Texture? previous;
        try {
            lock (_cursorGate) {
                ThrowIfCursorDisposedLocked();
                previous = _textureValues[binding.Slot];
                _textureValues[binding.Slot] = texture;
            }
        } catch {
            if (texture is not null) {
                texture.Lifetime.ReleaseOwnerReference();
            }

            throw;
        }

        if (previous is not null) {
            previous.Lifetime.ReleaseOwnerReference();
        }
    }

    /// <summary>
    /// Restores every uniform to its bake default and clears every texture binding to the built-in
    /// white fallback.
    /// </summary>
    /// <remarks>
    /// Every texture owner reference the cursor acquired is released exactly once.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The shader is disposed.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void ResetUniforms() {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(ResetUniforms));

        Texture?[] previous;
        lock (_cursorGate) {
            ThrowIfCursorDisposedLocked();
            Array.Copy(_defaultInstanceData, _instanceData, _instanceData.Length);
            previous = [.. _textureValues];
            Array.Clear(_textureValues);
        }

        foreach (Texture? texture in previous) {
            if (texture is not null) {
                texture.Lifetime.ReleaseOwnerReference();
            }
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    public void Dispose() {
        ThrowIfDisposed();

        Texture?[] owned;
        lock (_cursorGate) {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(Shader));
            }

            _disposed = true;
            owned = [.. _textureValues];
            Array.Clear(_textureValues);
        }

        foreach (Texture? texture in owned) {
            if (texture is not null) {
                texture.Lifetime.ReleaseOwnerReference();
            }
        }

        _asset.ReleaseOwnerReference();
    }

    private void AssignUniform(string name, ShaderUniformType expectedType, ReadOnlySpan<byte> value) {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(SetUniform));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_uniformsByName.TryGetValue(name, out ShaderAbiUniform uniform)) {
            if (_texturesByName.ContainsKey(name)) {
                throw new ArgumentException(
                    $"The uniform '{name}' of shader '{Path}' is a texture binding; it cannot be set "
                        + $"from a {expectedType} value.",
                    nameof(name));
            }

            throw new ArgumentException(
                $"The shader '{Path}' has no uniform named '{name}'.",
                nameof(name));
        }

        if (uniform.Type != expectedType) {
            throw new ArgumentException(
                $"The uniform '{name}' of shader '{Path}' is {uniform.Type}; it cannot be set from a "
                    + $"{expectedType} value.",
                nameof(name));
        }

        if (value.Length != uniform.Size) {
            throw new ArgumentException(
                $"The uniform '{name}' of shader '{Path}' has the layout size {uniform.Size} bytes, "
                    + $"but the value is {value.Length} bytes.",
                nameof(value));
        }

        lock (_cursorGate) {
            ThrowIfCursorDisposedLocked();
            value.CopyTo(_instanceData.AsSpan(uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE));
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

    private static void WriteVector4(Span<byte> destination, float x, float y, float z, float w) {
        WriteSingle(destination, 0, x);
        WriteSingle(destination, 4, y);
        WriteSingle(destination, 8, z);
        WriteSingle(destination, 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(offset, sizeof(float)), value);

    private void ThrowIfCursorDisposedLocked() {
        if (_disposed || _asset.IsDisposed) {
            throw new ObjectDisposedException(nameof(Shader));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
