using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A built-in screen-space ambient-occlusion effect derived from N8AO 2.1.0's full-resolution SSAO path.
/// </summary>
/// <remarks>
/// <para>
/// The effect reconstructs view-space normals from the camera target's reverse-Z depth, samples a
/// golden-angle hemisphere kernel with blue-noise rotation, runs two bilateral denoise iterations, and
/// composites the resulting AO over its chain input. The input is the original scene color only when
/// SSAO is the first enabled effect; otherwise it is the previous effect's output. It supports
/// perspective and orthographic cameras, world-space or screen-space radius, distance falloff, bias,
/// and an AO tint. The public
/// <see cref="PostProcessEffect.Enabled"/> value and every setting are snapshotted when a camera is
/// created; later changes apply to later cameras only.
/// </para>
/// <para>
/// The seven shader variants and the 128x128 blue-noise texture are engine-owned built-in assets. The
/// app preloads them before <c>IGame.Setup</c>; the effect never compiles a shader or loads an asset on
/// the render thread. The AO and denoise shaders are baked with compile-time sample counts selected by
/// <see cref="Quality"/>. The effect requires a camera target with a depth attachment; a depth-less
/// <see cref="RenderTexture"/> target is rejected when the camera is created.
/// </para>
/// <para>
/// The effect is tick-thread only, like the stack and every other effect. It owns no engine asset and
/// is not disposable.
/// </para>
/// </remarks>
public sealed class SsaoEffect : PostProcessEffect {
    private const float DEFAULT_RADIUS = 1.5f;
    private const float DEFAULT_DISTANCE_FALLOFF = 1f;
    private const float DEFAULT_INTENSITY = 2f;
    private const int DEFAULT_DENOISE_RADIUS = 12;
    private const float DEFAULT_BIAS_MULTIPLIER = 1f;

    private readonly SsaoAssets? _assets;
    private SsaoQuality _quality = SsaoQuality.Medium;
    private float _radius = DEFAULT_RADIUS;
    private float _distanceFalloff = DEFAULT_DISTANCE_FALLOFF;
    private float _intensity = DEFAULT_INTENSITY;
    private Color _color = Color.Black;
    private int _denoiseRadius = DEFAULT_DENOISE_RADIUS;
    private bool _screenSpaceRadius;
    private float _biasOffset;
    private float _biasMultiplier = DEFAULT_BIAS_MULTIPLIER;

    /// <summary>Initializes an SSAO effect with the approved defaults.</summary>
    public SsaoEffect() {
    }

    /// <summary>
    /// Initializes an effect over an explicit asset set. Used by tests; the app path resolves the
    /// assets from <see cref="CameraService"/> instead.
    /// </summary>
    /// <param name="assets">The loaded engine-owned SSAO assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <see langword="null"/>.</exception>
    internal SsaoEffect(SsaoAssets assets) {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    /// <summary>
    /// Gets or sets the baked quality variant. The default is <see cref="SsaoQuality.Medium"/>.
    /// </summary>
    /// <remarks>The setter is tick-thread only.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined quality.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public SsaoQuality Quality {
        get => _quality;
        set {
            TickThread.RequireCurrent(nameof(Quality));
            if (!Enum.IsDefined(value)) {
                throw new ArgumentOutOfRangeException(
                    nameof(Quality),
                    value,
                    "The SSAO quality must be a defined SsaoQuality value.");
            }

            _quality = value;
        }
    }

    /// <summary>
    /// Gets or sets the AO radius. The default is <c>1.5</c>.
    /// </summary>
    /// <remarks>
    /// The value is world units when <see cref="ScreenSpaceRadius"/> is <see langword="false"/>, or
    /// pixels when it is <see langword="true"/>. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is not positive.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float Radius {
        get => _radius;
        set {
            TickThread.RequireCurrent(nameof(Radius));
            if (!float.IsFinite(value) || value <= 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(Radius),
                    value,
                    "The SSAO radius must be finite and greater than zero.");
            }

            _radius = value;
        }
    }

    /// <summary>
    /// Gets or sets how quickly AO fades with distance relative to the radius. The default is <c>1</c>.
    /// </summary>
    /// <remarks>The setter is tick-thread only.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is not positive.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float DistanceFalloff {
        get => _distanceFalloff;
        set {
            TickThread.RequireCurrent(nameof(DistanceFalloff));
            if (!float.IsFinite(value) || value <= 0f) {
                throw new ArgumentOutOfRangeException(
                    nameof(DistanceFalloff),
                    value,
                    "The SSAO distance falloff must be finite and greater than zero.");
            }

            _distanceFalloff = value;
        }
    }

    /// <summary>
    /// Gets or sets the composite intensity. The default is <c>2</c>.
    /// </summary>
    /// <remarks>
    /// The composite raises the AO term to this power, so <c>0</c> leaves the scene unchanged and
    /// larger values darken occluded pixels more strongly. The setter is tick-thread only.
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
                    "The SSAO intensity must be finite and non-negative.");
            }

            _intensity = value;
        }
    }

    /// <summary>
    /// Gets or sets the AO color. The default is <see cref="Color.Black"/>.
    /// </summary>
    /// <remarks>
    /// The color multiplies the scene color in occluded pixels, so black produces normal ambient
    /// occlusion and a dark tint approximates colored bounce light. Every component must be finite.
    /// The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentException">A color component is not finite.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public Color Color {
        get => _color;
        set {
            TickThread.RequireCurrent(nameof(Color));
            if (!float.IsFinite(value.R)
                || !float.IsFinite(value.G)
                || !float.IsFinite(value.B)
                || !float.IsFinite(value.A)) {
                throw new ArgumentException(
                    "The SSAO color must have finite components.",
                    nameof(Color));
            }

            _color = value;
        }
    }

    /// <summary>
    /// Gets or sets the denoise radius in screen pixels. The default is <c>12</c>.
    /// </summary>
    /// <remarks>
    /// The radius is independent of <see cref="Quality"/>; both denoise iterations use this value.
    /// <c>0</c> keeps each denoise pass at the center pixel. The setter is tick-thread only.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public int DenoiseRadius {
        get => _denoiseRadius;
        set {
            TickThread.RequireCurrent(nameof(DenoiseRadius));
            if (value < 0) {
                throw new ArgumentOutOfRangeException(
                    nameof(DenoiseRadius),
                    value,
                    "The SSAO denoise radius must be zero or positive.");
            }

            _denoiseRadius = value;
        }
    }

    /// <summary>
    /// Gets or sets whether <see cref="Radius"/> is measured in screen pixels instead of world units.
    /// The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// In screen-space mode, a world radius is reconstructed at <see cref="Radius"/> pixels from the
    /// center depth, and <see cref="DistanceFalloff"/> is a ratio of that radius. The setter is
    /// tick-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public bool ScreenSpaceRadius {
        get => _screenSpaceRadius;
        set {
            TickThread.RequireCurrent(nameof(ScreenSpaceRadius));
            _screenSpaceRadius = value;
        }
    }

    /// <summary>
    /// Gets or sets the constant bias added to the depth comparison. The default is <c>0</c>.
    /// </summary>
    /// <remarks>The setter is tick-thread only.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float BiasOffset {
        get => _biasOffset;
        set {
            TickThread.RequireCurrent(nameof(BiasOffset));
            if (!float.IsFinite(value)) {
                throw new ArgumentOutOfRangeException(
                    nameof(BiasOffset),
                    value,
                    "The SSAO bias offset must be finite.");
            }

            _biasOffset = value;
        }
    }

    /// <summary>
    /// Gets or sets the multiplier applied to the derivative-based bias heuristic. The default is
    /// <c>1</c>.
    /// </summary>
    /// <remarks>The setter is tick-thread only.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public float BiasMultiplier {
        get => _biasMultiplier;
        set {
            TickThread.RequireCurrent(nameof(BiasMultiplier));
            if (!float.IsFinite(value)) {
                throw new ArgumentOutOfRangeException(
                    nameof(BiasMultiplier),
                    value,
                    "The SSAO bias multiplier must be finite.");
            }

            _biasMultiplier = value;
        }
    }

    /// <inheritdoc/>
    internal override PostProcessEffectSnapshot Capture(PostProcessCaptureContext context) {
        if (!context.TargetHasDepth) {
            throw new InvalidOperationException(
                $"The SSAO effect requires a camera target with a depth attachment, but "
                    + $"{context.TargetDescription} has no depth. Create the target with Depth: true; "
                    + "the app scene target always has depth.");
        }

        SsaoAssets assets = _assets ?? context.SsaoAssets ?? throw new InvalidOperationException(
            "The SSAO effect requires the engine's built-in SSAO assets, but they are not attached "
                + "to the camera service. The app preloads them before IGame.Setup; a test harness must "
                + "attach the loaded SsaoAssets to CameraService.");
        ObjectDisposedException.ThrowIf(assets.IsDisposed, assets);

        var settings = new SsaoSettings(
            _quality,
            _radius,
            _distanceFalloff,
            _intensity,
            _color,
            _denoiseRadius,
            _screenSpaceRadius,
            _biasOffset,
            _biasMultiplier);
        return SsaoEffectSnapshot.Rent(this, settings, assets);
    }
}

/// <summary>
/// The immutable SSAO settings frozen into one camera's effect snapshot.
/// </summary>
/// <param name="Quality">The baked quality variant.</param>
/// <param name="Radius">The AO radius in world units or pixels.</param>
/// <param name="DistanceFalloff">The distance falloff ratio.</param>
/// <param name="Intensity">The composite intensity.</param>
/// <param name="Color">The AO tint.</param>
/// <param name="DenoiseRadius">The denoise radius in pixels.</param>
/// <param name="ScreenSpaceRadius">Whether the radius is in screen pixels.</param>
/// <param name="BiasOffset">The constant depth bias.</param>
/// <param name="BiasMultiplier">The derivative-bias multiplier.</param>
internal readonly record struct SsaoSettings(
    SsaoQuality Quality,
    float Radius,
    float DistanceFalloff,
    float Intensity,
    Color Color,
    int DenoiseRadius,
    bool ScreenSpaceRadius,
    float BiasOffset,
    float BiasMultiplier);
