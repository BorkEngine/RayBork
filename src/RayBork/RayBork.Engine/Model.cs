using System.Numerics;

namespace RayBork;

/// <summary>A flat list of <see cref="ModelPart"/> values and their combined bounds.</summary>
/// <remarks>
/// A model is caller-owned, not an engine asset; it references every distinct part mesh and takes
/// ownership of every distinct part material. <see cref="Dispose"/> disposes each material and
/// releases each mesh once; repeated loads return a new model over the shared cached meshes.
/// </remarks>
public sealed class Model : IDisposable
{
    private readonly IReadOnlyList<ModelPart> _parts;
    private readonly BoundingBox _bounds;
    private readonly Mesh[] _meshes;
    private readonly Shader[] _materials;
    private bool _disposed;

    /// <summary>Initializes a model over caller parts.</summary>
    /// <param name="parts">The parts in draw order; at least one is required.</param>
    /// <exception cref="ArgumentException">The list is empty or a part is invalid.</exception>
    /// <exception cref="ObjectDisposedException">A part mesh or material is disposed.</exception>
    public Model(params ModelPart[] parts)
        : this((IEnumerable<ModelPart>)parts) { }

    /// <summary>Initializes a model over caller parts.</summary>
    /// <param name="parts">The parts in draw order; at least one is required.</param>
    /// <exception cref="ArgumentException">The list is empty or a part is invalid.</exception>
    /// <exception cref="ObjectDisposedException">A part mesh or material is disposed.</exception>
    public Model(IEnumerable<ModelPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        ModelPart[] resolved = [.. parts];
        ValidateParts(resolved);

        _parts = Array.AsReadOnly(resolved);
        _bounds = UnionBounds(resolved);
        _meshes = DistinctMeshes(resolved);
        _materials = DistinctMaterials(resolved);

        int acquired = 0;
        try
        {
            foreach (Mesh mesh in _meshes)
            {
                mesh.Lifetime.AddOwnerReference();
                acquired++;
            }
        }
        catch
        {
            for (int index = 0; index < acquired; index++)
            {
                _meshes[index].Lifetime.ReleaseOwnerReference();
            }

            throw;
        }
    }

    /// <summary>Gets the flat part list in construction order.</summary>
    public IReadOnlyList<ModelPart> Parts
    {
        get
        {
            ThrowIfDisposed();
            return _parts;
        }
    }

    /// <summary>Gets the union of every part's model-space bounds.</summary>
    public BoundingBox Bounds
    {
        get
        {
            ThrowIfDisposed();
            return _bounds;
        }
    }

    /// <summary>Gets whether the model was disposed; readable after disposal.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Disposes every distinct part material and releases every distinct part mesh.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Shader material in _materials)
        {
            try
            {
                material.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown force-disposed the material's program before the model was disposed.
            }
        }

        foreach (Mesh mesh in _meshes)
        {
            try
            {
                mesh.Lifetime.ReleaseOwnerReference();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown force-disposed the mesh before the model was disposed.
            }
        }
    }

    private static void ValidateParts(ModelPart[] parts)
    {
        if (parts.Length == 0)
        {
            throw new ArgumentException("A model must contain at least one part.", nameof(parts));
        }

        foreach (ModelPart part in parts)
        {
            if (string.IsNullOrWhiteSpace(part.Name))
            {
                throw new ArgumentException(
                    "Every model part must have a non-empty name.",
                    nameof(parts)
                );
            }

            if (part.Mesh is null)
            {
                throw new ArgumentException("Every model part must have a mesh.", nameof(parts));
            }

            if (part.Shader is null)
            {
                throw new ArgumentException(
                    "Every model part must have a shader material.",
                    nameof(parts)
                );
            }

            ObjectDisposedException.ThrowIf(part.Mesh.IsDisposed, part.Mesh);
            ObjectDisposedException.ThrowIf(part.Shader.IsDisposed, part.Shader);
        }
    }

    private static BoundingBox UnionBounds(ModelPart[] parts)
    {
        Vector3 minimum = parts[0].Bounds.Min;
        Vector3 maximum = parts[0].Bounds.Max;
        for (int index = 1; index < parts.Length; index++)
        {
            minimum = Vector3.Min(minimum, parts[index].Bounds.Min);
            maximum = Vector3.Max(maximum, parts[index].Bounds.Max);
        }

        return new BoundingBox(minimum, maximum);
    }

    private static Mesh[] DistinctMeshes(ModelPart[] parts)
    {
        HashSet<Mesh> meshes = new(ReferenceEqualityComparer.Instance);
        foreach (ModelPart part in parts)
        {
            meshes.Add(part.Mesh);
        }

        return [.. meshes];
    }

    private static Shader[] DistinctMaterials(ModelPart[] parts)
    {
        HashSet<Shader> materials = new(ReferenceEqualityComparer.Instance);
        foreach (ModelPart part in parts)
        {
            materials.Add(part.Shader);
        }

        return [.. materials];
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
