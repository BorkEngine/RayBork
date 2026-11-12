using RayBork.Internal;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Drives one tick-thread frame through a real <see cref="CameraService"/> and a detached
/// <see cref="RenderFramePacket"/>, without starting an app or a GPU backend.
/// </summary>
/// <remarks>
/// The host attaches the calling thread as the tick thread for the duration of the frame and detaches
/// it in <see cref="Dispose"/>. Tests create cameras through <see cref="Service"/>, inspect the packet
/// after <see cref="EndFrame"/>, and do not need the app harness unless they exercise the tick runner.
/// </remarks>
internal sealed class CameraTestHost : IDisposable {
    private bool _attached;

    internal CameraTestHost(int renderWidth = 1280, int renderHeight = 720) {
        Window = new FakePlatformWindow(static () => { }) {
            Width = renderWidth,
            Height = renderHeight,
        };
        Service = new CameraService(Window);
    }

    internal FakePlatformWindow Window { get; }

    internal CameraService Service { get; }

    internal RenderFramePacket Packet { get; private set; } = null!;

    internal RenderFramePacket BeginFrame(long frameIndex = 1, double time = 1.0, double deltaTime = 1.0 / 60.0) {
        if (!_attached) {
            TickThread.Attach();
            _attached = true;
        }

        Packet = new RenderFramePacket(frameIndex);
        Service.BeginFrame(Packet, time, deltaTime);
        return Packet;
    }

    internal Camera CreatePerspectiveCamera(
        Transform? transform = null,
        float fieldOfView = 60f,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.1f,
        float far = float.PositiveInfinity,
        Rect? viewport = null)
        => Service.CreateCamera(
            transform ?? Transform.Identity,
            fieldOfView,
            clearMode,
            clearColor,
            near,
            far,
            viewport);

    internal Camera CreateOrthographicCamera(
        Transform? transform = null,
        float height = 4f,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.1f,
        float far = 100f,
        Rect? viewport = null)
        => Service.CreateOrthographicCamera(
            transform ?? Transform.Identity,
            height,
            clearMode,
            clearColor,
            near,
            far,
            viewport);

    internal void EndFrame() => Service.EndFrame();

    public void Dispose() {
        if (!_attached) {
            return;
        }

        try {
            Service.AbandonFrame();
        } finally {
            _attached = false;
            TickThread.Detach();
        }
    }
}
