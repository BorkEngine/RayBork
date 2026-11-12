using RayBork.Internal;

namespace RayBork.Tests.Assets.Gpu;

/// <summary>
/// A fake GPU resource that records its upload and primary handle.
/// </summary>
internal sealed class FakeGpuAssetResource : GpuAssetResource {
    /// <summary>
    /// Initializes a fake resource for <paramref name="upload"/>.
    /// </summary>
    /// <param name="upload">The retained upload description.</param>
    /// <param name="handle">The primary handle the worker reports.</param>
    internal FakeGpuAssetResource(AssetGpuUpload upload, long handle) : base(upload, handle) {
    }
}
