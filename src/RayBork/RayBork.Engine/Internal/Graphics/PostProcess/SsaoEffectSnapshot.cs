using System.Numerics;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The frozen state of one <see cref="SsaoEffect"/> inside a recorded camera pass.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot fixes the public settings and the quality variant, then records four fullscreen
/// passes: AO into the first camera-sized target, two bilateral denoise iterations through the same
/// two-target pair, and the composite from the original scene color. The AO and denoise targets are
/// released with <see cref="PostProcessContext.CompleteAuxiliaryTarget"/>; the final composite target is
/// completed with <see cref="PostProcessContext.CompleteColorTarget"/> so the engine store-back runs
/// once.
/// </para>
/// <para>
/// Every pass writes one shader-ABI instance record. The ordered layout has four entries, with the AO,
/// denoise, denoise, and composite strides, so the frame's instance buffer is sized for all of them.
/// The AO and denoise programs are selected by the frozen quality variant; changing the effect after
/// camera creation cannot change a recorded frame. The snapshot is pooled by the effect and keeps its
/// layout array across cameras.
/// </para>
/// </remarks>
internal sealed class SsaoEffectSnapshot : PostProcessEffectSnapshot {
    private readonly PostProcessInstanceRecord[] _instanceLayout = new PostProcessInstanceRecord[4];

    private EngineAsset? _aoProgram;
    private EngineAsset? _denoiseProgram;
    private EngineAsset? _compositeProgram;
    private Texture? _blueNoise;
    private SsaoSettings _settings;
    private int _aoStride;
    private int _denoiseStride;
    private int _compositeStride;
    private int _aoProjectionOffset;
    private int _aoRadiusOffset;
    private int _aoFalloffOffset;
    private int _aoBiasOffset;
    private int _aoBiasMultiplierOffset;
    private int _aoScreenSpaceOffset;
    private int _denoiseProjectionOffset;
    private int _denoiseRadiusOffset;
    private int _denoiseWorldRadiusOffset;
    private int _denoiseFalloffOffset;
    private int _denoiseScreenSpaceOffset;
    private int _denoiseIterationOffset;
    private int _compositeColorOffset;
    private int _compositeIntensityOffset;

    private SsaoEffectSnapshot() {
    }

    /// <summary>Gets the frozen quality variant.</summary>
    internal SsaoQuality Quality => _settings.Quality;

    /// <summary>Gets the frozen AO sample count.</summary>
    internal int AoSampleCount => SsaoQualityInfo.AoSampleCount(_settings.Quality);

    /// <summary>Gets the frozen denoise sample count.</summary>
    internal int DenoiseSampleCount => SsaoQualityInfo.DenoiseSampleCount(_settings.Quality);

    /// <summary>Gets the frozen world or screen-space radius.</summary>
    internal float Radius => _settings.Radius;

    /// <summary>Gets the frozen denoise radius in pixels.</summary>
    internal int DenoiseRadius => _settings.DenoiseRadius;

    /// <inheritdoc/>
    internal override ReadOnlySpan<PostProcessInstanceRecord> InstanceLayout => _instanceLayout;

    /// <summary>
    /// Rents a snapshot from the effect's pool, or creates one, and fills it with the settings and the
    /// quality's built-in shader programs.
    /// </summary>
    /// <param name="owner">The effect that will pool the snapshot after its camera pass is recycled.</param>
    /// <param name="settings">The frozen public settings.</param>
    /// <param name="assets">The loaded engine-owned SSAO assets.</param>
    /// <returns>The captured snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> or <paramref name="assets"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">An SSAO asset was disposed.</exception>
    internal static SsaoEffectSnapshot Rent(
        PostProcessEffect owner,
        SsaoSettings settings,
        SsaoAssets assets) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(assets);
        ObjectDisposedException.ThrowIf(assets.IsDisposed, assets);

        ShaderProgram ao = assets.AoProgram(settings.Quality);
        ShaderProgram denoise = assets.DenoiseProgram(settings.Quality);
        ShaderProgram composite = assets.CompositeProgram;
        ObjectDisposedException.ThrowIf(ao.IsDisposed, ao);
        ObjectDisposedException.ThrowIf(denoise.IsDisposed, denoise);
        ObjectDisposedException.ThrowIf(composite.IsDisposed, composite);
        Texture blueNoise = assets.BlueNoise;

        SsaoEffectSnapshot snapshot =
            owner.TakeSnapshot<SsaoEffectSnapshot>() ?? new SsaoEffectSnapshot();
        snapshot.Fill(settings, ao, denoise, composite, blueNoise);
        return snapshot;
    }

    /// <inheritdoc/>
    internal override void Retain(FrameAssetRetention retention) {
        ArgumentNullException.ThrowIfNull(retention);
        if (_aoProgram is null || _denoiseProgram is null || _compositeProgram is null || _blueNoise is null) {
            return;
        }

        retention.Add(_aoProgram);
        retention.Add(_denoiseProgram);
        retention.Add(_compositeProgram);
        retention.Add(_blueNoise.RetentionTarget);
    }

    /// <inheritdoc/>
    protected override void ReleaseState() {
        _aoProgram = null;
        _denoiseProgram = null;
        _compositeProgram = null;
        _blueNoise = null;
        _settings = default;
        _aoStride = 0;
        _denoiseStride = 0;
        _compositeStride = 0;
        _aoProjectionOffset = 0;
        _aoRadiusOffset = 0;
        _aoFalloffOffset = 0;
        _aoBiasOffset = 0;
        _aoBiasMultiplierOffset = 0;
        _aoScreenSpaceOffset = 0;
        _denoiseProjectionOffset = 0;
        _denoiseRadiusOffset = 0;
        _denoiseWorldRadiusOffset = 0;
        _denoiseFalloffOffset = 0;
        _denoiseScreenSpaceOffset = 0;
        _denoiseIterationOffset = 0;
        _compositeColorOffset = 0;
        _compositeIntensityOffset = 0;
        _instanceLayout.AsSpan().Clear();
    }

    /// <inheritdoc/>
    internal override void Run(PostProcessContext context) {
        ArgumentNullException.ThrowIfNull(context);
        EngineAsset aoProgram = _aoProgram
            ?? throw new InvalidOperationException("The SSAO snapshot ran after it was reset.");
        EngineAsset denoiseProgram = _denoiseProgram
            ?? throw new InvalidOperationException("The SSAO snapshot ran after it was reset.");
        EngineAsset compositeProgram = _compositeProgram
            ?? throw new InvalidOperationException("The SSAO snapshot ran after it was reset.");
        Texture blueNoise = _blueNoise
            ?? throw new InvalidOperationException("The SSAO snapshot ran after it was reset.");

        uint depthSlot = context.SourceDepthSlot;
        if (depthSlot == 0) {
            throw new InvalidOperationException(
                "The SSAO effect requires the camera target's resolved depth, but the render pass has "
                    + "no depth slot.");
        }

        // Capture the original scene color before the first CompleteColorTarget. The composite pass
        // samples this slot, while the AO and denoise passes use the chain's own intermediates.
        uint sourceColorSlot = context.SourceColorSlot;
        uint blueNoiseSlot = PostProcessContext.ResolveTextureSlot(blueNoise);
        if (!Matrix4x4.Invert(context.Pass.Projection, out Matrix4x4 projectionInverse)) {
            throw new InvalidOperationException(
                "The SSAO effect could not invert the camera projection matrix.");
        }

        Pipeline aoPipeline = context.GetOrCreateFullscreenPipeline(
            aoProgram,
            PostProcessContext.FullscreenRenderState);
        Pipeline denoisePipeline = context.GetOrCreateFullscreenPipeline(
            denoiseProgram,
            PostProcessContext.FullscreenRenderState);
        Pipeline compositePipeline = context.GetOrCreateFullscreenPipeline(
            compositeProgram,
            PostProcessContext.FullscreenRenderState);

        VulkanPostTarget ao = context.RentColorTarget();
        RecordAo(context, ao, aoPipeline, depthSlot, blueNoiseSlot, projectionInverse);
        context.CompleteAuxiliaryTarget(ao);

        VulkanPostTarget firstDenoise = context.RentColorTarget();
        RecordDenoise(
            context,
            firstDenoise,
            denoisePipeline,
            ao.BindlessSlot,
            depthSlot,
            blueNoiseSlot,
            projectionInverse,
            iteration: 0);
        context.CompleteAuxiliaryTarget(firstDenoise);

        VulkanPostTarget secondDenoise = context.RentColorTarget();
        RecordDenoise(
            context,
            secondDenoise,
            denoisePipeline,
            firstDenoise.BindlessSlot,
            depthSlot,
            blueNoiseSlot,
            projectionInverse,
            iteration: 1);
        context.CompleteAuxiliaryTarget(secondDenoise);

        VulkanPostTarget composite = context.RentColorTarget();
        RecordComposite(
            context,
            composite,
            compositePipeline,
            sourceColorSlot,
            secondDenoise.BindlessSlot);
        context.CompleteColorTarget(composite);
    }

    private void Fill(
        SsaoSettings settings,
        ShaderProgram ao,
        ShaderProgram denoise,
        ShaderProgram composite,
        Texture blueNoise) {
        _settings = settings;
        _aoProgram = ao.Lifetime;
        _denoiseProgram = denoise.Lifetime;
        _compositeProgram = composite.Lifetime;
        _blueNoise = blueNoise;
        _aoStride = ao.Abi.InstanceRecordSize;
        _denoiseStride = denoise.Abi.InstanceRecordSize;
        _compositeStride = composite.Abi.InstanceRecordSize;

        _aoProjectionOffset = UniformOffset(ao, "uProjectionInverse");
        _aoRadiusOffset = UniformOffset(ao, "uRadius");
        _aoFalloffOffset = UniformOffset(ao, "uDistanceFalloff");
        _aoBiasOffset = UniformOffset(ao, "uBiasOffset");
        _aoBiasMultiplierOffset = UniformOffset(ao, "uBiasMultiplier");
        _aoScreenSpaceOffset = UniformOffset(ao, "uScreenSpaceRadius");
        _denoiseProjectionOffset = UniformOffset(denoise, "uProjectionInverse");
        _denoiseRadiusOffset = UniformOffset(denoise, "uDenoiseRadius");
        _denoiseWorldRadiusOffset = UniformOffset(denoise, "uRadius");
        _denoiseFalloffOffset = UniformOffset(denoise, "uDistanceFalloff");
        _denoiseScreenSpaceOffset = UniformOffset(denoise, "uScreenSpaceRadius");
        _denoiseIterationOffset = UniformOffset(denoise, "uIteration");
        _compositeColorOffset = UniformOffset(composite, "uAoColor");
        _compositeIntensityOffset = UniformOffset(composite, "uIntensity");

        _instanceLayout[0] = new PostProcessInstanceRecord(_aoStride, 1);
        _instanceLayout[1] = new PostProcessInstanceRecord(_denoiseStride, 1);
        _instanceLayout[2] = new PostProcessInstanceRecord(_denoiseStride, 1);
        _instanceLayout[3] = new PostProcessInstanceRecord(_compositeStride, 1);
    }

    private void RecordAo(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint depthSlot,
        uint blueNoiseSlot,
        Matrix4x4 projectionInverse) {
        ulong offset = context.ReserveInstanceRecord(_aoStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _aoStride);
        WriteHeader(record);
        WriteMatrix(record, _aoProjectionOffset, projectionInverse);
        WriteSingle(record, _aoRadiusOffset, _settings.Radius);
        WriteSingle(record, _aoFalloffOffset, _settings.DistanceFalloff);
        WriteSingle(record, _aoBiasOffset, _settings.BiasOffset);
        WriteSingle(record, _aoBiasMultiplierOffset, _settings.BiasMultiplier);
        WriteUInt32(record, _aoScreenSpaceOffset, _settings.ScreenSpaceRadius ? 1u : 0u);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, depthSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + ShaderAbiLayout.TEXTURE_INDEX_SIZE,
            blueNoiseSlot);
        context.RecordFullscreenPass(destination, pipeline, offset, _aoStride);
    }

    private void RecordDenoise(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint aoSlot,
        uint depthSlot,
        uint blueNoiseSlot,
        Matrix4x4 projectionInverse,
        uint iteration) {
        ulong offset = context.ReserveInstanceRecord(_denoiseStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _denoiseStride);
        WriteHeader(record);
        WriteMatrix(record, _denoiseProjectionOffset, projectionInverse);
        WriteSingle(record, _denoiseRadiusOffset, _settings.DenoiseRadius);
        WriteSingle(record, _denoiseWorldRadiusOffset, _settings.Radius);
        WriteSingle(record, _denoiseFalloffOffset, _settings.DistanceFalloff);
        WriteUInt32(record, _denoiseScreenSpaceOffset, _settings.ScreenSpaceRadius ? 1u : 0u);
        WriteUInt32(record, _denoiseIterationOffset, iteration);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, aoSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + ShaderAbiLayout.TEXTURE_INDEX_SIZE,
            depthSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + (2 * ShaderAbiLayout.TEXTURE_INDEX_SIZE),
            blueNoiseSlot);
        context.RecordFullscreenPass(destination, pipeline, offset, _denoiseStride);
    }

    private void RecordComposite(
        PostProcessContext context,
        VulkanPostTarget destination,
        Pipeline pipeline,
        uint sourceColorSlot,
        uint aoSlot) {
        ulong offset = context.ReserveInstanceRecord(_compositeStride);
        Span<byte> record = context.GetInstanceRecordSpan(offset, _compositeStride);
        WriteHeader(record);
        WriteVector4(record, _compositeColorOffset, _settings.Color);
        WriteSingle(record, _compositeIntensityOffset, _settings.Intensity);
        WriteUInt32(record, ShaderAbiLayout.TEXTURE_INDEX_START, sourceColorSlot);
        WriteUInt32(
            record,
            ShaderAbiLayout.TEXTURE_INDEX_START + ShaderAbiLayout.TEXTURE_INDEX_SIZE,
            aoSlot);
        context.RecordFullscreenPass(destination, pipeline, offset, _compositeStride);
    }

    private static int UniformOffset(ShaderProgram program, string name) {
        if (!program.UniformsByName.TryGetValue(name, out ShaderAbiUniform uniform)) {
            throw new InvalidOperationException(
                $"The built-in SSAO shader '{program.Path}' does not declare the required uniform '{name}'.");
        }

        return uniform.Offset;
    }

    private static void WriteHeader(Span<byte> record) {
        VulkanInstanceRecordWriter.WriteMatrix(record, ShaderAbiLayout.TRANSFORM_OFFSET, Matrix4x4.Identity);
        VulkanInstanceRecordWriter.WriteVector4(record, ShaderAbiLayout.COLOR_OFFSET, Vector4.One);
        VulkanInstanceRecordWriter.WriteVector4(
            record,
            ShaderAbiLayout.UV_RECT_OFFSET,
            new Vector4(0f, 0f, 1f, 1f));
    }

    private static void WriteMatrix(Span<byte> record, int offset, Matrix4x4 value) =>
        VulkanInstanceRecordWriter.WriteMatrix(record, offset, value);

    private static void WriteVector4(Span<byte> record, int offset, Vector4 value) =>
        VulkanInstanceRecordWriter.WriteVector4(record, offset, value);

    private static void WriteSingle(Span<byte> record, int offset, float value) =>
        VulkanInstanceRecordWriter.WriteSingle(record, offset, value);

    private static void WriteUInt32(Span<byte> record, int offset, uint value) =>
        VulkanInstanceRecordWriter.WriteUInt32(record, offset, value);
}
