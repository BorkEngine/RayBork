namespace RayBork.Internal;

/// <summary>
/// The target facts a post-process effect's capture validates against.
/// </summary>
/// <param name="TargetHasDepth">
/// Whether the camera's target has a depth attachment the chain can bind to the reserved
/// <c>sourceDepth</c> binding. The app scene target always has depth.
/// </param>
/// <param name="TargetDescription">
/// A human-readable name of the target for exception messages, such as "the app scene target" or
/// "render texture 3".
/// </param>
/// <param name="SsaoAssets">
/// The app's preloaded built-in SSAO assets, or <see langword="null"/> when the app did not attach
/// them. A custom effect ignores them; <see cref="SsaoEffect"/> requires them.
/// </param>
/// <param name="BloomAssets">
/// The app's preloaded built-in bloom assets, or <see langword="null"/> when the app did not attach
/// them. A custom effect ignores them; <see cref="BloomEffect"/> requires them.
/// </param>
internal readonly record struct PostProcessCaptureContext(
    bool TargetHasDepth,
    string TargetDescription,
    SsaoAssets? SsaoAssets = null,
    BloomAssets? BloomAssets = null);
