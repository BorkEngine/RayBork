using System.Numerics;

namespace RayBork.Internal;

/// <summary>One recorded draw call: a mesh, shader program, state, and instance transforms.</summary>
/// <remarks>
/// A value snapshot that owns pooled matrix, uniform-template, and texture-binding buffers, so the
/// render thread never reads a live game object. The tick thread fills a record while its frame slot
/// records; the packet seals it read-only, and the slot reuses the same buffers on its next acquisition.
/// </remarks>
internal sealed class RenderDrawRecord
{
    /// <summary>Largest per-instance binding count whose template bytes fit a 32-bit count.</summary>
    private const int MAX_TEXTURE_BINDING_COUNT = int.MaxValue / ShaderAbiLayout.TEXTURE_INDEX_SIZE;

    private EngineAsset? _mesh;
    private EngineAsset? _program;
    private Matrix4x4[] _worldMatrices = new Matrix4x4[4];
    private byte[] _instanceTemplate = [];
    private Texture?[] _textureBindings = [];
    private Texture?[] _instanceTextureBindings = [];
    private int _instanceCount;
    private int _instanceTemplateLength;
    private int _textureCount;
    private int _instanceTextureBindingCount;
    private int _instanceTextureBindingLength;
    private bool _sharedTextureStateDeclared;

    /// <summary>Gets the mesh asset, retained by the frame's retention set until the frame ends.</summary>
    /// <exception cref="InvalidOperationException">The record was reset.</exception>
    internal EngineAsset Mesh =>
        _mesh
        ?? throw new InvalidOperationException(
            "The draw record has no mesh; it was reset or never recorded."
        );

    /// <summary>Gets the mesh topology.</summary>
    internal PrimitiveTopology Topology { get; private set; }

    /// <summary>Gets the mesh-space bounds used for culling.</summary>
    internal BoundingBox MeshBounds { get; private set; }

    /// <summary>Gets the mesh index count.</summary>
    internal int IndexCount { get; private set; }

    /// <summary>
    /// Gets the internal shader program, retained by the frame's retention set until the frame ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">The record was reset.</exception>
    internal EngineAsset Program =>
        _program
        ?? throw new InvalidOperationException(
            "The draw record has no shader program; it was reset or never recorded."
        );

    /// <summary>Gets the effective render state.</summary>
    internal RenderState RenderState { get; private set; }

    /// <summary>Gets whether the draw rasterizes its triangles as lines instead of filling them.</summary>
    /// <remarks>
    /// Part of the batch and geometry pipeline keys, so it never merges with a filled draw.
    /// </remarks>
    internal bool Wireframe { get; private set; }

    /// <summary>Gets the per-instance linear tint.</summary>
    internal Color Tint { get; private set; }

    /// <summary>
    /// Gets the normalized UV rectangle: origin in <c>X</c> and <c>Y</c>, size in <c>Z</c> and <c>W</c>.
    /// </summary>
    /// <remarks>
    /// Draws that sample no sub-rectangle keep the full-texture default <c>(0, 0, 1, 1)</c>.
    /// </remarks>
    internal Vector4 UvRect { get; private set; }

    /// <summary>Gets the number of recorded instances.</summary>
    internal int InstanceCount => _instanceCount;

    /// <summary>Gets the per-instance shader uniform template, without the 96-byte instance header.</summary>
    internal ReadOnlySpan<byte> InstanceTemplate =>
        _instanceTemplate.AsSpan(0, _instanceTemplateLength);

    /// <summary>
    /// Gets one mesh-space-to-world matrix per instance, in recording order, including the model's local
    /// transform.
    /// </summary>
    internal ReadOnlySpan<Matrix4x4> GetWorldMatrices() => _worldMatrices.AsSpan(0, _instanceCount);

    /// <summary>
    /// Gets the shared texture bindings in declaration order; a <see langword="null"/> entry uses white.
    /// </summary>
    internal ReadOnlySpan<Texture?> GetTextureBindings() =>
        _textureBindings.AsSpan(0, _textureCount);

    /// <summary>Gets whether each instance carries its own texture set instead of one shared set.</summary>
    /// <remarks>
    /// When set, <see cref="GetTextureBindings"/> is empty and the renderer reads
    /// <see cref="GetInstanceTextureBindings"/>; the flag is not part of the batch key.
    /// </remarks>
    internal bool HasPerInstanceTextureBindings => _instanceTextureBindingCount > 0;

    /// <summary>Gets the bindings each instance carries, or <c>0</c> for a shared-set draw.</summary>
    internal int TextureBindingCount => _instanceTextureBindingCount;

    /// <summary>
    /// Gets the flat per-instance textures in instance-major order:
    /// <c>instance * TextureBindingCount + bindingIndex</c>; empty for a shared-set draw.
    /// </summary>
    /// <remarks>Exactly <c>InstanceCount * TextureBindingCount</c> entries for a per-instance draw.</remarks>
    internal ReadOnlySpan<Texture?> GetInstanceTextureBindings() =>
        _instanceTextureBindings.AsSpan(0, _instanceTextureBindingLength);

    /// <summary>Starts a new record and resets its per-draw values.</summary>
    /// <remarks>Requires a record rented from the recording frame packet.</remarks>
    internal void Begin(
        EngineAsset mesh,
        PrimitiveTopology topology,
        BoundingBox meshBounds,
        int indexCount,
        EngineAsset program,
        RenderState renderState,
        Color tint,
        bool wireframe = false
    )
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(program);

        _mesh = mesh;
        _program = program;
        Topology = topology;
        MeshBounds = meshBounds;
        IndexCount = indexCount;
        RenderState = renderState;
        Wireframe = wireframe;
        Tint = tint;
        UvRect = new Vector4(0f, 0f, 1f, 1f);
        _instanceCount = 0;
        _instanceTemplateLength = 0;
        _textureCount = 0;
        ClearInstanceTextureBindings();
        _instanceTextureBindingCount = 0;
        _sharedTextureStateDeclared = false;
    }

    /// <summary>Copies the instance template and shared texture bindings into the record.</summary>
    /// <remarks>
    /// Declares the shared form even when the bindings are empty, and is mutually exclusive with the
    /// per-instance form; a rejected call leaves the record unchanged.
    /// </remarks>
    internal void SetShaderState(
        ReadOnlySpan<byte> instanceTemplate,
        ReadOnlySpan<Texture?> textureBindings
    )
    {
        if (HasPerInstanceTextureBindings)
        {
            throw new InvalidOperationException(
                "The draw record already carries per-instance texture bindings and cannot also record "
                    + "a shared texture set. A record uses either SetShaderState or "
                    + "SetPerInstanceShaderState for its texture bindings."
            );
        }

        SetInstanceTemplate(instanceTemplate);
        if (_textureBindings.Length < textureBindings.Length)
        {
            _textureBindings = new Texture?[
                Math.Max(textureBindings.Length, _textureBindings.Length * 2)
            ];
        }

        textureBindings.CopyTo(_textureBindings);
        _textureCount = textureBindings.Length;
        _sharedTextureStateDeclared = true;
    }

    /// <summary>
    /// Captures a material's template and declares one texture-binding set per instance.
    /// </summary>
    /// <remarks>
    /// The material's program must be the record's program, and the per-instance form is exclusive
    /// with the shared form; a rejected call leaves the record unchanged. Textures are stored but not
    /// retained, so the recorder adds them to the frame's retention set. The pooled binding buffer is
    /// cleared by <see cref="BeginInstances"/>, <see cref="SetPerInstanceShaderState(Shader)"/>, and
    /// <see cref="Reset"/>.
    /// </remarks>
    internal void SetPerInstanceShaderState(Shader material)
    {
        ArgumentNullException.ThrowIfNull(material);
        // Reading the declared bindings first surfaces a disposed material as ObjectDisposedException.
        int textureBindingCount = material.Textures.Count;
        if (textureBindingCount == 0)
        {
            throw new InvalidOperationException(
                $"The shader '{material.Path}' declares no texture bindings, so it has no per-instance "
                    + "texture set to fill. Record the draw with SetShaderState instead."
            );
        }

        if (!ReferenceEquals(material.Program.Lifetime, _program))
        {
            throw new InvalidOperationException(
                $"The shader '{material.Path}' is not over the program the draw record began with. The "
                    + "record's instance template is laid out for its own program, so the material must "
                    + "be over that program; call Begin with the material's program first."
            );
        }

        SetPerInstanceShaderState(material.InstanceData, textureBindingCount);
    }

    /// <summary>
    /// Copies a raw instance template and declares a per-instance texture set of the given size.
    /// </summary>
    /// <remarks>
    /// Low-level form of <see cref="SetPerInstanceShaderState(Shader)"/>; every check runs before the
    /// record changes, so a rejected call leaves it untouched.
    /// </remarks>
    internal void SetPerInstanceShaderState(
        ReadOnlySpan<byte> instanceTemplate,
        int textureBindingCount
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBindingCount);
        if (textureBindingCount > MAX_TEXTURE_BINDING_COUNT)
        {
            throw new ArgumentOutOfRangeException(
                nameof(textureBindingCount),
                textureBindingCount,
                $"A per-instance texture set supports at most {MAX_TEXTURE_BINDING_COUNT} bindings; above "
                    + "that the index slots no longer fit in a 32-bit template byte count."
            );
        }

        if (_sharedTextureStateDeclared)
        {
            throw new InvalidOperationException(
                "The draw record already declared the shared shader state and cannot also record a "
                    + "per-instance texture set. A record uses either SetShaderState or "
                    + "SetPerInstanceShaderState for its texture bindings."
            );
        }

        int requiredTemplateBytes = textureBindingCount * ShaderAbiLayout.TEXTURE_INDEX_SIZE;
        if (instanceTemplate.Length < requiredTemplateBytes)
        {
            throw new InvalidOperationException(
                $"The shader instance template is {instanceTemplate.Length} byte(s), which cannot hold "
                    + $"the {textureBindingCount} declared texture index slot(s) ({requiredTemplateBytes} "
                    + "bytes). The template must start with one 32-bit index slot per texture binding; "
                    + "the draw's template and per-instance binding count disagree."
            );
        }

        SetInstanceTemplate(instanceTemplate);
        _instanceTextureBindingCount = textureBindingCount;
        UpdateInstanceTextureBindings();
    }

    /// <summary>Sets the normalized UV rectangle written to every instance of this draw.</summary>
    /// <remarks>Per-instance data, so it never splits or joins a batch.</remarks>
    internal void SetUvRect(Vector4 uvRect) => UvRect = uvRect;

    /// <summary>
    /// Marks <paramref name="count"/> instances recorded and returns the writable world-matrix buffer.
    /// </summary>
    /// <remarks>
    /// Also clears and resizes the per-instance texture buffer, so call it before
    /// <see cref="BeginInstanceTextureBindings"/> and fill the textures afterwards.
    /// </remarks>
    internal Span<Matrix4x4> BeginInstances(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_worldMatrices.Length < count)
        {
            _worldMatrices = new Matrix4x4[Math.Max(count, _worldMatrices.Length * 2)];
        }

        _instanceCount = count;
        UpdateInstanceTextureBindings();
        return _worldMatrices.AsSpan(0, count);
    }

    /// <summary>Gets the writable flat per-instance texture buffer, in instance-major order.</summary>
    /// <remarks>
    /// Call after <see cref="BeginInstances"/>: the span is <c>InstanceCount * TextureBindingCount</c>
    /// entries long and cleared first. It is pooled with the record, and a later
    /// <see cref="BeginInstances"/> or <see cref="Reset"/> clears and invalidates it. Filling it does
    /// not retain the textures; the recorder must add each to the frame's retention set.
    /// </remarks>
    internal Span<Texture?> BeginInstanceTextureBindings()
    {
        if (!HasPerInstanceTextureBindings)
        {
            throw new InvalidOperationException(
                "The draw record has no per-instance texture set; call SetPerInstanceShaderState "
                    + "before filling per-instance texture bindings."
            );
        }

        if (_instanceTextureBindingLength == 0)
        {
            throw new InvalidOperationException(
                "The draw record has no recorded instances; call BeginInstances before filling "
                    + "per-instance texture bindings."
            );
        }

        return _instanceTextureBindings.AsSpan(0, _instanceTextureBindingLength);
    }

    /// <summary>Releases asset references and clears both binding buffers for reuse.</summary>
    internal void Reset()
    {
        _mesh = null;
        _program = null;
        Array.Clear(_textureBindings, 0, _textureCount);
        _textureCount = 0;
        ClearInstanceTextureBindings();
        _instanceTextureBindingCount = 0;
        _instanceCount = 0;
        _instanceTemplateLength = 0;
        _sharedTextureStateDeclared = false;
        Topology = default;
        MeshBounds = default;
        IndexCount = 0;
        RenderState = default;
        Wireframe = false;
        Tint = default;
        UvRect = default;
    }

    private void SetInstanceTemplate(ReadOnlySpan<byte> instanceTemplate)
    {
        if (_instanceTemplate.Length < instanceTemplate.Length)
        {
            _instanceTemplate = new byte[
                Math.Max(instanceTemplate.Length, _instanceTemplate.Length * 2)
            ];
        }

        instanceTemplate.CopyTo(_instanceTemplate);
        _instanceTemplateLength = instanceTemplate.Length;
    }

    /// <summary>Resizes and clears the per-instance texture buffer for the recorded counts.</summary>
    private void UpdateInstanceTextureBindings()
    {
        int length =
            HasPerInstanceTextureBindings && _instanceCount != 0
                ? checked(_instanceCount * _instanceTextureBindingCount)
                : 0;
        ClearInstanceTextureBindings();
        if (length == 0)
        {
            return;
        }

        if (_instanceTextureBindings.Length < length)
        {
            _instanceTextureBindings = new Texture?[
                Math.Max(length, _instanceTextureBindings.Length * 2)
            ];
        }

        _instanceTextureBindingLength = length;
    }

    private void ClearInstanceTextureBindings()
    {
        if (_instanceTextureBindingLength != 0)
        {
            Array.Clear(_instanceTextureBindings, 0, _instanceTextureBindingLength);
            _instanceTextureBindingLength = 0;
        }
    }
}
