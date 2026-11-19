using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Creates the Vulkan asset worker's <see cref="AssetLoadException"/> failures with stable codes.
/// </summary>
/// <remarks>
/// A payload that cannot be decoded is <c>RB1008</c>; a resource the device cannot create or
/// support is <c>RB1010</c>, so a corrupt pack is distinguishable from an unsupported device.
/// </remarks>
internal static class VulkanAssetErrors
{
    /// <summary>Creates the <c>RB1008</c> decode failure for a packed payload.</summary>
    internal static AssetLoadException DecodeFailed(string payloadPath, string reason) =>
        new(
            $"The packed asset payload '{payloadPath}' is invalid: {reason}.",
            DiagnosticCodes.ASSET_LOAD_FAILED,
            null
        );

    /// <summary>Creates the <c>RB1010</c> upload failure for one asset.</summary>
    internal static AssetLoadException UploadFailed(string canonicalPath, string reason) =>
        new(
            $"The GPU upload of '{AssetPath.ToPublic(canonicalPath)}' failed: {reason}",
            DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
            null
        );

    /// <summary>Throws an upload failure when a Vulkan command did not succeed.</summary>
    /// <exception cref="AssetLoadException">The command failed (<c>RB1010</c>).</exception>
    internal static void ThrowIfFailed(Result result, string canonicalPath, string operation)
    {
        if (result == Result.Success)
        {
            return;
        }

        throw UploadFailed(canonicalPath, $"the Vulkan command {operation} failed with {result}.");
    }
}
