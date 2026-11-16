using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One runtime render target: the multisampled color attachment, its single-sample resolved color image,
/// and the sampled <see cref="ColorTexture"/> view.
/// </summary>
/// <remarks>
/// <para>
/// Create a render texture with <see cref="EngineContext.CreateRenderTextureAsync"/>. The call returns
/// after the render thread allocated the images. A camera created with
/// <see cref="EngineContext.CreateCamera"/> and the <c>target</c> parameter writes the target instead of
/// the app's linear HDR scene target. A later camera pass in the same tick can sample
/// <see cref="ColorTexture"/>; the renderer inserts the image barriers, and a camera that samples the
/// target it writes is rejected. The target's contents are undefined until a camera writes them.
/// </para>
/// <para>
/// <see cref="ColorTexture"/> is a <see cref="Texture"/> view owned by this object. It reports an empty
/// <see cref="Texture.Path"/>, it samples and batches like any texture, and disposing it directly throws
/// <see cref="InvalidOperationException"/>; dispose the render texture instead. Disposing the render
/// texture releases the view and queues the target's GPU images for destruction after every frame that
/// used them completes. <see cref="Dispose"/> is idempotent.
/// </para>
/// <para>
/// The depth attachment is internal in this task: <see cref="RenderTextureDescription.Depth"/> controls
/// whether one exists, but there is no public depth view yet. This task ships the color path only;
/// resizing, depth sampling, and readback are later M8 tasks.
/// </para>
/// </remarks>
public sealed class RenderTexture : IDisposable {
    private readonly RenderTextureTargetLifetime _lifetime;
    private readonly int _sampleCount;

    /// <summary>
    /// Initializes a render texture after the render thread created its target and color view. The
    /// engine creates one per successful <see cref="EngineContext.CreateRenderTextureAsync"/> request.
    /// </summary>
    /// <param name="lifetime">The target lifetime, holding the single owner reference.</param>
    /// <param name="colorTexture">The sampled color view over the resolved image.</param>
    /// <param name="sampleCount">The effective MSAA sample count.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal RenderTexture(
        RenderTextureTargetLifetime lifetime,
        Texture colorTexture,
        int sampleCount) {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(colorTexture);

        _lifetime = lifetime;
        ColorTexture = colorTexture;
        _sampleCount = sampleCount;
    }

    /// <summary>Gets the target width in pixels.</summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public int Width {
        get {
            ThrowIfDisposed();
            return _lifetime.Description.Width;
        }
    }

    /// <summary>Gets the target height in pixels.</summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public int Height {
        get {
            ThrowIfDisposed();
            return _lifetime.Description.Height;
        }
    }

    /// <summary>Gets the effective MSAA sample count of the target's color and depth attachments.</summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public int SampleCount {
        get {
            ThrowIfDisposed();
            return _sampleCount;
        }
    }

    /// <summary>
    /// Gets the single-sample resolved color texture. The render texture owns this view; do not dispose
    /// it. A later camera can sample it after a camera wrote the target in the same tick.
    /// </summary>
    /// <remarks>
    /// The property returns the same view object after disposal so a caller that already holds it can
    /// read <see cref="Texture.IsDisposed"/> and <see cref="Texture.Path"/>; every other view member
    /// throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    public Texture ColorTexture { get; }

    /// <summary>
    /// Gets whether the target's color format stores linear HDR values. It is <see langword="true"/> for
    /// <see cref="TextureFormat.Rgba16Float"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public bool IsHdr {
        get {
            ThrowIfDisposed();
            return RenderTextureValidation.IsHdr(_lifetime.Description.ColorFormat);
        }
    }

    /// <summary>
    /// Gets whether the render texture was disposed. It stays readable after disposal.
    /// </summary>
    public bool IsDisposed => _lifetime.IsDisposed;

    /// <summary>Gets the render-thread target id the frame packet records.</summary>
    internal long TargetId => _lifetime.TargetId;

    /// <summary>Gets the target lifetime the frame's retention set holds.</summary>
    internal RenderTextureTargetLifetime Lifetime => _lifetime;

    /// <summary>Gets the description the target was created from.</summary>
    internal RenderTextureDescription Description => _lifetime.Description;

    /// <summary>
    /// Releases the color view and queues the target's GPU images for destruction. Idempotent.
    /// </summary>
    /// <remarks>
    /// A target that is the destination or a sample source of a recorded in-flight frame stays alive
    /// until that frame completes; the target is destroyed only after its frame fence signals.
    /// </remarks>
    public void Dispose() {
        if (_lifetime.IsDisposed) {
            return;
        }

        ColorTexture.RetireRenderTargetView();
        RenderTextureService.ReleaseTarget(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_lifetime.IsDisposed, this);
}
