using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using RayBork.Tests.Graphics.Assets;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Loads the engine's real primitive assets from the built-in pack through a production GPU asset
/// bridge and records camera passes through a real <see cref="CameraService"/>.
/// </summary>
/// <remarks>
/// The harness owns the GPU bridge, catalog, assets, and every texture and font it creates, and
/// disposes them exactly once. Every wait pumps the bridge on the test thread; the harness never
/// sleeps.
/// </remarks>
internal sealed class PrimitiveDrawGpuHarness : IDisposable
{
    /// <summary>The texture binding name the built-in mesh program samples.</summary>
    internal const string MESH_TEXTURE_BINDING = "mainTexture";

    private static readonly SamplerState _defaultSampler = new(
        TextureFilter.Nearest,
        TextureWrap.Clamp,
        1f
    );

    private readonly BackendTestHost _host;
    private readonly AssetGpuUploadHarness _uploads;
    private readonly AssetCatalog _catalog;
    private readonly ProceduralAssetService _proceduralAssets;
    private readonly PrimitiveAssets _primitives;
    private readonly SsaoAssets _ssaoAssets;
    private readonly CameraTestHost _frames;
    private readonly List<Texture> _textures = [];
    private readonly List<Font> _fonts = [];
    private long _nextFrameIndex = 1;
    private bool _disposed;

    private PrimitiveDrawGpuHarness(
        BackendTestHost host,
        AssetGpuUploadHarness uploads,
        AssetCatalog catalog,
        ProceduralAssetService proceduralAssets,
        PrimitiveAssets primitives,
        SsaoAssets ssaoAssets,
        CameraTestHost frames
    )
    {
        _host = host;
        _uploads = uploads;
        _catalog = catalog;
        _proceduralAssets = proceduralAssets;
        _primitives = primitives;
        _ssaoAssets = ssaoAssets;
        _frames = frames;
        _frames.Service.AttachPrimitives(primitives);
        _frames.Service.AttachSsaoAssets(ssaoAssets);
    }

    /// <summary>Gets the backend host the frames are submitted to.</summary>
    internal BackendTestHost Host => _host;

    /// <summary>Gets the linear scene target width in pixels.</summary>
    internal uint Width => _host.SceneTarget.Extent.Width;

    /// <summary>Gets the linear scene target height in pixels.</summary>
    internal uint Height => _host.SceneTarget.Extent.Height;

    /// <summary>Gets the loaded engine primitive assets.</summary>
    /// <exception cref="ObjectDisposedException">The harness is disposed.</exception>
    internal PrimitiveAssets Primitives
    {
        get
        {
            AssertNotDisposed();
            return _primitives;
        }
    }

    /// <summary>
    /// Creates the backend and loads the real assets, or skips when the GPU host is unavailable.
    /// </summary>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <returns>The initialized harness.</returns>
    internal static PrimitiveDrawGpuHarness CreateOrSkip(int sampleCount = 4)
    {
        BackendTestHost host = BackendTestHost.CreateOrSkip(
            sampleCount: sampleCount,
            hdr: WindowHdrState.Sdr
        );
        AssetGpuUploadHarness? uploads = null;
        AssetCatalog? catalog = null;
        PrimitiveAssets? primitives = null;
        SsaoAssets? ssaoAssets = null;
        CameraTestHost? frames = null;
        try
        {
            uploads = new AssetGpuUploadHarness(host.Backend);
            catalog = new AssetCatalog(
                NullLoggerFactory.Instance,
                uploads.Bridge,
                BuiltInAssetPack.Bytes,
                [],
                []
            );
            catalog.Start();
            Task<PrimitiveAssets> load = PrimitiveAssets.LoadAsync(catalog);
            bool completed = SpinWait.SpinUntil(
                () =>
                {
                    uploads.Pump();
                    return load.IsCompleted;
                },
                AssetGpuUploadHarness.Timeout
            );
            Assert.True(
                completed,
                "The engine primitive assets did not finish loading through the built-in pack and the GPU asset bridge."
            );
            primitives = load.GetAwaiter().GetResult();

            Task<SsaoAssets> ssaoLoad = SsaoAssets.LoadAsync(catalog);
            completed = SpinWait.SpinUntil(
                () =>
                {
                    uploads.Pump();
                    return ssaoLoad.IsCompleted;
                },
                AssetGpuUploadHarness.Timeout
            );
            Assert.True(
                completed,
                "The built-in SSAO assets did not finish loading through the built-in pack and the GPU asset bridge."
            );
            ssaoAssets = ssaoLoad.GetAwaiter().GetResult();

            frames = new CameraTestHost(
                checked((int)host.SceneTarget.Extent.Width),
                checked((int)host.SceneTarget.Extent.Height)
            );
            ProceduralAssetService proceduralAssets = (
                (IProceduralAssetProvider)catalog
            ).ProceduralAssets;
            return new PrimitiveDrawGpuHarness(
                host,
                uploads,
                catalog,
                proceduralAssets,
                primitives,
                ssaoAssets,
                frames
            );
        }
        catch
        {
            primitives?.Dispose();
            ssaoAssets?.Dispose();
            frames?.Dispose();
            catalog?.Stop();
            uploads?.Dispose();
            host.Dispose();
            throw;
        }
    }

    /// <summary>Creates a procedural texture and waits for its GPU upload.</summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="pixels">The linear RGBA colors in row-major order.</param>
    /// <returns>The uploaded texture, owned and disposed by the harness.</returns>
    internal Texture CreateTexture(int width, int height, params Color[] pixels) =>
        CreateTexture(width, height, _defaultSampler, pixels);

    /// <summary>Creates a procedural texture with an explicit sampler and waits for its upload.</summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="sampler">The sampler state; use a linear filter for a sampled gradient.</param>
    /// <param name="pixels">The linear RGBA colors in row-major order.</param>
    /// <returns>The uploaded texture, owned and disposed by the harness.</returns>
    internal Texture CreateTexture(
        int width,
        int height,
        SamplerState sampler,
        params Color[] pixels
    )
    {
        AssertNotDisposed();
        TextureBuilder builder = _proceduralAssets.CreateTextureBuilder(
            width,
            height,
            TextureFormat.Rgba8Unorm,
            generateMips: false,
            sampler: sampler
        );
        builder.SetPixels(pixels);
        Texture texture = _uploads.WaitFor(
            builder.BuildAsync(TestContext.Current.CancellationToken)
        );
        _textures.Add(texture);
        return texture;
    }

    /// <summary>Loads a font from the mounted catalog and waits for its CPU decode.</summary>
    /// <param name="path">The canonical font path; the built-in Fira Sans face by default.</param>
    /// <returns>The loaded font, owned by the harness.</returns>
    internal Font LoadFont(string path = EngineDefaults.DEFAULT_FONT_PATH)
    {
        AssertNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Task<Font> load = _catalog.Load<Font>(path);
        bool completed = SpinWait.SpinUntil(
            () =>
            {
                _uploads.Pump();
                return load.IsCompleted;
            },
            AssetGpuUploadHarness.Timeout
        );
        Assert.True(completed, $"The font '{path}' did not finish loading from the built-in pack.");
        Font font = load.GetAwaiter().GetResult();
        _fonts.Add(font);
        return font;
    }

    /// <summary>Requests every glyph of the text and waits until each has a generated texture.</summary>
    /// <param name="font">The loaded font.</param>
    /// <param name="text">The text the test needs ready.</param>
    /// <param name="frameIndex">The frame index to begin the font's cache clock at.</param>
    /// <remarks>The waits pump the GPU bridge and are bounded by the harness timeout.</remarks>
    internal void WarmGlyphs(Font font, string text, long frameIndex = 1)
    {
        AssertNotDisposed();
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);

        font.BeginFrame(frameIndex);
        foreach (Rune rune in text.EnumerateRunes())
        {
            font.TryGetGlyphId(rune, out int glyphId);
            FrameAssetRetention probe = new();
            Texture? texture;
            try
            {
                texture = font.AcquireGlyphTexture(glyphId, probe);
            }
            finally
            {
                probe.Release();
            }

            if (texture is null)
            {
                Task settled = font.WhenGlyphSettledAsync(glyphId);
                bool completed = SpinWait.SpinUntil(
                    () =>
                    {
                        _uploads.Pump();
                        return settled.IsCompleted;
                    },
                    AssetGpuUploadHarness.Timeout
                );
                Assert.True(completed, "The glyph texture generation did not settle.");
                settled.GetAwaiter().GetResult();
            }

            Assert.True(
                font.TryGetGlyph(rune, out _),
                $"The glyph U+{rune.Value:X4} did not generate an MSDF texture."
            );
        }
    }

    /// <summary>Records one perspective camera pass and submits it to the backend.</summary>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitPerspective(
        Action<Camera> record,
        Transform? transform = null,
        Color? clearColor = null,
        float fieldOfView = 60f,
        float near = 0.1f,
        float far = float.PositiveInfinity,
        PostProcessStack? postProcess = null,
        RenderTexture? target = null
    )
    {
        AssertNotDisposed();
        return SubmitFrame(service =>
        {
            using Camera camera = service.CreateCamera(
                transform ?? Transform.Identity,
                CameraProjection.Perspective,
                fieldOfView,
                orthographicHeight: 0f,
                ClearMode.ColorAndDepth,
                clearColor,
                near,
                far,
                viewport: null,
                target,
                postProcess
            );
            record(camera);
        });
    }

    /// <summary>Records two sequential perspective passes and submits them as one frame.</summary>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitPerspectivePair(
        Action<Camera> first,
        Transform? firstTransform,
        Action<Camera> second,
        Transform? secondTransform,
        Color? clearColor = null
    )
    {
        AssertNotDisposed();
        return SubmitFrame(service =>
        {
            using (
                Camera camera = service.CreateCamera(
                    firstTransform ?? Transform.Identity,
                    CameraProjection.Perspective,
                    60f,
                    orthographicHeight: 0f,
                    ClearMode.ColorAndDepth,
                    clearColor,
                    0.1f,
                    float.PositiveInfinity,
                    viewport: null
                )
            )
            {
                first(camera);
            }

            using (
                Camera camera = service.CreateCamera(
                    secondTransform ?? Transform.Identity,
                    CameraProjection.Perspective,
                    60f,
                    orthographicHeight: 0f,
                    ClearMode.ColorAndDepth,
                    clearColor,
                    0.1f,
                    float.PositiveInfinity,
                    viewport: null
                )
            )
            {
                second(camera);
            }
        });
    }

    /// <summary>Records one orthographic camera pass and submits it to the backend.</summary>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitOrthographic(
        Action<Camera> record,
        Transform? transform = null,
        Color? clearColor = null,
        float height = 4f,
        float near = 0.1f,
        float far = 100f,
        PostProcessStack? postProcess = null,
        RenderTexture? target = null
    )
    {
        AssertNotDisposed();
        return SubmitFrame(service =>
        {
            using Camera camera = service.CreateCamera(
                transform ?? Transform.Identity,
                CameraProjection.Orthographic,
                fieldOfView: 0f,
                height,
                ClearMode.ColorAndDepth,
                clearColor,
                near,
                far,
                viewport: null,
                target,
                postProcess
            );
            record(camera);
        });
    }

    /// <summary>Reads one resolved linear scene color pixel.</summary>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadColor(uint x, uint y)
    {
        AssertNotDisposed();
        VulkanReadback.ReadSceneColorPixel(
            _host.Device,
            _host.SceneTarget,
            x,
            y,
            out float r,
            out float g,
            out float b
        );
        return (r, g, b);
    }

    /// <summary>Reads one resolved linear color pixel from a runtime render target.</summary>
    /// <param name="target">The render texture to read.</param>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadRenderTargetColor(RenderTexture target, uint x, uint y)
    {
        AssertNotDisposed();
        ArgumentNullException.ThrowIfNull(target);
        if (
            !_host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? renderTarget)
            || renderTarget is null
        )
        {
            throw new InvalidOperationException(
                $"The render target {target.TargetId} is not live."
            );
        }

        VulkanReadback.ReadRenderTargetColorPixel(
            _host.Device,
            renderTarget,
            x,
            y,
            out float r,
            out float g,
            out float b
        );
        return (r, g, b);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _host.Backend.WaitForFrameResourcesIdle();
            _frames.Service.DetachPrimitives();
            _frames.Service.DetachSsaoAssets();
            _frames.Dispose();
            foreach (Font font in _fonts)
            {
                if (!font.IsDisposed)
                {
                    font.Dispose();
                }
            }

            foreach (Texture texture in _textures)
            {
                texture.Dispose();
            }

            _primitives.Dispose();
            _ssaoAssets.Dispose();
            _catalog.Stop();
            _uploads.Dispose();
        }
        finally
        {
            _host.Dispose();
        }
    }

    private RenderFramePacket SubmitFrame(Action<CameraService> record)
    {
        RenderFramePacket packet = _frames.BeginFrame(_nextFrameIndex++);
        record(_frames.Service);
        _frames.EndFrame();
        _host.Backend.Submit(packet);
        _host.Backend.Present();
        _host.Backend.WaitForFrameResourcesIdle();
        packet.Retention.Release();
        return packet;
    }

    private void AssertNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
