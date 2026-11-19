using System.Numerics;
using RayBork.Internal;
using Silk.NET.Vulkan;

namespace RayBork.Tests;

/// <summary>Builds sealed frames for the GPU backend tests without a <see cref="Camera"/>.</summary>
/// <remarks>
/// The clear color and mode come from the camera pass, matching the production camera path.
/// </remarks>
internal static class BackendPacketTestHelper
{
    /// <summary>Creates a sealed frame with one camera pass that clears the whole scene target.</summary>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="clearColor">The linear clear color.</param>
    /// <param name="width">The viewport width in pixels.</param>
    /// <param name="height">The viewport height in pixels.</param>
    /// <param name="clearMode">The buffers the pass clears.</param>
    /// <param name="time">The absolute frame time in seconds.</param>
    /// <param name="deltaTime">The frame delta in seconds.</param>
    /// <returns>The sealed packet.</returns>
    internal static RenderFramePacket CreateClearingPacket(
        long frameIndex,
        Color clearColor,
        int width,
        int height,
        ClearMode clearMode = ClearMode.ColorAndDepth,
        double time = 0.0,
        double deltaTime = 0.0
    )
    {
        RenderFramePacket packet = new(frameIndex);
        packet.BeginRecording(time, deltaTime);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            index: 0,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Matrix4x4.Identity,
            Vector3.Zero,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: width,
            viewportHeight: height,
            clearMode,
            clearColor
        );
        packet.Seal();
        return packet;
    }

    /// <summary>Creates a sealed clearing frame at the host's current target extent.</summary>
    /// <returns>The sealed packet.</returns>
    internal static RenderFramePacket CreateClearingPacket(
        BackendTestHost host,
        long frameIndex,
        Color clearColor,
        ClearMode clearMode = ClearMode.ColorAndDepth
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        Extent2D extent = host.SceneTarget.Extent;
        return CreateClearingPacket(
            frameIndex,
            clearColor,
            checked((int)extent.Width),
            checked((int)extent.Height),
            clearMode
        );
    }

    /// <summary>Submits and presents one clearing frame at the host's current target extent.</summary>
    internal static void SubmitClearingFrame(
        BackendTestHost host,
        long frameIndex,
        Color clearColor,
        ClearMode clearMode = ClearMode.ColorAndDepth
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        host.Backend.Submit(CreateClearingPacket(host, frameIndex, clearColor, clearMode));
        host.Backend.Present();
    }
}
