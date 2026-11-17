namespace RayBork.Internal;

/// <summary>
/// Captures the public post-process stack entries into the immutable snapshots a camera pass records.
/// </summary>
/// <remarks>
/// The camera service calls <see cref="Capture"/> on the tick thread once per stack entry when the
/// camera is created. A disabled entry yields the shared <see cref="DisabledPostProcessEffectSnapshot"/>
/// so the recorded order is stable even when the effect's enabled value changes later.
/// </remarks>
internal static class PostProcessSnapshots
{
    /// <summary>
    /// Captures one effect entry.
    /// </summary>
    /// <param name="effect">The effect to capture.</param>
    /// <param name="targetHasDepth">Whether the camera target has a resolved depth image.</param>
    /// <param name="targetDescription">The target name used in contract diagnostics.</param>
    /// <param name="ssaoAssets">
    /// The app's preloaded SSAO built-ins, or <see langword="null"/> for a harness that does not run
    /// SSAO.
    /// </param>
    /// <param name="bloomAssets">
    /// The app's preloaded bloom built-ins, or <see langword="null"/> for a harness that does not run
    /// bloom.
    /// </param>
    /// <returns>The immutable snapshot the pass records.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The effect's shader or a bound texture is disposed.</exception>
    /// <exception cref="InvalidOperationException">The effect violates the post-process contract.</exception>
    internal static PostProcessEffectSnapshot Capture(
        PostProcessEffect effect,
        bool targetHasDepth,
        string targetDescription,
        SsaoAssets? ssaoAssets = null,
        BloomAssets? bloomAssets = null
    )
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDescription);

        if (!effect.Enabled)
        {
            return DisabledPostProcessEffectSnapshot.Instance;
        }

        PostProcessEffectSnapshot snapshot = effect.Capture(
            new PostProcessCaptureContext(
                targetHasDepth,
                targetDescription,
                ssaoAssets,
                bloomAssets
            )
        );
        snapshot.Attach(effect);
        return snapshot;
    }
}
