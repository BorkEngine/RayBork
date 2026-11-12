using System.Numerics;

namespace RayBork;

/// <summary>
/// One part of a multi-part <see cref="Model"/>: a mesh, its material, and the part-local transform.
/// </summary>
/// <remarks>
/// <para>
/// A model is a flat list of parts; it has no node hierarchy. glTF node transforms are baked into
/// <see cref="LocalTransform"/>, which is applied before the draw's instance transform:
/// <c>world = LocalTransform.ToMatrix() * instanceTransform.ToMatrix()</c> under the row-vector
/// convention. A draw never composes transforms through <see cref="Transform.WithParent"/>, which can
/// throw on shear.
/// </para>
/// <para>
/// <see cref="Shader"/> is the part material and is never <see langword="null"/>. The material starts
/// with the program's bake-default uniform values, and a draw snapshots its current uniform cursor and
/// texture bindings at call time. A <see cref="Model"/> takes ownership of every distinct part
/// material and disposes it with the model; construct one material per part.
/// </para>
/// <para>
/// <see cref="Bounds"/> is computed from <see cref="Mesh.Bounds"/> transformed by
/// <see cref="LocalTransform"/>; it is the axis-aligned box of the eight transformed corners and is not
/// passed in. It is model-space bounds after the part's local transform.
/// </para>
/// </remarks>
public readonly struct ModelPart {
    /// <summary>
    /// Initializes a part from its name, mesh, and material.
    /// </summary>
    /// <param name="name">The part name from the source scene.</param>
    /// <param name="mesh">The shared mesh asset.</param>
    /// <param name="shader">The part material; the model takes ownership of it.</param>
    /// <param name="renderState">
    /// The render state for this part, or <see langword="null"/> for
    /// <see cref="Shader.DefaultRenderState"/>.
    /// </param>
    /// <param name="localTransform">
    /// The part-local transform, or <see langword="null"/> for <see cref="Transform.Identity"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/>, <paramref name="mesh"/>, or <paramref name="shader"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ObjectDisposedException">The mesh or material is disposed.</exception>
    public ModelPart(
        string name,
        Mesh mesh,
        Shader shader,
        RenderState? renderState = null,
        Transform? localTransform = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(shader);
        ObjectDisposedException.ThrowIf(mesh.IsDisposed, mesh);
        ObjectDisposedException.ThrowIf(shader.IsDisposed, shader);

        Name = name;
        Mesh = mesh;
        Shader = shader;
        RenderState = renderState ?? shader.DefaultRenderState;
        LocalTransform = localTransform ?? Transform.Identity;
        Bounds = TransformBounds(mesh.Bounds, LocalTransform.ToMatrix());
    }

    /// <summary>Gets the part name from the source scene.</summary>
    public string Name { get; }

    /// <summary>Gets the mesh asset for the part. The model holds one owner reference to it.</summary>
    public Mesh Mesh { get; }

    /// <summary>Gets the material for the part. The model owns it and disposes it with the model.</summary>
    public Shader Shader { get; }

    /// <summary>
    /// Gets the render state used for the part. A <see langword="null"/> constructor argument resolved to
    /// the material's default render state.
    /// </summary>
    public RenderState RenderState { get; }

    /// <summary>Gets the part-local transform, applied before the draw's instance transform.</summary>
    public Transform LocalTransform { get; }

    /// <summary>Gets the model-space bounds of the part, after its local transform.</summary>
    public BoundingBox Bounds { get; }

    private static BoundingBox TransformBounds(BoundingBox bounds, Matrix4x4 matrix) {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++) {
            Vector3 corner = new(
                (cornerIndex & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (cornerIndex & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (cornerIndex & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            Vector3 transformed = Vector3.Transform(corner, matrix);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }

        return new BoundingBox(minimum, maximum);
    }
}
