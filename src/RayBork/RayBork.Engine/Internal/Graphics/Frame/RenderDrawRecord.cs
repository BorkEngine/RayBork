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
/// the renderer resolves them from <see cref="GetTextureBindings"/> and writes the bindless indices at
/// <see cref="ShaderAbiLayout.TEXTURE_INDEX_START"/>.
/// </para>
/// </remarks>
internal sealed class RenderDrawRecord {
    private EngineAsset? _mesh;
    private EngineAsset? _program;
    private Matrix4x4[] _worldMatrices = new Matrix4x4[4];
    private byte[] _instanceTemplate = [];
    private Texture?[] _textureBindings = [];
    private int _instanceCount;
    private int _instanceTemplateLength;
    private int _textureCount;

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

    /// <summary>Gets the per-instance linear tint.</summary>
    internal Color Tint { get; private set; }

    /// <summary>Gets the per-instance UV rectangle. M4 always records <c>(0, 0, 1, 1)</c>.</summary>
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
    /// Gets the per-texture object in shader sidecar declaration order. A <see langword="null"/> entry
    /// uses the built-in white fallback.
    /// </summary>
    internal ReadOnlySpan<Texture?> GetTextureBindings() => _textureBindings.AsSpan(0, _textureCount);

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
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>.</exception>
    internal void Begin(
        EngineAsset mesh,
        PrimitiveTopology topology,
        BoundingBox meshBounds,
        int indexCount,
        EngineAsset program,
        RenderState renderState,
        Color tint) {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(program);

        _mesh = mesh;
        _program = program;
        Topology = topology;
        MeshBounds = meshBounds;
        IndexCount = indexCount;
        RenderState = renderState;
        Tint = tint;
        UvRect = new Vector4(0f, 0f, 1f, 1f);
        _instanceCount = 0;
        _instanceTemplateLength = 0;
        _textureCount = 0;
    }

    /// <summary>
    /// Copies the shader cursor instance template and texture bindings into the record.
    /// </summary>
    /// <param name="instanceTemplate">
    /// The shader's captured per-instance uniform data, without the 96-byte instance header.
    /// </param>
    /// <param name="textureBindings">The captured texture objects in declaration order.</param>
    internal void SetShaderState(ReadOnlySpan<byte> instanceTemplate, ReadOnlySpan<Texture?> textureBindings) {
        if (_instanceTemplate.Length < instanceTemplate.Length) {
            _instanceTemplate = new byte[Math.Max(instanceTemplate.Length, _instanceTemplate.Length * 2)];
        }

        instanceTemplate.CopyTo(_instanceTemplate);
        _instanceTemplateLength = instanceTemplate.Length;

        if (_textureBindings.Length < textureBindings.Length) {
            _textureBindings = new Texture?[Math.Max(textureBindings.Length, _textureBindings.Length * 2)];
        }

        textureBindings.CopyTo(_textureBindings);
        _textureCount = textureBindings.Length;
    }

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
        return _worldMatrices.AsSpan(0, count);
    }

    /// <summary>
    /// Releases the asset references and clears the counts for reuse by a later frame.
    /// </summary>
    internal void Reset() {
        _mesh = null;
        _program = null;
        Array.Clear(_textureBindings, 0, _textureCount);
        _textureCount = 0;
        _instanceCount = 0;
        _instanceTemplateLength = 0;
        Topology = default;
        MeshBounds = default;
        IndexCount = 0;
        RenderState = default;
        Tint = default;
        UvRect = default;
    }
}
