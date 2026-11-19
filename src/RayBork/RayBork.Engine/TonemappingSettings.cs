using RayBork.Internal;

namespace RayBork;

/// <summary>App-lifetime tone-mapping settings: exposure, AgX white, and AgX contrast.</summary>
/// <remarks>
/// Every member is tick-thread only; the tick thread copies the values into the frame packet when
/// it seals the frame, so a write during <see cref="IGame.Tick"/> applies to the frame that tick
/// records. The present pass is frame-global, so every camera in a frame shares one configuration.
/// </remarks>
public sealed class TonemappingSettings
{
    private float _exposure;
    private float _agxWhite;
    private float _agxContrast;

    /// <summary>Initializes the settings from the builder's startup defaults.</summary>
    /// <param name="exposure">Exposure in stops; must be finite.</param>
    /// <param name="agxWhite">AgX white in scene-linear units; finite and at least <c>2</c>.</param>
    /// <param name="agxContrast">AgX curve contrast; finite and at least <c>1</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    internal TonemappingSettings(float exposure, float agxWhite, float agxContrast)
    {
        ValidateExposure(exposure, nameof(Exposure));
        ValidateAgxWhite(agxWhite, nameof(AgxWhite));
        ValidateAgxContrast(agxContrast, nameof(AgxContrast));

        _exposure = exposure;
        _agxWhite = agxWhite;
        _agxContrast = agxContrast;
    }

    /// <summary>Gets or sets the pre-AgX exposure in stops; <c>0</c> is neutral.</summary>
    /// <remarks>
    /// The renderer applies <c>2^Exposure</c>, so <c>+1</c> doubles linear exposure.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public float Exposure
    {
        get => _exposure;
        set
        {
            ValidateExposure(value, nameof(Exposure));
            TickThread.RequireCurrent(nameof(Exposure));
            _exposure = value;
        }
    }

    /// <summary>
    /// Gets or sets the scene-linear white AgX maps to the display maximum; default <c>16.29</c>.
    /// </summary>
    /// <remarks>On an HDR window the value is scaled by the display headroom.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is less than <c>2</c>.
    /// </exception>
    public float AgxWhite
    {
        get => _agxWhite;
        set
        {
            ValidateAgxWhite(value, nameof(AgxWhite));
            TickThread.RequireCurrent(nameof(AgxWhite));
            _agxWhite = value;
        }
    }

    /// <summary>Gets or sets the AgX curve contrast; default <c>1.25</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not finite or is less than <c>1</c>.
    /// </exception>
    public float AgxContrast
    {
        get => _agxContrast;
        set
        {
            ValidateAgxContrast(value, nameof(AgxContrast));
            TickThread.RequireCurrent(nameof(AgxContrast));
            _agxContrast = value;
        }
    }

    /// <summary>Copies the current values into a frame snapshot; tick-thread only.</summary>
    internal TonemappingSnapshot Snapshot() => new(_exposure, _agxWhite, _agxContrast);

    private static void ValidateExposure(float exposure, string paramName)
    {
        if (!float.IsFinite(exposure))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                exposure,
                "The exposure must be finite."
            );
        }
    }

    private static void ValidateAgxWhite(float agxWhite, string paramName)
    {
        if (!float.IsFinite(agxWhite) || agxWhite < AgxCurveParameters.MIN_WHITE)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                agxWhite,
                "The AgX white must be finite and at least 2."
            );
        }
    }

    private static void ValidateAgxContrast(float agxContrast, string paramName)
    {
        if (!float.IsFinite(agxContrast) || agxContrast < 1f)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                agxContrast,
                "The AgX contrast must be finite and at least 1."
            );
        }
    }
}
