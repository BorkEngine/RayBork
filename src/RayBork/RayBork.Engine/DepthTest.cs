namespace RayBork;

/// <summary>The reverse-Z depth comparison of a render state.</summary>
public enum DepthTest
{
    /// <summary>No depth test.</summary>
    Disabled = 0,

    /// <summary>Passes when the fragment is strictly closer.</summary>
    Greater = 1,

    /// <summary>Passes when the fragment is closer or equal.</summary>
    GreaterOrEqual = 2,

    /// <summary>Passes when the fragment is strictly farther.</summary>
    Less = 3,

    /// <summary>Passes when the fragment is farther or equal.</summary>
    LessOrEqual = 4,

    /// <summary>Always passes.</summary>
    Always = 5,
}
