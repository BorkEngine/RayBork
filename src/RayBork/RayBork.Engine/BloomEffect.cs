using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A built-in fast dual-filter bloom effect over the camera's scene-linear HDR color.
/// </summary>
/// <remarks>
/// <para>
/// The effect thresholds and soft-knees the chain input into a half-resolution level, downsamples up to
/// four more times (quarter, eighth, 1/16, and 1/32 of the target, each extent clamped to at least one
/// pixel), then tent-upsamples back to half resolution with <see cref="Scatter"/> as the coarse
/// contribution and adds the result to the input. The composite target is camera-target-sized, so the
/// engine store-back, the next effect in the chain, a later camera's sample, and both readback methods
/// all observe the bloom. The input is the previous effect's output when bloom is not first, and no pass
/// overwrites that input.
/// </para>
/// <para>
/// The four shader programs are engine-owned built-in assets. The app preloads them before
/// <c>IGame.Setup</c>; the effect never compiles a shader or loads an asset on the render thread. The
/// effect owns no asset, is not disposable, and needs no depth attachment, so it also runs on a
/// <see cref="RenderTexture"/> created with <c>Depth: false</c>.
/// </para>
/// <para>
/// The effect is tick-thread only, like the stack and every other effect. The public
/// <see cref="PostProcessEffect.Enabled"/> value and every setting are snapshotted when a camera is
/// created; later changes apply to later cameras only.
/// </para>
/// </remarks>
public sealed class BloomEffect : PostProcessEffect {
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

    /// <summary>Initializes a bloom effect with the approved defaults.</summary>
    public BloomEffect() {
    }

    /// <summary>
    /// Initializes an effect over an explicit asset set. Used by tests; the app path resolves the assets
    /// from <see cref="CameraService"/> instead.
    /// </summary>
    /// <param name="assets">The loaded engine-owned bloom assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <see langword="null"/>.</exception>
    internal BloomEffect(BloomAssets assets) {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    /// <summary>
    /// Gets or sets the scene-linear brightness the bloom starts from. The default is <c>1</c>.
    /// </summary>
    /// <remarks>
    /// The effective knee width is <c>Threshold * SoftKnee</c>, capped at <see cref="Threshold"/>, so
    /// the bloom ramps from <c>Threshold - knee</c> to <c>Threshold + knee</c>. With the default
    /// <c>SoftKnee</c> of <c>0.5</c>, a pixel at the threshold already contributes a small amount.
    /// <c>0</c> keeps every lit pixel and leaves black black. A typical HDR flash uses a threshold above
    /// the ordinary scene brightness so only the flash blooms. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float Threshold {
        get => _threshold;
        set {
            TickThread.RequireCurrent(nameof(Threshold));
            if (!float.IsFinite(value) || value < 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(Threshold),
                    value,
                    "The bloom threshold must be finite and non-negative.");
            }

            _threshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the soft-knee width as a fraction of <see cref="Threshold"/>. The default is
    /// <c>0.5</c>.
    /// </summary>
    /// <remarks>
    /// A positive knee blends the brightness ramp in smoothly instead of cutting it off at the
    /// threshold; <c>0</c> is a hard threshold. The effective knee is capped at <see cref="Threshold"/>,
    /// so a value above <c>1</c> behaves like <c>1</c>: the ramp never extends below zero and
    /// near-black pixels are not bloomed. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float SoftKnee {
        get => _softKnee;
        set {
            TickThread.RequireCurrent(nameof(SoftKnee));
            if (!float.IsFinite(value) || value < 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(SoftKnee),
                    value,
                    "The bloom soft knee must be finite and non-negative.");
            }

            _softKnee = value;
        }
    }

    /// <summary>
    /// Gets or sets how much bloom is added to the chain input. The default is <c>0.7</c>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> leaves the scene unchanged. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float Intensity {
        get => _intensity;
        set {
            TickThread.RequireCurrent(nameof(Intensity));
            if (!float.IsFinite(value) || value < 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(Intensity),
                    value,
                    "The bloom intensity must be finite and non-negative.");
            }

            _intensity = value;
        }
    }

    /// <summary>
    /// Gets or sets the weight of each coarser pyramid level in the tent upsample. The default is
    /// <c>0.7</c>.
    /// </summary>
    /// <remarks>
    /// <c>0</c> keeps only the half-resolution prefilter level, so the halo stays tight; larger values
    /// spread the coarser levels further. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is negative.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float Scatter {
        get => _scatter;
        set {
            TickThread.RequireCurrent(nameof(Scatter));
            if (!float.IsFinite(value) || value < 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(Scatter),
                    value,
                    "The bloom scatter must be finite and non-negative.");
            }

            _scatter = value;
        }
    }

    /// <summary>
    /// Gets or sets the color multiplied into the bloom before it is added. The default is
    /// <see cref="Color.White"/>.
    /// </summary>
    /// <remarks>
    /// Every component must be finite. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentException">A component is not finite.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public Color Tint {
        get => _tint;
        set {
            TickThread.RequireCurrent(nameof(Tint));
            if (!float.IsFinite(value.R)
                || !float.IsFinite(value.G)
                || !float.IsFinite(value.B)
                || !float.IsFinite(value.A)) {
                throw new ArgumentException(
                    "The bloom tint must have finite components.",
                    nameof(Tint));
            }

            _tint = value;
        }
    }

    /// <inheritdoc/>
    internal override PostProcessEffectSnapshot Capture(PostProcessCaptureContext context) {
        BloomAssets assets = _assets ?? context.BloomAssets ?? throw new InvalidOperationException(
            "The bloom effect requires the engine's built-in bloom assets, but they are not attached "
                + "to the camera service. The app preloads them before IGame.Setup; a test harness must "
                + "attach the loaded BloomAssets to CameraService.");
        ObjectDisposedException.ThrowIf(assets.IsDisposed, assets);

        var settings = new BloomSettings(
            _threshold,
            _softKnee,
            _intensity,
            _scatter,
            _tint);
        return BloomEffectSnapshot.Rent(this, settings, assets);
    }
}

/// <summary>
/// The immutable bloom settings frozen into one camera's effect snapshot.
/// </summary>
/// <param name="Threshold">The scene-linear brightness the bloom starts from.</param>
/// <param name="SoftKnee">The soft-knee width as a fraction of the threshold.</param>
/// <param name="Intensity">How much bloom is added to the chain input.</param>
/// <param name="Scatter">The weight of each coarser level in the upsample.</param>
/// <param name="Tint">The color multiplied into the bloom.</param>
internal readonly record struct BloomSettings(
    float Threshold,
    float SoftKnee,
    float Intensity,
    float Scatter,
    Color Tint);
