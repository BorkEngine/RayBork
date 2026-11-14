using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// One recorded draw call: a mesh/shader-program/state snapshot with one or more instance transforms.
/// </summary>
/// <remarks>
/// <para>
/// The record is a value snapshot. It owns a pooled world-matrix buffer, a pooled instance-uniform
/// template, and pooled shared and per-instance texture-binding buffers, so the render thread never
/// reads a live game object or a mutable list. The tick thread fills a record only while its frame slot
/// is recording; after the packet is sealed the record is read-only, and the slot reuses the same
/// buffers on its next acquisition. The read accessors below are the internal seam for the renderer and
/// the batch planner.
/// </para>
/// <para>
/// The instance-uniform template starts at instance offset
/// <see cref="ShaderAbiLayout.INSTANCE_HEADER_SIZE"/>: copying it to instance-record offset 96
/// reproduces the shader cursor or bake-default data. Texture index slots stay zero in the template;
/// the renderer resolves them from the record's texture references and writes the bindless indices at
/// <see cref="ShaderAbiLayout.TEXTURE_INDEX_START"/>.
/// </para>
/// <para>
/// A record carries either one shared texture-binding set for every instance
/// (<see cref="SetShaderState"/>) or one binding set per instance
/// (<see cref="SetPerInstanceShaderState(Shader)"/>). The two forms are mutually exclusive; a
/// per-instance set is what lets text draws batch different glyph textures into one instanced draw.
/// Neither form adds engine-asset references: the recorder retains every referenced texture in the
/// frame's retention set.
/// </para>
/// </remarks>
internal sealed class RenderDrawRecord {
    /// <summary>
    /// The largest per-instance binding count a template byte count can describe without overflowing a
    /// 32-bit multiplication.
    /// </summary>
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

    /// <summary>Gets the mesh asset. It is retained by the frame's retention set until the frame ends.</summary>
    /// <exception cref="InvalidOperationException">The record was reset.</exception>
    internal EngineAsset Mesh => _mesh
        ?? throw new InvalidOperationException("The draw record has no mesh; it was reset or never recorded.");

    /// <summary>Gets the mesh primitive topology.</summary>
    internal PrimitiveTopology Topology { get; private set; }

    /// <summary>Gets the mesh-space bounds used for frustum culling.</summary>
    internal BoundingBox MeshBounds { get; private set; }

    /// <summary>Gets the mesh index count, used for triangle statistics.</summary>
    internal int IndexCount { get; private set; }

    /// <summary>Gets the internal shader program asset. It is retained by the frame's retention set until the frame ends.</summary>
    /// <exception cref="InvalidOperationException">The record was reset.</exception>
    internal EngineAsset Program => _program
        ?? throw new InvalidOperationException("The draw record has no shader program; it was reset or never recorded.");

    /// <summary>Gets the effective render state of this draw.</summary>
    internal RenderState RenderState { get; private set; }

    /// <summary>
    /// Gets whether the draw rasterizes its triangles with the polygon-mode-line state instead of
    /// filling them.
    /// </summary>
    /// <remarks>
    /// The flag is part of the batch key and the geometry pipeline key, so a wireframe draw never
    /// merges with a filled draw over the same mesh, program, and render state. It is internal to the
    /// draw record: <see cref="RenderState"/> stays the public depth, culling, and blending contract.
    /// </remarks>
    internal bool Wireframe { get; private set; }

    /// <summary>Gets the per-instance linear tint.</summary>
    internal Color Tint { get; private set; }

    /// <summary>
    /// Gets the normalized UV rectangle written to every instance of this draw: <c>X</c> and <c>Y</c>
    /// are the texture-space origin and <c>Z</c> and <c>W</c> are the size. Draws that do not sample a
    /// sub-rectangle record <c>(0, 0, 1, 1)</c>.
    /// </summary>
    internal Vector4 UvRect { get; private set; }

    /// <summary>Gets the number of recorded instances.</summary>
    internal int InstanceCount => _instanceCount;

    /// <summary>Gets the per-instance shader uniform template, without the 96-byte instance header.</summary>
    internal ReadOnlySpan<byte> InstanceTemplate => _instanceTemplate.AsSpan(0, _instanceTemplateLength);

    /// <summary>
    /// Gets the world matrices in recording order, one per instance. The matrix already includes the
    /// model part's local transform; it maps mesh space to world space.
    /// </summary>
    internal ReadOnlySpan<Matrix4x4> GetWorldMatrices() => _worldMatrices.AsSpan(0, _instanceCount);

    /// <summary>
    /// Gets the shared per-texture object in shader sidecar declaration order. A <see langword="null"/>
    /// entry uses the built-in white fallback.
    /// </summary>
    internal ReadOnlySpan<Texture?> GetTextureBindings() => _textureBindings.AsSpan(0, _textureCount);

    /// <summary>
    /// Gets whether this draw carries one texture-binding set per instance instead of one shared set.
    /// </summary>
    /// <remarks>
    /// When this is <see langword="true"/>, <see cref="GetTextureBindings"/> is empty and the renderer
    /// reads the per-instance set from <see cref="GetInstanceTextureBindings"/>. The flag is not part of
    /// the batch key: per-instance bindings never split or join a batch.
    /// </remarks>
    internal bool HasPerInstanceTextureBindings => _instanceTextureBindingCount > 0;

    /// <summary>
    /// Gets the number of texture bindings each instance carries, or <c>0</c> for a draw that uses the
    /// shared set.
    /// </summary>
    internal int TextureBindingCount => _instanceTextureBindingCount;

    /// <summary>
    /// Gets the flat per-instance texture objects in instance-major order:
    /// <c>instance * TextureBindingCount + bindingIndex</c>. Empty for a shared-set draw.
    /// </summary>
    /// <remarks>
    /// The span has exactly <c>InstanceCount * TextureBindingCount</c> entries for a per-instance draw.
    /// Each rendered instance reads its own contiguous run, so instance 0's bindings are followed by
    /// instance 1's and so on.
    /// </remarks>
    internal ReadOnlySpan<Texture?> GetInstanceTextureBindings()
        => _instanceTextureBindings.AsSpan(0, _instanceTextureBindingLength);

    /// <summary>
    /// Starts a new record and resets its per-draw values. Called only with a record rented from the
    /// recording frame packet.
    /// </summary>
    /// <param name="mesh">The mesh asset.</param>
    /// <param name="topology">The mesh topology.</param>
    /// <param name="meshBounds">The mesh-space bounds.</param>
    /// <param name="indexCount">The mesh index count.</param>
    /// <param name="program">The internal shader program asset.</param>
    /// <param name="renderState">The effective render state.</param>
    /// <param name="tint">The linear tint.</param>
    /// <param name="wireframe">
    /// Whether to rasterize the triangles as lines instead of filling them. <c>false</c> draws the
    /// regular filled geometry.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    internal void Begin(
        EngineAsset mesh,
        PrimitiveTopology topology,
        BoundingBox meshBounds,
        int indexCount,
        EngineAsset program,
        RenderState renderState,
        Color tint,
        bool wireframe = false) {
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

    /// <summary>
    /// Copies the shader cursor instance template and shared texture bindings into the record.
    /// </summary>
    /// <param name="instanceTemplate">
    /// The shader's captured per-instance uniform data, without the 96-byte instance header.
    /// </param>
    /// <param name="textureBindings">The captured texture objects in declaration order.</param>
    /// <remarks>
    /// This declares the shared form, even when <paramref name="textureBindings"/> is empty. A record
    /// that already carries a per-instance set rejects this shared setter, and the record is left
    /// unchanged.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The record already carries a per-instance set; see
    /// <see cref="SetPerInstanceShaderState(Shader)"/>.
    /// </exception>
    internal void SetShaderState(ReadOnlySpan<byte> instanceTemplate, ReadOnlySpan<Texture?> textureBindings) {
        if (HasPerInstanceTextureBindings) {
            throw new InvalidOperationException(
                "The draw record already carries per-instance texture bindings and cannot also record "
                    + "a shared texture set. A record uses either SetShaderState or "
                    + "SetPerInstanceShaderState for its texture bindings.");
        }

        SetInstanceTemplate(instanceTemplate);
        if (_textureBindings.Length < textureBindings.Length) {
            _textureBindings = new Texture?[Math.Max(textureBindings.Length, _textureBindings.Length * 2)];
        }

        textureBindings.CopyTo(_textureBindings);
        _textureCount = textureBindings.Length;
        _sharedTextureStateDeclared = true;
    }

    /// <summary>
    /// Copies the material's captured instance template and declares that every instance of this draw
    /// carries its own texture-binding set, one texture per binding the material's program declares.
    /// </summary>
    /// <param name="material">
    /// The material whose cursor snapshot and declared texture bindings the record captures. It must not
    /// be disposed, and its program must be the program the record began with.
    /// </param>
    /// <remarks>
    /// <para>
    /// A text draw records one instance per glyph and binds that glyph's bindless texture here, so
    /// glyphs with different textures still form one instanced draw. The template and the binding count
    /// both come from the material, so a count that disagrees with the template cannot be recorded. The
    /// material's program must be the program the record began with; a different program would write
    /// another program's ABI into this pipeline's layout.
    /// </para>
    /// <para>
    /// The per-instance form changes only which texture each instance samples. The MSDF program samples
    /// the whole glyph texture and does not consume <see cref="UvRect"/>: one glyph is one padded
    /// texture sampled <c>0..1</c>, with the padding part of the glyph quad. <see cref="UvRect"/> stays
    /// the general per-instance sub-rectangle field for textured-quad shaders such as the billboard
    /// material, and text leaves it at the default <c>(0, 0, 1, 1)</c>.
    /// </para>
    /// <para>
    /// The set is filled instance by instance through <see cref="BeginInstanceTextureBindings"/>. The
    /// buffer is pooled and reused across frames exactly like the shared binding buffer, so filling it
    /// allocates nothing after warm-up. <see cref="BeginInstances"/>, this setter, and
    /// <see cref="Reset"/> clear it, so a pooled record never keeps a texture alive and stale bindings
    /// are never written into a frame.
    /// </para>
    /// <para>
    /// Frame retention stays the recorder's job: the record stores the texture objects but acquires no
    /// engine reference, so the recorder must add every per-instance texture to the frame's retention
    /// set exactly as it does for a shared binding.
    /// </para>
    /// <para>
    /// A record uses either this per-instance form or the shared <see cref="SetShaderState"/> form.
    /// Declaring one after the other throws <see cref="InvalidOperationException"/> and leaves the record
    /// unchanged.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="material"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The material was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The material's program declares no texture bindings, the material's program is not the record's
    /// program (including a record that has not begun), or the record already declared the shared form.
    /// The record is left unchanged.
    /// </exception>
    internal void SetPerInstanceShaderState(Shader material) {
        ArgumentNullException.ThrowIfNull(material);
        // Reading the declared bindings first surfaces a disposed material as ObjectDisposedException.
        int textureBindingCount = material.Textures.Count;
        if (textureBindingCount == 0) {
            throw new InvalidOperationException(
                $"The shader '{material.Path}' declares no texture bindings, so it has no per-instance "
                    + "texture set to fill. Record the draw with SetShaderState instead.");
        }

        if (!ReferenceEquals(material.Program.Lifetime, _program)) {
            throw new InvalidOperationException(
                $"The shader '{material.Path}' is not over the program the draw record began with. The "
                    + "record's instance template is laid out for its own program, so the material must "
                    + "be over that program; call Begin with the material's program first.");
        }

        SetPerInstanceShaderState(material.InstanceData, textureBindingCount);
    }

    /// <summary>
    /// Copies a raw instance template and declares a per-instance texture set of
    /// <paramref name="textureBindingCount"/> bindings.
    /// </summary>
    /// <param name="instanceTemplate">
    /// The per-instance uniform data without the 96-byte instance header. It must carry one 32-bit
    /// texture index slot per binding, starting at
    /// <see cref="ShaderAbiLayout.TEXTURE_INDEX_START"/>.
    /// </param>
    /// <param name="textureBindingCount">The number of texture bindings each instance carries.</param>
    /// <remarks>
    /// The low-level form of <see cref="SetPerInstanceShaderState(Shader)"/>. Engine recorders pass the
    /// material so the template and the count cannot disagree; this overload exists so a recorder that
    /// already holds a template snapshot, and the record unit tests, can exercise the validation. Every
    /// check runs before the record changes, so a rejected call leaves it untouched.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="textureBindingCount"/> is not positive or exceeds the index-slot byte count.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The template is too small to hold one index slot per binding, or the record already declared the
    /// shared form.
    /// </exception>
    internal void SetPerInstanceShaderState(ReadOnlySpan<byte> instanceTemplate, int textureBindingCount) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBindingCount);
        if (textureBindingCount > MAX_TEXTURE_BINDING_COUNT) {
            throw new ArgumentOutOfRangeException(
                nameof(textureBindingCount),
                textureBindingCount,
                $"A per-instance texture set supports at most {MAX_TEXTURE_BINDING_COUNT} bindings; above "
                    + "that the index slots no longer fit in a 32-bit template byte count.");
        }

        if (_sharedTextureStateDeclared) {
            throw new InvalidOperationException(
                "The draw record already declared the shared shader state and cannot also record a "
                    + "per-instance texture set. A record uses either SetShaderState or "
                    + "SetPerInstanceShaderState for its texture bindings.");
        }

        int requiredTemplateBytes = textureBindingCount * ShaderAbiLayout.TEXTURE_INDEX_SIZE;
        if (instanceTemplate.Length < requiredTemplateBytes) {
            throw new InvalidOperationException(
                $"The shader instance template is {instanceTemplate.Length} byte(s), which cannot hold "
                    + $"the {textureBindingCount} declared texture index slot(s) ({requiredTemplateBytes} "
                    + "bytes). The template must start with one 32-bit index slot per texture binding; "
                    + "the draw's template and per-instance binding count disagree.");
        }

        SetInstanceTemplate(instanceTemplate);
        _instanceTextureBindingCount = textureBindingCount;
        UpdateInstanceTextureBindings();
    }

    /// <summary>
    /// Sets the normalized UV rectangle written to every instance of this draw.
    /// </summary>
    /// <param name="uvRect">
    /// The texture-space origin in <c>X</c> and <c>Y</c> and the size in <c>Z</c> and <c>W</c>. The
    /// default full-texture rectangle is <c>(0, 0, 1, 1)</c>.
    /// </param>
    /// <remarks>
    /// The rectangle is per-instance data, so it never splits or joins a batch. A shader that samples a
    /// texture reads it as <c>rbInstance.UvRect</c>; the built-in mesh shader computes
    /// <c>UvRect.xy + uv * UvRect.zw</c>. Only the billboard source-rectangle overloads set a
    /// sub-rectangle; every other draw keeps the full-texture default.
    /// </remarks>
    internal void SetUvRect(Vector4 uvRect) => UvRect = uvRect;

    /// <summary>
    /// Ensures room for <paramref name="count"/> instances, marks them recorded, and returns the buffer
    /// the recorder fills with mesh-space-to-world matrices.
    /// </summary>
    /// <param name="count">The number of instances to record.</param>
    /// <returns>The writable matrix buffer, exactly <paramref name="count"/> entries long.</returns>
    /// <remarks>
    /// Declaring the instances also clears the per-instance texture buffer and resizes it to
    /// <c>count * TextureBindingCount</c> entries, so call this before
    /// <see cref="BeginInstanceTextureBindings"/> and fill the textures afterwards. A second call
    /// discards any bindings filled after the previous one.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    internal Span<Matrix4x4> BeginInstances(int count) {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_worldMatrices.Length < count) {
            _worldMatrices = new Matrix4x4[Math.Max(count, _worldMatrices.Length * 2)];
        }

        _instanceCount = count;
        UpdateInstanceTextureBindings();
        return _worldMatrices.AsSpan(0, count);
    }

    /// <summary>
    /// Returns the writable flat per-instance texture buffer for a draw recorded with
    /// <see cref="SetPerInstanceShaderState(Shader)"/>, in instance-major order:
    /// <c>instance * TextureBindingCount + bindingIndex</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it after <see cref="BeginInstances"/> declared the instances: the returned span is exactly
    /// <c>InstanceCount * TextureBindingCount</c> entries long and is cleared before it is handed out.
    /// The buffer is pooled with the record, so a warm record's call allocates nothing, and a later
    /// <see cref="BeginInstances"/>, <see cref="SetPerInstanceShaderState(Shader)"/>, or
    /// <see cref="Reset"/> invalidates the span and clears the buffer.
    /// </para>
    /// <para>
    /// Filling this buffer does not retain the textures. The recorder must add each one to the frame's
    /// retention set, exactly as it does for a shared binding, so the texture and its bindless slot stay
    /// alive until the render thread releases the frame slot.
    /// </para>
    /// </remarks>
    /// <returns>The writable per-instance texture buffer.</returns>
    /// <exception cref="InvalidOperationException">
    /// The record has no per-instance set, or no instances were recorded yet.
    /// </exception>
    internal Span<Texture?> BeginInstanceTextureBindings() {
        if (!HasPerInstanceTextureBindings) {
            throw new InvalidOperationException(
                "The draw record has no per-instance texture set; call SetPerInstanceShaderState "
                    + "before filling per-instance texture bindings.");
        }

        if (_instanceTextureBindingLength == 0) {
            throw new InvalidOperationException(
                "The draw record has no recorded instances; call BeginInstances before filling "
                    + "per-instance texture bindings.");
        }

        return _instanceTextureBindings.AsSpan(0, _instanceTextureBindingLength);
    }

    /// <summary>
    /// Releases the asset references and clears both texture-binding buffers and the counts for reuse by
    /// a later frame.
    /// </summary>
    internal void Reset() {
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

    private void SetInstanceTemplate(ReadOnlySpan<byte> instanceTemplate) {
        if (_instanceTemplate.Length < instanceTemplate.Length) {
            _instanceTemplate = new byte[Math.Max(instanceTemplate.Length, _instanceTemplate.Length * 2)];
        }

        instanceTemplate.CopyTo(_instanceTemplate);
        _instanceTemplateLength = instanceTemplate.Length;
    }

    /// <summary>
    /// Resizes and clears the per-instance texture buffer for the current instance and binding counts.
    /// </summary>
    /// <remarks>
    /// The buffer is cleared so a reused record never exposes a texture from an earlier draw, and the
    /// length is recomputed so the writable span always matches the recorded instances. Both callers
    /// validate their own arguments first, so a rejected call cannot reach here.
    /// </remarks>
    private void UpdateInstanceTextureBindings() {
        int length = HasPerInstanceTextureBindings && _instanceCount != 0
            ? checked(_instanceCount * _instanceTextureBindingCount)
            : 0;
        ClearInstanceTextureBindings();
        if (length == 0) {
            return;
        }

        if (_instanceTextureBindings.Length < length) {
            _instanceTextureBindings = new Texture?[Math.Max(length, _instanceTextureBindings.Length * 2)];
        }

        _instanceTextureBindingLength = length;
    }

    private void ClearInstanceTextureBindings() {
        if (_instanceTextureBindingLength != 0) {
            Array.Clear(_instanceTextureBindings, 0, _instanceTextureBindingLength);
            _instanceTextureBindingLength = 0;
        }
    }
}
