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
    private bool _ownsAttachment;

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

    internal void AttachTonemapping(TonemappingSettings tonemapping) => Service.AttachTonemapping(tonemapping);

    internal RenderFramePacket BeginFrame(long frameIndex = 1, double time = 1.0, double deltaTime = 1.0 / 60.0) {
        if (!_attached) {
            // Another test helper on this thread (for example the GPU draw harness) may already own the
            // tick-thread attachment; only detach in Dispose when this host attached.
            _ownsAttachment = !TickThread.IsCurrent;
            if (_ownsAttachment) {
                TickThread.Attach();
            }

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
        Rect? viewport = null,
        RenderTexture? target = null,
        PostProcessStack? postProcess = null)
        => Service.CreateCamera(
            transform ?? Transform.Identity,
            CameraProjection.Perspective,
            fieldOfView,
            orthographicHeight: 0f,
            clearMode,
            clearColor,
            near,
            far,
            viewport,
            target,
            postProcess);

    internal Camera CreateOrthographicCamera(
        Transform? transform = null,
        float height = 4f,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        Color? clearColor = null,
        float near = 0.1f,
        float far = 100f,
        Rect? viewport = null,
        RenderTexture? target = null,
        PostProcessStack? postProcess = null)
        => Service.CreateCamera(
            transform ?? Transform.Identity,
            CameraProjection.Orthographic,
            fieldOfView: 0f,
            height,
            clearMode,
            clearColor,
            near,
            far,
            viewport,
            target,
            postProcess);

    internal void EndFrame() => Service.EndFrame();

    public void Dispose() {
        if (!_attached) {
            return;
        }

        try {
            Service.AbandonFrame();
        } finally {
            _attached = false;
            if (_ownsAttachment) {
                _ownsAttachment = false;
                TickThread.Detach();
            }
        }
    }
}
