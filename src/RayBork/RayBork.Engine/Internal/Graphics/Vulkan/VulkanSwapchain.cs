using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// The status of a swapchain image acquisition or presentation.
/// </summary>
/// <remarks>
/// Out-of-date, surface-lost, and suboptimal are normal statuses, not failures.
/// </remarks>
internal enum SwapchainResult
{
    /// <summary>The operation completed and the image is usable.</summary>
    Success,

    /// <summary>The surface changed and the swapchain must be recreated.</summary>
    OutOfDate,

    /// <summary>The swapchain still works but the surface changed; recreate it soon.</summary>
    Suboptimal,

    /// <summary>The surface is gone and must be recreated through the platform.</summary>
    SurfaceLost,
}

/// <summary>
/// The Vulkan swapchain, its images, and its image views owned by the render thread.
/// </summary>
/// <remarks>
/// Recreate and dispose require an idle device; unexpected results throw, while out-of-date,
/// surface-lost, and suboptimal map to <see cref="SwapchainResult"/>.
/// </remarks>
internal sealed unsafe class VulkanSwapchain : IDisposable
{
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

    private VulkanSwapchain(Vk vulkan, Device device)
    {
        _vulkan = vulkan;
        _device = device;
    }

    /// <summary>Gets the Vulkan format of the swapchain images.</summary>
    internal Format Format => _format;

    /// <summary>Gets the current image extent.</summary>
    internal Extent2D Extent => _extent;

    /// <summary>Gets the number of images the swapchain owns.</summary>
    internal int ImageCount => _images.Length;

    /// <summary>Gets the present-pass color attachment views, in image index order.</summary>
    internal IReadOnlyList<ImageView> ImageViews => _imageViews;

    /// <summary>Gets whether diagnostic presented-pixel readback is supported.</summary>
    internal bool SupportsPresentReadback { get; private set; }

    /// <summary>Gets the swapchain images, in image index order; diagnostics only.</summary>
    internal IReadOnlyList<Image> Images => _images;

    /// <summary>
    /// Creates a swapchain and its image views for a non-zero image count and extent.
    /// </summary>
    internal static VulkanSwapchain Create(
        VulkanDevice device,
        SurfaceKHR surface,
        VulkanSwapchainFormat format,
        PresentModeKHR presentMode,
        int imageCount,
        Extent2D extent
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        VulkanSwapchain swapchain = new(device.Vulkan, device.Device);
        try
        {
            SwapchainResources resources = swapchain.Build(
                device,
                surface,
                format,
                presentMode,
                imageCount,
                extent,
                default
            );
            swapchain.Apply(resources);
            return swapchain;
        }
        catch
        {
            swapchain.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Recreates the swapchain in place, reusing the surface association of the old swapchain.
    /// </summary>
    /// <remarks>Requires an idle device; on failure the old swapchain stays intact.</remarks>
    internal void Recreate(
        VulkanDevice device,
        SurfaceKHR surface,
        VulkanSwapchainFormat format,
        PresentModeKHR presentMode,
        int imageCount,
        Extent2D extent
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ObjectDisposedException.ThrowIf(_disposed, this);

        SwapchainResources resources = Build(
            device,
            surface,
            format,
            presentMode,
            imageCount,
            extent,
            _swapchain
        );
        SwapchainKHR previousSwapchain = _swapchain;
        ImageView[] previousViews = _imageViews;
        Apply(resources);
        DestroyImageViews(previousViews);
        if (previousSwapchain.Handle != 0)
        {
            VulkanNativeFunctions.DestroySwapchain(_vulkan, _device, previousSwapchain);
        }
    }

    /// <summary>
    /// Acquires the next presentation image and signals the image-available semaphore.
    /// </summary>
    internal SwapchainResult Acquire(uint frameSlot, Semaphore imageAvailable, out uint imageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Result result = VulkanNativeFunctions.AcquireNextImage(
            _vulkan,
            _device,
            _swapchain,
            ACQUIRE_TIMEOUT,
            imageAvailable,
            out imageIndex
        );
        return result switch
        {
            Result.Success => SwapchainResult.Success,
            Result.SuboptimalKhr => SwapchainResult.Suboptimal,
            Result.ErrorOutOfDateKhr => SwapchainResult.OutOfDate,
            Result.ErrorSurfaceLostKhr => SwapchainResult.SurfaceLost,
            _ => throw new GraphicsException(
                $"The Vulkan command vkAcquireNextImageKHR failed with {result} while acquiring a swapchain image for frame slot {frameSlot}.",
                DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
                null
            ),
        };
    }

    /// <summary>Queues a presentation of an acquired image.</summary>
    internal SwapchainResult Present(Queue queue, Semaphore renderFinished, uint imageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SwapchainKHR swapchain = _swapchain;
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderFinished,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex,
        };
        Result result = VulkanNativeFunctions.Present(_vulkan, _device, queue, presentInfo);
        return result switch
        {
            Result.Success => SwapchainResult.Success,
            Result.SuboptimalKhr => SwapchainResult.Suboptimal,
            Result.ErrorOutOfDateKhr => SwapchainResult.OutOfDate,
            Result.ErrorSurfaceLostKhr => SwapchainResult.SurfaceLost,
            _ => throw new GraphicsException(
                $"The Vulkan command vkQueuePresentKHR failed with {result} while presenting an image.",
                DiagnosticCodes.GRAPHICS_UNRECOVERABLE,
                null
            ),
        };
    }

    /// <summary>Destroys the image views and the swapchain; the call is idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyImageViews(_imageViews);
        _imageViews = [];
        _images = [];
        if (_swapchain.Handle != 0)
        {
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
        SwapchainKHR oldSwapchain
    )
    {
        if (imageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageCount),
                imageCount,
                "The swapchain image count must be positive."
            );
        }

        if (extent.Width == 0 || extent.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extent),
                extent,
                "The swapchain extent must be positive."
            );
        }

        SurfaceCapabilitiesKHR capabilities = VulkanNativeFunctions.GetSurfaceCapabilities(
            _vulkan,
            device.PhysicalDevice,
            surface
        );
        Extent2D imageExtent = VulkanSwapchainExtentSelection.Select(
            capabilities,
            checked((int)extent.Width),
            checked((int)extent.Height)
        );
        uint graphicsFamily = device.Capabilities.GraphicsQueueFamily;
        uint presentFamily = device.Capabilities.PresentQueueFamily;
        bool concurrent = graphicsFamily != presentFamily;
        uint[] familyIndices = [graphicsFamily, presentFamily];
        (ImageUsageFlags imageUsage, bool supportsPresentReadback) =
            VulkanSwapchainImageUsage.Select(capabilities.SupportedUsageFlags);
        SwapchainCreateInfoKHR createInfo = new()
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = surface,
            MinImageCount = (uint)imageCount,
            ImageFormat = format.Format,
            ImageColorSpace = format.ColorSpace,
            ImageExtent = imageExtent,
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
        fixed (uint* familyIndicesPointer = familyIndices)
        {
            if (concurrent)
            {
                createInfo.QueueFamilyIndexCount = (uint)familyIndices.Length;
                createInfo.PQueueFamilyIndices = familyIndicesPointer;
            }

            swapchain = VulkanNativeFunctions.CreateSwapchain(_vulkan, _device, createInfo);
        }

        Image[] images = VulkanNativeFunctions.GetSwapchainImages(_vulkan, _device, swapchain);
        ImageView[] views = new ImageView[images.Length];
        try
        {
            for (int index = 0; index < images.Length; index++)
            {
                views[index] = CreateImageView(images[index], format.Format);
            }
        }
        catch
        {
            foreach (ImageView view in views)
            {
                if (view.Handle != 0)
                {
                    _vulkan.DestroyImageView(_device, view, null);
                }
            }

            VulkanNativeFunctions.DestroySwapchain(_vulkan, _device, swapchain);
            throw;
        }

        return new SwapchainResources(
            swapchain,
            images,
            views,
            format.Format,
            imageExtent,
            supportsPresentReadback
        );
    }

    private ImageView CreateImageView(Image image, Format format)
    {
        ImageViewCreateInfo createInfo = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            Components = new ComponentMapping
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange = new ImageSubresourceRange
            {
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

    private void Apply(SwapchainResources resources)
    {
        _swapchain = resources.Swapchain;
        _images = resources.Images;
        _imageViews = resources.ImageViews;
        _format = resources.Format;
        _extent = resources.Extent;
        SupportsPresentReadback = resources.SupportsPresentReadback;
    }

    private void DestroyImageViews(ImageView[] views)
    {
        foreach (ImageView view in views)
        {
            if (view.Handle != 0)
            {
                _vulkan.DestroyImageView(_device, view, null);
            }
        }
    }

    private static CompositeAlphaFlagsKHR SelectCompositeAlpha(SurfaceCapabilitiesKHR capabilities)
    {
        CompositeAlphaFlagsKHR supported = capabilities.SupportedCompositeAlpha;
        if ((supported & CompositeAlphaFlagsKHR.OpaqueBitKhr) != 0)
        {
            return CompositeAlphaFlagsKHR.OpaqueBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.PreMultipliedBitKhr) != 0)
        {
            return CompositeAlphaFlagsKHR.PreMultipliedBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.PostMultipliedBitKhr) != 0)
        {
            return CompositeAlphaFlagsKHR.PostMultipliedBitKhr;
        }

        if ((supported & CompositeAlphaFlagsKHR.InheritBitKhr) != 0)
        {
            return CompositeAlphaFlagsKHR.InheritBitKhr;
        }

        throw new GraphicsException(
            "The window surface advertises no supported composite-alpha mode.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private static void ThrowIfFailed(Result result, string operation)
    {
        if (result == Result.Success)
        {
            return;
        }

        throw new GraphicsException(
            $"The Vulkan command {operation} failed with {result}.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private readonly record struct SwapchainResources(
        SwapchainKHR Swapchain,
        Image[] Images,
        ImageView[] ImageViews,
        Format Format,
        Extent2D Extent,
        bool SupportsPresentReadback
    );
}
