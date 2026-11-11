using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 renderer backend: the swapchain, the linear HDR scene target, and the AgX/scRGB
/// present path behind <see cref="IRendererBackend"/>.
/// </summary>
/// <remarks>
/// <para>
/// The render thread owns every member. M2 records one scene pass that only clears the linear HDR
/// target and one present pass that samples the resolved target; there are no draw commands from game
/// code yet. Recoverable presentation failures are handled inside the backend by recreating the
/// presentation path and dropping the affected frame; unrecoverable failures throw
/// <see cref="GraphicsException"/>, and validation errors are fatal.
/// </para>
/// <para>
/// The backend does not call user code. It reads the surface snapshot from
/// <see cref="RendererBackendOptions.WindowStateProvider"/> on the render thread, creates and
/// destroys the native surface through <see cref="RendererBackendOptions.SurfaceProvider"/>, and
/// publishes immutable snapshots through <see cref="Info"/> and <see cref="Stats"/>.
/// </para>
/// </remarks>
internal sealed unsafe class Vulkan12Backend : IRendererBackend {
    private const int FRAMES_IN_FLIGHT = 3;
    private const int MIN_RENDER_FINISHED_SEMAPHORES = 3;
    private const string APPLICATION_NAME = "RayBork";
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    private RendererBackendOptions? _options;
    private VulkanInstance? _instance;
    private VulkanDevice? _device;
    private IntPtr _surfaceHandle;
    private SurfaceKHR _surface;
    private VulkanSwapchain? _swapchain;
    private VulkanSceneTarget? _sceneTarget;
    private VulkanPresentPipeline? _presentPipeline;
    private CommandPool _commandPool;
    private CommandBuffer[] _commandBuffers = [];
    private Semaphore[] _imageAvailableSemaphores = [];
    private Semaphore[] _renderFinishedSemaphores = [];
    private Fence[] _fences = [];
    private VulkanSwapchainFormat _swapchainFormat;
    private VulkanPresentModeChoice _presentMode;
    private WindowHdrState _selectedHdrState = WindowHdrState.Sdr;
    private int _sampleCount;
    private int _requestedWidth;
    private int _requestedHeight;
    private uint _frameSlot;
    private bool _pendingPresent;
    private uint _pendingImageIndex;
    private Semaphore _pendingRenderFinished;
    private bool _swapchainOutOfDate;
    private bool _initialized;
    private bool _disposed;
    private EngineInfo _info = RenderStatus.CreateDefaultInfo();
    private RenderStats _stats;

    /// <inheritdoc />
    public EngineInfo Info => _info;

    /// <inheritdoc />
    public RenderStats Stats => _stats;

    /// <summary>
    /// Gets the logical device, for GPU-test readback probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanDevice Device => _device ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the current scene target, for GPU-test readback probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanSceneTarget SceneTarget => _sceneTarget ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the Vulkan instance, for GPU-test validation checks.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanInstance Instance => _instance ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// The instance, device, surface, swapchain, or fixed render state could not be created. The
    /// diagnostic code is <c>RB3001</c>.
    /// </exception>
    public void Initialize(RendererBackendOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Settings);
        ArgumentNullException.ThrowIfNull(options.InstanceExtensions);
        ArgumentNullException.ThrowIfNull(options.SurfaceProvider);
        ArgumentNullException.ThrowIfNull(options.WindowStateProvider);
        ArgumentNullException.ThrowIfNull(options.Logger);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) {
            throw new InvalidOperationException("The renderer backend has already been initialized.");
        }

        _options = options;
        try {
            InitializeCore(options);
        } catch {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A presentation result is a recoverable status and does not throw. A fatal Vulkan failure or a
    /// validation error throws with the diagnostic code <c>RB3001</c>.
    /// </exception>
    public void Submit(RenderFramePacket frame) {
        EnsureInitialized();
        if (_pendingPresent) {
            PresentPending();
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        WindowSurfaceState state = _options!.WindowStateProvider();
        if (state.IsMinimized || state.RenderWidth <= 0 || state.RenderHeight <= 0) {
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (_swapchainOutOfDate) {
            RecreatePresentation(state, LogLevel.Debug, "an out-of-date presentation result");
        }

        if (state.RenderWidth != (int)_swapchain!.Extent.Width || state.RenderHeight != (int)_swapchain.Extent.Height) {
            Resize(state.RenderWidth, state.RenderHeight);
        }

        if (HdrSelectionChanged(state.Hdr)) {
            RecreatePresentation(state, LogLevel.Information, "a window HDR state change");
        }

        uint frameSlot = _frameSlot;
        Fence fence = _fences[frameSlot];
        Vk vulkan = _device!.Vulkan;
        Device deviceHandle = _device.Device;
        ThrowIfFailed(vulkan.WaitForFences(deviceHandle, 1, &fence, true, WAIT_TIMEOUT), "vkWaitForFences");

        Semaphore imageAvailable = _imageAvailableSemaphores[frameSlot];
        SwapchainResult acquire = _swapchain.Acquire(frameSlot, imageAvailable, out uint imageIndex);
        if (acquire == SwapchainResult.OutOfDate) {
            RecreatePresentation(state, LogLevel.Debug, "an out-of-date swapchain image acquisition");
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (acquire == SwapchainResult.Suboptimal) {
            _swapchainOutOfDate = true;
            _options.Logger.LogDebug(
                "The swapchain image acquisition was suboptimal; the presentation path is recreated at the next frame.");
        }

        CommandBuffer commandBuffer = _commandBuffers[frameSlot];
        ThrowIfFailed(vulkan.ResetCommandBuffer(commandBuffer, 0), "vkResetCommandBuffer");
        CommandBufferBeginInfo beginInfo = new() {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        ThrowIfFailed(vulkan.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");
        RecordScenePass(vulkan, commandBuffer);
        RenderSettings settings = _options.Settings;
        PresentPushConstants pushConstants = new() {
            ExposureMultiplier = MathF.Pow(2f, settings.Exposure),
            HdrWhiteScale = state.Hdr.HdrEnabled ? state.Hdr.SdrWhiteLevel : 1f,
            Tonemapping = settings.Tonemapping == TonemappingMode.AgX ? 0 : 1,
            Encoding = (int)_swapchainFormat.Encoding,
        };
        _presentPipeline!.Record(vulkan, commandBuffer, imageIndex, pushConstants);
        ThrowIfFailed(vulkan.EndCommandBuffer(commandBuffer), "vkEndCommandBuffer");

        Semaphore renderFinished = _renderFinishedSemaphores[imageIndex];
        PipelineStageFlags waitStage = PipelineStageFlags.ColorAttachmentOutputBit;
        SubmitInfo submitInfo = new() {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &imageAvailable,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &renderFinished,
        };
        ThrowIfFailed(vulkan.ResetFences(deviceHandle, 1, &fence), "vkResetFences");
        ThrowIfFailed(vulkan.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, fence), "vkQueueSubmit");

        _pendingPresent = true;
        _pendingImageIndex = imageIndex;
        _pendingRenderFinished = renderFinished;
        _frameSlot = (frameSlot + 1) % FRAMES_IN_FLIGHT;

        double cpuMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        _stats = new RenderStats(
            frame.FrameIndex,
            1,
            1,
            1,
            0,
            1,
            1,
            _sampleCount,
            _swapchainFormat.IsHdr,
            cpuMilliseconds,
            0,
            CountFramesInFlight(),
            0);
        _instance!.ThrowIfValidationError("submitting a frame");
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A validation error was recorded while presenting. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    public void Present() {
        EnsureInitialized();
        PresentPending();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The swapchain failures are recovered by recreating the presentation path at the next frame
    /// boundary. Surface and device recreation is added by the recovery work; until then those
    /// failures are reported as fatal.
    /// </remarks>
    public bool Recover(RenderRecoveryReason reason) {
        EnsureInitialized();
        switch (reason) {
            case RenderRecoveryReason.SwapchainOutOfDate:
            case RenderRecoveryReason.SwapchainSuboptimal:
                _swapchainOutOfDate = true;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public void Resize(int renderWidth, int renderHeight) {
        EnsureInitialized();
        if (renderWidth <= 0 || renderHeight <= 0) {
            _options!.Logger.LogDebug(
                "The presentation surface has no drawable extent ({Width}x{Height}); the presentation path is kept. The previous drawable request was {PreviousWidth}x{PreviousHeight}.",
                renderWidth,
                renderHeight,
                _requestedWidth,
                _requestedHeight);
            _requestedWidth = renderWidth;
            _requestedHeight = renderHeight;
            return;
        }

        _requestedWidth = renderWidth;
        _requestedHeight = renderHeight;

        VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(
            _device!.Vulkan,
            _device.PhysicalDevice,
            _surface);
        Extent2D extent = SelectExtent(support.Capabilities, renderWidth, renderHeight);
        Extent2D currentExtent = _swapchain!.Extent;
        if (extent.Width == currentExtent.Width && extent.Height == currentExtent.Height) {
            return;
        }

        WindowSurfaceState current = _options!.WindowStateProvider();
        WindowSurfaceState state = new(renderWidth, renderHeight, current.IsMinimized, current.IsFocused, current.Hdr);
        RecreatePresentation(state, LogLevel.Debug, "a window resize");
    }

    /// <summary>
    /// Waits for the device to idle and destroys the GPU objects, the swapchain, the device, the
    /// surface, and the instance, in that order. The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DeviceWaitIdle();
        _pendingPresent = false;
        _presentPipeline?.Dispose();
        _presentPipeline = null;
        _sceneTarget?.Dispose();
        _sceneTarget = null;
        DestroyFrameResources();
        _swapchain?.Dispose();
        _swapchain = null;
        _device?.Dispose();
        _device = null;
        if (_instance is not null && _surfaceHandle != IntPtr.Zero) {
            try {
                _options?.SurfaceProvider.DestroySurface(_instance.Handle.Handle, _surfaceHandle);
            } catch (GraphicsException exception) {
                // Disposal must still release the instance; the surface failure is reported but not
                // rethrown, matching the device and instance disposal paths.
                _options?.Logger.LogWarning("The native Vulkan surface could not be destroyed during disposal: {Message}", exception.Message);
            } finally {
                _surfaceHandle = IntPtr.Zero;
                _surface = default;
            }
        }

        _instance?.Dispose();
        _instance = null;
        _initialized = false;
        _options?.Logger.LogDebug("Vulkan 1.2 renderer backend disposed.");
    }

    private void InitializeCore(RendererBackendOptions options) {
        ILogger logger = options.Logger;
        _instance = VulkanInstance.Create(new VulkanInstanceOptions {
            ApplicationName = APPLICATION_NAME,
            ApplicationVersion = EngineVersion.Current,
            SurfaceExtensions = options.InstanceExtensions,
            RequestValidation = options.Settings.ValidationEnabled,
            Logger = logger,
        });
        _surfaceHandle = options.SurfaceProvider.CreateSurface(_instance.Handle.Handle);
        if (_surfaceHandle == IntPtr.Zero) {
            throw new GraphicsException(
                "The surface provider returned a zero Vulkan surface handle.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        _surface = new SurfaceKHR { Handle = unchecked((ulong)_surfaceHandle.ToInt64()) };
        _device = VulkanDevice.Create(_instance, _surfaceHandle, logger);
        VulkanDeviceCapabilities capabilities = _device.Capabilities;
        WindowSurfaceState state = options.WindowStateProvider();
        _selectedHdrState = state.Hdr;
        _requestedWidth = state.RenderWidth;
        _requestedHeight = state.RenderHeight;
        _sampleCount = VulkanSampleCountSelection.Select(options.Settings.SampleCount, capabilities.SupportedSampleCounts);
        _swapchainFormat = VulkanSwapchainFormatSelection.Select(capabilities.SurfaceFormats, state.Hdr);
        _presentMode = VulkanPresentModeSelection.Select(capabilities.Swapchain.PresentModes, options.Settings.PresentMode);
        VulkanSwapchainSupport support = capabilities.Swapchain;
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount);
        _swapchain = VulkanSwapchain.Create(
            _device,
            _surface,
            _swapchainFormat,
            _presentMode.Native,
            (int)imageCount,
            extent);
        _sceneTarget = VulkanSceneTarget.Create(_device, extent, _sampleCount, options.Settings.ClearColor);
        _presentPipeline = VulkanPresentPipeline.Create(
            _device,
            extent,
            _swapchainFormat.Format,
            _sceneTarget.ResolvedColorView);
        _presentPipeline.RecreateFramebuffers(extent, _swapchain.ImageViews);
        CreateFrameResources((int)imageCount);
        UpdateInfo();
        LogInitialized(capabilities, state, extent);
        LogSelectionWarnings(state);
        _initialized = true;
        _instance.ThrowIfValidationError("initializing the renderer");
    }

    private void RecreatePresentation(WindowSurfaceState state, LogLevel logLevel, string reason) {
        VulkanDevice device = _device!;
        DeviceWaitIdle();
        _pendingPresent = false;
        VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(device.Vulkan, device.PhysicalDevice, _surface);
        VulkanSwapchainFormat format = VulkanSwapchainFormatSelection.Select(support.Formats, state.Hdr);
        VulkanPresentModeChoice presentMode = VulkanPresentModeSelection.Select(
            support.PresentModes,
            _options!.Settings.PresentMode);
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount);
        bool formatChanged = format != _swapchainFormat;

        _swapchain!.Recreate(device, _surface, format, presentMode.Native, (int)imageCount, extent);
        _sceneTarget!.Dispose();
        _sceneTarget = VulkanSceneTarget.Create(device, extent, _sampleCount, _options.Settings.ClearColor);
        if (formatChanged) {
            _presentPipeline!.Dispose();
            _presentPipeline = VulkanPresentPipeline.Create(device, extent, format.Format, _sceneTarget.ResolvedColorView);
        } else {
            _presentPipeline!.WriteDescriptor(_sceneTarget.ResolvedColorView);
        }

        _presentPipeline.RecreateFramebuffers(extent, _swapchain.ImageViews);
        _swapchainFormat = format;
        _presentMode = presentMode;
        _selectedHdrState = state.Hdr;
        RecreateRenderFinishedSemaphores((int)imageCount);
        _swapchainOutOfDate = false;
        UpdateInfo();
        LogSelectionWarnings(state);
        _options.Logger.Log(
            logLevel,
            "The presentation path was recreated for {Reason}: {Format}/{ColorSpace}, present mode {PresentMode}, {SampleCount}x MSAA, {ImageCount} swapchain image(s), {Width}x{Height}.",
            reason,
            _swapchainFormat.Description,
            _swapchainFormat.ColorSpace,
            _presentMode.Public,
            _sampleCount,
            _swapchain.ImageCount,
            extent.Width,
            extent.Height);
    }

    private void CreateFrameResources(int imageCount) {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        CommandPoolCreateInfo poolInfo = new() {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
        };
        ThrowIfFailed(vulkan.CreateCommandPool(device.Device, &poolInfo, null, out _commandPool), "vkCreateCommandPool");

        CommandBuffer[] commandBuffers = new CommandBuffer[FRAMES_IN_FLIGHT];
        CommandBufferAllocateInfo allocateInfo = new() {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = FRAMES_IN_FLIGHT,
        };
        fixed (CommandBuffer* commandBuffersPointer = commandBuffers) {
            ThrowIfFailed(
                vulkan.AllocateCommandBuffers(device.Device, &allocateInfo, commandBuffersPointer),
                "vkAllocateCommandBuffers");
        }

        _commandBuffers = commandBuffers;
        _imageAvailableSemaphores = CreateSemaphores(FRAMES_IN_FLIGHT);
        _renderFinishedSemaphores = CreateSemaphores(Math.Max(imageCount, MIN_RENDER_FINISHED_SEMAPHORES));
        _fences = CreateFences(FRAMES_IN_FLIGHT);
    }

    private Semaphore[] CreateSemaphores(int count) {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        Semaphore[] semaphores = new Semaphore[count];
        try {
            for (int index = 0; index < count; index++) {
                SemaphoreCreateInfo createInfo = new() { SType = StructureType.SemaphoreCreateInfo };
                ThrowIfFailed(
                    vulkan.CreateSemaphore(device.Device, &createInfo, null, out semaphores[index]),
                    "vkCreateSemaphore");
            }

            return semaphores;
        } catch {
            DestroySemaphores(semaphores);
            throw;
        }
    }

    private Fence[] CreateFences(int count) {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        Fence[] fences = new Fence[count];
        try {
            for (int index = 0; index < count; index++) {
                FenceCreateInfo createInfo = new() {
                    SType = StructureType.FenceCreateInfo,
                    Flags = FenceCreateFlags.SignaledBit,
                };
                ThrowIfFailed(
                    vulkan.CreateFence(device.Device, &createInfo, null, out fences[index]),
                    "vkCreateFence");
            }

            return fences;
        } catch {
            DestroyFences(fences);
            throw;
        }
    }

    private void RecreateRenderFinishedSemaphores(int imageCount) {
        DestroySemaphores(_renderFinishedSemaphores);
        _renderFinishedSemaphores = CreateSemaphores(Math.Max(imageCount, MIN_RENDER_FINISHED_SEMAPHORES));
    }

    private void DestroyFrameResources() {
        if (_device is not null) {
            Vk vulkan = _device.Vulkan;
            DestroySemaphores(_imageAvailableSemaphores);
            DestroySemaphores(_renderFinishedSemaphores);
            DestroyFences(_fences);
            if (_commandPool.Handle != 0) {
                vulkan.DestroyCommandPool(_device.Device, _commandPool, null);
                _commandPool = default;
            }
        }

        _commandBuffers = [];
        _imageAvailableSemaphores = [];
        _renderFinishedSemaphores = [];
        _fences = [];
    }

    private void DestroySemaphores(Semaphore[] semaphores) {
        if (_device is null) {
            return;
        }

        foreach (Semaphore semaphore in semaphores) {
            if (semaphore.Handle != 0) {
                _device.Vulkan.DestroySemaphore(_device.Device, semaphore, null);
            }
        }
    }

    private void DestroyFences(Fence[] fences) {
        if (_device is null) {
            return;
        }

        foreach (Fence fence in fences) {
            if (fence.Handle != 0) {
                _device.Vulkan.DestroyFence(_device.Device, fence, null);
            }
        }
    }

    private void RecordScenePass(Vk vulkan, CommandBuffer commandBuffer) {
        VulkanSceneTarget target = _sceneTarget!;
        Color color = target.ClearColor;
        ClearValue colorClear = new() {
            Color = new ClearColorValue(color.R, color.G, color.B, color.A),
        };
        ClearValue depthClear = new() {
            DepthStencil = new ClearDepthStencilValue {
                Depth = VulkanDepthState.CLEAR_DEPTH,
                Stencil = 0,
            },
        };
        ClearValue[] clearValues = target.SampleCount > 1
            ? [colorClear, default, depthClear]
            : [colorClear, depthClear];
        Rect2D renderArea = new() {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = target.Extent,
        };
        fixed (ClearValue* clearValuesPointer = clearValues) {
            RenderPassBeginInfo beginInfo = new() {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = target.RenderPass,
                Framebuffer = target.Framebuffer,
                RenderArea = renderArea,
                ClearValueCount = (uint)clearValues.Length,
                PClearValues = clearValuesPointer,
            };
            vulkan.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
            vulkan.CmdEndRenderPass(commandBuffer);
        }
    }

    private void PresentPending() {
        if (!_pendingPresent) {
            return;
        }

        SwapchainResult result = _swapchain!.Present(_device!.PresentQueue, _pendingRenderFinished, _pendingImageIndex);
        _pendingPresent = false;
        _pendingRenderFinished = default;
        if (result != SwapchainResult.Success) {
            _swapchainOutOfDate = true;
            _options!.Logger.LogDebug(
                "The swapchain presentation returned {Status}; the presentation path is recreated at the next frame.",
                result);
        }

        _instance!.ThrowIfValidationError("presenting a frame");
    }

    private int CountFramesInFlight() {
        VulkanDevice device = _device!;
        int count = 0;
        foreach (Fence fence in _fences) {
            if (device.Vulkan.GetFenceStatus(device.Device, fence) == Result.NotReady) {
                count++;
            }
        }

        return count;
    }

    private RenderStats CreateZeroDrawStats(long frameIndex) => new(
        frameIndex,
        0,
        0,
        0,
        0,
        0,
        0,
        _sampleCount,
        _swapchainFormat.IsHdr,
        0,
        0,
        0,
        0);

    private bool HdrSelectionChanged(WindowHdrState hdr) =>
        hdr.HdrEnabled != _selectedHdrState.HdrEnabled
        || (hdr.HdrEnabled && hdr.SdrWhiteLevel != _selectedHdrState.SdrWhiteLevel);

    private void UpdateInfo() {
        _info = _device!.CreateEngineInfo() with {
            SwapchainFormat = _swapchainFormat.Description,
            HdrOutput = _swapchainFormat.IsHdr,
            SampleCount = _sampleCount,
            PresentMode = _presentMode.Public,
        };
    }

    private void DeviceWaitIdle() {
        if (_device is null) {
            return;
        }

        Result result = _device.Vulkan.DeviceWaitIdle(_device.Device);
        if (result != Result.Success) {
            _options?.Logger.LogWarning("The Vulkan device wait-idle before recreation failed with {Result}.", result);
        }
    }

    private void LogInitialized(VulkanDeviceCapabilities capabilities, WindowSurfaceState state, Extent2D extent) {
        ILogger logger = _options!.Logger;
        logger.LogInformation(
            "Vulkan 1.2 renderer initialized: {GpuName}, API {ApiVersion}, validation {Validation}.",
            capabilities.DeviceName,
            VulkanVersion.Format(capabilities.ApiVersion),
            _instance!.ValidationLayersEnabled ? "enabled" : "disabled");
        logger.LogInformation(
            "Presentation path: {Format}/{ColorSpace}, HDR {HdrOutput}, present mode {PresentMode}, {SampleCount}x MSAA, {ImageCount} swapchain image(s), {Width}x{Height}.",
            _swapchainFormat.Description,
            _swapchainFormat.ColorSpace,
            _swapchainFormat.IsHdr,
            _presentMode.Public,
            _sampleCount,
            _swapchain!.ImageCount,
            extent.Width,
            extent.Height);
        logger.LogInformation(
            "Window HDR state: enabled {HdrEnabled}, SDR white level {SdrWhiteLevel}, HDR headroom {HdrHeadroom}.",
            state.Hdr.HdrEnabled,
            state.Hdr.SdrWhiteLevel,
            state.Hdr.HdrHeadroom);
    }

    private void LogSelectionWarnings(WindowSurfaceState state) {
        ILogger logger = _options!.Logger;
        RenderSettings settings = _options.Settings;
        if (_sampleCount != settings.SampleCount) {
            logger.LogWarning(
                "The requested MSAA sample count {Requested} is not supported by the device; using {Effective}.",
                settings.SampleCount,
                _sampleCount);
        }

        if (_presentMode.FellBack) {
            logger.LogWarning(
                "The requested present mode {Requested} is not supported by the window surface; using {Effective}.",
                settings.PresentMode,
                _presentMode.Public);
        }

        if (state.Hdr.HdrEnabled && !_swapchainFormat.IsHdr) {
            logger.LogWarning(
                "HDR presentation was requested for the window but the surface advertises no scRGB float format; falling back to {Format} with SDR output.",
                _swapchainFormat.Description);
        }

        if (_swapchainFormat.IsHdr && _swapchainFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr) {
            logger.LogWarning(
                "The scRGB HDR swapchain uses the sRGB nonlinear color space because VK_EXT_swapchain_colorspace offered no extended-sRGB-linear pair; the compositor may treat the surface as SDR.");
        }
    }

    private void EnsureInitialized() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized) {
            throw new InvalidOperationException("The renderer backend has not been initialized.");
        }
    }

    private static Extent2D SelectExtent(SurfaceCapabilitiesKHR capabilities, int renderWidth, int renderHeight) {
        Extent2D extent = VulkanSwapchainExtentSelection.Select(capabilities, renderWidth, renderHeight);
        return new Extent2D {
            Width = Math.Max(1u, extent.Width),
            Height = Math.Max(1u, extent.Height),
        };
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
}
