using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// One recorded draw call: a mesh/shader-program/state snapshot with one or more instance transforms.
/// </summary>
/// <remarks>
/// <para>
/// The record is a value snapshot. It owns a pooled world-matrix buffer, a pooled instance-uniform
/// template, and a pooled texture-binding buffer, so the render thread never reads a live game object
/// or a mutable list. The tick thread fills a record only while its frame slot is recording; after the
/// packet is sealed the record is read-only, and the slot reuses the same buffers on its next
/// acquisition. The read accessors below are the internal seam for the renderer and the batch planner.
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
/// (<see cref="SetPerInstanceShaderState"/>). The two forms are mutually exclusive; a per-instance set
/// is what lets text draws batch different glyph textures into one instanced draw. Neither form adds
/// engine-asset references: the recorder retains every referenced texture in the frame's retention set.
/// </para>
/// </remarks>
internal sealed class RenderDrawRecord {
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
    }

    /// <summary>
    /// Copies the shader cursor instance template and shared texture bindings into the record.
    /// </summary>
    /// <param name="instanceTemplate">
    /// The shader's captured per-instance uniform data, without the 96-byte instance header.
    /// </param>
    /// <param name="textureBindings">The captured texture objects in declaration order.</param>
    /// <remarks>
    /// A record that already declared a per-instance set rejects this shared setter, even with an empty
    /// set, so the instance template cannot be replaced behind the per-instance bindings. The record is
    /// left unchanged.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The record already carries a per-instance set; see <see cref="SetPerInstanceShaderState"/>.
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
    }

    /// <summary>
    /// Copies the shader cursor instance template and declares that every instance of this draw carries
    /// its own texture-binding set.
    /// </summary>
    /// <param name="instanceTemplate">
    /// The shader's captured per-instance uniform data, without the 96-byte instance header. It must
    /// carry one 32-bit texture index slot per declared binding, starting at
    /// <see cref="ShaderAbiLayout.TEXTURE_INDEX_START"/>.
    /// </param>
    /// <param name="textureBindingCount">
    /// The number of texture bindings the shader declares. Every instance fills one texture per
    /// binding, in sidecar declaration order.
    /// </param>
    /// <remarks>
    /// <para>
    /// A text draw records one instance per glyph and binds that glyph's bindless texture here, so
    /// glyphs with different textures still form one instanced draw. Uniform values, the UV rectangle,
    /// and these bindings are per-instance data and never split a batch.
    /// </para>
    /// <para>
    /// The set is filled instance by instance through <see cref="BeginInstanceTextureBindings"/> after
    /// the instances are declared with <see cref="BeginInstances"/>. The buffer is pooled and reused
    /// across frames exactly like the shared binding buffer, so filling it allocates nothing after
    /// warm-up, and <see cref="Reset"/> clears it so a pooled record never keeps a texture alive.
    /// </para>
    /// <para>
    /// Frame retention stays the recorder's job: the record stores the texture objects but acquires no
    /// engine reference, so the recorder must add every per-instance texture to the frame's retention
    /// set exactly as it does for a shared binding.
    /// </para>
    /// <para>
    /// A record uses either this per-instance form or the shared <see cref="SetShaderState"/> form.
    /// Calling one setter after the other throws <see cref="InvalidOperationException"/> and leaves the
    /// record unchanged.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="textureBindingCount"/> is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The template is too small to hold one index slot per binding, or the record already carries a
    /// non-empty shared texture set. The record is left unchanged.
    /// </exception>
    internal void SetPerInstanceShaderState(ReadOnlySpan<byte> instanceTemplate, int textureBindingCount) {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(textureBindingCount);
        if (_textureCount > 0) {
            throw new InvalidOperationException(
                $"The draw record already carries {_textureCount} shared texture binding(s) and cannot "
                    + "also record a per-instance texture set. A record uses either SetShaderState or "
                    + "SetPerInstanceShaderState for its texture bindings.");
        }

        int requiredTemplateBytes = checked(textureBindingCount * ShaderAbiLayout.TEXTURE_INDEX_SIZE);
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
    /// <see cref="SetPerInstanceShaderState"/>, in instance-major order:
    /// <c>instance * TextureBindingCount + bindingIndex</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it after <see cref="BeginInstances"/> declared the instances: the returned span is exactly
    /// <c>InstanceCount * TextureBindingCount</c> entries long and is cleared before it is handed out.
    /// The buffer is pooled with the record, so a warm record's call allocates nothing, and a later
    /// <see cref="BeginInstances"/>, <see cref="SetPerInstanceShaderState"/>, or <see cref="Reset"/>
    /// invalidates the span and clears the buffer.
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
        ClearInstanceTextureBindings();
        if (!HasPerInstanceTextureBindings || _instanceCount == 0) {
            return;
        }

        int length = checked(_instanceCount * _instanceTextureBindingCount);
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
