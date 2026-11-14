using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;
using RayBork.Tests.Graphics.Assets;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Builds the engine's real primitive assets from the built-in pack through the production GPU asset
/// bridge and worker, records camera passes through a real <see cref="CameraService"/>, and submits
/// them to a real <see cref="Vulkan12Backend"/>.
/// </summary>
/// <remarks>
/// <para>
/// The harness mirrors the app startup order: it creates the GPU asset bridge and worker, mounts the
/// built-in pack in a started <see cref="AssetCatalog"/>, pumps the bridge on the test thread until
/// <see cref="PrimitiveAssets.LoadAsync"/> completes, and attaches the loaded primitives to the camera
/// service. It never sleeps: every upload and build is completed by the worker while the test pumps
/// the bridge.
/// </para>
/// <para>
/// The camera service records into a detached <see cref="RenderFramePacket"/>, so the camera helpers
/// take the same tick-thread path as <c>IGame.Tick</c>. Every submitted frame is waited to idleness
/// and its retention set is released before the harness returns, so the next frame and the teardown
/// observe no in-flight asset holds.
/// </para>
/// <para>
/// Disposal runs in reverse creation order: idle wait, camera host, test textures, primitive assets,
/// catalog, GPU bridge, backend. The harness owns the textures it creates and disposes them exactly
/// once.
/// </para>
/// </remarks>
internal sealed class PrimitiveDrawGpuHarness : IDisposable {
    /// <summary>The texture binding name the built-in mesh program samples.</summary>
    internal const string MESH_TEXTURE_BINDING = "mainTexture";

    private readonly BackendTestHost _host;
    private readonly AssetGpuUploadHarness _uploads;
    private readonly AssetCatalog _catalog;
    private readonly ProceduralAssetService _proceduralAssets;
    private readonly PrimitiveAssets _primitives;
    private readonly CameraTestHost _frames;
    private readonly List<Texture> _textures = [];
    private long _nextFrameIndex = 1;
    private bool _disposed;

    private PrimitiveDrawGpuHarness(
        BackendTestHost host,
        AssetGpuUploadHarness uploads,
        AssetCatalog catalog,
        ProceduralAssetService proceduralAssets,
        PrimitiveAssets primitives,
        CameraTestHost frames) {
        _host = host;
        _uploads = uploads;
        _catalog = catalog;
        _proceduralAssets = proceduralAssets;
        _primitives = primitives;
        _frames = frames;
        _frames.Service.AttachPrimitives(primitives);
    }

    /// <summary>Gets the backend host the frames are submitted to.</summary>
    internal BackendTestHost Host => _host;

    /// <summary>Gets the linear scene target width in pixels.</summary>
    internal uint Width => _host.SceneTarget.Extent.Width;

    /// <summary>Gets the linear scene target height in pixels.</summary>
    internal uint Height => _host.SceneTarget.Extent.Height;

    /// <summary>
    /// Gets the loaded engine primitive assets, so a test can build a <see cref="Model"/> over an
    /// engine mesh and a material over an engine program.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The harness is disposed.</exception>
    internal PrimitiveAssets Primitives {
        get {
            AssertNotDisposed();
            return _primitives;
        }
    }

    /// <summary>
    /// Creates the backend and loads the real primitive assets, or skips the test when the GPU host is
    /// environmentally unavailable.
    /// </summary>
    /// <param name="sampleCount">The requested MSAA sample count.</param>
    /// <returns>The initialized harness.</returns>
    internal static PrimitiveDrawGpuHarness CreateOrSkip(int sampleCount = 4) {
        BackendTestHost host = BackendTestHost.CreateOrSkip(sampleCount: sampleCount, hdr: WindowHdrState.Sdr);
        AssetGpuUploadHarness? uploads = null;
        AssetCatalog? catalog = null;
        PrimitiveAssets? primitives = null;
        CameraTestHost? frames = null;
        try {
            uploads = new AssetGpuUploadHarness(host.Backend);
            catalog = new AssetCatalog(
                NullLoggerFactory.Instance,
                uploads.Bridge,
                BuiltInAssetPack.Bytes,
                [],
                []);
            catalog.Start();
            Task<PrimitiveAssets> load = PrimitiveAssets.LoadAsync(catalog);
            bool completed = SpinWait.SpinUntil(
                () => {
                    uploads.Pump();
                    return load.IsCompleted;
                },
                AssetGpuUploadHarness.Timeout);
            Assert.True(
                completed,
                "The engine primitive assets did not finish loading through the built-in pack and the GPU asset bridge.");
            primitives = load.GetAwaiter().GetResult();
            frames = new CameraTestHost(
                checked((int)host.SceneTarget.Extent.Width),
                checked((int)host.SceneTarget.Extent.Height));
            ProceduralAssetService proceduralAssets = ((IProceduralAssetProvider)catalog).ProceduralAssets;
            return new PrimitiveDrawGpuHarness(host, uploads, catalog, proceduralAssets, primitives, frames);
        } catch {
            primitives?.Dispose();
            frames?.Dispose();
            catalog?.Stop();
            uploads?.Dispose();
            host.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds a procedural texture through the catalog's procedural asset service and waits for its
    /// GPU upload on the test thread.
    /// </summary>
    /// <param name="width">The base-level width in pixels.</param>
    /// <param name="height">The base-level height in pixels.</param>
    /// <param name="pixels">The linear RGBA colors in row-major order from top to bottom.</param>
    /// <returns>The uploaded texture; the harness owns and disposes it.</returns>
    internal Texture CreateTexture(int width, int height, params Color[] pixels) {
        AssertNotDisposed();
        TextureBuilder builder = _proceduralAssets.CreateTextureBuilder(
            width,
            height,
            TextureFormat.Rgba8Unorm,
            generateMips: false,
            sampler: new SamplerState(TextureFilter.Nearest, TextureWrap.Clamp, 1f));
        builder.SetPixels(pixels);
        Texture texture = _uploads.WaitFor(builder.BuildAsync(TestContext.Current.CancellationToken));
        _textures.Add(texture);
        return texture;
    }

    /// <summary>
    /// Records one perspective camera pass and submits it to the backend.
    /// </summary>
    /// <param name="record">The camera draw calls.</param>
    /// <param name="transform">The camera transform, or <see langword="null"/> for the origin.</param>
    /// <param name="clearColor">The linear clear color, or <see langword="null"/> for opaque black.</param>
    /// <param name="fieldOfView">The vertical field of view in degrees.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance; positive infinity is allowed.</param>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitPerspective(
        Action<Camera> record,
        Transform? transform = null,
        Color? clearColor = null,
        float fieldOfView = 60f,
        float near = 0.1f,
        float far = float.PositiveInfinity) {
        AssertNotDisposed();
        return SubmitFrame(service => {
            using Camera camera = service.CreateCamera(
                transform ?? Transform.Identity,
                fieldOfView,
                ClearMode.ColorAndDepth,
                clearColor,
                near,
                far,
                viewport: null);
            record(camera);
        });
    }

    /// <summary>
    /// Records two sequential perspective camera passes and submits them as one frame.
    /// </summary>
    /// <param name="first">The first camera's draw calls.</param>
    /// <param name="firstTransform">The first camera transform, or <see langword="null"/> for the origin.</param>
    /// <param name="second">The second camera's draw calls.</param>
    /// <param name="secondTransform">The second camera transform, or <see langword="null"/> for the origin.</param>
    /// <param name="clearColor">The linear clear color both passes use.</param>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitPerspectivePair(
        Action<Camera> first,
        Transform? firstTransform,
        Action<Camera> second,
        Transform? secondTransform,
        Color? clearColor = null) {
        AssertNotDisposed();
        return SubmitFrame(service => {
            using (Camera camera = service.CreateCamera(
                firstTransform ?? Transform.Identity,
                60f,
                ClearMode.ColorAndDepth,
                clearColor,
                0.1f,
                float.PositiveInfinity,
                viewport: null)) {
                first(camera);
            }

            using (Camera camera = service.CreateCamera(
                secondTransform ?? Transform.Identity,
                60f,
                ClearMode.ColorAndDepth,
                clearColor,
                0.1f,
                float.PositiveInfinity,
                viewport: null)) {
                second(camera);
            }
        });
    }

    /// <summary>
    /// Records one orthographic camera pass and submits it to the backend.
    /// </summary>
    /// <param name="record">The camera draw calls.</param>
    /// <param name="transform">The camera transform, or <see langword="null"/> for the origin.</param>
    /// <param name="clearColor">The linear clear color, or <see langword="null"/> for opaque black.</param>
    /// <param name="height">The visible world-space height.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The finite far clip distance.</param>
    /// <returns>The sealed packet that was submitted.</returns>
    internal RenderFramePacket SubmitOrthographic(
        Action<Camera> record,
        Transform? transform = null,
        Color? clearColor = null,
        float height = 4f,
        float near = 0.1f,
        float far = 100f) {
        AssertNotDisposed();
        return SubmitFrame(service => {
            using Camera camera = service.CreateOrthographicCamera(
                transform ?? Transform.Identity,
                height,
                ClearMode.ColorAndDepth,
                clearColor,
                near,
                far,
                viewport: null);
            record(camera);
        });
    }

    /// <summary>
    /// Reads one resolved linear scene color pixel.
    /// </summary>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadColor(uint x, uint y) {
        AssertNotDisposed();
        VulkanReadback.ReadSceneColorPixel(_host.Device, _host.SceneTarget, x, y, out float r, out float g, out float b);
        return (r, g, b);
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        try {
            _host.Backend.WaitForFrameResourcesIdle();
            _frames.Service.DetachPrimitives();
            _frames.Dispose();
            foreach (Texture texture in _textures) {
                texture.Dispose();
            }

            _primitives.Dispose();
            _catalog.Stop();
            _uploads.Dispose();
        } finally {
            _host.Dispose();
        }
    }

    private RenderFramePacket SubmitFrame(Action<CameraService> record) {
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
