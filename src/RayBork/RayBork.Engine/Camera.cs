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
    private const int CIRCLE_SEGMENTS = 36;
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RAY_DRAW_DISTANCE = 1_000_000f;

    private static readonly (int Start, int End)[] _boxEdges = [
        (0, 1),
        (2, 3),
        (4, 5),
        (6, 7),
        (0, 2),
        (1, 3),
        (4, 6),
        (5, 7),
        (0, 4),
        (1, 5),
        (2, 6),
        (3, 7),
    ];

    private static readonly Rect _fullTextureRect = new(0f, 0f, 1f, 1f);
    private static readonly Vector2 _centerOrigin = new(0.5f, 0.5f);

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
                part.LocalTransform.ToMatrix() * instanceMatrix,
                wireframe: false);
        }
    }

    /// <summary>
    /// Records a model as wireframe: every part is drawn with its own material and the polygon-mode-line
    /// state.
    /// </summary>
    /// <param name="model">The model to draw.</param>
    /// <param name="transform">The model instance transform applied after each part's local transform.</param>
    /// <param name="tint">The per-instance tint, or <see langword="null"/> for opaque white.</param>
    /// <remarks>
    /// <para>
    /// This is the raylib <c>DrawModelWires</c>/<c>DrawModelWiresEx</c> equivalent. It is
    /// Transform-only by design: the transform supplies the position, rotation, and scale, and there
    /// are no position, scale, or axis-angle overloads and no <c>Ex</c>/<c>Wires</c> suffix names.
    /// </para>
    /// <para>
    /// The draw is <see cref="DrawModel"/> with a polygon-mode-line geometry pipeline: every part still
    /// uses its own material snapshot (program, uniform cursor, and texture bindings at the call) and
    /// its <see cref="ModelPart.RenderState"/>. Culling, depth test, depth write, and blending are
    /// unchanged, so back faces stay culled and the visible triangle edges of front-facing geometry are
    /// drawn. The tint, textures, and alpha behave exactly as in <see cref="DrawModel"/>: built-in
    /// shaders multiply the tint into their output and sample the bound textures on the line
    /// fragments. <c>world = part.LocalTransform.ToMatrix() * transform.ToMatrix()</c> under the
    /// row-vector convention, and the transform scale and rotation apply to the part geometry.
    /// </para>
    /// <para>
    /// The wireframe pipeline needs the device's <c>fillModeNonSolid</c> feature, which the renderer
    /// enables when the device supports it. A device without the feature reports the draw as an
    /// unsupported <see cref="GraphicsException"/> (<c>RB3001</c>) instead of silently drawing nothing;
    /// M5 has no line-geometry fallback. Every wireframe pipeline is cached and keyed by the shader,
    /// render state, mesh topology, and polygon mode, so it never merges with a filled draw over the
    /// same mesh, program, and state.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <c>null</c>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, model, a part mesh, a part material, or a bound texture is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A part material lacks a stage, or the caller is not the tick thread.
    /// </exception>
    public void DrawWireModel(Model model, Transform transform, Color? tint = null) {
        EnsureDrawable(nameof(DrawWireModel));
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
                part.LocalTransform.ToMatrix() * instanceMatrix,
                wireframe: true);
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
            tint ?? Color.White,
            wireframe: false);
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
            tint ?? Color.White,
            wireframe: false);
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
                effectiveTint,
                wireframe: false);
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
    /// Records a triangle strip as one instanced triangle draw.
    /// </summary>
    /// <param name="points">The world-space strip vertices in order.</param>
    /// <param name="color">The linear triangle color.</param>
    /// <remarks>
    /// <para>
    /// The strip is decomposed into <c>points.Length - 2</c> triangles. The first triangle uses the
    /// first three points; every following triangle swaps its first two corners, which is the sliding
    /// window of <see cref="PrimitiveTopology.TriangleStrip"/>. Each triangle keeps the same winding
    /// and front-face culling as <see cref="DrawTriangle"/>.
    /// </para>
    /// <para>
    /// A degenerate (zero-area) triangle records no instance, and fewer than three points records
    /// nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a point is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawTriangleStrip(ReadOnlySpan<Vector3> points, Color color) {
        EnsureDrawable(nameof(DrawTriangleStrip));
        for (int index = 0; index < points.Length; index++) {
            ValidateFinite(points[index], nameof(points));
        }

        if (points.Length < 3) {
            return;
        }

        int triangleCount = 0;
        for (int triangle = 0; triangle + 2 < points.Length; triangle++) {
            Vector3 a = GetStripCorner(points, triangle, 0);
            Vector3 b = GetStripCorner(points, triangle, 1);
            Vector3 c = GetStripCorner(points, triangle, 2);
            if (Vector3.Cross(b - a, c - a).LengthSquared() > 0f) {
                triangleCount++;
            }
        }

        if (triangleCount == 0) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            triangleCount);
        int matrixIndex = 0;
        for (int triangle = 0; triangle + 2 < points.Length; triangle++) {
            Vector3 a = GetStripCorner(points, triangle, 0);
            Vector3 b = GetStripCorner(points, triangle, 1);
            Vector3 c = GetStripCorner(points, triangle, 2);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() > 0f) {
                matrices[matrixIndex++] = CreateTriangleMatrix(a, b, c, normal, Vector3.UnitZ);
            }
        }
    }

    /// <summary>
    /// Records a ray as a long world-space line segment along its unit direction.
    /// </summary>
    /// <param name="ray">The world-space ray.</param>
    /// <param name="color">The linear line color.</param>
    /// <remarks>
    /// <para>
    /// A ray is infinite; the draw approximates it with the segment from <see cref="Ray.Origin"/> to
    /// <see cref="Ray.Origin"/> plus <see cref="Ray.Direction"/> times <c>1,000,000</c> world units.
    /// The segment is clipped by the view and the depth range like any other world-space line, so it
    /// reads as an infinite ray while every buffer coordinate stays finite. The ray stores a unit
    /// direction, so the far endpoint is exactly that distance away.
    /// </para>
    /// <para>A <c>default(Ray)</c> has a zero direction and records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of the origin is not finite, or the far endpoint is not finite.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawRay(Ray ray, Color color) {
        EnsureDrawable(nameof(DrawRay));
        ValidateFinite(ray.Origin, nameof(ray));
        ValidateFinite(ray.Direction, nameof(ray));
        if (!(ray.Direction.LengthSquared() > 0f)) {
            return;
        }

        Vector3 end = ray.Origin + (ray.Direction * RAY_DRAW_DISTANCE);
        ValidateFinite(end, nameof(ray));

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1);
        matrices[0] = CreateLineMatrix(ray.Origin, end);
    }

    /// <summary>
    /// Records the twelve edges of a box as one instanced line draw.
    /// </summary>
    /// <param name="box">The box; its corners come from <see cref="BoundingBox.Min"/> and
    /// <see cref="BoundingBox.Max"/>.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// Each edge is one engine line instance. An edge whose endpoints coincide records no instance, so
    /// a box with a zero extent draws only its non-degenerate edges. An inverted box is drawn as
    /// given.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <see cref="BoundingBox.Min"/> or
    /// <see cref="BoundingBox.Max"/> is not finite.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawBoundingBox(BoundingBox box, Color color) {
        EnsureDrawable(nameof(DrawBoundingBox));
        ValidateFinite(box.Min, nameof(box));
        ValidateFinite(box.Max, nameof(box));

        Span<Vector3> corners = stackalloc Vector3[8];
        for (int corner = 0; corner < corners.Length; corner++) {
            corners[corner] = new Vector3(
                (corner & 1) == 0 ? box.Min.X : box.Max.X,
                (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                (corner & 4) == 0 ? box.Min.Z : box.Max.Z);
        }

        int edgeCount = 0;
        foreach ((int start, int end) in _boxEdges) {
            if ((corners[end] - corners[start]).LengthSquared() > 0f) {
                edgeCount++;
            }
        }

        if (edgeCount == 0) {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            edgeCount);
        int matrixIndex = 0;
        foreach ((int start, int end) in _boxEdges) {
            if ((corners[end] - corners[start]).LengthSquared() > 0f) {
                matrices[matrixIndex++] = CreateLineMatrix(corners[start], corners[end]);
            }
        }
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
    /// Records one solid box centered at <paramref name="position"/> with explicit width, height, and
    /// depth.
    /// </summary>
    /// <param name="position">The world-space center.</param>
    /// <param name="width">The world-space width along <c>X</c>.</param>
    /// <param name="height">The world-space height along <c>Y</c>.</param>
    /// <param name="length">The world-space depth along <c>Z</c>.</param>
    /// <param name="color">The linear box color.</param>
    /// <remarks>
    /// This is the scalar convenience form of <see cref="DrawCube(Vector3, Vector3, Color)"/>: the
    /// dimensions map to <c>(X, Y, Z)</c> size. A zero component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a dimension or of
    /// <paramref name="position"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCube(Vector3 position, float width, float height, float length, Color color) {
        EnsureDrawable(nameof(DrawCube));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));
        ValidateFinite(length, nameof(length));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(length, nameof(length));
        DrawCube(position, new Vector3(width, height, length), color);
    }

    /// <summary>
    /// Records one solid box at a transform with the transform's scale applied to
    /// <paramref name="size"/>.
    /// </summary>
    /// <param name="transform">The box transform; its position is the box center.</param>
    /// <param name="size">The local-space width, height, and depth before the transform scale.</param>
    /// <param name="color">The linear box color.</param>
    /// <remarks>
    /// <para>
    /// The instance matrix is <c>Matrix4x4.CreateScale(size) * transform.ToMatrix()</c> under the
    /// row-vector convention. The transform position is the box center, the transform rotation orients
    /// the box, and the transform scale multiplies <paramref name="size"/> on each local axis. Identity
    /// rotation and unit scale reproduce <see cref="DrawCube(Vector3, Vector3, Color)"/> at
    /// <see cref="Transform.Position"/>.
    /// </para>
    /// <para>A zero size component or a zero transform scale component records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">A component of the transform or
    /// <paramref name="size"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component or a transform scale component
    /// is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCube(Transform transform, Vector3 size, Color color) {
        EnsureDrawable(nameof(DrawCube));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f || HasZeroScale(transform)) {
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
        matrices[0] = Matrix4x4.CreateScale(size) * transform.ToMatrix();
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
    /// Records one solid sphere at a transform with the transform's scale applied to
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="transform">The sphere transform; its position is the sphere center.</param>
    /// <param name="radius">The local-space radius before the transform scale.</param>
    /// <param name="color">The linear sphere color.</param>
    /// <remarks>
    /// <para>
    /// The instance matrix is <c>Matrix4x4.CreateScale(radius) * transform.ToMatrix()</c> under the
    /// row-vector convention. The transform position is the sphere center, the rotation orients it, and
    /// the scale multiplies the radius on each local axis, so a non-uniform scale produces an
    /// ellipsoid. Identity rotation and unit scale reproduce
    /// <see cref="DrawSphere(Vector3, float, Color)"/> at <see cref="Transform.Position"/>.
    /// </para>
    /// <para>A zero radius or a zero transform scale component records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius or a transform scale component is
    /// negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawSphere(Transform transform, float radius, Color color) {
        EnsureDrawable(nameof(DrawSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform)) {
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
        matrices[0] = Matrix4x4.CreateScale(radius) * transform.ToMatrix();
    }

    /// <summary>
    /// Records one solid tessellated sphere centered at <paramref name="center"/>.
    /// </summary>
    /// <param name="center">The world-space center.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="rings">The number of latitude bands; at least two.</param>
    /// <param name="slices">The number of longitude segments; at least three.</param>
    /// <param name="color">The linear sphere color.</param>
    /// <remarks>
    /// Unlike <see cref="DrawSphere(Vector3, float, Color)"/>, which instances the fixed engine sphere
    /// mesh, this overload composes the requested surface as one instanced triangle draw with
    /// <c>2 * slices * (rings - 1)</c> triangles. The two poles are triangle fans and the seam closes
    /// without a duplicated meridian. A zero radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="center"/> or
    /// <paramref name="radius"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawSphere(Vector3 center, float radius, int rings, int slices, Color color) {
        EnsureDrawable(nameof(DrawSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f) {
            return;
        }

        DrawTessellatedSphere(
            Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center),
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records one solid tessellated sphere at a transform with the transform's scale applied to
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="transform">The sphere transform; its position is the sphere center.</param>
    /// <param name="radius">The local-space radius before the transform scale.</param>
    /// <param name="rings">The number of latitude bands; at least two.</param>
    /// <param name="slices">The number of longitude segments; at least three.</param>
    /// <param name="color">The linear sphere color.</param>
    /// <remarks>
    /// The surface is composed as in <see cref="DrawSphere(Vector3, float, int, int, Color)"/> and its
    /// instance matrix is <c>Matrix4x4.CreateScale(radius) * transform.ToMatrix()</c>, so the transform
    /// position is the center, the rotation orients the surface, and the scale multiplies the radius on
    /// each local axis. A zero radius or a zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The radius or a transform scale component is negative, <paramref name="rings"/> is less than
    /// two, or <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawSphere(Transform transform, float radius, int rings, int slices, Color color) {
        EnsureDrawable(nameof(DrawSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f || HasZeroScale(transform)) {
            return;
        }

        DrawTessellatedSphere(
            Matrix4x4.CreateScale(radius) * transform.ToMatrix(),
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records one capped equal-radius cylinder whose base is at <paramref name="position"/> and whose
    /// axis points up world <c>+Y</c>, using the default 16 side segments.
    /// </summary>
    /// <param name="position">The world-space center of the base cap.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="height">The world-space height above the base.</param>
    /// <param name="color">The linear cylinder color.</param>
    /// <remarks>
    /// This is the convenience overload for callers that do not need a tessellation argument; it calls
    /// <see cref="DrawCylinder(Vector3, float, float, Color, int)"/> with <c>16</c> sides. A radius or
    /// height of zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/>, <paramref name="radius"/>, or
    /// <paramref name="height"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> or <paramref name="height"/> is negative.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCylinder(Vector3 position, float radius, float height, Color color) {
        DrawCylinder(position, radius, height, color, sides: 16);
    }

    /// <summary>
    /// Records one capped equal-radius cylinder whose base is at <paramref name="position"/> and whose
    /// axis points up world <c>+Y</c>.
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
    public void DrawCylinder(Vector3 position, float radius, float height, Color color, int sides) {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (radius == 0f || height == 0f) {
            return;
        }

        DrawCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radius,
            radius,
            height,
            sides,
            color);
    }

    /// <summary>
    /// Records one capped cylinder or cone with unequal cap radii whose base is at
    /// <paramref name="position"/> and whose axis points up world <c>+Y</c>.
    /// </summary>
    /// <param name="position">The world-space center of the bottom cap.</param>
    /// <param name="radiusTop">The radius of the top cap; zero makes a cone apex.</param>
    /// <param name="radiusBottom">The radius of the bottom cap; zero makes an inverted cone apex.</param>
    /// <param name="height">The world-space height above the bottom cap.</param>
    /// <param name="sides">The number of side segments; must be at least three.</param>
    /// <param name="color">The linear cylinder color.</param>
    /// <remarks>
    /// <para>
    /// <paramref name="radiusBottom"/> is the cap at <paramref name="position"/> and
    /// <paramref name="radiusTop"/> is the cap <paramref name="height"/> above it. Equal positive radii
    /// are recorded as two instanced draws: <paramref name="sides"/> side quads and <c>2 * sides</c>
    /// cap triangles with outward-facing winding. Unequal positive radii are a tapered cylinder that
    /// needs two triangles per side segment, so it is recorded as one instanced triangle draw with
    /// <c>4 * sides</c> triangles: two sides and one triangle per cap for each segment. When exactly
    /// one radius is zero the shape is a cone and is recorded as one instanced triangle draw with
    /// <c>2 * sides</c> triangles: <paramref name="sides"/> sides and <paramref name="sides"/> base-cap
    /// triangles; the degenerate cap at the apex records nothing.
    /// </para>
    /// <para>A zero height or two zero radii records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/>, <paramref name="radiusTop"/>,
    /// <paramref name="radiusBottom"/>, or <paramref name="height"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius or the height is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f)) {
            return;
        }

        DrawCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radiusTop,
            radiusBottom,
            height,
            sides,
            color);
    }

    /// <summary>
    /// Records one capped cylinder between two axis endpoints.
    /// </summary>
    /// <param name="start">The world-space center of the start cap.</param>
    /// <param name="end">The world-space center of the end cap.</param>
    /// <param name="startRadius">The radius of the cap at <paramref name="start"/>.</param>
    /// <param name="endRadius">The radius of the cap at <paramref name="end"/>.</param>
    /// <param name="sides">The number of side segments; must be at least three.</param>
    /// <param name="color">The linear cylinder color.</param>
    /// <remarks>
    /// The local <c>+Y</c> axis is oriented from <paramref name="start"/> to <paramref name="end"/> and
    /// the height is the segment length, so the cap radii apply at the matching endpoints. A zero
    /// radius on one end makes a cone. The roll around the segment is deterministic. A zero-length
    /// segment or two zero radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an endpoint or radius is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(startRadius, nameof(startRadius));
        ValidateFinite(endRadius, nameof(endRadius));
        ValidateNonNegative(startRadius, nameof(startRadius));
        ValidateNonNegative(endRadius, nameof(endRadius));
        ValidateSides(sides);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || (startRadius == 0f && endRadius == 0f)) {
            return;
        }

        DrawCylinderCore(
            CreateSegmentMatrix(start, end),
            endRadius,
            startRadius,
            axis.Length(),
            sides,
            color);
    }

    /// <summary>
    /// Records a capped cylinder at a transform with the transform's scale applied to its dimensions.
    /// </summary>
    /// <param name="transform">The cylinder transform; its position is the center of the bottom cap.</param>
    /// <param name="radiusTop">The local-space top radius before the transform scale.</param>
    /// <param name="radiusBottom">The local-space bottom radius before the transform scale.</param>
    /// <param name="height">The local-space height above the bottom cap before the transform scale.</param>
    /// <param name="sides">The number of side segments; must be at least three.</param>
    /// <param name="color">The linear cylinder color.</param>
    /// <remarks>
    /// <para>
    /// The cylinder's local axis is <c>+Y</c> and its bottom cap is at the local origin, so the
    /// transform position is the bottom-cap center, <see cref="Transform.Up"/> is the axis, and
    /// <see cref="Transform.Position"/> plus <c>Up * height * Scale.Y</c> is the top-cap center. The
    /// instance matrices are composed as <c>local * transform.ToMatrix()</c>, so the transform scale
    /// multiplies the radii on the local X/Z axes and the height on the local Y axis. Identity rotation
    /// and unit scale reproduce <see cref="DrawCylinder(Vector3, float, float, Color, int)"/> at
    /// <see cref="Transform.Position"/>.
    /// </para>
    /// <para>A zero height, two zero radii, or a zero transform scale component records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">A radius, the height, or a transform component is not
    /// finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius, the height, or a transform scale component is negative, or <paramref name="sides"/>
    /// is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateTransform(transform);
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f) || HasZeroScale(transform)) {
            return;
        }

        DrawCylinderCore(transform.ToMatrix(), radiusTop, radiusBottom, height, sides, color);
    }

    /// <summary>
    /// Records a wire cylinder whose base is at <paramref name="position"/> and whose axis points up
    /// world <c>+Y</c>.
    /// </summary>
    /// <param name="position">The world-space center of the bottom cap.</param>
    /// <param name="radiusTop">The radius of the top ring; zero omits the ring.</param>
    /// <param name="radiusBottom">The radius of the bottom ring; zero omits the ring.</param>
    /// <param name="height">The world-space height above the bottom cap.</param>
    /// <param name="sides">The number of side and ring segments; must be at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The wire is one instanced line draw with <paramref name="sides"/> vertical edges plus one ring
    /// of <paramref name="sides"/> segments for each non-zero radius, so
    /// <c>sides * (1 + nonZeroRadiusCount)</c> line instances. It contributes no triangles. A zero
    /// height or two zero radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="position"/> or a radius or
    /// the height is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius or the height is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f)) {
            return;
        }

        DrawWireCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radiusTop,
            radiusBottom,
            height,
            sides,
            color);
    }

    /// <summary>
    /// Records a wire cylinder between two axis endpoints.
    /// </summary>
    /// <param name="start">The world-space center of the start ring.</param>
    /// <param name="end">The world-space center of the end ring.</param>
    /// <param name="startRadius">The radius of the ring at <paramref name="start"/>.</param>
    /// <param name="endRadius">The radius of the ring at <paramref name="end"/>.</param>
    /// <param name="sides">The number of side and ring segments; must be at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The local <c>+Y</c> axis is oriented from <paramref name="start"/> to <paramref name="end"/> and
    /// the height is the segment length. The instance count follows
    /// <see cref="DrawWireCylinder(Vector3, float, float, float, int, Color)"/>. A zero-length segment
    /// or two zero radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an endpoint or radius is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(startRadius, nameof(startRadius));
        ValidateFinite(endRadius, nameof(endRadius));
        ValidateNonNegative(startRadius, nameof(startRadius));
        ValidateNonNegative(endRadius, nameof(endRadius));
        ValidateSides(sides);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || (startRadius == 0f && endRadius == 0f)) {
            return;
        }

        DrawWireCylinderCore(
            CreateSegmentMatrix(start, end),
            endRadius,
            startRadius,
            axis.Length(),
            sides,
            color);
    }

    /// <summary>
    /// Records a wire cylinder at a transform with the transform's scale applied to its dimensions.
    /// </summary>
    /// <param name="transform">The cylinder transform; its position is the bottom-ring center.</param>
    /// <param name="radiusTop">The local-space top radius before the transform scale.</param>
    /// <param name="radiusBottom">The local-space bottom radius before the transform scale.</param>
    /// <param name="height">The local-space height above the bottom ring before the transform scale.</param>
    /// <param name="sides">The number of side and ring segments; must be at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The local axis is <c>+Y</c> with the bottom ring at the local origin, so the transform position
    /// is the bottom-ring center and the transform scale multiplies the radii and height. A zero
    /// height, two zero radii, or a zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A radius, the height, or a transform component is not
    /// finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A radius, the height, or a transform scale component is negative, or <paramref name="sides"/>
    /// is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateTransform(transform);
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f) || HasZeroScale(transform)) {
            return;
        }

        DrawWireCylinderCore(transform.ToMatrix(), radiusTop, radiusBottom, height, sides, color);
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
    /// Records one horizontal plane at a transform with the transform's scale applied to
    /// <paramref name="size"/>.
    /// </summary>
    /// <param name="transform">The plane transform; its position is the plane center.</param>
    /// <param name="size">The local-space width along <c>X</c> and depth along <c>Z</c> before the
    /// transform scale.</param>
    /// <param name="color">The linear plane color.</param>
    /// <remarks>
    /// The plane's local surface is the <c>XZ</c> plane facing local <c>+Y</c>, so the instance matrix
    /// is <c>Matrix4x4.CreateScale(size.X, 1, size.Y) * transform.ToMatrix()</c> under the row-vector
    /// convention: the transform position is the plane center, the rotation orients the surface, and
    /// the transform's X and Z scale components multiply the width and depth. Identity rotation and
    /// unit scale reproduce <see cref="DrawPlane(Vector3, Vector2, Color)"/> at
    /// <see cref="Transform.Position"/>. A zero size component or a zero transform scale component
    /// records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of the transform or
    /// <paramref name="size"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component or a transform scale component
    /// is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawPlane(Transform transform, Vector2 size, Color color) {
        EnsureDrawable(nameof(DrawPlane));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform)) {
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
        matrices[0] = Matrix4x4.CreateScale(size.X, 1f, size.Y) * transform.ToMatrix();
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
    /// Records the twelve edges of a box centered at <paramref name="position"/> with explicit width,
    /// height, and depth.
    /// </summary>
    /// <param name="position">The world-space center.</param>
    /// <param name="width">The world-space width along <c>X</c>.</param>
    /// <param name="height">The world-space height along <c>Y</c>.</param>
    /// <param name="length">The world-space depth along <c>Z</c>.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// This is the scalar convenience form of <see cref="DrawWireCube(Vector3, Vector3, Color)"/>: the
    /// dimensions map to <c>(X, Y, Z)</c> size. A zero component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a dimension or of
    /// <paramref name="position"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCube(Vector3 position, float width, float height, float length, Color color) {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));
        ValidateFinite(length, nameof(length));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(length, nameof(length));
        DrawWireCube(position, new Vector3(width, height, length), color);
    }

    /// <summary>
    /// Records the twelve edges of a box at a transform with the transform's scale applied to
    /// <paramref name="size"/>.
    /// </summary>
    /// <param name="transform">The box transform; its position is the box center.</param>
    /// <param name="size">The local-space width, height, and depth before the transform scale.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The instance matrix is <c>Matrix4x4.CreateScale(size) * transform.ToMatrix()</c> under the
    /// row-vector convention, so the transform position is the box center, the rotation orients the
    /// edges, and the scale multiplies <paramref name="size"/>. Identity rotation and unit scale
    /// reproduce <see cref="DrawWireCube(Vector3, Vector3, Color)"/> at
    /// <see cref="Transform.Position"/>. A zero size component or a zero transform scale component
    /// records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of the transform or
    /// <paramref name="size"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component or a transform scale component
    /// is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCube(Transform transform, Vector3 size, Color color) {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f || HasZeroScale(transform)) {
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
        matrices[0] = Matrix4x4.CreateScale(size) * transform.ToMatrix();
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
    /// Records three great circles at a transform with the transform's scale applied to
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="transform">The wire-sphere transform; its position is the center.</param>
    /// <param name="radius">The local-space radius before the transform scale.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The instance matrix is <c>Matrix4x4.CreateScale(radius) * transform.ToMatrix()</c>, so the
    /// transform position is the center, the rotation orients the great circles, and the scale
    /// multiplies the radius on each local axis. Identity rotation and unit scale reproduce
    /// <see cref="DrawWireSphere(Vector3, float, Color)"/> at <see cref="Transform.Position"/>. A zero
    /// radius or a zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius or a transform scale component is
    /// negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireSphere(Transform transform, float radius, Color color) {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform)) {
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
        matrices[0] = Matrix4x4.CreateScale(radius) * transform.ToMatrix();
    }

    /// <summary>
    /// Records a tessellated wire sphere centered at <paramref name="center"/>.
    /// </summary>
    /// <param name="center">The world-space center.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="rings">The number of latitude bands; at least two.</param>
    /// <param name="slices">The number of longitude segments; at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// Unlike <see cref="DrawWireSphere(Vector3, float, Color)"/>, which instances the fixed
    /// wire-sphere mesh, this overload composes <paramref name="slices"/> meridians of
    /// <paramref name="rings"/> segments each plus <c>rings - 1</c> latitude circles of
    /// <paramref name="slices"/> segments each, for <c>slices * (2 * rings - 1)</c> line instances in
    /// one draw. The poles and seam are shared and the draw contributes no triangles. A zero radius
    /// records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="center"/> or
    /// <paramref name="radius"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireSphere(Vector3 center, float radius, int rings, int slices, Color color) {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f) {
            return;
        }

        DrawTessellatedWireSphere(
            Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center),
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records a tessellated wire sphere at a transform with the transform's scale applied to
    /// <paramref name="radius"/>.
    /// </summary>
    /// <param name="transform">The wire-sphere transform; its position is the center.</param>
    /// <param name="radius">The local-space radius before the transform scale.</param>
    /// <param name="rings">The number of latitude bands; at least two.</param>
    /// <param name="slices">The number of longitude segments; at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The line instances are composed as in
    /// <see cref="DrawWireSphere(Vector3, float, int, int, Color)"/> and transformed by
    /// <c>Matrix4x4.CreateScale(radius) * transform.ToMatrix()</c>. A zero radius or a zero transform
    /// scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The radius or a transform scale component is negative, <paramref name="rings"/> is less than
    /// two, or <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireSphere(Transform transform, float radius, int rings, int slices, Color color) {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f || HasZeroScale(transform)) {
            return;
        }

        DrawTessellatedWireSphere(
            Matrix4x4.CreateScale(radius) * transform.ToMatrix(),
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records one solid capsule between two spherical cap centers.
    /// </summary>
    /// <param name="start">The world-space center of the start spherical cap.</param>
    /// <param name="end">The world-space center of the end spherical cap.</param>
    /// <param name="radius">The cap radius.</param>
    /// <param name="rings">The number of latitude bands per hemispherical cap; at least two.</param>
    /// <param name="slices">The number of longitude segments around the axis; at least three.</param>
    /// <param name="color">The linear capsule color.</param>
    /// <remarks>
    /// The capsule is the cylinder between the two cap centers plus one hemisphere at each end. It is
    /// recorded as two instanced draws: <paramref name="slices"/> side quads and
    /// <c>2 * slices * (2 * rings - 1)</c> cap triangles. The cap-center distance is the segment
    /// length, so the total capsule height is <c>|end - start| + 2 * radius</c>. A zero radius or a
    /// zero-length segment records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an endpoint or the radius is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color) {
        EnsureDrawable(nameof(DrawCapsule));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || radius == 0f) {
            return;
        }

        float height = axis.Length();
        DrawCapsuleCore(
            CreateSegmentMatrix((start + end) * 0.5f, end),
            radius,
            height,
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records one solid capsule at a transform; the capsule is centered at the transform origin and
    /// its axis is local <c>+Y</c>.
    /// </summary>
    /// <param name="transform">The capsule transform; the capsule is centered at its position.</param>
    /// <param name="radius">The local-space cap radius before the transform scale.</param>
    /// <param name="height">The local-space distance between the two cap centers before the transform
    /// scale.</param>
    /// <param name="rings">The number of latitude bands per hemispherical cap; at least two.</param>
    /// <param name="slices">The number of longitude segments around the axis; at least three.</param>
    /// <param name="color">The linear capsule color.</param>
    /// <remarks>
    /// <para>
    /// The cap centers are at local <c>(0, ±height/2, 0)</c>, so the capsule is centered on the
    /// transform origin and its total height is <c>(height + 2 * radius) * Scale.Y</c> along
    /// <see cref="Transform.Up"/>. The instance matrices are composed as
    /// <c>local * transform.ToMatrix()</c>, so the transform rotation orients the capsule and the scale
    /// multiplies the radius and height.
    /// </para>
    /// <para>A zero radius, zero height, or zero transform scale component records nothing.</para>
    /// </remarks>
    /// <exception cref="ArgumentException">The radius, height, or a transform component is not
    /// finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The radius, height, or a transform scale component is negative, <paramref name="rings"/> is
    /// less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color) {
        EnsureDrawable(nameof(DrawCapsule));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateTessellation(rings, slices);
        if (radius == 0f || height == 0f || HasZeroScale(transform)) {
            return;
        }

        DrawCapsuleCore(transform.ToMatrix(), radius, height, rings, slices, color);
    }

    /// <summary>
    /// Records the wire frame of a capsule between two spherical cap centers.
    /// </summary>
    /// <param name="start">The world-space center of the start spherical cap.</param>
    /// <param name="end">The world-space center of the end spherical cap.</param>
    /// <param name="radius">The cap radius.</param>
    /// <param name="rings">The number of latitude bands per hemispherical cap; at least two.</param>
    /// <param name="slices">The number of longitude segments around the axis; at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The wire frame is one instanced line draw with <c>slices * (4 * rings + 1)</c> segments:
    /// <paramref name="slices"/> body edges, one equator ring per end, and per hemispherical cap
    /// <paramref name="slices"/> meridians of <paramref name="rings"/> segments plus <c>rings - 1</c>
    /// interior latitude rings. It contributes no triangles. A zero radius or a zero-length segment
    /// records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an endpoint or the radius is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color) {
        EnsureDrawable(nameof(DrawWireCapsule));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || radius == 0f) {
            return;
        }

        float height = axis.Length();
        DrawWireCapsuleCore(
            CreateSegmentMatrix((start + end) * 0.5f, end),
            radius,
            height,
            rings,
            slices,
            color);
    }

    /// <summary>
    /// Records the wire frame of a capsule at a transform; the capsule is centered at the transform
    /// origin and its axis is local <c>+Y</c>.
    /// </summary>
    /// <param name="transform">The capsule transform; the capsule is centered at its position.</param>
    /// <param name="radius">The local-space cap radius before the transform scale.</param>
    /// <param name="height">The local-space distance between the two cap centers before the transform
    /// scale.</param>
    /// <param name="rings">The number of latitude bands per hemispherical cap; at least two.</param>
    /// <param name="slices">The number of longitude segments around the axis; at least three.</param>
    /// <param name="color">The linear wire color.</param>
    /// <remarks>
    /// The wire frame is composed as in
    /// <see cref="DrawWireCapsule(Vector3, Vector3, float, int, int, Color)"/> with the cap centers at
    /// local <c>±height/2</c> on <c>Y</c>, then transformed by <c>transform.ToMatrix()</c>. A zero
    /// radius, zero height, or zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius, height, or a transform component is not
    /// finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The radius, height, or a transform scale component is negative, <paramref name="rings"/> is
    /// less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawWireCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color) {
        EnsureDrawable(nameof(DrawWireCapsule));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateTessellation(rings, slices);
        if (radius == 0f || height == 0f || HasZeroScale(transform)) {
            return;
        }

        DrawWireCapsuleCore(transform.ToMatrix(), radius, height, rings, slices, color);
    }

    /// <summary>
    /// Records a closed circle in the plane defined by a center, a rotation axis, and a rotation
    /// angle.
    /// </summary>
    /// <param name="center">The world-space circle center.</param>
    /// <param name="radius">The world-space radius.</param>
    /// <param name="rotationAxis">The axis the base circle is rotated around; must be non-zero.</param>
    /// <param name="rotationAngle">The rotation around <paramref name="rotationAxis"/> in degrees.</param>
    /// <param name="color">The linear line color.</param>
    /// <remarks>
    /// The base circle lies in the local <c>XZ</c> plane with local <c>+Y</c> as its normal, matching
    /// <see cref="DrawPlane(Vector3, Vector2, Color)"/>, and is rotated by
    /// <paramref name="rotationAngle"/> degrees around <paramref name="rotationAxis"/> before it is
    /// translated to <paramref name="center"/>. The circle is 36 line segments and closes the loop. A
    /// zero radius records nothing; a zero rotation axis is <see cref="ArgumentException"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="center"/>,
    /// <paramref name="radius"/>, <paramref name="rotationAxis"/>, or
    /// <paramref name="rotationAngle"/> is not finite, or <paramref name="rotationAxis"/> is zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCircle(
        Vector3 center,
        float radius,
        Vector3 rotationAxis,
        float rotationAngle,
        Color color) {
        EnsureDrawable(nameof(DrawCircle));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(rotationAxis, nameof(rotationAxis));
        ValidateFinite(rotationAngle, nameof(rotationAngle));
        ValidateNonNegative(radius, nameof(radius));
        float axisLengthSquared = rotationAxis.LengthSquared();
        if (!(axisLengthSquared > 0f)) {
            throw new ArgumentException(
                "The rotation axis must not be zero length.",
                nameof(rotationAxis));
        }

        if (radius == 0f) {
            return;
        }

        Matrix4x4 rotation = Matrix4x4.CreateFromAxisAngle(
            rotationAxis / MathF.Sqrt(axisLengthSquared),
            rotationAngle * DEGREES_TO_RADIANS);
        DrawCircleCore(
            Matrix4x4.CreateScale(radius) * rotation * Matrix4x4.CreateTranslation(center),
            color);
    }

    /// <summary>
    /// Records a closed circle at a transform in its local <c>XZ</c> plane.
    /// </summary>
    /// <param name="transform">The circle transform; its position is the circle center and its local
    /// <c>+Y</c> is the circle normal.</param>
    /// <param name="radius">The local-space radius before the transform scale.</param>
    /// <param name="color">The linear line color.</param>
    /// <remarks>
    /// The base circle is the local <c>XZ</c> plane with local <c>+Y</c> as its normal, matching
    /// <see cref="DrawPlane(Transform, Vector2, Color)"/> and <see cref="DrawGrid"/>. Its instance
    /// matrix is <c>Matrix4x4.CreateScale(radius) * transform.ToMatrix()</c>, so the transform position
    /// is the center, the rotation orients the plane, and the scale multiplies the radius on the local
    /// X and Z axes (a non-uniform scale produces an ellipse). The circle is 36 line segments and
    /// closes the loop. A zero radius or a zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius or a transform scale component is
    /// negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The camera or an engine primitive asset is disposed.</exception>
    public void DrawCircle(Transform transform, float radius, Color color) {
        EnsureDrawable(nameof(DrawCircle));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform)) {
            return;
        }

        DrawCircleCore(Matrix4x4.CreateScale(radius) * transform.ToMatrix(), color);
    }

    /// <summary>
    /// Records one textured, camera-facing quad centered at <paramref name="position"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="position">The world-space position of the quad's origin pivot.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <remarks>
    /// <para>
    /// This is the convenience form of <see cref="DrawBillboard(Texture, Vector3, Vector2, Color, bool)"/>
    /// with <c>faceCamera: true</c> and the full texture: the quad faces the camera and its normalized
    /// origin is its center.
    /// </para>
    /// <para>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth write.
    /// The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A size
    /// component of zero records nothing.
    /// </para>
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
    public void DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color) {
        DrawBillboard(texture, position, size, color, faceCamera: true);
    }

    /// <summary>
    /// Records one textured quad centered at <paramref name="position"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="position">The world-space position of the quad's origin pivot.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <param name="faceCamera">
    /// When <see langword="true"/>, the quad faces the camera. When <see langword="false"/>, the quad is
    /// the existing vertical billboard: it keeps world up and only the camera's yaw rotates it.
    /// </param>
    /// <remarks>
    /// <para>
    /// The quad samples the full texture and uses the quad's center as its origin pivot. The position
    /// overloads have no transform, so a <c>faceCamera: false</c> draw keeps the engine's vertical
    /// billboard orientation.
    /// </para>
    /// <para>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth write.
    /// The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A size
    /// component of zero records nothing.
    /// </para>
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
        bool faceCamera) {
        DrawBillboardAtPosition(
            texture,
            position,
            _fullTextureRect,
            size,
            _centerOrigin,
            color,
            faceCamera);
    }

    /// <summary>
    /// Records one textured, camera-facing quad under <paramref name="transform"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="transform">
    /// The billboard transform; see <see cref="DrawBillboard(Texture, Transform, Vector2, Color, bool)"/>.
    /// </param>
    /// <param name="size">The world-space width and height before the transform scale.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <remarks>
    /// This is the convenience form of
    /// <see cref="DrawBillboard(Texture, Transform, Vector2, Color, bool)"/> with <c>faceCamera: true</c>
    /// and the full texture.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A transform component or a size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A size component or a transform scale component is negative.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color) {
        DrawBillboard(texture, transform, size, color, faceCamera: true);
    }

    /// <summary>
    /// Records one textured quad under <paramref name="transform"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="transform">
    /// The billboard transform. Its position is the origin pivot's world position, its rotation supplies
    /// the in-plane roll when <paramref name="faceCamera"/> is <see langword="true"/> and orients the
    /// plane when it is <see langword="false"/>, and its scale multiplies <paramref name="size"/> on the
    /// local X and Y axes.
    /// </param>
    /// <param name="size">The world-space width and height before the transform scale.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <param name="faceCamera">
    /// When <see langword="true"/>, the quad faces the camera and the transform rotation contributes
    /// only its twist about the billboard's local plane normal as the in-plane roll. When
    /// <see langword="false"/>, the quad does not face the camera: the transform rotation orients the
    /// billboard plane in world space, so an identity transform gives a world-space quad in the local
    /// XY plane. This is intentionally different from the position overloads, whose
    /// <c>faceCamera: false</c> form keeps the camera-relative vertical billboard; an identity
    /// transform does not reproduce that vertical orientation.
    /// </param>
    /// <remarks>
    /// <para>
    /// The quad samples the full texture and uses the quad's center as its origin pivot. With
    /// <c>faceCamera: false</c> the transform rotation orients the plane in world space; the position
    /// overloads keep the camera-relative vertical billboard instead, so an identity transform does not
    /// reproduce the position overload's non-facing orientation.
    /// </para>
    /// <para>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth write.
    /// The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A zero
    /// size component or a zero transform scale component records nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A transform component or a size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A size component or a transform scale component is negative.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Vector2 size,
        Color color,
        bool faceCamera) {
        DrawBillboardWithTransform(
            texture,
            transform,
            _fullTextureRect,
            size,
            _centerOrigin,
            color,
            faceCamera);
    }

    /// <summary>
    /// Records one textured, camera-facing quad that samples a normalized sub-rectangle of
    /// <paramref name="texture"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="source">
    /// The normalized source rectangle in the texture: <see cref="Rect.X"/> and <see cref="Rect.Y"/> are
    /// the top-left corner and <see cref="Rect.Width"/> and <see cref="Rect.Height"/> are the positive
    /// extents, all in <c>[0, 1]</c>.
    /// </param>
    /// <param name="position">The world-space position of the quad's origin pivot.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <remarks>
    /// This is the convenience form of
    /// <see cref="DrawBillboard(Texture, Rect, Vector3, Vector2, Color, bool)"/> with
    /// <c>faceCamera: true</c> and the quad's center as its origin pivot.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/>, <paramref name="size"/>, or
    /// <paramref name="source"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A <paramref name="size"/> component is negative, <paramref name="source"/> does not have positive
    /// width and height, or <paramref name="source"/> lies outside the normalized texture rectangle.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Rect source,
        Vector3 position,
        Vector2 size,
        Color color) {
        DrawBillboard(texture, source, position, size, color, faceCamera: true);
    }

    /// <summary>
    /// Records one textured quad that samples a normalized sub-rectangle of <paramref name="texture"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="source">
    /// The normalized source rectangle in the texture: <see cref="Rect.X"/> and <see cref="Rect.Y"/> are
    /// the top-left corner and <see cref="Rect.Width"/> and <see cref="Rect.Height"/> are the positive
    /// extents, all in <c>[0, 1]</c>.
    /// </param>
    /// <param name="position">The world-space position of the quad's origin pivot.</param>
    /// <param name="size">The world-space width and height of the quad.</param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <param name="faceCamera">
    /// When <see langword="true"/>, the quad faces the camera. When <see langword="false"/>, the quad is
    /// the existing vertical billboard: it keeps world up and only the camera's yaw rotates it.
    /// </param>
    /// <remarks>
    /// <para>
    /// The source rectangle is recorded as the per-instance UV rectangle and the built-in mesh shader
    /// maps the quad's authored UVs through it, so <c>(0, 0, 1, 1)</c> is the same full-texture sample
    /// as the overloads without a source rectangle.
    /// </para>
    /// <para>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth write.
    /// The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A size
    /// component of zero records nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A component of <paramref name="position"/>, <paramref name="size"/>, or
    /// <paramref name="source"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A <paramref name="size"/> component is negative, <paramref name="source"/> does not have positive
    /// width and height, or <paramref name="source"/> lies outside the normalized texture rectangle.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Rect source,
        Vector3 position,
        Vector2 size,
        Color color,
        bool faceCamera) {
        DrawBillboardAtPosition(
            texture,
            position,
            source,
            size,
            _centerOrigin,
            color,
            faceCamera);
    }

    /// <summary>
    /// Records one textured, camera-facing quad with an explicit source rectangle, size, and origin
    /// pivot under <paramref name="transform"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="transform">The billboard transform; see the seven-argument overload.</param>
    /// <param name="source">The normalized source rectangle; see the five-argument source overload.</param>
    /// <param name="size">The world-space width and height before the transform scale.</param>
    /// <param name="origin">
    /// The normalized pivot in the billboard's local quad: <c>(0, 0)</c> is the bottom-left corner and
    /// <c>(1, 1)</c> is the top-right corner. <c>(0.5, 0.5)</c> centers the quad on the transform
    /// position.
    /// </param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <remarks>
    /// This is the convenience form of
    /// <see cref="DrawBillboard(Texture, Transform, Rect, Vector2, Vector2, Color, bool)"/> with
    /// <c>faceCamera: true</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A transform, size, source, or origin component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A size component or a transform scale component is negative, <paramref name="source"/> does not
    /// have positive width and height, or <paramref name="source"/> lies outside the normalized texture
    /// rectangle.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color) {
        DrawBillboard(texture, transform, source, size, origin, color, faceCamera: true);
    }

    /// <summary>
    /// Records one textured quad with an explicit source rectangle, size, and origin pivot under
    /// <paramref name="transform"/>.
    /// </summary>
    /// <param name="texture">The texture to sample; the record retains it until the frame ends.</param>
    /// <param name="transform">
    /// The billboard transform. Its position is the origin pivot's world position, its rotation supplies
    /// the in-plane roll when <paramref name="faceCamera"/> is <see langword="true"/> and orients the
    /// plane when it is <see langword="false"/>, and its scale multiplies <paramref name="size"/> on the
    /// local X and Y axes.
    /// </param>
    /// <param name="source">
    /// The normalized source rectangle in the texture: <see cref="Rect.X"/> and <see cref="Rect.Y"/> are
    /// the top-left corner and <see cref="Rect.Width"/> and <see cref="Rect.Height"/> are the positive
    /// extents, all in <c>[0, 1]</c>.
    /// </param>
    /// <param name="size">The world-space width and height before the transform scale.</param>
    /// <param name="origin">
    /// The normalized pivot in the billboard's local quad: <c>(0, 0)</c> is the bottom-left corner and
    /// <c>(1, 1)</c> is the top-right corner. <c>(0.5, 0.5)</c> centers the quad on the transform
    /// position.
    /// </param>
    /// <param name="color">The linear tint multiplied into the sampled texture.</param>
    /// <param name="faceCamera">
    /// When <see langword="true"/>, the quad faces the camera and the transform rotation contributes
    /// only its twist about the billboard's local plane normal as the in-plane roll. When
    /// <see langword="false"/>, the quad does not face the camera: the transform rotation orients the
    /// billboard plane in world space, so an identity transform gives a world-space quad in the local
    /// XY plane. This is intentionally different from the position overloads, whose
    /// <c>faceCamera: false</c> form keeps the camera-relative vertical billboard; an identity
    /// transform does not reproduce that vertical orientation.
    /// </param>
    /// <remarks>
    /// <para>
    /// The instance matrix maps the engine quad through the local billboard quad, the normalized origin
    /// pivot, and the transform. Under the row-vector convention the local quad is
    /// <c>Translate(-(origin - 0.5)) * Scale(size)</c>, so the pivot lands on the transform position and
    /// the transform scale multiplies <paramref name="size"/>. With <c>faceCamera: true</c> the quad is
    /// rolled by the transform's twist about its local <c>+Z</c> axis and then oriented by the camera
    /// basis; with <c>false</c> the transform orientation is used directly, so an identity transform
    /// gives a world-space quad in the local XY plane. The position overloads instead keep the
    /// camera-relative vertical billboard when they do not face the camera.
    /// </para>
    /// <para>
    /// The source rectangle is recorded as the per-instance UV rectangle and the built-in mesh shader
    /// maps the quad's authored UVs through it, so <c>(0, 0, 1, 1)</c> is the same full-texture sample
    /// as the overloads without a source rectangle.
    /// </para>
    /// <para>
    /// The billboard is one instance of the engine quad mesh and is alpha-blended with no depth write.
    /// The engine material's texture binding is set immediately before the draw is recorded and
    /// cleared immediately afterwards, so the shared material never keeps a game texture past the
    /// call; the recorded frame retains the texture until the renderer releases the frame slot. A zero
    /// size component or a zero transform scale component records nothing.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">
    /// A transform, size, source, or origin component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A size component or a transform scale component is negative, <paramref name="source"/> does not
    /// have positive width and height, or <paramref name="source"/> lies outside the normalized texture
    /// rectangle.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The camera, <paramref name="texture"/>, or an engine primitive asset is disposed.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The camera service has no primitive assets, or the caller is not the tick thread.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera) {
        DrawBillboardWithTransform(texture, transform, source, size, origin, color, faceCamera);
    }

    /// <summary>
    /// Validates and records a billboard positioned by a world-space point with an identity transform.
    /// </summary>
    /// <remarks>
    /// Validation runs before any retention or record mutation. A non-finite argument is
    /// <see cref="ArgumentException"/>, a negative size or a source rectangle that has non-positive
    /// extents or lies outside the normalized texture rectangle is
    /// <see cref="ArgumentOutOfRangeException"/>, and a zero size component records nothing. The
    /// position overloads keep the existing vertical billboard orientation when
    /// <paramref name="faceCamera"/> is <see langword="false"/> because they have no transform
    /// rotation.
    /// </remarks>
    private void DrawBillboardAtPosition(
        Texture texture,
        Vector3 position,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera) {
        EnsureDrawable(nameof(DrawBillboard));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateBillboardSource(source, nameof(source));
        ValidateFinite(origin, nameof(origin));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f) {
            return;
        }

        Matrix4x4 matrix = faceCamera
            ? CreateFacingBillboardMatrix(new Transform(position), size, origin)
            : CreateVerticalBillboardMatrix(position, size, origin);
        RecordBillboard(texture, source, color, matrix);
    }

    /// <summary>
    /// Validates and records a billboard positioned, oriented, and scaled by a <see cref="Transform"/>.
    /// </summary>
    /// <remarks>
    /// Validation runs before any retention or record mutation. A non-finite argument is
    /// <see cref="ArgumentException"/>, a negative size or transform scale component or a source
    /// rectangle that has non-positive extents or lies outside the normalized texture rectangle is
    /// <see cref="ArgumentOutOfRangeException"/>, and a zero size component or a zero transform scale
    /// component records nothing.
    /// </remarks>
    private void DrawBillboardWithTransform(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera) {
        EnsureDrawable(nameof(DrawBillboard));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateBillboardSource(source, nameof(source));
        ValidateFinite(origin, nameof(origin));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform)) {
            return;
        }

        Matrix4x4 matrix = faceCamera
            ? CreateFacingBillboardMatrix(transform, size, origin)
            : CreateBillboardQuadMatrix(size, origin) * transform.ToMatrix();
        RecordBillboard(texture, source, color, matrix);
    }

    /// <summary>
    /// Binds the texture, records the one-instance billboard draw, and clears the shared binding.
    /// </summary>
    /// <remarks>
    /// The caller has already validated every argument and composed the world matrix.
    /// </remarks>
    private void RecordBillboard(Texture texture, Rect source, Color color, Matrix4x4 matrix) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.MeshMaterial;
        Vector4 uvRect = new(source.X, source.Y, source.Width, source.Height);
        // The engine material is shared by every billboard, so the binding exists only around this
        // recording call; the record keeps its own texture reference.
        material.SetUniform(MESH_TEXTURE_BINDING, texture);
        try {
            Span<Matrix4x4> matrices = BeginPrimitiveDraw(
                primitives.Quad,
                material,
                RenderState.AlphaBlend,
                color,
                instanceCount: 1,
                uvRect);
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
        Color tint,
        bool wireframe) {
        RenderDrawRecord record = _packet.RentDrawRecord();
        record.Begin(mesh, topology, bounds, indexCount, program, renderState, tint, wireframe);
        _pass.Add(record);
        return record;
    }

    private void RecordPart(
        ModelPart part,
        RenderState renderState,
        Color tint,
        Matrix4x4 worldMatrix,
        bool wireframe) {
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
            tint,
            wireframe);
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
    /// pass. The convenience overload records the full-texture UV rectangle <c>(0, 0, 1, 1)</c>.
    /// </remarks>
    private Span<Matrix4x4> BeginPrimitiveDraw(
        Mesh mesh,
        Shader material,
        RenderState renderState,
        Color tint,
        int instanceCount) =>
        BeginPrimitiveDraw(
            mesh,
            material,
            renderState,
            tint,
            instanceCount,
            new Vector4(0f, 0f, 1f, 1f));

    /// <summary>
    /// Retains the mesh, program, and textures, snapshots the material cursor state, records one draw
    /// with <paramref name="instanceCount"/> instances and the given per-instance UV rectangle, and
    /// returns the world-matrix slot to fill.
    /// </summary>
    /// <remarks>
    /// Validation happens before any retention or record mutation, so a rejected call leaves the frame
    /// packet and its retention set untouched. Every caller runs on the tick thread from a recording
    /// pass. <paramref name="uvRect"/> is per-instance data and never affects batching.
    /// </remarks>
    private Span<Matrix4x4> BeginPrimitiveDraw(
        Mesh mesh,
        Shader material,
        RenderState renderState,
        Color tint,
        int instanceCount,
        Vector4 uvRect) {
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
            tint,
            wireframe: false);
        record.SetShaderState(material.InstanceData, textures);
        record.SetUvRect(uvRect);
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
        Matrix4x4 orientation = CreateBillboardOrientation(position, right, up, front);
        return Matrix4x4.CreateScale(width, height, 1f) * orientation;
    }

    /// <summary>
    /// Builds the local billboard quad that maps the unit quad so the normalized
    /// <paramref name="origin"/> pivot sits at the local origin and <paramref name="size"/> scales it.
    /// </summary>
    /// <remarks>
    /// The engine quad is one unit square in its local XY plane centered on the origin, so the pivot
    /// offset from the center is <c>origin - 0.5</c>. Under the row-vector convention the translation
    /// is applied before the scale, which puts the pivot at the local origin and keeps the offset in
    /// world-size units.
    /// </remarks>
    private static Matrix4x4 CreateBillboardQuadMatrix(Vector2 size, Vector2 origin) =>
        Matrix4x4.CreateTranslation(-(origin.X - 0.5f), -(origin.Y - 0.5f), 0f)
        * Matrix4x4.CreateScale(size.X, size.Y, 1f);

    /// <summary>
    /// Builds the vertical billboard matrix: world up plus the camera yaw, with the origin pivot and
    /// the world-space position.
    /// </summary>
    private Matrix4x4 CreateVerticalBillboardMatrix(Vector3 position, Vector2 size, Vector2 origin) {
        (Vector3 right, Vector3 up, Vector3 front) = GetVerticalBillboardBasis();
        return CreateBillboardQuadMatrix(size, origin)
            * CreateBillboardOrientation(position, right, up, front);
    }

    /// <summary>
    /// Builds the camera-facing billboard matrix with the transform position and scale and the
    /// transform rotation's in-plane roll.
    /// </summary>
    /// <remarks>
    /// The transform scale multiplies the width and height. The roll is applied around the pivot in the
    /// billboard's local XY plane before the camera basis orients the quad.
    /// </remarks>
    private Matrix4x4 CreateFacingBillboardMatrix(Transform transform, Vector2 size, Vector2 origin) {
        Vector2 scaledSize = new(size.X * transform.Scale.X, size.Y * transform.Scale.Y);
        Matrix4x4 quad = Matrix4x4.CreateTranslation(-(origin.X - 0.5f), -(origin.Y - 0.5f), 0f)
            * Matrix4x4.CreateScale(scaledSize.X, scaledSize.Y, 1f)
            * Matrix4x4.CreateRotationZ(GetBillboardRoll(transform.Rotation));
        (Vector3 right, Vector3 up, Vector3 front) = GetCameraFacingBasis();
        return quad * CreateBillboardOrientation(transform.Position, right, up, front);
    }

    /// <summary>
    /// Extracts the billboard's in-plane roll from a rotation: the twist about the local <c>+Z</c>
    /// plane normal.
    /// </summary>
    /// <remarks>
    /// The quaternion is decomposed into a swing and a twist about <c>+Z</c>. The twist angle is
    /// <c>2 * atan2(Z, W)</c>, which is the rotation that must be applied in the billboard's plane. A
    /// pure roll gives its angle, a pure pitch or yaw gives zero, and a combined rotation uses only its
    /// roll component. The result is wrapped to <c>(-pi, pi]</c>.
    /// </remarks>
    private static float GetBillboardRoll(Quaternion rotation) {
        float roll = 2f * MathF.Atan2(rotation.Z, rotation.W);
        if (roll > MathF.PI) {
            roll -= MathF.Tau;
        } else if (roll < -MathF.PI) {
            roll += MathF.Tau;
        }

        return roll;
    }

    /// <summary>
    /// Builds the row-vector quad orientation from the given right, up, and front axes and position.
    /// </summary>
    private static Matrix4x4 CreateBillboardOrientation(
        Vector3 position,
        Vector3 right,
        Vector3 up,
        Vector3 front) =>
        new(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            front.X, front.Y, front.Z, 0f,
            position.X, position.Y, position.Z, 1f);

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

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback) {
        float length = value.Length();
        return length > 0f && float.IsFinite(length) ? value / length : fallback;
    }

    /// <summary>
    /// Composes a rotation and translation that maps the local <c>+Y</c> axis to
    /// <paramref name="end"/> - <paramref name="start"/> and the local origin to
    /// <paramref name="start"/>.
    /// </summary>
    /// <remarks>
    /// The roll around the segment is deterministic: the local X axis is the normalized cross product
    /// of world up (or world X for a vertical segment) and the segment axis.
    /// </remarks>
    private static Matrix4x4 CreateSegmentMatrix(Vector3 start, Vector3 end) {
        Vector3 axisY = Vector3.Normalize(end - start);
        Vector3 helper = MathF.Abs(axisY.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 axisX = Vector3.Normalize(Vector3.Cross(helper, axisY));
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        return new Matrix4x4(
            axisX.X, axisX.Y, axisX.Z, 0f,
            axisY.X, axisY.Y, axisY.Z, 0f,
            axisZ.X, axisZ.Y, axisZ.Z, 0f,
            start.X, start.Y, start.Z, 1f);
    }

    /// <summary>
    /// Composes the reference-triangle map for one world-space triangle and then applies
    /// <paramref name="outer"/>.
    /// </summary>
    private static Matrix4x4 CreateTriangleMatrix(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Matrix4x4 outer) =>
        CreateTriangleMatrix(a, b, c, Vector3.Cross(b - a, c - a), Vector3.UnitZ) * outer;

    /// <summary>
    /// Records a tessellated unit sphere through <paramref name="outer"/> as one instanced triangle
    /// draw.
    /// </summary>
    /// <param name="outer">The unit-sphere-to-world matrix.</param>
    /// <param name="rings">The validated number of latitude bands; at least two.</param>
    /// <param name="slices">The validated number of longitude segments; at least three.</param>
    /// <param name="color">The linear sphere color.</param>
    private void DrawTessellatedSphere(Matrix4x4 outer, int rings, int slices, Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            2 * slices * (rings - 1));
        int matrixIndex = 0;
        float firstPhi = MathF.PI / rings;
        for (int slice = 0; slice < slices; slice++) {
            Vector3 start = GetSpherePoint(firstPhi, GetSliceAngle(slice, slices));
            Vector3 end = GetSpherePoint(firstPhi, GetSliceAngle(slice + 1, slices));
            matrices[matrixIndex++] = CreateTriangleMatrix(Vector3.UnitY, start, end, outer);
        }

        for (int ring = 1; ring < rings - 1; ring++) {
            float upperPhi = MathF.PI * ring / rings;
            float lowerPhi = MathF.PI * (ring + 1) / rings;
            for (int slice = 0; slice < slices; slice++) {
                float startAngle = GetSliceAngle(slice, slices);
                float endAngle = GetSliceAngle(slice + 1, slices);
                Vector3 upperStart = GetSpherePoint(upperPhi, startAngle);
                Vector3 upperEnd = GetSpherePoint(upperPhi, endAngle);
                Vector3 lowerStart = GetSpherePoint(lowerPhi, startAngle);
                Vector3 lowerEnd = GetSpherePoint(lowerPhi, endAngle);
                // The same winding as the engine sphere mesh: upper-left, lower-left, upper-right,
                // then upper-right, lower-left, lower-right.
                matrices[matrixIndex++] = CreateTriangleMatrix(upperStart, lowerStart, upperEnd, outer);
                matrices[matrixIndex++] = CreateTriangleMatrix(upperEnd, lowerStart, lowerEnd, outer);
            }
        }

        float lastPhi = MathF.PI * (rings - 1) / rings;
        for (int slice = 0; slice < slices; slice++) {
            Vector3 start = GetSpherePoint(lastPhi, GetSliceAngle(slice, slices));
            Vector3 end = GetSpherePoint(lastPhi, GetSliceAngle(slice + 1, slices));
            matrices[matrixIndex++] = CreateTriangleMatrix(start, -Vector3.UnitY, end, outer);
        }
    }

    /// <summary>
    /// Records a tessellated unit wire sphere through <paramref name="outer"/> as one instanced line
    /// draw.
    /// </summary>
    /// <param name="outer">The unit-sphere-to-world matrix.</param>
    /// <param name="rings">The validated number of latitude bands; at least two.</param>
    /// <param name="slices">The validated number of longitude segments; at least three.</param>
    /// <param name="color">The linear wire color.</param>
    private void DrawTessellatedWireSphere(Matrix4x4 outer, int rings, int slices, Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            slices * ((2 * rings) - 1));
        int matrixIndex = 0;
        for (int ring = 1; ring < rings; ring++) {
            float phi = MathF.PI * ring / rings;
            for (int slice = 0; slice < slices; slice++) {
                Vector3 start = GetSpherePoint(phi, GetSliceAngle(slice, slices));
                Vector3 end = GetSpherePoint(phi, GetSliceAngle(slice + 1, slices));
                matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
            }
        }

        for (int slice = 0; slice < slices; slice++) {
            float theta = GetSliceAngle(slice, slices);
            Vector3 start = Vector3.UnitY;
            for (int ring = 1; ring < rings; ring++) {
                Vector3 end = GetSpherePoint(MathF.PI * ring / rings, theta);
                matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                start = end;
            }

            matrices[matrixIndex++] = CreateLineMatrix(start, -Vector3.UnitY) * outer;
        }
    }

    /// <summary>
    /// Records a capped cylinder in local shape space through <paramref name="outer"/>.
    /// </summary>
    /// <remarks>
    /// Local space has its bottom cap at the origin and its axis along <c>+Y</c>. Equal positive radii
    /// use quad side instances and two cap triangle instances per side. Unequal positive radii use two
    /// side triangles plus one triangle per cap for each side because a tapered segment is not an
    /// affine image of the unit quad. A zero radius at one end uses one side triangle and one base-cap
    /// triangle per side; the degenerate cap at the apex is skipped. The caller validates the values,
    /// rejects two zero radii, and rejects a zero height.
    /// </remarks>
    private void DrawCylinderCore(
        Matrix4x4 outer,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Vector3 topCenter = new(0f, height, 0f);
        bool hasTop = radiusTop > 0f;
        bool hasBottom = radiusBottom > 0f;
        if (hasTop && hasBottom && radiusTop == radiusBottom) {
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
            for (int side = 0; side < sides; side++) {
                Vector3 bottomStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 bottomEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 topStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 topEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                sideMatrices[side] = CreateCylinderSideMatrix(bottomStart, bottomEnd, height) * outer;
                capMatrices[side * 2] = CreateTriangleMatrix(
                    Vector3.Zero,
                    bottomStart,
                    bottomEnd,
                    Vector3.Cross(bottomStart, bottomEnd),
                    -Vector3.UnitY) * outer;
                capMatrices[(side * 2) + 1] = CreateTriangleMatrix(
                    topCenter,
                    topEnd,
                    topStart,
                    Vector3.Cross(topEnd - topCenter, topStart - topCenter),
                    Vector3.UnitY) * outer;
            }
        } else if (hasTop && hasBottom) {
            // A tapered side segment is a trapezoid, not an affine image of the unit quad, so the
            // unequal-radius cylinder records two triangles per side segment instead of one quad.
            int sideTriangleCount = sides * 2;
            Span<Matrix4x4> triangleMatrices = BeginPrimitiveDraw(
                primitives.Triangle,
                material,
                material.DefaultRenderState,
                color,
                sides * 4);
            for (int side = 0; side < sides; side++) {
                Vector3 bottomStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 bottomEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 topStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 topEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                triangleMatrices[side * 2] = CreateTriangleMatrix(
                    bottomEnd,
                    bottomStart,
                    topStart,
                    outer);
                triangleMatrices[(side * 2) + 1] = CreateTriangleMatrix(
                    bottomEnd,
                    topStart,
                    topEnd,
                    outer);
                triangleMatrices[sideTriangleCount + side] = CreateTriangleMatrix(
                    Vector3.Zero,
                    bottomStart,
                    bottomEnd,
                    Vector3.Cross(bottomStart, bottomEnd),
                    -Vector3.UnitY) * outer;
                triangleMatrices[sideTriangleCount + sides + side] = CreateTriangleMatrix(
                    topCenter,
                    topEnd,
                    topStart,
                    Vector3.Cross(topEnd - topCenter, topStart - topCenter),
                    Vector3.UnitY) * outer;
            }
        } else {
            Span<Matrix4x4> triangleMatrices = BeginPrimitiveDraw(
                primitives.Triangle,
                material,
                material.DefaultRenderState,
                color,
                sides * 2);
            for (int side = 0; side < sides; side++) {
                Vector3 ringStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 ringEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 capStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 capEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                if (hasBottom) {
                    // Wound like the tapered side triangles: apex, then the ring points in the
                    // outward-triangle order, so the outside of the cone is the front face.
                    triangleMatrices[side] = CreateTriangleMatrix(
                        topCenter,
                        ringEnd,
                        ringStart,
                        outer);
                    triangleMatrices[sides + side] = CreateTriangleMatrix(
                        Vector3.Zero,
                        ringStart,
                        ringEnd,
                        Vector3.Cross(ringStart, ringEnd),
                        -Vector3.UnitY) * outer;
                } else {
                    // The inverted cone is the same side triangle with the apex at the bottom.
                    triangleMatrices[side] = CreateTriangleMatrix(
                        Vector3.Zero,
                        capStart,
                        capEnd,
                        outer);
                    triangleMatrices[sides + side] = CreateTriangleMatrix(
                        topCenter,
                        capEnd,
                        capStart,
                        Vector3.Cross(capEnd - topCenter, capStart - topCenter),
                        Vector3.UnitY) * outer;
                }
            }
        }
    }

    /// <summary>
    /// Records a wire cylinder in local shape space through <paramref name="outer"/> as one instanced
    /// line draw.
    /// </summary>
    /// <remarks>
    /// The draw has one vertical edge per side plus one closed ring per non-zero radius. The caller
    /// validates the values, rejects two zero radii, and rejects a zero height.
    /// </remarks>
    private void DrawWireCylinderCore(
        Matrix4x4 outer,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        bool hasTop = radiusTop > 0f;
        bool hasBottom = radiusBottom > 0f;
        int instanceCount = sides + (hasTop ? sides : 0) + (hasBottom ? sides : 0);
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            instanceCount);
        int matrixIndex = 0;
        for (int side = 0; side < sides; side++) {
            matrices[matrixIndex++] = CreateLineMatrix(
                GetCylinderPoint(radiusBottom, 0f, side, sides),
                GetCylinderPoint(radiusTop, height, side, sides)) * outer;
        }

        if (hasBottom) {
            for (int side = 0; side < sides; side++) {
                matrices[matrixIndex++] = CreateLineMatrix(
                    GetCylinderPoint(radiusBottom, 0f, side, sides),
                    GetCylinderPoint(radiusBottom, 0f, side + 1, sides)) * outer;
            }
        }

        if (hasTop) {
            for (int side = 0; side < sides; side++) {
                matrices[matrixIndex++] = CreateLineMatrix(
                    GetCylinderPoint(radiusTop, height, side, sides),
                    GetCylinderPoint(radiusTop, height, side + 1, sides)) * outer;
            }
        }
    }

    /// <summary>
    /// Records a solid capsule in local shape space through <paramref name="outer"/>.
    /// </summary>
    /// <remarks>
    /// Local space is centered at the origin with the cap centers at <c>(0, ±height/2, 0)</c>. The body
    /// is <c>slices</c> side quads and the two hemispherical caps are
    /// <c>2 * slices * (2 * rings - 1)</c> triangles. The caller validates the values and rejects a
    /// zero radius or height.
    /// </remarks>
    private void DrawCapsuleCore(
        Matrix4x4 outer,
        float radius,
        float height,
        int rings,
        int slices,
        Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> sideMatrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            material.DefaultRenderState,
            color,
            slices);
        int capTriangleCount = 2 * slices * ((2 * rings) - 1);
        Span<Matrix4x4> capMatrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            capTriangleCount);
        float halfHeight = height * 0.5f;
        for (int side = 0; side < slices; side++) {
            Vector3 bottomStart = GetCylinderPoint(radius, -halfHeight, side, slices);
            Vector3 bottomEnd = GetCylinderPoint(radius, -halfHeight, side + 1, slices);
            sideMatrices[side] = CreateCylinderSideMatrix(bottomStart, bottomEnd, height) * outer;
        }

        int matrixIndex = AddHemisphereTriangles(
            capMatrices,
            0,
            halfHeight,
            radius,
            rings,
            slices,
            top: true,
            outer);
        AddHemisphereTriangles(
            capMatrices,
            matrixIndex,
            -halfHeight,
            radius,
            rings,
            slices,
            top: false,
            outer);
    }

    /// <summary>
    /// Records a wire capsule in local shape space through <paramref name="outer"/> as one instanced
    /// line draw.
    /// </summary>
    /// <remarks>
    /// The draw has <c>slices * (4 * rings + 1)</c> line instances: one body edge per slice, one
    /// equator ring per end, and per hemispherical cap one meridian per slice with <c>rings</c>
    /// segments plus <c>rings - 1</c> interior latitude rings. The caller validates the values and
    /// rejects a zero radius or height.
    /// </remarks>
    private void DrawWireCapsuleCore(
        Matrix4x4 outer,
        float radius,
        float height,
        int rings,
        int slices,
        Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            slices * ((4 * rings) + 1));
        int matrixIndex = 0;
        float halfHeight = height * 0.5f;
        for (int slice = 0; slice < slices; slice++) {
            matrices[matrixIndex++] = CreateLineMatrix(
                GetCylinderPoint(radius, -halfHeight, slice, slices),
                GetCylinderPoint(radius, halfHeight, slice, slices)) * outer;
        }

        for (int slice = 0; slice < slices; slice++) {
            matrices[matrixIndex++] = CreateLineMatrix(
                GetCylinderPoint(radius, -halfHeight, slice, slices),
                GetCylinderPoint(radius, -halfHeight, slice + 1, slices)) * outer;
            matrices[matrixIndex++] = CreateLineMatrix(
                GetCylinderPoint(radius, halfHeight, slice, slices),
                GetCylinderPoint(radius, halfHeight, slice + 1, slices)) * outer;
        }

        for (int cap = 0; cap < 2; cap++) {
            bool top = cap == 0;
            float centerY = top ? halfHeight : -halfHeight;
            for (int slice = 0; slice < slices; slice++) {
                float theta = GetSliceAngle(slice, slices);
                Vector3 start = top ? new Vector3(0f, centerY + radius, 0f) : new Vector3(0f, centerY - radius, 0f);
                for (int ring = 1; ring <= rings; ring++) {
                    float phi = (MathF.PI * 0.5f) * ring / rings;
                    Vector3 end = GetHemispherePoint(centerY, radius, phi, theta, top);
                    matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                    start = end;
                }
            }

            for (int ring = 1; ring < rings; ring++) {
                float phi = (MathF.PI * 0.5f) * ring / rings;
                for (int slice = 0; slice < slices; slice++) {
                    Vector3 start = GetHemispherePoint(
                        centerY,
                        radius,
                        phi,
                        GetSliceAngle(slice, slices),
                        top);
                    Vector3 end = GetHemispherePoint(
                        centerY,
                        radius,
                        phi,
                        GetSliceAngle(slice + 1, slices),
                        top);
                    matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                }
            }
        }
    }

    /// <summary>
    /// Fill the cap triangles of one capsule hemisphere, starting at <paramref name="matrixIndex"/>.
    /// </summary>
    /// <remarks>
    /// The top hemisphere uses the same winding as the engine sphere mesh. The bottom hemisphere
    /// mirrors the top coordinates and reverses each triangle's winding, so both caps face outward.
    /// </remarks>
    private static int AddHemisphereTriangles(
        Span<Matrix4x4> matrices,
        int matrixIndex,
        float centerY,
        float radius,
        int rings,
        int slices,
        bool top,
        Matrix4x4 outer) {
        Vector3 pole = top
            ? new Vector3(0f, centerY + radius, 0f)
            : new Vector3(0f, centerY - radius, 0f);
        float firstPhi = (MathF.PI * 0.5f) / rings;
        for (int slice = 0; slice < slices; slice++) {
            Vector3 start = GetHemispherePoint(
                centerY,
                radius,
                firstPhi,
                GetSliceAngle(slice, slices),
                top);
            Vector3 end = GetHemispherePoint(
                centerY,
                radius,
                firstPhi,
                GetSliceAngle(slice + 1, slices),
                top);
            matrixIndex = AddCapsuleTriangle(matrices, matrixIndex, pole, start, end, top, outer);
        }

        for (int ring = 1; ring < rings; ring++) {
            float upperPhi = (MathF.PI * 0.5f) * ring / rings;
            float lowerPhi = (MathF.PI * 0.5f) * (ring + 1) / rings;
            for (int slice = 0; slice < slices; slice++) {
                float startAngle = GetSliceAngle(slice, slices);
                float endAngle = GetSliceAngle(slice + 1, slices);
                Vector3 upperStart = GetHemispherePoint(centerY, radius, upperPhi, startAngle, top);
                Vector3 upperEnd = GetHemispherePoint(centerY, radius, upperPhi, endAngle, top);
                Vector3 lowerStart = GetHemispherePoint(centerY, radius, lowerPhi, startAngle, top);
                Vector3 lowerEnd = GetHemispherePoint(centerY, radius, lowerPhi, endAngle, top);
                matrixIndex = AddCapsuleTriangle(
                    matrices,
                    matrixIndex,
                    upperStart,
                    lowerStart,
                    upperEnd,
                    top,
                    outer);
                matrixIndex = AddCapsuleTriangle(
                    matrices,
                    matrixIndex,
                    upperEnd,
                    lowerStart,
                    lowerEnd,
                    top,
                    outer);
            }
        }

        return matrixIndex;
    }

    private static int AddCapsuleTriangle(
        Span<Matrix4x4> matrices,
        int matrixIndex,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        bool top,
        Matrix4x4 outer) {
        matrices[matrixIndex] = top
            ? CreateTriangleMatrix(a, b, c, outer)
            : CreateTriangleMatrix(a, c, b, outer);
        return matrixIndex + 1;
    }

    /// <summary>
    /// Records a closed unit circle in the local <c>XZ</c> plane through <paramref name="outer"/> as
    /// one instanced line draw.
    /// </summary>
    private void DrawCircleCore(Matrix4x4 outer, Color color) {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            CIRCLE_SEGMENTS);
        for (int segment = 0; segment < CIRCLE_SEGMENTS; segment++) {
            matrices[segment] = CreateLineMatrix(
                GetCirclePoint(segment, CIRCLE_SEGMENTS),
                GetCirclePoint(segment + 1, CIRCLE_SEGMENTS)) * outer;
        }
    }

    private static Vector3 GetCirclePoint(int segment, int segments) {
        float angle = GetSliceAngle(segment, segments);
        return new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
    }

    private static Vector3 GetCylinderPoint(float radius, float y, int side, int sides) {
        float angle = GetSliceAngle(side, sides);
        return new Vector3(MathF.Cos(angle) * radius, y, MathF.Sin(angle) * radius);
    }

    private static Vector3 GetSpherePoint(float phi, float theta) =>
        new(MathF.Sin(phi) * MathF.Sin(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Cos(theta));

    private static Vector3 GetHemispherePoint(
        float centerY,
        float radius,
        float phi,
        float theta,
        bool top) {
        float ringRadius = radius * MathF.Sin(phi);
        float poleOffset = radius * MathF.Cos(phi);
        return new Vector3(
            ringRadius * MathF.Sin(theta),
            centerY + (top ? poleOffset : -poleOffset),
            ringRadius * MathF.Cos(theta));
    }

    private static float GetSliceAngle(int slice, int slices) => MathF.Tau * slice / slices;

    private static Vector3 GetStripCorner(ReadOnlySpan<Vector3> points, int triangle, int corner) {
        if ((triangle & 1) == 0) {
            return points[triangle + corner];
        }

        return corner switch {
            0 => points[triangle + 1],
            1 => points[triangle],
            _ => points[triangle + 2],
        };
    }

    private static void ValidateTransform(Transform transform) {
        ValidateFinite(transform.Position, nameof(transform));
        Quaternion rotation = transform.Rotation;
        if (!float.IsFinite(rotation.X)
            || !float.IsFinite(rotation.Y)
            || !float.IsFinite(rotation.Z)
            || !float.IsFinite(rotation.W)) {
            throw new ArgumentException("The transform rotation must be finite.", nameof(transform));
        }

        ValidateFinite(transform.Scale, nameof(transform));
        ValidateNonNegative(transform.Scale, nameof(transform));
    }

    private static bool HasZeroScale(Transform transform) =>
        transform.Scale.X == 0f || transform.Scale.Y == 0f || transform.Scale.Z == 0f;

    /// <summary>
    /// Validates a normalized billboard source rectangle: finite, with positive extents, inside the
    /// <c>[0, 1]</c> texture rectangle.
    /// </summary>
    /// <remarks>
    /// <see cref="Rect.X"/> and <see cref="Rect.Y"/> are the top-left corner and
    /// <see cref="Rect.Width"/> and <see cref="Rect.Height"/> are the extents. The bounds are exact
    /// comparisons, so <c>(0, 0, 1, 1)</c> and a rectangle that ends exactly on <c>1</c> are legal.
    /// </remarks>
    private static void ValidateBillboardSource(Rect source, string parameterName) {
        ValidateFinite(source.X, parameterName);
        ValidateFinite(source.Y, parameterName);
        ValidateFinite(source.Width, parameterName);
        ValidateFinite(source.Height, parameterName);
        if (!(source.Width > 0f) || !(source.Height > 0f)) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                source,
                "The source rectangle must have a positive width and height.");
        }

        if (source.X < 0f || source.Y < 0f || source.Right > 1f || source.Bottom > 1f) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                source,
                "The source rectangle must lie within the normalized [0, 1] texture rectangle.");
        }
    }

    private static void ValidateTessellation(int rings, int slices) {
        if (rings < 2) {
            throw new ArgumentOutOfRangeException(
                nameof(rings),
                rings,
                "The shape needs at least two rings.");
        }

        if (slices < 3) {
            throw new ArgumentOutOfRangeException(
                nameof(slices),
                slices,
                "The shape needs at least three slices.");
        }
    }

    private static void ValidateSides(int sides) {
        if (sides < 3) {
            throw new ArgumentOutOfRangeException(
                nameof(sides),
                sides,
                "A cylinder needs at least three sides.");
        }
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
