namespace RayBork.Internal;

/// <summary>
/// The frozen state of one <see cref="ShaderPostProcessEffect"/> inside a recorded camera pass: the
/// internal shader program, the material cursor's per-instance uniform bytes, the material's texture
/// bindings, and the reserved source binding slots the engine overrides.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is created on the tick thread when the camera is created and copied into the frame's
/// <see cref="PostProcessEffectSnapshot"/> array. It owns no engine reference itself; the camera adds
/// the program and every bound texture to the frame's retention set with <see cref="Retain"/>, so
/// disposing the material after camera creation cannot corrupt the recorded frame.
/// </para>
/// <para>
/// The engine resolves the reserved <c>sourceColor</c> and <c>sourceDepth</c> texture index slots at
/// record time, so any texture the material had bound to those names is ignored. Every other uniform
/// and texture binding comes from the snapshot.
/// </para>
/// <para>
/// A snapshot is pooled by its effect and reused for a later camera: the uniform buffer, texture array,
/// and instance layout are kept and refilled instead of reallocated. The custom effect records exactly
/// one entry in <see cref="InstanceLayout"/>.
/// </para>
/// </remarks>
internal sealed class ShaderPostProcessEffectSnapshot : PostProcessEffectSnapshot {
    private EngineAsset? _program;
    private byte[] _instanceData = [];
    private int _instanceDataLength;
    private Texture?[] _textures = [];
    private int _textureCount;
    private readonly PostProcessInstanceRecord[] _instanceLayout = [default];
    private int _sourceColorBinding;
    private int _sourceDepthBinding;

    private ShaderPostProcessEffectSnapshot() {
    }

    /// <summary>
    /// Gets the captured per-instance uniform bytes, without the 96-byte instance header.
    /// </summary>
    internal ReadOnlySpan<byte> InstanceData => _instanceData.AsSpan(0, _instanceDataLength);

    /// <summary>Gets the frozen instance-record stride of the effect's one fullscreen pass.</summary>
    internal int InstanceStride => _instanceLayout[0].Stride;

    /// <summary>Gets the texture binding index that receives the source color.</summary>
    internal int SourceColorBinding => _sourceColorBinding;

    /// <summary>
    /// Gets the texture binding index that receives the target's resolved depth, or <c>-1</c> when the
    /// shader does not declare one.
    /// </summary>
    internal int SourceDepthBinding => _sourceDepthBinding;

    /// <inheritdoc/>
    internal override ReadOnlySpan<Texture?> TextureValues => _textures.AsSpan(0, _textureCount);

    /// <inheritdoc/>
    internal override ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => _instanceLayout;

    /// <inheritdoc/>
    internal override bool IsReservedTextureBinding(int index) =>
        index == _sourceColorBinding || index == _sourceDepthBinding;

    /// <summary>
    /// Rents a snapshot from the effect's pool, or creates one, and fills it from a shader program and
    /// one material cursor state.
    /// </summary>
    /// <param name="owner">The effect that will pool the snapshot after its camera pass is recycled.</param>
    /// <param name="program">The shader program the material uses.</param>
    /// <param name="instanceData">The material's captured uniform data without the instance header.</param>
    /// <param name="textures">The material's captured texture bindings in declaration order.</param>
    /// <param name="sourceColorBinding">The binding index of the reserved <c>sourceColor</c> name.</param>
    /// <param name="sourceDepthBinding">
    /// The binding index of the reserved <c>sourceDepth</c> name, or <c>-1</c> when it is not declared.
    /// </param>
    /// <param name="targetHasDepth">
    /// Whether the camera target has a resolved depth image for the source depth binding.
    /// </param>
    /// <param name="targetDescription">The target name used in the depthless-target error.</param>
    /// <returns>The captured snapshot.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="owner"/> or <paramref name="program"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The texture array does not hold one binding per program texture, or a reserved slot is outside
    /// the declared bindings.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The shader declares <c>sourceDepth</c> but the target has no depth attachment.
    /// </exception>
    internal static ShaderPostProcessEffectSnapshot Rent(
        PostProcessEffect owner,
        ShaderProgram program,
        ReadOnlySpan<byte> instanceData,
        ReadOnlySpan<Texture?> textures,
        int sourceColorBinding,
        int sourceDepthBinding,
        bool targetHasDepth,
        string targetDescription) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(program);
        int textureCount = program.Abi.Textures.Count;
        if (textures.Length != textureCount) {
            throw new ArgumentException(
                $"The shader '{program.Path}' declares {textureCount} texture binding(s), but the "
                    + $"captured material state has {textures.Length}.",
                nameof(textures));
        }

        if (instanceData.Length != program.Abi.InstanceDataSize) {
            throw new ArgumentException(
                $"The shader '{program.Path}' expects {program.Abi.InstanceDataSize} bytes of uniform "
                    + $"data, but the captured material state has {instanceData.Length}.",
                nameof(instanceData));
        }

        ValidateReservedBinding(program, sourceColorBinding, nameof(sourceColorBinding));
        if (sourceDepthBinding >= 0) {
            ValidateReservedBinding(program, sourceDepthBinding, nameof(sourceDepthBinding));
            if (!targetHasDepth) {
                throw new InvalidOperationException(
                    $"The post-process shader '{program.Path}' declares the reserved '{PostProcessNames.SOURCE_DEPTH}' "
                        + $"texture binding, but {targetDescription} has no depth attachment to bind to it. "
                        + "Use a target created with Depth: true, or remove the sourceDepth binding.");
            }
        }

        ShaderPostProcessEffectSnapshot snapshot =
            owner.TakeSnapshot<ShaderPostProcessEffectSnapshot>() ?? new ShaderPostProcessEffectSnapshot();
        snapshot.Fill(
            program,
            instanceData,
            textures,
            sourceColorBinding,
            sourceDepthBinding);
        return snapshot;
    }

    /// <inheritdoc/>
    internal override void Retain(FrameAssetRetention retention) {
        ArgumentNullException.ThrowIfNull(retention);
        if (_program is null) {
            return;
        }

        retention.Add(_program);
        for (int index = 0; index < _textureCount; index++) {
            if (IsReservedTextureBinding(index)) {
                // The engine overrides the reserved binding, so its material value is never sampled.
                continue;
            }

            Texture? texture = _textures[index];
            if (texture is not null) {
                retention.Add(texture.RetentionTarget);
            }
        }
    }

    /// <inheritdoc/>
    protected override void ReleaseState() {
        _program = null;
        Array.Clear(_textures, 0, _textureCount);
        _textureCount = 0;
        _instanceDataLength = 0;
        _sourceColorBinding = 0;
        _sourceDepthBinding = -1;
        _instanceLayout[0] = default;
    }

    /// <inheritdoc/>
    internal override void Run(PostProcessContext context) {
        ArgumentNullException.ThrowIfNull(context);
        EngineAsset program = _program ?? throw new InvalidOperationException(
            "The post-process effect snapshot ran after it was reset.");
        VulkanPostTarget destination = context.RentColorTarget();
        context.RecordCustomShaderPass(destination, this, program);
        context.CompleteColorTarget(destination);
    }

    private void Fill(
        ShaderProgram program,
        ReadOnlySpan<byte> instanceData,
        ReadOnlySpan<Texture?> textures,
        int sourceColorBinding,
        int sourceDepthBinding) {
        _program = program.Lifetime;
        if (_instanceData.Length < instanceData.Length) {
            _instanceData = new byte[instanceData.Length];
        }

        instanceData.CopyTo(_instanceData);
        _instanceDataLength = instanceData.Length;
        if (_textures.Length < textures.Length) {
            _textures = new Texture?[textures.Length];
        }

        textures.CopyTo(_textures);
        _textureCount = textures.Length;
        _sourceColorBinding = sourceColorBinding;
        _sourceDepthBinding = sourceDepthBinding;
        _instanceLayout[0] = new PostProcessInstanceRecord(
            ShaderAbiLayout.INSTANCE_HEADER_SIZE + instanceData.Length,
            1);
    }

    private static void ValidateReservedBinding(ShaderProgram program, int binding, string parameterName) {
        ArgumentOutOfRangeException.ThrowIfNegative(binding);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(binding, program.Abi.Textures.Count, parameterName);
    }
}
