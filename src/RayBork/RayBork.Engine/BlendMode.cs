namespace RayBork;

/// <summary>
/// The blend preset of a render state.
/// </summary>
public enum BlendMode {
    /// <summary>Opaque writes; the source alpha is ignored.</summary>
    Opaque = 0,

    /// <summary>Standard source-over alpha blending.</summary>
    Alpha = 1,

    /// <summary>Additive blending.</summary>
    Additive = 2,

    /// <summary>Multiplicative blending.</summary>
    Multiply = 3,
}
