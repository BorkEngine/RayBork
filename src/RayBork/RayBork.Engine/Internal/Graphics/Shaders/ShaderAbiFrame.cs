namespace RayBork.Internal;

/// <summary>
/// The std430 layout of one frame block record in the descriptor set 0, binding 0 frame storage
/// buffer.
/// </summary>
/// <remarks>
/// The block is exactly 256 bytes. The renderer writes these offsets and the generated GLSL declares
/// the matching <c>RayBorkFrame</c> struct array; the two must change together. Every member is a
/// <c>mat4</c> or a <c>vec4</c>, so the std430 alignment and offsets equal the std140 ones and one
/// array element is a 256-byte stride. <c>CameraPosition.w</c> is always 1, <c>Time</c> packs seconds,
/// delta seconds, the frame index, and zero, and <c>ScreenSize</c> packs the target width, height,
/// exposure multiplier, and zero.
/// </remarks>
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
