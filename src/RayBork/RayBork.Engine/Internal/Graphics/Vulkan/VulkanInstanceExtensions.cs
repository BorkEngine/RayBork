namespace RayBork.Internal;

/// <summary>
/// Validates the Vulkan instance extensions a platform requires before the instance is created.
/// </summary>
/// <remarks>
/// The comparisons are ordinal because Vulkan extension names are case-sensitive ASCII tokens.
/// </remarks>
internal static class VulkanInstanceExtensions {
    /// <summary>
    /// Returns the required extension names that the loader does not advertise.
    /// </summary>
    /// <remarks>
    /// The result preserves the order of <paramref name="required"/> and contains each missing name
    /// once. Empty names are ignored.
    /// </remarks>
    /// <param name="available">The extension names reported by the Vulkan loader.</param>
    /// <param name="required">The extension names the platform requires.</param>
    /// <returns>The missing extension names, or an empty list when every requirement is met.</returns>
    internal static IReadOnlyList<string> Validate(
        IReadOnlyList<string> available,
        IReadOnlyList<string> required) {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(required);

        HashSet<string> availableNames = new(available, StringComparer.Ordinal);
        HashSet<string> seenRequired = new(StringComparer.Ordinal);
        List<string> missing = [];
        foreach (string extension in required) {
            if (string.IsNullOrEmpty(extension) || !seenRequired.Add(extension)) {
                continue;
            }

            if (!availableNames.Contains(extension)) {
                missing.Add(extension);
            }
        }

        return missing;
    }

    /// <summary>
    /// Throws when the loader does not advertise one of the required extension names.
    /// </summary>
    /// <param name="available">The extension names reported by the Vulkan loader.</param>
    /// <param name="required">The extension names the platform requires.</param>
    /// <exception cref="GraphicsException">
    /// One or more required extensions are missing. The message names the missing extensions and
    /// the platform extension list; the diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static void Require(
        IReadOnlyList<string> available,
        IReadOnlyList<string> required) {
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(required);

        IReadOnlyList<string> missing = Validate(available, required);
        if (missing.Count == 0) {
            return;
        }

        string missingList = string.Join(", ", missing);
        string platformList = string.Join(", ", required);
        throw new GraphicsException(
            $"The Vulkan instance is missing required platform extensions: {missingList}. The platform requires: {platformList}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }
}
