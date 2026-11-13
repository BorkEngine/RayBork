using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>
/// A frame-scoped recorder that captures one camera pass into the frame being recorded.
/// </summary>
/// <remarks>
/// <para>
/// A camera is not a GPU object and not a reusable scene camera. Create it with
/// <see cref="EngineContext.CreateCamera"/> or <see cref="EngineContext.CreateOrthographicCamera"/>
/// during <see cref="IGame.Tick"/>, record draws, and dispose it before the callback returns. Cameras
/// are sequential: creating one while another is active throws <see cref="InvalidOperationException"/>,
/// and a camera left undisposed when the tick ends is an <see cref="InvalidOperationException"/> that
/// names it.
/// </para>
/// <para>
/// Each draw copies value snapshots of the mesh, material cursor state, tint, and transforms into the
/// frame packet. The renderer never reads a live game object. Every recorded frame retains each mesh,
/// internal shader program, and bound texture until the render thread releases the frame slot after
/// presentation; a material object is not an engine asset and is never retained.
/// </para>
/// <para>
/// The camera looks down local <c>-Z</c> in a right-handed, Y-up world. The projection uses Vulkan's
/// <c>[0, 1]</c> reverse-Z depth range and already contains the Y-flip that maps world <c>+Y</c> to the
/// top of the screen. <see cref="Dispose"/> seals the pass and is idempotent; after disposal every
/// property and every draw throws <see cref="ObjectDisposedException"/>, while <see cref="IsDisposed"/>
/// stays readable. All members are tick-thread only.
/// </para>
/// <para>
/// M4 does not ship a render target: the <c>Target</c> property and the <c>target</c> parameter arrive
/// with <c>RenderTexture</c> in M8. A camera always records into the app's linear HDR scene target.
/// </para>
/// </remarks>
public sealed class Camera : IDisposable {
    private const string MESH_TEXTURE_BINDING = "mainTexture";
    private const float OVERLAY_NDC_DEPTH = 0.5f;

    private static readonly RenderState _overlayRenderState = new() {
        Blend = BlendMode.Alpha,
        Cull = CullMode.None,
        DepthTest = DepthTest.Always,
        DepthWrite = false,
    };

    private readonly CameraService _service;
    private readonly RenderFramePacket _packet;
    private readonly CameraPass _pass;
    private readonly Transform _transform;
    private readonly CameraProjection _projection;
    private readonly float _fieldOfView;
    private readonly float _orthographicHeight;
    private readonly float _near;
    private readonly float _far;
    private readonly ClearMode _clearMode;
    private readonly Color _clearColor;
    private readonly Rect _viewport;
    private readonly Matrix4x4 _viewMatrix;
    private readonly Matrix4x4 _projectionMatrix;
    private readonly Matrix4x4 _viewProjectionMatrix;
    private readonly BoundingFrustum _frustum;
    private bool _disposed;

    /// <summary>
    /// Initializes a camera for a pass that <see cref="CameraService"/> already appended to the frame.
    /// </summary>
    internal Camera(
        CameraService service,
        RenderFramePacket packet,
        CameraPass pass,
        int number,
        Transform transform,
        CameraProjection projection,
        float fieldOfView,
        float orthographicHeight,
        float near,
        float far,
        ClearMode clearMode,
        Color clearColor,
        Rect viewport,
        Matrix4x4 viewMatrix,
        Matrix4x4 projectionMatrix,
        Matrix4x4 viewProjectionMatrix) {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(pass);

        _service = service;
        _packet = packet;
        _pass = pass;
        Number = number;
        _transform = transform;
        _projection = projection;
        _fieldOfView = fieldOfView;
        _orthographicHeight = orthographicHeight;
        _near = near;
        _far = far;
        _clearMode = clearMode;
        _clearColor = clearColor;
        _viewport = viewport;
        _viewMatrix = viewMatrix;
        _projectionMatrix = projectionMatrix;
        _viewProjectionMatrix = viewProjectionMatrix;
        _frustum = new BoundingFrustum(viewProjectionMatrix);
    }

    /// <summary>Gets the one-based camera number used to name the camera in leak diagnostics.</summary>
    internal int Number { get; }

    /// <summary>Gets the world-space camera transform. The camera looks down its local <c>-Z</c>.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Transform Transform {
        get {
            ThrowIfDisposed();
            return _transform;
        }
    }

    /// <summary>Gets the projection kind.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public CameraProjection Projection {
        get {
            ThrowIfDisposed();
            return _projection;
        }
    }

    /// <summary>Gets the vertical field of view in degrees for a perspective camera, or <c>0</c> for an
    /// orthographic camera.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public float FieldOfView {
        get {
            ThrowIfDisposed();
            return _fieldOfView;
        }
    }

    /// <summary>Gets the visible world-space height for an orthographic camera, or <c>0</c> for a
    /// perspective camera.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public float OrthographicHeight {
        get {
            ThrowIfDisposed();
            return _orthographicHeight;
        }
    }

    /// <summary>Gets the near clip distance.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public float Near {
        get {
            ThrowIfDisposed();
            return _near;
        }
    }

    /// <summary>Gets the far clip distance. A perspective camera may report positive infinity.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public float Far {
        get {
            ThrowIfDisposed();
            return _far;
        }
    }

    /// <summary>Gets the buffers this pass clears before it draws.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public ClearMode ClearMode {
        get {
            ThrowIfDisposed();
            return _clearMode;
        }
    }

    /// <summary>Gets the linear color written when color is cleared. HDR components above <c>1</c> are
    /// allowed.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Color ClearColor {
        get {
            ThrowIfDisposed();
            return _clearColor;
        }
    }

    /// <summary>
    /// Gets the pixel viewport in the render target, clamped to the target when the camera was created.
    /// The origin is the top-left corner.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Rect Viewport {
        get {
            ThrowIfDisposed();
            return _viewport;
        }
    }

    /// <summary>Gets the world-to-view matrix.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Matrix4x4 ViewMatrix {
        get {
            ThrowIfDisposed();
            return _viewMatrix;
        }
    }

    /// <summary>
    /// Gets the view-to-clip matrix. It uses Vulkan's <c>[0, 1]</c> reverse-Z depth range and already
    /// contains the Y-flip.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Matrix4x4 ProjectionMatrix {
        get {
            ThrowIfDisposed();
            return _projectionMatrix;
        }
    }

    /// <summary>Gets the combined <c>ViewMatrix * ProjectionMatrix</c> matrix.</summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Matrix4x4 ViewProjectionMatrix {
        get {
            ThrowIfDisposed();
            return _viewProjectionMatrix;
        }
    }

    /// <summary>
    /// Gets the world-space frustum derived from <see cref="ViewProjectionMatrix"/>. The plane names
    /// follow the engine's Y-flipped NDC; see <see cref="BoundingFrustum"/>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public BoundingFrustum Frustum {
        get {
            ThrowIfDisposed();
            return _frustum;
        }
    }

    /// <summary>Gets whether the camera was disposed. Stays readable after disposal.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Records a model with each part's material snapshot, render state, and local transform.
    /// </summary>
    /// <param name="model">The model to draw.</param>
    /// <param name="transform">The model instance transform applied after each part's local transform.</param>
    /// <param name="tint">The per-instance tint, or <see langword="null"/> for opaque white.</param>
    /// <remarks>
    /// <para>
    /// Each part uses its own material. The draw snapshots that material's current uniform cursor and
    /// texture bindings at the call and uses the part's <see cref="ModelPart.RenderState"/>. Different
    /// parts may therefore use different programs, render states, uniform values, and textures in one
    /// model draw.
    /// </para>
    /// <para>
    /// <c>world = part.LocalTransform.ToMatrix() * transform.ToMatrix()</c> under the row-vector
    /// convention. The recorded frame retains the internal program of every part material and every
    /// bound texture; a material object itself is not an engine asset and is not retained.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, model, a part mesh, a part material, or a bound texture is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A part material lacks a stage, or the caller is not the tick thread.
    /// </exception>
    public void DrawModel(Model model, Transform transform, Color? tint = null) {
        EnsureDrawable(nameof(DrawModel));
        ArgumentNullException.ThrowIfNull(model);
        ThrowIfModelDisposed(model);

        IReadOnlyList<ModelPart> parts = model.Parts;
        ValidateParts(parts);

        Color effectiveTint = tint ?? Color.White;
        Matrix4x4 instanceMatrix = transform.ToMatrix();
        foreach (ModelPart part in parts) {
            RecordPart(
                part,
                part.RenderState,
                effectiveTint,
                part.LocalTransform.ToMatrix() * instanceMatrix);
        }
    }

    /// <summary>
    /// Records one mesh with the shader cursor state captured at the call.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="transform">The instance transform; it maps mesh space to world space.</param>
    /// <param name="shader">The shader used for the draw.</param>
    /// <param name="renderState">
    /// The render state override, or <see langword="null"/> for <see cref="Shader.DefaultRenderState"/>.
    /// </param>
    /// <param name="tint">The per-instance tint, or <see langword="null"/> for opaque white.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The camera, mesh, shader, or a bound texture is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The shader lacks a stage, or the caller is not the tick thread.
    /// </exception>
    public void DrawMesh(
        Mesh mesh,
        Transform transform,
        Shader shader,
        RenderState? renderState = null,
        Color? tint = null) {
        EnsureDrawable(nameof(DrawMesh));
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(shader);
        ValidateMesh(mesh);
        ValidateShader(shader);
        ReadOnlySpan<Texture?> textures = shader.TextureValues;
        ValidateTextures(textures);

        _packet.Retention.Add(mesh.Lifetime);
        RetainShader(shader, textures);
        RenderDrawRecord record = BeginDraw(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            shader.Program.Lifetime,
            renderState ?? shader.DefaultRenderState,
            tint ?? Color.White);
        record.SetShaderState(shader.InstanceData, textures);
        record.BeginInstances(1)[0] = transform.ToMatrix();
    }

    /// <summary>
    /// Records one instanced mesh draw. Every transform uses the shader cursor state captured once at
    /// the call.
    /// </summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="shader">The shader used for every instance.</param>
    /// <param name="transforms">The instance transforms; they map mesh space to world space.</param>
    /// <param name="renderState">
    /// The render state override, or <see langword="null"/> for <see cref="Shader.DefaultRenderState"/>.
    /// </param>
    /// <param name="tint">The per-instance tint, or <see langword="null"/> for opaque white.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">The camera, mesh, shader, or a bound texture is disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The shader lacks a stage, or the caller is not the tick thread.
    /// </exception>
    public void DrawInstanced(
        Mesh mesh,
        Shader shader,
        ReadOnlySpan<Transform> transforms,
        RenderState? renderState = null,
        Color? tint = null) {
        EnsureDrawable(nameof(DrawInstanced));
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(shader);
        ValidateMesh(mesh);
        ValidateShader(shader);
        ReadOnlySpan<Texture?> textures = shader.TextureValues;
        ValidateTextures(textures);
        if (transforms.Length == 0) {
            return;
        }

        _packet.Retention.Add(mesh.Lifetime);
        RetainShader(shader, textures);
        RenderDrawRecord record = BeginDraw(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            shader.Program.Lifetime,
            renderState ?? shader.DefaultRenderState,
            tint ?? Color.White);
        record.SetShaderState(shader.InstanceData, textures);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(transforms.Length);
        for (int index = 0; index < transforms.Length; index++) {
            worldMatrices[index] = transforms[index].ToMatrix();
        }
    }

    /// <summary>
    /// Records one instanced model draw with each part's material snapshot and render state.
    /// </summary>
    /// <param name="model">The model to draw.</param>
    /// <param name="transforms">The model instance transforms applied after each part's local transform.</param>
    /// <param name="tint">The per-instance tint, or <see langword="null"/> for opaque white.</param>
    /// <remarks>
    /// Each part uses its own material snapshot and <see cref="ModelPart.RenderState"/> for every
    /// transform. Different parts may use different programs. The recorded frame retains the internal
    /// program of every part material and every bound texture; a material object itself is not retained.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, model, a part mesh, a part material, or a bound texture is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A part material lacks a stage, or the caller is not the tick thread.
    /// </exception>
    public void DrawModelInstanced(
        Model model,
        ReadOnlySpan<Transform> transforms,
        Color? tint = null) {
        EnsureDrawable(nameof(DrawModelInstanced));
        ArgumentNullException.ThrowIfNull(model);
        ThrowIfModelDisposed(model);

        IReadOnlyList<ModelPart> parts = model.Parts;
        ValidateParts(parts);
        if (transforms.Length == 0) {
            return;
        }

        Color effectiveTint = tint ?? Color.White;
        Matrix4x4[] instanceMatrices = new Matrix4x4[transforms.Length];
        for (int index = 0; index < transforms.Length; index++) {
            instanceMatrices[index] = transforms[index].ToMatrix();
        }

        foreach (ModelPart part in parts) {
            Shader material = part.Shader;
            ReadOnlySpan<Texture?> textures = material.TextureValues;
            _packet.Retention.Add(part.Mesh.Lifetime);
            RetainShader(material, textures);
            RenderDrawRecord record = BeginDraw(
                part.Mesh.Lifetime,
                part.Mesh.Topology,
                part.Mesh.Bounds,
                part.Mesh.IndexCount,
                material.Program.Lifetime,
                part.RenderState,
                effectiveTint);
            record.SetShaderState(material.InstanceData, textures);
            Span<Matrix4x4> worldMatrices = record.BeginInstances(instanceMatrices.Length);
            Matrix4x4 localMatrix = part.LocalTransform.ToMatrix();
            for (int index = 0; index < instanceMatrices.Length; index++) {
                worldMatrices[index] = localMatrix * instanceMatrices[index];
            }
        }
    }

    /// <summary>
    /// Records one world-space line segment with the engine line mesh.
    /// </summary>
    /// <param name="start">The world-space segment start.</param>
    /// <param name="end">The world-space segment end.</param>
    /// <param name="color">The linear line color.</param>
    /// <remarks>
    /// The draw is one instance of the engine line mesh mapped from its unit <c>(0, 0, 0)</c>–<c>(1, 0, 0)</c>
    /// reference segment to <paramref name="start"/>–<paramref name="end"/>. It participates in the
    /// normal batching, sorting, culling, and statistics path and contributes no triangles. A
    /// zero-length segment records nothing. The line is one pixel wide; thick lines are not supported.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="start"/> or <paramref name="end"/> is not finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawLine(Vector3 start, Vector3 end, Color color) {
        EnsureDrawable(nameof(DrawLine));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        if (!((end - start).LengthSquared() > 0f)) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateLineMatrix(start, end);
    }

    /// <summary>
    /// Records a world-space polyline as one instanced line draw.
    /// </summary>
    /// <param name="points">The world-space polyline points in order.</param>
    /// <param name="color">The linear line color.</param>
    /// <remarks>
    /// Every consecutive point pair is one instance of the engine line mesh, so the polyline is
    /// recorded as a strip that shares its interior vertices. Fewer than two points records nothing,
    /// and a pair whose endpoints coincide is skipped. The draw contributes no triangles.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a point is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawLines(ReadOnlySpan<Vector3> points, Color color) {
        EnsureDrawable(nameof(DrawLines));
        for (int index = 0; index < points.Length; index++) {
            ValidateFinite(points[index], nameof(points));
        }

        if (points.Length < 2) {
            return;
        }

        int segmentCount = 0;
        for (int index = 1; index < points.Length; index++) {
            if ((points[index] - points[index - 1]).LengthSquared() > 0f) {
                segmentCount++;
            }
        }

        if (segmentCount == 0) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            segmentCount);
        int matrixIndex = 0;
        for (int index = 1; index < points.Length; index++) {
            Vector3 start = points[index - 1];
            Vector3 end = points[index];
            if ((end - start).LengthSquared() > 0f) {
                matrices[matrixIndex++] = CreateLineMatrix(start, end);
            }
        }
    }

    /// <summary>
    /// Records one camera-facing square of <paramref name="size"/> world units.
    /// </summary>
    /// <param name="position">The world-space center.</param>
    /// <param name="color">The linear point color.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <remarks>
    /// The point is an engine quad oriented with the camera basis, which avoids Vulkan point-size
    /// limits. The default <paramref name="size"/> of one world unit gives a one-unit square. A size
    /// of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/> or <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawPoint(Vector3 position, Color color, float size = 1f) {
        EnsureDrawable(nameof(DrawPoint));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        (Vector3 right, Vector3 up, Vector3 front) = GetCameraFacingBasis();
        matrices[0] = CreateBillboardMatrix(position, size, size, right, up, front);
    }

    /// <summary>
    /// Records one world-space triangle with the engine triangle mesh.
    /// </summary>
    /// <param name="a">The first world-space vertex.</param>
    /// <param name="b">The second world-space vertex.</param>
    /// <param name="c">The third world-space vertex.</param>
    /// <param name="color">The linear triangle color.</param>
    /// <remarks>
    /// The draw is one instance of the engine reference triangle under the affine map that sends
    /// <c>(0, 0, 0)</c>, <c>(1, 0, 0)</c>, and <c>(0, 1, 0)</c> to <paramref name="a"/>,
    /// <paramref name="b"/>, and <paramref name="c"/>. The map preserves the reference orientation, so
    /// a triangle that is counter-clockwise as seen by the camera keeps its front face and a clockwise
    /// one is culled. A degenerate or collinear triangle records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a vertex is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color) {
        EnsureDrawable(nameof(DrawTriangle));
        ValidateFinite(a, nameof(a));
        ValidateFinite(b, nameof(b));
        ValidateFinite(c, nameof(c));
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (!(normal.LengthSquared() > 0f)) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateTriangleMatrix(a, b, c, normal, Vector3.UnitZ);
    }

    /// <summary>
    /// Records one solid box centered at <paramref name="position"/> with <paramref name="size"/>
    /// extents.
    /// </summary>
    /// <param name="position">The world-space center.</param>
    /// <param name="size">The world-space width, height, and depth.</param>
    /// <param name="color">The linear box color.</param>
    /// <remarks>
    /// The draw is one instance of the engine cube mesh scaled by <paramref name="size"/>. A size
    /// component of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/> or <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCube(Vector3 position, Vector3 size, Color color) {
        EnsureDrawable(nameof(DrawCube));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Cube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateTranslationScaleMatrix(position, size);
    }

    /// <summary>
    /// Records one solid UV sphere centered at <paramref name="center"/>.
    /// </summary>
    /// <param name="center">The world-space center.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="color">The linear sphere color.</param>
    /// <remarks>
    /// The draw is one instance of the engine unit-radius sphere scaled by <paramref name="radius"/>.
    /// The sphere uses the engine's default tessellation and has no LOD. A radius of zero records
    /// nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="center"/> or <paramref name="radius"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawSphere(Vector3 center, float radius, Color color) {
        EnsureDrawable(nameof(DrawSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Sphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateTranslationScaleMatrix(center, new Vector3(radius));
    }

    /// <summary>
    /// Records one capped cylinder whose base is at <paramref name="position"/> and whose axis points
    /// up world <c>+Y</c>.
    /// </summary>
    /// <param name="position">The world-space center of the base cap.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="height">The world-space height above the base.</param>
    /// <param name="color">The linear cylinder color.</param>
    /// <param name="sides">The number of side quads; must be at least three.</param>
    /// <remarks>
    /// The cylinder is recorded as two instanced draws: <paramref name="sides"/> engine quads for the
    /// sides and <c>2 * sides</c> engine triangles for the two caps, with outward-facing winding. The
    /// side quads share their ring vertices exactly, so the silhouette is exact for any
    /// <paramref name="sides"/> of at least three. A radius or height of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/>, <paramref name="radius"/>, or
    /// <paramref name="height"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="height"/> is negative, or <paramref name="sides"/>
    /// is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCylinder(Vector3 position, float radius, float height, Color color, int sides = 16) {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        if (sides < 3) {
            throw new ArgumentOutOfRangeException(
                nameof(sides),
                sides,
                "A cylinder needs at least three sides.");
        }

        if (radius == 0f || height == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> sideMatrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            material.DefaultRenderState,
            color,
            sides);
        Span<Matrix4x4> capMatrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            sides * 2);
        Vector3 topOffset = Vector3.UnitY * height;
        Vector3 bottomCenter = position;
        Vector3 topCenter = position + topOffset;
        for (int side = 0; side < sides; side++) {
            float angle = MathF.Tau * side / sides;
            float nextAngle = MathF.Tau * (side + 1) / sides;
            Vector3 bottomStart = position + (new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * radius);
            Vector3 bottomEnd = position + (new Vector3(MathF.Cos(nextAngle), 0f, MathF.Sin(nextAngle)) * radius);
            Vector3 topStart = bottomStart + topOffset;
            Vector3 topEnd = bottomEnd + topOffset;
            sideMatrices[side] = CreateCylinderSideMatrix(bottomStart, bottomEnd, height);
            // The bottom cap faces -Y and the top cap faces +Y; crossing the ring edges in this order
            // makes both caps wind outward, matching the side quads.
            capMatrices[side * 2] = CreateTriangleMatrix(
                bottomCenter,
                bottomStart,
                bottomEnd,
                Vector3.Cross(bottomStart - bottomCenter, bottomEnd - bottomCenter),
                -Vector3.UnitY);
            capMatrices[(side * 2) + 1] = CreateTriangleMatrix(
                topCenter,
                topEnd,
                topStart,
                Vector3.Cross(topEnd - topCenter, topStart - topCenter),
                Vector3.UnitY);
        }
    }

    /// <summary>
    /// Records one horizontal plane centered at <paramref name="center"/>.
    /// </summary>
    /// <param name="center">The world-space center.</param>
    /// <param name="size">The world-space width along <c>X</c> and depth along <c>Z</c>.</param>
    /// <param name="color">The linear plane color.</param>
    /// <remarks>
    /// The draw is one instance of the engine plane mesh, which lies in the <c>XZ</c> plane and faces
    /// world <c>+Y</c>. A size component of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="center"/> or <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawPlane(Vector3 center, Vector2 size, Color color) {
        EnsureDrawable(nameof(DrawPlane));
        ValidateFinite(center, nameof(center));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Plane,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = Matrix4x4.CreateScale(size.X, 1f, size.Y) * Matrix4x4.CreateTranslation(center);
    }

    /// <summary>
    /// Records a uniform XZ grid centered at the world origin.
    /// </summary>
    /// <param name="slices">The number of grid cells per axis; must be at least one.</param>
    /// <param name="spacing">The world-space distance between adjacent lines; must be positive.</param>
    /// <param name="color">The linear grid color.</param>
    /// <remarks>
    /// The grid has <c>slices + 1</c> lines per axis, so it is recorded as one instanced line draw
    /// with <c>2 * (slices + 1)</c> instances. Every line is horizontal and <paramref name="spacing"/>
    /// world units from its neighbor; the grid spans <c>slices * spacing</c> world units per axis. It
    /// contributes no triangles.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="spacing"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="slices"/> is less than one, or <paramref name="spacing"/> is not positive.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawGrid(int slices, float spacing, Color color) {
        EnsureDrawable(nameof(DrawGrid));
        ValidateFinite(spacing, nameof(spacing));
        if (slices < 1) {
            throw new ArgumentOutOfRangeException(
                nameof(slices),
                slices,
                "A grid needs at least one cell per axis.");
        }

        ValidatePositive(spacing, nameof(spacing));

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            (slices + 1) * 2);
        float halfExtent = slices * spacing * 0.5f;
        int matrixIndex = 0;
        for (int line = 0; line <= slices; line++) {
            float offset = (-halfExtent) + (line * spacing);
            matrices[matrixIndex++] = CreateLineMatrix(
                new Vector3(-halfExtent, 0f, offset),
                new Vector3(halfExtent, 0f, offset));
        }

        for (int line = 0; line <= slices; line++) {
            float offset = (-halfExtent) + (line * spacing);
            matrices[matrixIndex++] = CreateLineMatrix(
                new Vector3(offset, 0f, -halfExtent),
                new Vector3(offset, 0f, halfExtent));
        }
    }

    /// <summary>
    /// Records the twelve edges of a box centered at <paramref name="position"/> with
    /// <paramref name="size"/> extents.
    /// </summary>
    /// <param name="position">The world-space center.</param>
    /// <param name="size">The world-space width, height, and depth.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The draw is one instance of the engine wire-cube mesh scaled by <paramref name="size"/>, so it
    /// needs no polygon-mode support and contributes no triangles. A size component of zero records
    /// nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/> or <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCube(Vector3 position, Vector3 size, Color color) {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireCube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateTranslationScaleMatrix(position, size);
    }

    /// <summary>
    /// Records three great circles around <paramref name="center"/> with <paramref name="radius"/>
    /// world units.
    /// </summary>
    /// <param name="center">The world-space center.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The draw is one instance of the engine wire-sphere mesh scaled by <paramref name="radius"/> and
    /// contributes no triangles. A radius of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="center"/> or <paramref name="radius"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireSphere(Vector3 center, float radius, Color color) {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireSphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateTranslationScaleMatrix(center, new Vector3(radius));
    }

    /// <summary>
    /// Records one textured quad centered at <paramref name="position"/> that faces the camera.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="position">The world-space center.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <param name="faceCamera">
    /// When <see langword="true"/>, the quad's front faces along the camera's view direction. When
    /// <see langword="false"/>, the quad is vertical: it keeps world up and only the camera's yaw
    /// rotates it.
    /// </param>
    /// <remarks>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth
    /// write. The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A size
    /// component of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/> or <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of <paramref name="size"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Vector3 position,
        Vector2 size,
        Color color,
        bool faceCamera = true) {
        EnsureDrawable(nameof(DrawBillboard));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.MeshMaterial;
        (Vector3 right, Vector3 up, Vector3 front) = faceCamera
            ? GetCameraFacingBasis()
            : GetVerticalBillboardBasis();
        // The engine material is shared by every billboard and textured quad, so the binding exists
        // only around this recording call; the record keeps its own texture reference.
        material.SetUniform(MESH_TEXTURE_BINDING, texture);
        try {
            Span<Matrix4x4> matrices = BeginPrimitiveDraw(
                primitives.Quad,
                material,
                RenderState.AlphaBlend,
                color,
                instanceCount: 1);
            matrices[0] = CreateBillboardMatrix(position, size.X, size.Y, right, up, front);
        } finally {
            material.SetUniform(MESH_TEXTURE_BINDING, null);
        }
    }

    /// <summary>
    /// Records one screen-space textured quad over the camera viewport.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="destination">
    /// The destination rectangle in render pixels relative to the camera viewport, with a top-left
    /// origin and Y increasing downward.
    /// </param>
    /// <param name="tint">The linear tint multiplied into the sampled texture, or <see langword="null"/>
    /// for opaque white.</param>
    /// <remarks>
    /// The destination rectangle's corners are unprojected through the inverse view-projection matrix
    /// at an NDC depth of <c>0.5</c>, and the engine unit quad is mapped affinely onto that world-space
    /// parallelogram, so the projected rectangle is pixel-exact for perspective and orthographic
    /// cameras. The recorded instance transform stays affine, so batching, frustum culling, and depth
    /// sorting see the quad's real world bounds and center, and the quad is never culled for a
    /// translated or rotated perspective camera. It is an overlay: alpha blending, no depth write,
    /// <see cref="DepthTest.Always"/>, and no face culling, so it ignores scene depth. The quad's
    /// top-left UV lands at the destination top-left. The engine material's texture binding is set
    /// immediately before the draw is recorded and cleared immediately afterwards, so the shared
    /// material never keeps a game texture past the call. A destination width or height of zero
    /// records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A component of <paramref name="destination"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="destination"/> has a negative width or height.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, the camera view-projection matrix is singular, a
    /// destination corner cannot be unprojected, the resulting world-space quad is non-finite or
    /// degenerate, or the caller is not the tick thread.
    /// </exception>
    public void DrawTexture(Texture texture, Rect destination, Color? tint = null) {
        EnsureDrawable(nameof(DrawTexture));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateFinite(destination, nameof(destination));
        ValidateNonNegative(destination.Width, nameof(destination));
        ValidateNonNegative(destination.Height, nameof(destination));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (destination.Width == 0f || destination.Height == 0f) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.MeshMaterial;
        Matrix4x4 matrix = CreateOverlayQuadMatrix(destination);
        material.SetUniform(MESH_TEXTURE_BINDING, texture);
        try {
            Span<Matrix4x4> matrices = BeginPrimitiveDraw(
                primitives.Quad,
                material,
                _overlayRenderState,
                tint ?? Color.White,
                instanceCount: 1);
            matrices[0] = matrix;
        } finally {
            material.SetUniform(MESH_TEXTURE_BINDING, null);
        }
    }

    /// <summary>
    /// Converts a render-pixel coordinate with a top-left origin into a world-space ray.
    /// </summary>
    /// <param name="screenPosition">The render-pixel position, in target space (not window space).</param>
    /// <returns>
    /// A normalized world-space ray starting at the camera position for a perspective camera, or at the
    /// near-plane point for an orthographic camera.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="screenPosition"/> has a non-finite component.</exception>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Ray ScreenToWorldRay(Vector2 screenPosition) {
        ThrowIfDisposed();
        ValidateFinite(screenPosition, nameof(screenPosition));

        float ndcX = ((screenPosition.X - _viewport.X) / _viewport.Width * 2f) - 1f;
        float ndcY = ((screenPosition.Y - _viewport.Y) / _viewport.Height * 2f) - 1f;
        if (!Matrix4x4.Invert(_viewProjectionMatrix, out Matrix4x4 inverse)) {
            throw new InvalidOperationException(
                "The camera view-projection matrix is singular; a world ray cannot be unprojected.");
        }

        Vector4 nearClip = new(ndcX, ndcY, 1f, 1f);
        Vector4 nearWorld = Vector4.Transform(nearClip, inverse);
        Vector3 nearPoint = new(nearWorld.X / nearWorld.W, nearWorld.Y / nearWorld.W, nearWorld.Z / nearWorld.W);

        if (_projection == CameraProjection.Orthographic) {
            Vector3 orthographicDirection = _transform.Forward;
            float lengthSquared = orthographicDirection.LengthSquared();
            orthographicDirection = lengthSquared > 0f
                ? orthographicDirection / MathF.Sqrt(lengthSquared)
                : -Vector3.UnitZ;
            return new Ray(nearPoint, orthographicDirection);
        }

        Vector3 origin = _transform.Position;
        Vector3 direction = Vector3.Normalize(nearPoint - origin);
        return new Ray(origin, direction);
    }

    /// <summary>
    /// Projects a world-space position to render pixels with a top-left origin.
    /// </summary>
    /// <param name="worldPosition">The world-space position.</param>
    /// <returns>
    /// The render-pixel position in the camera viewport, or <c>(NaN, NaN)</c> when the point is behind
    /// the camera (<c>w &lt;= 0</c>) and has no finite screen position.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="worldPosition"/> has a non-finite component.</exception>
    /// <exception cref="ObjectDisposedException">The camera is disposed.</exception>
    public Vector2 WorldToScreen(Vector3 worldPosition) {
        ThrowIfDisposed();
        ValidateFinite(worldPosition, nameof(worldPosition));

        Vector4 clip = Vector4.Transform(new Vector4(worldPosition, 1f), _viewProjectionMatrix);
        if (clip.W <= 0f) {
            return new Vector2(float.NaN, float.NaN);
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2(
            _viewport.X + ((ndcX * 0.5f) + 0.5f) * _viewport.Width,
            _viewport.Y + ((ndcY * 0.5f) + 0.5f) * _viewport.Height);
    }

    /// <summary>
    /// Seals the pass. Idempotent.
    /// </summary>
    /// <remarks>
    /// After disposal every property and draw throws <see cref="ObjectDisposedException"/>; the pass is
    /// submitted to the frame when the tick ends.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The caller is not the tick thread.</exception>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        TickThread.RequireCurrent(nameof(Dispose));
        if (_disposed) {
            return;
        }

        _disposed = true;
        _pass.Seal();
        _service.Release(this);
    }

    private RenderDrawRecord BeginDraw(
        EngineAsset mesh,
        PrimitiveTopology topology,
        BoundingBox bounds,
        int indexCount,
        EngineAsset program,
        RenderState renderState,
        Color tint) {
        RenderDrawRecord record = _packet.RentDrawRecord();
        record.Begin(mesh, topology, bounds, indexCount, program, renderState, tint);
        _pass.Add(record);
        return record;
    }

    private void RecordPart(ModelPart part, RenderState renderState, Color tint, Matrix4x4 worldMatrix) {
        Shader material = part.Shader;
        ReadOnlySpan<Texture?> textures = material.TextureValues;
        _packet.Retention.Add(part.Mesh.Lifetime);
        RetainShader(material, textures);
        RenderDrawRecord record = BeginDraw(
            part.Mesh.Lifetime,
            part.Mesh.Topology,
            part.Mesh.Bounds,
            part.Mesh.IndexCount,
            material.Program.Lifetime,
            renderState,
            tint);
        record.SetShaderState(material.InstanceData, textures);
        record.BeginInstances(1)[0] = worldMatrix;
    }

    private void RetainShader(Shader shader, ReadOnlySpan<Texture?> textures) {
        _packet.Retention.Add(shader.Program.Lifetime);
        foreach (Texture? texture in textures) {
            if (texture is not null) {
                _packet.Retention.Add(texture.Lifetime);
            }
        }
    }

    private PrimitiveAssets RequirePrimitives() {
        PrimitiveAssets? primitives = _service.Primitives;
        if (primitives is null) {
            throw new InvalidOperationException(
                "The camera's primitive draw helpers need the app's PrimitiveAssets, but the "
                    + "CameraService has none attached. The app attaches them before IGame.Setup runs; a "
                    + "directly constructed CameraService in a test must call AttachPrimitives first.");
        }

        return primitives;
    }

    /// <summary>
    /// Retains the mesh, program, and textures, snapshots the material cursor state, records one draw
    /// with <paramref name="instanceCount"/> instances, and returns the world-matrix slot to fill.
    /// </summary>
    /// <remarks>
    /// Validation happens before any retention or record mutation, so a rejected call leaves the frame
    /// packet and its retention set untouched. Every caller runs on the tick thread from a recording
    /// pass.
    /// </remarks>
    private Span<Matrix4x4> BeginPrimitiveDraw(
        Mesh mesh,
        Shader material,
        RenderState renderState,
        Color tint,
        int instanceCount) {
        ValidateMesh(mesh);
        ObjectDisposedException.ThrowIf(material.IsDisposed, material);
        ReadOnlySpan<Texture?> textures = material.TextureValues;
        ValidateTextures(textures);

        _packet.Retention.Add(mesh.Lifetime);
        RetainShader(material, textures);
        RenderDrawRecord record = BeginDraw(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            material.Program.Lifetime,
            renderState,
            tint);
        record.SetShaderState(material.InstanceData, textures);
        return record.BeginInstances(instanceCount);
    }

    /// <summary>
    /// Builds the right, up, and front axes of a quad that faces along the camera's view direction.
    /// </summary>
    private (Vector3 Right, Vector3 Up, Vector3 Front) GetCameraFacingBasis() {
        Vector3 front = NormalizeOr(-_transform.Forward, Vector3.UnitZ);
        Vector3 upHint = NormalizeOr(_transform.Up, Vector3.UnitY);
        Vector3 right = NormalizeOr(Vector3.Cross(upHint, front), Vector3.UnitX);
        return (right, Vector3.Cross(front, right), front);
    }

    /// <summary>
    /// Builds the right, up, and front axes of a vertical billboard: world up plus the camera yaw.
    /// </summary>
    private (Vector3 Right, Vector3 Up, Vector3 Front) GetVerticalBillboardBasis() {
        Vector3 forward = _transform.Forward;
        Vector3 front = NormalizeOr(new Vector3(-forward.X, 0f, -forward.Z), Vector3.UnitZ);
        Vector3 right = NormalizeOr(Vector3.Cross(Vector3.UnitY, front), Vector3.UnitX);
        return (right, Vector3.UnitY, front);
    }

    private static Matrix4x4 CreateBillboardMatrix(
        Vector3 position,
        float width,
        float height,
        Vector3 right,
        Vector3 up,
        Vector3 front) {
        Matrix4x4 orientation = new(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            front.X, front.Y, front.Z, 0f,
            position.X, position.Y, position.Z, 1f);
        return Matrix4x4.CreateScale(width, height, 1f) * orientation;
    }

    private static Matrix4x4 CreateTranslationScaleMatrix(Vector3 position, Vector3 scale)
        => Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);

    private static Matrix4x4 CreateLineMatrix(Vector3 start, Vector3 end) {
        Vector3 direction = end - start;
        Vector3 axisX = Vector3.Normalize(direction);
        Vector3 helper = MathF.Abs(axisX.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 axisY = Vector3.Normalize(helper - (axisX * Vector3.Dot(helper, axisX)));
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        return new Matrix4x4(
            direction.X, direction.Y, direction.Z, 0f,
            axisY.X, axisY.Y, axisY.Z, 0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0f,
            start.X, start.Y, start.Z, 1f);
    }

    private static Matrix4x4 CreateTriangleMatrix(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 normal,
        Vector3 fallbackNormal) {
        Vector3 axisZ = NormalizeOr(normal, fallbackNormal);
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        return new Matrix4x4(
            edge1.X, edge1.Y, edge1.Z, 0f,
            edge2.X, edge2.Y, edge2.Z, 0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0f,
            a.X, a.Y, a.Z, 1f);
    }

    private static Matrix4x4 CreateCylinderSideMatrix(Vector3 start, Vector3 end, float height) {
        Vector3 chord = end - start;
        float width = chord.Length();
        // The local X axis runs from the next ring point back to this one so the quad's +Z front
        // faces away from the cylinder axis.
        Vector3 axisX = -chord / width;
        Vector3 axisY = Vector3.UnitY;
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        Vector3 center = ((start + end) * 0.5f) + (Vector3.UnitY * (height * 0.5f));
        return new Matrix4x4(
            axisX.X * width, axisX.Y * width, axisX.Z * width, 0f,
            axisY.X * height, axisY.Y * height, axisY.Z * height, 0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0f,
            center.X, center.Y, center.Z, 1f);
    }

    private Matrix4x4 CreateOverlayQuadMatrix(Rect destination) {
        float left = ((destination.X / _viewport.Width) * 2f) - 1f;
        float right = (((destination.X + destination.Width) / _viewport.Width) * 2f) - 1f;
        float top = ((destination.Y / _viewport.Height) * 2f) - 1f;
        float bottom = (((destination.Y + destination.Height) / _viewport.Height) * 2f) - 1f;
        if (!Matrix4x4.Invert(_viewProjectionMatrix, out Matrix4x4 inverse)) {
            throw new InvalidOperationException(
                "The camera view-projection matrix is singular; the screen-space quad cannot be "
                    + "unprojected.");
        }

        Vector3 topLeft = UnprojectOverlayCorner(inverse, left, top);
        Vector3 topRight = UnprojectOverlayCorner(inverse, right, top);
        Vector3 bottomLeft = UnprojectOverlayCorner(inverse, left, bottom);
        Vector3 bottomRight = UnprojectOverlayCorner(inverse, right, bottom);
        // The destination rectangle at a constant NDC depth unprojects to a world-space
        // parallelogram parallel to the camera's image plane, so an affine map from the local unit
        // quad to that parallelogram projects to exactly the same NDC rectangle. Keeping the
        // instance matrix affine is what makes the batcher's Vector3.Transform bounds and raw
        // translation depth correct; a projective matrix has a non-trivial W column that those
        // affine consumers ignore, which culls a translated camera's overlay behind the camera.
        Vector3 x = topRight - topLeft;
        Vector3 y = topLeft - bottomLeft;
        Vector3 z = NormalizeOr(Vector3.Cross(x, y), Vector3.UnitZ);
        Vector3 center = (topLeft + topRight + bottomLeft + bottomRight) * 0.25f;
        Matrix4x4 matrix = new(
            x.X, x.Y, x.Z, 0f,
            y.X, y.Y, y.Z, 0f,
            z.X, z.Y, z.Z, 0f,
            center.X, center.Y, center.Z, 1f);
        ValidateOverlayMatrix(matrix);
        return matrix;
    }

    private static Vector3 UnprojectOverlayCorner(Matrix4x4 inverse, float ndcX, float ndcY) {
        Vector4 world = Vector4.Transform(
            new Vector4(ndcX, ndcY, OVERLAY_NDC_DEPTH, 1f),
            inverse);
        if (!float.IsFinite(world.W) || world.W == 0f) {
            throw new InvalidOperationException(
                "The screen-space quad cannot be unprojected: a destination corner has a zero or "
                    + "non-finite homogeneous coordinate.");
        }

        return new Vector3(world.X / world.W, world.Y / world.W, world.Z / world.W);
    }

    private static void ValidateOverlayMatrix(Matrix4x4 matrix) {
        if (!IsFinite(matrix)) {
            throw new InvalidOperationException(
                "The screen-space quad maps to a world-space matrix with a non-finite component.");
        }

        // The three basis rows are the quad's edges and plane normal, so a non-positive determinant
        // means the unprojected corners collapsed to a line or point and the quad cannot be drawn.
        if (!(matrix.GetDeterminant() > 0f)) {
            throw new InvalidOperationException(
                "The screen-space quad maps to a degenerate world-space matrix.");
        }
    }

    private static bool IsFinite(Matrix4x4 matrix) =>
        float.IsFinite(matrix.M11)
        && float.IsFinite(matrix.M12)
        && float.IsFinite(matrix.M13)
        && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21)
        && float.IsFinite(matrix.M22)
        && float.IsFinite(matrix.M23)
        && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31)
        && float.IsFinite(matrix.M32)
        && float.IsFinite(matrix.M33)
        && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41)
        && float.IsFinite(matrix.M42)
        && float.IsFinite(matrix.M43)
        && float.IsFinite(matrix.M44);

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) {
        float length = value.Length();
        return length > 0f && float.IsFinite(length) ? value / length : fallback;
    }

    private static void ValidateParts(IReadOnlyList<ModelPart> parts) {
        foreach (ModelPart part in parts) {
            ValidateMesh(part.Mesh);
            ValidateShader(part.Shader);
            ValidateTextures(part.Shader.TextureValues);
        }
    }

    private static void ValidateMesh(Mesh mesh) {
        ObjectDisposedException.ThrowIf(mesh.IsDisposed, mesh);
    }

    private static void ValidateShader(Shader shader) {
        ObjectDisposedException.ThrowIf(shader.IsDisposed, shader);
        const ShaderStageMask requiredStages = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if (shader.Stages != requiredStages) {
            throw new InvalidOperationException(
                $"The shader '{shader.Path}' must declare both the vertex and fragment stages to be "
                    + "used in a draw.");
        }
    }

    private static void ValidateTextures(ReadOnlySpan<Texture?> textures) {
        foreach (Texture? texture in textures) {
            if (texture is not null) {
                ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
            }
        }
    }

    private static void ThrowIfModelDisposed(Model model) {
        ObjectDisposedException.ThrowIf(model.IsDisposed, model);
    }

    private static void ValidateFinite(Vector2 value, string parameterName) {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) {
            throw new ArgumentException("The position components must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(Vector3 value, string parameterName) {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) {
            throw new ArgumentException("The position components must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(float value, string parameterName) {
        if (!float.IsFinite(value)) {
            throw new ArgumentException("The value must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(Rect value, string parameterName) {
        if (!float.IsFinite(value.X)
            || !float.IsFinite(value.Y)
            || !float.IsFinite(value.Width)
            || !float.IsFinite(value.Height)) {
            throw new ArgumentException("The rectangle components must be finite.", parameterName);
        }
    }

    private static void ValidateNonNegative(float value, string parameterName) {
        if (value < 0f) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be zero or positive.");
        }
    }

    private static void ValidateNonNegative(Vector2 size, string parameterName) {
        if (size.X < 0f || size.Y < 0f) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                "The extent components must be zero or positive.");
        }
    }

    private static void ValidateNonNegative(Vector3 size, string parameterName) {
        if (size.X < 0f || size.Y < 0f || size.Z < 0f) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                "The extent components must be zero or positive.");
        }
    }

    private static void ValidatePositive(float value, string parameterName) {
        if (!(value > 0f)) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be positive.");
        }
    }

    private void EnsureDrawable(string operation) {
        ThrowIfDisposed();
        TickThread.RequireCurrent(operation);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
