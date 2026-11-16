using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 renderer backend: the swapchain, the linear HDR scene target, the M4 geometry pass
/// with bindless textures and automatic instancing, the runtime render target color and depth paths, and
/// the AgX/scRGB present path behind <see cref="IRendererBackend"/>.
/// </summary>
/// <remarks>
/// <para>
/// The render thread owns every member. <see cref="Submit"/> waits for a frame slot, plans the
/// packet's batches, acquires a swapchain image, records one render pass per camera pass, records the
/// present pass, and submits without waiting for the frame fence. Each camera render pass uses its
/// clamped viewport as the render area, binds one shared descriptor set per slot, clears that viewport
/// with <c>vkCmdClearAttachments</c> when the camera asks, writes that pass's frame block into a
/// host-visible frame storage buffer with a pass-index push constant, and draws instanced scene
/// batches whose per-instance records live in a second host-visible storage buffer.
/// Recoverable presentation, surface, and device failures are handled inside the backend;
/// <see cref="Recover"/> idles the device, drops the affected frame, recreates the affected resources,
/// and resumes. A bounded recovery loop or a failed recovery throws
/// <see cref="GraphicsDeviceLostException"/>, and validation errors are fatal.
/// </para>
/// <para>
/// The backend does not call user code. It reads the surface snapshot from
/// <see cref="RendererBackendOptions.WindowStateProvider"/> on the render thread, creates and
/// destroys the native surface through <see cref="RendererBackendOptions.SurfaceProvider"/>, and
/// publishes immutable snapshots through <see cref="Info"/> and <see cref="Stats"/>.
/// </para>
/// <para>
/// The render thread creates the instance and device and destroys every GPU object, the surface, and
/// the device. The instance and its loader outlive the render thread: <see cref="Dispose"/> enqueues
/// their teardown on <see cref="RendererBackendOptions.TeardownQueue"/>, and the main thread destroys
/// them after the render thread has stopped. The split keeps Vulkan driver thread-local state valid
/// until the thread that used it has exited.
/// </para>
/// <para>
/// M4 also implements <see cref="IAssetGpuWorkerProvider"/>. The render loop creates one asset GPU
/// worker after initialization and keeps it for the backend's lifetime. The worker reads the current
/// device, device generation, bindless table, and frame-idleness state through accessors, so a device
/// recreation is handled by <see cref="IAssetGpuWorker.Restore"/> without replacing the worker.
/// </para>
/// </remarks>
internal sealed unsafe class Vulkan12Backend : IRendererBackend, IAssetGpuWorkerProvider {
    /// <summary>
    /// The number of consecutive recovery attempts without a completed frame after which recovery is
    /// fatal. The bound stops a recovery loop that can never make progress.
    /// </summary>
    internal const int MAX_CONSECUTIVE_RECOVERIES = 3;

    private const int FRAMES_IN_FLIGHT = VulkanFrameResources.FRAMES_IN_FLIGHT;
    private const int MIN_RENDER_FINISHED_SEMAPHORES = 3;
    private const string APPLICATION_NAME = "RayBork";
    private const ulong WAIT_TIMEOUT = ulong.MaxValue;

    private readonly Dictionary<long, RenderTargetEntry> _renderTargets = [];
    private readonly HashSet<RenderTargetEntry> _frameRenderTargets = [];
    private readonly HashSet<RenderTargetEntry> _frameSampledRenderTargets = [];

    private RendererBackendOptions? _options;
    private VulkanInstance? _instance;
    private VulkanDevice? _device;
    private VulkanAssetWorker? _assetGpuWorker;
    private VulkanDescriptorState? _descriptorState;
    private VulkanFrameResources? _frameResources;
    private VulkanBindlessTextureTable? _bindlessTextures;
    private VulkanGeometryPipelineCache? _geometryPipelines;
    private IntPtr _surfaceHandle;
    private SurfaceKHR _surface;
    private VulkanSwapchain? _swapchain;
    private VulkanRenderTarget? _sceneTarget;
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
    private uint _lastPresentedImageIndex = uint.MaxValue;
    private Semaphore _pendingRenderFinished;
    private bool _swapchainOutOfDate;
    private bool _suboptimalRecoveryPending;
    private long _presentedFrames;
    private int _recoveryCount;
    private int _consecutiveRecoveries;
    private int _deviceGeneration;
    private RenderRecoveryReason? _simulatedFailure;
    private bool _unrecoverableFailureForTests;
    private bool _initialized;
    private bool _disposed;
    private EngineInfo _info = RenderStatus.CreateDefaultInfo();
    private RenderStats _stats;

    /// <inheritdoc />
    public EngineInfo Info => _info;

    /// <inheritdoc />
    public RenderStats Stats => _stats;

    /// <inheritdoc />
    public int DeviceGeneration => _deviceGeneration;

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
    internal VulkanRenderTarget SceneTarget => _sceneTarget ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the current runtime render target, for GPU-test readback probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <param name="targetId">The render texture target id.</param>
    /// <param name="target">The live target, or <see langword="null"/> when the id is not live.</param>
    /// <returns><see langword="true"/> when the target is live.</returns>
    internal bool TryGetRenderTarget(long targetId, out VulkanRenderTarget? target) {
        if (_renderTargets.TryGetValue(targetId, out RenderTargetEntry? entry)) {
            target = entry.Target;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>
    /// Gets the current swapchain, for GPU-test presented-pixel probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanSwapchain Swapchain => _swapchain ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the swapchain image index of the most recent successful present, for GPU-test
    /// presented-pixel probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only. The value is
    /// <see cref="uint.MaxValue"/> until the first successful present, and it may be stale after the
    /// presentation path is recreated because a new swapchain can reuse the same indices.
    /// </remarks>
    internal uint LastPresentedImageIndex => _lastPresentedImageIndex;

    /// <summary>
    /// Gets the swapchain image index of the submitted frame that is waiting for presentation, for
    /// GPU-test presented-pixel probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only. The value is
    /// <see cref="uint.MaxValue"/> when no frame is waiting for presentation. A probe must read the
    /// image before <c>vkQueuePresentKHR</c> hands it to the presentation engine, so it uses this
    /// index and then presents; <see cref="LastPresentedImageIndex"/> confirms the index afterwards.
    /// </remarks>
    internal uint PendingPresentImageIndex => _pendingPresent ? _pendingImageIndex : uint.MaxValue;

    /// <summary>
    /// Gets the Vulkan instance, for GPU-test validation checks.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanInstance Instance => _instance ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the bindless texture table, for GPU-test slot and white-texture probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanBindlessTextureTable BindlessTextures => _bindlessTextures
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the geometry pipeline cache, for GPU-test pipeline-count probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanGeometryPipelineCache GeometryPipelines => _geometryPipelines
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the per-frame-in-flight geometry resources, for GPU-test buffer probes.
    /// </summary>
    /// <remarks>
    /// This is diagnostic access, not part of the renderer seam. Render-thread only.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanFrameResources FrameResources => _frameResources
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Creates the GPU asset worker for the backend's current device.
    /// </summary>
    /// <remarks>
    /// Render-thread only. The render loop calls this once after <see cref="Initialize"/> and attaches
    /// the worker to the GPU asset bridge. The worker resolves the current device, generation,
    /// bindless table, and frame idleness on every upload, restore, and destroy, so it stays valid
    /// across a device recreation and the render loop never replaces it. The worker must not be used
    /// after the backend is disposed.
    /// </remarks>
    /// <returns>The worker for the current device.</returns>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    public IAssetGpuWorker CreateAssetGpuWorker() {
        EnsureInitialized();
        VulkanAssetWorker worker = new(
            () => Device,
            () => DeviceGeneration,
            () => BindlessTextures,
            AreFrameResourcesIdle,
            WaitForFrameResourcesIdle,
            _options!.Logger);
        _assetGpuWorker = worker;
        return worker;
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A render target image, view, sampler, or bindless slot could not be created. The diagnostic code
    /// is <c>RB3001</c>.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed creation completes its request with the failure and keeps the render loop alive; a creation failure must never escape the render thread.")]
    public void ProcessRenderTextureRequests(RenderTextureService service) {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(service);

        foreach (RenderTextureService.CreateRequest request in service.DrainCreates()) {
            try {
                CreateRenderTextureTarget(service, request, request.Description);
            } catch (Exception exception) {
                request.DisposeRegistration();
                request.Completion.TrySetException(exception);
            }
        }

        foreach (RenderTextureService.ResizeRequest request in service.DrainResizes()) {
            try {
                ResizeRenderTextureTarget(request);
            } catch (Exception exception) {
                request.Completion.TrySetException(exception);
            }
        }

        foreach (RenderTextureTargetLifetime lifetime in service.DrainDestroys()) {
            if (!_renderTargets.TryGetValue(lifetime.TargetId, out RenderTargetEntry? entry)) {
                continue;
            }

            if (!IsRenderTargetIdle(lifetime)) {
                service.RequeueDestruction(lifetime);
                continue;
            }

            _renderTargets.Remove(entry.Id);
            DestroyRenderTarget(entry, retireView: true);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed request cleans up its partially created GPU objects and reports the failure through the request instead of escaping the render thread.")]
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The render texture and its target are published to the request's completion task or destroyed by the finally; the analyzer cannot model the transfer of ownership through the task source.")]
    private void CreateRenderTextureTarget(
        RenderTextureService service,
        RenderTextureService.CreateRequest request,
        RenderTextureDescription description) {
        if (request.Completion.Task.IsCompleted) {
            request.DisposeRegistration();
            return;
        }

        VulkanDevice device = _device!;
        PhysicalDeviceProperties properties = device.Vulkan.GetPhysicalDeviceProperties(device.PhysicalDevice);
        if (description.Width > properties.Limits.MaxImageDimension2D
            || description.Height > properties.Limits.MaxImageDimension2D) {
            throw new ArgumentOutOfRangeException(
                nameof(description),
                description,
                $"The {description.Width}x{description.Height} render texture exceeds the device's "
                    + $"maximum 2D image dimension {properties.Limits.MaxImageDimension2D}.");
        }

        int sampleCount = RenderTextureValidation.SelectSampleCount(
            description.SampleCount,
            device.Capabilities.SupportedSampleCounts);
        if (sampleCount != description.SampleCount) {
            _options!.Logger.LogWarning(
                "The requested render texture MSAA sample count {Requested} is not supported by the device; using {Effective}.",
                description.SampleCount,
                sampleCount);
        }

        Format format = ToVulkanColorFormat(description.ColorFormat);
        VulkanRenderTarget target = VulkanRenderTarget.Create(
            device,
            new Extent2D { Width = checked((uint)description.Width), Height = checked((uint)description.Height) },
            sampleCount,
            format,
            description.Depth);
        Sampler sampler;
        Sampler depthSampler;
        uint slot;
        uint depthSlot;
        try {
            CreateRenderTargetViewBindings(device, target, out sampler, out slot, out depthSampler, out depthSlot);
        } catch {
            target.Dispose();
            throw;
        }

        RenderTargetEntry? entry = null;
        bool success = false;
        try {
            long targetId = service.NextTargetId();
            RenderTextureTargetLifetime lifetime = new(targetId, description, sampleCount, service.QueueDestruction);
            RenderTextureViewResource viewResource = new(target.ResolvedColorView, sampler) {
                BindlessSlot = slot,
            };
            RenderTextureViewLifetime viewLifetime = new(lifetime, viewResource);
            RenderTextureViewResource? depthViewResource = null;
            RenderTextureViewLifetime? depthViewLifetime = null;
            Texture? depthTexture = null;
            if (target.HasDepth) {
                depthViewResource = new RenderTextureViewResource(target.ResolvedDepthView, depthSampler) {
                    BindlessSlot = depthSlot,
                };
                depthViewLifetime = new RenderTextureViewLifetime(lifetime, depthViewResource);
                TextureAssetDescriptor depthDescriptor = new() {
                    Payload = string.Empty,
                    Width = checked((uint)description.Width),
                    Height = checked((uint)description.Height),
                    MipLevels = 1,
                    Format = TextureFormat.Depth32Float,
                    IsSrgb = false,
                    Filter = TextureFilter.Nearest,
                    Wrap = TextureWrap.Clamp,
                    MaxAnisotropy = 1f,
                };
                depthTexture = new Texture(depthViewLifetime, depthDescriptor, isProcedural: false, isRenderTargetView: true);
            }

            TextureAssetDescriptor descriptor = new() {
                Payload = string.Empty,
                Width = checked((uint)description.Width),
                Height = checked((uint)description.Height),
                MipLevels = 1,
                Format = description.ColorFormat,
                IsSrgb = description.ColorFormat == TextureFormat.Rgba8Srgb,
                Filter = TextureFilter.Linear,
                Wrap = TextureWrap.Clamp,
                MaxAnisotropy = 1f,
            };
            Texture colorTexture = new(viewLifetime, descriptor, isProcedural: false, isRenderTargetView: true);
            RenderTexture renderTexture = new(lifetime, colorTexture, depthTexture, service);
            entry = new RenderTargetEntry(
                targetId,
                sampleCount,
                target,
                lifetime,
                viewLifetime,
                viewResource,
                sampler,
                depthViewLifetime,
                depthViewResource,
                depthSampler);
            _renderTargets.Add(targetId, entry);
            success = request.Completion.TrySetResult(renderTexture);
            if (!success) {
                _renderTargets.Remove(targetId);
            }
        } finally {
            request.DisposeRegistration();
            if (!success) {
                if (entry is not null) {
                    DestroyRenderTarget(entry, retireView: true);
                } else {
                    DestroyRenderTargetViewBindings(device, sampler, slot, depthSampler, depthSlot);
                    target.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Recreates one live render target's images, render pass, framebuffer, bindless views, and pipeline
    /// cache at the request's size, then updates the target's retained description and public dimensions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Render-thread only. The request is processed before the next frame is acquired, so the tick thread
    /// that called <see cref="RenderTexture.Resize"/> is blocked and cannot record another frame while the
    /// target is recreated. A frame hold is never waited on: a target that a published but not yet
    /// rendered frame still holds fails the request with <see cref="InvalidOperationException"/> instead of
    /// deadlocking the render loop. The last-use fence is waited on because a frame that was already
    /// released can still be in flight on the GPU; the new images are not referenced by it.
    /// </para>
    /// <para>
    /// The new target is fully created, and its samplers exist, before any live entry is changed; a
    /// failure before that point leaves the old target usable and releases the partial new resources. The
    /// target keeps its bindless slots and the descriptors are rewritten to the new views, so a resize
    /// cannot exhaust the bindless table and never leaves a stale slot. The old pipeline cache is
    /// destroyed before the old render pass, and the old target after the new resources are live.
    /// </para>
    /// </remarks>
    /// <param name="request">The queued resize request.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The new size exceeds the current device's maximum 2D image dimension.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A recorded or in-flight frame holds the target, or the target is no longer live.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The render texture was disposed before the request ran.</exception>
    /// <exception cref="GraphicsException">The renderer could not recreate the target's GPU objects.</exception>
    private void ResizeRenderTextureTarget(RenderTextureService.ResizeRequest request) {
        RenderTextureTargetLifetime lifetime = request.Lifetime;
        if (lifetime.IsDisposed) {
            throw new ObjectDisposedException(
                nameof(RenderTexture),
                "The render texture is disposed; it cannot be resized.");
        }

        if (!_renderTargets.TryGetValue(request.TargetId, out RenderTargetEntry? entry)) {
            throw new InvalidOperationException(
                $"The frame references render target {request.TargetId}, which is no longer alive. A resize "
                    + "cannot recreate a target that was destroyed.");
        }

        if (lifetime.IsInUse) {
            throw new InvalidOperationException(
                $"Render target {request.TargetId} is in use by a recorded or in-flight frame; the resize "
                    + "was not applied. Resize the render texture only after the frame that uses it completed.");
        }

        // The public Resize no-ops an unchanged size before it queues; keep the render thread idempotent
        // in case a second request reaches it for the same size.
        RenderTextureDescription description = lifetime.Description;
        if (description.Width == request.Width && description.Height == request.Height) {
            request.Completion.TrySetResult();
            return;
        }

        VulkanDevice device = _device!;
        PhysicalDeviceProperties properties = device.Vulkan.GetPhysicalDeviceProperties(device.PhysicalDevice);
        uint maxDimension = properties.Limits.MaxImageDimension2D;
        if (request.Width > maxDimension || request.Height > maxDimension) {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"{nameof(RenderTexture.Resize)} cannot resize render target {request.TargetId} to "
                    + $"{request.Width}x{request.Height}: it exceeds the device's maximum 2D image dimension "
                    + $"{maxDimension}. The target keeps its {description.Width}x{description.Height} images.");
        }

        WaitForRenderTargetIdle(lifetime);

        VulkanRenderTarget target = VulkanRenderTarget.Create(
            device,
            new Extent2D { Width = checked((uint)request.Width), Height = checked((uint)request.Height) },
            entry.SampleCount,
            ToVulkanColorFormat(description.ColorFormat),
            description.Depth);
        Sampler sampler = default;
        Sampler depthSampler = default;
        try {
            sampler = CreateRenderTargetSampler(device, TextureFilter.Linear);
            if (target.HasDepth) {
                depthSampler = CreateRenderTargetSampler(device, TextureFilter.Nearest);
            }
        } catch {
            DestroyRenderTargetSampler(device, sampler);
            DestroyRenderTargetSampler(device, depthSampler);
            target.Dispose();
            throw;
        }

        // Every resource the new target needs is live, so the swap below only assigns fields and
        // rewrites descriptors; it cannot fail and leave the entry pointing at a half-created target.
        VulkanRenderTarget previousTarget = entry.Target;
        Sampler previousSampler = entry.Sampler;
        Sampler previousDepthSampler = entry.DepthSampler;
        VulkanGeometryPipelineCache? previousPipelines = entry.Pipelines;
        entry.Target = target;
        entry.Sampler = sampler;
        entry.ViewResource.View = target.ResolvedColorView;
        entry.ViewResource.Sampler = sampler;
        entry.ViewLifetime.SetBindingResource(entry.ViewResource);
        _bindlessTextures!.WriteTexture(entry.ViewResource.BindlessSlot, target.ResolvedColorView, sampler);
        if (target.HasDepth && entry.DepthViewResource is not null) {
            entry.DepthSampler = depthSampler;
            entry.DepthViewResource.View = target.ResolvedDepthView;
            entry.DepthViewResource.Sampler = depthSampler;
            entry.DepthViewLifetime!.SetBindingResource(entry.DepthViewResource);
            _bindlessTextures.WriteTexture(entry.DepthViewResource.BindlessSlot, target.ResolvedDepthView, depthSampler);
        }

        entry.Pipelines = null;
        lifetime.UpdateDescription(description with { Width = request.Width, Height = request.Height });
        // The new images were never used by a frame; the previous images' last-use fence was waited on.
        lifetime.SetLastUseFrameSlot(-1);

        previousPipelines?.Dispose();
        previousTarget.Dispose();
        DestroyRenderTargetSampler(device, previousSampler);
        DestroyRenderTargetSampler(device, previousDepthSampler);
        request.Completion.TrySetResult();
    }

    /// <summary>
    /// Creates the color and, when the target has depth, depth sampler and bindless slot and writes their
    /// descriptors into every frame descriptor set.
    /// </summary>
    /// <remarks>
    /// A failure releases the slots and samplers created so far before it rethrows, so the caller only has
    /// to dispose the target. The color slot is reserved first and the depth slot second, matching the
    /// entry's view order.
    /// </remarks>
    /// <param name="device">The current logical device.</param>
    /// <param name="target">The created target whose resolved views are sampled.</param>
    /// <param name="sampler">The color view's sampler.</param>
    /// <param name="slot">The color view's bindless slot.</param>
    /// <param name="depthSampler">The depth view's sampler, or a default handle for a depthless target.</param>
    /// <param name="depthSlot">The depth view's bindless slot, or <c>0</c> for a depthless target.</param>
    /// <exception cref="GraphicsException">
    /// The bindless texture table is full. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    private void CreateRenderTargetViewBindings(
        VulkanDevice device,
        VulkanRenderTarget target,
        out Sampler sampler,
        out uint slot,
        out Sampler depthSampler,
        out uint depthSlot) {
        sampler = default;
        slot = 0;
        depthSampler = default;
        depthSlot = 0;
        try {
            sampler = CreateRenderTargetSampler(device, TextureFilter.Linear);
            if (!_bindlessTextures!.TryAllocate(out slot)) {
                throw new GraphicsException(
                    $"The bindless texture table is full ({_bindlessTextures.Capacity} slots, including "
                        + "the reserved white slot), so the render texture color view has no slot.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            _bindlessTextures.WriteTexture(slot, target.ResolvedColorView, sampler);

            if (target.HasDepth) {
                depthSampler = CreateRenderTargetSampler(device, TextureFilter.Nearest);
                if (!_bindlessTextures.TryAllocate(out depthSlot)) {
                    throw new GraphicsException(
                        $"The bindless texture table is full ({_bindlessTextures.Capacity} slots, including "
                            + "the reserved white slot), so the render texture depth view has no slot.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null);
                }

                _bindlessTextures.WriteTexture(depthSlot, target.ResolvedDepthView, depthSampler);
            }
        } catch {
            DestroyRenderTargetViewBindings(device, sampler, slot, depthSampler, depthSlot);
            throw;
        }
    }

    /// <summary>
    /// Releases a color and depth bindless slot and destroys their samplers. Safe with default handles.
    /// </summary>
    /// <param name="device">The device that owns the samplers.</param>
    /// <param name="sampler">The color sampler, or a default handle.</param>
    /// <param name="slot">The color bindless slot, or <c>0</c>.</param>
    /// <param name="depthSampler">The depth sampler, or a default handle.</param>
    /// <param name="depthSlot">The depth bindless slot, or <c>0</c>.</param>
    private void DestroyRenderTargetViewBindings(
        VulkanDevice device,
        Sampler sampler,
        uint slot,
        Sampler depthSampler,
        uint depthSlot) {
        if (slot != 0 && _bindlessTextures is not null) {
            _bindlessTextures.Release(slot);
        }

        if (depthSlot != 0 && _bindlessTextures is not null) {
            _bindlessTextures.Release(depthSlot);
        }

        DestroyRenderTargetSampler(device, sampler);
        DestroyRenderTargetSampler(device, depthSampler);
    }

    /// <summary>
    /// Destroys one render-target sampler when it has a handle. Safe with a default handle.
    /// </summary>
    /// <param name="device">The device that owns the sampler.</param>
    /// <param name="sampler">The sampler to destroy.</param>
    private static void DestroyRenderTargetSampler(VulkanDevice device, Sampler sampler) {
        if (sampler.Handle != 0) {
            device.Vulkan.DestroySampler(device.Device, sampler, null);
        }
    }

    private static Format ToVulkanColorFormat(TextureFormat format) => format switch {
        TextureFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
        TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        TextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
        _ => throw new ArgumentOutOfRangeException(
            nameof(format),
            format,
            "The format is not a supported render texture color format."),
    };

    private static Sampler CreateRenderTargetSampler(VulkanDevice device, TextureFilter filter) {
        bool nearest = filter == TextureFilter.Nearest;
        SamplerCreateInfo createInfo = new() {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = nearest ? Filter.Nearest : Filter.Linear,
            MinFilter = nearest ? Filter.Nearest : Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MipLodBias = 0f,
            AnisotropyEnable = false,
            MaxAnisotropy = 1f,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MinLod = 0f,
            MaxLod = 0f,
            BorderColor = BorderColor.FloatOpaqueBlack,
            UnnormalizedCoordinates = false,
        };
        Result result = device.Vulkan.CreateSampler(device.Device, &createInfo, null, out Sampler sampler);
        ThrowIfFailed(result, "vkCreateSampler");
        return sampler;
    }

    private RenderTargetEntry RequireRenderTarget(long targetId) {
        if (_renderTargets.TryGetValue(targetId, out RenderTargetEntry? entry)) {
            return entry;
        }

        throw new GraphicsException(
            $"The frame references render target {targetId}, which is no longer alive. A render texture "
                + "must stay alive until every recorded frame that writes or samples it completes.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    /// <summary>
    /// Gets whether the frame slot that last used the target signaled its fence, so a destroy or resize
    /// can safely release or replace the target images. Render-thread only.
    /// </summary>
    /// <param name="lifetime">The target lifetime to check.</param>
    /// <returns><see langword="true"/> when the last-use fence signaled or the target was never used.</returns>
    private bool IsRenderTargetIdle(RenderTextureTargetLifetime lifetime) {
        if (_device is null || _fences.Length == 0) {
            return true;
        }

        int frameSlot = lifetime.LastUseFrameSlot;
        if (frameSlot < 0 || frameSlot >= _fences.Length) {
            return true;
        }

        return _device.Vulkan.GetFenceStatus(_device.Device, _fences[frameSlot]) == Result.Success;
    }

    /// <summary>
    /// Waits for the frame slot that last used the target to signal its fence. Render-thread only.
    /// </summary>
    /// <remarks>
    /// A replacement target must not be created while the GPU still reads or writes the old images. The
    /// wait is bounded by the frame's GPU completion; the frame hold check has already rejected a target a
    /// recorded but not yet rendered frame still references, which is the only case that could deadlock.
    /// </remarks>
    /// <param name="lifetime">The target lifetime to wait for.</param>
    private void WaitForRenderTargetIdle(RenderTextureTargetLifetime lifetime) {
        if (_device is null || _fences.Length == 0) {
            return;
        }

        int frameSlot = lifetime.LastUseFrameSlot;
        if (frameSlot < 0 || frameSlot >= _fences.Length) {
            return;
        }

        Fence fence = _fences[frameSlot];
        ThrowIfFailed(
            _device.Vulkan.WaitForFences(_device.Device, 1, &fence, true, WAIT_TIMEOUT),
            "vkWaitForFences");
    }

    private void DestroyRenderTarget(RenderTargetEntry entry, bool retireView) {
        VulkanGeometryPipelineCache? pipelines = entry.Pipelines;
        entry.Pipelines = null;
        pipelines?.Dispose();
        if (entry.ViewResource.BindlessSlot != 0 && _bindlessTextures is not null) {
            _bindlessTextures.Release(entry.ViewResource.BindlessSlot);
            entry.ViewResource.BindlessSlot = 0;
        }

        if (entry.DepthViewResource is not null
            && entry.DepthViewResource.BindlessSlot != 0
            && _bindlessTextures is not null) {
            _bindlessTextures.Release(entry.DepthViewResource.BindlessSlot);
            entry.DepthViewResource.BindlessSlot = 0;
        }

        if (entry.Sampler.Handle != 0 && _device is not null) {
            _device.Vulkan.DestroySampler(_device.Device, entry.Sampler, null);
            entry.Sampler = default;
        }

        if (entry.DepthSampler.Handle != 0 && _device is not null) {
            _device.Vulkan.DestroySampler(_device.Device, entry.DepthSampler, null);
            entry.DepthSampler = default;
        }

        if (retireView) {
            entry.ViewLifetime.Retire();
            entry.DepthViewLifetime?.Retire();
            // The backend retired the target without a public Dispose (shutdown, or a canceled
            // creation). The public object must report IsDisposed even though no destroy request ran.
            entry.Lifetime.MarkDestroyed();
        }

        entry.Target.Dispose();
    }

    private void DestroyRenderTargets() {
        foreach (RenderTargetEntry entry in _renderTargets.Values) {
            DestroyRenderTarget(entry, retireView: true);
        }

        _renderTargets.Clear();
        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
    }

    /// <summary>
    /// Destroys every live render texture's GPU images, bindless slot, sampler, and pipeline cache while
    /// keeping its public view and lifetime so a device recreation can recreate the resources from the
    /// retained description.
    /// </summary>
    private void DestroyRenderTargetGpuResources() {
        foreach (RenderTargetEntry entry in _renderTargets.Values) {
            DestroyRenderTarget(entry, retireView: false);
        }

        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
    }

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
        ArgumentNullException.ThrowIfNull(options.TeardownQueue);
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
    /// A fatal Vulkan failure or a validation error throws with the diagnostic code <c>RB3001</c>.
    /// </exception>
    /// <exception cref="GraphicsDeviceLostException">
    /// A recoverable presentation failure could not be recovered. The diagnostic code is
    /// <c>RB3002</c>.
    /// </exception>
    public void Submit(RenderFramePacket frame) {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(frame);
        int generationBeforeRecovery = _deviceGeneration;
        HandleSimulatedFailure();
        if (_pendingPresent) {
            PresentPending();
        }

        if (_deviceGeneration != generationBeforeRecovery) {
            // The device was recreated for this frame. The asset GPU resources still belong to the
            // destroyed device and the bridge has not recreated them yet, so drawing now would
            // reference stale handles and bindless slots. Drop the frame; the render loop restores
            // every live asset after it observes the new generation and the next frame draws.
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        WindowSurfaceState state = _options!.WindowStateProvider();
        if (state.IsMinimized || state.RenderWidth <= 0 || state.RenderHeight <= 0) {
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (_swapchainOutOfDate) {
            RecoverOrThrow(RenderRecoveryReason.SwapchainOutOfDate);
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
        BeforeAcquireForTests?.Invoke();
        SwapchainResult acquire = _swapchain.Acquire(frameSlot, imageAvailable, out uint imageIndex);
        if (acquire == SwapchainResult.OutOfDate) {
            RecoverOrThrow(RenderRecoveryReason.SwapchainOutOfDate);
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (acquire == SwapchainResult.SurfaceLost) {
            RecoverOrThrow(RenderRecoveryReason.SurfaceLost);
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (acquire == SwapchainResult.Suboptimal) {
            _suboptimalRecoveryPending = true;
            _options.Logger.LogDebug(
                "The swapchain image acquisition was suboptimal; the presentation path is recreated after the present.");
        }

        // CPU frame time covers only planning, recording, and submission. It starts after the fence
        // wait and the swapchain acquire, both of which are blocking waits on the GPU or display, and
        // stops after the queue submission, so it never reports a presentation or acquire stall as
        // render CPU work.
        long startTimestamp = Stopwatch.GetTimestamp();
        FramePlan plan = new BatchPlanner().Plan(frame);
        ulong instanceBytes = ComputeInstanceBytes(plan);
        _frameResources!.EnsureFrameCapacity((int)frameSlot, plan.Passes.Count, frame.FrameIndex);
        _frameResources!.EnsureSlot((int)frameSlot, instanceBytes, frame.FrameIndex);

        CommandBuffer commandBuffer = _commandBuffers[frameSlot];
        ThrowIfFailed(vulkan.ResetCommandBuffer(commandBuffer, 0), "vkResetCommandBuffer");
        CommandBufferBeginInfo beginInfo = new() {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        ThrowIfFailed(vulkan.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");

        HashSet<ulong> pipelines = [];
        RecordScenePass(
            vulkan,
            commandBuffer,
            plan,
            frame,
            (int)frameSlot,
            pipelines);

        RenderSettings settings = _options.Settings;

        // The present pass is frame-global and uses the frame's tone-mapping snapshot, not the live
        // runtime settings object: the render thread never reads the game's settings.
        TonemappingSnapshot tonemapping = frame.Tonemapping;
        float paperWhite = state.Hdr.HdrEnabled ? state.Hdr.SdrWhiteLevel : 1f;
        PresentEncoding encoding = _swapchainFormat.Encoding;
        float outputMax = encoding == PresentEncoding.ScRgbHdr && state.Hdr.HdrEnabled
            ? state.Hdr.HdrHeadroom
            : 1f;
        AgxCurveParameters agx = AgxCurveParameters.Create(tonemapping.AgxWhite, tonemapping.AgxContrast, outputMax);
        PresentPushConstants pushConstants = new() {
            ExposureMultiplier = MathF.Pow(2f, tonemapping.Exposure),
            HdrWhiteScale = paperWhite,
            OutputMaxValue = (float)agx.OutputMax,
            AwpContrast = (float)agx.Contrast,
            AwpToeA = (float)agx.ToeA,
            AwpSlope = (float)agx.Slope,
            AwpW = (float)agx.W,
            Tonemapping = settings.Tonemapping == TonemappingMode.AgX ? 0 : 1,
            Encoding = (int)encoding,
            Reserved = 0,
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
            _presentedFrames,
            plan.DrawCalls,
            plan.Instances,
            plan.Triangles,
            plan.CulledInstances,
            pipelines.Count,
            _bindlessTextures!.LiveCount,
            _sampleCount,
            _swapchainFormat.IsHdr,
            cpuMilliseconds,
            0,
            CountFramesInFlight(),
            _recoveryCount);
        _instance!.ThrowIfValidationError("submitting a frame");
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A validation error was recorded while presenting. The diagnostic code is <c>RB3001</c>.
    /// </exception>
    /// <exception cref="GraphicsDeviceLostException">
    /// A recoverable presentation failure could not be recovered. The diagnostic code is
    /// <c>RB3002</c>.
    /// </exception>
    public void Present() {
        EnsureInitialized();
        HandleSimulatedFailure();
        PresentPending();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The call waits for the device to idle and drops the pending present before it destroys any
    /// resource, so no in-flight frame is partially presented. It recreates the affected presentation
    /// path, surface, or device and publishes the new <see cref="Info"/> and
    /// <see cref="RenderStats.RecoveryCount"/> before it returns. A presentation-only recovery keeps
    /// the bindless texture table, the frame resources, and the descriptor sets because the logical
    /// device did not change; a device recreation rebuilds all of them.
    /// </para>
    /// <para>
    /// The call is bounded: more than <see cref="MAX_CONSECUTIVE_RECOVERIES"/> attempts without a
    /// completed frame, an injected unrecoverable fault, a platform failure while recreating the
    /// surface, or an unexpected exception returns <see langword="false"/>. Every field is cleared
    /// before its resource is destroyed, so a later <see cref="Dispose"/> is safe even after a failed
    /// recovery. Recovery never throws for a recoverable failure; it throws only when validation
    /// reported an error.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsException">
    /// The Vulkan validation layer reported an error while recovering. The diagnostic code is
    /// <c>RB3001</c>.
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Device recovery reports the failure through its return value and statistics instead of crashing the render thread.")]
    public bool Recover(RenderRecoveryReason reason) {
        EnsureInitialized();
        if (_consecutiveRecoveries >= MAX_CONSECUTIVE_RECOVERIES) {
            _options!.Logger.LogError(
                "The renderer already attempted {Limit} consecutive recoveries without completing a frame; the failure {Reason} is fatal.",
                MAX_CONSECUTIVE_RECOVERIES,
                reason);
            return false;
        }

        if (_unrecoverableFailureForTests) {
            _unrecoverableFailureForTests = false;
            _options!.Logger.LogError(
                "The renderer cannot recover from {Reason} because an unrecoverable failure was injected.",
                reason);
            return false;
        }

        _consecutiveRecoveries++;
        try {
            WindowSurfaceState state = _options!.WindowStateProvider();
            switch (reason) {
                case RenderRecoveryReason.SwapchainOutOfDate:
                    RecreatePresentation(state, LogLevel.Warning, "an out-of-date swapchain");
                    break;
                case RenderRecoveryReason.SwapchainSuboptimal:
                    RecreatePresentation(state, LogLevel.Information, "a suboptimal swapchain");
                    break;
                case RenderRecoveryReason.SurfaceLost:
                    if (!RecreateSurface(state)) {
                        return false;
                    }

                    break;
                case RenderRecoveryReason.DeviceLost:
                    RecreateDevice(state);
                    break;
                default:
                    _options.Logger.LogError("The renderer cannot recover from the unknown reason {Reason}.", reason);
                    return false;
            }
        } catch (Exception exception) {
            _options!.Logger.LogError(exception, "The renderer recovery from {Reason} failed.", reason);
            return false;
        }

        _recoveryCount++;
        _stats = _stats with { RecoveryCount = _recoveryCount };
        string operation = reason switch {
            RenderRecoveryReason.SwapchainSuboptimal => "recovering the presentation path",
            RenderRecoveryReason.SurfaceLost => "recovering the surface",
            RenderRecoveryReason.DeviceLost => "recovering the device",
            _ => "recovering the presentation path",
        };
        _instance!.ThrowIfValidationError(operation);
        return true;
    }

    /// <summary>
    /// Marks the device as lost so the next <see cref="Submit"/> or <see cref="Present"/> runs the
    /// device-loss recovery path. Test-only; it is not part of the renderer seam.
    /// </summary>
    /// <remarks>
    /// The request is consumed by the next frame call and routed through <see cref="Recover"/>, so a
    /// test exercises the same logic a driver-detected device loss uses.
    /// </remarks>
    internal void SimulateDeviceLossForTests() => _simulatedFailure = RenderRecoveryReason.DeviceLost;

    /// <summary>
    /// Marks the surface as lost so the next <see cref="Submit"/> or <see cref="Present"/> runs the
    /// surface-loss recovery path. Test-only; it is not part of the renderer seam.
    /// </summary>
    /// <remarks>
    /// The request is consumed by the next frame call and routed through <see cref="Recover"/>, so a
    /// test exercises the same logic a driver-detected surface loss uses.
    /// </remarks>
    internal void SimulateSurfaceLossForTests() => _simulatedFailure = RenderRecoveryReason.SurfaceLost;

    /// <summary>
    /// Makes the next recovery return <see langword="false"/> without touching GPU state. Test-only;
    /// it is not part of the renderer seam.
    /// </summary>
    /// <remarks>
    /// When no loss is pending, the next <see cref="Submit"/> or <see cref="Present"/> requests a
    /// device-loss recovery, so a test reaches the fatal path with one call.
    /// </remarks>
    internal void SimulateUnrecoverableFailureForTests() {
        _unrecoverableFailureForTests = true;
        _simulatedFailure ??= RenderRecoveryReason.DeviceLost;
    }

    /// <summary>
    /// Sets the out-of-date flag so the next <see cref="Submit"/> recreates the presentation path
    /// through <see cref="Recover"/>. Test-only; it is not part of the renderer seam.
    /// </summary>
    internal void SimulateSwapchainOutOfDateForTests() => _swapchainOutOfDate = true;

    /// <summary>
    /// Gets or sets a hook invoked immediately before the swapchain acquire. Test-only; it is not
    /// part of the renderer seam. The CPU-time test uses it to hold the acquire long enough to prove
    /// that <see cref="RenderStats.CpuFrameMilliseconds"/> excludes the wait.
    /// </summary>
    internal Action? BeforeAcquireForTests { get; set; }

    /// <summary>
    /// Gets whether no submitted frame can still reference a GPU asset resource.
    /// </summary>
    /// <remarks>
    /// Render-thread only. The asset GPU worker polls this before the bridge destroys a resource, so
    /// a texture or mesh destroyed after the tick that drew it stays alive until every frame fence
    /// signals. A device with no fences, or a disposed or lost device, is reported as idle so teardown
    /// can proceed.
    /// </remarks>
    /// <returns><see langword="true"/> when every frame slot fence is signaled.</returns>
    internal bool AreFrameResourcesIdle() {
        if (_device is null || _fences.Length == 0) {
            return true;
        }

        Vk vulkan = _device.Vulkan;
        foreach (Fence fence in _fences) {
            Result result = vulkan.GetFenceStatus(_device.Device, fence);
            if (result == Result.NotReady) {
                return false;
            }

            if (result != Result.Success) {
                // A lost device cannot retire its fences. Reporting "not idle" keeps resources alive
                // until the recovery path recreates the device and drops them with it.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Waits until every frame slot fence signals. Diagnostic and teardown helper; render-thread only.
    /// </summary>
    /// <remarks>
    /// The render loop calls the worker's wait before the bridge destroys live resources. A failure is
    /// logged and returns instead of throwing, because shutdown must still release what it can.
    /// </remarks>
    internal void WaitForFrameResourcesIdle() {
        if (_device is null || _fences.Length == 0) {
            return;
        }

        fixed (Fence* fences = _fences) {
            Result result = _device.Vulkan.WaitForFences(
                _device.Device,
                (uint)_fences.Length,
                fences,
                true,
                WAIT_TIMEOUT);
            if (result != Result.Success) {
                _options?.Logger.LogWarning(
                    "The renderer could not wait for frame idleness before destroying GPU assets: {Result}.",
                    result);
            }
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
    /// Waits for the device to idle and destroys the GPU objects, the swapchain, the device, and the
    /// surface on the render thread, then queues the instance and loader for main-thread teardown.
    /// The call is idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        DestroyRenderTargets();
        _presentPipeline?.Dispose();
        _presentPipeline = null;
        _sceneTarget?.Dispose();
        _sceneTarget = null;
        _frameResources?.Dispose();
        _frameResources = null;
        DestroyAssetGpuResources();
        _bindlessTextures?.Dispose();
        _bindlessTextures = null;
        _descriptorState?.Dispose();
        _descriptorState = null;
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

        VulkanInstance? instance = _instance;
        _instance = null;
        if (instance is not null) {
            instance.Dispose();
            if (_options is not null) {
                _options.TeardownQueue.Enqueue(instance.DisposeFromMainThread);
            }
        }

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
        _deviceGeneration = 1;
        VulkanDeviceCapabilities capabilities = _device.Capabilities;
        WindowSurfaceState state = options.WindowStateProvider();
        _requestedWidth = state.RenderWidth;
        _requestedHeight = state.RenderHeight;
        _sampleCount = VulkanSampleCountSelection.Select(options.Settings.SampleCount, capabilities.SupportedSampleCounts);
        CreatePresentationPath(state);
        CreateGeometryResources();
        CreateFrameResources(_swapchain!.ImageCount);
        UpdateInfo();
        LogInitialized(capabilities, state, _swapchain.Extent);
        LogSelectionWarnings(state);
        _initialized = true;
        _instance.ThrowIfValidationError("initializing the renderer");
    }

    /// <summary>
    /// Creates the swapchain, scene target, and present pipeline from scratch for the current device
    /// and surface.
    /// </summary>
    /// <remarks>
    /// Used at startup and after a device or surface loss, when no previous swapchain can be reused.
    /// The caller has selected the sample count from the current device capabilities and must create
    /// the geometry and frame resources after this call. Render-thread only; the device must be idle.
    /// </remarks>
    /// <param name="state">The current window surface snapshot.</param>
    private void CreatePresentationPath(WindowSurfaceState state) {
        VulkanDevice device = _device!;
        RendererBackendOptions options = _options!;
        VulkanDeviceCapabilities capabilities = device.Capabilities;
        _selectedHdrState = state.Hdr;
        _swapchainFormat = VulkanSwapchainFormatSelection.Select(capabilities.SurfaceFormats, state.Hdr);
        _presentMode = VulkanPresentModeSelection.Select(
            capabilities.Swapchain.PresentModes,
            options.Settings.PresentMode);
        VulkanSwapchainSupport support = capabilities.Swapchain;
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount);
        _swapchain = VulkanSwapchain.Create(
            device,
            _surface,
            _swapchainFormat,
            _presentMode.Native,
            (int)imageCount,
            extent);
        extent = _swapchain.Extent;
        _sceneTarget = VulkanRenderTarget.Create(device, extent, _sampleCount, Format.R16G16B16A16Sfloat, depth: true);
        _presentPipeline = VulkanPresentPipeline.Create(
            device,
            extent,
            _swapchainFormat.Format,
            _sceneTarget.ResolvedColorView);
        _presentPipeline.RecreateFramebuffers(extent, _swapchain.ImageViews);
        _swapchainOutOfDate = false;
        _suboptimalRecoveryPending = false;
    }

    /// <summary>
    /// Creates the per-device geometry resources: the shared descriptor state, the frame-in-flight
    /// buffers and sets, the bindless texture table with its white slot, and the geometry pipeline
    /// cache.
    /// </summary>
    /// <remarks>Render-thread only; the device must exist and the scene target must be created.</remarks>
    private void CreateGeometryResources() {
        VulkanDevice device = _device!;
        VulkanDeviceCapabilities capabilities = device.Capabilities;
        _descriptorState = VulkanDescriptorState.Create(
            device,
            capabilities.BindlessTextureCapacity,
            FRAMES_IN_FLIGHT);
        _frameResources = VulkanFrameResources.Create(device, _descriptorState);
        _bindlessTextures = new VulkanBindlessTextureTable(capabilities.BindlessTextureCapacity, _options!.Logger);
        _bindlessTextures.Initialize(device, _descriptorState, _frameResources);
        CreateGeometryPipelines();
        _options.Logger.LogInformation(
            "Bindless texture table: {Capacity} slot(s) (device limits: per-stage update-after-bind sampled images {StageLimit}, per-set {SetLimit}, per-stage samplers {SamplerLimit}); instance storage-buffer bound {InstanceBound} byte(s).",
            capabilities.BindlessTextureCapacity,
            capabilities.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            capabilities.MaxDescriptorSetUpdateAfterBindSampledImages,
            capabilities.MaxPerStageDescriptorUpdateAfterBindSamplers,
            _frameResources.MaxInstanceBytes);
    }

    /// <summary>
    /// Creates the geometry pipeline cache for the current scene render pass and sample count.
    /// </summary>
    /// <remarks>
    /// Render-thread only. The caller disposes the previous cache before the scene render pass is
    /// destroyed. The cache snapshots the device's <c>fillModeNonSolid</c> support for the wireframe
    /// model pipelines.
    /// </remarks>
    private void CreateGeometryPipelines() {
        _geometryPipelines = VulkanGeometryPipelineCache.Create(
            _device!,
            _sceneTarget!.RenderPass,
            _descriptorState!.PipelineLayout,
            _sampleCount,
            _deviceGeneration);
    }

    /// <summary>
    /// Destroys the geometry pipelines before the render pass they reference is destroyed.
    /// </summary>
    private void DestroyGeometryPipelines() {
        VulkanGeometryPipelineCache? pipelines = _geometryPipelines;
        _geometryPipelines = null;
        pipelines?.Dispose();
    }

    private void RecreatePresentation(WindowSurfaceState state, LogLevel logLevel, string reason) {
        VulkanDevice device = _device!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        // The scene render pass changes with the scene target, so every cached pipeline is invalid.
        DestroyGeometryPipelines();
        VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(device.Vulkan, device.PhysicalDevice, _surface);
        VulkanSwapchainFormat format = VulkanSwapchainFormatSelection.Select(support.Formats, state.Hdr);
        VulkanPresentModeChoice presentMode = VulkanPresentModeSelection.Select(
            support.PresentModes,
            options.Settings.PresentMode);
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount);
        bool formatChanged = format != _swapchainFormat;

        if (_swapchain is null) {
            _swapchain = VulkanSwapchain.Create(device, _surface, format, presentMode.Native, (int)imageCount, extent);
        } else {
            _swapchain.Recreate(device, _surface, format, presentMode.Native, (int)imageCount, extent);
        }

        extent = _swapchain.Extent;

        VulkanRenderTarget? sceneTarget = _sceneTarget;
        _sceneTarget = null;
        sceneTarget?.Dispose();
        _sceneTarget = VulkanRenderTarget.Create(device, extent, _sampleCount, Format.R16G16B16A16Sfloat, depth: true);
        CreateGeometryPipelines();
        VulkanPresentPipeline? presentPipeline = _presentPipeline;
        if (presentPipeline is null || formatChanged) {
            _presentPipeline = null;
            presentPipeline?.Dispose();
            _presentPipeline = VulkanPresentPipeline.Create(device, extent, format.Format, _sceneTarget.ResolvedColorView);
        } else {
            presentPipeline.WriteDescriptor(_sceneTarget.ResolvedColorView);
        }

        _presentPipeline!.RecreateFramebuffers(extent, _swapchain.ImageViews);
        _swapchainFormat = format;
        _presentMode = presentMode;
        _selectedHdrState = state.Hdr;
        _requestedWidth = state.RenderWidth;
        _requestedHeight = state.RenderHeight;
        RecreateRenderFinishedSemaphores(_swapchain.ImageCount);
        _swapchainOutOfDate = false;
        _suboptimalRecoveryPending = false;
        UpdateInfo();
        LogSelectionWarnings(state);
        options.Logger.Log(
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

    /// <summary>
    /// Recreates the native surface and the presentation path that depends on it.
    /// </summary>
    /// <remarks>
    /// The destination surface is destroyed through the platform before the replacement is created;
    /// the Vulkan swapchain is destroyed first because a surface must outlive its swapchains. A
    /// platform failure is fatal: the method returns <see langword="false"/> with every field that
    /// was destroyed already cleared. Render-thread only; the caller must have waited for the device
    /// to idle.
    /// </remarks>
    /// <param name="state">The current window surface snapshot.</param>
    /// <returns><see langword="true"/> when the surface and presentation path were recreated.</returns>
    private bool RecreateSurface(WindowSurfaceState state) {
        VulkanInstance instance = _instance!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        DestroyPresentationResources();
        IntPtr instanceHandle = instance.Handle.Handle;
        if (_surfaceHandle != IntPtr.Zero) {
            IntPtr lostSurface = _surfaceHandle;
            _surfaceHandle = IntPtr.Zero;
            _surface = default;
            try {
                options.SurfaceProvider.DestroySurface(instanceHandle, lostSurface);
            } catch (GraphicsException exception) {
                options.Logger.LogError(
                    exception,
                    "The lost native Vulkan surface could not be destroyed; the renderer cannot recover.");
                return false;
            }
        }

        try {
            _surfaceHandle = options.SurfaceProvider.CreateSurface(instanceHandle);
        } catch (GraphicsException exception) {
            options.Logger.LogError(
                exception,
                "The native Vulkan surface could not be recreated after surface loss; the renderer cannot recover.");
            return false;
        }

        if (_surfaceHandle == IntPtr.Zero) {
            options.Logger.LogError(
                "The native Vulkan surface could not be recreated after surface loss; the renderer cannot recover.");
            return false;
        }

        _surface = new SurfaceKHR { Handle = unchecked((ulong)_surfaceHandle.ToInt64()) };
        CreatePresentationPath(state);
        CreateGeometryPipelines();
        return true;
    }

    /// <summary>
    /// Recreates the logical device and every device-level resource while keeping the instance and
    /// the surface.
    /// </summary>
    /// <remarks>
    /// The destroyed fields are cleared before their resources are released so that a later
    /// <see cref="Dispose"/> is safe even when creation of the new device fails. Render-thread only.
    /// </remarks>
    /// <param name="state">The current window surface snapshot.</param>
    private void RecreateDevice(WindowSurfaceState state) {
        VulkanInstance instance = _instance!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        DestroyRenderTargetGpuResources();
        DestroyPresentationResources();
        DestroyFrameResources();
        _frameResources?.Dispose();
        _frameResources = null;
        DestroyAssetGpuResources();
        _bindlessTextures?.Reset();
        _bindlessTextures = null;
        _descriptorState?.Dispose();
        _descriptorState = null;
        VulkanDevice? lostDevice = _device;
        _device = null;
        lostDevice?.Dispose();

        _device = VulkanDevice.Create(instance, _surfaceHandle, options.Logger);
        _deviceGeneration++;
        _sampleCount = VulkanSampleCountSelection.Select(
            options.Settings.SampleCount,
            _device.Capabilities.SupportedSampleCounts);
        _requestedWidth = state.RenderWidth;
        _requestedHeight = state.RenderHeight;
        CreatePresentationPath(state);
        CreateGeometryResources();
        CreateFrameResources(_swapchain!.ImageCount);
        RecreateRenderTargets();
        UpdateInfo();
        LogSelectionWarnings(state);
        options.Logger.LogWarning(
            "The Vulkan device was recreated after device loss: {GpuName}, API {ApiVersion}, present mode {PresentMode}, {SampleCount}x MSAA.",
            _device.Capabilities.DeviceName,
            VulkanVersion.Format(_device.Capabilities.ApiVersion),
            _presentMode.Public,
            _sampleCount);
    }

    /// <summary>
    /// Recreates every live render texture's GPU images, sampler, and bindless slot after the logical
    /// device was recreated. The public <see cref="RenderTexture"/> objects keep their identity; only the
    /// render-thread target and view resources are replaced.
    /// </summary>
    /// <remarks>
    /// Render-thread only, with the new device, descriptor state, frame resources, and bindless table
    /// already created. A target whose images belonged to the destroyed device has already been dropped
    /// by <see cref="DestroyRenderTargets"/>, so this method allocates fresh images from the retained
    /// description. A failure is fatal for the render loop.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The recreated target is stored in the live entry or destroyed in the catch; the analyzer cannot model the field transfer.")]
    private void RecreateRenderTargets() {
        if (_renderTargets.Count == 0) {
            return;
        }

        RenderTargetEntry[] entries = [.. _renderTargets.Values];
        foreach (RenderTargetEntry entry in entries) {
            if (!IsRenderTargetIdle(entry.Lifetime)) {
                // The device was recreated after a lost fence; no submitted frame can still reference
                // the destroyed images, so the idle check is best-effort here.
                _options!.Logger.LogDebug(
                    "Recreating render target {TargetId} while its previous frame fence did not signal.",
                    entry.Id);
            }

            VulkanDevice device = _device!;
            RenderTextureDescription description = entry.Lifetime.Description;
            PhysicalDeviceProperties properties = device.Vulkan.GetPhysicalDeviceProperties(device.PhysicalDevice);
            if (description.Width > properties.Limits.MaxImageDimension2D
                || description.Height > properties.Limits.MaxImageDimension2D) {
                throw new GraphicsException(
                    $"Render target {entry.Id} cannot be recreated on the new device: its "
                        + $"{description.Width}x{description.Height} extent exceeds the device's maximum 2D "
                        + $"image dimension {properties.Limits.MaxImageDimension2D}. Dispose and recreate the "
                        + "render texture with a supported size.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null);
            }

            int sampleCount = RenderTextureValidation.SelectSampleCount(
                description.SampleCount,
                device.Capabilities.SupportedSampleCounts);
            if (sampleCount != entry.SampleCount) {
                _options!.Logger.LogWarning(
                    "The render texture sample count changed from {Previous} to {Effective} after the device recreation.",
                    entry.SampleCount,
                    sampleCount);
            }

            VulkanRenderTarget target = VulkanRenderTarget.Create(
                device,
                new Extent2D { Width = checked((uint)description.Width), Height = checked((uint)description.Height) },
                sampleCount,
                ToVulkanColorFormat(description.ColorFormat),
                description.Depth);
            Sampler sampler;
            Sampler depthSampler;
            uint slot;
            uint depthSlot;
            try {
                CreateRenderTargetViewBindings(device, target, out sampler, out slot, out depthSampler, out depthSlot);
            } catch {
                target.Dispose();
                throw;
            }

            // Every new resource is live; the swap only assigns fields and cannot leave the entry
            // pointing at a half-created target.
            entry.Target.Dispose();
            entry.Target = target;
            entry.SampleCount = sampleCount;
            entry.Lifetime.SetSampleCount(sampleCount);
            entry.Sampler = sampler;
            entry.ViewResource.View = target.ResolvedColorView;
            entry.ViewResource.Sampler = sampler;
            entry.ViewResource.BindlessSlot = slot;
            entry.ViewLifetime.SetBindingResource(entry.ViewResource);
            if (target.HasDepth && entry.DepthViewResource is not null) {
                entry.DepthSampler = depthSampler;
                entry.DepthViewResource.View = target.ResolvedDepthView;
                entry.DepthViewResource.Sampler = depthSampler;
                entry.DepthViewResource.BindlessSlot = depthSlot;
                entry.DepthViewLifetime!.SetBindingResource(entry.DepthViewResource);
            }

            entry.Pipelines = null;
        }
    }

    /// <summary>
    /// Destroys the swapchain, scene target, and present pipeline in dependency order, clearing each
    /// field before its resource is released.
    /// </summary>
    /// <remarks>
    /// Render-thread only; the caller has waited for the device to idle and already destroyed the
    /// geometry pipelines that reference the scene render pass.
    /// </remarks>
    private void DestroyPresentationResources() {
        VulkanPresentPipeline? presentPipeline = _presentPipeline;
        _presentPipeline = null;
        presentPipeline?.Dispose();
        VulkanRenderTarget? sceneTarget = _sceneTarget;
        _sceneTarget = null;
        sceneTarget?.Dispose();
        VulkanSwapchain? swapchain = _swapchain;
        _swapchain = null;
        swapchain?.Dispose();
    }

    /// <summary>
    /// Drops the submitted frame that was waiting for presentation.
    /// </summary>
    /// <remarks>
    /// The device has been idled, so the frame's fence is signaled and the swapchain image can be
    /// released without presenting a partial image.
    /// </remarks>
    private void DropPendingPresent() {
        _pendingPresent = false;
        _pendingRenderFinished = default;
        _suboptimalRecoveryPending = false;
    }

    /// <summary>
    /// Runs a recovery request from a frame call and converts a fatal result into
    /// <see cref="GraphicsDeviceLostException"/>.
    /// </summary>
    /// <param name="reason">The failure to recover from.</param>
    /// <exception cref="GraphicsDeviceLostException">
    /// The recovery returned <see langword="false"/>. The diagnostic code is <c>RB3002</c>.
    /// </exception>
    private void RecoverOrThrow(RenderRecoveryReason reason) {
        if (Recover(reason)) {
            return;
        }

        throw new GraphicsDeviceLostException(
            $"The Vulkan renderer could not recover from {reason}; the app stops.",
            DiagnosticCodes.GRAPHICS_DEVICE_LOST,
            null);
    }

    /// <summary>
    /// Consumes a fault-injection request and routes it through the normal recovery path.
    /// </summary>
    /// <remarks>Test-only; the caller is the render thread.</remarks>
    private void HandleSimulatedFailure() {
        if (_simulatedFailure is not { } reason) {
            return;
        }

        _simulatedFailure = null;
        RecoverOrThrow(reason);
    }

    /// <summary>
    /// Destroys the live GPU asset resources before their logical device is destroyed.
    /// </summary>
    /// <remarks>
    /// Vulkan requires every child object to be destroyed before its device, and the validation layer
    /// reports a live asset object as a leak. The asset GPU worker tracks its live resources, releases
    /// their bindless slots, and destroys the cached samplers here. The GPU asset bridge retains every
    /// upload description, so the render loop recreates the resources after a device recovery and the
    /// public asset stays valid. Render-thread only, with the device idle. Destruction is best effort:
    /// a failure is logged and the device teardown continues.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Asset GPU teardown runs during backend shutdown, when the worker or the device may already be gone.")]
    private void DestroyAssetGpuResources() {
        VulkanAssetWorker? worker = _assetGpuWorker;
        if (worker is null) {
            return;
        }

        try {
            worker.DestroyAllResources();
        } catch (Exception exception) {
            _options?.Logger.LogError(
                exception,
                "The GPU asset resources could not be destroyed before the Vulkan device was destroyed.");
        }
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

    /// <summary>
    /// Records one render pass per camera pass into the camera's target: the frame-start scene-target
    /// reset, the per-target barriers, viewport-scoped clears, the per-pass frame block write, the
    /// pass-index push constant, the instanced geometry draws, and the final resolved-color transitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Render-thread only. The frame slot's fence has already signaled, so the slot's buffers and
    /// descriptor set can be updated. The frame descriptor set is bound once for the whole frame.
    /// Every camera pass begins its own render pass instance with its clamped viewport as the render
    /// area, writes its own 256-byte block into the slot's frame storage buffer, and pushes its index,
    /// so each pass's draws read their own <c>rbFrame</c> values and only that pass' render area is
    /// resolved.
    /// </para>
    /// <para>
    /// The app scene target discards its content before the first pass and its resolved color and depth
    /// are transitioned to <c>ShaderReadOnlyOptimal</c> for the present pass and a later internal
    /// consumer after the last one. A runtime render target keeps its content: the frame waits for the
    /// last frame that used each target, the writing pass transitions the target's color and depth
    /// images to their attachment layouts, and the resolved images transition back to a shader read
    /// after the pass so a later camera can sample them.
    /// </para>
    /// </remarks>
    private void RecordScenePass(
        Vk vulkan,
        CommandBuffer commandBuffer,
        FramePlan plan,
        RenderFramePacket frame,
        int frameSlot,
        HashSet<ulong> pipelines) {
        VulkanRenderTarget sceneTarget = _sceneTarget!;
        sceneTarget.DiscardContentAndTransitionToAttachments(vulkan, commandBuffer);
        PrepareFrameRenderTargets(vulkan, commandBuffer, frame, frameSlot);
        DescriptorSet descriptorSet = _frameResources!.GetDescriptorSet(frameSlot);
        vulkan.CmdBindDescriptorSets(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _descriptorState!.PipelineLayout,
            VulkanDescriptorState.GEOMETRY_SET,
            1,
            &descriptorSet,
            0,
            null);

        ulong instanceByteOffset = 0;
        bool renderedScenePass = false;
        for (int passIndex = 0; passIndex < plan.Passes.Count; passIndex++) {
            PassPlan passPlan = plan.Passes[passIndex];
            CameraPass pass = passPlan.Pass;
            long targetId = pass.TargetId;
            VulkanRenderTarget target = targetId == 0 ? sceneTarget : RequireRenderTarget(targetId).Target;
            if (!TryResolveViewport(pass, target.Extent, out Viewport viewport, out Rect2D scissor)) {
                continue;
            }

            if (targetId == 0) {
                if (renderedScenePass) {
                    sceneTarget.SynchronizeBetweenPasses(vulkan, commandBuffer);
                }
            } else {
                target.TransitionForWrite(vulkan, commandBuffer);
            }

            RenderPassBeginInfo beginInfo = new() {
                SType = StructureType.RenderPassBeginInfo,
                RenderPass = target.RenderPass,
                Framebuffer = target.Framebuffer,
                RenderArea = scissor,
                ClearValueCount = 0,
                PClearValues = null,
            };
            vulkan.CmdBeginRenderPass(commandBuffer, &beginInfo, SubpassContents.Inline);
            vulkan.CmdSetViewport(commandBuffer, 0, 1, &viewport);
            vulkan.CmdSetScissor(commandBuffer, 0, 1, &scissor);
            if ((pass.ClearMode & ClearMode.Color) != 0) {
                ClearColorAttachment(vulkan, commandBuffer, pass.ClearColor, scissor);
            }

            // A depth-less render target accepts ClearMode.Depth as a documented no-op, exactly like the
            // stencil flag on the scene target.
            if ((pass.ClearMode & ClearMode.Depth) != 0 && target.HasDepth) {
                ClearDepthAttachment(vulkan, commandBuffer, scissor);
            }

            WriteFrameBlock(frameSlot, passIndex, pass, frame, target.Extent);
            PushPassIndex(vulkan, commandBuffer, passIndex);
            if (passPlan.Batches.Count > 0) {
                VulkanGeometryPipelineCache geometryPipelines = targetId == 0
                    ? _geometryPipelines!
                    : GetRenderTargetPipelines(RequireRenderTarget(targetId));
                foreach (PlannedBatch batch in passPlan.Batches) {
                    VulkanShaderAssetResource shader = RequireShaderResource(batch.Program);
                    VulkanMeshAssetResource mesh = RequireMeshResource(batch.Mesh);
                    Pipeline pipeline = geometryPipelines.GetOrCreate(
                        shader,
                        batch.RenderState,
                        batch.Topology,
                        batch.Wireframe);
                    pipelines.Add(pipeline.Handle);

                    RenderDrawRecord first = batch.Instances[0].Record;
                    int stride = ShaderAbiLayout.INSTANCE_HEADER_SIZE + first.InstanceTemplate.Length;
                    instanceByteOffset = AlignUp(instanceByteOffset, (ulong)stride);
                    uint firstInstance = checked((uint)(instanceByteOffset / (ulong)stride));
                    WriteInstanceRecords(frameSlot, instanceByteOffset, stride, batch);
                    instanceByteOffset += (ulong)(stride * batch.InstanceCount);

                    vulkan.CmdBindPipeline(commandBuffer, PipelineBindPoint.Graphics, pipeline);
                    Buffer vertexBuffer = mesh.VertexBuffer;
                    ulong vertexOffset = 0;
                    vulkan.CmdBindVertexBuffers(commandBuffer, 0, 1, &vertexBuffer, &vertexOffset);
                    vulkan.CmdBindIndexBuffer(commandBuffer, mesh.IndexBuffer, 0, IndexType.Uint32);
                    vulkan.CmdDrawIndexed(
                        commandBuffer,
                        checked((uint)first.IndexCount),
                        checked((uint)batch.InstanceCount),
                        0,
                        0,
                        firstInstance);
                }
            }

            vulkan.CmdEndRenderPass(commandBuffer);
            if (targetId == 0) {
                renderedScenePass = true;
            } else {
                target.TransitionResolvedToShaderRead(vulkan, commandBuffer);
            }
        }

        sceneTarget.TransitionResolvedToShaderRead(vulkan, commandBuffer);
    }

    /// <summary>
    /// Waits for the previous frame that used each runtime render target and prepares the sampled targets
    /// for their read.
    /// </summary>
    /// <remarks>
    /// A render target is reused only after the previous frame that wrote or sampled it completed, so a
    /// frame can never overwrite content an in-flight frame still reads. Sampled targets are transitioned
    /// to <c>ShaderReadOnlyOptimal</c> here, before any render pass begins, because an image barrier is
    /// not legal inside a render pass.
    /// </remarks>
    private void PrepareFrameRenderTargets(
        Vk vulkan,
        CommandBuffer commandBuffer,
        RenderFramePacket frame,
        int frameSlot) {
        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
        foreach (CameraPass pass in frame.Passes) {
            if (pass.TargetId != 0) {
                _frameRenderTargets.Add(RequireRenderTarget(pass.TargetId));
            }

            foreach (RenderDrawRecord draw in pass.Draws) {
                AddSampledRenderTargets(draw.GetTextureBindings());
                AddSampledRenderTargets(draw.GetInstanceTextureBindings());
            }
        }

        foreach (RenderTargetEntry entry in _frameRenderTargets) {
            PrepareRenderTargetUse(entry, frameSlot);
        }

        foreach (RenderTargetEntry entry in _frameSampledRenderTargets) {
            PrepareRenderTargetUse(entry, frameSlot);
            entry.Target.PrepareSampled(vulkan, commandBuffer);
        }
    }

    private void AddSampledRenderTargets(ReadOnlySpan<Texture?> textures) {
        foreach (Texture? texture in textures) {
            if (texture is not null && texture.IsRenderTargetView) {
                _frameSampledRenderTargets.Add(RequireRenderTarget(texture.RenderTargetId));
            }
        }
    }

    private void PrepareRenderTargetUse(RenderTargetEntry entry, int frameSlot) {
        int lastFrameSlot = entry.Lifetime.LastUseFrameSlot;
        if (lastFrameSlot >= 0 && lastFrameSlot != frameSlot) {
            Fence fence = _fences[lastFrameSlot];
            ThrowIfFailed(
                _device!.Vulkan.WaitForFences(_device.Device, 1, &fence, true, WAIT_TIMEOUT),
                "vkWaitForFences");
        }

        entry.Lifetime.SetLastUseFrameSlot(frameSlot);
    }

    private VulkanGeometryPipelineCache GetRenderTargetPipelines(RenderTargetEntry entry) =>
        entry.Pipelines ??= VulkanGeometryPipelineCache.Create(
            _device!,
            entry.Target.RenderPass,
            _descriptorState!.PipelineLayout,
            entry.Target.SampleCount,
            _deviceGeneration);

    /// <summary>
    /// Records the 4-byte push constant that selects one camera pass's frame block.
    /// </summary>
    /// <remarks>
    /// The push constant is command-buffer state, so it must be set after the previous pass's draws
    /// and before the next pass's first draw. The descriptor set stays bound across passes because the
    /// frame storage buffer holds every pass's block.
    /// </remarks>
    private void PushPassIndex(Vk vulkan, CommandBuffer commandBuffer, int passIndex) {
        uint index = checked((uint)passIndex);
        vulkan.CmdPushConstants(
            commandBuffer,
            _descriptorState!.PipelineLayout,
            VulkanDescriptorState.PUSH_CONSTANT_STAGES,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_OFFSET,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_SIZE,
            &index);
    }

    private static bool TryResolveViewport(
        CameraPass pass,
        Extent2D extent,
        out Viewport viewport,
        out Rect2D scissor) {
        int x = Math.Clamp(pass.ViewportX, 0, (int)extent.Width);
        int y = Math.Clamp(pass.ViewportY, 0, (int)extent.Height);
        int width = Math.Clamp(pass.ViewportWidth, 0, (int)extent.Width - x);
        int height = Math.Clamp(pass.ViewportHeight, 0, (int)extent.Height - y);
        if (width <= 0 || height <= 0) {
            viewport = default;
            scissor = default;
            return false;
        }

        viewport = new Viewport {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MinDepth = VulkanDepthState.MIN_DEPTH,
            MaxDepth = VulkanDepthState.MAX_DEPTH,
        };
        scissor = new Rect2D {
            Offset = new Offset2D { X = x, Y = y },
            Extent = new Extent2D { Width = (uint)width, Height = (uint)height },
        };
        return true;
    }

    private static void ClearColorAttachment(
        Vk vulkan,
        CommandBuffer commandBuffer,
        Color color,
        Rect2D rect) {
        ClearAttachment attachment = new() {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue {
                Color = new ClearColorValue(color.R, color.G, color.B, color.A),
            },
        };
        ClearRect clearRect = new() {
            Rect = rect,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        vulkan.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &clearRect);
    }

    private static void ClearDepthAttachment(Vk vulkan, CommandBuffer commandBuffer, Rect2D rect) {
        ClearAttachment attachment = new() {
            AspectMask = ImageAspectFlags.DepthBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue {
                DepthStencil = new ClearDepthStencilValue {
                    Depth = VulkanDepthState.CLEAR_DEPTH,
                    Stencil = 0,
                },
            },
        };
        ClearRect clearRect = new() {
            Rect = rect,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        vulkan.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &clearRect);
    }

    /// <summary>
    /// Writes one camera pass's 256-byte frame block at its pass index.
    /// </summary>
    /// <remarks>
    /// The engine stores matrices in System.Numerics' row-vector convention, while GLSL reads the
    /// uniform bytes as a column-major <c>mat4</c> and multiplies <c>matrix * vector</c>. Those two
    /// conventions are transposes of each other, so writing the CPU matrix's raw row-major words is
    /// exactly the column-major matrix the shader needs; transposing here would invert the transform
    /// (the GPU draw tests cover the round trip). <see cref="WriteMatrix"/> documents the byte order.
    /// </remarks>
    private void WriteFrameBlock(
        int frameSlot,
        int passIndex,
        CameraPass pass,
        RenderFramePacket frame,
        Extent2D targetExtent) {
        Span<byte> block = _frameResources!.GetFrameBlockSpan(frameSlot, passIndex);
        WriteMatrix(block, ShaderAbiFrame.VIEW_PROJECTION_OFFSET, pass.ViewProjection);
        // The shader multiplies a column vector by the column-major matrix it reads; raw row-major
        // System.Numerics words are the transpose of that, which is the engine's row-vector transform.
        WriteMatrix(block, ShaderAbiFrame.VIEW_OFFSET, pass.View);
        WriteMatrix(block, ShaderAbiFrame.PROJECTION_OFFSET, pass.Projection);
        WriteVector4(
            block,
            ShaderAbiFrame.CAMERA_POSITION_OFFSET,
            new Vector4(pass.CameraPosition, 1f));
        WriteVector4(
            block,
            ShaderAbiFrame.VIEWPORT_OFFSET,
            new Vector4(
                pass.ViewportX,
                pass.ViewportY,
                pass.ViewportWidth,
                pass.ViewportHeight));
        WriteVector4(
            block,
            ShaderAbiFrame.TIME_OFFSET,
            new Vector4((float)frame.Time, (float)frame.DeltaTime, frame.FrameIndex, 0f));
        WriteVector4(
            block,
            ShaderAbiFrame.SCREEN_SIZE_OFFSET,
            new Vector4(
                targetExtent.Width,
                targetExtent.Height,
                // The scene shader and the present pass must apply the same frame exposure.
                MathF.Pow(2f, frame.Tonemapping.Exposure),
                0f));
    }

    /// <summary>
    /// Writes every instance record of one batch at its final byte offset.
    /// </summary>
    /// <remarks>
    /// The record header is the transposed world matrix, the tint, and the UV rectangle, followed by
    /// the draw's uniform template and the resolved bindless texture indices. The batch's first
    /// instance index is <c>byteOffset / stride</c>, which the shader receives through
    /// <c>gl_InstanceIndex</c>. A draw with its own per-instance texture set resolves each instance's
    /// indices from that instance's run; every other draw repeats its shared set. Neither path allocates,
    /// and the stride stays <c>INSTANCE_HEADER_SIZE + template length</c> either way.
    /// </remarks>
    private void WriteInstanceRecords(
        int frameSlot,
        ulong byteOffset,
        int stride,
        PlannedBatch batch) {
        Span<byte> records = _frameResources!.GetInstanceSpan(
            frameSlot,
            byteOffset,
            (ulong)(stride * batch.InstanceCount));
        for (int index = 0; index < batch.InstanceCount; index++) {
            PlannedInstance instance = batch.Instances[index];
            RenderDrawRecord draw = instance.Record;
            Span<byte> record = records.Slice(index * stride, stride);
            WriteMatrix(record, ShaderAbiLayout.TRANSFORM_OFFSET, draw.GetWorldMatrices()[instance.InstanceIndex]);
            WriteVector4(record, ShaderAbiLayout.COLOR_OFFSET, new Vector4(draw.Tint.R, draw.Tint.G, draw.Tint.B, draw.Tint.A));
            WriteVector4(record, ShaderAbiLayout.UV_RECT_OFFSET, draw.UvRect);
            draw.InstanceTemplate.CopyTo(record[ShaderAbiLayout.INSTANCE_HEADER_SIZE..]);

            ReadOnlySpan<Texture?> textures = GetInstanceTextureRun(draw, instance.InstanceIndex);
            for (int textureIndex = 0; textureIndex < textures.Length; textureIndex++) {
                WriteUInt32(
                    record,
                    ShaderAbiLayout.TEXTURE_INDEX_START + (textureIndex * ShaderAbiLayout.TEXTURE_INDEX_SIZE),
                    ResolveTextureSlot(textures[textureIndex]));
            }
        }
    }

    /// <summary>
    /// Gets the texture-binding run one instance writes: its own contiguous slice for a draw with a
    /// per-instance set, or the shared set every instance repeats.
    /// </summary>
    /// <param name="draw">The draw record that supplies the bindings.</param>
    /// <param name="instanceIndex">The zero-based instance index inside the draw record.</param>
    /// <returns>The texture objects in sidecar declaration order for this instance.</returns>
    /// <remarks>
    /// The per-instance set is stored instance-major, so one instance's bindings are a contiguous run
    /// and the slice is a view over the record's pooled buffer; the read allocates nothing. The recorder
    /// retained each texture in the frame's retention set, so the resolved bindless slots are still live.
    /// </remarks>
    private static ReadOnlySpan<Texture?> GetInstanceTextureRun(RenderDrawRecord draw, int instanceIndex) {
        if (!draw.HasPerInstanceTextureBindings) {
            return draw.GetTextureBindings();
        }

        int bindingCount = draw.TextureBindingCount;
        return draw.GetInstanceTextureBindings().Slice(instanceIndex * bindingCount, bindingCount);
    }

    /// <summary>
    /// Resolves one texture binding to its bindless slot. A render target color or depth view resolves
    /// exactly like a packed or procedural texture; the frame's retention set already kept its target
    /// alive.
    /// </summary>
    private static uint ResolveTextureSlot(Texture? texture) {
        if (texture is null) {
            return VulkanBindlessTextureTable.WHITE_SLOT;
        }

        ITextureBindingResource? binding = texture.BindingResource;
        if (binding is null) {
            throw new GraphicsException(
                $"{DescribeTexture(texture)} is not a Vulkan GPU texture and cannot be bound in a draw.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        if (binding.BindlessSlot == 0) {
            throw new GraphicsException(
                $"{DescribeTexture(texture)} has no bindless slot. The texture must finish its GPU "
                    + "upload before a draw can reference it.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null);
        }

        return binding.BindlessSlot;
    }

    private static string DescribeTexture(Texture texture) =>
        texture.Path.Length == 0 ? "The render target texture view" : $"The texture '{texture.Path}'";

    private static VulkanShaderAssetResource RequireShaderResource(EngineAsset shader) {
        if (shader.Resource is VulkanShaderAssetResource resource) {
            return resource;
        }

        throw new GraphicsException(
            $"The shader '{shader.Path}' is not a Vulkan GPU shader and cannot create a geometry pipeline.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    private static VulkanMeshAssetResource RequireMeshResource(EngineAsset mesh) {
        if (mesh.Resource is VulkanMeshAssetResource resource) {
            return resource;
        }

        throw new GraphicsException(
            $"The mesh '{mesh.Path}' is not a Vulkan GPU mesh and cannot be drawn.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null);
    }

    /// <summary>
    /// Computes the instance-record bytes a frame needs, including per-batch stride alignment.
    /// </summary>
    private static ulong ComputeInstanceBytes(FramePlan plan) {
        ulong bytes = 0;
        foreach (PassPlan pass in plan.Passes) {
            foreach (PlannedBatch batch in pass.Batches) {
                RenderDrawRecord first = batch.Instances[0].Record;
                int stride = ShaderAbiLayout.INSTANCE_HEADER_SIZE + first.InstanceTemplate.Length;
                bytes = AlignUp(bytes, (ulong)stride);
                bytes += (ulong)(stride * batch.InstanceCount);
            }
        }

        return bytes;
    }

    private static ulong AlignUp(ulong value, ulong alignment)
        => checked(((value + alignment - 1) / alignment) * alignment);

    /// <summary>
    /// Writes one System.Numerics matrix in its storage order.
    /// </summary>
    /// <remarks>
    /// The words are written M11..M44 (row-major). GLSL reads a <c>mat4</c> from std140/std430 storage
    /// as column-major, so it sees the transpose of what this method writes, which is the engine's
    /// row-vector matrix expressed in the shader's column-vector convention.
    /// </remarks>
    private static void WriteMatrix(Span<byte> destination, int offset, Matrix4x4 value) {
        Span<byte> target = destination[offset..];
        WriteSingle(target, 0, value.M11);
        WriteSingle(target, 4, value.M12);
        WriteSingle(target, 8, value.M13);
        WriteSingle(target, 12, value.M14);
        WriteSingle(target, 16, value.M21);
        WriteSingle(target, 20, value.M22);
        WriteSingle(target, 24, value.M23);
        WriteSingle(target, 28, value.M24);
        WriteSingle(target, 32, value.M31);
        WriteSingle(target, 36, value.M32);
        WriteSingle(target, 40, value.M33);
        WriteSingle(target, 44, value.M34);
        WriteSingle(target, 48, value.M41);
        WriteSingle(target, 52, value.M42);
        WriteSingle(target, 56, value.M43);
        WriteSingle(target, 60, value.M44);
    }

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value) {
        Span<byte> target = destination[offset..];
        WriteSingle(target, 0, value.X);
        WriteSingle(target, 4, value.Y);
        WriteSingle(target, 8, value.Z);
        WriteSingle(target, 12, value.W);
    }

    private static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);

    private static void WriteUInt32(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);

    /// <summary>
    /// Gets whether a presentation result means the image reached the presentation engine. A
    /// suboptimal result still presented; an out-of-date or lost surface did not.
    /// </summary>
    /// <param name="result">The result of <c>vkQueuePresentKHR</c>.</param>
    /// <returns><see langword="true"/> when the presentation succeeded.</returns>
    internal static bool CountsAsPresented(SwapchainResult result) =>
        result is SwapchainResult.Success or SwapchainResult.Suboptimal;

    private void PresentPending() {
        if (!_pendingPresent) {
            return;
        }

        SwapchainResult result = _swapchain!.Present(_device!.PresentQueue, _pendingRenderFinished, _pendingImageIndex);
        uint presentedImageIndex = _pendingImageIndex;
        _pendingPresent = false;
        _pendingRenderFinished = default;
        _instance!.ThrowIfValidationError("presenting a frame");
        if (CountsAsPresented(result)) {
            // The image reached the presentation engine, so a frame completed. A suboptimal hint is a
            // routine driver notification and must not count toward the recovery bound, but the
            // presentation itself succeeded and is counted. An out-of-date, lost, or recreated
            // presentation is not.
            _lastPresentedImageIndex = presentedImageIndex;
            _presentedFrames++;
            _consecutiveRecoveries = 0;
            _stats = _stats with { PresentedFrames = _presentedFrames };
        }

        bool recoverSuboptimal = _suboptimalRecoveryPending;
        _suboptimalRecoveryPending = false;
        switch (result) {
            case SwapchainResult.OutOfDate:
                RecoverOrThrow(RenderRecoveryReason.SwapchainOutOfDate);
                break;
            case SwapchainResult.SurfaceLost:
                RecoverOrThrow(RenderRecoveryReason.SurfaceLost);
                break;
            case SwapchainResult.Suboptimal:
                RecoverOrThrow(RenderRecoveryReason.SwapchainSuboptimal);
                break;
            default:
                if (recoverSuboptimal) {
                    RecoverOrThrow(RenderRecoveryReason.SwapchainSuboptimal);
                }

                break;
        }
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
        _presentedFrames,
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
        _recoveryCount);

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

    /// <summary>
    /// The render-thread resources of one runtime render target: the images and render pass, the bindless
    /// color and depth views, the target's samplers, and the lazily created geometry pipeline cache.
    /// </summary>
    private sealed class RenderTargetEntry {
        internal RenderTargetEntry(
            long id,
            int sampleCount,
            VulkanRenderTarget target,
            RenderTextureTargetLifetime lifetime,
            RenderTextureViewLifetime viewLifetime,
            RenderTextureViewResource viewResource,
            Sampler sampler,
            RenderTextureViewLifetime? depthViewLifetime,
            RenderTextureViewResource? depthViewResource,
            Sampler depthSampler) {
            Id = id;
            SampleCount = sampleCount;
            Target = target;
            Lifetime = lifetime;
            ViewLifetime = viewLifetime;
            ViewResource = viewResource;
            Sampler = sampler;
            DepthViewLifetime = depthViewLifetime;
            DepthViewResource = depthViewResource;
            DepthSampler = depthSampler;
        }

        internal long Id { get; }

        internal int SampleCount { get; set; }

        internal VulkanRenderTarget Target { get; set; }

        internal RenderTextureTargetLifetime Lifetime { get; }

        internal RenderTextureViewLifetime ViewLifetime { get; }

        internal RenderTextureViewResource ViewResource { get; }

        internal Sampler Sampler { get; set; }

        internal RenderTextureViewLifetime? DepthViewLifetime { get; }

        internal RenderTextureViewResource? DepthViewResource { get; }

        internal Sampler DepthSampler { get; set; }

        internal VulkanGeometryPipelineCache? Pipelines { get; set; }
    }
}
