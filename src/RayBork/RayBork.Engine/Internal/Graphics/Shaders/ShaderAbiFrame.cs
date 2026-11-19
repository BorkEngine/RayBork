namespace RayBork.Internal;

/// <summary>The std430 layout of the 256-byte frame block in descriptor set 0, binding 0.</summary>
/// <remarks>Offsets match the generated <c>RayBorkFrame</c> GLSL struct and must change with it. Every
/// member is 16-byte aligned, so one array element is a 256-byte stride.</remarks>
internal static class ShaderAbiFrame
{
    /// <summary>The complete frame block size in bytes.</summary>
    internal const int BLOCK_SIZE = 256;

    /// <summary>The offset of the <c>mat4 ViewProjection</c>.</summary>
    internal const int VIEW_PROJECTION_OFFSET = 0;

    /// <summary>The offset of the <c>mat4 View</c>.</summary>
    internal const int VIEW_OFFSET = 64;

    /// <summary>The offset of the <c>mat4 Projection</c>.</summary>
    internal const int PROJECTION_OFFSET = 128;

    /// <summary>The offset of the <c>vec4 CameraPosition</c>.</summary>
    internal const int CAMERA_POSITION_OFFSET = 192;

    /// <summary>The offset of the <c>vec4 Viewport</c>.</summary>
    internal const int VIEWPORT_OFFSET = 208;

    /// <summary>The offset of the <c>vec4 Time</c>.</summary>
    internal const int TIME_OFFSET = 224;

    /// <summary>The offset of the <c>vec4 ScreenSize</c>.</summary>
    internal const int SCREEN_SIZE_OFFSET = 240;
}
