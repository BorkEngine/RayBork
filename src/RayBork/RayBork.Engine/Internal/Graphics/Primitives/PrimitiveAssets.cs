namespace RayBork.Internal;

/// <summary>
/// The app-lifetime owner of the engine's built-in primitive meshes and the two materials the
/// primitive draw helpers use.
/// </summary>
/// <remarks>
/// <para>
/// The app creates this instance after the asset catalog starts and before <see cref="IGame.Setup"/>
/// runs, attaches it to <see cref="CameraService"/>, and releases it during shutdown after
/// <see cref="IGame.Shutdown"/> returns and before the catalog force-disposes its cached assets.
/// </para>
/// <para>
/// A <see cref="PrimitiveAssets"/> owns one <see cref="Shader"/> material over the built-in
/// <c>/shaders/primitive</c> program for vertex-color geometry and one over the built-in
/// <c>/shaders/mesh</c> program for billboards and textured quads. It also holds one owner reference
/// for each of the eight primitive meshes. Creation runs on the main thread before the tick thread
/// starts, and the meshes and materials are read on the tick thread while the app records frames.
/// <see cref="Dispose"/> runs on the main thread after the tick thread joined.
/// </para>
/// <para>
/// <see cref="Dispose"/> is idempotent: it disposes each material once, releases each mesh owner
/// reference once, and then releases the two program owner references. A creation failure releases
/// everything already created and throws <see cref="AssetLoadException"/> with
/// <see cref="DiagnosticCodes.ASSET_LOAD_FAILED"/>, so a failed startup leaves no partial state.
/// Every accessor throws <see cref="ObjectDisposedException"/> after disposal.
/// </para>
/// </remarks>
internal sealed class PrimitiveAssets : IDisposable {
    /// <summary>The canonical path of the built-in vertex-color primitive shader program.</summary>
    internal const string PRIMITIVE_SHADER_PATH = "/shaders/primitive";

    private readonly ShaderProgram _primitiveProgram;
    private readonly ShaderProgram _meshProgram;
    private readonly Shader _primitiveMaterial;
    private readonly Shader _meshMaterial;
    private readonly Meshes _meshes;
    private bool _disposed;

    /// <summary>
    /// Initializes the owner from the already built meshes, materials, and programs. The owner takes
    /// over the two program references, the two material references, and one reference per mesh.
    /// </summary>
    /// <param name="primitiveProgram">The preloaded <c>/shaders/primitive</c> program.</param>
    /// <param name="meshProgram">The preloaded <c>/shaders/mesh</c> program.</param>
    /// <param name="primitiveMaterial">The material over <paramref name="primitiveProgram"/>.</param>
    /// <param name="meshMaterial">The material over <paramref name="meshProgram"/>.</param>
    /// <param name="meshes">The eight primitive meshes, each with one owner reference.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    private PrimitiveAssets(
        ShaderProgram primitiveProgram,
        ShaderProgram meshProgram,
        Shader primitiveMaterial,
        Shader meshMaterial,
        Meshes meshes) {
        ArgumentNullException.ThrowIfNull(primitiveProgram);
        ArgumentNullException.ThrowIfNull(meshProgram);
        ArgumentNullException.ThrowIfNull(primitiveMaterial);
        ArgumentNullException.ThrowIfNull(meshMaterial);

        _primitiveProgram = primitiveProgram;
        _meshProgram = meshProgram;
        _primitiveMaterial = primitiveMaterial;
        _meshMaterial = meshMaterial;
        _meshes = meshes;
    }

    /// <summary>Gets whether the app released the primitive assets. It stays readable after disposal.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>
    /// Gets the engine-owned material over the <c>/shaders/primitive</c> program. The primitive draw
    /// helpers share it and vary the tint through the per-instance color.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Shader PrimitiveMaterial {
        get {
            ThrowIfDisposed();
            return _primitiveMaterial;
        }
    }

    /// <summary>
    /// Gets the engine-owned material over the <c>/shaders/mesh</c> program for the billboard and
    /// textured-quad helpers. Its texture binding is set immediately before each recorded draw.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Shader MeshMaterial {
        get {
            ThrowIfDisposed();
            return _meshMaterial;
        }
    }

    /// <summary>Gets the unit cube mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Cube => GetMesh(_meshes.Cube);

    /// <summary>Gets the unit-radius UV sphere mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Sphere => GetMesh(_meshes.Sphere);

    /// <summary>Gets the unit XZ plane mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Plane => GetMesh(_meshes.Plane);

    /// <summary>Gets the unit XY quad mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Quad => GetMesh(_meshes.Quad);

    /// <summary>Gets the unit right-triangle mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Triangle => GetMesh(_meshes.Triangle);

    /// <summary>Gets the unit line-segment mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh Line => GetMesh(_meshes.Line);

    /// <summary>Gets the unit wire-cube mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh WireCube => GetMesh(_meshes.WireCube);

    /// <summary>Gets the unit wire-sphere mesh. The engine owns it; do not dispose it.</summary>
    /// <exception cref="ObjectDisposedException">The app released the primitive assets.</exception>
    internal Mesh WireSphere => GetMesh(_meshes.WireSphere);

    /// <summary>
    /// Loads the two built-in shader programs, creates their engine-owned materials, and builds the
    /// eight primitive meshes through the catalog's procedural asset service.
    /// </summary>
    /// <param name="assets">The started asset catalog.</param>
    /// <returns>The primitive assets, which own one reference per program, material, and mesh.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assets"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The catalog does not provide the procedural asset service; the engine wired the wrong catalog.
    /// </exception>
    /// <exception cref="AssetLoadException">
    /// A built-in program is missing, was baked as the wrong type, cannot be uploaded, or a primitive
    /// mesh cannot be built. The message names the built-in assets; every asset created before the
    /// failure is released.
    /// </exception>
    internal static async Task<PrimitiveAssets> LoadAsync(IAssetCatalog assets) {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets is not IProceduralAssetProvider provider) {
            throw new InvalidOperationException(
                "The asset catalog does not provide the procedural asset service; the engine wired the wrong catalog.");
        }

        ShaderProgram? primitiveProgram = null;
        ShaderProgram? meshProgram = null;
        Shader? primitiveMaterial = null;
        Shader? meshMaterial = null;
        Mesh? cube = null;
        Mesh? sphere = null;
        Mesh? plane = null;
        Mesh? quad = null;
        Mesh? triangle = null;
        Mesh? line = null;
        Mesh? wireCube = null;
        Mesh? wireSphere = null;
        try {
            primitiveProgram = await assets.Load<ShaderProgram>(PRIMITIVE_SHADER_PATH).ConfigureAwait(false);
            meshProgram = await assets.Load<ShaderProgram>(EngineDefaults.MESH_SHADER_PATH).ConfigureAwait(false);
            primitiveMaterial = primitiveProgram.CreateMaterial();
            meshMaterial = meshProgram.CreateMaterial();
            cube = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateCube()).ConfigureAwait(false);
            sphere = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateSphere()).ConfigureAwait(false);
            plane = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreatePlane()).ConfigureAwait(false);
            quad = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateQuad()).ConfigureAwait(false);
            triangle = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateTriangle()).ConfigureAwait(false);
            line = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateLine()).ConfigureAwait(false);
            wireCube = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateWireCube()).ConfigureAwait(false);
            wireSphere = await BuildMeshAsync(provider.ProceduralAssets, PrimitiveMeshFactory.CreateWireSphere()).ConfigureAwait(false);
            return new PrimitiveAssets(
                primitiveProgram,
                meshProgram,
                primitiveMaterial,
                meshMaterial,
                new Meshes(cube, sphere, plane, quad, triangle, line, wireCube, wireSphere));
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            DisposeQuietly(primitiveMaterial);
            DisposeQuietly(meshMaterial);
            ReleaseQuietly(cube);
            ReleaseQuietly(sphere);
            ReleaseQuietly(plane);
            ReleaseQuietly(quad);
            ReleaseQuietly(triangle);
            ReleaseQuietly(line);
            ReleaseQuietly(wireCube);
            ReleaseQuietly(wireSphere);
            ReleaseQuietly(primitiveProgram);
            ReleaseQuietly(meshProgram);
            throw new AssetLoadException(
                $"The engine's built-in primitive assets are missing or corrupt: the "
                    + $"'{PRIMITIVE_SHADER_PATH}' program, the '{EngineDefaults.MESH_SHADER_PATH}' program, "
                    + "and the engine primitive meshes must all load from the built-in pack.",
                DiagnosticCodes.ASSET_LOAD_FAILED,
                exception);
        }
    }

    /// <summary>
    /// Disposes both materials, releases every mesh owner reference, and releases the program owner
    /// references. Idempotent; called by the app during shutdown after the game callbacks returned.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        DisposeQuietly(_primitiveMaterial);
        DisposeQuietly(_meshMaterial);
        ReleaseQuietly(_meshes.Cube);
        ReleaseQuietly(_meshes.Sphere);
        ReleaseQuietly(_meshes.Plane);
        ReleaseQuietly(_meshes.Quad);
        ReleaseQuietly(_meshes.Triangle);
        ReleaseQuietly(_meshes.Line);
        ReleaseQuietly(_meshes.WireCube);
        ReleaseQuietly(_meshes.WireSphere);
        ReleaseQuietly(_primitiveProgram);
        ReleaseQuietly(_meshProgram);
    }

    private static async Task<Mesh> BuildMeshAsync(
        ProceduralAssetService service,
        PrimitiveMeshData data) {
        MeshBuilder builder = service.CreateMeshBuilder(data.Topology);
        builder.SetVertices(data.Vertices);
        builder.SetIndices(data.Indices);
        builder.ComputeBounds();
        return await builder.BuildAsync().ConfigureAwait(false);
    }

    private Mesh GetMesh(Mesh mesh) {
        ThrowIfDisposed();
        return mesh;
    }

    private static void DisposeQuietly(Shader? material) {
        if (material is null || material.IsDisposed) {
            return;
        }

        try {
            material.Dispose();
        } catch (ObjectDisposedException) {
            // The catalog force-disposed the program during shutdown; the material reference is gone.
        }
    }

    private static void ReleaseQuietly(Mesh? mesh) {
        if (mesh is null) {
            return;
        }

        try {
            mesh.Lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // The catalog force-disposed the mesh during shutdown; its reference is already gone.
        }
    }

    private static void ReleaseQuietly(ShaderProgram? program) {
        if (program is null) {
            return;
        }

        try {
            program.Lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // The catalog force-disposed the program during shutdown; its reference is already gone.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct Meshes(
        Mesh Cube,
        Mesh Sphere,
        Mesh Plane,
        Mesh Quad,
        Mesh Triangle,
        Mesh Line,
        Mesh WireCube,
        Mesh WireSphere);
}
