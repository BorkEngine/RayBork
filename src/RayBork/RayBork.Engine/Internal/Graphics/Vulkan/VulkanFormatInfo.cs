using System.Globalization;
using System.Reflection;
using Silk.NET.Core.Attributes;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Maps Silk.NET Vulkan formats to the stable names the public API reports.
/// </summary>
/// <remarks>
/// <see cref="EngineInfo.SwapchainFormat"/> is a Vulkan format token without the <c>VK_FORMAT_</c>
/// prefix, for example <c>B8G8R8A8_SRGB</c>. The Silk.NET enum member names use a different casing,
/// so the mapping reads the native-name metadata that Silk.NET emits for every format.
/// </remarks>
internal static class VulkanFormatInfo {
    private const string VK_FORMAT_PREFIX = "VK_FORMAT_";
    private const string SRGB_TOKEN = "_SRGB";

    /// <summary>
    /// Gets the stable public name of a Vulkan format.
    /// </summary>
    /// <param name="format">The Vulkan format.</param>
    /// <returns>
    /// The format token without the <c>VK_FORMAT_</c> prefix, or <c>VK_FORMAT_</c> followed by the
    /// numeric value when the format is not defined by Vulkan.
    /// </returns>
    internal static string Describe(Format format) {
        string? memberName = Enum.GetName(format);
        if (memberName is null) {
            return VK_FORMAT_PREFIX + ((int)format).ToString(CultureInfo.InvariantCulture);
        }

        FieldInfo? field = typeof(Format).GetField(memberName, BindingFlags.Public | BindingFlags.Static);
        NativeNameAttribute? nativeName = field?.GetCustomAttribute<NativeNameAttribute>();
        string? name = nativeName?.Name;
        if (string.IsNullOrEmpty(name)) {
            return VK_FORMAT_PREFIX + ((int)format).ToString(CultureInfo.InvariantCulture);
        }

        return name.StartsWith(VK_FORMAT_PREFIX, StringComparison.Ordinal)
            ? name[VK_FORMAT_PREFIX.Length..]
            : name;
    }

    /// <summary>
    /// Determines whether a format is HDR-capable for M2 presentation.
    /// </summary>
    /// <remarks>
    /// M2 presents HDR through scRGB and keeps HDR10 deferred, so only
    /// <c>R16G16B16A16_SFLOAT</c> and <c>A2B10G10R10_UNORM_PACK32</c> are reported as HDR.
    /// </remarks>
    /// <param name="format">The Vulkan format.</param>
    /// <returns><see langword="true"/> when the format is an HDR-capable surface format.</returns>
    internal static bool IsHdr(Format format) =>
        format is Format.R16G16B16A16Sfloat or Format.A2B10G10R10UnormPack32;

    /// <summary>
    /// Determines whether a format transfers through the sRGB transfer function.
    /// </summary>
    /// <param name="format">The Vulkan format.</param>
    /// <returns><see langword="true"/> when the format name contains the sRGB token.</returns>
    internal static bool IsSrgb(Format format) =>
        Describe(format).Contains(SRGB_TOKEN, StringComparison.Ordinal);
}
