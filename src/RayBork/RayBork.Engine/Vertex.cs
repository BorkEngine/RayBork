using System.Numerics;
using System.Runtime.InteropServices;

namespace RayBork;

/// <summary>
/// One vertex in the fixed mesh layout: position, normal, four UV sets, linear color, and tangent.
/// </summary>
/// <remarks>
/// The ABI layout is 88 sequential bytes, unpadded, so a vertex array uploads directly. Fields are
/// mutable because <see cref="MeshBuilder"/> edits vertices in place.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    /// <summary>
    /// The vertex position in mesh space.
    /// </summary>
    public Vector3 Position;

    /// <summary>
    /// The unit normal; the constructor default is <c>(0, 1, 0)</c>.
    /// </summary>
    public Vector3 Normal;

    /// <summary>
    /// The first UV set; the constructor default is zero.
    /// </summary>
    public Vector2 Uv;

    /// <summary>
    /// The second UV set; the constructor default is zero.
    /// </summary>
    public Vector2 Uv1;

    /// <summary>
    /// The third UV set; the constructor default is zero.
    /// </summary>
    public Vector2 Uv2;

    /// <summary>
    /// The fourth UV set; the constructor default is zero.
    /// </summary>
    public Vector2 Uv3;

    /// <summary>
    /// The linear RGBA color; the constructor default is opaque white.
    /// </summary>
    public Color Color;

    /// <summary>
    /// The tangent in <c>XYZ</c> with the bitangent handedness in <c>W</c>; zero by default.
    /// </summary>
    public Vector4 Tangent;

    /// <summary>
    /// Initializes a vertex from a position, defaulting omitted attributes.
    /// </summary>
    public Vertex(
        Vector3 position,
        Vector3? normal = null,
        Vector2? uv = null,
        Vector2? uv1 = null,
        Vector2? uv2 = null,
        Vector2? uv3 = null,
        Color? color = null,
        Vector4? tangent = null
    )
    {
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
