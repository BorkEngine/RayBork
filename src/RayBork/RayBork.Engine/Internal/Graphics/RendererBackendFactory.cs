namespace RayBork.Internal;

/// <summary>Creates the renderer backend for one <see cref="RayBorkApp.Run"/> call.</summary>
/// <remarks>Runs on the render thread, which owns the returned backend.</remarks>
/// <returns>The uninitialized backend.</returns>
internal delegate IRendererBackend RendererBackendFactory();
