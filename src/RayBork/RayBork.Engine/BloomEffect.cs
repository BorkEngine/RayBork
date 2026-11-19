using RayBork.Internal;

namespace RayBork;

/// <summary>A built-in fast dual-filter bloom effect over the scene-linear HDR color.</summary>
/// <remarks>
/// Thresholds the input into a half-resolution pyramid and adds the tent-upsampled result back.
/// Needs no depth attachment; tick-thread only, and settings snapshot per camera so later changes
/// apply to later cameras only.
/// </remarks>
public sealed class BloomEffect : PostProcessEffect
{
    private const float DEFAULT_THRESHOLD = 1f;
    private const float DEFAULT_SOFT_KNEE = 0.5f;
    private const float DEFAULT_INTENSITY = 0.7f;
    private const float DEFAULT_SCATTER = 0.7f;

    private readonly BloomAssets? _assets;
    private float _threshold = DEFAULT_THRESHOLD;
    private float _softKnee = DEFAULT_SOFT_KNEE;
    private float _intensity = DEFAULT_INTENSITY;
    private float _scatter = DEFAULT_SCATTER;
    private Color _tint = Color.White;

    /// <summary>Initializes a bloom effect with the default settings.</summary>
    public BloomEffect() { }

    /// <summary>Initializes an effect over an explicit asset set; tests only.</summary>
    internal BloomEffect(BloomAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    /// <summary>Gets or sets the scene-linear brightness where bloom starts.</summary>
    /// <remarks>Default <c>1</c>; the knee is <c>Threshold * SoftKnee</c>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    public float Threshold
    {
        get => _threshold;
        set
        {
            TickThread.RequireCurrent(nameof(Threshold));
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Threshold),
                    value,
                    "The bloom threshold must be finite and non-negative."
                );
            }

            _threshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the soft-knee width as a fraction of <see cref="Threshold"/>; default <c>0.5</c>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> is a hard threshold; the effective knee is capped at <see cref="Threshold"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    public float SoftKnee
    {
        get => _softKnee;
        set
        {
            TickThread.RequireCurrent(nameof(SoftKnee));
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(SoftKnee),
                    value,
                    "The bloom soft knee must be finite and non-negative."
                );
            }

            _softKnee = value;
        }
    }

    /// <summary>Gets or sets how much bloom is added to the input; default <c>0.7</c>.</summary>
    /// <remarks><c>0</c> leaves the scene unchanged.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    public float Intensity
    {
        get => _intensity;
        set
        {
            TickThread.RequireCurrent(nameof(Intensity));
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Intensity),
                    value,
                    "The bloom intensity must be finite and non-negative."
                );
            }

            _intensity = value;
        }
    }

    /// <summary>
    /// Gets or sets the weight of each coarser pyramid level in the tent upsample; default <c>0.7</c>.
    /// </summary>
    /// <remarks><c>0</c> keeps only the half-resolution level, so the halo is tight.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    public float Scatter
    {
        get => _scatter;
        set
        {
            TickThread.RequireCurrent(nameof(Scatter));
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Scatter),
                    value,
                    "The bloom scatter must be finite and non-negative."
                );
            }

            _scatter = value;
        }
    }

    /// <summary>Gets or sets the bloom tint; default <see cref="Color.White"/>.</summary>
    /// <remarks>Applied to the bloom before it is added to the chain input.</remarks>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    public Color Tint
    {
        get => _tint;
        set
        {
            TickThread.RequireCurrent(nameof(Tint));
            if (
                !float.IsFinite(value.R)
                || !float.IsFinite(value.G)
                || !float.IsFinite(value.B)
                || !float.IsFinite(value.A)
            )
            {
                throw new ArgumentException(
                    "The bloom tint must have finite components.",
                    nameof(Tint)
                );
            }

            _tint = value;
        }
    }

    /// <inheritdoc/>
    internal override PostProcessEffectSnapshot Capture(PostProcessCaptureContext context)
    {
        BloomAssets assets = _assets
            ?? context.BloomAssets
            ?? throw new InvalidOperationException(
                "The bloom effect requires the engine's built-in bloom assets, but they are not attached "
                    + "to the camera service. The app preloads them before IGame.Setup; a test harness must "
                    + "attach the loaded BloomAssets to CameraService."
            );
        ObjectDisposedException.ThrowIf(assets.IsDisposed, assets);

        var settings = new BloomSettings(_threshold, _softKnee, _intensity, _scatter, _tint);
        return BloomEffectSnapshot.Rent(this, settings, assets);
    }
}

/// <summary>The immutable bloom settings frozen into one camera's effect snapshot.</summary>
internal readonly record struct BloomSettings(
    float Threshold,
    float SoftKnee,
    float Intensity,
    float Scatter,
    Color Tint
);
