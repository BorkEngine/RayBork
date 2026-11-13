using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One mesh primitive: an immutable vertex/index buffer pair with its topology and bounds.
/// </summary>
/// <remarks>
/// <para>
/// A mesh is either a standalone baked mesh-part asset loaded from a pack or one procedural mesh built
/// by a <see cref="MeshBuilder"/>. A multi-part model is the M4 entry point for a glTF or OBJ source.
/// The GPU buffers are created during the load or build and are released when the last reference is
/// disposed.
/// </para>
/// <para>
/// The CPU vertices and indices are retained for both kinds: a loaded mesh decodes them from the
/// packed payload and a procedural mesh keeps the builder's snapshot. Later M5 collision helpers read
/// them. Textures do not retain CPU pixels.
/// </para>
/// <para>
/// <see cref="Path"/> and <see cref="IsDisposed"/> stay readable after disposal. Every other member
/// throws <see cref="ObjectDisposedException"/> once the asset is disposed.
/// </para>
/// </remarks>
public sealed class Mesh : IAsset {
    private readonly EngineAsset _asset;
    private readonly ReadOnlyMemory<Vertex> _vertices;
    private readonly ReadOnlyMemory<uint> _indices;
    private readonly int _vertexCount;
    private readonly int _indexCount;
    private readonly BoundingBox _bounds;
    private readonly PrimitiveTopology _topology;
    private readonly bool _isProcedural;
    private readonly MeshCollisionData? _collision;

    /// <summary>
    /// Initializes a mesh from its uploaded lifetime state, its descriptor, and its CPU geometry.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog or the builder.</param>
    /// <param name="descriptor">The packed or synthetic mesh descriptor.</param>
    /// <param name="vertices">The CPU vertices the mesh retains.</param>
    /// <param name="indices">The CPU 32-bit indices the mesh retains.</param>
    /// <param name="isProcedural">Whether the mesh was built by a <see cref="MeshBuilder"/>.</param>
    /// <param name="collision">
    /// The collision acceleration structure that describes the same snapshot, or <c>null</c> when the
    /// mesh has none. The mesh does not dispose it; its arrays are managed.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Mesh(
        EngineAsset asset,
        MeshAssetDescriptor descriptor,
        Vertex[] vertices,
        uint[] indices,
        bool isProcedural,
        MeshCollisionData? collision = null) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);

        _asset = asset;
        _vertices = vertices;
        _indices = indices;
        _vertexCount = vertices.Length;
        _indexCount = indices.Length;
        _bounds = new BoundingBox(
            new Vector3(descriptor.Bounds[0], descriptor.Bounds[1], descriptor.Bounds[2]),
            new Vector3(descriptor.Bounds[3], descriptor.Bounds[4], descriptor.Bounds[5]));
        _topology = descriptor.Topology;
        _isProcedural = isProcedural;
        _collision = collision;
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>
    /// Gets whether the mesh was built by a <see cref="MeshBuilder"/> instead of loaded from a pack.
    /// A procedural mesh has a reserved synthetic <see cref="Path"/> under <c>/_procedural/mesh/</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public bool IsProcedural {
        get {
            ThrowIfDisposed();
            return _isProcedural;
        }
    }

    /// <summary>
    /// Gets the number of vertices in the vertex buffer.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public int VertexCount {
        get {
            ThrowIfDisposed();
            return _vertexCount;
        }
    }

    /// <summary>
    /// Gets the number of 32-bit indices in the index buffer.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public int IndexCount {
        get {
            ThrowIfDisposed();
            return _indexCount;
        }
    }

    /// <summary>
    /// Gets the CPU vertices retained for collision and inspection.
    /// </summary>
    /// <remarks>
    /// A loaded mesh decodes the vertices from the packed payload; a procedural mesh returns the
    /// builder's snapshot. The memory is read-only and stays valid until the last reference is
    /// disposed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public ReadOnlyMemory<Vertex> Vertices {
        get {
            ThrowIfDisposed();
            return _vertices;
        }
    }

    /// <summary>
    /// Gets the CPU 32-bit indices retained for collision and inspection.
    /// </summary>
    /// <remarks>
    /// A loaded mesh decodes the indices from the packed payload; a procedural mesh returns the
    /// builder's snapshot. The memory is read-only and stays valid until the last reference is
    /// disposed.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public ReadOnlyMemory<uint> Indices {
        get {
            ThrowIfDisposed();
            return _indices;
        }
    }

    /// <summary>
    /// Gets the mesh-space bounds.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public BoundingBox Bounds {
        get {
            ThrowIfDisposed();
            return _bounds;
        }
    }

    /// <summary>
    /// Gets whether the mesh carries a collision acceleration structure for ray/mesh queries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A procedural mesh gets one only when <see cref="MeshBuilder.GenerateCollisions"/> was called
    /// before <see cref="MeshBuilder.BuildAsync"/>. A mesh loaded from a pack gets one when its
    /// descriptor names a baked collision payload; the bake writes that payload for every triangle
    /// mesh unless the source sidecar opts out with <c>collisions = false</c>.
    /// <see cref="Collision.GetRayMesh"/> traverses the structure when it is present and tests every
    /// triangle otherwise; both paths return the same nearest hit.
    /// </para>
    /// <para>
    /// The structure is immutable and is not disposed with the mesh.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public bool HasCollisionAcceleration {
        get {
            ThrowIfDisposed();
            return _collision is not null;
        }
    }

    /// <summary>
    /// Gets the primitive topology of the mesh.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    public PrimitiveTopology Topology {
        get {
            ThrowIfDisposed();
            return _topology;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the internal retention state the M4 draw recorder will retain per frame. The catalog or the
    /// builder owns the asset and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>
    /// Gets the collision acceleration structure, or <c>null</c> when the mesh has none.
    /// </summary>
    /// <remarks>
    /// The structure is immutable and describes the same CPU snapshot that <see cref="Vertices"/> and
    /// <see cref="Indices"/> expose.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The mesh is disposed.</exception>
    internal MeshCollisionData? Collision {
        get {
            ThrowIfDisposed();
            return _collision;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">Every reference was already released.</exception>
    public void Dispose() {
        if (_asset.IsDisposed) {
            throw new ObjectDisposedException(nameof(Mesh));
        }

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
