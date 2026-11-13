using RayBork.Internal;

namespace RayBork;

/// <summary>
/// The app-lifetime runtime tone-mapping settings: exposure, AgX white, and AgX contrast.
/// </summary>
/// <remarks>
/// <para>
/// The settings start from the <see cref="RayBorkAppBuilder"/> values and can be changed while the app
/// runs through <see cref="EngineContext.Tonemapping"/>. The present pass is frame-global, so every
/// camera pass in a frame shares one tone-mapping configuration; there is no per-camera or per-draw
/// setting.
/// </para>
/// <para>
/// Every member is tick-thread only. The tick thread copies the current values into the frame packet
/// when it seals the frame, so a write during <see cref="IGame.Tick"/> applies to the frame that tick
/// records. A frame that was already published keeps its own snapshot. The instance lives for the app
/// lifetime; it is not disposable and not thread-safe.
/// </para>
/// </remarks>
public sealed class TonemappingSettings {
    private float _exposure;
    private float _agxWhite;
    private float _agxContrast;

    /// <summary>
    /// Initializes the settings from the builder's startup defaults. Called by the app on the main
    /// thread before the tick thread starts.
    /// </summary>
    /// <param name="exposure">The exposure in stops. It must be finite.</param>
    /// <param name="agxWhite">The AgX white in scene-linear units. It must be finite and at least <c>2</c>.</param>
    /// <param name="agxContrast">The AgX curve contrast. It must be finite and at least <c>1</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its documented range.</exception>
    internal TonemappingSettings(float exposure, float agxWhite, float agxContrast) {
        ValidateExposure(exposure);
        ValidateAgxWhite(agxWhite);
        ValidateAgxContrast(agxContrast);

        _exposure = exposure;
        _agxWhite = agxWhite;
        _agxContrast = agxContrast;
    }

    /// <summary>
    /// Gets or sets the exposure in stops applied before AgX. <c>0</c> is neutral and <c>+1</c>
    /// doubles linear exposure; the renderer applies <c>2^Exposure</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    /// <remarks>
    /// Read and write it on the tick thread. A write during <see cref="IGame.Tick"/> applies to the
    /// frame that tick records.
    /// </remarks>
    public float Exposure {
        get => _exposure;
        set {
            ValidateExposure(value);
            TickThread.RequireCurrent(nameof(Exposure));
            _exposure = value;
        }
    }

    /// <summary>
    /// Gets or sets the scene-linear white that AgX maps to the display maximum. The default is
    /// <c>16.29</c>; on an HDR window the value is scaled by the reported display headroom before the
    /// curve is evaluated.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is less than <c>2</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    /// <remarks>
    /// Read and write it on the tick thread. A write during <see cref="IGame.Tick"/> applies to the
    /// frame that tick records.
    /// </remarks>
    public float AgxWhite {
        get => _agxWhite;
        set {
            ValidateAgxWhite(value);
            TickThread.RequireCurrent(nameof(AgxWhite));
            _agxWhite = value;
        }
    }

    /// <summary>
    /// Gets or sets the AgX curve contrast. The default is <c>1.25</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is less than <c>1</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    /// <remarks>
    /// Read and write it on the tick thread. A write during <see cref="IGame.Tick"/> applies to the
    /// frame that tick records.
    /// </remarks>
    public float AgxContrast {
        get => _agxContrast;
        set {
            ValidateAgxContrast(value);
            TickThread.RequireCurrent(nameof(AgxContrast));
            _agxContrast = value;
        }
    }

    /// <summary>
    /// Copies the values in effect now into a frame snapshot. Tick-thread only.
    /// </summary>
    /// <returns>The value snapshot the frame packet carries to the render thread.</returns>
    internal TonemappingSnapshot Snapshot() => new(_exposure, _agxWhite, _agxContrast);

    private static void ValidateExposure(float exposure) {
        if (!float.IsFinite(exposure)) {
            throw new ArgumentOutOfRangeException(
                nameof(Exposure),
                exposure,
                "The exposure must be finite.");
        }
    }

    private static void ValidateAgxWhite(float agxWhite) {
        if (!float.IsFinite(agxWhite) || agxWhite < AgxCurveParameters.MIN_WHITE) {
            throw new ArgumentOutOfRangeException(
                nameof(AgxWhite),
                agxWhite,
                "The AgX white must be finite and at least 2.");
        }
    }

    private static void ValidateAgxContrast(float agxContrast) {
        if (!float.IsFinite(agxContrast) || agxContrast < 1f) {
            throw new ArgumentOutOfRangeException(
                nameof(AgxContrast),
                agxContrast,
                "The AgX contrast must be finite and at least 1.");
        }
    }
}
