namespace RayBork;

/// <summary>
/// An immutable snapshot of the renderer, device, and presentation state selected at startup.
/// </summary>
/// <remarks>
/// The render thread projects this value from the backend after initialization and after a recovery
/// that changes the device or presentation state. Read it through <see cref="EngineContext.Info"/>.
/// </remarks>
/// <param name="Name">The engine name. It is <c>RayBork</c> in v1.</param>
/// <param name="Version">The version of the loaded engine assembly, as <see cref="EngineVersion.Current"/>.</param>
/// <param name="RendererName">
/// The active renderer backend name, for example <c>Vulkan 1.2</c>. The public API is
/// renderer-agnostic, so a future backend changes this string, not game code.
/// </param>
/// <param name="GpuName">The name of the selected GPU device.</param>
/// <param name="VulkanApiVersion">
/// The packed Vulkan API version of the selected device, encoded as
/// <c>(major &lt;&lt; 22) | (minor &lt;&lt; 12) | patch</c>. It is <c>0</c> before the renderer starts
/// or when the active backend is not Vulkan.
/// </param>
/// <param name="SwapchainFormat">The selected swapchain surface format.</param>
/// <param name="HdrOutput">Whether the selected swapchain format is HDR-capable.</param>
/// <param name="SampleCount">The effective MSAA sample count after device fallback.</param>
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
    bool ValidationEnabled);
