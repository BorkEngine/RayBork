using Silk.NET.Vulkan;

namespace RayBork.Internal;

/// <summary>
/// Creates the <see cref="AssetLoadException"/> failures of the Vulkan asset worker with the stable
/// asset diagnostic codes.
/// </summary>
/// <remarks>
/// A payload that cannot be decoded is <c>RB1008</c>; a GPU resource that cannot be created,
/// recreated, or supported by the device is <c>RB1010</c>. The worker and its helpers keep the two
/// separate so a caller can tell a corrupt pack from a device that cannot run the asset.
/// </remarks>
internal static class VulkanAssetErrors {
    /// <summary>
    /// Creates a decode failure for a packed payload.
    /// </summary>
    /// <param name="payloadPath">The canonical path of the payload entry, for diagnostics.</param>
    /// <param name="reason">The concrete validation failure.</param>
    /// <returns>The decode failure with the <c>RB1008</c> diagnostic code.</returns>
    internal static AssetLoadException DecodeFailed(string payloadPath, string reason) => new(
        $"The packed asset payload '{payloadPath}' is invalid: {reason}.",
        DiagnosticCodes.ASSET_LOAD_FAILED,
        null);

    /// <summary>
    /// Creates an upload failure for one asset.
    /// </summary>
    /// <param name="canonicalPath">The canonical asset path without a leading slash.</param>
    /// <param name="reason">The concrete GPU failure.</param>
    /// <returns>The upload failure with the <c>RB1010</c> diagnostic code.</returns>
    internal static AssetLoadException UploadFailed(string canonicalPath, string reason) => new(
        $"The GPU upload of '{AssetPath.ToPublic(canonicalPath)}' failed: {reason}",
        DiagnosticCodes.ASSET_GPU_UPLOAD_FAILED,
        null);

    /// <summary>
    /// Throws an upload failure when a Vulkan command did not succeed.
    /// </summary>
    /// <param name="result">The command result.</param>
    /// <param name="canonicalPath">The canonical asset path without a leading slash.</param>
    /// <param name="operation">The Vulkan command and what it was creating.</param>
    /// <exception cref="AssetLoadException">
    /// <paramref name="result"/> is not <see cref="Result.Success"/>. The diagnostic code is
    /// <c>RB1010</c>.
    /// </exception>
    internal static void ThrowIfFailed(Result result, string canonicalPath, string operation) {
        if (result == Result.Success) {
            return;
        }

        throw UploadFailed(canonicalPath, $"the Vulkan command {operation} failed with {result}.");
    }
}
