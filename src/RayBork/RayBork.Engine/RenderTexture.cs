using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A runtime render target with multisampled color and depth attachments and resolved
/// single-sample views.
/// </summary>
/// <remarks>
/// The render texture owns <see cref="ColorTexture"/> and <see cref="DepthTexture"/>; dispose it
/// rather than the views, which keep their identity across <see cref="Resize"/> and device
/// recovery. Readback reads the resolved color image after post-processing, never depth.
/// </remarks>
public sealed class RenderTexture : IDisposable
{
    private readonly RenderTextureTargetLifetime _lifetime;
    private readonly RenderTextureService _service;

    /// <summary>
    /// Initializes a render texture from its target lifetime, color view, and optional depth view.
    /// </summary>
    internal RenderTexture(
        RenderTextureTargetLifetime lifetime,
        Texture colorTexture,
        Texture? depthTexture,
        RenderTextureService service
    )
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(colorTexture);
        ArgumentNullException.ThrowIfNull(service);

        _lifetime = lifetime;
        _service = service;
        ColorTexture = colorTexture;
        DepthTexture = depthTexture;
    }

    /// <summary>Gets the width in pixels.</summary>
    /// <exception cref="ObjectDisposedException">The render texture is disposed.</exception>
    public int Width
    {
        get
        {
            ThrowIfDisposed();
            return _lifetime.Description.Width;
        }
    }

    /// <summary>Gets the height in pixels.</summary>
    public int Height
    {
        get
        {
            ThrowIfDisposed();
            return _lifetime.Description.Height;
        }
    }

    /// <summary>
    /// Gets the effective MSAA sample count, re-selected after a device recovery.
    /// </summary>
    public int SampleCount
    {
        get
        {
            ThrowIfDisposed();
            return _lifetime.SampleCount;
        }
    }

    /// <summary>Gets the single-sample resolved color view.</summary>
    public Texture ColorTexture { get; }

    /// <summary>
    /// Gets the resolved depth view, or <see langword="null"/> when the target has no depth
    /// attachment.
    /// </summary>
    public Texture? DepthTexture { get; }

    /// <summary>Gets whether the color format stores linear HDR values.</summary>
    public bool IsHdr
    {
        get
        {
            ThrowIfDisposed();
            return RenderTextureValidation.IsHdr(_lifetime.Description.ColorFormat);
        }
    }

    /// <summary>
    /// Gets whether the render texture was disposed; stays readable after disposal.
    /// </summary>
    public bool IsDisposed => _lifetime.IsDisposed;

    /// <summary>Gets the render-thread target id recorded in frame packets.</summary>
    internal long TargetId => _lifetime.TargetId;

    /// <summary>Gets the target lifetime retained by the frame that uses it.</summary>
    internal RenderTextureTargetLifetime Lifetime => _lifetime;

    /// <summary>
    /// Recreates the target's images in place at a new size; the contents become undefined.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is not positive, exceeds <c>16384</c>, or exceeds the device limit.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A camera targets or samples the target, or an in-flight frame references it.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The render texture or the app's renderer is disposed.
    /// </exception>
    /// <exception cref="GraphicsException">The target's images could not be recreated.</exception>
    /// <remarks>
    /// Blocks until the render thread recreates the target; a same-size resize is a no-op.
    /// </remarks>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        _service.ResizeAsync(_lifetime, width, height).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Reads the resolved color image as raw scene-linear colors indexed <c>[y, x]</c>, with row zero
    /// at the top.
    /// </summary>
    /// <returns>Scene-linear colors with alpha; HDR values above <c>1</c> are preserved.</returns>
    /// <exception cref="OperationCanceledException">
    /// Canceled before the readback completed; cancellation is best effort once queued.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The render texture or the app's renderer is disposed.
    /// </exception>
    /// <exception cref="GraphicsException">The target's color image could not be copied.</exception>
    /// <remarks>
    /// Formats decode as stored: <see cref="TextureFormat.Rgba16Float"/> with HDR preserved,
    /// <see cref="TextureFormat.Rgba8Unorm"/> divided by 255, and
    /// <see cref="TextureFormat.Rgba8Srgb"/> sRGB-decoded, with linear alpha. An unwritten or
    /// just-resized target reads as opaque black. The call stalls the render thread, so reserve
    /// it for screenshots and tests.
    /// </remarks>
    public Task<Color[,]> ReadColorsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _service.ReadColorsAsync(_lifetime, cancellationToken);
    }

    /// <summary>
    /// Reads the resolved color image as display-referred RGBA8 pixels, row-major from the
    /// top-left.
    /// </summary>
    /// <returns>
    /// <c>Width * Height * 4</c> bytes with every alpha channel at <c>255</c>; not premultiplied.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Canceled before the readback completed; cancellation is best effort once queued.
    /// </exception>
    /// <exception cref="GraphicsException">The target's color image could not be copied.</exception>
    /// <remarks>
    /// Applies the exposure, AgX white, and contrast of the last frame that wrote the target and the
    /// startup <see cref="TonemappingMode"/>; an unwritten target reads as opaque black.
    /// </remarks>
    public Task<byte[]> ReadPixelsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _service.ReadPixelsAsync(_lifetime, cancellationToken);
    }

    /// <summary>Releases the views and queues the target's GPU images; idempotent.</summary>
    public void Dispose()
    {
        if (_lifetime.IsDisposed)
        {
            return;
        }

        ColorTexture.RetireRenderTargetView();
        DepthTexture?.RetireRenderTargetView();
        RenderTextureService.ReleaseTarget(this);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_lifetime.IsDisposed, this);
}
