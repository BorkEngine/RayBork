using System.Reflection;

namespace RayBork.Bake.Incremental;

/// <summary>Resolves the bake tool version recorded in the incremental manifest.</summary>
internal static class BakeToolInfo {
    /// <summary>Gets the assembly informational version, then the assembly version, then <c>0.0.0</c>.</summary>
    internal static string Version { get; } = ResolveVersion();

    private static string ResolveVersion() {
        Assembly assembly = typeof(BakeToolInfo).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational)) {
            return informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
