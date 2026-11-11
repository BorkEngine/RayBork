namespace RayBork.Internal;

/// <summary>
/// Creates the renderer backend for one <see cref="RayBorkApp.Run"/> call.
/// </summary>
/// <remarks>
/// The production factory creates the Vulkan 1.2 backend. Tests substitute a fake backend
/// through the internal <see cref="RayBorkAppBuilder"/> overload, so lifecycle tests do not touch
/// the GPU. The factory runs on the render thread during startup, and the returned backend is owned
/// and disposed by that thread.
/// </remarks>
/// <returns>The uninitialized backend. The app initializes, drives, and disposes it.</returns>
internal delegate IRendererBackend RendererBackendFactory();
