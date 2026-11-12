using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// The status of a swapchain image acquisition or presentation.
/// </summary>
/// <remarks>
/// Out-of-date, surface-lost, and suboptimal results are normal presentation statuses, not failures.
/// The caller recreates the presentation path, the surface, or both instead of unwinding.
/// </remarks>
internal enum SwapchainResult {
    /// <summary>The operation completed and the image is usable.</summary>
    Success,

    /// <summary>The surface changed and the swapchain must be recreated.</summary>
    OutOfDate,

    /// <summary>The swapchain still works but the surface changed; recreate it soon.</summary>
    Suboptimal,

    /// <summary>The surface is no longer usable and must be recreated through the platform.</summary>
    SurfaceLost,
}

/// <summary>
/// The Vulkan swapchain, its images, and its image views owned by the render thread.
/// </summary>
/// <remarks>
/// <para>
/// A swapchain instance is created once and then recreated in place on resize, HDR, or surface
/// changes. <see cref="Recreate"/> passes the old handle to <c>vkCreateSwapchainKHR</c> so the driver
/// can reuse the surface association, then destroys the old image views and handle. The caller must
/// wait for the device to idle before recreating or disposing.
/// </para>
/// <para>
/// Every member is render-thread only. Acquire and present map the out-of-date, surface-lost, and
/// suboptimal results to <see cref="SwapchainResult"/>; every other result is fatal and becomes a
/// <see cref="GraphicsException"/>.
/// </para>
/// </remarks>
internal sealed unsafe class VulkanSwapchain : IDisposable {
    private const uint IMAGE_ARRAY_LAYERS = 1;
    private const uint IMAGE_MIP_LEVELS = 1;
    private const ulong ACQUIRE_TIMEOUT = ulong.MaxValue;

    private readonly Vk _vulkan;
    private readonly Device _device;

    private SwapchainKHR _swapchain;
    private Image[] _images = [];
    private ImageView[] _imageViews = [];
    private Format _format;
    private Extent2D _extent;
    private bool _disposed;

    private VulkanSwapchain(Vk vulkan, Device device) {
        _vulkan = vulkan;
        _device = device;
    }

    /// <summary>
    /// Gets the Vulkan format of the swapchain images.
    /// </summary>
    internal Format Format => _format;

    /// <summary>
    /// Gets the current image extent.
    /// </summary>
    internal Extent2D Extent => _extent;

    /// <summary>
    /// Gets the number of images the swapchain owns.
    /// </summary>
    internal int ImageCount => _images.Length;

    /// <summary>
    /// Gets the image views used as present-pass color attachments, in image index order.
    /// </summary>
    internal IReadOnlyList<ImageView> ImageViews => _imageViews;

    /// <summary>
    /// Gets whether the swapchain images were created with transfer-source usage, so a diagnostic
    /// presented-pixel readback is possible. The frame path never reads images back.
    /// </summary>
    internal bool SupportsPresentReadback { get; private set; }

    /// <summary>
    /// Gets the swapchain images, in image index order.
    /// </summary>
    /// <remarks>
    /// Diagnostics only: <see cref="VulkanReadback.ReadPresentedColorPixel"/> uses this to probe the
    /// image a submitted frame rendered. The renderer reaches the images through
    /// <see cref="ImageViews"/> and the present pipeline's framebuffers.
    /// </remarks>
    internal IReadOnlyList<Image> Images => _images;

    /// <summary>
    /// Creates a swapchain and its image views.
    /// </summary>
    /// <param name="device">The device that owns the swapchain.</param>
    /// <param name="surface">The window surface the swapchain targets.</param>
    /// <param name="format">The selected surface format and color space.</param>
    /// <param name="presentMode">The selected present mode.</param>
    /// <param name="imageCount">The number of images to request.</param>
    /// <param name="extent">The positive image extent.</param>
    /// <returns>The created swapchain.</returns>
    /// <exception cref="GraphicsException">
    /// The swapchain or an image view could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal static VulkanSwapchain Create(
        VulkanDevice device,
        SurfaceKHR surface,
        VulkanSwapchainFormat format,
        PresentModeKHR presentMode,
        int imageCount,
        Extent2D extent) {
        ArgumentNullException.ThrowIfNull(device);
        VulkanSwapchain swapchain = new(device.Vulkan, device.Device);
        try {
            SwapchainResources resources = swapchain.Build(
                device,
                surface,
                format,
                presentMode,
                imageCount,
                extent,
                default);
            swapchain.Apply(resources);
            return swapchain;
        } catch {
            swapchain.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Recreates the swapchain in place, reusing the surface association of the old swapchain.
    /// </summary>
    /// <remarks>
    /// The caller must wait for the device to idle before calling this method. On failure the old
    /// swapchain remains intact and the exception propagates.
    /// </remarks>
    /// <param name="device">The device that owns the swapchain.</param>
    /// <param name="surface">The window surface the swapchain targets.</param>
    /// <param name="format">The selected surface format and color space.</param>
    /// <param name="presentMode">The selected present mode.</param>
    /// <param name="imageCount">The number of images to request.</param>
    /// <param name="extent">The positive image extent.</param>
    /// <exception cref="GraphicsException">
    /// The swapchain or an image view could not be created. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    internal void Recreate(
        VulkanDevice device,
        SurfaceKHR surface,
        VulkanSwapchainFormat format,
        PresentModeKHR presentMode,
        int imageCount,
        Extent2D extent) {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SwapchainResources resources = Build(
            device,
            surface,
            format,
            presentMode,
            imageCount,
            extent,
            _swapchain);
        SwapchainKHR previousSwapchain = _swapchain;
        ImageView[] previousViews = _imageViews;
        Apply(resources);
        DestroyImageViews(previousViews);
        if (previousSwapchain.Handle != 0) {
            VulkanNativeFunctions.DestroySwapchain(_vulkan, _device, previousSwapchain);
        }
    }

    /// <summary>
    /// Acquires the next presentation image.
    /// </summary>
    /// <param name="frameSlot">The frame slot that owns the image-available semaphore, for diagnostics.</param>
    /// <param name="imageAvailable">The semaphore the acquire signals when the image is available.</param>
    /// <param name="imageIndex">The acquired image index.</param>
    /// <returns>The acquisition status.</returns>
    /// <exception cref="GraphicsException">
    /// The acquire failed with a result that is not a presentation status. The diagnostic code is
    /// <c>RB3003</c>.
    /// </exception>
    internal SwapchainResult Acquire(uint frameSlot, Semaphore imageAvailable, out uint imageIndex) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Result result = VulkanNativeFunctions.AcquireNextImage(
            _vulkan,
            _device,
            _swapchain,
            ACQUIRE_TIMEOUT,
            imageAvailable,
            out imageIndex);
        return result switch {
            Result.Success => SwapchainResult.Success,
            Result.SuboptimalKhr => SwapchainResult.Suboptimal,
            Result.ErrorOutOfDateKhr => SwapchainResult.OutOfDate,
            Result.ErrorSurfaceLostKhr => SwapchainResult.SurfaceLost,
            _ => throw new GraphicsException(
                $"The Vulkan command vkAcquireNextImageKHR failed with {result} while acquiring a swapchain image for frame slot {frameSlot}.",
                DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
                null),
        };
    }

    /// <summary>
    /// Queues a presentation of an acquired image.
    /// </summary>
    /// <param name="queue">The presentation queue.</param>
    /// <param name="renderFinished">The semaphore that signals when rendering the image finished.</param>
    /// <param name="imageIndex">The acquired image index.</param>
    /// <returns>The presentation status.</returns>
    /// <exception cref="GraphicsException">
    /// The present failed with a result that is not a presentation status. The diagnostic code is
    /// <c>RB3003</c>.
    /// </exception>
    internal SwapchainResult Present(Queue queue, Semaphore renderFinished, uint imageIndex) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SwapchainKHR swapchain = _swapchain;
        PresentInfoKHR presentInfo = new() {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinished,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };
        Result result = VulkanNativeFunctions.Present(_vulkan, _device, queue, presentInfo);
        return result switch {
            Result.Success => SwapchainResult.Success,
            Result.SuboptimalKhr => SwapchainResult.Suboptimal,
            Result.ErrorOutOfDateKhr => SwapchainResult.OutOfDate,
            Result.ErrorSurfaceLostKhr => SwapchainResult.SurfaceLost,
            _ => throw new GraphicsException(
                $"The Vulkan command vkQueuePresentKHR failed with {result} while presenting an image.",
                DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
                null),
        };
    }

    /// <summary>
    /// Destroys the image views and the swapchain. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DestroyImageViews(_imageViews);
        _imageViews = [];
        _images = [];
        if (_swapchain.Handle != 0) {
            VulkanNativeFunctions.DestroySwapchain(_vulkan, _device, _swapchain);
            _swapchain = default;
        }
    }

    private SwapchainResources Build(
        VulkanDevice device,
        SurfaceKHR surface,
        VulkanSwapchainFormat format,
        PresentModeKHR presentMode,
        int imageCount,
        Extent2D extent,
        SwapchainKHR oldSwapchain) {
        if (imageCount <= 0) {
            throw new ArgumentOutOfRangeException(nameof(imageCount), imageCount, "The swapchain image count must be positive.");
        }

        if (extent.Width == 0 || extent.Height == 0) {
            throw new ArgumentOutOfRangeException(nameof(extent), extent, "The swapchain extent must be positive.");
        }

        SurfaceCapabilitiesKHR capabilities = VulkanNativeFunctions.GetSurfaceCapabilities(
            _vulkan,
            device.PhysicalDevice,
            surface);
        uint graphicsFamily = device.Capabilities.GraphicsQueueFamily;
        uint presentFamily = device.Capabilities.PresentQueueFamily;
        bool concurrent = graphicsFamily != presentFamily;
        uint[] familyIndices = [graphicsFamily, presentFamily];
        (ImageUsageFlags imageUsage, bool supportsPresentReadback) =
            VulkanSwapchainImageUsage.Select(capabilities.SupportedUsageFlags);
        SwapchainCreateInfoKHR createInfo = new() {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = (uint)imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = IMAGE_ARRAY_LAYERS,
            ImageUsage = imageUsage,
            ImageSharingMode = concurrent ? SharingMode.Concurrent : SharingMode.Exclusive,
            PreTransform = capabilities.CurrentTransform,
            CompositeAlpha = SelectCompositeAlpha(capabilities),
            PresentMode = presentMode,
            Clipped = true,
            OldSwapchain = oldSwapchain,
        };
        SwapchainKHR swapchain;
        fixed (uint* familyIndicesPointer = familyIndices) {
            if (concurrent) {
                createInfo.QueueFamilyIndexCount = (uint)familyIndices.Length;
                createInfo.PQueueFamilyIndices = familyIndicesPointer;
            }

            swapchain = VulkanNativeFunctions.CreateSwapchain(_vulkan, _device, createInfo);
        }

        Image[] images = VulkanNativeFunctions.GetSwapchainImages(_vulkan, _device, swapchain);
        ImageView[] views = new ImageView[images.Length];
        try {
            for (int index = 0; index < images.Length; index++) {
                views[index] = CreateImageView(images[index], format.Format);
            }
        } catch {
            foreach (ImageView view in views) {
                if (view.Handle != 0) {
                    _vulkan.DestroyImageView(_device, view, null);
                }
            }

            VulkanNativeFunctions.DestroySwapchain(_vulkan, _device, swapchain);
            throw;
        }

        return new SwapchainResources(swapchain, images, views, format.Format, extent, supportsPresentReadback);
    }

    private ImageView CreateImageView(Image image, Format format) {
        ImageViewCreateInfo createInfo = new() {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange = new ImageSubresourceRange {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseMipLevel = 0,
                LevelCount = IMAGE_MIP_LEVELS,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };
        Result result = _vulkan.CreateImageView(_device, &createInfo, null, out ImageView view);
        ThrowIfFailed(result, "vkCreateImageView");
        return view;
    }

    private void Apply(SwapchainResources resources) {
        _swapchain = resources.Swapchain;
        _images = resources.Images;
        _imageViews = resources.ImageViews;
        _format = resources.Format;
        _extent = resources.Extent;
        SupportsPresentReadback = resources.SupportsPresentReadback;
    }

    private void DestroyImageViews(ImageView[] views) {
        foreach (ImageView view in views) {
            if (view.Handle != 0) {
                _vulkan.DestroyImageView(_device, view, null);
            }
        }
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(SurfaceCapabilitiesKHR capabilities) {
        CompositeAlphaFlagsKHR supported = capabilities.SupportedCompositeAlpha;
        if ((supported & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0) {
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.PreMultipliedBitKhr) != 0) {
            return CompositeAlphaFlagsKHR.PreMultipliedBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.PostMultipliedBitKhr) != 0) {
            return CompositeAlphaFlagsKHR.PostMultipliedBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.InheritBitKhr) != 0) {
            return CompositeAlphaFlagsKHR.InheritBitKhr;
        }

        throw new GraphicsException(
            "The window surface advertises no supported composite-alpha mode.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static void ThrowIfFailed(Result result, string operation) {
        if (result == Result.Success) {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private readonly record struct SwapchainResources(
        SwapchainKHR Swapchain,
        Image[] Images,
        ImageView[] ImageViews,
        Format Format,
        Extent2D Extent,
        bool SupportsPresentReadback);
}
