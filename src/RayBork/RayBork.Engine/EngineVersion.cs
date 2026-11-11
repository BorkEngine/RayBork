namespace RayBork;

/// <summary>
/// Reports the version of the RayBork engine assembly.
/// </summary>
public static class EngineVersion {
    /// <summary>
    /// Gets the version of the loaded RayBork engine assembly.
    /// </summary>
    public static Version Current { get; } =
        typeof(EngineVersion).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
}
