using System.Globalization;
using System.Reflection;
using Silk.NET.Core.Attributes;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>Maps Silk.NET Vulkan formats to the stable names the public API reports.</summary>
/// <remarks>
/// <see cref="EngineInfo.SwapchainFormat"/> is a token without the <c>VK_FORMAT_</c> prefix,
/// for example <c>B8G8R8A8_SRGB</c>; the mapping reads Silk.NET's native-name metadata.
/// </remarks>
internal static class VulkanFormatInfo
{
    private const string VK_FORMAT_PREFIX = "VK_FORMAT_";
    private const string SRGB_TOKEN = "_SRGB";

    /// <summary>Returns the public token without the <c>VK_FORMAT_</c> prefix.</summary>
    /// <returns>
    /// The token, or <c>VK_FORMAT_</c> plus the numeric value when the format is undefined.
    /// </returns>
    internal static string Describe(Format format)
    {
        string? memberName = Enum.GetName(format);
        if (memberName is null)
        {
            return VK_FORMAT_PREFIX + ((int)format).ToString(CultureInfo.InvariantCulture);
        }

        FieldInfo? field = typeof(Format).GetField(
            memberName,
            BindingFlags.Public | BindingFlags.Static
        );
        NativeNameAttribute? nativeName = field?.GetCustomAttribute<NativeNameAttribute>();
        string? name = nativeName?.Name;
        if (string.IsNullOrEmpty(name))
        {
            return VK_FORMAT_PREFIX + ((int)format).ToString(CultureInfo.InvariantCulture);
        }

        return name.StartsWith(VK_FORMAT_PREFIX, StringComparison.Ordinal)
            ? name[VK_FORMAT_PREFIX.Length..]
            : name;
    }

    /// <summary>Returns whether a format is HDR-capable for M2 presentation.</summary>
    /// <remarks>
    /// Only <c>R16G16B16A16_SFLOAT</c> and <c>A2B10G10R10_UNORM_PACK32</c> are HDR formats.
    /// </remarks>
    internal static bool IsHdr(Format format) =>
        format is Format.R16G16B16A16Sfloat or Format.A2B10G10R10UnormPack32;

    /// <summary>Returns whether a format name contains the sRGB token.</summary>
    internal static bool IsSrgb(Format format) =>
        Describe(format).Contains(SRGB_TOKEN, StringComparison.Ordinal);
}
