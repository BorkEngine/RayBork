using System.Numerics;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>The frozen state of one <see cref="BloomEffect"/> inside a recorded camera pass.</summary>
/// <remarks>
/// Freezes the settings and the half-to-1/32 dual-filter pyramid, then composites at camera-target
/// size. Pyramid levels complete as auxiliary, the composite as color, so store-back runs once.
/// </remarks>
internal sealed class BloomEffectSnapshot : PostProcessEffectSnapshot
{
    private readonly PostProcessInstanceRecord[] _instanceLayout = new PostProcessInstanceRecord[
        BloomPyramid.MAX_PASSES
    ];
    private readonly Extent2D[] _levels = new Extent2D[BloomPyramid.MAX_LEVELS];
    private readonly VulkanPostTarget?[] _downsampleTargets = new VulkanPostTarget?[
        BloomPyramid.MAX_LEVELS
    ];

    private EngineAsset? _prefilterProgram;
    private EngineAsset? _downsampleProgram;
    private EngineAsset? _upsampleProgram;
    private EngineAsset? _compositeProgram;
    private BloomSettings _settings;
    private int _prefilterStride;
    private int _downsampleStride;
    private int _upsampleStride;
    private int _compositeStride;
    private int _thresholdOffset;
    private int _softKneeOffset;
    private int _scatterOffset;
    private int _tintOffset;
    private int _intensityOffset;

    private BloomEffectSnapshot() { }

    /// <summary>Gets the frozen threshold.</summary>
    internal float Threshold => _settings.Threshold;

    /// <summary>Gets the frozen soft knee.</summary>
    internal float SoftKnee => _settings.SoftKnee;

    /// <summary>Gets the frozen intensity.</summary>
    internal float Intensity => _settings.Intensity;

    /// <summary>Gets the frozen scatter.</summary>
    internal float Scatter => _settings.Scatter;

    /// <summary>Gets the frozen tint.</summary>
    internal Color Tint => _settings.Tint;

    /// <inheritdoc/>
    internal override ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => _instanceLayout;

    /// <summary>Rents a pooled snapshot filled with the frozen settings and built-in programs.</summary>
    /// <exception cref="ArgumentNullException">The effect or assets are <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">A bloom asset was disposed.</exception>
    internal static BloomEffectSnapshot Rent(
        PostProcessEffect owner,
        BloomSettings settings,
        BloomAssets assets
    )
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(assets);
        ObjectDisposedException.ThrowIf(assets.IsDisposed, assets);

        ShaderProgram prefilter = assets.PrefilterProgram;
        ShaderProgram downsample = assets.DownsampleProgram;
        ShaderProgram upsample = assets.UpsampleProgram;
        ShaderProgram composite = assets.CompositeProgram;
        ObjectDisposedException.ThrowIf(prefilter.IsDisposed, prefilter);
        ObjectDisposedException.ThrowIf(downsample.IsDisposed, downsample);
        ObjectDisposedException.ThrowIf(upsample.IsDisposed, upsample);
        ObjectDisposedException.ThrowIf(composite.IsDisposed, composite);

        BloomEffectSnapshot snapshot = owner.TakeSnapshot<BloomEffectSnapshot>()
            ?? new BloomEffectSnapshot();
        snapshot.Fill(settings, prefilter, downsample, upsample, composite);
        return snapshot;
    }

    /// <inheritdoc/>
    internal override void Retain(FrameAssetRetention retention)
    {
        ArgumentNullException.ThrowIfNull(retention);
        if (
            _prefilterProgram is null
            || _downsampleProgram is null
            || _upsampleProgram is null
            || _compositeProgram is null
        )
        {
            return;
        }

        retention.Add(_prefilterProgram);
        retention.Add(_downsampleProgram);
        retention.Add(_upsampleProgram);
        retention.Add(_compositeProgram);
    }

    /// <inheritdoc/>
    protected override void ReleaseState()
    {
        _prefilterProgram = null;
        _downsampleProgram = null;
        _upsampleProgram = null;
        _compositeProgram = null;
        _settings = default;
        _prefilterStride = 0;
        _downsampleStride = 0;
        _upsampleStride = 0;
        _compositeStride = 0;
        _thresholdOffset = 0;
        _softKneeOffset = 0;
        _scatterOffset = 0;
        _tintOffset = 0;
        _intensityOffset = 0;
        _instanceLayout.AsSpan().Clear();
        _levels.AsSpan().Clear();
        _downsampleTargets.AsSpan().Clear();
    }

    /// <inheritdoc/>
    internal override void Run(PostProcessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        EngineAsset prefilterProgram = _prefilterProgram
            ?? throw new InvalidOperationException("The bloom snapshot ran after it was reset.");
        EngineAsset downsampleProgram = _downsampleProgram
            ?? throw new InvalidOperationException("The bloom snapshot ran after it was reset.");
        EngineAsset upsampleProgram = _upsampleProgram
            ?? throw new InvalidOperationException("The bloom snapshot ran after it was reset.");
        EngineAsset compositeProgram = _compositeProgram
            ?? throw new InvalidOperationException("The bloom snapshot ran after it was reset.");

        // The chain input must be read before the first completion; the composite samples it.
        uint sourceColorSlot = context.SourceColorSlot;
        int levelCount = BloomPyramid.BuildLevels(
            checked((int)context.Extent.Width),
            checked((int)context.Extent.Height),
            _levels
        );

        Pipeline prefilterPipeline = context.GetOrCreateFullscreenPipeline(
            prefilterProgram,
            PostProcessContext.FullscreenRenderState
        );
        Pipeline compositePipeline = context.GetOrCreateFullscreenPipeline(
            compositeProgram,
            PostProcessContext.FullscreenRenderState
        );

        // A one-level pyramid is only the prefilter and the composite; do not create the downsample and
        // upsample pipelines when the target collapses to that case.
        Pipeline downsamplePipeline = default;
        Pipeline upsamplePipeline = default;
        if (levelCount > 1)
        {
            downsamplePipeline = context.GetOrCreateFullscreenPipeline(
                downsampleProgram,
                PostProcessContext.FullscreenRenderState
            );
            upsamplePipeline = context.GetOrCreateFullscreenPipeline(
                upsampleProgram,
                PostProcessContext.FullscreenRenderState
            );
        }

        VulkanPostTarget current = context.RentColorTarget(_levels[0]);
        RecordPrefilter(context, current, prefilterPipeline, sourceColorSlot);
        context.CompleteAuxiliaryTarget(current);
        _downsampleTargets[0] = current;

        for (int level = 1; level < levelCount; level++)
        {
            VulkanPostTarget downsampled = context.RentColorTarget(_levels[level]);
            RecordDownsample(
                context,
                downsampled,
                downsamplePipeline,
                _downsampleTargets[level - 1]!.BindlessSlot
            );
            context.CompleteAuxiliaryTarget(downsampled);
            _downsampleTargets[level] = downsampled;
        }

        VulkanPostTarget bloom = _downsampleTargets[levelCount - 1]!;
        for (int level = levelCount - 2; level >= 0; level--)
        {
            VulkanPostTarget upsampled = context.RentColorTarget(_levels[level]);
            RecordUpsample(
                context,
                upsampled,
                upsamplePipeline,
                bloom.BindlessSlot,
                _downsampleTargets[level]!.BindlessSlot
            );
            context.CompleteAuxiliaryTarget(upsampled);
            bloom = upsampled;
        }

        VulkanPostTarget composite = context.RentColorTarget();
        RecordComposite(context, composite, compositePipeline, sourceColorSlot, bloom.BindlessSlot);
        context.CompleteColorTarget(composite);
    }

    private void Fill(
        BloomSettings settings,
        ShaderProgram prefilter,
        ShaderProgram downsample,
        ShaderProgram upsample,
        ShaderProgram composite
    )
    {
        _settings = settings;
        _prefilterProgram = prefilter.Lifetime;
        _downsampleProgram = downsample.Lifetime;
        _upsampleProgram = upsample.Lifetime;
        _compositeProgram = composite.Lifetime;
        _prefilterStride = prefilter.Abi.InstanceRecordSize;
        _downsampleStride = downsample.Abi.InstanceRecordSize;
        _upsampleStride = upsample.Abi.InstanceRecordSize;
        _compositeStride = composite.Abi.InstanceRecordSize;
        _thresholdOffset = UniformOffset(prefilter, "uThreshold");
        _softKneeOffset = UniformOffset(prefilter, "uSoftKnee");
        _scatterOffset = UniformOffset(upsample, "uScatter");
        _tintOffset = UniformOffset(composite, "uTint");
        _intensityOffset = UniformOffset(composite, "uIntensity");

        int index = 0;
        _instanceLayout[index++] = new PostProcessInstanceRecord(_prefilterStride, 1);
        for (int level = 1; level < BloomPyramid.MAX_LEVELS; level++)
        {
            _instanceLayout[index++] = new PostProcessInstanceRecord(_downsampleStride, 1);
        }

        for (int level = 1; level < BloomPyramid.MAX_LEVELS; level++)
        {
            _instanceLayout[index++] = new PostProcessInstanceRecord(_upsampleStride, 1);
        }

        _instanceLayout[index] = new PostProcessInstanceRecord(_compositeStride, 1);
    }

    private void RecordPrefilter(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint sourceSlot
    )
    {
        ulong offset = context.ReserveInstanceRecord(_prefilterStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _prefilterStride);
        WriteHeader(record);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, sourceSlot);
        WriteSingle(record, _thresholdOffset, _settings.Threshold);
        WriteSingle(record, _softKneeOffset, _settings.SoftKnee);
        context.RecordFullscreenPass(destination, pipeline, offset, _prefilterStride);
    }

    private void RecordDownsample(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint sourceSlot
    )
    {
        ulong offset = context.ReserveInstanceRecord(_downsampleStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _downsampleStride);
        WriteHeader(record);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, sourceSlot);
        context.RecordFullscreenPass(destination, pipeline, offset, _downsampleStride);
    }

    private void RecordUpsample(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint coarseSlot,
        uint fineSlot
    )
    {
        ulong offset = context.ReserveInstanceRecord(_upsampleStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _upsampleStride);
        WriteHeader(record);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, coarseSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + ShaderAbiLayout.TEXTURE_INDEX_SIZE,
            fineSlot
        );
        WriteSingle(record, _scatterOffset, _settings.Scatter);
        context.RecordFullscreenPass(destination, pipeline, offset, _upsampleStride);
    }

    private void RecordComposite(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint sourceSlot,
        uint bloomSlot
    )
    {
        ulong offset = context.ReserveInstanceRecord(_compositeStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _compositeStride);
        WriteHeader(record);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, sourceSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + ShaderAbiLayout.TEXTURE_INDEX_SIZE,
            bloomSlot
        );
        WriteVector4(
            record,
            _tintOffset,
            new Vector4(_settings.Tint.R, _settings.Tint.G, _settings.Tint.B, _settings.Tint.A)
        );
        WriteSingle(record, _intensityOffset, _settings.Intensity);
        context.RecordFullscreenPass(destination, pipeline, offset, _compositeStride);
    }

    private static int UniformOffset(ShaderProgram program, string name)
    {
        if (!program.UniformsByName.TryGetValue(name, out ShaderAbiUniform uniform))
        {
            throw new InvalidOperationException(
                $"The built-in bloom shader '{program.Path}' does not declare the required uniform '{name}'."
            );
        }

        return uniform.Offset;
    }

    private static void WriteHeader(Span<byte> record)
    {
        VulkanInstanceRecordWriter.WriteMatrix(
            record,
            ShaderAbiLayout.TRANSFORM_OFFSET,
            Matrix4x4.Identity
        );
        VulkanInstanceRecordWriter.WriteVector4(record, ShaderAbiLayout.COLOR_OFFSET, Vector4.One);
        VulkanInstanceRecordWriter.WriteVector4(
            record,
            ShaderAbiLayout.UV_RECT_OFFSET,
            new Vector4(0f, 0f, 1f, 1f)
        );
    }

    private static void WriteVector4(Span<byte> record, int offset, Vector4 value) =>
        VulkanInstanceRecordWriter.WriteVector4(record, offset, value);

    private static void WriteSingle(Span<byte> record, int offset, float value) =>
        VulkanInstanceRecordWriter.WriteSingle(record, offset, value);

    private static void WriteUInt32(Span<byte> record, int offset, uint value) =>
        VulkanInstanceRecordWriter.WriteUInt32(record, offset, value);
}
