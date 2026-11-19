using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One mesh primitive: an immutable vertex/index buffer pair with its topology and bounds.
/// </summary>
/// <remarks>
/// The GPU buffers are released when the last reference is disposed; the CPU vertices and indices
/// are retained for collision and inspection. <see cref="Path"/> and <see cref="IsDisposed"/> stay
/// readable after disposal, while every other member throws <see cref="ObjectDisposedException"/>.
/// </remarks>
public sealed class Mesh : IAsset
{
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
    /// Initializes a mesh from its lifetime state, descriptor, and CPU geometry.
    /// </summary>
    internal Mesh(
        EngineAsset asset,
        MeshAssetDescriptor descriptor,
        Vertex[] vertices,
        uint[] indices,
        bool isProcedural,
        MeshCollisionData? collision = null
    )
    {
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
            new Vector3(descriptor.Bounds[3], descriptor.Bounds[4], descriptor.Bounds[5])
        );
        _topology = descriptor.Topology;
        _isProcedural = isProcedural;
        _collision = collision;
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

    /// <summary>
    /// Gets whether the mesh was built by a <see cref="MeshBuilder"/>.
    /// </summary>
    public bool IsProcedural
    {
        get
        {
            ThrowIfDisposed();
            return _isProcedural;
        }
    }

    /// <summary>
    /// Gets the number of vertices in the vertex buffer.
    /// </summary>
    public int VertexCount
    {
        get
        {
            ThrowIfDisposed();
            return _vertexCount;
        }
    }

    /// <summary>
    /// Gets the number of 32-bit indices in the index buffer.
    /// </summary>
    public int IndexCount
    {
        get
        {
            ThrowIfDisposed();
            return _indexCount;
        }
    }

    /// <summary>
    /// Gets the CPU vertices retained for collision and inspection.
    /// </summary>
    public ReadOnlyMemory<Vertex> Vertices
    {
        get
        {
            ThrowIfDisposed();
            return _vertices;
        }
    }

    /// <summary>
    /// Gets the CPU 32-bit indices retained for collision and inspection.
    /// </summary>
    public ReadOnlyMemory<uint> Indices
    {
        get
        {
            ThrowIfDisposed();
            return _indices;
        }
    }

    /// <summary>
    /// Gets the mesh-space bounds.
    /// </summary>
    public BoundingBox Bounds
    {
        get
        {
            ThrowIfDisposed();
            return _bounds;
        }
    }

    /// <summary>
    /// Gets whether an immutable ray/mesh collision acceleration structure is present.
    /// </summary>
    public bool HasCollisionAcceleration
    {
        get
        {
            ThrowIfDisposed();
            return _collision is not null;
        }
    }

    /// <summary>
    /// Gets the primitive topology of the mesh.
    /// </summary>
    public PrimitiveTopology Topology
    {
        get
        {
            ThrowIfDisposed();
            return _topology;
        }
    }

    /// <inheritdoc/>
    public bool IsDisposed => _asset.IsDisposed;

    /// <summary>
    /// Gets the shared lifetime state used for per-frame retention.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

    /// <summary>
    /// Gets the collision acceleration structure, or <c>null</c> when the mesh has none.
    /// </summary>
    internal MeshCollisionData? Collision
    {
        get
        {
            ThrowIfDisposed();
            return _collision;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);

        _asset.ReleaseOwnerReference();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_asset.IsDisposed, this);
}
