using System.Numerics;

namespace RayBork;

/// <summary>
/// A multi-part model: a flat list of <see cref="ModelPart"/> values and their combined bounds.
/// </summary>
/// <remarks>
/// <para>
/// A model is not an engine asset and has no path or shared identity. It is a caller-owned composition
/// of shared <see cref="Mesh"/> assets and owned <see cref="Shader"/> materials. Constructing a model
/// over caller parts acquires one engine reference to every distinct mesh and transfers every distinct
/// part material to the model. Callers create one material per part and must not dispose a material
/// after transferring it.
/// </para>
/// <para>
/// <see cref="IAssetCatalog.Load{T}"/> returns a new model wrapper per call over the shared cached
/// part meshes and programs. Repeated loads never share the model or its materials, but two materials
/// over the same program still batch together because the internal program is the batch key.
/// </para>
/// <para>
/// <see cref="Dispose"/> is idempotent. It disposes each distinct part material exactly once and
/// releases each distinct part mesh reference exactly once. <see cref="IsDisposed"/> stays readable
/// after disposal; every other member throws <see cref="ObjectDisposedException"/>. M4 models are
/// static: there is no skeletal or morph animation, and the part list is immutable.
/// </para>
/// </remarks>
public sealed class Model : IDisposable {
    private readonly IReadOnlyList<ModelPart> _parts;
    private readonly BoundingBox _bounds;
    private readonly Mesh[] _meshes;
    private readonly Shader[] _materials;
    private bool _disposed;

    /// <summary>
    /// Initializes a model over caller parts.
    /// </summary>
    /// <param name="parts">The parts in draw order. The list must contain at least one part.</param>
    /// <remarks>
    /// The constructor acquires one engine reference to every distinct part mesh and takes ownership
    /// of every distinct part material. The caller keeps its own mesh references and must release
    /// them separately; it must not dispose a material after the model was constructed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parts"/> is <c>null</c>, or a part name is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="parts"/> is empty, a part name is whitespace, or a part has no mesh or material.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A part mesh or material is disposed.</exception>
    public Model(params ModelPart[] parts)
        : this((IEnumerable<ModelPart>)parts) {
    }

    /// <summary>
    /// Initializes a model over caller parts.
    /// </summary>
    /// <param name="parts">The parts in draw order. The sequence must contain at least one part.</param>
    /// <remarks>
    /// The constructor acquires one engine reference to every distinct part mesh and takes ownership
    /// of every distinct part material. The caller keeps its own mesh references and must release
    /// them separately; it must not dispose a material after the model was constructed.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parts"/> is <c>null</c>, or a part name is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="parts"/> is empty, a part name is whitespace, or a part has no mesh or material.
    /// </exception>
    /// <exception cref="ObjectDisposedException">A part mesh or material is disposed.</exception>
    public Model(IEnumerable<ModelPart> parts) {
        ArgumentNullException.ThrowIfNull(parts);

        ModelPart[] resolved = [.. parts];
        ValidateParts(resolved);

        _parts = Array.AsReadOnly(resolved);
        _bounds = UnionBounds(resolved);
        _meshes = DistinctMeshes(resolved);
        _materials = DistinctMaterials(resolved);

        int acquired = 0;
        try {
            foreach (Mesh mesh in _meshes) {
                mesh.Lifetime.AddOwnerReference();
                acquired++;
            }
        } catch {
            for (int index = 0; index < acquired; index++) {
                _meshes[index].Lifetime.ReleaseOwnerReference();
            }

            throw;
        }
    }

    /// <summary>Gets the flat part list in construction order.</summary>
    /// <exception cref="ObjectDisposedException">The model is disposed.</exception>
    public IReadOnlyList<ModelPart> Parts {
        get {
            ThrowIfDisposed();
            return _parts;
        }
    }

    /// <summary>Gets the union of every part's model-space bounds.</summary>
    /// <exception cref="ObjectDisposedException">The model is disposed.</exception>
    public BoundingBox Bounds {
        get {
            ThrowIfDisposed();
            return _bounds;
        }
    }

    /// <summary>Gets whether the model was disposed. Stays readable after disposal.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Disposes every distinct part material and releases one engine reference for every distinct part
    /// mesh. Idempotent.
    /// </summary>
    /// <remarks>
    /// A part mesh that another model or the game also references stays usable. Every material was
    /// transferred to this model, so it is disposed here exactly once and must not be disposed by the
    /// caller.
    /// </remarks>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        foreach (Shader material in _materials) {
            try {
                material.Dispose();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the material's program before the model was disposed.
            }
        }

        foreach (Mesh mesh in _meshes) {
            try {
                mesh.Lifetime.ReleaseOwnerReference();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the mesh before the model was disposed.
            }
        }
    }

    private static void ValidateParts(ModelPart[] parts) {
        if (parts.Length == 0) {
            throw new ArgumentException("A model must contain at least one part.", nameof(parts));
        }

        foreach (ModelPart part in parts) {
            if (string.IsNullOrWhiteSpace(part.Name)) {
                throw new ArgumentException(
                    "Every model part must have a non-empty name.",
                    nameof(parts));
            }

            if (part.Mesh is null) {
                throw new ArgumentException("Every model part must have a mesh.", nameof(parts));
            }

            if (part.Shader is null) {
                throw new ArgumentException("Every model part must have a shader material.", nameof(parts));
            }

            ObjectDisposedException.ThrowIf(part.Mesh.IsDisposed, part.Mesh);
            ObjectDisposedException.ThrowIf(part.Shader.IsDisposed, part.Shader);
        }
    }

    private static BoundingBox UnionBounds(ModelPart[] parts) {
        Vector3 minimum = parts[0].Bounds.Min;
        Vector3 maximum = parts[0].Bounds.Max;
        for (int index = 1; index < parts.Length; index++) {
            minimum = Vector3.Min(minimum, parts[index].Bounds.Min);
            maximum = Vector3.Max(maximum, parts[index].Bounds.Max);
        }

        return new BoundingBox(minimum, maximum);
    }

    private static Mesh[] DistinctMeshes(ModelPart[] parts) {
        HashSet<Mesh> meshes = new(ReferenceEqualityComparer.Instance);
        foreach (ModelPart part in parts) {
            meshes.Add(part.Mesh);
        }

        return [.. meshes];
    }

    private static Shader[] DistinctMaterials(ModelPart[] parts) {
        HashSet<Shader> materials = new(ReferenceEqualityComparer.Instance);
        foreach (ModelPart part in parts) {
            materials.Add(part.Shader);
        }

        return [.. materials];
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
