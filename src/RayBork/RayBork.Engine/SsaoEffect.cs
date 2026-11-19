using RayBork.Internal;

namespace RayBork;

/// <summary>A built-in SSAO effect that requires a depth-attached camera target.</summary>
/// <remarks>
/// Composites over the chain input: the scene color when first, else the previous effect's output.
/// Tick-thread only; settings snapshot per camera, so later changes apply to later cameras only.
/// </remarks>
public sealed class SsaoEffect : PostProcessEffect
{
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

    /// <summary>Initializes an SSAO effect with the default settings.</summary>
    public SsaoEffect() { }

    /// <summary>Initializes an effect over an explicit asset set; tests only.</summary>
    internal SsaoEffect(SsaoAssets assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    /// <summary>
    /// Gets or sets the baked quality variant; default <see cref="SsaoQuality.Medium"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined quality.</exception>
    public SsaoQuality Quality
    {
        get => _quality;
        set
        {
            TickThread.RequireCurrent(nameof(Quality));
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Quality),
                    value,
                    "The SSAO quality must be a defined SsaoQuality value."
                );
            }

            _quality = value;
        }
    }

    /// <summary>Gets or sets the AO radius in world units or screen pixels; default <c>1.5</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is not positive.</exception>
    public float Radius
    {
        get => _radius;
        set
        {
            TickThread.RequireCurrent(nameof(Radius));
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Radius),
                    value,
                    "The SSAO radius must be finite and greater than zero."
                );
            }

            _radius = value;
        }
    }

    /// <summary>Gets or sets the distance falloff relative to the radius; default <c>1</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite or is not positive.</exception>
    public float DistanceFalloff
    {
        get => _distanceFalloff;
        set
        {
            TickThread.RequireCurrent(nameof(DistanceFalloff));
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DistanceFalloff),
                    value,
                    "The SSAO distance falloff must be finite and greater than zero."
                );
            }

            _distanceFalloff = value;
        }
    }

    /// <summary>Gets or sets the composite intensity; default <c>2</c>.</summary>
    /// <remarks><c>0</c> leaves the scene unchanged; larger values darken occlusion more.</remarks>
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
                    "The SSAO intensity must be finite and non-negative."
                );
            }

            _intensity = value;
        }
    }

    /// <summary>Gets or sets the AO tint; default <see cref="Color.Black"/>.</summary>
    /// <remarks>Multiplied into occluded pixels; a black tint is neutral AO.</remarks>
    /// <exception cref="ArgumentException">A color component is not finite.</exception>
    public Color Color
    {
        get => _color;
        set
        {
            TickThread.RequireCurrent(nameof(Color));
            if (
                !float.IsFinite(value.R)
                || !float.IsFinite(value.G)
                || !float.IsFinite(value.B)
                || !float.IsFinite(value.A)
            )
            {
                throw new ArgumentException(
                    "The SSAO color must have finite components.",
                    nameof(Color)
                );
            }

            _color = value;
        }
    }

    /// <summary>Gets or sets the denoise radius in screen pixels; default <c>12</c>.</summary>
    /// <remarks><c>0</c> keeps the center pixel; independent of <see cref="Quality"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int DenoiseRadius
    {
        get => _denoiseRadius;
        set
        {
            TickThread.RequireCurrent(nameof(DenoiseRadius));
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(DenoiseRadius),
                    value,
                    "The SSAO denoise radius must be zero or positive."
                );
            }

            _denoiseRadius = value;
        }
    }

    /// <summary>Gets or sets whether <see cref="Radius"/> is in screen pixels.</summary>
    /// <remarks>Default <c>false</c>; a pixel radius is reconstructed from center depth.</remarks>
    public bool ScreenSpaceRadius
    {
        get => _screenSpaceRadius;
        set
        {
            TickThread.RequireCurrent(nameof(ScreenSpaceRadius));
            _screenSpaceRadius = value;
        }
    }

    /// <summary>Gets or sets the constant depth-comparison bias; default <c>0</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public float BiasOffset
    {
        get => _biasOffset;
        set
        {
            TickThread.RequireCurrent(nameof(BiasOffset));
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(BiasOffset),
                    value,
                    "The SSAO bias offset must be finite."
                );
            }

            _biasOffset = value;
        }
    }

    /// <summary>Gets or sets the derivative-bias multiplier; default <c>1</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public float BiasMultiplier
    {
        get => _biasMultiplier;
        set
        {
            TickThread.RequireCurrent(nameof(BiasMultiplier));
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(BiasMultiplier),
                    value,
                    "The SSAO bias multiplier must be finite."
                );
            }

            _biasMultiplier = value;
        }
    }

    /// <inheritdoc/>
    internal override PostProcessEffectSnapshot Capture(PostProcessCaptureContext context)
    {
        if (!context.TargetHasDepth)
        {
            throw new InvalidOperationException(
                $"The SSAO effect requires a camera target with a depth attachment, but "
                    + $"{context.TargetDescription} has no depth. Create the target with Depth: true; "
                    + "the app scene target always has depth."
            );
        }

        SsaoAssets assets = _assets
            ?? context.SsaoAssets
            ?? throw new InvalidOperationException(
                "The SSAO effect requires the engine's built-in SSAO assets, but they are not attached "
                    + "to the camera service. The app preloads them before IGame.Setup; a test harness must "
                    + "attach the loaded SsaoAssets to CameraService."
            );
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
            _biasMultiplier
        );
        return SsaoEffectSnapshot.Rent(this, settings, assets);
    }
}

/// <summary>The immutable SSAO settings frozen into one camera's effect snapshot.</summary>
internal readonly record struct SsaoSettings(
    SsaoQuality Quality,
    float Radius,
    float DistanceFalloff,
    float Intensity,
    Color Color,
    int DenoiseRadius,
    bool ScreenSpaceRadius,
    float BiasOffset,
    float BiasMultiplier
);
