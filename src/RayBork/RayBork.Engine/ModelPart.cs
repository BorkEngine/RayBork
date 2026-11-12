using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One part of a multi-part <see cref="Model"/>.
/// </summary>
/// <remarks>
/// <para>
/// A model is a flat list of parts; it has no node hierarchy. glTF node transforms are baked into
/// <see cref="LocalTransform"/>, which is applied before the draw's instance transform:
/// <c>world = LocalTransform.ToMatrix() * instanceTransform.ToMatrix()</c> under the row-vector
/// convention. The method never composes transforms through <see cref="Transform.WithParent"/>, which
/// can throw on shear.
/// </para>
/// <para>
/// <see cref="Shader"/> is the shader baked for the part. <see cref="Camera.DrawModel(Model, Transform)"/>
/// uses that shader's bake-default render state, uniform template, and texture fallbacks; mutating the
/// shader cursor with <see cref="Shader.SetUniform(string, float)"/> does not change the draw.
/// <see cref="Mesh"/> and <see cref="Shader"/> are owned by the model until the model is disposed.
/// </para>
/// </remarks>
public readonly struct ModelPart {
    /// <summary>
    /// Initializes a part from the model loader.
    /// </summary>
    internal ModelPart(string name, Mesh mesh, Shader shader, Transform localTransform, BoundingBox bounds) {
        Name = name;
        Mesh = mesh;
        Shader = shader;
        LocalTransform = localTransform;
        Bounds = bounds;
    }

    /// <summary>Gets the part name from the source scene.</summary>
    public string Name { get; }

    /// <summary>Gets the mesh asset for the part. The model holds one owner reference to it.</summary>
    public Mesh Mesh { get; }

    /// <summary>Gets the baked shader for the part. The model holds one owner reference to it.</summary>
    public Shader Shader { get; }

    /// <summary>Gets the part-local transform, applied before the draw's instance transform.</summary>
    public Transform LocalTransform { get; }

    /// <summary>Gets the model-space bounds of the part, after its local transform.</summary>
    public BoundingBox Bounds { get; }
}
