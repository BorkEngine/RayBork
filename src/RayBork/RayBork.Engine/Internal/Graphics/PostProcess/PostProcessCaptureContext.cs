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
internal readonly record struct PostProcessCaptureContext(bool TargetHasDepth, string TargetDescription);
