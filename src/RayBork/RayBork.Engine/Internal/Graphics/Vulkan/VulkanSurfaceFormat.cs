using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// One surface format and color-space pair advertised by the window surface.
/// </summary>
internal readonly record struct VulkanSurfaceFormat(Format Format, ColorSpaceKHR ColorSpace);
