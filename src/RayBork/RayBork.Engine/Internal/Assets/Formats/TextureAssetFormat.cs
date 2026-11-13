using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// The built-in packed texture format: it decodes the descriptor, validates it and the KTX2 bytes, and
/// builds the texture upload.
/// </summary>
/// <remarks>
/// The format is stateless, so one shared instance serves every texture load on the loader thread. The
/// payload is a complete KTX2 2.0 file; the runtime never parses a source image format.
/// </remarks>
internal sealed class TextureAssetFormat : IEngineAssetFormat<Texture> {
    /// <summary>
    /// The shared format instance.
    /// </summary>
    internal static readonly TextureAssetFormat Instance = new();

    private TextureAssetFormat() {
    }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Texture;

    /// <inheritdoc/>
    public DecodedAsset<Texture> Decode(AssetFormatContext context) {
        ArgumentNullException.ThrowIfNull(context);

        TextureAssetDescriptor descriptor = AssetSerialization.Deserialize<TextureAssetDescriptor>(
            context.DescriptorPayload);
        byte[] payload = context.ReadRawPayload(descriptor.Payload, "KTX2");
        ValidateTexture(descriptor, payload, context.CanonicalPath);
        TextureAssetDescriptor captured = descriptor;
        return new DecodedAsset<Texture>(
            new TextureGpuUpload(context.CanonicalPath, descriptor, payload),
            lifetime => new Texture(lifetime, captured));
    }

    private static void ValidateTexture(
        TextureAssetDescriptor descriptor,
        byte[] payload,
        string canonical) {
        if (descriptor.Width == 0 || descriptor.Height == 0) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the texture descriptor must declare a non-zero size");
        }

        if (descriptor.MipLevels == 0) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the texture descriptor must declare at least one mip level");
        }

        uint maxMipLevels = (uint)BitOperations.Log2(Math.Max(descriptor.Width, descriptor.Height)) + 1u;
        if (descriptor.MipLevels > maxMipLevels) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the texture descriptor declares {descriptor.MipLevels} mip levels; "
                    + $"{descriptor.Width}x{descriptor.Height} supports at most {maxMipLevels}");
        }

        if (!Enum.IsDefined(descriptor.Format)
            || descriptor.Format is TextureFormat.Rgba16Float or TextureFormat.Depth32Float) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the texture descriptor format {(int)descriptor.Format} is not a packed texture format");
        }

        bool srgbFormat = descriptor.Format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb;
        if (descriptor.IsSrgb != srgbFormat) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the texture descriptor sRGB flag {descriptor.IsSrgb} does not match format "
                    + $"{descriptor.Format}");
        }

        if (!Enum.IsDefined(descriptor.Filter) || !Enum.IsDefined(descriptor.Wrap)) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the texture descriptor sampler state is not valid");
        }

        if (!float.IsFinite(descriptor.MaxAnisotropy) || descriptor.MaxAnisotropy < 1f) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                "the texture descriptor maximum anisotropy must be finite and at least 1");
        }

        if (payload.Length == 0) {
            throw AssetLoadErrors.CreateLoadFailure(canonical, "the texture KTX2 payload is empty");
        }
    }
}
