namespace RayBork;

/// <summary>The blend preset of a render state.</summary>
public enum BlendMode
{
    /// <summary>Writes the source color; the source alpha is ignored.</summary>
    Opaque = 0,

    /// <summary>Source-over alpha blending.</summary>
    Alpha = 1,

    /// <summary>Additive blending.</summary>
    Additive = 2,

    /// <summary>Multiplicative blending.</summary>
    Multiply = 3,
}
