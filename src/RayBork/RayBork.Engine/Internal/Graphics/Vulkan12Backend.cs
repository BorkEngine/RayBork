using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace RayBork.Internal;

/// <summary>
/// The Vulkan 1.2 renderer backend: swapchain, HDR scene target, geometry and post-process passes,
/// and the AgX/scRGB present path behind <see cref="IRendererBackend"/>.
/// </summary>
/// <remarks>
/// The render thread owns every member; <see cref="Dispose"/> enqueues instance teardown on
/// <see cref="RendererBackendOptions.TeardownQueue"/> because the instance and its loader outlive
/// that thread. Recoverable presentation, surface, and device failures are recovered in place by
/// <see cref="Recover"/>; a failed recovery throws <see cref="GraphicsDeviceLostException"/> and
/// validation errors are fatal.
/// </remarks>
internal sealed unsafe class Vulkan12Backend : IRendererBackend, IAssetGpuWorkerProvider
{
    /// <summary>Recovery attempts without a completed frame that make recovery fatal.</summary>
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
    private VulkanPostProcessor? _postProcessor;
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
    private uint _sceneSourceColorSlot;
    private uint _sceneSourceDepthSlot;
    private Sampler _sceneSourceColorSampler;
    private Sampler _sceneSourceDepthSampler;
    private int _sceneSourceSlotAllocationCount;
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

    /// <summary>Gets the logical device for GPU-test probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanDevice Device =>
        _device
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Gets the scene target for GPU-test probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanRenderTarget SceneTarget =>
        _sceneTarget
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Gets a live render target by id for GPU-test probes; render-thread only.</summary>
    /// <param name="targetId">The render texture target id.</param>
    /// <param name="target">The live target, or <see langword="null"/> when the id is not live.</param>
    /// <returns><see langword="true"/> when the target is live.</returns>
    internal bool TryGetRenderTarget(long targetId, out VulkanRenderTarget? target)
    {
        if (_renderTargets.TryGetValue(targetId, out RenderTargetEntry? entry))
        {
            target = entry.Target;
            return true;
        }

        target = null;
        return false;
    }

    /// <summary>Gets whether a frame wrote color to a live render target; GPU-test only.</summary>
    /// <param name="targetId">The render texture target id.</param>
    /// <returns><see langword="true"/> when the target's written flag is set.</returns>
    internal bool HasRenderTargetColorContentForTests(long targetId) =>
        _renderTargets.TryGetValue(targetId, out RenderTargetEntry? entry)
        && entry.HasResolvedColorContent;

    /// <summary>Gets the swapchain for GPU-test pixel probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanSwapchain Swapchain =>
        _swapchain
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>
    /// Gets the most recent presented image index for GPU-test probes; <see cref="uint.MaxValue"/>
    /// until the first present and stale after the presentation path is recreated.
    /// </summary>
    internal uint LastPresentedImageIndex => _lastPresentedImageIndex;

    /// <summary>
    /// Gets the pending present image index for GPU-test probes, or <see cref="uint.MaxValue"/>
    /// when none is pending; a probe reads it before <c>vkQueuePresentKHR</c> and presents afterwards.
    /// </summary>
    internal uint PendingPresentImageIndex => _pendingPresent ? _pendingImageIndex : uint.MaxValue;

    /// <summary>Gets the Vulkan instance for GPU-test checks; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanInstance Instance =>
        _instance
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Gets the bindless table for GPU-test probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanBindlessTextureTable BindlessTextures =>
        _bindlessTextures
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Gets the geometry pipeline cache for GPU-test probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanGeometryPipelineCache GeometryPipelines =>
        _geometryPipelines
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Gets the frame resources for GPU-test probes; render-thread only.</summary>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    internal VulkanFrameResources FrameResources =>
        _frameResources
        ?? throw new InvalidOperationException("The renderer backend has not been initialized.");

    /// <summary>Creates the GPU asset worker; render-thread only, after initialization.</summary>
    /// <remarks>
    /// The worker resolves live state per operation, so it survives a device recreation unreplaced.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The backend has not been initialized.</exception>
    public IAssetGpuWorker CreateAssetGpuWorker()
    {
        EnsureInitialized();
        VulkanAssetWorker worker = new(
            () => Device,
            () => DeviceGeneration,
            () => BindlessTextures,
            AreFrameResourcesIdle,
            WaitForFrameResourcesIdle,
            _options!.Logger
        );
        _assetGpuWorker = worker;
        return worker;
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A render target image, view, sampler, or bindless slot could not be created (<c>RB3001</c>).
    /// </exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed creation completes its request with the failure and keeps the render loop alive; a creation failure must never escape the render thread."
    )]
    public void ProcessRenderTextureRequests(RenderTextureService service)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(service);

        foreach (RenderTextureService.CreateRequest request in service.DrainCreates())
        {
            try
            {
                CreateRenderTextureTarget(service, request, request.Description);
            }
            catch (Exception exception)
            {
                request.DisposeRegistration();
                request.Completion.TrySetException(exception);
            }
        }

        foreach (RenderTextureService.ResizeRequest request in service.DrainResizes())
        {
            try
            {
                ResizeRenderTextureTarget(request, request.Width, request.Height);
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }
        }

        // Readbacks run before destructions in the same round: a target disposed while its request was
        // queued fails on the render thread with the disposed-target exception instead of disappearing
        // from the registry first. A target is only read after its last-use frame fence signaled.
        foreach (RenderTextureService.ReadbackRequest request in service.DrainReadbacks())
        {
            try
            {
                ProcessReadback(request);
            }
            catch (Exception exception)
            {
                request.TrySetException(exception);
            }
            finally
            {
                request.DisposeRegistration();
            }
        }

        foreach (RenderTextureTargetLifetime lifetime in service.DrainDestroys())
        {
            if (!_renderTargets.TryGetValue(lifetime.TargetId, out RenderTargetEntry? entry))
            {
                continue;
            }

            if (!IsRenderTargetIdle(lifetime))
            {
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
        Justification = "A failed request cleans up its partially created GPU objects and reports the failure through the request instead of escaping the render thread."
    )]
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The render texture and its target are published to the request's completion task or destroyed by the finally; the analyzer cannot model the transfer of ownership through the task source."
    )]
    private void CreateRenderTextureTarget(
        RenderTextureService service,
        RenderTextureService.CreateRequest request,
        RenderTextureDescription description
    )
    {
        if (request.Completion.Task.IsCompleted)
        {
            request.DisposeRegistration();
            return;
        }

        VulkanDevice device = _device!;
        uint maxDimension = GetMaxImageDimension2D(device);
        if (description.Width > maxDimension || description.Height > maxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(description),
                description,
                $"The {description.Width}x{description.Height} render texture exceeds the device's "
                    + $"maximum 2D image dimension {maxDimension}."
            );
        }

        int sampleCount = RenderTextureValidation.SelectSampleCount(
            description.SampleCount,
            device.Capabilities.SupportedSampleCounts
        );
        if (sampleCount != description.SampleCount)
        {
            _options!.Logger.LogWarning(
                "The requested render texture MSAA sample count {Requested} is not supported by the device; using {Effective}.",
                description.SampleCount,
                sampleCount
            );
        }

        Format format = ToVulkanColorFormat(description.ColorFormat);
        VulkanRenderTarget target = VulkanRenderTarget.Create(
            device,
            new Extent2D
            {
                Width = checked((uint)description.Width),
                Height = checked((uint)description.Height),
            },
            sampleCount,
            format,
            description.Depth
        );
        Sampler sampler;
        Sampler depthSampler;
        uint slot;
        uint depthSlot;
        try
        {
            CreateRenderTargetViewBindings(
                device,
                target,
                out sampler,
                out slot,
                out depthSampler,
                out depthSlot
            );
        }
        catch
        {
            target.Dispose();
            throw;
        }

        RenderTargetEntry? entry = null;
        bool success = false;
        try
        {
            long targetId = service.NextTargetId();
            RenderTextureTargetLifetime lifetime = new(
                targetId,
                description,
                sampleCount,
                service.QueueDestruction
            );
            RenderTextureViewResource viewResource = new(target.ResolvedColorView, sampler)
            {
                BindlessSlot = slot,
            };
            RenderTextureViewLifetime viewLifetime = new(
                lifetime,
                viewResource,
                description.Width,
                description.Height
            );
            RenderTextureViewResource? depthViewResource = null;
            RenderTextureViewLifetime? depthViewLifetime = null;
            Texture? depthTexture = null;
            if (target.HasDepth)
            {
                depthViewResource = new RenderTextureViewResource(
                    target.ResolvedDepthView,
                    depthSampler
                )
                {
                    BindlessSlot = depthSlot,
                };
                depthViewLifetime = new RenderTextureViewLifetime(
                    lifetime,
                    depthViewResource,
                    description.Width,
                    description.Height
                );
                TextureAssetDescriptor depthDescriptor = new()
                {
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
                depthTexture = new Texture(
                    depthViewLifetime,
                    depthDescriptor,
                    isProcedural: false,
                    isRenderTargetView: true
                );
            }

            TextureAssetDescriptor descriptor = new()
            {
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
            Texture colorTexture = new(
                viewLifetime,
                descriptor,
                isProcedural: false,
                isRenderTargetView: true
            );
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
                depthSampler,
                _options!.Settings.Tonemapping
            );
            _renderTargets.Add(targetId, entry);
            success = request.Completion.TrySetResult(renderTexture);
            if (!success)
            {
                _renderTargets.Remove(targetId);
            }
        }
        finally
        {
            request.DisposeRegistration();
            if (!success)
            {
                if (entry is not null)
                {
                    DestroyRenderTarget(entry, retireView: true);
                }
                else
                {
                    DestroyRenderTargetViewBindings(device, sampler, slot, depthSampler, depthSlot);
                    target.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Recreates one live render target's GPU objects at the requested size; render-thread only.
    /// </summary>
    /// <remarks>
    /// A target held by a recorded frame fails instead of waiting, so the render loop cannot deadlock;
    /// the last-use fence is still waited on because a released frame can remain in flight. New images
    /// are created before the live entry changes, so a failure leaves the old target usable.
    /// </remarks>
    private void ResizeRenderTextureTarget(
        RenderTextureService.ResizeRequest request,
        int width,
        int height
    )
    {
        RenderTextureTargetLifetime lifetime = request.Lifetime;
        if (lifetime.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(RenderTexture),
                "The render texture is disposed; it cannot be resized."
            );
        }

        if (!_renderTargets.TryGetValue(request.TargetId, out RenderTargetEntry? entry))
        {
            throw new InvalidOperationException(
                $"The frame references render target {request.TargetId}, which is no longer alive. A resize "
                    + "cannot recreate a target that was destroyed."
            );
        }

        if (lifetime.IsInUse)
        {
            throw new InvalidOperationException(
                $"Render target {request.TargetId} is in use by a recorded or in-flight frame; the resize "
                    + "was not applied. Resize the render texture only after the frame that uses it completed."
            );
        }

        // The public Resize no-ops an unchanged size before it queues; keep the render thread idempotent
        // in case a second request reaches it for the same size.
        RenderTextureDescription description = lifetime.Description;
        if (description.Width == width && description.Height == height)
        {
            request.Completion.TrySetResult();
            return;
        }

        VulkanDevice device = _device!;
        uint maxDimension = GetMaxImageDimension2D(device);
        if (width > maxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"{nameof(RenderTexture.Resize)} cannot resize render target {request.TargetId} to "
                    + $"{width}x{height}: it exceeds the device's maximum 2D image dimension {maxDimension}. "
                    + $"The target keeps its {description.Width}x{description.Height} images."
            );
        }

        if (height > maxDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"{nameof(RenderTexture.Resize)} cannot resize render target {request.TargetId} to "
                    + $"{width}x{height}: it exceeds the device's maximum 2D image dimension {maxDimension}. "
                    + $"The target keeps its {description.Width}x{description.Height} images."
            );
        }

        WaitForRenderTargetIdle(lifetime);

        VulkanRenderTarget target = VulkanRenderTarget.Create(
            device,
            new Extent2D { Width = checked((uint)width), Height = checked((uint)height) },
            entry.SampleCount,
            ToVulkanColorFormat(description.ColorFormat),
            description.Depth
        );

        // The new target has every image, view, render pass, and framebuffer it needs. The swap below
        // writes two bindless descriptors and assigns fields; the descriptor writes are the only calls
        // that can throw, and they throw only while the bindless table is not initialized. The backend
        // services requests only between Initialize and Dispose, and the table is initialized for the
        // current device, so these writes cannot throw in the supported state. A future failure point
        // here would have to roll the entry back before it disposes the new target.
        VulkanRenderTarget previousTarget = entry.Target;
        VulkanGeometryPipelineCache? previousPipelines = entry.Pipelines;
        entry.Target = target;
        entry.ViewResource.View = target.ResolvedColorView;
        entry.ViewLifetime.SetBindingResource(entry.ViewResource);
        _bindlessTextures!.WriteTexture(
            entry.ViewResource.BindlessSlot,
            target.ResolvedColorView,
            entry.Sampler
        );
        if (target.HasDepth && entry.DepthViewResource is not null)
        {
            entry.DepthViewResource.View = target.ResolvedDepthView;
            entry.DepthViewLifetime!.SetBindingResource(entry.DepthViewResource);
            _bindlessTextures.WriteTexture(
                entry.DepthViewResource.BindlessSlot,
                target.ResolvedDepthView,
                entry.DepthSampler
            );
        }

        entry.Pipelines = null;
        entry.ViewLifetime.SetDimensions(width, height);
        entry.DepthViewLifetime?.SetDimensions(width, height);
        lifetime.UpdateDescription(description with { Width = width, Height = height });
        // The new images were never used by a frame; the previous images' last-use fence was waited on,
        // and their content does not carry to the recreated images.
        entry.HasResolvedColorContent = false;
        lifetime.SetLastUseFrameSlot(-1);

        previousPipelines?.Dispose();
        _postProcessor?.DestroyStoreBack(previousTarget);
        previousTarget.Dispose();
        request.Completion.TrySetResult();
    }

    /// <summary>
    /// Services one queued readback: waits for the last-use fence, copies and converts the resolved
    /// color image when a completed frame wrote content, and otherwise returns the opaque-black default
    /// without copying or transitioning the image; render-thread only.
    /// </summary>
    /// <remarks>
    /// A copied image is restored to its layout before returning, and the target's views, slots, and
    /// lifetime are untouched. A disposed target or a dead id fails the request.
    /// </remarks>
    private void ProcessReadback(RenderTextureService.ReadbackRequest request)
    {
        RenderTextureTargetLifetime lifetime = request.Lifetime;
        if (lifetime.IsDisposed)
        {
            throw new ObjectDisposedException(
                nameof(RenderTexture),
                "The render texture is disposed; it cannot be read back."
            );
        }

        if (!_renderTargets.TryGetValue(request.TargetId, out RenderTargetEntry? entry))
        {
            throw new InvalidOperationException(
                $"The readback addresses render target {request.TargetId}, which is no longer alive. A "
                    + "render texture must be alive when its readback is serviced."
            );
        }

        if (request.IsCompleted)
        {
            return;
        }

        WaitForRenderTargetIdle(lifetime);
        BeforeReadbackCopyForTests?.Invoke();
        int width = checked((int)entry.Target.Extent.Width);
        int height = checked((int)entry.Target.Extent.Height);
        Color[,] colors;
        if (!entry.HasResolvedColorContent)
        {
            // No completed frame wrote the target. Its GPU content is undefined, and a sampled-only
            // frame may have moved it to ShaderReadOnlyOptimal without writing it. Return the defined
            // opaque-black default instead of copying undefined GPU memory, and leave the image and its
            // tracked layout untouched.
            colors = RenderTextureReadbackConverter.CreateOpaqueBlackColors(width, height);
        }
        else
        {
            ReadbackCopyCountForTests++;
            byte[] pixels = VulkanReadback.ReadRenderTargetColorImage(_device!, entry.Target);
            if (request.IsCompleted)
            {
                // A cancellation that arrived during the fence wait or the copy discards the result;
                // the copy helper already restored the layout, so the target is unchanged.
                return;
            }

            colors = RenderTextureReadbackConverter.DecodeColors(
                pixels,
                width,
                height,
                entry.Lifetime.Description.ColorFormat
            );
        }

        switch (request)
        {
            case RenderTextureService.ReadbackRequest<byte[]> pixelRequest:
                pixelRequest.Completion.TrySetResult(
                    RenderTextureReadbackConverter.EncodePixels(
                        colors,
                        entry.Tone,
                        entry.StartupTonemappingMode
                    )
                );
                break;
            case RenderTextureService.ReadbackRequest<Color[,]> colorRequest:
                colorRequest.Completion.TrySetResult(colors);
                break;
            default:
                throw new InvalidOperationException(
                    "The readback request has an unsupported completion value; only pixels and colors are queued."
                );
        }
    }

    /// <summary>
    /// Creates the color, and when present the depth, sampler and bindless slot and writes their
    /// descriptors into every frame descriptor set; the color slot is reserved first.
    /// </summary>
    /// <remarks>
    /// The depth outputs are a default sampler and slot <c>0</c> when the target has no depth. A
    /// failure releases the slots and samplers created so far before it rethrows.
    /// </remarks>
    private void CreateRenderTargetViewBindings(
        VulkanDevice device,
        VulkanRenderTarget target,
        out Sampler sampler,
        out uint slot,
        out Sampler depthSampler,
        out uint depthSlot
    )
    {
        sampler = default;
        slot = 0;
        depthSampler = default;
        depthSlot = 0;
        try
        {
            sampler = CreateRenderTargetSampler(device, TextureFilter.Linear);
            if (!_bindlessTextures!.TryAllocate(out slot))
            {
                throw new GraphicsException(
                    $"The bindless texture table is full ({_bindlessTextures.Capacity} slots, including "
                        + "the reserved white slot), so the render texture color view has no slot.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            _bindlessTextures.WriteTexture(slot, target.ResolvedColorView, sampler);

            if (target.HasDepth)
            {
                depthSampler = CreateRenderTargetSampler(device, TextureFilter.Nearest);
                if (!_bindlessTextures.TryAllocate(out depthSlot))
                {
                    throw new GraphicsException(
                        $"The bindless texture table is full ({_bindlessTextures.Capacity} slots, including "
                            + "the reserved white slot), so the render texture depth view has no slot.",
                        DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                        null
                    );
                }

                _bindlessTextures.WriteTexture(depthSlot, target.ResolvedDepthView, depthSampler);
            }
        }
        catch
        {
            DestroyRenderTargetViewBindings(device, sampler, slot, depthSampler, depthSlot);
            throw;
        }
    }

    /// <summary>
    /// Releases the color and depth bindless slots and destroys their samplers; safe with default
    /// handles and slot <c>0</c>.
    /// </summary>
    private void DestroyRenderTargetViewBindings(
        VulkanDevice device,
        Sampler sampler,
        uint slot,
        Sampler depthSampler,
        uint depthSlot
    )
    {
        if (slot != 0 && _bindlessTextures is not null)
        {
            _bindlessTextures.Release(slot);
        }

        if (depthSlot != 0 && _bindlessTextures is not null)
        {
            _bindlessTextures.Release(depthSlot);
        }

        DestroyRenderTargetSampler(device, sampler);
        DestroyRenderTargetSampler(device, depthSampler);
    }

    /// <summary>
    /// Destroys one render-target sampler when it has a handle; safe with a default handle.
    /// </summary>
    private static void DestroyRenderTargetSampler(VulkanDevice device, Sampler sampler)
    {
        if (sampler.Handle != 0)
        {
            device.Vulkan.DestroySampler(device.Device, sampler, null);
        }
    }

    private static Format ToVulkanColorFormat(TextureFormat format) =>
        format switch
        {
            TextureFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
            TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
            TextureFormat.Rgba8Srgb => Format.R8G8B8A8Srgb,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The format is not a supported render texture color format."
            ),
        };

    private static Sampler CreateRenderTargetSampler(VulkanDevice device, TextureFilter filter)
    {
        bool nearest = filter == TextureFilter.Nearest;
        SamplerCreateInfo createInfo = new()
        {
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
        Result result = device.Vulkan.CreateSampler(
            device.Device,
            &createInfo,
            null,
            out Sampler sampler
        );
        ThrowIfFailed(result, "vkCreateSampler");
        return sampler;
    }

    private RenderTargetEntry RequireRenderTarget(long targetId)
    {
        if (_renderTargets.TryGetValue(targetId, out RenderTargetEntry? entry))
        {
            return entry;
        }

        throw new GraphicsException(
            $"The frame references render target {targetId}, which is no longer alive. A render texture "
                + "must stay alive until every recorded frame that writes or samples it completes.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    /// <summary>Gets the device's maximum 2D image dimension or the test override.</summary>
    private uint GetMaxImageDimension2D(VulkanDevice device)
    {
        if (MaxImageDimension2DOverrideForTests is { } maxDimension)
        {
            return maxDimension;
        }

        return device
            .Vulkan.GetPhysicalDeviceProperties(device.PhysicalDevice)
            .Limits.MaxImageDimension2D;
    }

    /// <summary>Gets whether the last-use fence signaled; a never-used target is idle.</summary>
    private bool IsRenderTargetIdle(RenderTextureTargetLifetime lifetime)
    {
        if (_device is null || _fences.Length == 0)
        {
            return true;
        }

        int frameSlot = lifetime.LastUseFrameSlot;
        if (frameSlot < 0 || frameSlot >= _fences.Length)
        {
            return true;
        }

        return _device.Vulkan.GetFenceStatus(_device.Device, _fences[frameSlot]) == Result.Success;
    }

    /// <summary>Waits for the last-use fence; render-thread only.</summary>
    /// <remarks>
    /// A recorded frame that still holds the target is rejected earlier, so the wait cannot deadlock.
    /// </remarks>
    private void WaitForRenderTargetIdle(RenderTextureTargetLifetime lifetime)
    {
        if (_device is null || _fences.Length == 0)
        {
            return;
        }

        int frameSlot = lifetime.LastUseFrameSlot;
        if (frameSlot < 0 || frameSlot >= _fences.Length)
        {
            return;
        }

        Fence fence = _fences[frameSlot];
        ThrowIfFailed(
            _device.Vulkan.WaitForFences(_device.Device, 1, &fence, true, WAIT_TIMEOUT),
            "vkWaitForFences"
        );
        RenderTargetFenceWaitCountForTests++;
    }

    private void DestroyRenderTarget(RenderTargetEntry entry, bool retireView)
    {
        VulkanGeometryPipelineCache? pipelines = entry.Pipelines;
        entry.Pipelines = null;
        pipelines?.Dispose();
        if (entry.ViewResource.BindlessSlot != 0 && _bindlessTextures is not null)
        {
            _bindlessTextures.Release(entry.ViewResource.BindlessSlot);
            entry.ViewResource.BindlessSlot = 0;
        }

        if (
            entry.DepthViewResource is not null
            && entry.DepthViewResource.BindlessSlot != 0
            && _bindlessTextures is not null
        )
        {
            _bindlessTextures.Release(entry.DepthViewResource.BindlessSlot);
            entry.DepthViewResource.BindlessSlot = 0;
        }

        if (entry.Sampler.Handle != 0 && _device is not null)
        {
            _device.Vulkan.DestroySampler(_device.Device, entry.Sampler, null);
            entry.Sampler = default;
        }

        if (entry.DepthSampler.Handle != 0 && _device is not null)
        {
            _device.Vulkan.DestroySampler(_device.Device, entry.DepthSampler, null);
            entry.DepthSampler = default;
        }

        if (retireView)
        {
            entry.ViewLifetime.Retire();
            entry.DepthViewLifetime?.Retire();
            // The backend retired the target without a public Dispose (shutdown, or a canceled
            // creation). The public object must report IsDisposed even though no destroy request ran.
            entry.Lifetime.MarkDestroyed();
        }

        _postProcessor?.DestroyStoreBack(entry.Target);
        entry.Target.Dispose();
    }

    private void DestroyRenderTargets()
    {
        foreach (RenderTargetEntry entry in _renderTargets.Values)
        {
            DestroyRenderTarget(entry, retireView: true);
        }

        _renderTargets.Clear();
        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
    }

    /// <summary>
    /// Destroys live render textures' GPU resources, keeping views and lifetimes for recreation.
    /// </summary>
    private void DestroyRenderTargetGpuResources()
    {
        foreach (RenderTargetEntry entry in _renderTargets.Values)
        {
            DestroyRenderTarget(entry, retireView: false);
        }

        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">Initialization failed (<c>RB3001</c>).</exception>
    public void Initialize(RendererBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Settings);
        ArgumentNullException.ThrowIfNull(options.InstanceExtensions);
        ArgumentNullException.ThrowIfNull(options.SurfaceProvider);
        ArgumentNullException.ThrowIfNull(options.WindowStateProvider);
        ArgumentNullException.ThrowIfNull(options.Logger);
        ArgumentNullException.ThrowIfNull(options.TeardownQueue);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            throw new InvalidOperationException(
                "The renderer backend has already been initialized."
            );
        }

        _options = options;
        try
        {
            InitializeCore(options);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A fatal Vulkan failure or validation error was recorded (<c>RB3001</c>).
    /// </exception>
    /// <exception cref="GraphicsDeviceLostException">
    /// A recoverable presentation failure could not be recovered (<c>RB3002</c>).
    /// </exception>
    public void Submit(RenderFramePacket frame)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(frame);
        int generationBeforeRecovery = _deviceGeneration;
        HandleSimulatedFailure();
        if (_pendingPresent)
        {
            PresentPending();
        }

        if (_deviceGeneration != generationBeforeRecovery)
        {
            // The device was recreated for this frame. The asset GPU resources still belong to the
            // destroyed device and the bridge has not recreated them yet, so drawing now would
            // reference stale handles and bindless slots. Drop the frame; the render loop restores
            // every live asset after it observes the new generation and the next frame draws.
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        WindowSurfaceState state = _options!.WindowStateProvider();
        if (state.IsMinimized || state.RenderWidth <= 0 || state.RenderHeight <= 0)
        {
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (_swapchainOutOfDate)
        {
            RecoverOrThrow(RenderRecoveryReason.SwapchainOutOfDate);
        }

        if (
            state.RenderWidth != (int)_swapchain!.Extent.Width
            || state.RenderHeight != (int)_swapchain.Extent.Height
        )
        {
            Resize(state.RenderWidth, state.RenderHeight);
        }

        if (HdrSelectionChanged(state.Hdr))
        {
            RecreatePresentation(state, LogLevel.Information, "a window HDR state change");
        }

        uint frameSlot = _frameSlot;
        Fence fence = _fences[frameSlot];
        Vk vulkan = _device!.Vulkan;
        Device deviceHandle = _device.Device;
        ThrowIfFailed(
            vulkan.WaitForFences(deviceHandle, 1, &fence, true, WAIT_TIMEOUT),
            "vkWaitForFences"
        );

        Semaphore imageAvailable = _imageAvailableSemaphores[frameSlot];
        BeforeAcquireForTests?.Invoke();
        SwapchainResult acquire = _swapchain.Acquire(
            frameSlot,
            imageAvailable,
            out uint imageIndex
        );
        if (acquire == SwapchainResult.OutOfDate)
        {
            RecoverOrThrow(RenderRecoveryReason.SwapchainOutOfDate);
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (acquire == SwapchainResult.SurfaceLost)
        {
            RecoverOrThrow(RenderRecoveryReason.SurfaceLost);
            _stats = CreateZeroDrawStats(frame.FrameIndex);
            return;
        }

        if (acquire == SwapchainResult.Suboptimal)
        {
            _suboptimalRecoveryPending = true;
            _options.Logger.LogDebug(
                "The swapchain image acquisition was suboptimal; the presentation path is recreated after the present."
            );
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
        // The slot's fence signaled, so the processor can release the post-target extents this slot's
        // previous frame did not use before the new frame records into it.
        _postProcessor!.BeginFrame((int)frameSlot, frame.FrameIndex);

        CommandBuffer commandBuffer = _commandBuffers[frameSlot];
        ThrowIfFailed(vulkan.ResetCommandBuffer(commandBuffer, 0), "vkResetCommandBuffer");
        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        ThrowIfFailed(vulkan.BeginCommandBuffer(commandBuffer, &beginInfo), "vkBeginCommandBuffer");

        HashSet<ulong> pipelines = [];
        RecordScenePass(vulkan, commandBuffer, plan, frame, (int)frameSlot, pipelines);

        RenderSettings settings = _options.Settings;

        // The present pass is frame-global and uses the frame's tone-mapping snapshot, not the live
        // runtime settings object: the render thread never reads the game's settings.
        TonemappingSnapshot tonemapping = frame.Tonemapping;
        float paperWhite = state.Hdr.HdrEnabled ? state.Hdr.SdrWhiteLevel : 1f;
        PresentEncoding encoding = _swapchainFormat.Encoding;
        float outputMax =
            encoding == PresentEncoding.ScRgbHdr && state.Hdr.HdrEnabled
                ? state.Hdr.HdrHeadroom
                : 1f;
        AgxCurveParameters agx = AgxCurveParameters.Create(
            tonemapping.AgxWhite,
            tonemapping.AgxContrast,
            outputMax
        );
        PresentPushConstants pushConstants = new()
        {
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
        SubmitInfo submitInfo = new()
        {
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
        ThrowIfFailed(
            vulkan.QueueSubmit(_device.GraphicsQueue, 1, &submitInfo, fence),
            "vkQueueSubmit"
        );

        _pendingPresent = true;
        _pendingImageIndex = imageIndex;
        _pendingRenderFinished = renderFinished;
        _frameSlot = (frameSlot + 1) % FRAMES_IN_FLIGHT;

        double cpuMilliseconds =
            (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
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
            _recoveryCount
        );
        _instance!.ThrowIfValidationError("submitting a frame");
    }

    /// <inheritdoc />
    /// <exception cref="GraphicsException">
    /// A validation error was recorded while presenting (<c>RB3001</c>).
    /// </exception>
    /// <exception cref="GraphicsDeviceLostException">
    /// A recoverable presentation failure could not be recovered (<c>RB3002</c>).
    /// </exception>
    public void Present()
    {
        EnsureInitialized();
        HandleSimulatedFailure();
        PresentPending();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Idles the device, drops the pending present, and recreates the affected presentation path,
    /// surface, or device. More than <see cref="MAX_CONSECUTIVE_RECOVERIES"/> attempts without a
    /// completed frame, an injected fault, or a platform failure returns <see langword="false"/>; every
    /// cleared field is safe for a later <see cref="Dispose"/>.
    /// </remarks>
    /// <exception cref="GraphicsException">A validation error occurred (<c>RB3001</c>).</exception>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Device recovery reports the failure through its return value and statistics instead of crashing the render thread."
    )]
    public bool Recover(RenderRecoveryReason reason)
    {
        EnsureInitialized();
        if (_consecutiveRecoveries >= MAX_CONSECUTIVE_RECOVERIES)
        {
            _options!.Logger.LogError(
                "The renderer already attempted {Limit} consecutive recoveries without completing a frame; the failure {Reason} is fatal.",
                MAX_CONSECUTIVE_RECOVERIES,
                reason
            );
            return false;
        }

        if (_unrecoverableFailureForTests)
        {
            _unrecoverableFailureForTests = false;
            _options!.Logger.LogError(
                "The renderer cannot recover from {Reason} because an unrecoverable failure was injected.",
                reason
            );
            return false;
        }

        _consecutiveRecoveries++;
        try
        {
            WindowSurfaceState state = _options!.WindowStateProvider();
            switch (reason)
            {
                case RenderRecoveryReason.SwapchainOutOfDate:
                    RecreatePresentation(state, LogLevel.Warning, "an out-of-date swapchain");
                    break;
                case RenderRecoveryReason.SwapchainSuboptimal:
                    RecreatePresentation(state, LogLevel.Information, "a suboptimal swapchain");
                    break;
                case RenderRecoveryReason.SurfaceLost:
                    if (!RecreateSurface(state))
                    {
                        return false;
                    }

                    break;
                case RenderRecoveryReason.DeviceLost:
                    RecreateDevice(state);
                    break;
                default:
                    _options.Logger.LogError(
                        "The renderer cannot recover from the unknown reason {Reason}.",
                        reason
                    );
                    return false;
            }
        }
        catch (Exception exception)
        {
            _options!.Logger.LogError(
                exception,
                "The renderer recovery from {Reason} failed.",
                reason
            );
            return false;
        }

        _recoveryCount++;
        _stats = _stats with { RecoveryCount = _recoveryCount };
        string operation = reason switch
        {
            RenderRecoveryReason.SwapchainSuboptimal => "recovering the presentation path",
            RenderRecoveryReason.SurfaceLost => "recovering the surface",
            RenderRecoveryReason.DeviceLost => "recovering the device",
            _ => "recovering the presentation path",
        };
        _instance!.ThrowIfValidationError(operation);
        return true;
    }

    /// <summary>Marks the device as lost for the next frame call; test-only.</summary>
    internal void SimulateDeviceLossForTests() =>
        _simulatedFailure = RenderRecoveryReason.DeviceLost;

    /// <summary>Marks the surface as lost for the next frame call; test-only.</summary>
    internal void SimulateSurfaceLossForTests() =>
        _simulatedFailure = RenderRecoveryReason.SurfaceLost;

    /// <summary>Makes the next recovery fail without touching GPU state; test-only.</summary>
    internal void SimulateUnrecoverableFailureForTests()
    {
        _unrecoverableFailureForTests = true;
        _simulatedFailure ??= RenderRecoveryReason.DeviceLost;
    }

    /// <summary>Sets the out-of-date flag for the next <see cref="Submit"/>; test-only.</summary>
    internal void SimulateSwapchainOutOfDateForTests() => _swapchainOutOfDate = true;

    /// <summary>Gets or sets a hook invoked before the swapchain acquire; test-only.</summary>
    internal Action? BeforeAcquireForTests { get; set; }

    /// <summary>Gets or sets a hook invoked before the readback copy; test-only.</summary>
    internal Action? BeforeReadbackCopyForTests { get; set; }

    /// <summary>
    /// Gets or sets a test override for the maximum 2D image dimension; <see langword="null"/> is real.
    /// </summary>
    internal uint? MaxImageDimension2DOverrideForTests { get; set; }

    /// <summary>Gets the render-target fence wait count; test-only.</summary>
    internal int RenderTargetFenceWaitCountForTests { get; private set; }

    /// <summary>Gets the readback full-color-copy count; test-only.</summary>
    internal int ReadbackCopyCountForTests { get; private set; }

    /// <summary>Computes a frame's instance-record bytes; test-only.</summary>
    internal static ulong ComputeInstanceBytesForTests(RenderFramePacket frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return ComputeInstanceBytes(new BatchPlanner().Plan(frame));
    }

    /// <summary>Gets the post-target set count per frame slot; test-only.</summary>
    internal int PostTargetRecreationCountForTests => _postProcessor?.TargetRecreationCount ?? 0;

    /// <summary>Gets the scene-target source-slot allocation count; test-only.</summary>
    internal int SceneTargetSourceSlotAllocationCountForTests => _sceneSourceSlotAllocationCount;

    /// <summary>Gets whether no frame can reference a GPU asset; render-thread only.</summary>
    /// <remarks>
    /// The asset worker polls this before destroying a resource; a device with no fences, or a disposed
    /// or lost device, reports idle so teardown can proceed.
    /// </remarks>
    internal bool AreFrameResourcesIdle()
    {
        if (_device is null || _fences.Length == 0)
        {
            return true;
        }

        Vk vulkan = _device.Vulkan;
        foreach (Fence fence in _fences)
        {
            Result result = vulkan.GetFenceStatus(_device.Device, fence);
            if (result == Result.NotReady)
            {
                return false;
            }

            if (result != Result.Success)
            {
                // A lost device cannot retire its fences. Reporting "not idle" keeps resources alive
                // until the recovery path recreates the device and drops them with it.
                return false;
            }
        }

        return true;
    }

    /// <summary>Waits until every frame slot fence signals; render-thread only.</summary>
    /// <remarks>A failure is logged and returns so shutdown can still release what it can.</remarks>
    internal void WaitForFrameResourcesIdle()
    {
        if (_device is null || _fences.Length == 0)
        {
            return;
        }

        fixed (Fence* fences = _fences)
        {
            Result result = _device.Vulkan.WaitForFences(
                _device.Device,
                (uint)_fences.Length,
                fences,
                true,
                WAIT_TIMEOUT
            );
            if (result != Result.Success)
            {
                _options?.Logger.LogWarning(
                    "The renderer could not wait for frame idleness before destroying GPU assets: {Result}.",
                    result
                );
            }
        }
    }

    /// <inheritdoc />
    public void Resize(int renderWidth, int renderHeight)
    {
        EnsureInitialized();
        if (renderWidth <= 0 || renderHeight <= 0)
        {
            _options!.Logger.LogDebug(
                "The presentation surface has no drawable extent ({Width}x{Height}); the presentation path is kept. The previous drawable request was {PreviousWidth}x{PreviousHeight}.",
                renderWidth,
                renderHeight,
                _requestedWidth,
                _requestedHeight
            );
            _requestedWidth = renderWidth;
            _requestedHeight = renderHeight;
            return;
        }

        _requestedWidth = renderWidth;
        _requestedHeight = renderHeight;

        VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(
            _device!.Vulkan,
            _device.PhysicalDevice,
            _surface
        );
        Extent2D extent = SelectExtent(support.Capabilities, renderWidth, renderHeight);
        Extent2D currentExtent = _swapchain!.Extent;
        if (extent.Width == currentExtent.Width && extent.Height == currentExtent.Height)
        {
            return;
        }

        WindowSurfaceState current = _options!.WindowStateProvider();
        WindowSurfaceState state = new(
            renderWidth,
            renderHeight,
            current.IsMinimized,
            current.IsFocused,
            current.Hdr
        );
        RecreatePresentation(state, LogLevel.Debug, "a window resize");
    }

    /// <summary>
    /// Waits for the device to idle and destroys the GPU objects, surface, and device on the render
    /// thread, then queues the instance for main-thread teardown; idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        VulkanPostProcessor? postProcessor = _postProcessor;
        _postProcessor = null;
        postProcessor?.Dispose();
        DestroyRenderTargets();
        _presentPipeline?.Dispose();
        _presentPipeline = null;
        ReleaseSceneTargetSourceSlots();
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
        if (_instance is not null && _surfaceHandle != IntPtr.Zero)
        {
            try
            {
                _options?.SurfaceProvider.DestroySurface(_instance.Handle.Handle, _surfaceHandle);
            }
            catch (GraphicsException exception)
            {
                // Disposal must still release the instance; the surface failure is reported but not
                // rethrown, matching the device and instance disposal paths.
                _options?.Logger.LogWarning(
                    "The native Vulkan surface could not be destroyed during disposal: {Message}",
                    exception.Message
                );
            }
            finally
            {
                _surfaceHandle = IntPtr.Zero;
                _surface = default;
            }
        }

        VulkanInstance? instance = _instance;
        _instance = null;
        if (instance is not null)
        {
            instance.Dispose();
            if (_options is not null)
            {
                _options.TeardownQueue.Enqueue(instance.DisposeFromMainThread);
            }
        }

        _initialized = false;
        _options?.Logger.LogDebug("Vulkan 1.2 renderer backend disposed.");
    }

    private void InitializeCore(RendererBackendOptions options)
    {
        ILogger logger = options.Logger;
        _instance = VulkanInstance.Create(
            new VulkanInstanceOptions
            {
                ApplicationName = APPLICATION_NAME,
                ApplicationVersion = EngineVersion.Current,
                SurfaceExtensions = options.InstanceExtensions,
                RequestValidation = options.Settings.ValidationEnabled,
                Logger = logger,
            }
        );
        _surfaceHandle = options.SurfaceProvider.CreateSurface(_instance.Handle.Handle);
        if (_surfaceHandle == IntPtr.Zero)
        {
            throw new GraphicsException(
                "The surface provider returned a zero Vulkan surface handle.",
                DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                null
            );
        }

        _surface = new SurfaceKHR { Handle = unchecked((ulong)_surfaceHandle.ToInt64()) };
        _device = VulkanDevice.Create(_instance, _surfaceHandle, logger);
        _deviceGeneration = 1;
        VulkanDeviceCapabilities capabilities = _device.Capabilities;
        WindowSurfaceState state = options.WindowStateProvider();
        _requestedWidth = state.RenderWidth;
        _requestedHeight = state.RenderHeight;
        _sampleCount = VulkanSampleCountSelection.Select(
            options.Settings.SampleCount,
            capabilities.SupportedSampleCounts
        );
        CreatePresentationPath(state);
        CreateGeometryResources();
        CreateFrameResources(_swapchain!.ImageCount);
        UpdateInfo();
        LogInitialized(capabilities, state, _swapchain.Extent);
        LogSelectionWarnings(state);
        _initialized = true;
        _instance.ThrowIfValidationError("initializing the renderer");
    }

    /// <summary>Creates the swapchain, scene target, and present pipeline.</summary>
    /// <remarks>
    /// Used at startup and after a device or surface loss; the device must be idle. The caller creates
    /// the geometry and frame resources afterwards.
    /// </remarks>
    private void CreatePresentationPath(WindowSurfaceState state)
    {
        VulkanDevice device = _device!;
        RendererBackendOptions options = _options!;
        VulkanDeviceCapabilities capabilities = device.Capabilities;
        _selectedHdrState = state.Hdr;
        _swapchainFormat = VulkanSwapchainFormatSelection.Select(
            capabilities.SurfaceFormats,
            state.Hdr
        );
        _presentMode = VulkanPresentModeSelection.Select(
            capabilities.Swapchain.PresentModes,
            options.Settings.PresentMode
        );
        VulkanSwapchainSupport support = capabilities.Swapchain;
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount
        );
        _swapchain = VulkanSwapchain.Create(
            device,
            _surface,
            _swapchainFormat,
            _presentMode.Native,
            (int)imageCount,
            extent
        );
        extent = _swapchain.Extent;
        _sceneTarget = VulkanRenderTarget.Create(
            device,
            extent,
            _sampleCount,
            Format.R16G16B16A16Sfloat,
            depth: true
        );
        _presentPipeline = VulkanPresentPipeline.Create(
            device,
            extent,
            _swapchainFormat.Format,
            _sceneTarget.ResolvedColorView
        );
        _presentPipeline.RecreateFramebuffers(extent, _swapchain.ImageViews);
        _swapchainOutOfDate = false;
        _suboptimalRecoveryPending = false;
    }

    /// <summary>
    /// Creates the per-device descriptor state, frame resources, bindless table, geometry pipelines,
    /// post processor, and scene source slots; render-thread only, with the scene target live.
    /// </summary>
    private void CreateGeometryResources()
    {
        VulkanDevice device = _device!;
        VulkanDeviceCapabilities capabilities = device.Capabilities;
        _descriptorState = VulkanDescriptorState.Create(
            device,
            capabilities.BindlessTextureCapacity,
            FRAMES_IN_FLIGHT
        );
        _frameResources = VulkanFrameResources.Create(device, _descriptorState);
        _bindlessTextures = new VulkanBindlessTextureTable(
            capabilities.BindlessTextureCapacity,
            _options!.Logger
        );
        _bindlessTextures.Initialize(device, _descriptorState, _frameResources);
        CreateGeometryPipelines();
        CreatePostProcessor();
        CreateSceneTargetSourceSlots();
        _options.Logger.LogInformation(
            "Bindless texture table: {Capacity} slot(s) (device limits: per-stage update-after-bind sampled images {StageLimit}, per-set {SetLimit}, per-stage samplers {SamplerLimit}); instance storage-buffer bound {InstanceBound} byte(s).",
            capabilities.BindlessTextureCapacity,
            capabilities.MaxPerStageDescriptorUpdateAfterBindSampledImages,
            capabilities.MaxDescriptorSetUpdateAfterBindSampledImages,
            capabilities.MaxPerStageDescriptorUpdateAfterBindSamplers,
            _frameResources.MaxInstanceBytes
        );
    }

    /// <summary>Creates the geometry pipeline cache for the scene pass and sample count.</summary>
    /// <remarks>
    /// The caller disposes the previous cache before the render pass. The cache snapshots the device's
    /// <c>fillModeNonSolid</c> support for wireframe pipelines.
    /// </remarks>
    private void CreateGeometryPipelines()
    {
        _geometryPipelines = VulkanGeometryPipelineCache.Create(
            _device!,
            _sceneTarget!.RenderPass,
            _descriptorState!.PipelineLayout,
            _sampleCount,
            _deviceGeneration
        );
    }

    /// <summary>
    /// Destroys the geometry pipelines before the render pass they reference is destroyed.
    /// </summary>
    private void DestroyGeometryPipelines()
    {
        VulkanGeometryPipelineCache? pipelines = _geometryPipelines;
        _geometryPipelines = null;
        pipelines?.Dispose();
    }

    /// <summary>Creates the camera post-process executor for the current device.</summary>
    private void CreatePostProcessor()
    {
        _postProcessor = VulkanPostProcessor.Create(
            _device!,
            _bindlessTextures!,
            _frameResources!,
            _descriptorState!.PipelineLayout,
            _deviceGeneration
        );
    }

    /// <summary>Allocates the scene target's internal bindless color and depth slots.</summary>
    /// <remarks>
    /// The post-process chain samples these slots: the color slot uses a linear clamp sampler and the
    /// depth slot a nearest clamp sampler. A failure releases the partial slots and samplers.
    /// </remarks>
    /// <exception cref="GraphicsException">The bindless table is full (<c>RB3001</c>).</exception>
    private void CreateSceneTargetSourceSlots()
    {
        ReleaseSceneTargetSourceSlots();
        VulkanRenderTarget sceneTarget = _sceneTarget!;
        VulkanBindlessTextureTable bindless = _bindlessTextures!;
        VulkanDevice device = _device!;
        Sampler colorSampler = default;
        Sampler depthSampler = default;
        uint colorSlot = 0;
        uint depthSlot = 0;
        try
        {
            colorSampler = CreateRenderTargetSampler(device, TextureFilter.Linear);
            if (!bindless.TryAllocate(out colorSlot))
            {
                throw new GraphicsException(
                    $"The bindless texture table is full ({bindless.Capacity} slots, including the "
                        + "reserved white slot), so the scene target's resolved color view has no slot.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            bindless.WriteTexture(colorSlot, sceneTarget.ResolvedColorView, colorSampler);
            depthSampler = CreateRenderTargetSampler(device, TextureFilter.Nearest);
            if (!bindless.TryAllocate(out depthSlot))
            {
                throw new GraphicsException(
                    $"The bindless texture table is full ({bindless.Capacity} slots, including the "
                        + "reserved white slot), so the scene target's resolved depth view has no slot.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            bindless.WriteTexture(depthSlot, sceneTarget.ResolvedDepthView, depthSampler);
            _sceneSourceColorSampler = colorSampler;
            _sceneSourceDepthSampler = depthSampler;
            _sceneSourceColorSlot = colorSlot;
            _sceneSourceDepthSlot = depthSlot;
            _sceneSourceSlotAllocationCount++;
        }
        catch
        {
            if (colorSlot != 0)
            {
                bindless.Release(colorSlot);
            }

            if (depthSlot != 0)
            {
                bindless.Release(depthSlot);
            }

            DestroyRenderTargetSampler(device, colorSampler);
            DestroyRenderTargetSampler(device, depthSampler);
            throw;
        }
    }

    /// <summary>
    /// Releases the scene target's source slots and samplers; safe when they were never allocated.
    /// </summary>
    private void ReleaseSceneTargetSourceSlots()
    {
        if (_bindlessTextures is not null)
        {
            if (_sceneSourceColorSlot != 0)
            {
                _bindlessTextures.Release(_sceneSourceColorSlot);
            }

            if (_sceneSourceDepthSlot != 0)
            {
                _bindlessTextures.Release(_sceneSourceDepthSlot);
            }
        }

        if (_device is not null)
        {
            DestroyRenderTargetSampler(_device, _sceneSourceColorSampler);
            DestroyRenderTargetSampler(_device, _sceneSourceDepthSampler);
        }

        _sceneSourceColorSlot = 0;
        _sceneSourceDepthSlot = 0;
        _sceneSourceColorSampler = default;
        _sceneSourceDepthSampler = default;
    }

    private void RecreatePresentation(WindowSurfaceState state, LogLevel logLevel, string reason)
    {
        VulkanDevice device = _device!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        // The scene render pass changes with the scene target, so every cached pipeline is invalid.
        DestroyGeometryPipelines();
        VulkanSwapchainSupport support = VulkanSwapchainSupport.Query(
            device.Vulkan,
            device.PhysicalDevice,
            _surface
        );
        VulkanSwapchainFormat format = VulkanSwapchainFormatSelection.Select(
            support.Formats,
            state.Hdr
        );
        VulkanPresentModeChoice presentMode = VulkanPresentModeSelection.Select(
            support.PresentModes,
            options.Settings.PresentMode
        );
        Extent2D extent = SelectExtent(support.Capabilities, state.RenderWidth, state.RenderHeight);
        uint imageCount = VulkanSwapchainImageCount.Select(
            support.Capabilities.MinImageCount,
            support.Capabilities.MaxImageCount
        );
        bool formatChanged = format != _swapchainFormat;

        if (_swapchain is null)
        {
            _swapchain = VulkanSwapchain.Create(
                device,
                _surface,
                format,
                presentMode.Native,
                (int)imageCount,
                extent
            );
        }
        else
        {
            _swapchain.Recreate(
                device,
                _surface,
                format,
                presentMode.Native,
                (int)imageCount,
                extent
            );
        }

        extent = _swapchain.Extent;

        VulkanRenderTarget? sceneTarget = _sceneTarget;
        _sceneTarget = null;
        if (sceneTarget is not null)
        {
            _postProcessor?.DestroyStoreBack(sceneTarget);
        }

        ReleaseSceneTargetSourceSlots();
        sceneTarget?.Dispose();
        _sceneTarget = VulkanRenderTarget.Create(
            device,
            extent,
            _sampleCount,
            Format.R16G16B16A16Sfloat,
            depth: true
        );
        CreateSceneTargetSourceSlots();
        CreateGeometryPipelines();
        VulkanPresentPipeline? presentPipeline = _presentPipeline;
        if (presentPipeline is null || formatChanged)
        {
            _presentPipeline = null;
            presentPipeline?.Dispose();
            _presentPipeline = VulkanPresentPipeline.Create(
                device,
                extent,
                format.Format,
                _sceneTarget.ResolvedColorView
            );
        }
        else
        {
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
            extent.Height
        );
    }

    /// <summary>Recreates the native surface and its presentation path.</summary>
    /// <remarks>
    /// The swapchain is destroyed before the platform surface because a surface must outlive its
    /// swapchains. A platform failure returns <see langword="false"/> with destroyed fields cleared;
    /// render-thread only, with the device idle.
    /// </remarks>
    private bool RecreateSurface(WindowSurfaceState state)
    {
        VulkanInstance instance = _instance!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        DestroyPresentationResources();
        IntPtr instanceHandle = instance.Handle.Handle;
        if (_surfaceHandle != IntPtr.Zero)
        {
            IntPtr lostSurface = _surfaceHandle;
            _surfaceHandle = IntPtr.Zero;
            _surface = default;
            try
            {
                options.SurfaceProvider.DestroySurface(instanceHandle, lostSurface);
            }
            catch (GraphicsException exception)
            {
                options.Logger.LogError(
                    exception,
                    "The lost native Vulkan surface could not be destroyed; the renderer cannot recover."
                );
                return false;
            }
        }

        try
        {
            _surfaceHandle = options.SurfaceProvider.CreateSurface(instanceHandle);
        }
        catch (GraphicsException exception)
        {
            options.Logger.LogError(
                exception,
                "The native Vulkan surface could not be recreated after surface loss; the renderer cannot recover."
            );
            return false;
        }

        if (_surfaceHandle == IntPtr.Zero)
        {
            options.Logger.LogError(
                "The native Vulkan surface could not be recreated after surface loss; the renderer cannot recover."
            );
            return false;
        }

        _surface = new SurfaceKHR { Handle = unchecked((ulong)_surfaceHandle.ToInt64()) };
        CreatePresentationPath(state);
        // The device and bindless table survive a surface loss, but the scene target was recreated, so
        // its internal post-process source slots must point at the new resolved views.
        CreateSceneTargetSourceSlots();
        CreateGeometryPipelines();
        return true;
    }

    /// <summary>
    /// Recreates the logical device and every device-level resource while keeping the instance and
    /// surface; render-thread only.
    /// </summary>
    /// <remarks>
    /// Fields are cleared before their resources are released so a later <see cref="Dispose"/> is safe.
    /// </remarks>
    private void RecreateDevice(WindowSurfaceState state)
    {
        VulkanInstance instance = _instance!;
        RendererBackendOptions options = _options!;
        DeviceWaitIdle();
        DropPendingPresent();
        DestroyGeometryPipelines();
        VulkanPostProcessor? postProcessor = _postProcessor;
        _postProcessor = null;
        postProcessor?.Dispose();
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
            _device.Capabilities.SupportedSampleCounts
        );
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
            _sampleCount
        );
    }

    /// <summary>
    /// Recreates every live render texture's GPU images, sampler, and bindless slot on the new device
    /// while keeping the public <see cref="RenderTexture"/> identity; render-thread only.
    /// </summary>
    /// <remarks>
    /// The new device, descriptor state, frame resources, and bindless table must already exist. A
    /// target that cannot be recreated is fatal for the render loop.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The recreated target is stored in the live entry or destroyed in the catch; the analyzer cannot model the field transfer."
    )]
    private void RecreateRenderTargets()
    {
        if (_renderTargets.Count == 0)
        {
            return;
        }

        RenderTargetEntry[] entries = [.. _renderTargets.Values];
        foreach (RenderTargetEntry entry in entries)
        {
            if (!IsRenderTargetIdle(entry.Lifetime))
            {
                // The device was recreated after a lost fence; no submitted frame can still reference
                // the destroyed images, so the idle check is best-effort here.
                _options!.Logger.LogDebug(
                    "Recreating render target {TargetId} while its previous frame fence did not signal.",
                    entry.Id
                );
            }

            VulkanDevice device = _device!;
            RenderTextureDescription description = entry.Lifetime.Description;
            uint maxDimension = GetMaxImageDimension2D(device);
            if (description.Width > maxDimension || description.Height > maxDimension)
            {
                throw new GraphicsException(
                    $"Render target {entry.Id} cannot be recreated on the new device: its "
                        + $"{description.Width}x{description.Height} extent exceeds the device's maximum 2D "
                        + $"image dimension {maxDimension}. Dispose and recreate the "
                        + "render texture with a supported size.",
                    DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
                    null
                );
            }

            int sampleCount = RenderTextureValidation.SelectSampleCount(
                description.SampleCount,
                device.Capabilities.SupportedSampleCounts
            );
            if (sampleCount != entry.SampleCount)
            {
                _options!.Logger.LogWarning(
                    "The render texture sample count changed from {Previous} to {Effective} after the device recreation.",
                    entry.SampleCount,
                    sampleCount
                );
            }

            VulkanRenderTarget target = VulkanRenderTarget.Create(
                device,
                new Extent2D
                {
                    Width = checked((uint)description.Width),
                    Height = checked((uint)description.Height),
                },
                sampleCount,
                ToVulkanColorFormat(description.ColorFormat),
                description.Depth
            );
            Sampler sampler;
            Sampler depthSampler;
            uint slot;
            uint depthSlot;
            try
            {
                CreateRenderTargetViewBindings(
                    device,
                    target,
                    out sampler,
                    out slot,
                    out depthSampler,
                    out depthSlot
                );
            }
            catch
            {
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
            if (target.HasDepth && entry.DepthViewResource is not null)
            {
                entry.DepthSampler = depthSampler;
                entry.DepthViewResource.View = target.ResolvedDepthView;
                entry.DepthViewResource.Sampler = depthSampler;
                entry.DepthViewResource.BindlessSlot = depthSlot;
                entry.DepthViewLifetime!.SetBindingResource(entry.DepthViewResource);
            }

            entry.Pipelines = null;
            // The recreated images have no content until a completed frame writes the target.
            entry.HasResolvedColorContent = false;
        }
    }

    /// <summary>
    /// Destroys the swapchain, scene target, and present pipeline in order.
    /// </summary>
    private void DestroyPresentationResources()
    {
        VulkanPresentPipeline? presentPipeline = _presentPipeline;
        _presentPipeline = null;
        presentPipeline?.Dispose();
        VulkanRenderTarget? sceneTarget = _sceneTarget;
        _sceneTarget = null;
        if (sceneTarget is not null)
        {
            _postProcessor?.DestroyStoreBack(sceneTarget);
        }

        ReleaseSceneTargetSourceSlots();
        sceneTarget?.Dispose();
        VulkanSwapchain? swapchain = _swapchain;
        _swapchain = null;
        swapchain?.Dispose();
    }

    /// <summary>Drops the pending present; the device is already idle.</summary>
    private void DropPendingPresent()
    {
        _pendingPresent = false;
        _pendingRenderFinished = default;
        _suboptimalRecoveryPending = false;
    }

    /// <summary>
    /// Runs a recovery request from a frame call and converts a fatal result into
    /// <see cref="GraphicsDeviceLostException"/>.
    /// </summary>
    /// <exception cref="GraphicsDeviceLostException">
    /// The recovery returned <see langword="false"/> (<c>RB3002</c>).
    /// </exception>
    private void RecoverOrThrow(RenderRecoveryReason reason)
    {
        if (Recover(reason))
        {
            return;
        }

        throw new GraphicsDeviceLostException(
            $"The Vulkan renderer could not recover from {reason}; the app stops.",
            DiagnosticCodes.GRAPHICS_DEVICE_LOST,
            null
        );
    }

    /// <summary>Routes a fault injection through the normal recovery path; test-only.</summary>
    private void HandleSimulatedFailure()
    {
        if (_simulatedFailure is not { } reason)
        {
            return;
        }

        _simulatedFailure = null;
        RecoverOrThrow(reason);
    }

    /// <summary>
    /// Destroys the live GPU asset resources before their device; render-thread only, device idle.
    /// </summary>
    /// <remarks>
    /// Vulkan requires every child object to be destroyed before its device, and the validation layer
    /// reports a live asset object as a leak. Destruction is best effort: a failure is logged and
    /// teardown continues.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Asset GPU teardown runs during backend shutdown, when the worker or the device may already be gone."
    )]
    private void DestroyAssetGpuResources()
    {
        VulkanAssetWorker? worker = _assetGpuWorker;
        if (worker is null)
        {
            return;
        }

        try
        {
            worker.DestroyAllResources();
        }
        catch (Exception exception)
        {
            _options?.Logger.LogError(
                exception,
                "The GPU asset resources could not be destroyed before the Vulkan device was destroyed."
            );
        }
    }

    private void CreateFrameResources(int imageCount)
    {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        CommandPoolCreateInfo poolInfo = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = device.Capabilities.GraphicsQueueFamily,
        };
        ThrowIfFailed(
            vulkan.CreateCommandPool(device.Device, &poolInfo, null, out _commandPool),
            "vkCreateCommandPool"
        );

        CommandBuffer[] commandBuffers = new CommandBuffer[FRAMES_IN_FLIGHT];
        CommandBufferAllocateInfo allocateInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = FRAMES_IN_FLIGHT,
        };
        fixed (CommandBuffer* commandBuffersPointer = commandBuffers)
        {
            ThrowIfFailed(
                vulkan.AllocateCommandBuffers(device.Device, &allocateInfo, commandBuffersPointer),
                "vkAllocateCommandBuffers"
            );
        }

        _commandBuffers = commandBuffers;
        _imageAvailableSemaphores = CreateSemaphores(FRAMES_IN_FLIGHT);
        _renderFinishedSemaphores = CreateSemaphores(
            Math.Max(imageCount, MIN_RENDER_FINISHED_SEMAPHORES)
        );
        _fences = CreateFences(FRAMES_IN_FLIGHT);
    }

    private Semaphore[] CreateSemaphores(int count)
    {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        Semaphore[] semaphores = new Semaphore[count];
        try
        {
            for (int index = 0; index < count; index++)
            {
                SemaphoreCreateInfo createInfo = new()
                {
                    SType = StructureType.SemaphoreCreateInfo,
                };
                ThrowIfFailed(
                    vulkan.CreateSemaphore(device.Device, &createInfo, null, out semaphores[index]),
                    "vkCreateSemaphore"
                );
            }

            return semaphores;
        }
        catch
        {
            DestroySemaphores(semaphores);
            throw;
        }
    }

    private Fence[] CreateFences(int count)
    {
        VulkanDevice device = _device!;
        Vk vulkan = device.Vulkan;
        Fence[] fences = new Fence[count];
        try
        {
            for (int index = 0; index < count; index++)
            {
                FenceCreateInfo createInfo = new()
                {
                    SType = StructureType.FenceCreateInfo,
                    Flags = FenceCreateFlags.SignaledBit,
                };
                ThrowIfFailed(
                    vulkan.CreateFence(device.Device, &createInfo, null, out fences[index]),
                    "vkCreateFence"
                );
            }

            return fences;
        }
        catch
        {
            DestroyFences(fences);
            throw;
        }
    }

    private void RecreateRenderFinishedSemaphores(int imageCount)
    {
        DestroySemaphores(_renderFinishedSemaphores);
        _renderFinishedSemaphores = CreateSemaphores(
            Math.Max(imageCount, MIN_RENDER_FINISHED_SEMAPHORES)
        );
    }

    private void DestroyFrameResources()
    {
        if (_device is not null)
        {
            Vk vulkan = _device.Vulkan;
            DestroySemaphores(_imageAvailableSemaphores);
            DestroySemaphores(_renderFinishedSemaphores);
            DestroyFences(_fences);
            if (_commandPool.Handle != 0)
            {
                vulkan.DestroyCommandPool(_device.Device, _commandPool, null);
                _commandPool = default;
            }
        }

        _commandBuffers = [];
        _imageAvailableSemaphores = [];
        _renderFinishedSemaphores = [];
        _fences = [];
    }

    private void DestroySemaphores(Semaphore[] semaphores)
    {
        if (_device is null)
        {
            return;
        }

        foreach (Semaphore semaphore in semaphores)
        {
            if (semaphore.Handle != 0)
            {
                _device.Vulkan.DestroySemaphore(_device.Device, semaphore, null);
            }
        }
    }

    private void DestroyFences(Fence[] fences)
    {
        if (_device is null)
        {
            return;
        }

        foreach (Fence fence in fences)
        {
            if (fence.Handle != 0)
            {
                _device.Vulkan.DestroyFence(_device.Device, fence, null);
            }
        }
    }

    /// <summary>
    /// Records one render pass per camera pass into the camera's target; render-thread only.
    /// </summary>
    /// <remarks>
    /// Each pass uses its clamped viewport as the render area, re-binds the shared frame descriptor set,
    /// writes its 256-byte frame block, and pushes its index so every pass reads its own <c>rbFrame</c>
    /// values. The scene target discards content before the first pass and resolves to
    /// <c>ShaderReadOnlyOptimal</c> after every pass; a runtime target waits for the previous frame,
    /// transitions for writing, and resolves back to a shader read.
    /// </remarks>
    private void RecordScenePass(
        Vk vulkan,
        CommandBuffer commandBuffer,
        FramePlan plan,
        RenderFramePacket frame,
        int frameSlot,
        HashSet<ulong> pipelines
    )
    {
        VulkanRenderTarget sceneTarget = _sceneTarget!;
        sceneTarget.DiscardContentAndTransitionToAttachments(vulkan, commandBuffer);
        PrepareFrameRenderTargets(vulkan, commandBuffer, frame, frameSlot);
        DescriptorSet descriptorSet = _frameResources!.GetDescriptorSet(frameSlot);

        ulong instanceByteOffset = 0;
        bool renderedScenePass = false;
        for (int passIndex = 0; passIndex < plan.Passes.Count; passIndex++)
        {
            PassPlan passPlan = plan.Passes[passIndex];
            CameraPass pass = passPlan.Pass;
            long targetId = pass.TargetId;
            RenderTargetEntry? targetEntry = targetId == 0 ? null : RequireRenderTarget(targetId);
            VulkanRenderTarget target = targetEntry?.Target ?? sceneTarget;
            if (!TryResolveViewport(pass, target.Extent, out Viewport viewport, out Rect2D scissor))
            {
                continue;
            }

            if (targetEntry is not null)
            {
                // Only a pass that can write color marks the target as content-bearing and records its
                // tone snapshot. ClearMode.None/Depth with no surviving draw batches leaves the color
                // untouched, so a later readback must keep the earlier content and tone. A post-effect
                // store-back also writes color and sets the same flag and snapshot below.
                bool writesColor =
                    (pass.ClearMode & ClearMode.Color) != 0 || passPlan.Batches.Count > 0;
                if (writesColor)
                {
                    targetEntry.HasResolvedColorContent = true;
                    targetEntry.Tone = frame.Tonemapping;
                }
            }

            // The first scene pass already transitioned the attachments from Undefined. Every later
            // scene pass may follow a post-process store-back that left the resolved color in a shader
            // read, so the general attachment transition handles both cases.
            if (targetId != 0)
            {
                target.TransitionForWrite(vulkan, commandBuffer);
            }
            else if (renderedScenePass)
            {
                sceneTarget.TransitionForWrite(vulkan, commandBuffer);
            }

            RenderPassBeginInfo beginInfo = new()
            {
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
            // The post-process store-back binds its own descriptor set and pipeline layout, so every
            // pass re-binds the shared geometry set instead of relying on the previous pass' binding.
            vulkan.CmdBindDescriptorSets(
                commandBuffer,
                PipelineBindPoint.Graphics,
                _descriptorState!.PipelineLayout,
                VulkanDescriptorState.GEOMETRY_SET,
                1,
                &descriptorSet,
                0,
                null
            );
            if ((pass.ClearMode & ClearMode.Color) != 0)
            {
                ClearColorAttachment(vulkan, commandBuffer, pass.ClearColor, scissor);
            }

            // A depth-less render target accepts ClearMode.Depth as a documented no-op, exactly like the
            // stencil flag on the scene target.
            if ((pass.ClearMode & ClearMode.Depth) != 0 && target.HasDepth)
            {
                ClearDepthAttachment(vulkan, commandBuffer, scissor);
            }

            WriteFrameBlock(frameSlot, passIndex, pass, frame, target.Extent);
            PushPassIndex(vulkan, commandBuffer, passIndex);
            if (passPlan.Batches.Count > 0)
            {
                VulkanGeometryPipelineCache geometryPipelines =
                    targetId == 0 ? _geometryPipelines! : GetRenderTargetPipelines(targetEntry!);
                foreach (PlannedBatch batch in passPlan.Batches)
                {
                    VulkanShaderAssetResource shader = RequireShaderResource(batch.Program);
                    VulkanMeshAssetResource mesh = RequireMeshResource(batch.Mesh);
                    Pipeline pipeline = geometryPipelines.GetOrCreate(
                        shader,
                        batch.RenderState,
                        batch.Topology,
                        batch.Wireframe
                    );
                    pipelines.Add(pipeline.Handle);

                    RenderDrawRecord first = batch.Instances[0].Record;
                    int stride =
                        ShaderAbiLayout.INSTANCE_HEADER_SIZE + first.InstanceTemplate.Length;
                    instanceByteOffset = VulkanInstanceRecordWriter.AlignUp(
                        instanceByteOffset,
                        (ulong)stride
                    );
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
                        firstInstance
                    );
                }
            }

            vulkan.CmdEndRenderPass(commandBuffer);
            if (targetId == 0)
            {
                renderedScenePass = true;
            }

            // A later camera, the present pass, or a post effect samples the resolved images, so they
            // leave an attachment layout before the chain runs or the next pass begins. A scene pass
            // with no stack needs no readable intermediate state: the next scene pass transitions the
            // attachments for its own write, and the frame-final transition covers the present pass and
            // the diagnostic depth probe.
            if (targetId != 0)
            {
                target.TransitionResolvedToShaderRead(vulkan, commandBuffer);
            }
            else if (pass.HasEnabledPostProcessEffects)
            {
                sceneTarget.TransitionResolvedToShaderRead(vulkan, commandBuffer);
            }

            if (pass.HasEnabledPostProcessEffects)
            {
                uint sourceColorSlot =
                    targetId == 0 ? _sceneSourceColorSlot : targetEntry!.ViewResource.BindlessSlot;
                uint sourceDepthSlot =
                    targetId == 0
                        ? _sceneSourceDepthSlot
                        : targetEntry!.DepthViewResource?.BindlessSlot ?? 0;
                _postProcessor!.Process(
                    vulkan,
                    commandBuffer,
                    frame,
                    pass,
                    passIndex,
                    frameSlot,
                    target,
                    sourceColorSlot,
                    sourceDepthSlot,
                    scissor,
                    ref instanceByteOffset
                );
                if (targetEntry is not null)
                {
                    // The store-back wrote the target's color for the camera viewport, so a readback
                    // must return the post-processed content and its frame's tone snapshot.
                    targetEntry.HasResolvedColorContent = true;
                    targetEntry.Tone = frame.Tonemapping;
                }
            }
        }

        sceneTarget.TransitionResolvedToShaderRead(vulkan, commandBuffer);
    }

    /// <summary>
    /// Waits for the previous frame of each runtime target and prepares sampled targets.
    /// </summary>
    /// <remarks>
    /// A target is reused only after the previous frame that wrote or sampled it completed. Sampled
    /// targets are transitioned before any render pass, because an image barrier is illegal inside one.
    /// </remarks>
    private void PrepareFrameRenderTargets(
        Vk vulkan,
        CommandBuffer commandBuffer,
        RenderFramePacket frame,
        int frameSlot
    )
    {
        _frameRenderTargets.Clear();
        _frameSampledRenderTargets.Clear();
        foreach (CameraPass pass in frame.Passes)
        {
            if (pass.TargetId != 0)
            {
                _frameRenderTargets.Add(RequireRenderTarget(pass.TargetId));
            }

            foreach (RenderDrawRecord draw in pass.Draws)
            {
                AddSampledRenderTargets(draw.GetTextureBindings());
                AddSampledRenderTargets(draw.GetInstanceTextureBindings());
            }

            // A post-process effect can sample render target views through its own material bindings,
            // so those targets must be readable before the chain runs too. The reserved source bindings
            // are overridden by the engine and are never sampled, so they are skipped.
            foreach (PostProcessEffectSnapshot effect in pass.PostProcessEffects)
            {
                AddSampledPostProcessTextures(effect);
            }
        }

        foreach (RenderTargetEntry entry in _frameRenderTargets)
        {
            PrepareRenderTargetUse(entry, frameSlot);
        }

        foreach (RenderTargetEntry entry in _frameSampledRenderTargets)
        {
            PrepareRenderTargetUse(entry, frameSlot);
            entry.Target.PrepareSampled(vulkan, commandBuffer);
        }
    }

    private void AddSampledPostProcessTextures(PostProcessEffectSnapshot effect)
    {
        ReadOnlySpan<Texture?> textures = effect.TextureValues;
        for (int index = 0; index < textures.Length; index++)
        {
            if (effect.IsReservedTextureBinding(index))
            {
                continue;
            }

            Texture? texture = textures[index];
            if (texture is not null && texture.IsRenderTargetView)
            {
                _frameSampledRenderTargets.Add(RequireRenderTarget(texture.RenderTargetId));
            }
        }
    }

    private void AddSampledRenderTargets(ReadOnlySpan<Texture?> textures)
    {
        foreach (Texture? texture in textures)
        {
            if (texture is not null && texture.IsRenderTargetView)
            {
                _frameSampledRenderTargets.Add(RequireRenderTarget(texture.RenderTargetId));
            }
        }
    }

    private void PrepareRenderTargetUse(RenderTargetEntry entry, int frameSlot)
    {
        int lastFrameSlot = entry.Lifetime.LastUseFrameSlot;
        if (lastFrameSlot >= 0 && lastFrameSlot != frameSlot)
        {
            Fence fence = _fences[lastFrameSlot];
            ThrowIfFailed(
                _device!.Vulkan.WaitForFences(_device.Device, 1, &fence, true, WAIT_TIMEOUT),
                "vkWaitForFences"
            );
        }

        entry.Lifetime.SetLastUseFrameSlot(frameSlot);
    }

    private VulkanGeometryPipelineCache GetRenderTargetPipelines(RenderTargetEntry entry) =>
        entry.Pipelines ??= VulkanGeometryPipelineCache.Create(
            _device!,
            entry.Target.RenderPass,
            _descriptorState!.PipelineLayout,
            entry.Target.SampleCount,
            _deviceGeneration
        );

    /// <summary>Records the pass-index push constant that selects a frame block.</summary>
    /// <remarks>
    /// It must be set after the previous pass's draws and before the next pass's first draw; the
    /// descriptor set stays bound because the buffer holds every pass's block.
    /// </remarks>
    private void PushPassIndex(Vk vulkan, CommandBuffer commandBuffer, int passIndex)
    {
        uint index = checked((uint)passIndex);
        vulkan.CmdPushConstants(
            commandBuffer,
            _descriptorState!.PipelineLayout,
            VulkanDescriptorState.PUSH_CONSTANT_STAGES,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_OFFSET,
            VulkanDescriptorState.PASS_INDEX_PUSH_CONSTANT_SIZE,
            &index
        );
    }

    private static bool TryResolveViewport(
        CameraPass pass,
        Extent2D extent,
        out Viewport viewport,
        out Rect2D scissor
    )
    {
        int x = Math.Clamp(pass.ViewportX, 0, (int)extent.Width);
        int y = Math.Clamp(pass.ViewportY, 0, (int)extent.Height);
        int width = Math.Clamp(pass.ViewportWidth, 0, (int)extent.Width - x);
        int height = Math.Clamp(pass.ViewportHeight, 0, (int)extent.Height - y);
        if (width <= 0 || height <= 0)
        {
            viewport = default;
            scissor = default;
            return false;
        }

        viewport = new Viewport
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            MinDepth = VulkanDepthState.MIN_DEPTH,
            MaxDepth = VulkanDepthState.MAX_DEPTH,
        };
        scissor = new Rect2D
        {
            Offset = new Offset2D { X = x, Y = y },
            Extent = new Extent2D { Width = (uint)width, Height = (uint)height },
        };
        return true;
    }

    private static void ClearColorAttachment(
        Vk vulkan,
        CommandBuffer commandBuffer,
        Color color,
        Rect2D rect
    )
    {
        ClearAttachment attachment = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue
            {
                Color = new ClearColorValue(color.R, color.G, color.B, color.A),
            },
        };
        ClearRect clearRect = new()
        {
            Rect = rect,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        vulkan.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &clearRect);
    }

    private static void ClearDepthAttachment(Vk vulkan, CommandBuffer commandBuffer, Rect2D rect)
    {
        ClearAttachment attachment = new()
        {
            AspectMask = ImageAspectFlags.DepthBit,
            ColorAttachment = 0,
            ClearValue = new ClearValue
            {
                DepthStencil = new ClearDepthStencilValue
                {
                    Depth = VulkanDepthState.CLEAR_DEPTH,
                    Stencil = 0,
                },
            },
        };
        ClearRect clearRect = new()
        {
            Rect = rect,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        vulkan.CmdClearAttachments(commandBuffer, 1, &attachment, 1, &clearRect);
    }

    /// <summary>Writes one pass's 256-byte frame block at its pass index.</summary>
    /// <remarks>
    /// System.Numerics matrices are row-vector and GLSL reads column-major; the raw row-major words are
    /// exactly the matrix the shader needs, so transposing here would invert the transform.
    /// </remarks>
    private void WriteFrameBlock(
        int frameSlot,
        int passIndex,
        CameraPass pass,
        RenderFramePacket frame,
        Extent2D targetExtent
    )
    {
        Span<byte> block = _frameResources!.GetFrameBlockSpan(frameSlot, passIndex);
        WriteMatrix(block, ShaderAbiFrame.VIEW_PROJECTION_OFFSET, pass.ViewProjection);
        // The shader multiplies a column vector by the column-major matrix it reads; raw row-major
        // System.Numerics words are the transpose of that, which is the engine's row-vector transform.
        WriteMatrix(block, ShaderAbiFrame.VIEW_OFFSET, pass.View);
        WriteMatrix(block, ShaderAbiFrame.PROJECTION_OFFSET, pass.Projection);
        WriteVector4(
            block,
            ShaderAbiFrame.CAMERA_POSITION_OFFSET,
            new Vector4(pass.CameraPosition, 1f)
        );
        WriteVector4(
            block,
            ShaderAbiFrame.VIEWPORT_OFFSET,
            new Vector4(pass.ViewportX, pass.ViewportY, pass.ViewportWidth, pass.ViewportHeight)
        );
        WriteVector4(
            block,
            ShaderAbiFrame.TIME_OFFSET,
            new Vector4((float)frame.Time, (float)frame.DeltaTime, frame.FrameIndex, 0f)
        );
        WriteVector4(
            block,
            ShaderAbiFrame.SCREEN_SIZE_OFFSET,
            new Vector4(
                targetExtent.Width,
                targetExtent.Height,
                // The scene shader and the present pass must apply the same frame exposure.
                MathF.Pow(2f, frame.Tonemapping.Exposure),
                0f
            )
        );
    }

    /// <summary>Writes every instance record of one batch at its final byte offset.</summary>
    /// <remarks>
    /// A record is the transposed world matrix, the tint, the UV rectangle, then the draw's uniform
    /// template and resolved bindless texture indices. The first instance index is
    /// <c>byteOffset / stride</c>, reaching the shader as <c>gl_InstanceIndex</c>; a per-instance texture
    /// set resolves that instance's run and every other draw repeats its shared set.
    /// </remarks>
    private void WriteInstanceRecords(
        int frameSlot,
        ulong byteOffset,
        int stride,
        PlannedBatch batch
    )
    {
        Span<byte> records = _frameResources!.GetInstanceSpan(
            frameSlot,
            byteOffset,
            (ulong)(stride * batch.InstanceCount)
        );
        for (int index = 0; index < batch.InstanceCount; index++)
        {
            PlannedInstance instance = batch.Instances[index];
            RenderDrawRecord draw = instance.Record;
            Span<byte> record = records.Slice(index * stride, stride);
            WriteMatrix(
                record,
                ShaderAbiLayout.TRANSFORM_OFFSET,
                draw.GetWorldMatrices()[instance.InstanceIndex]
            );
            WriteVector4(
                record,
                ShaderAbiLayout.COLOR_OFFSET,
                new Vector4(draw.Tint.R, draw.Tint.G, draw.Tint.B, draw.Tint.A)
            );
            WriteVector4(record, ShaderAbiLayout.UV_RECT_OFFSET, draw.UvRect);
            draw.InstanceTemplate.CopyTo(record[ShaderAbiLayout.INSTANCE_HEADER_SIZE..]);

            ReadOnlySpan<Texture?> textures = GetInstanceTextureRun(draw, instance.InstanceIndex);
            for (int textureIndex = 0; textureIndex < textures.Length; textureIndex++)
            {
                WriteUInt32(
                    record,
                    ShaderAbiLayout.TEXTURE_INDEX_START
                        + (textureIndex * ShaderAbiLayout.TEXTURE_INDEX_SIZE),
                    ResolveTextureSlot(textures[textureIndex])
                );
            }
        }
    }

    /// <summary>
    /// Gets the texture-binding run one instance writes: its contiguous slice or the shared set.
    /// </summary>
    /// <remarks>
    /// Per-instance bindings are instance-major, so the run is a pooled view and allocates nothing.
    /// </remarks>
    private static ReadOnlySpan<Texture?> GetInstanceTextureRun(
        RenderDrawRecord draw,
        int instanceIndex
    )
    {
        if (!draw.HasPerInstanceTextureBindings)
        {
            return draw.GetTextureBindings();
        }

        int bindingCount = draw.TextureBindingCount;
        return draw.GetInstanceTextureBindings().Slice(instanceIndex * bindingCount, bindingCount);
    }

    /// <summary>
    /// Resolves one texture binding to its bindless slot; render target views resolve the same way.
    /// </summary>
    private static uint ResolveTextureSlot(Texture? texture) =>
        VulkanTextureBinding.ResolveSlot(texture);

    /// <summary>Gets the Vulkan shader resource behind a shader program asset.</summary>
    /// <exception cref="GraphicsException">The asset is not a GPU shader (<c>RB3001</c>).</exception>
    internal static VulkanShaderAssetResource RequireShaderResource(EngineAsset shader)
    {
        if (shader.Resource is VulkanShaderAssetResource resource)
        {
            return resource;
        }

        throw new GraphicsException(
            $"The shader '{shader.Path}' is not a Vulkan GPU shader and cannot be used to create a pipeline.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    private static VulkanMeshAssetResource RequireMeshResource(EngineAsset mesh)
    {
        if (mesh.Resource is VulkanMeshAssetResource resource)
        {
            return resource;
        }

        throw new GraphicsException(
            $"The mesh '{mesh.Path}' is not a Vulkan GPU mesh and cannot be drawn.",
            DiagnosticCodes.GRAPHICS_INITIALIZATION_FAILED,
            null
        );
    }

    /// <summary>Computes the instance-record bytes a frame needs, with per-stride alignment.</summary>
    /// <remarks>
    /// Post-process effect records follow each camera pass' scene batches, so they are included: a later
    /// scene batch can align after an effect record and need more bytes than a scene-only sum.
    /// </remarks>
    private static ulong ComputeInstanceBytes(FramePlan plan)
    {
        ulong bytes = 0;
        for (int passIndex = 0; passIndex < plan.Passes.Count; passIndex++)
        {
            PassPlan pass = plan.Passes[passIndex];
            foreach (PlannedBatch batch in pass.Batches)
            {
                RenderDrawRecord first = batch.Instances[0].Record;
                int stride = ShaderAbiLayout.INSTANCE_HEADER_SIZE + first.InstanceTemplate.Length;
                bytes = VulkanInstanceRecordWriter.AlignUp(bytes, (ulong)stride);
                bytes += (ulong)(stride * batch.InstanceCount);
            }

            ReadOnlySpan<PostProcessEffectSnapshot> effects = pass.Pass.PostProcessEffects;
            foreach (PostProcessEffectSnapshot effect in effects)
            {
                if (!effect.Enabled)
                {
                    continue;
                }

                foreach (PostProcessInstanceRecord record in effect.InstanceLayout)
                {
                    if (record.Count <= 0 || record.Stride <= 0)
                    {
                        continue;
                    }

                    bytes = VulkanInstanceRecordWriter.AlignUp(bytes, (ulong)record.Stride);
                    bytes += (ulong)(record.Stride * record.Count);
                }
            }
        }

        return bytes;
    }

    /// <summary>Writes one System.Numerics matrix in its storage order.</summary>
    /// <remarks>
    /// The words are M11..M44 row-major; GLSL reads a <c>mat4</c> as column-major and sees the
    /// transpose, the engine's row-vector matrix in the shader's convention.
    /// </remarks>
    private static void WriteMatrix(Span<byte> destination, int offset, Matrix4x4 value) =>
        VulkanInstanceRecordWriter.WriteMatrix(destination, offset, value);

    private static void WriteVector4(Span<byte> destination, int offset, Vector4 value) =>
        VulkanInstanceRecordWriter.WriteVector4(destination, offset, value);

    private static void WriteUInt32(Span<byte> destination, int offset, uint value) =>
        VulkanInstanceRecordWriter.WriteUInt32(destination, offset, value);

    /// <summary>
    /// Gets whether a presentation result reached the presentation engine; a suboptimal result presented,
    /// an out-of-date or lost surface did not.
    /// </summary>
    internal static bool CountsAsPresented(SwapchainResult result) =>
        result is SwapchainResult.Success or SwapchainResult.Suboptimal;

    private void PresentPending()
    {
        if (!_pendingPresent)
        {
            return;
        }

        SwapchainResult result = _swapchain!.Present(
            _device!.PresentQueue,
            _pendingRenderFinished,
            _pendingImageIndex
        );
        uint presentedImageIndex = _pendingImageIndex;
        _pendingPresent = false;
        _pendingRenderFinished = default;
        _instance!.ThrowIfValidationError("presenting a frame");
        if (CountsAsPresented(result))
        {
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
        switch (result)
        {
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
                if (recoverSuboptimal)
                {
                    RecoverOrThrow(RenderRecoveryReason.SwapchainSuboptimal);
                }

                break;
        }
    }

    private int CountFramesInFlight()
    {
        VulkanDevice device = _device!;
        int count = 0;
        foreach (Fence fence in _fences)
        {
            if (device.Vulkan.GetFenceStatus(device.Device, fence) == Result.NotReady)
            {
                count++;
            }
        }

        return count;
    }

    private RenderStats CreateZeroDrawStats(long frameIndex) =>
        new(
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
            _recoveryCount
        );

    private bool HdrSelectionChanged(WindowHdrState hdr) =>
        hdr.HdrEnabled != _selectedHdrState.HdrEnabled
        || (hdr.HdrEnabled && hdr.SdrWhiteLevel != _selectedHdrState.SdrWhiteLevel);

    private void UpdateInfo()
    {
        _info = _device!.CreateEngineInfo() with
        {
            SwapchainFormat = _swapchainFormat.Description,
            HdrOutput = _swapchainFormat.IsHdr,
            SampleCount = _sampleCount,
            PresentMode = _presentMode.Public,
        };
    }

    private void DeviceWaitIdle()
    {
        if (_device is null)
        {
            return;
        }

        Result result = _device.Vulkan.DeviceWaitIdle(_device.Device);
        if (result != Result.Success)
        {
            _options?.Logger.LogWarning(
                "The Vulkan device wait-idle before recreation failed with {Result}.",
                result
            );
        }
    }

    private void LogInitialized(
        VulkanDeviceCapabilities capabilities,
        WindowSurfaceState state,
        Extent2D extent
    )
    {
        ILogger logger = _options!.Logger;
        logger.LogInformation(
            "Vulkan 1.2 renderer initialized: {GpuName}, API {ApiVersion}, validation {Validation}.",
            capabilities.DeviceName,
            VulkanVersion.Format(capabilities.ApiVersion),
            _instance!.ValidationLayersEnabled ? "enabled" : "disabled"
        );
        logger.LogInformation(
            "Presentation path: {Format}/{ColorSpace}, HDR {HdrOutput}, present mode {PresentMode}, {SampleCount}x MSAA, {ImageCount} swapchain image(s), {Width}x{Height}.",
            _swapchainFormat.Description,
            _swapchainFormat.ColorSpace,
            _swapchainFormat.IsHdr,
            _presentMode.Public,
            _sampleCount,
            _swapchain!.ImageCount,
            extent.Width,
            extent.Height
        );
        logger.LogInformation(
            "Window HDR state: enabled {HdrEnabled}, SDR white level {SdrWhiteLevel}, HDR headroom {HdrHeadroom}.",
            state.Hdr.HdrEnabled,
            state.Hdr.SdrWhiteLevel,
            state.Hdr.HdrHeadroom
        );
    }

    private void LogSelectionWarnings(WindowSurfaceState state)
    {
        ILogger logger = _options!.Logger;
        RenderSettings settings = _options.Settings;
        if (_sampleCount != settings.SampleCount)
        {
            logger.LogWarning(
                "The requested MSAA sample count {Requested} is not supported by the device; using {Effective}.",
                settings.SampleCount,
                _sampleCount
            );
        }

        if (_presentMode.FellBack)
        {
            logger.LogWarning(
                "The requested present mode {Requested} is not supported by the window surface; using {Effective}.",
                settings.PresentMode,
                _presentMode.Public
            );
        }

        if (state.Hdr.HdrEnabled && !_swapchainFormat.IsHdr)
        {
            logger.LogWarning(
                "HDR presentation was requested for the window but the surface advertises no scRGB float format; falling back to {Format} with SDR output.",
                _swapchainFormat.Description
            );
        }

        if (
            _swapchainFormat.IsHdr
            && _swapchainFormat.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr
        )
        {
            logger.LogWarning(
                "The scRGB HDR swapchain uses the sRGB nonlinear color space because VK_EXT_swapchain_colorspace offered no extended-sRGB-linear pair; the compositor may treat the surface as SDR."
            );
        }
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_initialized)
        {
            throw new InvalidOperationException("The renderer backend has not been initialized.");
        }
    }

    private static Extent2D SelectExtent(
        SurfaceCapabilitiesKHR capabilities,
        int renderWidth,
        int renderHeight
    )
    {
        Extent2D extent = VulkanSwapchainExtentSelection.Select(
            capabilities,
            renderWidth,
            renderHeight
        );
        return new Extent2D
        {
            Width = Math.Max(1u, extent.Width),
            Height = Math.Max(1u, extent.Height),
        };
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

    /// <summary>
    /// The render-thread resources of a runtime render target: images, views, samplers, pipelines.
    /// </summary>
    private sealed class RenderTargetEntry
    {
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
            Sampler depthSampler,
            TonemappingMode startupTonemappingMode
        )
        {
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
            StartupTonemappingMode = startupTonemappingMode;
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

        /// <summary>
        /// Gets or sets whether a completed frame wrote color; sampled-only frames leave it unset.
        /// A resize or recovery clears it, and readbacks return opaque black while unset.
        /// </summary>
        internal bool HasResolvedColorContent { get; set; }

        /// <summary>
        /// Gets or sets the tone snapshot of the last frame that wrote color; other passes keep it.
        /// </summary>
        internal TonemappingSnapshot Tone { get; set; } = TonemappingSnapshot.Default;

        /// <summary>Gets the startup tone-mapping mode used by readbacks.</summary>
        internal TonemappingMode StartupTonemappingMode { get; }
    }
}
