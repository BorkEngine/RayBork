using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// One surface format and color-space pair advertised by the window surface.
/// </summary>
/// <param name="Format">The Vulkan surface format.</param>
/// <param name="ColorSpace">The color space the surface advertises for that format.</param>
internal readonly record struct VulkanSurfaceFormat(Format Format, ColorSpaceKHR ColorSpace);
