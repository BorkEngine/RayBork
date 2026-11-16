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
/// bindless slot behind the same public <see cref="Texture"/> objects. <see cref="Resize"/> recreates
/// the target's images in place: the target id, the color format, the depth flag, the effective sample
/// count, and the two view objects are unchanged, while <see cref="Width"/>, <see cref="Height"/>, and
/// the views' dimensions report the new size. The target's contents are undefined after a resize.
/// <see cref="ReadColorsAsync"/> and <see cref="ReadPixelsAsync"/> read the target's resolved color
/// image, not its depth image; post-processing is a later M8 task.
/// </para>
/// </remarks>
public sealed class RenderTexture : IDisposable {
    private readonly RenderTextureTargetLifetime _lifetime;
    private readonly RenderTextureService _service;

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
    /// <param name="service">The request handoff that recreates the target on the render thread.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="lifetime"/>, <paramref name="colorTexture"/>, or <paramref name="service"/> is
    /// <c>null</c>.
    /// </exception>
    internal RenderTexture(
        RenderTextureTargetLifetime lifetime,
        Texture colorTexture,
        Texture? depthTexture,
        RenderTextureService service) {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(colorTexture);
        ArgumentNullException.ThrowIfNull(service);

        _lifetime = lifetime;
        _service = service;
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
    /// Recreates the target's color and depth images, render pass, framebuffer, bindless views, and
    /// pipeline cache at a new size. The target id, color format, depth attachment, effective sample
    /// count, and both <see cref="ColorTexture"/>/<see cref="DepthTexture"/> objects keep their identity;
    /// the public dimensions and the views' <see cref="Texture.Width"/>/<see cref="Texture.Height"/>
    /// report the new size. The target's contents are undefined after the call.
    /// </summary>
    /// <param name="width">The new width in pixels.</param>
    /// <param name="height">The new height in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, exceeds <c>16384</c>, or exceeds the current device's maximum 2D image
    /// dimension.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A camera targets the target, a draw samples one of its views, or a recorded or in-flight frame
    /// still references it. Resize the target only when no frame that used it is in flight; the call never
    /// blocks on a current-frame hold.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The render texture or the app's renderer is disposed.</exception>
    /// <exception cref="GraphicsException">The renderer could not recreate the target's images.</exception>
    /// <remarks>
    /// <para>
    /// Call it from the tick thread, like <see cref="EngineContext.CreateRenderTextureAsync"/>. The call
    /// blocks until the render thread recreated the target; the render thread services the request
    /// independently of the tick thread, so a call from <see cref="IGame.Tick"/> stalls only that tick.
    /// A resize to the current size recreates nothing and, for a live target with a running renderer,
    /// does not throw. The render thread updates the target description and both views' dimensions
    /// together before it completes the request, so the public dimensions and
    /// <see cref="Texture.Width"/>/<see cref="Texture.Height"/> never report a split size.
    /// </para>
    /// <para>
    /// The device-limit check runs on the render thread against the current device's maximum 2D image
    /// dimension, so a size that is valid at creation can still be rejected after a device recreation.
    /// A later device recovery recreates the resized target from the updated description.
    /// </para>
    /// </remarks>
    public void Resize(int width, int height) {
        ThrowIfDisposed();
        _service.ResizeAsync(_lifetime, width, height).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads the target's resolved color image as raw scene-linear colors, indexed <c>[y, x]</c> with the
    /// first dimension as the height and row zero at the top.
    /// </summary>
    /// <param name="cancellationToken">Cancels the readback before the render thread completes it.</param>
    /// <returns>
    /// The raw scene-linear colors, alpha included. HDR components above <c>1</c> are preserved; no AgX
    /// tone mapping or sRGB encoding is applied.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the render thread completed the readback.
    /// Cancellation after the request was queued is best effort; the target is never left in a partial
    /// state.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The render texture is disposed, or the app's renderer stopped.
    /// </exception>
    /// <exception cref="GraphicsException">The renderer could not copy the target's color image.</exception>
    /// <remarks>
    /// <para>
    /// The value comes from the single-sample resolved color image after the last completed frame that
    /// wrote or sampled the target, after any post-processed output that later writes the target. The
    /// render thread reads the current description and image, so the dimensions survive
    /// <see cref="Resize"/> and a device recovery. It reads the color image only, never depth.
    /// </para>
    /// <para>
    /// The formats are decoded from their stored values: <see cref="TextureFormat.Rgba16Float"/> half
    /// floats with HDR preserved, <see cref="TextureFormat.Rgba8Unorm"/> bytes divided by 255, and
    /// <see cref="TextureFormat.Rgba8Srgb"/> sRGB-decoded to linear. Alpha is linear and included for
    /// every format. The target's contents are undefined before a camera writes them; an unwritten or
    /// just-resized target, including one that a frame only sampled, returns the defined default of
    /// opaque black.
    /// </para>
    /// <para>
    /// The call is legal from <see cref="IGame.Setup"/> and <see cref="IGame.Tick"/>. It stalls the render
    /// thread while it waits for the target's last-use fence and copies the image, so it is intended for
    /// screenshots and tests, not per-frame work. <see cref="IGame.Setup"/> can <c>await</c> it;
    /// <see cref="IGame.Tick"/> is <c>void</c>, so a tick caller blocks on the returned task with
    /// <c>GetAwaiter().GetResult()</c> and stalls that tick until the readback completes.
    /// </para>
    /// </remarks>
    public Task<Color[,]> ReadColorsAsync(CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        return _service.ReadColorsAsync(_lifetime, cancellationToken);
    }

    /// <summary>
    /// Reads the target's resolved color image as display-referred RGBA8 pixels in row-major top-left
    /// order, after exposure, AgX tone mapping, and sRGB encoding.
    /// </summary>
    /// <param name="cancellationToken">Cancels the readback before the render thread completes it.</param>
    /// <returns>
    /// The RGBA8 pixels, row-major from the top-left, with length <c>Width * Height * 4</c>. Every alpha
    /// channel is <c>255</c> because the SDR present path writes opaque output; the pixels are not
    /// premultiplied.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled before the render thread completed the readback.
    /// Cancellation after the request was queued is best effort; the target is never left in a partial
    /// state.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The render texture is disposed, or the app's renderer stopped.
    /// </exception>
    /// <exception cref="GraphicsException">The renderer could not copy the target's color image.</exception>
    /// <remarks>
    /// <para>
    /// This is the SDR screenshot path, not scRGB/HDR output. It uses the exposure, AgX white, and AgX
    /// contrast of the last completed frame that wrote the target, and the app's startup
    /// <see cref="TonemappingMode"/>; the runtime tone values are never read from the game's live
    /// settings on the render thread. A target that no frame wrote uses the built-in default tone
    /// snapshot and the startup mode and returns opaque black, because it has no resolved content yet.
    /// The conversion matches the present shader's SDR path within a small rounding tolerance because it
    /// uses the same CPU AgX reference.
    /// </para>
    /// <para>
    /// The color image is read after the last completed frame that wrote or sampled the target, with the
    /// current dimensions after <see cref="Resize"/> and device recovery. It reads color only, never
    /// depth. The call is legal from <see cref="IGame.Setup"/> and <see cref="IGame.Tick"/> and stalls the
    /// render thread, so use it for screenshots and tests, not per-frame work. A <see cref="IGame.Tick"/>
    /// caller blocks on the returned task with <c>GetAwaiter().GetResult()</c> because
    /// <see cref="IGame.Tick"/> is <c>void</c>.
    /// </para>
    /// </remarks>
    public Task<byte[]> ReadPixelsAsync(CancellationToken cancellationToken = default) {
        ThrowIfDisposed();
        return _service.ReadPixelsAsync(_lifetime, cancellationToken);
    }

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
