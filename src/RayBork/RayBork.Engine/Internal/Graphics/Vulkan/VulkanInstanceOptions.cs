using Microsoft.Extensions.Logging;

namespace RayBork.Internal;

/// <summary>Captures the inputs for <see cref="VulkanInstance.Create"/>.</summary>
/// <remarks>
/// The render thread captures this value once at startup. The platform supplies
/// <see cref="SurfaceExtensions"/>; the engine never guesses them.
/// </remarks>
internal sealed class VulkanInstanceOptions
{
    /// <summary>Gets the application name reported to the Vulkan loader.</summary>
    public required string ApplicationName { get; init; }

    /// <summary>Gets the application version reported to the Vulkan loader.</summary>
    public required Version ApplicationVersion { get; init; }

    /// <summary>Gets the instance extensions the platform requires for a native surface.</summary>
    public required IReadOnlyList<string> SurfaceExtensions { get; init; }

    /// <summary>Gets whether validation and the debug-utils messenger are requested.</summary>
    public required bool RequestValidation { get; init; }

    /// <summary>Gets the renderer logger owned by the render thread.</summary>
    public required ILogger Logger { get; init; }
}
