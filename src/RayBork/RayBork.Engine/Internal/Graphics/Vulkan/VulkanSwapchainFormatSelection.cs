using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The swapchain format, color space, and present encoding selected for the window surface.
/// </summary>
/// <remarks>
/// <see cref="Description"/> is the stable public format token, for example
/// <c>R16G16B16A16_SFLOAT</c>.
/// </remarks>
internal readonly record struct VulkanSwapchainFormat(
    Format Format,
    ColorSpaceKHR ColorSpace,
    PresentEncoding Encoding,
    bool IsHdr,
    string Description
);

/// <summary>
/// Chooses the swapchain format and present encoding for the window HDR state.
/// </summary>
/// <remarks>
/// HDR uses the scRGB half-float path; extended-sRGB linear is preferred over sRGB nonlinear
/// because HDR10/PQ is deferred. SDR prefers a format whose hardware transfer function matches
/// the encoding; <c>A2B10G10R10_UNORM_PACK32</c> is never selected.
/// </remarks>
internal static class VulkanSwapchainFormatSelection
{
    /// <summary>Selects the format for the surface format pairs the window advertises.</summary>
    /// <exception cref="GraphicsException">
    /// No usable pair is advertised; the diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanSwapchainFormat Select(
        IReadOnlyList<VulkanSurfaceFormat> formats,
        WindowHdrState hdr
    )
    {
        ArgumentNullException.ThrowIfNull(formats);

        IReadOnlyList<VulkanSurfaceFormat> pairs = NormalizeLegacyAnyFormat(formats);
        if (hdr.HdrEnabled)
        {
            if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt))
            {
                return Create(
                    Format.R16G16B16A16Sfloat,
                    ColorSpaceKHR.SpaceExtendedSrgbLinearExt,
                    PresentEncoding.ScRgbHdr,
                    true
                );
            }

            if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
            {
                return Create(
                    Format.R16G16B16A16Sfloat,
                    ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                    PresentEncoding.ScRgbHdr,
                    true
                );
            }
        }

        if (TryFind(pairs, Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
        {
            return Create(
                Format.B8G8R8A8Srgb,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                PresentEncoding.LinearClamp,
                false
            );
        }

        if (TryFind(pairs, Format.R8G8B8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
        {
            return Create(
                Format.R8G8B8A8Srgb,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                PresentEncoding.LinearClamp,
                false
            );
        }

        if (TryFind(pairs, Format.B8G8R8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
        {
            return Create(
                Format.B8G8R8A8Unorm,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                PresentEncoding.ManualSrgb,
                false
            );
        }

        if (TryFind(pairs, Format.R8G8B8A8Unorm, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
        {
            return Create(
                Format.R8G8B8A8Unorm,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                PresentEncoding.ManualSrgb,
                false
            );
        }

        if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceExtendedSrgbLinearExt))
        {
            return Create(
                Format.R16G16B16A16Sfloat,
                ColorSpaceKHR.SpaceExtendedSrgbLinearExt,
                PresentEncoding.LinearClamp,
                false
            );
        }

        if (TryFind(pairs, Format.R16G16B16A16Sfloat, ColorSpaceKHR.SpaceSrgbNonlinearKhr))
        {
            return Create(
                Format.R16G16B16A16Sfloat,
                ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                PresentEncoding.LinearClamp,
                false
            );
        }

        throw new GraphicsException(
            $"The window surface advertises no usable swapchain format and color-space pair. Available pairs: {DescribePairs(pairs)}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private static VulkanSwapchainFormat Create(
        Format format,
        ColorSpaceKHR colorSpace,
        PresentEncoding encoding,
        bool isHdr
    ) => new(format, colorSpace, encoding, isHdr, VulkanFormatInfo.Describe(format));

    private static bool TryFind(
        IReadOnlyList<VulkanSurfaceFormat> pairs,
        Format format,
        ColorSpaceKHR colorSpace
    )
    {
        for (int index = 0; index < pairs.Count; index++)
        {
            if (pairs[index].Format == format && pairs[index].ColorSpace == colorSpace)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<VulkanSurfaceFormat> NormalizeLegacyAnyFormat(
        IReadOnlyList<VulkanSurfaceFormat> formats
    )
    {
        if (formats.Count == 1 && formats[0].Format == Format.Undefined)
        {
            return
            [
                new VulkanSurfaceFormat(Format.B8G8R8A8Srgb, ColorSpaceKHR.SpaceSrgbNonlinearKhr),
            ];
        }

        return formats;
    }

    private static string DescribePairs(IReadOnlyList<VulkanSurfaceFormat> pairs)
    {
        if (pairs.Count == 0)
        {
            return "none";
        }

        List<string> descriptions = new(pairs.Count);
        foreach (VulkanSurfaceFormat pair in pairs)
        {
            descriptions.Add($"{VulkanFormatInfo.Describe(pair.Format)}/{pair.ColorSpace}");
        }

        return string.Join(", ", descriptions);
    }
}
