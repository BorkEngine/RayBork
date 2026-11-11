namespace RayBork;

/// <summary>
/// The face-culling mode of a render state.
/// </summary>
public enum CullMode {
    /// <summary>Draw both faces.</summary>
    None = 0,

    /// <summary>Cull counter-clockwise front faces.</summary>
    Front = 1,

    /// <summary>Cull clockwise back faces.</summary>
    Back = 2,
}
