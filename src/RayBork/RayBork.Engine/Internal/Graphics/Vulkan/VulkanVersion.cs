using System.Globalization;

namespace RayBork.Internal;

/// <summary>Converts between packed Vulkan API versions and readable text.</summary>
/// <remarks>
/// A version packs 7 major, 10 minor, and 12 patch bits into one <see cref="uint"/>; it is
/// formatted only for logs and diagnostics.
/// </remarks>
internal static class VulkanVersion
{
    private const int MAJOR_SHIFT = 22;
    private const int MINOR_SHIFT = 12;
    private const uint MAJOR_MASK = 0x7Fu;
    private const uint MINOR_MASK = 0x3FFu;
    private const uint PATCH_MASK = 0xFFFu;

    /// <summary>Packs major, minor, and patch into a Vulkan version.</summary>
    internal static uint Pack(int major, int minor, int patch) =>
        ((uint)major << MAJOR_SHIFT) | ((uint)minor << MINOR_SHIFT) | (uint)patch;

    /// <summary>Unpacks a Vulkan version into major, minor, and patch.</summary>
    internal static (int Major, int Minor, int Patch) Unpack(uint version) =>
        (
            (int)((version >> MAJOR_SHIFT) & MAJOR_MASK),
            (int)((version >> MINOR_SHIFT) & MINOR_MASK),
            (int)(version & PATCH_MASK)
        );

    /// <summary>Formats a version as <c>major.minor.patch</c>, such as <c>1.2.0</c>.</summary>
    internal static string Format(uint version)
    {
        (int major, int minor, int patch) = Unpack(version);
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}.{patch}");
    }

    /// <summary>Formats a version as <c>Vulkan major.minor</c>, such as <c>Vulkan 1.2</c>.</summary>
    internal static string FormatRendererName(uint version)
    {
        (int major, int minor, _) = Unpack(version);
        return string.Create(CultureInfo.InvariantCulture, $"Vulkan {major}.{minor}");
    }
}
