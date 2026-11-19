namespace RayBork.Internal;

/// <summary>Validates the required Vulkan instance extensions before creation.</summary>
/// <remarks>Ordinal comparisons: extension names are case-sensitive ASCII tokens.</remarks>
internal static class VulkanInstanceExtensions
{
    /// <summary>Returns the required extension names the loader does not advertise.</summary>
    /// <param name="available">The extension names reported by the Vulkan loader.</param>
    /// <param name="required">The extension names the platform requires.</param>
    /// <returns>The missing names in required order, each once; empty names are ignored.</returns>
    internal static IReadOnlyList<string> Validate(
        IReadOnlyList<string> available,
        IReadOnlyList<string> required
    )
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(required);

        HashSet<string> availableNames = new(available, StringComparer.Ordinal);
        HashSet<string> seenRequired = new(StringComparer.Ordinal);
        List<string> missing = [];
        foreach (string extension in required)
        {
            if (string.IsNullOrEmpty(extension) || !seenRequired.Add(extension))
            {
                continue;
            }

            if (!availableNames.Contains(extension))
            {
                missing.Add(extension);
            }
        }

        return missing;
    }

    /// <summary>Throws when the loader does not advertise a required extension.</summary>
    /// <exception cref="GraphicsException">
    /// A required extension is missing; the message names it and the code is <c>RB3001</c>.
    /// </exception>
    internal static void Require(IReadOnlyList<string> available, IReadOnlyList<string> required)
    {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(required);

        IReadOnlyList<string> missing = Validate(available, required);
        if (missing.Count == 0)
        {
            return;
        }

        string missingList = string.Join(", ", missing);
        string platformList = string.Join(", ", required);
        throw new GraphicsException(
            $"The Vulkan instance is missing required platform extensions: {missingList}. The platform requires: {platformList}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }
}
