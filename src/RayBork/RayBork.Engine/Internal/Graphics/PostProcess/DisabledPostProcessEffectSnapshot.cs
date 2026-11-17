namespace RayBork.Internal;

/// <summary>
/// The snapshot of an effect that was disabled when its camera was created.
/// </summary>
/// <remarks>
/// A disabled entry still occupies its stack position in the camera pass snapshot, so a later stack
/// change cannot shift the captured order. The renderer skips it, and it retains nothing.
/// </remarks>
internal sealed class DisabledPostProcessEffectSnapshot : PostProcessEffectSnapshot
{
    /// <summary>The shared instance; a disabled snapshot is immutable.</summary>
    internal static readonly DisabledPostProcessEffectSnapshot Instance = new();

    private DisabledPostProcessEffectSnapshot() { }

    /// <inheritdoc/>
    internal override bool IsShared => true;

    /// <inheritdoc/>
    internal override bool Enabled => false;

    /// <inheritdoc/>
    internal override void Run(PostProcessContext context) =>
        throw new InvalidOperationException(
            "A disabled post-process effect snapshot cannot run; the renderer skips disabled entries."
        );
}
