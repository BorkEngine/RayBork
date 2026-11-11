using System.Globalization;

namespace RayBork.Internal;

/// <summary>
/// Converts between packed Vulkan API versions and readable text.
/// </summary>
/// <remarks>
/// A Vulkan version packs a major, minor, and patch number into one <see cref="uint"/>:
/// <c>(major &lt;&lt; 22) | (minor &lt;&lt; 12) | patch</c>. The engine uses the packed value in
/// <see cref="EngineInfo.VulkanApiVersion"/> and formats it only for logs and diagnostics.
/// </remarks>
internal static class VulkanVersion {
    private const int MAJOR_SHIFT = 22;
    private const int MINOR_SHIFT = 12;
    private const uint MAJOR_MASK = 0x7Fu;
    private const uint MINOR_MASK = 0x3FFu;
    private const uint PATCH_MASK = 0xFFFu;

    /// <summary>
    /// Packs a major, minor, and patch version into the Vulkan <c>uint</c> encoding.
    /// </summary>
    /// <param name="major">The major version; the encoded field has 7 bits.</param>
    /// <param name="minor">The minor version; the encoded field has 10 bits.</param>
    /// <param name="patch">The patch version; the encoded field has 12 bits.</param>
    /// <returns>The packed Vulkan version.</returns>
    internal static uint Pack(int major, int minor, int patch) =>
        ((uint)major << MAJOR_SHIFT) | ((uint)minor << MINOR_SHIFT) | (uint)patch;

    /// <summary>
    /// Unpacks a Vulkan version into its major, minor, and patch fields.
    /// </summary>
    /// <param name="version">The packed Vulkan version.</param>
    /// <returns>The unpacked version components.</returns>
    internal static (int Major, int Minor, int Patch) Unpack(uint version) => (
        (int)((version >> MAJOR_SHIFT) & MAJOR_MASK),
        (int)((version >> MINOR_SHIFT) & MINOR_MASK),
        (int)(version & PATCH_MASK));

    /// <summary>
    /// Formats a packed Vulkan version as <c>major.minor.patch</c>.
    /// </summary>
    /// <param name="version">The packed Vulkan version.</param>
    /// <returns>The formatted version, for example <c>1.2.0</c>.</returns>
    internal static string Format(uint version) {
        (int major, int minor, int patch) = Unpack(version);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{major}.{minor}.{patch}");
    }

    /// <summary>
    /// Formats a packed Vulkan version as a backend renderer name with only major and minor.
    /// </summary>
    /// <param name="version">The packed Vulkan version.</param>
    /// <returns>The formatted name, for example <c>Vulkan 1.2</c>.</returns>
    internal static string FormatRendererName(uint version) {
        (int major, int minor, _) = Unpack(version);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Vulkan {major}.{minor}");
    }
}
