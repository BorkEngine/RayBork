namespace RayBork.Internal;

/// <summary>
/// The seam between the render thread's Vulkan instance and the main-thread platform surface.
/// </summary>
/// <remarks>
/// <para>
/// The render thread calls both methods. The app implementation marshals each call to the main
/// thread, which owns the window and the native surface; <see cref="CreateSurface"/> blocks until
/// the main thread has created the surface. Tests back this interface with a direct platform call
/// on the test thread.
/// </para>
/// <para>
/// The renderer owns the native surface through this interface. It must call
/// <see cref="DestroySurface"/> before it destroys the Vulkan instance that created the surface.
/// </para>
/// </remarks>
internal interface IRendererSurfaceProvider {
    /// <summary>
    /// Creates the native Vulkan surface for the platform window.
    /// </summary>
    /// <remarks>
    /// Called on the render thread. The call blocks until the main thread has created the surface.
    /// A zero handle is not a successful result and is reported as a failure.
    /// </remarks>
    /// <param name="instance">The native <c>VkInstance</c> handle that will own the surface.</param>
    /// <returns>The non-zero native <c>VkSurfaceKHR</c> handle.</returns>
    /// <exception cref="GraphicsException">
    /// The platform could not create the surface. The platform failure is the inner exception and
    /// the diagnostic code is <c>RB3001</c>.
    /// </exception>
    IntPtr CreateSurface(IntPtr instance);

    /// <summary>
    /// Destroys a native Vulkan surface created by <see cref="CreateSurface"/>.
    /// </summary>
    /// <remarks>
    /// Called on the render thread. The call blocks until the main thread has destroyed the
    /// surface. It must complete before the instance is destroyed. A zero handle is a no-op.
    /// </remarks>
    /// <param name="instance">The native <c>VkInstance</c> handle that created the surface.</param>
    /// <param name="surface">
    /// The native <c>VkSurfaceKHR</c> handle to destroy, or <see cref="IntPtr.Zero"/> for no-op.
    /// </param>
    /// <exception cref="GraphicsException">
    /// The platform could not destroy the surface. The platform failure is the inner exception and
    /// the diagnostic code is <c>RB3001</c>.
    /// </exception>
    void DestroySurface(IntPtr instance, IntPtr surface);
}
