namespace RayBork.Internal;

/// <summary>
/// The seam between the render thread's Vulkan instance and the main-thread platform surface.
/// </summary>
/// <remarks>
/// The render thread calls both methods and blocks until the main thread completes each one. The
/// renderer must destroy the surface before the Vulkan instance that created it.
/// </remarks>
internal interface IRendererSurfaceProvider
{
    /// <summary>Creates the native Vulkan surface for the platform window.</summary>
    /// <param name="instance">The native <c>VkInstance</c> that will own the surface.</param>
    /// <returns>The non-zero native <c>VkSurfaceKHR</c> handle.</returns>
    /// <exception cref="GraphicsException">
    /// The platform could not create the surface; the diagnostic code is <c>RB3001</c>.
    /// </exception>
    IntPtr CreateSurface(IntPtr instance);

    /// <summary>Destroys a native Vulkan surface created by <see cref="CreateSurface"/>.</summary>
    /// <param name="instance">The native <c>VkInstance</c> that created the surface.</param>
    /// <param name="surface">The native <c>VkSurfaceKHR</c> handle; zero is a no-op.</param>
    /// <exception cref="GraphicsException">
    /// The platform could not destroy the surface; the diagnostic code is <c>RB3001</c>.
    /// </exception>
    void DestroySurface(IntPtr instance, IntPtr surface);
}
