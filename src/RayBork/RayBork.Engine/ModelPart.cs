using System.Numerics;

namespace RayBork;

/// <summary>One part of a <see cref="Model"/>: a mesh, material, and local transform.</summary>
/// <remarks>
/// The local transform applies before the draw's instance transform. A <see cref="Model"/> takes
/// ownership of every distinct part material and disposes it with the model.
/// </remarks>
public readonly struct ModelPart
{
    /// <summary>Initializes a part from a name, mesh, material, and options.</summary>
    /// <remarks>Null state and transform use the material default and identity.</remarks>
    /// <exception cref="ArgumentException">The name is blank or an argument is invalid.</exception>
    /// <exception cref="ObjectDisposedException">The mesh or material is disposed.</exception>
    public ModelPart(
        string name,
        Mesh mesh,
        Shader shader,
        RenderState? renderState = null,
        Transform? localTransform = null
    )
    {
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

    /// <summary>Gets the mesh; the model holds one owner reference to it.</summary>
    public Mesh Mesh { get; }

    /// <summary>Gets the material; the model owns and disposes it with the model.</summary>
    public Shader Shader { get; }

    /// <summary>Gets the render state used for the part.</summary>
    public RenderState RenderState { get; }

    /// <summary>Gets the part-local transform, applied before the instance transform.</summary>
    public Transform LocalTransform { get; }

    /// <summary>Gets the model-space bounds after the local transform.</summary>
    public BoundingBox Bounds { get; }

    private static BoundingBox TransformBounds(BoundingBox bounds, Matrix4x4 matrix)
    {
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
        {
            Vector3 corner = new(
                (cornerIndex & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                (cornerIndex & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                (cornerIndex & 4) == 0 ? bounds.Min.Z : bounds.Max.Z
            );
            Vector3 transformed = Vector3.Transform(corner, matrix);
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }

        return new BoundingBox(minimum, maximum);
    }
}
