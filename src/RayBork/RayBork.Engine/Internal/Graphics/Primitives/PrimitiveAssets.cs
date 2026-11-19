using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>The app-lifetime owner of the built-in primitive meshes and engine materials.</summary>
/// <remarks>Creation and disposal run on the main thread; meshes and materials are read on the tick
/// thread. Disposal is idempotent and releases each reference once; asset accessors throw
/// <see cref="ObjectDisposedException"/> after it while <see cref="IsDisposed"/> stays readable.</remarks>
internal sealed class PrimitiveAssets : IDisposable
{
    /// <summary>The canonical path of the built-in vertex-color primitive shader program.</summary>
    internal const string PRIMITIVE_SHADER_PATH = "/shaders/primitive";

    /// <summary>The canonical path of the built-in MSDF text shader program.</summary>
    internal const string MSDF_SHADER_PATH = "/shaders/msdf";

    private readonly ShaderProgram _primitiveProgram;
    private readonly ShaderProgram _meshProgram;
    private readonly ShaderProgram _msdfProgram;
    private readonly Shader _primitiveMaterial;
    private readonly Shader _meshMaterial;
    private readonly Shader _msdfMaterial;
    private readonly Meshes _meshes;
    private bool _disposed;

    /// <summary>Initializes the owner and takes over its asset references.</summary>
    /// <param name="primitiveProgram">The preloaded <c>/shaders/primitive</c> program.</param>
    /// <param name="meshProgram">The preloaded <c>/shaders/mesh</c> program.</param>
    /// <param name="msdfProgram">The preloaded <c>/shaders/msdf</c> program.</param>
    /// <param name="primitiveMaterial">The material over <paramref name="primitiveProgram"/>.</param>
    /// <param name="meshMaterial">The material over <paramref name="meshProgram"/>.</param>
    /// <param name="msdfMaterial">The material over <paramref name="msdfProgram"/>.</param>
    /// <param name="meshes">The nine primitive meshes, each with one owner reference.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    private PrimitiveAssets(
        ShaderProgram primitiveProgram,
        ShaderProgram meshProgram,
        ShaderProgram msdfProgram,
        Shader primitiveMaterial,
        Shader meshMaterial,
        Shader msdfMaterial,
        Meshes meshes
    )
    {
        ArgumentNullException.ThrowIfNull(primitiveProgram);
        ArgumentNullException.ThrowIfNull(meshProgram);
        ArgumentNullException.ThrowIfNull(msdfProgram);
        ArgumentNullException.ThrowIfNull(primitiveMaterial);
        ArgumentNullException.ThrowIfNull(meshMaterial);
        ArgumentNullException.ThrowIfNull(msdfMaterial);

        _primitiveProgram = primitiveProgram;
        _meshProgram = meshProgram;
        _msdfProgram = msdfProgram;
        _primitiveMaterial = primitiveMaterial;
        _meshMaterial = meshMaterial;
        _msdfMaterial = msdfMaterial;
        _meshes = meshes;
    }

    /// <summary>Gets whether the app released the primitive assets; readable after disposal.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Gets the engine-owned <c>/shaders/primitive</c> material; tint varies per instance.</summary>
    internal Shader PrimitiveMaterial
    {
        get
        {
            ThrowIfDisposed();
            return _primitiveMaterial;
        }
    }

    /// <summary>Gets the engine-owned <c>/shaders/mesh</c> material for billboards and textured
    /// quads.</summary>
    internal Shader MeshMaterial
    {
        get
        {
            ThrowIfDisposed();
            return _meshMaterial;
        }
    }

    /// <summary>Gets the engine-owned <c>/shaders/msdf</c> material used for MSDF text.</summary>
    internal Shader MsdfMaterial
    {
        get
        {
            ThrowIfDisposed();
            return _msdfMaterial;
        }
    }

    /// <summary>Gets the unit cube mesh; the engine owns it.</summary>
    internal Mesh Cube => GetMesh(_meshes.Cube);

    /// <summary>Gets the unit-radius UV sphere mesh; the engine owns it.</summary>
    internal Mesh Sphere => GetMesh(_meshes.Sphere);

    /// <summary>Gets the unit XZ plane mesh; the engine owns it.</summary>
    internal Mesh Plane => GetMesh(_meshes.Plane);

    /// <summary>Gets the unit XY quad mesh; the engine owns it.</summary>
    internal Mesh Quad => GetMesh(_meshes.Quad);

    /// <summary>Gets the unit XY disc mesh; the engine owns it.</summary>
    internal Mesh Disc => GetMesh(_meshes.Disc);

    /// <summary>Gets the unit right-triangle mesh; the engine owns it.</summary>
    internal Mesh Triangle => GetMesh(_meshes.Triangle);

    /// <summary>Gets the unit line-segment mesh; the engine owns it.</summary>
    internal Mesh Line => GetMesh(_meshes.Line);

    /// <summary>Gets the unit wire-cube mesh; the engine owns it.</summary>
    internal Mesh WireCube => GetMesh(_meshes.WireCube);

    /// <summary>Gets the unit wire-sphere mesh; the engine owns it.</summary>
    internal Mesh WireSphere => GetMesh(_meshes.WireSphere);

    /// <summary>Loads the built-in programs, materials, and nine primitive meshes.</summary>
    /// <param name="assets">The started asset catalog.</param>
    /// <returns>The primitive assets owning one reference per asset.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">The catalog lacks the procedural asset
    /// service.</exception>
    /// <exception cref="AssetLoadException">A built-in asset is missing, invalid, or cannot be
    /// uploaded.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The built-in programs, materials, and meshes created here transfer to the returned PrimitiveAssets, which disposes them; the failure path releases whatever was created before it threw."
    )]
    internal static async Task<PrimitiveAssets> LoadAsync(IAssetCatalog assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets is not IProceduralAssetProvider provider)
        {
            throw new InvalidOperationException(
                "The asset catalog does not provide the procedural asset service; the engine wired the wrong catalog."
            );
        }

        ShaderProgram? primitiveProgram = null;
        ShaderProgram? meshProgram = null;
        ShaderProgram? msdfProgram = null;
        Shader? primitiveMaterial = null;
        Shader? meshMaterial = null;
        Shader? msdfMaterial = null;
        Mesh? cube = null;
        Mesh? sphere = null;
        Mesh? plane = null;
        Mesh? quad = null;
        Mesh? disc = null;
        Mesh? triangle = null;
        Mesh? line = null;
        Mesh? wireCube = null;
        Mesh? wireSphere = null;
        try
        {
            primitiveProgram = await assets
                .Load<ShaderProgram>(PRIMITIVE_SHADER_PATH)
                .ConfigureAwait(false);
            meshProgram = await assets
                .Load<ShaderProgram>(EngineDefaults.MESH_SHADER_PATH)
                .ConfigureAwait(false);
            msdfProgram = await assets.Load<ShaderProgram>(MSDF_SHADER_PATH).ConfigureAwait(false);
            primitiveMaterial = primitiveProgram.CreateMaterial();
            meshMaterial = meshProgram.CreateMaterial();
            msdfMaterial = msdfProgram.CreateMaterial();
            cube = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateCube()
                )
                .ConfigureAwait(false);
            sphere = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateSphere()
                )
                .ConfigureAwait(false);
            plane = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreatePlane()
                )
                .ConfigureAwait(false);
            quad = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateQuad()
                )
                .ConfigureAwait(false);
            disc = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateDisc()
                )
                .ConfigureAwait(false);
            triangle = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateTriangle()
                )
                .ConfigureAwait(false);
            line = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateLine()
                )
                .ConfigureAwait(false);
            wireCube = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateWireCube()
                )
                .ConfigureAwait(false);
            wireSphere = await BuildMeshAsync(
                    provider.ProceduralAssets,
                    PrimitiveMeshFactory.CreateWireSphere()
                )
                .ConfigureAwait(false);
            return new PrimitiveAssets(
                primitiveProgram,
                meshProgram,
                msdfProgram,
                primitiveMaterial,
                meshMaterial,
                msdfMaterial,
                new Meshes(cube, sphere, plane, quad, disc, triangle, line, wireCube, wireSphere)
            );
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DisposeQuietly(primitiveMaterial);
            DisposeQuietly(meshMaterial);
            DisposeQuietly(msdfMaterial);
            ReleaseQuietly(cube);
            ReleaseQuietly(sphere);
            ReleaseQuietly(plane);
            ReleaseQuietly(quad);
            ReleaseQuietly(disc);
            ReleaseQuietly(triangle);
            ReleaseQuietly(line);
            ReleaseQuietly(wireCube);
            ReleaseQuietly(wireSphere);
            ReleaseQuietly(primitiveProgram);
            ReleaseQuietly(meshProgram);
            ReleaseQuietly(msdfProgram);
            throw new AssetLoadException(
                $"The engine's built-in primitive assets are missing or corrupt: the "
                    + $"'{PRIMITIVE_SHADER_PATH}' program, the '{EngineDefaults.MESH_SHADER_PATH}' program, "
                    + $"the '{MSDF_SHADER_PATH}' program, and the engine primitive meshes must all load "
                    + "from the built-in pack.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception
            );
        }
    }

    /// <summary>Disposes materials and releases all mesh and program owner references.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeQuietly(_primitiveMaterial);
        DisposeQuietly(_meshMaterial);
        DisposeQuietly(_msdfMaterial);
        ReleaseQuietly(_meshes.Cube);
        ReleaseQuietly(_meshes.Sphere);
        ReleaseQuietly(_meshes.Plane);
        ReleaseQuietly(_meshes.Quad);
        ReleaseQuietly(_meshes.Disc);
        ReleaseQuietly(_meshes.Triangle);
        ReleaseQuietly(_meshes.Line);
        ReleaseQuietly(_meshes.WireCube);
        ReleaseQuietly(_meshes.WireSphere);
        ReleaseQuietly(_primitiveProgram);
        ReleaseQuietly(_meshProgram);
        ReleaseQuietly(_msdfProgram);
    }

    private static async Task<Mesh> BuildMeshAsync(
        ProceduralAssetService service,
        PrimitiveMeshData data
    )
    {
        MeshBuilder builder = service.CreateMeshBuilder(data.Topology);
        builder.SetVertices(data.Vertices);
        builder.SetIndices(data.Indices);
        builder.ComputeBounds();
        return await builder.BuildAsync().ConfigureAwait(false);
    }

    private Mesh GetMesh(Mesh mesh)
    {
        ThrowIfDisposed();
        return mesh;
    }

    private static void DisposeQuietly(Shader? material)
    {
        if (material is null || material.IsDisposed)
        {
            return;
        }

        try
        {
            material.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the program during shutdown; the material reference is gone.
        }
    }

    private static void ReleaseQuietly(Mesh? mesh)
    {
        if (mesh is null)
        {
            return;
        }

        try
        {
            mesh.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the mesh during shutdown; its reference is already gone.
        }
    }

    private static void ReleaseQuietly(ShaderProgram? program)
    {
        if (program is null)
        {
            return;
        }

        try
        {
            program.Lifetime.ReleaseOwnerReference();
        }
        catch (ObjectDisposedException)
        {
            // The catalog force-disposed the program during shutdown; its reference is already gone.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct Meshes(
        Mesh Cube,
        Mesh Sphere,
        Mesh Plane,
        Mesh Quad,
        Mesh Disc,
        Mesh Triangle,
        Mesh Line,
        Mesh WireCube,
        Mesh WireSphere
    );
}
