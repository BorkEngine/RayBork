using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One material instance: a reference to an internal shader program plus this material's own uniform
/// cursor and texture bindings.
/// </summary>
/// <remarks>
/// Each <see cref="IAssetCatalog.Load{T}"/> call returns an independent material over the same cached
/// program, and every cursor mutation is tick-thread only. <see cref="Path"/> and
/// <see cref="IsDisposed"/> stay readable after disposal; every other public member throws
/// <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class Shader : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly string _path;
    private readonly IReadOnlyDictionary<string, ShaderAbiUniform> _uniformsByName;
    private readonly IReadOnlyDictionary<string, ShaderAbiTexture> _texturesByName;
    private readonly byte[] _instanceData;
    private readonly Texture?[] _textureValues;
    private readonly Lock _cursorGate = new();
    private bool _disposed;

    /// <summary>
    /// Initializes one material over an already referenced program that it releases on
    /// <see cref="Dispose"/>.
    /// </summary>
    internal Shader(ShaderProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);

        _program = program;
        _path = program.Path;
        _uniformsByName = program.UniformsByName;
        _texturesByName = program.TexturesByName;
        _instanceData = new byte[program.Abi.InstanceDataSize];
        program.DefaultInstanceData.CopyTo(_instanceData);
        _textureValues = new Texture?[program.Abi.Textures.Count];
    }

    /// <summary>Gets the canonical program path; readable after disposal.</summary>
    public string Path => _path;

    /// <summary>Gets the shader stages the baked program declares.</summary>
    public ShaderStageMask Stages
    {
        get
        {
            ThrowIfDisposed();
            return _program.Stages;
        }
    }

    /// <summary>Gets the baked render state a draw uses unless it overrides it.</summary>
    public RenderState DefaultRenderState
    {
        get
        {
            ThrowIfDisposed();
            return _program.DefaultRenderState;
        }
    }

    /// <summary>Gets the baked uniform declarations, not the current values.</summary>
    public IReadOnlyList<ShaderUniform> Uniforms
    {
        get
        {
            ThrowIfDisposed();
            return _program.Uniforms;
        }
    }

    /// <summary>Gets the baked texture bindings in bindless slot order; read-only.</summary>
    public IReadOnlyList<ShaderTextureBinding> Textures
    {
        get
        {
            ThrowIfDisposed();
            return _program.Textures;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _disposed || _program.IsDisposed;

    /// <summary>Gets the internal program this material references.</summary>
    internal ShaderProgram Program => _program;

    /// <summary>Gets the per-instance layout derived from the sidecar declaration order.</summary>
    internal ShaderAbiLayout Abi => _program.Abi;

    /// <summary>Gets the per-instance record size in bytes, including the 96-byte header.</summary>
    internal int InstanceDataSize => _program.InstanceDataSize;

    /// <summary>Gets the cursor template that starts after the 96-byte instance header.</summary>
    /// <remarks>
    /// Valid until the next cursor mutation; the draw recorder reads it on the tick thread. A draw
    /// copies it to instance-record offset 96.
    /// </remarks>
    internal ReadOnlySpan<byte> InstanceData => _instanceData;

    /// <summary>Gets the current texture bindings in sidecar order; <c>null</c> means white.</summary>
    /// <remarks>
    /// Valid until the next cursor mutation; the draw recorder retains the textures for the frame.
    /// </remarks>
    internal ReadOnlySpan<Texture?> TextureValues => _textureValues;

    /// <summary>Gets the immutable bake-default instance template.</summary>
    /// <remarks>
    /// A draw without overrides copies it to instance-record offset 96; it stays stable for the
    /// program lifetime.
    /// </remarks>
    internal ReadOnlySpan<byte> DefaultInstanceData => _program.DefaultInstanceData;

    /// <summary>Gets the immutable bake-default texture bindings.</summary>
    /// <remarks>
    /// All entries are <c>null</c>, so bindings resolve to the white fallback and stay stable for the
    /// program lifetime.
    /// </remarks>
    internal ReadOnlySpan<Texture?> DefaultTextureValues => _program.DefaultTextureValues;

    /// <summary>Writes a <c>float</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, float value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.Float, bytes);
    }

    /// <summary>Writes an <c>int</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.Int, bytes);
    }

    /// <summary>Writes a <c>uint</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        AssignUniform(name, ShaderUniformType.UInt, bytes);
    }

    /// <summary>Writes a <c>bool</c> as <c>0</c> or <c>1</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, bool value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value ? 1u : 0u);
        AssignUniform(name, ShaderUniformType.Bool, bytes);
    }

    /// <summary>Writes a <c>vec2</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Vector2 value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(bytes[..4], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(4, 4), value.Y);
        AssignUniform(name, ShaderUniformType.Vector2, bytes);
    }

    /// <summary>Writes a <c>vec3</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Vector3 value)
    {
        Span<byte> bytes = stackalloc byte[12];
        BinaryPrimitives.WriteSingleLittleEndian(bytes[..4], value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(4, 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.Slice(8, 4), value.Z);
        AssignUniform(name, ShaderUniformType.Vector3, bytes);
    }

    /// <summary>Writes a <c>vec4</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Vector4 value)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.X, value.Y, value.Z, value.W);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>Writes the quaternion components as a <c>vec4</c> uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Quaternion value)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.X, value.Y, value.Z, value.W);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>Writes a row-major <c>mat4</c> into the named uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Matrix4x4 value)
    {
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

    /// <summary>Writes the RGBA components as a <c>vec4</c> uniform.</summary>
    /// <exception cref="ArgumentException">The name is unknown or has another type.</exception>
    public void SetUniform(string name, Color value)
    {
        Span<byte> bytes = stackalloc byte[16];
        WriteVector4(bytes, value.R, value.G, value.B, value.A);
        AssignUniform(name, ShaderUniformType.Vector4, bytes);
    }

    /// <summary>Binds a texture, or <c>null</c> for the white fallback.</summary>
    /// <remarks>
    /// The binding holds an owner reference until it is overwritten,
    /// <see cref="ResetUniforms"/> runs, or the material is disposed.
    /// </remarks>
    /// <exception cref="ArgumentException">Unknown name or non-texture binding.</exception>
    /// <exception cref="ObjectDisposedException">The texture is disposed.</exception>
    public void SetUniform(string name, Texture? texture)
    {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(SetUniform));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_texturesByName.TryGetValue(name, out ShaderAbiTexture binding))
        {
            if (_uniformsByName.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"The uniform '{name}' of shader '{Path}' is not a texture binding.",
                    nameof(name)
                );
            }

            throw new ArgumentException(
                $"The shader '{Path}' has no texture binding named '{name}'.",
                nameof(name)
            );
        }

        if (texture is not null)
        {
            texture.AddOwnerReference();
        }

        Texture? previous;
        try
        {
            lock (_cursorGate)
            {
                ThrowIfCursorDisposedLocked();
                previous = _textureValues[binding.Slot];
                _textureValues[binding.Slot] = texture;
            }
        }
        catch
        {
            if (texture is not null)
            {
                texture.ReleaseOwnerReference();
            }

            throw;
        }

        if (previous is not null)
        {
            previous.ReleaseOwnerReference();
        }
    }

    /// <summary>Restores every uniform to its bake default and clears texture bindings.</summary>
    /// <remarks>Releases every texture owner reference the cursor holds.</remarks>
    public void ResetUniforms()
    {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(ResetUniforms));

        Texture?[] previous;
        lock (_cursorGate)
        {
            ThrowIfCursorDisposedLocked();
            _program.DefaultInstanceData.CopyTo(_instanceData);
            previous = [.. _textureValues];
            Array.Clear(_textureValues);
        }

        foreach (Texture? texture in previous)
        {
            if (texture is not null)
            {
                texture.ReleaseOwnerReference();
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>Releases only this material's references; other materials stay usable.</remarks>
    public void Dispose()
    {
        ThrowIfDisposed();

        Texture?[] owned;
        lock (_cursorGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _disposed = true;
            owned = [.. _textureValues];
            Array.Clear(_textureValues);
        }

        foreach (Texture? texture in owned)
        {
            if (texture is not null)
            {
                texture.ReleaseOwnerReference();
            }
        }

        try
        {
            _program.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown force-disposed the program after this material's reference was accounted for.
        }
    }

    private void AssignUniform(
        string name,
        ShaderUniformType expectedType,
        ReadOnlySpan<byte> value
    )
    {
        ThrowIfDisposed();
        TickThread.RequireCurrent(nameof(SetUniform));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_uniformsByName.TryGetValue(name, out ShaderAbiUniform uniform))
        {
            if (_texturesByName.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"The uniform '{name}' of shader '{Path}' is a texture binding; it cannot be set "
                        + $"from a {expectedType} value.",
                    nameof(name)
                );
            }

            throw new ArgumentException(
                $"The shader '{Path}' has no uniform named '{name}'.",
                nameof(name)
            );
        }

        if (uniform.Type != expectedType)
        {
            throw new ArgumentException(
                $"The uniform '{name}' of shader '{Path}' is {uniform.Type}; it cannot be set from a "
                    + $"{expectedType} value.",
                nameof(name)
            );
        }

        if (value.Length != uniform.Size)
        {
            throw new ArgumentException(
                $"The uniform '{name}' of shader '{Path}' has the layout size {uniform.Size} bytes, "
                    + $"but the value is {value.Length} bytes.",
                nameof(value)
            );
        }

        lock (_cursorGate)
        {
            ThrowIfCursorDisposedLocked();
            value.CopyTo(
                _instanceData.AsSpan(uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE)
            );
        }
    }

    private static void WriteVector4(Span<byte> destination, float x, float y, float z, float w)
    {
        WriteSingle(destination, 0, x);
        WriteSingle(destination, 4, y);
        WriteSingle(destination, 8, z);
        WriteSingle(destination, 12, w);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(offset, sizeof(float)), value);

    private void ThrowIfCursorDisposedLocked()
    {
        ObjectDisposedException.ThrowIf(_disposed || _program.IsDisposed, this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
