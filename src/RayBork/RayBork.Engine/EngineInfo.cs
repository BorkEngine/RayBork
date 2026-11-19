namespace RayBork;

/// <summary>An immutable snapshot of the renderer, device, and presentation state.</summary>
/// <remarks>
/// The render thread projects this value after initialization and after a recovery that changes
/// the device or presentation state. Read it through <see cref="EngineContext.Info"/>.
/// </remarks>
/// <param name="Name">The engine name; <c>RayBork</c> in v1.</param>
/// <param name="Version">The loaded engine assembly version.</param>
/// <param name="RendererName">The active backend name, for example <c>Vulkan 1.2</c>.</param>
/// <param name="GpuName">The name of the selected GPU device.</param>
/// <param name="VulkanApiVersion">The packed Vulkan API version; <c>0</c> if unavailable.</param>
/// <param name="SwapchainFormat">The selected swapchain surface format.</param>
/// <param name="HdrOutput">Whether the swapchain format is HDR-capable.</param>
/// <param name="SampleCount">The effective MSAA sample count after fallback.</param>
/// <param name="PresentMode">The effective present mode after fallback.</param>
/// <param name="ValidationEnabled">Whether the graphics validation layer is active.</param>
public readonly record struct EngineInfo(
    string Name,
    Version Version,
    string RendererName,
    string GpuName,
    uint VulkanApiVersion,
    string SwapchainFormat,
    bool HdrOutput,
    int SampleCount,
    PresentMode PresentMode,
    bool ValidationEnabled
);
