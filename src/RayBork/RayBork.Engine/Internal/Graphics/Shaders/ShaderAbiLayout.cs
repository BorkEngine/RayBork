namespace RayBork.Internal;

/// <summary>The shared std430 CPU layout of the generated per-instance shader record.</summary>
/// <remarks>The 96-byte header holds transform at 0, color at 64, and UV rect at 80, followed by
/// texture index slots and reflected uniforms. Members follow std430 alignment, and the record is
/// rounded to 16 bytes and capped at 16 KiB.</remarks>
internal sealed class ShaderAbiLayout
{
    /// <summary>The byte size of the fixed instance header: transform, color, and UV rectangle.</summary>
    internal const int INSTANCE_HEADER_SIZE = 96;

    /// <summary>The offset of the instance <c>mat4 Transform</c>.</summary>
    internal const int TRANSFORM_OFFSET = 0;

    /// <summary>The offset of the instance <c>vec4 Color</c>.</summary>
    internal const int COLOR_OFFSET = 64;

    /// <summary>The offset of <c>vec4 UvRect</c>: normalized origin (X, Y) and size (Z, W).</summary>
    internal const int UV_RECT_OFFSET = 80;

    /// <summary>The offset of the first texture index slot.</summary>
    internal const int TEXTURE_INDEX_START = INSTANCE_HEADER_SIZE;

    /// <summary>The byte size of one texture index slot.</summary>
    internal const int TEXTURE_INDEX_SIZE = sizeof(uint);

    /// <summary>The alignment of the complete instance record.</summary>
    internal const int RECORD_ALIGNMENT = 16;

    /// <summary>The maximum supported instance record size.</summary>
    internal const int MAX_INSTANCE_RECORD_SIZE = 16 * 1024;

    private readonly ShaderAbiUniform[] _uniforms;
    private readonly ShaderAbiTexture[] _textures;

    /// <summary>Builds the instance layout from sidecar uniform and texture declarations.</summary>
    /// <param name="uniforms">The reflected uniforms in declaration order.</param>
    /// <param name="textures">The texture binding names in declaration order.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A name or uniform type is invalid.</exception>
    /// <exception cref="InvalidOperationException">The record exceeds
    /// <see cref="MAX_INSTANCE_RECORD_SIZE"/> bytes.</exception>
    internal ShaderAbiLayout(ShaderUniformAsset[] uniforms, string[] textures)
    {
        ArgumentNullException.ThrowIfNull(uniforms);
        ArgumentNullException.ThrowIfNull(textures);

        _textures = new ShaderAbiTexture[textures.Length];
        int offset = TEXTURE_INDEX_START;
        for (int index = 0; index < textures.Length; index++)
        {
            string? name = textures[index];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    $"The texture binding at index {index} has no name.",
                    nameof(textures)
                );
            }

            _textures[index] = new ShaderAbiTexture(name, index, offset);
            offset += TEXTURE_INDEX_SIZE;
            ThrowIfTooLarge(offset);
        }

        _uniforms = new ShaderAbiUniform[uniforms.Length];
        for (int index = 0; index < uniforms.Length; index++)
        {
            ShaderUniformAsset uniform = uniforms[index]
                ?? throw new ArgumentException(
                    $"The uniform at index {index} is null.",
                    nameof(uniforms)
                );
            if (string.IsNullOrWhiteSpace(uniform.Name))
            {
                throw new ArgumentException(
                    $"The uniform at index {index} has no name.",
                    nameof(uniforms)
                );
            }

            GetMemberLayout(uniform.Type, out int alignment, out int size, out int componentCount);
            offset = AlignUp(offset, alignment);
            _uniforms[index] = new ShaderAbiUniform(
                uniform.Name,
                uniform.Type,
                offset,
                size,
                componentCount
            );
            offset += size;
            ThrowIfTooLarge(offset);
        }

        InstanceRecordSize = AlignUp(offset, RECORD_ALIGNMENT);
        ThrowIfTooLarge(InstanceRecordSize);
    }

    /// <summary>Gets the complete per-instance record size in bytes, including the header.</summary>
    internal int InstanceRecordSize { get; }

    /// <summary>Gets the record data size in bytes after <see cref="INSTANCE_HEADER_SIZE"/>.</summary>
    internal int InstanceDataSize => InstanceRecordSize - INSTANCE_HEADER_SIZE;

    /// <summary>Gets the reflected uniforms in declaration order.</summary>
    internal IReadOnlyList<ShaderAbiUniform> Uniforms => _uniforms;

    /// <summary>Gets the texture bindings in declaration order and their index slots.</summary>
    internal IReadOnlyList<ShaderAbiTexture> Textures => _textures;

    /// <summary>Gets the instance offset of one texture index slot.</summary>
    /// <param name="textureIndex">The zero-based texture declaration index.</param>
    /// <returns>The absolute byte offset from the record start.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the texture list.</exception>
    internal int TextureIndexOffset(int textureIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(textureIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(textureIndex, _textures.Length);
        return _textures[textureIndex].IndexOffset;
    }

    /// <summary>Gets the instance offset of one reflected uniform.</summary>
    /// <param name="uniformIndex">The zero-based declaration index.</param>
    /// <returns>The absolute byte offset from the record start.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the uniform list.</exception>
    internal int UniformOffset(int uniformIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformIndex, _uniforms.Length);
        return _uniforms[uniformIndex].Offset;
    }

    /// <summary>Gets the byte size of one reflected uniform's value.</summary>
    /// <param name="uniformIndex">The zero-based declaration index.</param>
    /// <returns>The size excluding trailing alignment padding.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the uniform list.</exception>
    internal int UniformByteSize(int uniformIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uniformIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(uniformIndex, _uniforms.Length);
        return _uniforms[uniformIndex].Size;
    }

    /// <summary>Gets the std430 alignment, size, and component count of one uniform type.</summary>
    /// <param name="type">The reflected uniform type.</param>
    /// <param name="alignment">The required byte alignment.</param>
    /// <param name="size">The member size in bytes.</param>
    /// <param name="componentCount">The number of 32-bit components.</param>
    /// <exception cref="ArgumentException">The type is not supported by the shader ABI.</exception>
    internal static void GetMemberLayout(
        ShaderUniformType type,
        out int alignment,
        out int size,
        out int componentCount
    )
    {
        switch (type)
        {
            case ShaderUniformType.Float:
            case ShaderUniformType.Int:
            case ShaderUniformType.UInt:
            case ShaderUniformType.Bool:
                alignment = 4;
                size = 4;
                componentCount = 1;
                return;

            case ShaderUniformType.Vector2:
                alignment = 8;
                size = 8;
                componentCount = 2;
                return;

            case ShaderUniformType.Vector3:
                alignment = 16;
                size = 12;
                componentCount = 3;
                return;

            case ShaderUniformType.Vector4:
                alignment = 16;
                size = 16;
                componentCount = 4;
                return;

            case ShaderUniformType.Matrix4x4:
                alignment = 16;
                size = 64;
                componentCount = 16;
                return;

            default:
                throw new ArgumentException(
                    $"The uniform type '{type}' is not supported by the M4 shader ABI.",
                    nameof(type)
                );
        }
    }

    private static int AlignUp(int offset, int alignment) =>
        checked((offset + alignment - 1) / alignment * alignment);

    private static void ThrowIfTooLarge(int size)
    {
        if (size > MAX_INSTANCE_RECORD_SIZE)
        {
            throw new InvalidOperationException(
                $"The shader instance record is {size} bytes; the M4 shader ABI supports at most "
                    + $"{MAX_INSTANCE_RECORD_SIZE} bytes per instance."
            );
        }
    }
}
