using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One runtime render target: the multisampled color and depth attachments, their single-sample resolved
/// images, and the sampled <see cref="ColorTexture"/> and <see cref="DepthTexture"/> views.
/// </summary>
/// <remarks>
/// <para>
/// Create a render texture with <see cref="EngineContext.CreateRenderTextureAsync"/>. The call returns
/// after the render thread allocated the images. A camera created with
/// <see cref="EngineContext.CreateCamera"/> and the <c>target</c> parameter writes the target instead of
/// the app's linear HDR scene target. A later camera pass in the same tick can sample
/// <see cref="ColorTexture"/> or <see cref="DepthTexture"/>; the renderer inserts the image barriers, and
/// a camera that samples the target it writes is rejected. The target's contents are undefined until a
/// camera writes them.
/// </para>
/// <para>
/// <see cref="ColorTexture"/> is a <see cref="Texture"/> view over the resolved color image, and
/// <see cref="DepthTexture"/> is a view over the resolved depth image when
/// <see cref="RenderTextureDescription.Depth"/> is <see langword="true"/>. Both report an empty
/// <see cref="Texture.Path"/>, sample and batch like any texture, and are owned by this object. Disposing
/// a view directly throws <see cref="InvalidOperationException"/>; dispose the render texture instead.
/// Disposing the render texture releases the views and queues the target's GPU images for destruction
/// after every frame that used them completes. <see cref="Dispose"/> is idempotent.
/// </para>
/// <para>
/// The views keep their identity across a device recovery: the renderer replaces the GPU image and
/// bindless slot behind the same public <see cref="Texture"/> objects. Resizing, readback, and
/// post-processing are later M8 tasks.
/// </para>
/// </remarks>
public sealed class RenderTexture : IDisposable {
    private readonly RenderTextureTargetLifetime _lifetime;

    /// <summary>
    /// Initializes a render texture after the render thread created its target and color and depth views.
    /// The engine creates one per successful <see cref="EngineContext.CreateRenderTextureAsync"/> request.
    /// </summary>
    /// <param name="lifetime">The target lifetime, holding the single owner reference.</param>
    /// <param name="colorTexture">The sampled color view over the resolved color image.</param>
    /// <param name="depthTexture">
    /// The sampled depth view over the resolved depth image, or <see langword="null"/> when the target has
    /// no depth attachment.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="lifetime"/> or <paramref name="colorTexture"/> is <c>null</c>.
    /// </exception>
    internal RenderTexture(
        RenderTextureTargetLifetime lifetime,
        Texture colorTexture,
        Texture? depthTexture) {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(colorTexture);

        _lifetime = lifetime;
        ColorTexture = colorTexture;
        DepthTexture = depthTexture;
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

    /// <summary>
    /// Gets the effective MSAA sample count of the target's color and depth attachments. A device
    /// recovery re-selects the count from the recreated device and updates this value.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public int SampleCount {
        get {
            ThrowIfDisposed();
            return _lifetime.SampleCount;
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
    /// Gets the single-sample resolved depth texture, or <see langword="null"/> when the target was
    /// created with <see cref="RenderTextureDescription.Depth"/> <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view reports <see cref="TextureFormat.Depth32Float"/>, <see cref="Texture.IsSrgb"/>
    /// <see langword="false"/>, and a nearest-filter clamp sampler. The renderer resolves a multisampled
    /// depth attachment into this image and transitions it to a shader-read layout after a writing pass,
    /// so a later camera can sample it in the same tick. A camera that samples the depth view of the
    /// target it writes is rejected.
    /// </para>
    /// <para>
    /// The render texture owns the view: a direct <see cref="Texture.Dispose"/> throws
    /// <see cref="InvalidOperationException"/>; dispose the render texture instead. The property returns
    /// the same view object after a device recovery; only its GPU image is replaced. After disposal the
    /// view stays readable through <see cref="Texture.IsDisposed"/> and <see cref="Texture.Path"/>; every
    /// other view member throws <see cref="ObjectDisposedException"/>.
    /// </para>
    /// </remarks>
    public Texture? DepthTexture { get; }

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
    /// Gets whether the render texture was disposed. It stays readable after disposal, including after
    /// the renderer destroyed every live target at shutdown.
    /// </summary>
    public bool IsDisposed => _lifetime.IsDisposed;

    /// <summary>Gets the render-thread target id the frame packet records.</summary>
    internal long TargetId => _lifetime.TargetId;

    /// <summary>Gets the target lifetime the frame's retention set holds.</summary>
    internal RenderTextureTargetLifetime Lifetime => _lifetime;

    /// <summary>
    /// Releases the color and depth views and queues the target's GPU images for destruction. Idempotent.
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
        DepthTexture?.RetireRenderTargetView();
        RenderTextureService.ReleaseTarget(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_lifetime.IsDisposed, this);
}
