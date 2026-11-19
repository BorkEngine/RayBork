using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// The per-device sampler cache: one sampler per <see cref="SamplerState"/>, shared by all textures.
/// </summary>
/// <remarks>
/// The cache owns every sampler it creates and a texture keeps only the handle. The cache is cleared
/// on device recreation, and <see cref="DestroyAll"/> runs on the render thread before the device
/// is destroyed.
/// </remarks>
internal sealed unsafe class VulkanSamplerCache : IDisposable
{
    private const float LOD_CLAMP_NONE = 1000f;

    private readonly Func<VulkanDevice> _deviceAccessor;
    private readonly ILogger _logger;
    private readonly Dictionary<SamplerState, Sampler> _samplers = [];
    private bool _disposed;

    /// <summary>Initializes the cache for the backend's current logical device.</summary>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal VulkanSamplerCache(Func<VulkanDevice> deviceAccessor, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(deviceAccessor);
        ArgumentNullException.ThrowIfNull(logger);

        _deviceAccessor = deviceAccessor;
        _logger = logger;
    }

    /// <summary>Gets the number of samplers the cache owns.</summary>
    internal int Count => _samplers.Count;

    /// <summary>Returns the cached sampler for a texture state, creating it on first use.</summary>
    /// <returns>The cached sampler.</returns>
    /// <exception cref="AssetLoadException">
    /// The state is invalid (<c>RB1008</c>) or sampler creation failed (<c>RB1010</c>).
    /// </exception>
    internal Sampler GetOrCreate(
        TextureFilter filter,
        TextureWrap wrap,
        float maxAnisotropy,
        string canonicalPath,
        string payloadPath
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(filter))
        {
            throw VulkanAssetErrors.DecodeFailed(
                payloadPath,
                $"the sampler filter '{filter}' is not defined"
            );
        }

        if (!Enum.IsDefined(wrap))
        {
            throw VulkanAssetErrors.DecodeFailed(
                payloadPath,
                $"the sampler wrap mode '{wrap}' is not defined"
            );
        }

        if (!float.IsFinite(maxAnisotropy) || maxAnisotropy < 1f)
        {
            throw VulkanAssetErrors.DecodeFailed(
                payloadPath,
                "the maximum anisotropy must be finite and at least 1"
            );
        }

        SamplerState state = new(filter, wrap, maxAnisotropy);
        if (_samplers.TryGetValue(state, out Sampler cached))
        {
            return cached;
        }

        Sampler sampler = CreateSampler(filter, wrap, maxAnisotropy, canonicalPath);
        _samplers.Add(state, sampler);
        return sampler;
    }

    /// <summary>Destroys every cached sampler with the device and clears the cache.</summary>
    /// <remarks>The caller has waited for device idleness; the call is idempotent.</remarks>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Shutdown destroys every cached sampler best-effort so one failure cannot leave the rest alive."
    )]
    internal void DestroyAll()
    {
        if (_samplers.Count == 0)
        {
            return;
        }

        VulkanDevice? device = null;
        try
        {
            device = _deviceAccessor();
        }
        catch (ObjectDisposedException)
        {
            // The device is already gone; its samplers were destroyed with it.
        }

        if (device is not null)
        {
            Vk vulkan = device.Vulkan;
            foreach (Sampler sampler in _samplers.Values)
            {
                if (sampler.Handle == 0)
                {
                    continue;
                }

                try
                {
                    vulkan.DestroySampler(device.Device, sampler, null);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "The cached Vulkan asset sampler could not be destroyed."
                    );
                }
            }
        }

        _samplers.Clear();
    }

    /// <summary>Destroys the cache. The call is idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyAll();
    }

    private Sampler CreateSampler(
        TextureFilter filter,
        TextureWrap wrap,
        float maxAnisotropy,
        string canonicalPath
    )
    {
        VulkanDevice device = _deviceAccessor();
        bool nearest = filter == TextureFilter.Nearest;
        bool anisotropic =
            filter == TextureFilter.Anisotropic && device.Capabilities.SamplerAnisotropy;
        float anisotropy = 1f;
        if (filter == TextureFilter.Anisotropic)
        {
            if (anisotropic)
            {
                anisotropy = Math.Clamp(
                    maxAnisotropy,
                    1f,
                    Math.Max(1f, device.Capabilities.MaxSamplerAnisotropy)
                );
            }
            else
            {
                _logger.LogWarning(
                    "The texture '{Path}' requested anisotropic filtering (maximum anisotropy {MaxAnisotropy}), but the device does not enable the Vulkan samplerAnisotropy feature; the sampler falls back to linear filtering.",
                    AssetPath.ToPublic(canonicalPath),
                    maxAnisotropy
                );
            }
        }

        SamplerCreateInfo createInfo = new()
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = nearest ? Filter.Nearest : Filter.Linear,
            MinFilter = nearest ? Filter.Nearest : Filter.Linear,
            MipmapMode = nearest ? SamplerMipmapMode.Nearest : SamplerMipmapMode.Linear,
            AddressModeU = ToAddressMode(wrap),
            AddressModeV = ToAddressMode(wrap),
            AddressModeW = ToAddressMode(wrap),
            MipLodBias = 0f,
            AnisotropyEnable = anisotropic,
            MaxAnisotropy = anisotropy,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MinLod = 0f,
            MaxLod = LOD_CLAMP_NONE,
            BorderColor = BorderColor.FloatOpaqueBlack,
            UnnormalizedCoordinates = false,
        };
        Result result = device.Vulkan.CreateSampler(
            device.Device,
            &createInfo,
            null,
            out Sampler sampler
        );
        if (result != Result.Success || sampler.Handle == 0)
        {
            throw VulkanAssetErrors.UploadFailed(
                canonicalPath,
                $"vkCreateSampler failed with {result}."
            );
        }

        return sampler;
    }

    private static SamplerAddressMode ToAddressMode(TextureWrap wrap) =>
        wrap switch
        {
            TextureWrap.Repeat => SamplerAddressMode.Repeat,
            TextureWrap.Clamp => SamplerAddressMode.ClampToEdge,
            TextureWrap.Mirror => SamplerAddressMode.MirroredRepeat,
            _ => throw new ArgumentOutOfRangeException(
                nameof(wrap),
                wrap,
                "The sampler wrap mode is not a defined texture wrap mode."
            ),
        };
}
