namespace RayBork;

/// <summary>
/// The depth comparison of a render state.
/// </summary>
/// <remarks>
/// The engine uses reverse-Z depth: a closer fragment has a larger depth value. <see cref="Greater"/>
/// and <see cref="GreaterOrEqual"/> mean "closer wins".
/// </remarks>
public enum DepthTest {
    /// <summary>No depth test.</summary>
    Disabled = 0,

    /// <summary>Pass when the fragment is strictly closer.</summary>
    Greater = 1,

    /// <summary>Pass when the fragment is closer or equal.</summary>
    GreaterOrEqual = 2,

    /// <summary>Pass when the fragment is strictly farther. An escape hatch.</summary>
    Less = 3,

    /// <summary>Pass when the fragment is farther or equal. An escape hatch.</summary>
    LessOrEqual = 4,

    /// <summary>Always pass.</summary>
    Always = 5,
}
