using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The swapchain format, color space, and present encoding selected for the window surface.
/// </summary>
/// <remarks>
/// The value is pure render-thread state. <see cref="Description"/> is the stable public format
/// token, for example <c>R16G16B16A16_SFLOAT</c>, that the backend projects into
/// <see cref="EngineInfo.SwapchainFormat"/>.
/// </remarks>
/// <param name="Format">The Vulkan surface format selected for the swapchain images.</param>
/// <param name="ColorSpace">The color space the surface advertises for <paramref name="Format"/>.</param>
/// <param name="Encoding">The encoding the present fragment shader applies for this format.</param>
/// <param name="IsHdr">Whether the selected pair presents HDR through scRGB.</param>
/// <param name="Description">The public format token reported by <see cref="EngineInfo.SwapchainFormat"/>.</param>
internal readonly record struct VulkanSwapchainFormat(
    Format Format,
    ColorSpaceKHR ColorSpace,
    PresentEncoding Encoding,
    bool IsHdr,
    string Description);

/// <summary>
/// Chooses the swapchain format and present encoding for the window HDR state.
/// </summary>
/// <remarks>
/// The policy is pure so it can be unit tested without a GPU. HDR presentation uses the scRGB
/// half-float path because HDR10/PQ is deferred; SDR presentation prefers a format whose hardware
/// transfer function matches the selected encoding. <c>A2B10G10R10_UNORM_PACK32</c> is never
/// selected in M2. The caller owns the fallback warnings.
/// </remarks>
internal static class VulkanSwapchainFormatSelection {
    /// <summary>
    /// Selects the swapchain format for the surface format pairs the window advertises.
    /// </summary>
    /// <param name="formats">The format and color-space pairs the surface advertises.</param>
    /// <param name="hdr">The cached window HDR state.</param>
    /// <returns>The selected format, color space, encoding, and HDR flag.</returns>
    /// <exception cref="GraphicsException">
    /// The surface advertises no usable pair. The message lists every available pair and the
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanSwapchainFormat Select(IReadOnlyList<VulkanSurfaceFormat> formats, WindowHdrState hdr) {
        ArgumentNullException.ThrowIfNull(formats);

        IReadOnlyList<VulkanSurfaceFormat> pairs = NormalizeLegacyAnyFormat(formats);
        if (hdr.HdrEnabled) {
            if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt)) {
                return Create(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt, PresentEncoding.ScRgbHdr, true);
            }

            if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
                return Create(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.ScRgbHdr, true);
            }
        }

        if (TryFind(pairs, Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
            return Create(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.LinearClamp, false);
        }

        if (TryFind(pairs, Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
            return Create(Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.LinearClamp, false);
        }

        if (TryFind(pairs, Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
            return Create(Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.ManualSrgb, false);
        }

        if (TryFind(pairs, Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
            return Create(Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.ManualSrgb, false);
        }

        if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt)) {
            return Create(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt, PresentEncoding.LinearClamp, false);
        }

        if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr)) {
            return Create(Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr, PresentEncoding.LinearClamp, false);
        }

        throw new GraphicsException(
            $"The window surface advertises no usable swapchain format and color-space pair. Available pairs: {DescribePairs(pairs)}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static VulkanSwapchainFormat Create(
        Format format,
        ColorSpaceKHR colorSpace,
        PresentEncoding encoding,
        bool isHdr) => new(format, colorSpace, encoding, isHdr, VulkanFormatInfo.Describe(format));

    private static bool TryFind(
        IReadOnlyList<VulkanSurfaceFormat> pairs,
        Format format,
        ColorSpaceKHR colorSpace) {
        for (int index = 0; index < pairs.Count; index++) {
            if (pairs[index].Format == format && pairs[index].ColorSpace == colorSpace) {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<VulkanSurfaceFormat> NormalizeLegacyAnyFormat(IReadOnlyList<VulkanSurfaceFormat> formats) {
        if (formats.Count == 1 && formats[0].Format == Format.Undefined) {
            return [new VulkanSurfaceFormat(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr)];
        }

        return formats;
    }

    private static string DescribePairs(IReadOnlyList<VulkanSurfaceFormat> pairs) {
        if (pairs.Count == 0) {
            return "none";
        }

        List<string> descriptions = new(pairs.Count);
        foreach (VulkanSurfaceFormat pair in pairs) {
            descriptions.Add($"{VulkanFormatInfo.Describe(pair.Format)}/{pair.ColorSpace}");
        }

        return string.Join(", ", descriptions);
    }
}
