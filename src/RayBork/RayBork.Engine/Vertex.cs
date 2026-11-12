using System.Numerics;
using System.Runtime.InteropServices;

namespace RayBork;

/// <summary>
/// One vertex in the fixed engine mesh layout: position, normal, four UV sets, linear color, and
/// tangent.
/// </summary>
/// <remarks>
/// <para>
/// The layout is fixed by the shader ABI and is exactly 88 bytes: three floats for
/// <see cref="Position"/>, three for <see cref="Normal"/>, two for each of <see cref="Uv"/>,
/// <see cref="Uv1"/>, <see cref="Uv2"/>, and <see cref="Uv3"/>, four for the linear
/// <see cref="Color"/>, and four for <see cref="Tangent"/>. The fields are sequential in declaration
/// order with no padding, so a vertex array can be uploaded directly. Custom vertex layouts are
/// post-1.0; every mesh uses this struct.
/// </para>
/// <para>
/// The fields are public and mutable because a <see cref="MeshBuilder"/> edits vertices in place. A
/// <c>default(Vertex)</c> has a zero normal, zero UVs, transparent black color, and a zero tangent;
/// use the constructor to get the meaningful defaults of a new vertex.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex {
    /// <summary>
    /// The vertex position in mesh space.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// The unit vertex normal. The constructor default is <c>(0, 1, 0)</c>.
    /// </summary>
    public Vector3 Normal;

    /// <summary>
    /// The first UV set. The constructor default is zero.
    /// </summary>
    public Vector2 Uv;

    /// <summary>
    /// The second UV set. The constructor default is zero.
    /// </summary>
    public Vector2 Uv1;

    /// <summary>
    /// The third UV set. The constructor default is zero.
    /// </summary>
    public Vector2 Uv2;

    /// <summary>
    /// The fourth UV set. The constructor default is zero.
    /// </summary>
    public Vector2 Uv3;

    /// <summary>
    /// The linear RGBA vertex color. The constructor default is opaque white.
    /// </summary>
    public Color Color;

    /// <summary>
    /// The tangent vector in <c>XYZ</c> and the bitangent handedness in <c>W</c>. The constructor
    /// default is zero.
    /// </summary>
    public Vector4 Tangent;

    /// <summary>
    /// Initializes a vertex from a position and optional attributes. The attribute defaults are the
    /// values a new mesh vertex starts with.
    /// </summary>
    /// <param name="position">The vertex position in mesh space.</param>
    /// <param name="normal">The unit normal, or <c>(0, 1, 0)</c> when omitted.</param>
    /// <param name="uv">The first UV set, or zero when omitted.</param>
    /// <param name="uv1">The second UV set, or zero when omitted.</param>
    /// <param name="uv2">The third UV set, or zero when omitted.</param>
    /// <param name="uv3">The fourth UV set, or zero when omitted.</param>
    /// <param name="color">The linear color, or opaque white when omitted.</param>
    /// <param name="tangent">
    /// The tangent vector and handedness, or zero when omitted. <see cref="MeshBuilder.ComputeTangents"/>
    /// computes it from the normals and UVs.
    /// </param>
    public Vertex(
        Vector3 position,
        Vector3? normal = null,
        Vector2? uv = null,
        Vector2? uv1 = null,
        Vector2? uv2 = null,
        Vector2? uv3 = null,
        Color? color = null,
        Vector4? tangent = null) {
        Position = position;
        Normal = normal ?? new Vector3(0f, 1f, 0f);
        Uv = uv ?? Vector2.Zero;
        Uv1 = uv1 ?? Vector2.Zero;
        Uv2 = uv2 ?? Vector2.Zero;
        Uv3 = uv3 ?? Vector2.Zero;
        Color = color ?? Color.White;
        Tangent = tangent ?? Vector4.Zero;
    }
}
