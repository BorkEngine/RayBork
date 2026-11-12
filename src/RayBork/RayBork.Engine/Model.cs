using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A loaded multi-part model: a flat list of <see cref="ModelPart"/> values and their combined bounds.
/// </summary>
/// <remarks>
/// <para>
/// A model is a reference-counted engine asset. <see cref="IAssetCatalog.Load{T}"/> returns the same
/// instance for repeated loads of the same path and acquires one owner reference per successful load;
/// <see cref="Dispose"/> releases one owner reference. The object is disposed when the last owner
/// releases it.
/// </para>
/// <para>
/// The model owns one reference to every distinct part mesh and one material per part. The meshes are
/// shared engine assets; each part's <see cref="ModelPart.Shader"/> is a material the model owns and
/// disposes with its last owner. Two parts that name the same shader program path still get distinct
/// materials over the same internal program. The references are released when the model's last owner
/// releases it; one owner disposing a shared model does not invalidate the parts for the remaining
/// owners. Two distinct models that share a part each release their own reference. M4 models are
/// static: there is no skeletal or morph animation, and the part list is immutable.
/// </para>
/// <para>
/// <see cref="Path"/> and <see cref="IsDisposed"/> stay readable after disposal. Every other member
/// throws <see cref="ObjectDisposedException"/> once the asset is disposed.
/// </para>
/// </remarks>
public sealed class Model : IAsset {
    private readonly EngineAsset _asset;
    private readonly IReadOnlyList<ModelPart> _parts;
    private readonly BoundingBox _bounds;
    private readonly Mesh[] _meshes;
    private readonly Shader[] _shaders;
    private bool _partsReleased;

    /// <summary>
    /// Initializes a model from its uploaded lifetime state, its descriptor, and its resolved parts.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="descriptor">The packed model descriptor.</param>
    /// <param name="parts">The resolved parts in descriptor order.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Model(EngineAsset asset, ModelAssetDescriptor descriptor, ModelPart[] parts) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(parts);

        _asset = asset;
        _parts = Array.AsReadOnly(parts);
        _bounds = new BoundingBox(
            new System.Numerics.Vector3(descriptor.Bounds[0], descriptor.Bounds[1], descriptor.Bounds[2]),
            new System.Numerics.Vector3(descriptor.Bounds[3], descriptor.Bounds[4], descriptor.Bounds[5]));

        HashSet<Mesh> meshes = new(ReferenceEqualityComparer.Instance);
        HashSet<Shader> shaders = new(ReferenceEqualityComparer.Instance);
        foreach (ModelPart part in parts) {
            meshes.Add(part.Mesh);
            shaders.Add(part.Shader);
        }

        _meshes = [.. meshes];
        _shaders = [.. shaders];
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>Gets the flat part list in model descriptor order.</summary>
    /// <exception cref="ObjectDisposedException">The model is disposed.</exception>
    public IReadOnlyList<ModelPart> Parts {
        get {
            ThrowIfDisposed();
            return _parts;
        }
    }

    /// <summary>Gets the combined model-space bounds of every part.</summary>
    /// <exception cref="ObjectDisposedException">The model is disposed.</exception>
    public BoundingBox Bounds {
        get {
            ThrowIfDisposed();
            return _bounds;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the draw recorder holds per frame. The catalog owns the asset
    /// and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>
    /// Releases one owner reference for the model. The distinct part references are released with the
    /// last owner, through the catalog's owner-release callback; one owner disposing a shared model must
    /// not invalidate the parts for the remaining owners.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    public void Dispose() {
        if (_asset.IsDisposed) {
            throw new ObjectDisposedException(nameof(Model));
        }

        _asset.ReleaseOwnerReference();
    }

    /// <summary>
    /// Releases the distinct part references and the part materials without releasing the model's own
    /// owner reference. The catalog calls it when the model's last owner goes away, and when a load is
    /// disposed before any waiter receives the model, so the references acquired during the load cannot
    /// leak.
    /// </summary>
    /// <remarks>
    /// Mesh references are released through the shared <see cref="EngineAsset"/> lifetime because a
    /// part mesh may be shared by another model. Every part material is owned by this model alone, so
    /// it is disposed exactly once here; a material the catalog already force-disposed during shutdown
    /// is skipped. A part that another model also uses has its own material in that model, so disposing
    /// one model never invalidates the other. Idempotent.
    /// </remarks>
    internal void ReleasePartReferences() {
        if (_partsReleased) {
            return;
        }

        _partsReleased = true;
        foreach (Mesh mesh in _meshes) {
            try {
                mesh.Lifetime.ReleaseOwnerReference();
            } catch (ObjectDisposedException) {
                // The catalog force-disposed the part during shutdown.
            }
        }

        foreach (Shader material in _shaders) {
            try {
                material.Dispose();
            } catch (ObjectDisposedException) {
                // The catalog force-disposed the material's program during shutdown.
            }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
