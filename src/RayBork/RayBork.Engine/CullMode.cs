namespace RayBork;

/// <summary>The face-culling mode of a render state.</summary>
public enum CullMode
{
    /// <summary>Draws both faces.</summary>
    None = 0,

    /// <summary>Culls counter-clockwise front faces.</summary>
    Front = 1,

    /// <summary>Culls clockwise back faces.</summary>
    Back = 2,
}
