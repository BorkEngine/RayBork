using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// One loaded mesh primitive: an immutable vertex/index buffer pair with its topology and bounds.
/// </summary>
/// <remarks>
/// <para>
/// M3 exposes the loading and metadata surface. A mesh is a standalone baked mesh-part asset; a
/// multi-part model is the M4 entry point for a glTF or OBJ source. The GPU buffers are created from
/// the packed mesh payload during the load and are released when the last reference is disposed.
/// </para>
/// <para>
/// <see cref="Path"/> and <see cref="IsDisposed"/> stay readable after disposal. Every other member
/// throws <see cref="ObjectDisposedException"/> once the asset is disposed.
/// </para>
/// </remarks>
public sealed class Mesh : IAsset {
    private readonly EngineAsset _asset;
    private readonly int _vertexCount;
    private readonly int _indexCount;
    private readonly BoundingBox _bounds;
    private readonly PrimitiveTopology _topology;

    /// <summary>
    /// Initializes a mesh from its uploaded lifetime state and packed descriptor.
    /// </summary>
    /// <param name="asset">The shared lifetime state acquired by the catalog.</param>
    /// <param name="descriptor">The packed mesh descriptor.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal Mesh(EngineAsset asset, MeshAssetDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(descriptor);

        _asset = asset;
        _vertexCount = checked((int)descriptor.VertexCount);
        _indexCount = checked((int)descriptor.IndexCount);
        _bounds = new BoundingBox(
            new Vector3(descriptor.Bounds[0], descriptor.Bounds[1], descriptor.Bounds[2]),
            new Vector3(descriptor.Bounds[3], descriptor.Bounds[4], descriptor.Bounds[5]));
        _topology = descriptor.Topology;
    }

    /// <inheritdoc/>
    public string Path => _asset.Path;

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
    /// Gets the internal retention state the M4 draw recorder will retain per frame. The catalog owns
    /// the asset and exposes the lifetime only inside the engine assembly.
    /// </summary>
    internal EngineAsset Lifetime => _asset;

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
