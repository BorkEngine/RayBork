namespace RayBork.Internal;

/// <summary>Captures post-process stack entries into the immutable camera-pass snapshots.</summary>
/// <remarks>
/// <see cref="Capture"/> runs once per stack entry on the tick thread when the camera is created. A
/// disabled entry yields the shared <see cref="DisabledPostProcessEffectSnapshot"/>, so recorded order
/// stays stable.
/// </remarks>
internal static class PostProcessSnapshots
{
    /// <summary>Captures one stack entry into its immutable camera-pass snapshot.</summary>
    /// <returns>The immutable snapshot the pass records.</returns>
    /// <exception cref="ArgumentNullException">The effect is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">A shader or bound texture is disposed.</exception>
    /// <exception cref="InvalidOperationException">The effect violates its contract.</exception>
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
