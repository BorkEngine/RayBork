using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using RayBork.Internal;

namespace RayBork;

/// <summary>A frame-scoped, tick-thread-only recorder for one camera pass in a frame.</summary>
/// <remarks>
/// Create it during <see cref="IGame.Tick"/> and dispose it before the callback returns: cameras
/// are sequential, a second active camera or an undisposed one throws
/// <see cref="InvalidOperationException"/>, and after the idempotent <see cref="Dispose"/> every
/// other public member throws <see cref="ObjectDisposedException"/> while <see cref="IsDisposed"/>
/// stays readable; a disposed asset argument also throws <see cref="ObjectDisposedException"/>.
/// Draws snapshot mesh, material, tint, and transform values and retain each mesh, program, and
/// bound texture in the frame (never a material object) until the render thread releases the frame
/// slot; matrices compose as <c>local * transform</c> under the row-vector convention. The camera
/// looks down local <c>-Z</c> in a right-handed Y-up world, uses a <c>[0, 1]</c> reverse-Z
/// projection with a baked-in Y-flip, requires unit scale, and writes the app's linear HDR scene
/// target unless created with a <see cref="RenderTexture"/> (a later camera may sample it, but a
/// camera that samples the target it writes is rejected).
/// </remarks>
public sealed class Camera : IDisposable
{
    private const string MESH_TEXTURE_BINDING = "mainTexture";
    private const int CIRCLE_SEGMENTS = 36;
    private const float DEGREES_TO_RADIANS = MathF.PI / 180f;
    private const float RAY_DRAW_DISTANCE = 1_000_000f;
    private const int MAX_STACK_INSTANCE_MATRICES = 64;

    private static readonly (int Start, int End)[] _boxEdges =
    [
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

    /// <summary>Text render state: alpha blending, no culling, reverse-Z, and no depth writes.</summary>
    private static readonly RenderState _textRenderState = new()
    {
        Blend = BlendMode.Alpha,
        Cull = CullMode.None,
        DepthTest = DepthTest.GreaterOrEqual,
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
    private readonly RenderTexture? _target;
    private readonly long _targetId;
    private bool _disposed;

    /// <summary>Initializes a camera for a pass already appended to the frame.</summary>
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
        Matrix4x4 viewProjectionMatrix,
        RenderTexture? target
    )
    {
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
        _target = target;
        _targetId = target?.TargetId ?? 0;
    }

    /// <summary>One-based camera number used to name the camera in leak diagnostics.</summary>
    internal int Number { get; }

    /// <summary>The world-space camera transform; the camera looks down its local <c>-Z</c>.</summary>
    public Transform Transform
    {
        get
        {
            ThrowIfDisposed();
            return _transform;
        }
    }

    /// <summary>The projection kind.</summary>
    public CameraProjection Projection
    {
        get
        {
            ThrowIfDisposed();
            return _projection;
        }
    }

    /// <summary>Vertical field of view in degrees for a perspective camera, or <c>0</c> otherwise.</summary>
    public float FieldOfView
    {
        get
        {
            ThrowIfDisposed();
            return _fieldOfView;
        }
    }

    /// <summary>Visible world-space height for an orthographic camera, or <c>0</c> otherwise.</summary>
    public float OrthographicHeight
    {
        get
        {
            ThrowIfDisposed();
            return _orthographicHeight;
        }
    }

    /// <summary>The near clip distance in world units.</summary>
    public float Near
    {
        get
        {
            ThrowIfDisposed();
            return _near;
        }
    }

    /// <summary>The far clip distance; a perspective camera may report positive infinity.</summary>
    public float Far
    {
        get
        {
            ThrowIfDisposed();
            return _far;
        }
    }

    /// <summary>The buffers this pass clears before it draws.</summary>
    public ClearMode ClearMode
    {
        get
        {
            ThrowIfDisposed();
            return _clearMode;
        }
    }

    /// <summary>The linear clear color; HDR components above <c>1</c> are allowed.</summary>
    public Color ClearColor
    {
        get
        {
            ThrowIfDisposed();
            return _clearColor;
        }
    }

    /// <summary>The render texture this pass writes, or <see langword="null"/> for the HDR target.</summary>
    public RenderTexture? Target
    {
        get
        {
            ThrowIfDisposed();
            return _target;
        }
    }

    /// <summary>The pixel viewport, clamped to the target at creation, with top-left origin.</summary>
    public Rect Viewport
    {
        get
        {
            ThrowIfDisposed();
            return _viewport;
        }
    }

    /// <summary>The world-to-view matrix.</summary>
    public Matrix4x4 ViewMatrix
    {
        get
        {
            ThrowIfDisposed();
            return _viewMatrix;
        }
    }

    /// <summary>The view-to-clip matrix; it uses <c>[0, 1]</c> reverse-Z and contains the Y-flip.</summary>
    public Matrix4x4 ProjectionMatrix
    {
        get
        {
            ThrowIfDisposed();
            return _projectionMatrix;
        }
    }

    /// <summary>The combined <c>ViewMatrix * ProjectionMatrix</c> matrix (world to clip).</summary>
    public Matrix4x4 ViewProjectionMatrix
    {
        get
        {
            ThrowIfDisposed();
            return _viewProjectionMatrix;
        }
    }

    /// <summary>The world-space frustum; plane names follow the engine's Y-flipped NDC.</summary>
    public BoundingFrustum Frustum
    {
        get
        {
            ThrowIfDisposed();
            return _frustum;
        }
    }

    /// <summary>Whether the camera was disposed; stays readable after disposal.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Records a model with per-part material, render state, and transform.</summary>
    /// <remarks>
    /// World is <c>part.LocalTransform.ToMatrix() * transform.ToMatrix()</c> under the row-vector
    /// convention; each part uses its own material snapshot and
    /// <see cref="ModelPart.RenderState"/>; an omitted tint is opaque white.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">A part material lacks a
    /// vertex or fragment stage.
    /// </exception>
    public void DrawModel(Model model, Transform transform, Color? tint = null)
    {
        EnsureDrawable(nameof(DrawModel));
        ArgumentNullException.ThrowIfNull(model);
        ThrowIfModelDisposed(model);

        IReadOnlyList<ModelPart> parts = model.Parts;
        ValidateParts(parts);

        Color effectiveTint = tint ?? Color.White;
        Matrix4x4 instanceMatrix = transform.ToMatrix();
        int partCount = parts.Count;
        for (int index = 0; index < partCount; index++)
        {
            ModelPart part = parts[index];
            RecordPart(
                part,
                part.RenderState,
                effectiveTint,
                part.LocalTransform.ToMatrix() * instanceMatrix,
                wireframe: false
            );
        }
    }

    /// <summary>Records a model as wireframe through the polygon-mode-line pipeline.</summary>
    /// <remarks>
    /// Materials, render state, and the world matrix follow <see cref="DrawModel"/>. Requires the
    /// device <c>fillModeNonSolid</c> feature; without it the draw reports
    /// <see cref="GraphicsException"/> (<c>RB3001</c>) instead of falling back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">A part material lacks a
    /// vertex or fragment stage.
    /// </exception>
    public void DrawWireModel(Model model, Transform transform, Color? tint = null)
    {
        EnsureDrawable(nameof(DrawWireModel));
        ArgumentNullException.ThrowIfNull(model);
        ThrowIfModelDisposed(model);

        IReadOnlyList<ModelPart> parts = model.Parts;
        ValidateParts(parts);

        Color effectiveTint = tint ?? Color.White;
        Matrix4x4 instanceMatrix = transform.ToMatrix();
        int partCount = parts.Count;
        for (int index = 0; index < partCount; index++)
        {
            ModelPart part = parts[index];
            RecordPart(
                part,
                part.RenderState,
                effectiveTint,
                part.LocalTransform.ToMatrix() * instanceMatrix,
                wireframe: true
            );
        }
    }

    /// <summary>Records one mesh with the shader cursor state captured at the call.</summary>
    /// <remarks>
    /// An omitted tint is opaque white; an omitted render state uses
    /// <see cref="Shader.DefaultRenderState"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The shader lacks a vertex or
    /// fragment stage.</exception>
    public void DrawMesh(
        Mesh mesh,
        Transform transform,
        Shader shader,
        RenderState? renderState = null,
        Color? tint = null
    )
    {
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
            wireframe: false
        );
        record.SetShaderState(shader.InstanceData, textures);
        record.BeginInstances(1)[0] = transform.ToMatrix();
    }

    /// <summary>Records one instanced mesh draw; an empty span records nothing.</summary>
    /// <remarks>
    /// An omitted tint is opaque white; an omitted render state uses
    /// <see cref="Shader.DefaultRenderState"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The shader lacks a vertex or
    /// fragment stage.</exception>
    public void DrawInstanced(
        Mesh mesh,
        Shader shader,
        ReadOnlySpan<Transform> transforms,
        RenderState? renderState = null,
        Color? tint = null
    )
    {
        EnsureDrawable(nameof(DrawInstanced));
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(shader);
        ValidateMesh(mesh);
        ValidateShader(shader);
        ReadOnlySpan<Texture?> textures = shader.TextureValues;
        ValidateTextures(textures);
        if (transforms.Length == 0)
        {
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
            wireframe: false
        );
        record.SetShaderState(shader.InstanceData, textures);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(transforms.Length);
        for (int index = 0; index < transforms.Length; index++)
        {
            worldMatrices[index] = transforms[index].ToMatrix();
        }
    }

    /// <summary>Records each model part instanced across every transform; empty spans draw nothing.</summary>
    /// <remarks>
    /// Every part uses its own material snapshot and <see cref="ModelPart.RenderState"/> for all
    /// instances, with <c>world = part.LocalTransform.ToMatrix() * transform.ToMatrix()</c>; an
    /// omitted tint is opaque white.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">A part material lacks a
    /// vertex or fragment stage.
    /// </exception>
    public void DrawModelInstanced(
        Model model,
        ReadOnlySpan<Transform> transforms,
        Color? tint = null
    )
    {
        EnsureDrawable(nameof(DrawModelInstanced));
        ArgumentNullException.ThrowIfNull(model);
        ThrowIfModelDisposed(model);

        IReadOnlyList<ModelPart> parts = model.Parts;
        ValidateParts(parts);
        if (transforms.Length == 0)
        {
            return;
        }

        Color effectiveTint = tint ?? Color.White;
        int instanceCount = transforms.Length;

        // Small instanced model draws stay on the stack; larger ones rent from the pool once per call
        // so the steady-state path records without a heap allocation.
        if (instanceCount <= MAX_STACK_INSTANCE_MATRICES)
        {
            Span<Matrix4x4> instanceMatrices = stackalloc Matrix4x4[instanceCount];
            RecordInstancedModelParts(parts, transforms, instanceMatrices, effectiveTint);
        }
        else
        {
            Matrix4x4[] rentedMatrices = ArrayPool<Matrix4x4>.Shared.Rent(instanceCount);
            try
            {
                RecordInstancedModelParts(
                    parts,
                    transforms,
                    rentedMatrices.AsSpan(0, instanceCount),
                    effectiveTint
                );
            }
            finally
            {
                ArrayPool<Matrix4x4>.Shared.Return(rentedMatrices);
            }
        }
    }

    private void RecordInstancedModelParts(
        IReadOnlyList<ModelPart> parts,
        ReadOnlySpan<Transform> transforms,
        Span<Matrix4x4> instanceMatrices,
        Color tint
    )
    {
        int instanceCount = transforms.Length;
        for (int index = 0; index < instanceCount; index++)
        {
            instanceMatrices[index] = transforms[index].ToMatrix();
        }

        int partCount = parts.Count;
        for (int partIndex = 0; partIndex < partCount; partIndex++)
        {
            ModelPart part = parts[partIndex];
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
                tint,
                wireframe: false
            );
            record.SetShaderState(material.InstanceData, textures);
            Span<Matrix4x4> worldMatrices = record.BeginInstances(instanceCount);
            Matrix4x4 localMatrix = part.LocalTransform.ToMatrix();
            for (int index = 0; index < instanceCount; index++)
            {
                worldMatrices[index] = localMatrix * instanceMatrices[index];
            }
        }
    }

    /// <summary>Records one world-space line segment, one pixel wide.</summary>
    /// <remarks>
    /// One instance of the engine line mesh; a zero-length segment records nothing and thick
    /// lines are not supported.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an endpoint is not finite.</exception>
    public void DrawLine(Vector3 start, Vector3 end, Color color)
    {
        EnsureDrawable(nameof(DrawLine));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        if (!((end - start).LengthSquared() > 0f))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateLineMatrix(start, end);
    }

    /// <summary>Records a world-space polyline as one instanced line draw.</summary>
    /// <remarks>
    /// Every consecutive point pair is one line instance, sharing interior vertices; coincident
    /// pairs and fewer than two points record nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a point is not finite.</exception>
    public void DrawLines(ReadOnlySpan<Vector3> points, Color color)
    {
        EnsureDrawable(nameof(DrawLines));
        for (int index = 0; index < points.Length; index++)
        {
            ValidateFinite(points[index], nameof(points));
        }

        if (points.Length < 2)
        {
            return;
        }

        int segmentCount = 0;
        for (int index = 1; index < points.Length; index++)
        {
            if ((points[index] - points[index - 1]).LengthSquared() > 0f)
            {
                segmentCount++;
            }
        }

        if (segmentCount == 0)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            segmentCount
        );
        int matrixIndex = 0;
        for (int index = 1; index < points.Length; index++)
        {
            Vector3 start = points[index - 1];
            Vector3 end = points[index];
            if ((end - start).LengthSquared() > 0f)
            {
                matrices[matrixIndex++] = CreateLineMatrix(start, end);
            }
        }
    }

    /// <summary>Records one camera-facing square of <paramref name="size"/> world units.</summary>
    /// <remarks>
    /// The point is an engine quad oriented with the camera basis, avoiding Vulkan point-size
    /// limits; the size is the quad width and height, one world unit by default,
    /// and zero records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="position"/> or
    /// <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/>
    /// is negative.</exception>
    public void DrawPoint(Vector3 position, Color color, float size = 1f)
    {
        EnsureDrawable(nameof(DrawPoint));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        (Vector3 right, Vector3 up, Vector3 front) = GetCameraFacingBasis();
        matrices[0] = CreateBillboardMatrix(position, size, size, right, up, front);
    }

    /// <summary>Records one world-space triangle.</summary>
    /// <remarks>
    /// The engine reference triangle is mapped affinely with its orientation preserved:
    /// counter-clockwise triangles as seen by the camera stay front-facing and clockwise ones are
    /// culled. A degenerate or collinear triangle records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a vertex is not finite.</exception>
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        EnsureDrawable(nameof(DrawTriangle));
        ValidateFinite(a, nameof(a));
        ValidateFinite(b, nameof(b));
        ValidateFinite(c, nameof(c));
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (!(normal.LengthSquared() > 0f))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateTriangleMatrix(a, b, c, normal, Vector3.UnitZ);
    }

    /// <summary>Records a world-space triangle strip as one instanced triangle draw.</summary>
    /// <remarks>
    /// Decomposes <c>points.Length - 2</c> triangles with the sliding-window winding of
    /// <see cref="PrimitiveTopology.TriangleStrip"/>; degenerate triangles and fewer than three
    /// points record nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of a point is not finite.</exception>
    public void DrawTriangleStrip(ReadOnlySpan<Vector3> points, Color color)
    {
        EnsureDrawable(nameof(DrawTriangleStrip));
        for (int index = 0; index < points.Length; index++)
        {
            ValidateFinite(points[index], nameof(points));
        }

        if (points.Length < 3)
        {
            return;
        }

        int triangleCount = 0;
        for (int triangle = 0; triangle + 2 < points.Length; triangle++)
        {
            Vector3 a = GetStripCorner(points, triangle, 0);
            Vector3 b = GetStripCorner(points, triangle, 1);
            Vector3 c = GetStripCorner(points, triangle, 2);
            if (Vector3.Cross(b - a, c - a).LengthSquared() > 0f)
            {
                triangleCount++;
            }
        }

        if (triangleCount == 0)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            triangleCount
        );
        int matrixIndex = 0;
        for (int triangle = 0; triangle + 2 < points.Length; triangle++)
        {
            Vector3 a = GetStripCorner(points, triangle, 0);
            Vector3 b = GetStripCorner(points, triangle, 1);
            Vector3 c = GetStripCorner(points, triangle, 2);
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() > 0f)
            {
                matrices[matrixIndex++] = CreateTriangleMatrix(a, b, c, normal, Vector3.UnitZ);
            }
        }
    }

    /// <summary>Records a ray as a long world-space line along its unit direction.</summary>
    /// <remarks>
    /// The infinite ray is approximated by a segment from <see cref="Ray.Origin"/> to
    /// <see cref="Ray.Origin"/> plus <see cref="Ray.Direction"/> times <c>1,000,000</c> world
    /// units, so every buffer coordinate stays finite. A <c>default(Ray)</c> with a zero
    /// direction records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of the origin or far endpoint is not finite.
    /// </exception>
    public void DrawRay(Ray ray, Color color)
    {
        EnsureDrawable(nameof(DrawRay));
        ValidateFinite(ray.Origin, nameof(ray));
        ValidateFinite(ray.Direction, nameof(ray));
        if (!(ray.Direction.LengthSquared() > 0f))
        {
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
            instanceCount: 1
        );
        matrices[0] = CreateLineMatrix(ray.Origin, end);
    }

    /// <summary>Records the twelve edges of a box as one instanced line draw.</summary>
    /// <remarks>
    /// Coincident endpoints record no instance, so a zero-extent box draws only its non-degenerate
    /// edges; an inverted box is drawn as given.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <see cref="BoundingBox.Min"/> or
    /// <see cref="BoundingBox.Max"/> is not finite.
    /// </exception>
    public void DrawBoundingBox(BoundingBox box, Color color)
    {
        EnsureDrawable(nameof(DrawBoundingBox));
        ValidateFinite(box.Min, nameof(box));
        ValidateFinite(box.Max, nameof(box));

        Span<Vector3> corners = stackalloc Vector3[8];
        for (int corner = 0; corner < corners.Length; corner++)
        {
            corners[corner] = new Vector3(
                (corner & 1) == 0 ? box.Min.X : box.Max.X,
                (corner & 2) == 0 ? box.Min.Y : box.Max.Y,
                (corner & 4) == 0 ? box.Min.Z : box.Max.Z
            );
        }

        int edgeCount = 0;
        foreach ((int start, int end) in _boxEdges)
        {
            if ((corners[end] - corners[start]).LengthSquared() > 0f)
            {
                edgeCount++;
            }
        }

        if (edgeCount == 0)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            edgeCount
        );
        int matrixIndex = 0;
        foreach ((int start, int end) in _boxEdges)
        {
            if ((corners[end] - corners[start]).LengthSquared() > 0f)
            {
                matrices[matrixIndex++] = CreateLineMatrix(corners[start], corners[end]);
            }
        }
    }

    /// <summary>Records one solid box at <paramref name="position"/> with the given size.</summary>
    /// <remarks>
    /// One instance of the engine cube mesh scaled by <paramref name="size"/>; a zero size
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of <paramref name="position"/> or
    /// <paramref name="size"/> is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A component of
    /// <paramref name="size"/> is negative.
    /// </exception>
    public void DrawCube(Vector3 position, Vector3 size, Color color)
    {
        EnsureDrawable(nameof(DrawCube));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Cube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateTranslationScaleMatrix(position, size);
    }

    /// <summary>Records one solid box with explicit width, height, and depth.</summary>
    /// <remarks>
    /// Scalar convenience form of <see cref="DrawCube(Vector3, Vector3, Color)"/>; dimensions map
    /// to <c>(X, Y, Z)</c> and a zero dimension records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A dimension or a position component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A dimension
    /// is negative.</exception>
    public void DrawCube(Vector3 position, float width, float height, float length, Color color)
    {
        EnsureDrawable(nameof(DrawCube));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));
        ValidateFinite(length, nameof(length));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(length, nameof(length));
        DrawCube(position, new Vector3(width, height, length), color);
    }

    /// <summary>Records one solid box at <paramref name="transform"/>, scaled by the transform.</summary>
    /// <remarks>
    /// The transform position is the box center and its rotation orients the box; a zero size or
    /// transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A transform or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    public void DrawCube(Transform transform, Vector3 size, Color color)
    {
        EnsureDrawable(nameof(DrawCube));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Cube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(size) * transform.ToMatrix();
    }

    /// <summary>Records one solid sphere scaled from the engine unit sphere.</summary>
    /// <remarks>
    /// The engine sphere uses fixed default tessellation and has no LOD; a zero
    /// radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A center component or the radius is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius
    /// is negative.</exception>
    public void DrawSphere(Vector3 center, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Sphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateTranslationScaleMatrix(center, new Vector3(radius));
    }

    /// <summary>Records one solid sphere transformed and scaled by <paramref name="transform"/>.</summary>
    /// <remarks>
    /// A non-uniform transform scale produces an ellipsoid; a zero radius or transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or a transform
    /// scale component is negative.
    /// </exception>
    public void DrawSphere(Transform transform, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Sphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(radius) * transform.ToMatrix();
    }

    /// <summary>Records one solid tessellated sphere.</summary>
    /// <remarks>
    /// The surface is composed directly with pole fans and a welded seam instead of the fixed
    /// engine mesh; a zero radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A center component or the radius is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius is negative,
    /// <paramref name="rings"/> is less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawSphere(Vector3 center, float radius, int rings, int slices, Color color)
    {
        EnsureDrawable(nameof(DrawSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f)
        {
            return;
        }

        DrawTessellatedSphere(
            Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center),
            rings,
            slices,
            color
        );
    }

    /// <summary>Records one solid tessellated sphere at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// A non-uniform transform scale produces an ellipsoid; a zero radius or transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or a transform
    /// scale component is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawSphere(Transform transform, float radius, int rings, int slices, Color color)
    {
        EnsureDrawable(nameof(DrawSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        DrawTessellatedSphere(
            Matrix4x4.CreateScale(radius) * transform.ToMatrix(),
            rings,
            slices,
            color
        );
    }

    /// <summary>Records a capped cylinder with 16 side segments.</summary>
    /// <remarks>
    /// Convenience form of the <c>sides</c> overload; a zero radius or height records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A position component, radius, or height is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or height
    /// is negative.</exception>
    public void DrawCylinder(Vector3 position, float radius, float height, Color color)
    {
        DrawCylinder(position, radius, height, color, sides: 16);
    }

    /// <summary>Records a capped cylinder with the given side count.</summary>
    /// <remarks>
    /// Two instanced draws: <paramref name="sides"/> quads for the wall and <c>2 * sides</c>
    /// triangles for the caps, wound outward; a zero radius or height records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A position component, radius, or height is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or height is
    /// negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawCylinder(Vector3 position, float radius, float height, Color color, int sides)
    {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (radius == 0f || height == 0f)
        {
            return;
        }

        DrawCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radius,
            radius,
            height,
            sides,
            color
        );
    }

    /// <summary>Records a capped cylinder or cone with unequal cap radii.</summary>
    /// <remarks>
    /// <paramref name="radiusBottom"/> is the cap at the position and <paramref name="radiusTop"/>
    /// the cap <paramref name="height"/> above; equal positive radii make a cylinder, unequal radii
    /// a tapered cylinder, and one zero radius a cone whose degenerate apex cap is skipped. A zero
    /// height or two zero radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A position, radius, or height component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A radius or the height is
    /// negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f))
        {
            return;
        }

        DrawCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radiusTop,
            radiusBottom,
            height,
            sides,
            color
        );
    }

    /// <summary>Records one capped cylinder between two axis endpoints.</summary>
    /// <remarks>
    /// The local <c>+Y</c> axis runs from <paramref name="start"/> to <paramref name="end"/>, with
    /// each cap taking its matching radius; the roll around the segment is deterministic. A
    /// zero-length segment or two zero radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">An endpoint or radius component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A radius is negative, or
    /// <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(startRadius, nameof(startRadius));
        ValidateFinite(endRadius, nameof(endRadius));
        ValidateNonNegative(startRadius, nameof(startRadius));
        ValidateNonNegative(endRadius, nameof(endRadius));
        ValidateSides(sides);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || (startRadius == 0f && endRadius == 0f))
        {
            return;
        }

        DrawCylinderCore(
            CreateSegmentMatrix(start, end),
            endRadius,
            startRadius,
            axis.Length(),
            sides,
            color
        );
    }

    /// <summary>Records a capped cylinder scaled by <paramref name="transform"/>.</summary>
    /// <remarks>
    /// The local axis is <c>+Y</c> with the bottom cap at the origin, so the transform position is
    /// the bottom-cap center; a zero height, two zero radii, or a zero transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A radius, height, or transform component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius, height, or transform scale component
    /// is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCylinder));
        ValidateTransform(transform);
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f) || HasZeroScale(transform))
        {
            return;
        }

        DrawCylinderCore(transform.ToMatrix(), radiusTop, radiusBottom, height, sides, color);
    }

    /// <summary>Records a wire cylinder with its base at a position and axis <c>+Y</c>.</summary>
    /// <remarks>
    /// One instanced line draw with <paramref name="sides"/> vertical edges plus one ring per
    /// non-zero radius; it contributes no triangles, and a zero height or two zero
    /// radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A position, radius, or height component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A radius or the height is
    /// negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawWireCylinder(
        Vector3 position,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateFinite(position, nameof(position));
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f))
        {
            return;
        }

        DrawWireCylinderCore(
            Matrix4x4.CreateTranslation(position),
            radiusTop,
            radiusBottom,
            height,
            sides,
            color
        );
    }

    /// <summary>Records a wire cylinder between two axis endpoints.</summary>
    /// <remarks>
    /// The local <c>+Y</c> axis runs from <paramref name="start"/> to <paramref name="end"/>;
    /// instance count follows the position overload, and a zero-length segment or two zero
    /// radii records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">An endpoint or radius component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A radius is negative, or
    /// <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawWireCylinder(
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(startRadius, nameof(startRadius));
        ValidateFinite(endRadius, nameof(endRadius));
        ValidateNonNegative(startRadius, nameof(startRadius));
        ValidateNonNegative(endRadius, nameof(endRadius));
        ValidateSides(sides);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || (startRadius == 0f && endRadius == 0f))
        {
            return;
        }

        DrawWireCylinderCore(
            CreateSegmentMatrix(start, end),
            endRadius,
            startRadius,
            axis.Length(),
            sides,
            color
        );
    }

    /// <summary>Records a wire cylinder scaled by <paramref name="transform"/>.</summary>
    /// <remarks>
    /// The bottom ring is at the local origin and the axis is local <c>+Y</c>; a zero height, two
    /// zero radii, or a zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A radius, height, or transform component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A radius, height, or transform scale component
    /// is negative, or <paramref name="sides"/> is less than three.
    /// </exception>
    public void DrawWireCylinder(
        Transform transform,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireCylinder));
        ValidateTransform(transform);
        ValidateFinite(radiusTop, nameof(radiusTop));
        ValidateFinite(radiusBottom, nameof(radiusBottom));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radiusTop, nameof(radiusTop));
        ValidateNonNegative(radiusBottom, nameof(radiusBottom));
        ValidateNonNegative(height, nameof(height));
        ValidateSides(sides);
        if (height == 0f || (radiusTop == 0f && radiusBottom == 0f) || HasZeroScale(transform))
        {
            return;
        }

        DrawWireCylinderCore(transform.ToMatrix(), radiusTop, radiusBottom, height, sides, color);
    }

    /// <summary>Records a horizontal plane centered at <paramref name="center"/>.</summary>
    /// <remarks>
    /// One instance of the engine plane mesh, which lies in <c>XZ</c> and faces world <c>+Y</c>; a
    /// zero size component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A center or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component is negative.</exception>
    public void DrawPlane(Vector3 center, Vector2 size, Color color)
    {
        EnsureDrawable(nameof(DrawPlane));
        ValidateFinite(center, nameof(center));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Plane,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] =
            Matrix4x4.CreateScale(size.X, 1f, size.Y) * Matrix4x4.CreateTranslation(center);
    }

    /// <summary>Records one plane at <paramref name="transform"/>, scaling the size by its scale.</summary>
    /// <remarks>
    /// The local surface is <c>XZ</c> facing local <c>+Y</c>, so the transform position is the
    /// plane center; a zero size or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A transform or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    public void DrawPlane(Transform transform, Vector2 size, Color color)
    {
        EnsureDrawable(nameof(DrawPlane));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Plane,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(size.X, 1f, size.Y) * transform.ToMatrix();
    }

    /// <summary>Records one solid quad in the transform's local <c>XY</c> plane.</summary>
    /// <remarks>
    /// The quad faces local <c>+Z</c>; an omitted render state is <see cref="RenderState.Opaque"/>,
    /// and the render state is part of the batch key, so blended and opaque quads never merge. A
    /// zero size or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A transform, size, or color component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "The approved M7 contract gives every DrawQuad overload optional tint/renderState parameters; the overloads stay unambiguous because the required Color and Texture arguments cannot convert to each other and the source form adds a required Rect."
    )]
    public void DrawQuad(
        Transform transform,
        Vector2 size,
        Color color,
        RenderState? renderState = null
    )
    {
        EnsureDrawable(nameof(DrawQuad));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateFinite(color, nameof(color));
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            renderState ?? RenderState.Opaque,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(size.X, size.Y, 1f) * transform.ToMatrix();
    }

    /// <summary>Records one textured quad in local XY, sampling the full texture.</summary>
    /// <remarks>
    /// One engine quad instance over the built-in <c>/shaders/mesh</c> material; the shared
    /// <c>mainTexture</c> binding exists only around the call. An omitted tint is opaque white, an
    /// omitted render state is <see cref="RenderState.Opaque"/>, and a zero size or transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform, size, or tint component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "The approved M7 contract gives every DrawQuad overload optional tint/renderState parameters; the overloads stay unambiguous because the required Color and Texture arguments cannot convert to each other and the source form adds a required Rect."
    )]
    public void DrawQuad(
        Transform transform,
        Vector2 size,
        Texture texture,
        Color? tint = null,
        RenderState? renderState = null
    )
    {
        DrawTexturedQuad(transform, size, texture, _fullTextureRect, tint, renderState);
    }

    /// <summary>Records one textured quad in local XY, sampling a sub-rectangle.</summary>
    /// <remarks>
    /// The source rectangle is the normalized top-left corner plus positive extents inside
    /// <c>[0, 1]</c>; <c>(0, 0, 1, 1)</c> samples the full texture and the rectangle never splits a
    /// batch. An omitted tint is opaque white, an omitted render state is
    /// <see cref="RenderState.Opaque"/>, and a zero size or transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform, size, source, or tint
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale component is
    /// negative, or the source rectangle is not positive and inside the
    /// normalized texture rectangle.
    /// </exception>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "The approved M7 contract gives every DrawQuad overload optional tint/renderState parameters; the overloads stay unambiguous because the required Color and Texture arguments cannot convert to each other and the source form adds a required Rect."
    )]
    public void DrawQuad(
        Transform transform,
        Vector2 size,
        Texture texture,
        Rect source,
        Color? tint = null,
        RenderState? renderState = null
    )
    {
        DrawTexturedQuad(transform, size, texture, source, tint, renderState);
    }

    /// <summary>Records a uniform XZ grid centered at the world origin.</summary>
    /// <remarks>
    /// <c>2 * (slices + 1)</c> line instances spanning <c>slices * spacing</c> units per axis;
    /// equivalent to the transform overload with <see cref="Transform.Identity"/> and
    /// contributes no triangles.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="spacing"/> is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slices"/> is less than one, or
    /// <paramref name="spacing"/> is not positive.
    /// </exception>
    public void DrawGrid(int slices, float spacing, Color color) =>
        DrawGrid(Transform.Identity, slices, spacing, color);

    /// <summary>Records a uniform XZ grid in the transform's local <c>XZ</c> plane.</summary>
    /// <remarks>
    /// <c>2 * (slices + 1)</c> line instances with local <c>+Y</c> as the normal; the transform
    /// scale multiplies both spacing and line extents. A zero transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="spacing"/> or a transform
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slices"/> is less than one,
    /// <paramref name="spacing"/> is not positive, or a transform scale component is negative.
    /// </exception>
    public void DrawGrid(Transform transform, int slices, float spacing, Color color)
    {
        EnsureDrawable(nameof(DrawGrid));
        ValidateTransform(transform);
        ValidateFinite(spacing, nameof(spacing));
        if (slices < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slices),
                slices,
                "A grid needs at least one cell per axis."
            );
        }

        ValidatePositive(spacing, nameof(spacing));
        if (HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            (slices + 1) * 2
        );
        Matrix4x4 transformMatrix = transform.ToMatrix();
        float halfExtent = slices * spacing * 0.5f;
        int matrixIndex = 0;
        for (int line = 0; line <= slices; line++)
        {
            float offset = (-halfExtent) + (line * spacing);
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    new Vector3(-halfExtent, 0f, offset),
                    new Vector3(halfExtent, 0f, offset)
                ) * transformMatrix;
        }

        for (int line = 0; line <= slices; line++)
        {
            float offset = (-halfExtent) + (line * spacing);
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    new Vector3(offset, 0f, -halfExtent),
                    new Vector3(offset, 0f, halfExtent)
                ) * transformMatrix;
        }
    }

    /// <summary>Records the twelve edges of a box.</summary>
    /// <remarks>
    /// One engine wire-cube mesh instance, so no polygon-mode feature is needed; a zero size
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A position or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component is negative.</exception>
    public void DrawWireCube(Vector3 position, Vector3 size, Color color)
    {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireCube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateTranslationScaleMatrix(position, size);
    }

    /// <summary>Records a box wireframe with explicit width, height, and depth.</summary>
    /// <remarks>
    /// Scalar convenience form of the size overload; dimensions map to <c>(X, Y, Z)</c> and a zero
    /// dimension records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A dimension or position component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A dimension
    /// is negative.</exception>
    public void DrawWireCube(Vector3 position, float width, float height, float length, Color color)
    {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateFinite(width, nameof(width));
        ValidateFinite(height, nameof(height));
        ValidateFinite(length, nameof(length));
        ValidateNonNegative(width, nameof(width));
        ValidateNonNegative(height, nameof(height));
        ValidateNonNegative(length, nameof(length));
        DrawWireCube(position, new Vector3(width, height, length), color);
    }

    /// <summary>Records a box wireframe at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// A zero size or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A transform or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    public void DrawWireCube(Transform transform, Vector3 size, Color color)
    {
        EnsureDrawable(nameof(DrawWireCube));
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        if (size.X == 0f || size.Y == 0f || size.Z == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireCube,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(size) * transform.ToMatrix();
    }

    /// <summary>Records three great circles of a wire sphere centered at <paramref name="center"/>.</summary>
    /// <remarks>
    /// One engine wire-sphere mesh instance; it contributes no triangles, and a zero
    /// radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A center component or the radius is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius
    /// is negative.</exception>
    public void DrawWireSphere(Vector3 center, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f)
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireSphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = CreateTranslationScaleMatrix(center, new Vector3(radius));
    }

    /// <summary>Records a wire sphere scaled by <paramref name="transform"/>.</summary>
    /// <remarks>
    /// A zero radius or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or a transform
    /// scale component is negative.
    /// </exception>
    public void DrawWireSphere(Transform transform, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.WireSphere,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(radius) * transform.ToMatrix();
    }

    /// <summary>Records a tessellated wire sphere.</summary>
    /// <remarks>
    /// Composed from <paramref name="slices"/> meridians and <c>rings - 1</c> latitude circles with
    /// shared poles and seam; a zero radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A center component or the radius is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius is negative,
    /// <paramref name="rings"/> is less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawWireSphere(Vector3 center, float radius, int rings, int slices, Color color)
    {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f)
        {
            return;
        }

        DrawTessellatedWireSphere(
            Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center),
            rings,
            slices,
            color
        );
    }

    /// <summary>Records a tessellated wire sphere at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// A zero radius or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or a transform
    /// scale component is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawWireSphere(
        Transform transform,
        float radius,
        int rings,
        int slices,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireSphere));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        DrawTessellatedWireSphere(
            Matrix4x4.CreateScale(radius) * transform.ToMatrix(),
            rings,
            slices,
            color
        );
    }

    /// <summary>Records one solid capsule between two spherical cap centers.</summary>
    /// <remarks>
    /// The cylinder body joins one hemisphere at each cap center, so total height is
    /// <c>|end - start| + 2 * radius</c>; a zero radius or zero-length segment records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">An endpoint or radius component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius is negative,
    /// <paramref name="rings"/> is less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCapsule));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || radius == 0f)
        {
            return;
        }

        float height = axis.Length();
        DrawCapsuleCore(
            CreateSegmentMatrix((start + end) * 0.5f, end),
            radius,
            height,
            rings,
            slices,
            color
        );
    }

    /// <summary>Records a solid capsule at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// Cap centers are at local <c>(0, ±height/2, 0)</c>, so total height is
    /// <c>(height + 2 * radius) * Scale.Y</c> along <see cref="Transform.Up"/>; a zero radius, zero
    /// height, or zero transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius, height, or a transform
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius, height, or a transform scale
    /// component is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCapsule));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateTessellation(rings, slices);
        if (radius == 0f || height == 0f || HasZeroScale(transform))
        {
            return;
        }

        DrawCapsuleCore(transform.ToMatrix(), radius, height, rings, slices, color);
    }

    /// <summary>Records the wire frame of a capsule between two spherical cap centers.</summary>
    /// <remarks>
    /// One instanced line draw with <c>slices * (4 * rings + 1)</c> segments; it contributes no
    /// triangles, and a zero radius or zero-length segment records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">An endpoint or radius component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius is negative,
    /// <paramref name="rings"/> is less than two, or <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawWireCapsule(
        Vector3 start,
        Vector3 end,
        float radius,
        int rings,
        int slices,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireCapsule));
        ValidateFinite(start, nameof(start));
        ValidateFinite(end, nameof(end));
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateTessellation(rings, slices);
        Vector3 axis = end - start;
        if (!(axis.LengthSquared() > 0f) || radius == 0f)
        {
            return;
        }

        float height = axis.Length();
        DrawWireCapsuleCore(
            CreateSegmentMatrix((start + end) * 0.5f, end),
            radius,
            height,
            rings,
            slices,
            color
        );
    }

    /// <summary>Records a capsule wireframe at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// Cap centers are at local <c>±height/2</c> on <c>Y</c>; a zero radius, zero height, or zero
    /// transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius, height, or a transform
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius, height, or a transform scale
    /// component is negative, <paramref name="rings"/> is less than two, or
    /// <paramref name="slices"/> is less than three.
    /// </exception>
    public void DrawWireCapsule(
        Transform transform,
        float radius,
        float height,
        int rings,
        int slices,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawWireCapsule));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(height, nameof(height));
        ValidateNonNegative(radius, nameof(radius));
        ValidateNonNegative(height, nameof(height));
        ValidateTessellation(rings, slices);
        if (radius == 0f || height == 0f || HasZeroScale(transform))
        {
            return;
        }

        DrawWireCapsuleCore(transform.ToMatrix(), radius, height, rings, slices, color);
    }

    /// <summary>Records a closed circle rotated by degrees around an axis.</summary>
    /// <remarks>
    /// The base circle lies in local <c>XZ</c> with local <c>+Y</c> as its normal, matching
    /// <see cref="DrawPlane(Vector3, Vector2, Color)"/>; a zero radius records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">A component of an argument is not finite, or
    /// <paramref name="rotationAxis"/> is zero.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius is negative.</exception>
    public void DrawCircle(
        Vector3 center,
        float radius,
        Vector3 rotationAxis,
        float rotationAngle,
        Color color
    )
    {
        EnsureDrawable(nameof(DrawCircle));
        ValidateFinite(center, nameof(center));
        ValidateFinite(radius, nameof(radius));
        ValidateFinite(rotationAxis, nameof(rotationAxis));
        ValidateFinite(rotationAngle, nameof(rotationAngle));
        ValidateNonNegative(radius, nameof(radius));
        float axisLengthSquared = rotationAxis.LengthSquared();
        if (!(axisLengthSquared > 0f))
        {
            throw new ArgumentException(
                "The rotation axis must not be zero length.",
                nameof(rotationAxis)
            );
        }

        if (radius == 0f)
        {
            return;
        }

        Matrix4x4 rotation = Matrix4x4.CreateFromAxisAngle(
            rotationAxis / MathF.Sqrt(axisLengthSquared),
            rotationAngle * DEGREES_TO_RADIANS
        );
        DrawCircleCore(
            Matrix4x4.CreateScale(radius) * rotation * Matrix4x4.CreateTranslation(center),
            color
        );
    }

    /// <summary>Records a closed circle at <paramref name="transform"/>.</summary>
    /// <remarks>
    /// Local <c>+Y</c> is the circle normal; non-uniform scale produces an ellipse, and a zero
    /// radius or transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius or a transform component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">The radius or a transform
    /// scale component is negative.
    /// </exception>
    public void DrawCircle(Transform transform, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawCircle));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        DrawCircleCore(Matrix4x4.CreateScale(radius) * transform.ToMatrix(), color);
    }

    /// <summary>Records one solid disc in the transform's local <c>XY</c> plane.</summary>
    /// <remarks>
    /// One engine unit-disc instance over <c>/shaders/primitive</c> with the opaque state; the
    /// surface faces local <c>+Z</c>, non-uniform scale produces an ellipse, and a zero radius or
    /// transform scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentException">The radius, a transform component, or a color
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The radius or a transform scale
    /// component is negative.
    /// </exception>
    public void DrawDisc(Transform transform, float radius, Color color)
    {
        EnsureDrawable(nameof(DrawDisc));
        ValidateTransform(transform);
        ValidateFinite(radius, nameof(radius));
        ValidateNonNegative(radius, nameof(radius));
        ValidateFinite(color, nameof(color));
        if (radius == 0f || HasZeroScale(transform))
        {
            return;
        }

        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Disc,
            material,
            material.DefaultRenderState,
            color,
            instanceCount: 1
        );
        matrices[0] = Matrix4x4.CreateScale(radius) * transform.ToMatrix();
    }

    /// <summary>Records a textured camera-facing billboard at a position.</summary>
    /// <remarks>
    /// Convenience form of the <c>faceCamera</c> overload with <c>faceCamera: true</c>, the full
    /// texture, and the center pivot; the billboard is one alpha-blended engine quad with no depth
    /// write. A zero size component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A position or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component is negative.</exception>
    public void DrawBillboard(Texture texture, Vector3 position, Vector2 size, Color color)
    {
        DrawBillboard(texture, position, size, color, faceCamera: true);
    }

    /// <summary>Records one textured billboard centered at <paramref name="position"/>.</summary>
    /// <remarks>
    /// Samples the full texture with the center pivot and is alpha-blended with no depth write;
    /// <c>faceCamera: false</c> keeps world up and applies only the camera yaw, and a zero size
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A position or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size component is negative.</exception>
    public void DrawBillboard(
        Texture texture,
        Vector3 position,
        Vector2 size,
        Color color,
        bool faceCamera
    )
    {
        DrawBillboardAtPosition(
            texture,
            position,
            _fullTextureRect,
            size,
            _centerOrigin,
            color,
            faceCamera
        );
    }

    /// <summary>Records one textured camera-facing billboard under <paramref name="transform"/>.</summary>
    /// <remarks>
    /// Convenience form with <c>faceCamera: true</c> and the full texture; see the
    /// <c>faceCamera</c> transform overload.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    public void DrawBillboard(Texture texture, Transform transform, Vector2 size, Color color)
    {
        DrawBillboard(texture, transform, size, color, faceCamera: true);
    }

    /// <summary>Records one textured billboard under <paramref name="transform"/>.</summary>
    /// <remarks>
    /// The transform position is the origin pivot, its scale multiplies the size, and its rotation
    /// is the in-plane roll when facing and orients the plane otherwise; <c>faceCamera: false</c>
    /// uses the rotation directly, so an identity transform gives a local-XY quad (unlike the
    /// position overloads' vertical billboard). Samples the full texture, is alpha-blended with no
    /// depth write, and records nothing for a zero size or transform scale component.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform or size component is not finite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale
    /// component is negative.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Vector2 size,
        Color color,
        bool faceCamera
    )
    {
        DrawBillboardWithTransform(
            texture,
            transform,
            _fullTextureRect,
            size,
            _centerOrigin,
            color,
            faceCamera
        );
    }

    /// <summary>Records one camera-facing billboard sampling a normalized source rectangle.</summary>
    /// <remarks>
    /// The source rectangle is the normalized top-left corner plus positive extents inside
    /// <c>[0, 1]</c>; this convenience form uses <c>faceCamera: true</c> and the center pivot, and
    /// a zero size component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A position, size, or source component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A size component is
    /// negative, or the source is not positive and inside the normalized rectangle.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Rect source,
        Vector3 position,
        Vector2 size,
        Color color
    )
    {
        DrawBillboard(texture, source, position, size, color, faceCamera: true);
    }

    /// <summary>Records one billboard sampling a normalized source rectangle.</summary>
    /// <remarks>
    /// The rectangle is a per-instance UV rectangle, so <c>(0, 0, 1, 1)</c> is the full texture and
    /// it never splits a batch; <c>faceCamera: false</c> keeps world up and applies only the camera
    /// yaw, and a zero size component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A position, size, or source component is not
    /// finite.</exception> <exception cref="ArgumentOutOfRangeException">A size component is
    /// negative, or the source is not positive and inside the normalized rectangle.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Rect source,
        Vector3 position,
        Vector2 size,
        Color color,
        bool faceCamera
    )
    {
        DrawBillboardAtPosition(texture, position, source, size, _centerOrigin, color, faceCamera);
    }

    /// <summary>Records a camera-facing billboard with explicit UVs and pivot.</summary>
    /// <remarks>
    /// The origin is the normalized pivot in the local quad: <c>(0, 0)</c> is the bottom-left
    /// corner, <c>(1, 1)</c> the top-right, and <c>(0.5, 0.5)</c> centers the quad on the transform
    /// position. This convenience form uses <c>faceCamera: true</c>, and a zero size or transform
    /// scale component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform, size, source, or origin
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale component is
    /// negative, or the source is not positive and inside the normalized rectangle.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color
    )
    {
        DrawBillboard(texture, transform, source, size, origin, color, faceCamera: true);
    }

    /// <summary>Records a billboard with explicit UVs and pivot under a transform.</summary>
    /// <remarks>
    /// The transform position is the origin pivot, its scale multiplies the size, and its rotation
    /// is the in-plane roll when facing and orients the plane otherwise; the origin is the
    /// normalized pivot in the local quad, where <c>(0, 0)</c> is the bottom-left corner and
    /// <c>(0.5, 0.5)</c> centers the quad. <c>faceCamera: false</c> uses the rotation directly, so
    /// an identity transform gives a local-XY quad, and a zero size or transform scale
    /// component records nothing.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="texture"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A transform, size, source, or origin
    /// component is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A size or transform scale component is
    /// negative, or the source is not positive and inside the normalized rectangle.
    /// </exception>
    public void DrawBillboard(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera
    )
    {
        DrawBillboardWithTransform(texture, transform, source, size, origin, color, faceCamera);
    }

    /// <summary>Records world-space MSDF text laid out in a transform's local <c>XY</c> plane.</summary>
    /// <remarks>
    /// Layout runs <c>+X</c> right, <c>+Y</c> up, and the readable face points toward local
    /// <c>+Z</c>; the layout origin is the top-left corner of the first line's ascent box and the
    /// em height is in world units. The max width is the alignment and wrapping box width in world
    /// units, or <c>0</c> for no limit, and word or character wrapping require a positive width;
    /// empty text records nothing, and glyph textures are generated asynchronously so an unready,
    /// failed, or empty glyph is skipped while the pen still advances and appears on a later frame.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is
    /// <see langword="null"/>.</exception> <exception cref="ArgumentException">A transform
    /// position, rotation, or scale component, or a color component, is not finite.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is not finite and positive, a
    /// transform scale component is negative, the alignment or wrapping value is undefined, or the
    /// max width is invalid for the wrapping mode.
    /// </exception>
    public void DrawText(
        Font font,
        ReadOnlySpan<char> text,
        Transform transform,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left,
        TextWrapping wrapping = TextWrapping.None,
        float maxWidth = 0f
    )
    {
        EnsureDrawable(nameof(DrawText));
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(font.IsDisposed, font);
        ValidateTransform(transform);
        ValidateFinite(color, nameof(color));
        TextLayoutBuffer layout = _service.TextLayout;
        TextLayoutEngine.Arrange(
            font.Metrics,
            text,
            size,
            alignment,
            wrapping,
            maxWidth,
            layout,
            out _
        );
        if (layout.Count == 0 || HasZeroScale(transform))
        {
            return;
        }

        RecordText(font, layout, color, transform.ToMatrix());
    }

    /// <summary>Records text camera-facing and centered on a position.</summary>
    /// <remarks>
    /// Same layout, asynchronous glyph behavior, and validation as <see cref="DrawText"/>, using
    /// <see cref="TextWrapping.None"/> and no width limit; the union of the layout box and glyph
    /// quads is centered on the position in the camera plane. There is no roll parameter.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="font"/> is
    /// <see langword="null"/>.</exception> <exception cref="ArgumentException">A position or color
    /// component is not finite.</exception> <exception cref="ArgumentOutOfRangeException">The size
    /// is not finite and positive, or the alignment value is undefined.
    /// </exception>
    public void DrawTextBillboard(
        Font font,
        ReadOnlySpan<char> text,
        Vector3 position,
        float size,
        Color color,
        TextAlignment alignment = TextAlignment.Left
    )
    {
        EnsureDrawable(nameof(DrawTextBillboard));
        ArgumentNullException.ThrowIfNull(font);
        ObjectDisposedException.ThrowIf(font.IsDisposed, font);
        ValidateFinite(position, nameof(position));
        ValidateFinite(color, nameof(color));
        TextLayoutBuffer layout = _service.TextLayout;
        TextLayoutEngine.Arrange(
            font.Metrics,
            text,
            size,
            alignment,
            TextWrapping.None,
            0f,
            layout,
            out TextLayoutResult result
        );
        if (layout.Count == 0)
        {
            return;
        }

        (Vector3 right, Vector3 up, Vector3 front) = GetCameraFacingBasis();
        Matrix4x4 block =
            TextDrawMath.CreateCenteredBlockOffset(result.BoundsMin, result.BoundsMax)
            * CreateBillboardOrientation(position, right, up, front);
        RecordText(font, layout, color, block);
    }

    /// <summary>Validates and records a billboard at a world point.</summary>
    /// <remarks>
    /// Validation runs before any retention or record mutation; <c>faceCamera: false</c> keeps the
    /// vertical billboard because there is no transform rotation.
    /// </remarks>
    private void DrawBillboardAtPosition(
        Texture texture,
        Vector3 position,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera
    )
    {
        EnsureDrawable(nameof(DrawBillboard));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateFinite(position, nameof(position));
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateBillboardSource(source, nameof(source));
        ValidateFinite(origin, nameof(origin));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f)
        {
            return;
        }

        Matrix4x4 matrix = faceCamera
            ? CreateFacingBillboardMatrix(new Transform(position), size, origin)
            : CreateVerticalBillboardMatrix(position, size, origin);
        RecordBillboard(texture, source, color, matrix);
    }

    /// <summary>Validates and records a transform-positioned billboard.</summary>
    private void DrawBillboardWithTransform(
        Texture texture,
        Transform transform,
        Rect source,
        Vector2 size,
        Vector2 origin,
        Color color,
        bool faceCamera
    )
    {
        EnsureDrawable(nameof(DrawBillboard));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateBillboardSource(source, nameof(source));
        ValidateFinite(origin, nameof(origin));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform))
        {
            return;
        }

        Matrix4x4 matrix = faceCamera
            ? CreateFacingBillboardMatrix(transform, size, origin)
            : CreateBillboardQuadMatrix(size, origin) * transform.ToMatrix();
        RecordBillboard(texture, source, color, matrix);
    }

    /// <summary>Validates and records one textured quad in a transform's local <c>XY</c> plane.</summary>
    private void DrawTexturedQuad(
        Transform transform,
        Vector2 size,
        Texture texture,
        Rect source,
        Color? tint,
        RenderState? renderState
    )
    {
        EnsureDrawable(nameof(DrawQuad));
        ArgumentNullException.ThrowIfNull(texture);
        ValidateTransform(transform);
        ValidateFinite(size, nameof(size));
        ValidateNonNegative(size, nameof(size));
        ValidateBillboardSource(source, nameof(source));
        Color effectiveTint = tint ?? Color.White;
        ValidateFinite(effectiveTint, nameof(tint));
        ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
        if (size.X == 0f || size.Y == 0f || HasZeroScale(transform))
        {
            return;
        }

        RecordTexturedQuad(
            texture,
            source,
            effectiveTint,
            renderState ?? RenderState.Opaque,
            Matrix4x4.CreateScale(size.X, size.Y, 1f) * transform.ToMatrix()
        );
    }

    /// <summary>Records one textured quad and clears the shared texture binding.</summary>
    /// <remarks>
    /// The caller has validated the arguments and composed the world matrix; the engine mesh
    /// material's binding exists only around this call.
    /// </remarks>
    private void RecordTexturedQuad(
        Texture texture,
        Rect source,
        Color tint,
        RenderState renderState,
        Matrix4x4 matrix
    )
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.MeshMaterial;
        Vector4 uvRect = new(source.X, source.Y, source.Width, source.Height);
        material.SetUniform(MESH_TEXTURE_BINDING, texture);
        try
        {
            Span<Matrix4x4> matrices = BeginPrimitiveDraw(
                primitives.Quad,
                material,
                renderState,
                tint,
                instanceCount: 1,
                uvRect
            );
            matrices[0] = matrix;
        }
        finally
        {
            material.SetUniform(MESH_TEXTURE_BINDING, null);
        }
    }

    /// <summary>Records one alpha-blended billboard instance.</summary>
    private void RecordBillboard(Texture texture, Rect source, Color color, Matrix4x4 matrix) =>
        RecordTexturedQuad(texture, source, color, RenderState.AlphaBlend, matrix);

    /// <summary>Records one MSDF draw for the ready glyphs.</summary>
    /// <remarks>
    /// The quad-to-world matrix is the text transform or the centered camera-facing block. The MSDF
    /// material must declare exactly one texture binding; each glyph texture is retained through
    /// <see cref="Font.AcquireGlyphTexture"/>, while the quad mesh and program are retained here.
    /// </remarks>
    private void RecordText(Font font, TextLayoutBuffer layout, Color color, Matrix4x4 quadToWorld)
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.MsdfMaterial;
        int textureBindingCount = material.Textures.Count;
        if (textureBindingCount != 1)
        {
            throw new InvalidOperationException(
                $"The engine MSDF material '{material.Path}' declares {textureBindingCount} texture "
                    + "bindings; a text draw records exactly one per-instance glyph texture, so the "
                    + "material and the text path disagree."
            );
        }

        ReadOnlySpan<TextGlyphInstance> glyphs = layout.Glyphs;
        Span<Matrix4x4> matrices = _service.ReserveTextGlyphs(glyphs.Length);
        Span<Texture?> textures = _service.TextGlyphTextures;
        try
        {
            _service.BeginTextFont(font);
            int readyGlyphCount = 0;
            for (int index = 0; index < glyphs.Length; index++)
            {
                TextGlyphInstance glyph = glyphs[index];
                if (!(glyph.Size.X > 0f) || !(glyph.Size.Y > 0f))
                {
                    continue;
                }

                Texture? texture = font.AcquireGlyphTexture(glyph.GlyphId, _packet.Retention);
                if (texture is null)
                {
                    continue;
                }

                matrices[readyGlyphCount] =
                    TextDrawMath.CreateGlyphQuad(glyph.Position, glyph.Size) * quadToWorld;
                textures[readyGlyphCount] = texture;
                readyGlyphCount++;
            }

            if (readyGlyphCount == 0)
            {
                return;
            }

            _packet.Retention.Add(primitives.Quad.Lifetime);
            _packet.Retention.Add(material.Program.Lifetime);
            RenderDrawRecord record = BeginDraw(
                primitives.Quad.Lifetime,
                primitives.Quad.Topology,
                primitives.Quad.Bounds,
                primitives.Quad.IndexCount,
                material.Program.Lifetime,
                _textRenderState,
                color,
                wireframe: false
            );
            record.SetPerInstanceShaderState(material);
            matrices[..readyGlyphCount].CopyTo(record.BeginInstances(readyGlyphCount));
            // The binding-count guard above keeps the flat per-instance buffer instance-major: one
            // glyph texture per instance.
            textures[..readyGlyphCount].CopyTo(record.BeginInstanceTextureBindings());
        }
        finally
        {
            _service.ClearTextGlyphs();
        }
    }

    /// <summary>Converts a top-left render-pixel coordinate into a world-space ray.</summary>
    /// <param name="screenPosition">Target-space render pixels, not viewport-relative: add the
    /// camera <see cref="Viewport"/> origin before passing a viewport-relative position.
    /// </param>
    /// <returns>
    /// A normalized world-space ray from the camera position, or from the near-plane point for
    /// an orthographic camera.
    /// </returns>
    /// <exception cref="ArgumentException">The position has a non-finite component.</exception>
    public Ray ScreenToWorldRay(Vector2 screenPosition)
    {
        ThrowIfDisposed();
        ValidateFinite(screenPosition, nameof(screenPosition));

        float ndcX = ((screenPosition.X - _viewport.X) / _viewport.Width * 2f) - 1f;
        float ndcY = ((screenPosition.Y - _viewport.Y) / _viewport.Height * 2f) - 1f;
        if (!Matrix4x4.Invert(_viewProjectionMatrix, out Matrix4x4 inverse))
        {
            throw new InvalidOperationException(
                "The camera view-projection matrix is singular; a world ray cannot be unprojected."
            );
        }

        Vector4 nearClip = new(ndcX, ndcY, 1f, 1f);
        Vector4 nearWorld = Vector4.Transform(nearClip, inverse);
        Vector3 nearPoint = new(
            nearWorld.X / nearWorld.W,
            nearWorld.Y / nearWorld.W,
            nearWorld.Z / nearWorld.W
        );

        if (_projection == CameraProjection.Orthographic)
        {
            Vector3 orthographicDirection = _transform.Forward;
            float lengthSquared = orthographicDirection.LengthSquared();
            orthographicDirection =
                lengthSquared > 0f
                    ? orthographicDirection / MathF.Sqrt(lengthSquared)
                    : -Vector3.UnitZ;
            return new Ray(nearPoint, orthographicDirection);
        }

        Vector3 origin = _transform.Position;
        Vector3 direction = Vector3.Normalize(nearPoint - origin);
        return new Ray(origin, direction);
    }

    /// <summary>Projects a world-space position to target-space pixels (top-left origin).</summary>
    /// <returns>
    /// The viewport render-pixel position, or <c>(NaN, NaN)</c> when the point is behind the
    /// camera ( <c>w &lt;= 0</c>).
    /// </returns>
    /// <exception cref="ArgumentException">The position has a non-finite component.</exception>
    public Vector2 WorldToScreen(Vector3 worldPosition)
    {
        ThrowIfDisposed();
        ValidateFinite(worldPosition, nameof(worldPosition));

        Vector4 clip = Vector4.Transform(new Vector4(worldPosition, 1f), _viewProjectionMatrix);
        if (clip.W <= 0f)
        {
            return new Vector2(float.NaN, float.NaN);
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        return new Vector2(
            _viewport.X + ((ndcX * 0.5f) + 0.5f) * _viewport.Width,
            _viewport.Y + ((ndcY * 0.5f) + 0.5f) * _viewport.Height
        );
    }

    /// <summary>Maps a viewport point to a world transform on a forward-distance plane.</summary>
    /// <param name="point">Viewport-relative render pixels with a top-left origin; subtract the
    /// camera <see cref="Viewport"/> origin to convert a target-space position.
    /// </param>
    /// <param name="distance">The plane distance along the forward axis in world
    /// units; finite and positive.
    /// </param>
    /// <returns>
    /// A transform at the world point on that plane, with the camera rotation and a uniform scale
    /// in world units per render pixel.
    /// </returns>
    /// <remarks>
    /// The local <c>XY</c> plane is screen-aligned with local <c>+Z</c> toward the camera, so a
    /// sprite or text quad reads upright. The scale is
    /// <c>2 * distance * tan(fov / 2) / viewport.Height</c> for perspective and
    /// <c>orthographicHeight / viewport.Height</c> for orthographic.
    /// </remarks>
    /// <exception cref="ArgumentException">The point has a non-finite component.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The distance is not finite or
    /// not greater than zero.
    /// </exception>
    public Transform ScreenToWorldTransform(Vector2 point, float distance)
    {
        ThrowIfDisposed();
        ValidateFinite(point, nameof(point));
        if (!float.IsFinite(distance) || distance <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                distance,
                "The plane distance must be finite and greater than zero."
            );
        }

        float ndcX = (point.X / _viewport.Width * 2f) - 1f;
        float ndcY = (point.Y / _viewport.Height * 2f) - 1f;
        float aspectRatio = _viewport.Width / _viewport.Height;
        float halfHeight;
        float unitsPerPixel;
        if (_projection == CameraProjection.Perspective)
        {
            float tangent = MathF.Tan(_fieldOfView * DEGREES_TO_RADIANS * 0.5f);
            halfHeight = distance * tangent;
            unitsPerPixel = 2f * distance * tangent / _viewport.Height;
        }
        else
        {
            halfHeight = _orthographicHeight * 0.5f;
            unitsPerPixel = _orthographicHeight / _viewport.Height;
        }

        float halfWidth = halfHeight * aspectRatio;
        Vector3 viewPosition = new(ndcX * halfWidth, -ndcY * halfHeight, -distance);
        Vector3 worldPosition = Vector3.Transform(viewPosition, _transform.ToMatrix());
        return new Transform(worldPosition, _transform.Rotation, new Vector3(unitsPerPixel));
    }

    /// <summary>Seals the pass and releases the camera; idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        TickThread.RequireCurrent(nameof(Dispose));
        if (_disposed)
        {
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
        bool wireframe
    )
    {
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
        bool wireframe
    )
    {
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
            wireframe
        );
        record.SetShaderState(material.InstanceData, textures);
        record.BeginInstances(1)[0] = worldMatrix;
    }

    private void RetainShader(Shader shader, ReadOnlySpan<Texture?> textures)
    {
        _packet.Retention.Add(shader.Program.Lifetime);
        foreach (Texture? texture in textures)
        {
            if (texture is not null)
            {
                if (texture.IsRenderTargetView && IsSamePassRenderTargetView(texture))
                {
                    throw new InvalidOperationException(
                        $"Camera {Number} writes a render texture and samples that target's color or depth "
                            + "view in the same pass. A render texture can be sampled only from a later camera "
                            + "pass in the same tick; the frame was not recorded."
                    );
                }

                _packet.Retention.Add(texture.RetentionTarget);
            }
        }
    }

    /// <summary>Whether a render-target view belongs to the render texture this camera writes.</summary>
    private bool IsSamePassRenderTargetView(Texture texture) =>
        _targetId != 0 && texture.RenderTargetId == _targetId;

    private PrimitiveAssets RequirePrimitives()
    {
        PrimitiveAssets? primitives = _service.Primitives;
        if (primitives is null)
        {
            throw new InvalidOperationException(
                "The camera's primitive draw helpers need the app's PrimitiveAssets, but the "
                    + "CameraService has none attached. The app attaches them before IGame.Setup runs; a "
                    + "directly constructed CameraService in a test must call AttachPrimitives first."
            );
        }

        return primitives;
    }

    /// <summary>Retains assets, records one draw, and returns the world-matrix slot.</summary>
    private Span<Matrix4x4> BeginPrimitiveDraw(
        Mesh mesh,
        Shader material,
        RenderState renderState,
        Color tint,
        int instanceCount
    ) =>
        BeginPrimitiveDraw(
            mesh,
            material,
            renderState,
            tint,
            instanceCount,
            new Vector4(0f, 0f, 1f, 1f)
        );

    /// <summary>Retains assets and records one draw with a per-instance UV rectangle.</summary>
    /// <remarks>
    /// Validation runs before any retention or record mutation, so a rejected call leaves the frame
    /// packet untouched; the UV rectangle never affects batching.
    /// </remarks>
    private Span<Matrix4x4> BeginPrimitiveDraw(
        Mesh mesh,
        Shader material,
        RenderState renderState,
        Color tint,
        int instanceCount,
        Vector4 uvRect
    )
    {
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
            wireframe: false
        );
        record.SetShaderState(material.InstanceData, textures);
        record.SetUvRect(uvRect);
        return record.BeginInstances(instanceCount);
    }

    /// <summary>Builds the right, up, and front axes for a quad facing the camera's view direction.</summary>
    private (Vector3 Right, Vector3 Up, Vector3 Front) GetCameraFacingBasis()
    {
        Vector3 front = NormalizeOr(-_transform.Forward, Vector3.UnitZ);
        Vector3 upHint = NormalizeOr(_transform.Up, Vector3.UnitY);
        Vector3 right = NormalizeOr(Vector3.Cross(upHint, front), Vector3.UnitX);
        return (right, Vector3.Cross(front, right), front);
    }

    /// <summary>Builds the axes for a vertical billboard: world up plus the camera yaw.</summary>
    private (Vector3 Right, Vector3 Up, Vector3 Front) GetVerticalBillboardBasis()
    {
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
        Vector3 front
    )
    {
        Matrix4x4 orientation = CreateBillboardOrientation(position, right, up, front);
        return Matrix4x4.CreateScale(width, height, 1f) * orientation;
    }

    /// <summary>Maps the unit quad to the normalized origin pivot and size.</summary>
    private static Matrix4x4 CreateBillboardQuadMatrix(Vector2 size, Vector2 origin) =>
        Matrix4x4.CreateTranslation(-(origin.X - 0.5f), -(origin.Y - 0.5f), 0f)
        * Matrix4x4.CreateScale(size.X, size.Y, 1f);

    /// <summary>Builds the vertical billboard matrix from a position, size, and pivot.</summary>
    private Matrix4x4 CreateVerticalBillboardMatrix(Vector3 position, Vector2 size, Vector2 origin)
    {
        (Vector3 right, Vector3 up, Vector3 front) = GetVerticalBillboardBasis();
        return CreateBillboardQuadMatrix(size, origin)
            * CreateBillboardOrientation(position, right, up, front);
    }

    /// <summary>Builds the camera-facing billboard matrix from a transform and roll.</summary>
    private Matrix4x4 CreateFacingBillboardMatrix(Transform transform, Vector2 size, Vector2 origin)
    {
        Vector2 scaledSize = new(size.X * transform.Scale.X, size.Y * transform.Scale.Y);
        Matrix4x4 quad =
            Matrix4x4.CreateTranslation(-(origin.X - 0.5f), -(origin.Y - 0.5f), 0f)
            * Matrix4x4.CreateScale(scaledSize.X, scaledSize.Y, 1f)
            * Matrix4x4.CreateRotationZ(GetBillboardRoll(transform.Rotation));
        (Vector3 right, Vector3 up, Vector3 front) = GetCameraFacingBasis();
        return quad * CreateBillboardOrientation(transform.Position, right, up, front);
    }

    /// <summary>Extracts the billboard's in-plane roll: the twist about the local <c>+Z</c> normal.</summary>
    /// <remarks>
    /// A pure pitch or yaw gives zero; the result is wrapped to <c>(-pi, pi]</c>.
    /// </remarks>
    private static float GetBillboardRoll(Quaternion rotation)
    {
        float roll = 2f * MathF.Atan2(rotation.Z, rotation.W);
        if (roll > MathF.PI)
        {
            roll -= MathF.Tau;
        }
        else if (roll < -MathF.PI)
        {
            roll += MathF.Tau;
        }

        return roll;
    }

    /// <summary>Builds the row-vector quad orientation from the right, up, front, and position.</summary>
    private static Matrix4x4 CreateBillboardOrientation(
        Vector3 position,
        Vector3 right,
        Vector3 up,
        Vector3 front
    ) =>
        new(
            right.X,
            right.Y,
            right.Z,
            0f,
            up.X,
            up.Y,
            up.Z,
            0f,
            front.X,
            front.Y,
            front.Z,
            0f,
            position.X,
            position.Y,
            position.Z,
            1f
        );

    private static Matrix4x4 CreateTranslationScaleMatrix(Vector3 position, Vector3 scale) =>
        Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);

    private static Matrix4x4 CreateLineMatrix(Vector3 start, Vector3 end)
    {
        Vector3 direction = end - start;
        Vector3 axisX = Vector3.Normalize(direction);
        Vector3 helper = MathF.Abs(axisX.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 axisY = Vector3.Normalize(helper - (axisX * Vector3.Dot(helper, axisX)));
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        return new Matrix4x4(
            direction.X,
            direction.Y,
            direction.Z,
            0f,
            axisY.X,
            axisY.Y,
            axisY.Z,
            0f,
            axisZ.X,
            axisZ.Y,
            axisZ.Z,
            0f,
            start.X,
            start.Y,
            start.Z,
            1f
        );
    }

    private static Matrix4x4 CreateTriangleMatrix(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 normal,
        Vector3 fallbackNormal
    )
    {
        Vector3 axisZ = NormalizeOr(normal, fallbackNormal);
        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        return new Matrix4x4(
            edge1.X,
            edge1.Y,
            edge1.Z,
            0f,
            edge2.X,
            edge2.Y,
            edge2.Z,
            0f,
            axisZ.X,
            axisZ.Y,
            axisZ.Z,
            0f,
            a.X,
            a.Y,
            a.Z,
            1f
        );
    }

    private static Matrix4x4 CreateCylinderSideMatrix(Vector3 start, Vector3 end, float height)
    {
        Vector3 chord = end - start;
        float width = chord.Length();
        // The local X axis runs from the next ring point back to this one so the quad's +Z front
        // faces away from the cylinder axis.
        Vector3 axisX = -chord / width;
        Vector3 axisY = Vector3.UnitY;
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        Vector3 center = ((start + end) * 0.5f) + (Vector3.UnitY * (height * 0.5f));
        return new Matrix4x4(
            axisX.X * width,
            axisX.Y * width,
            axisX.Z * width,
            0f,
            axisY.X * height,
            axisY.Y * height,
            axisY.Z * height,
            0f,
            axisZ.X,
            axisZ.Y,
            axisZ.Z,
            0f,
            center.X,
            center.Y,
            center.Z,
            1f
        );
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
    {
        float length = value.Length();
        return length > 0f && float.IsFinite(length) ? value / length : fallback;
    }

    /// <summary>Maps local <c>+Y</c> to <c>end - start</c> and the origin to <c>start</c>.</summary>
    /// <remarks>
    /// The roll around the segment is deterministic: local X is the cross product of world up
    /// (world X for a vertical segment) and the segment axis.
    /// </remarks>
    private static Matrix4x4 CreateSegmentMatrix(Vector3 start, Vector3 end)
    {
        Vector3 axisY = Vector3.Normalize(end - start);
        Vector3 helper = MathF.Abs(axisY.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 axisX = Vector3.Normalize(Vector3.Cross(helper, axisY));
        Vector3 axisZ = Vector3.Cross(axisX, axisY);
        return new Matrix4x4(
            axisX.X,
            axisX.Y,
            axisX.Z,
            0f,
            axisY.X,
            axisY.Y,
            axisY.Z,
            0f,
            axisZ.X,
            axisZ.Y,
            axisZ.Z,
            0f,
            start.X,
            start.Y,
            start.Z,
            1f
        );
    }

    /// <summary>Composes the reference-triangle map and applies <paramref name="outer"/>.</summary>
    private static Matrix4x4 CreateTriangleMatrix(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Matrix4x4 outer
    ) => CreateTriangleMatrix(a, b, c, Vector3.Cross(b - a, c - a), Vector3.UnitZ) * outer;

    /// <summary>Records a tessellated unit sphere as one instanced triangle draw.</summary>
    private void DrawTessellatedSphere(Matrix4x4 outer, int rings, int slices, Color color)
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            2 * slices * (rings - 1)
        );
        int matrixIndex = 0;
        float firstPhi = MathF.PI / rings;
        for (int slice = 0; slice < slices; slice++)
        {
            Vector3 start = GetSpherePoint(firstPhi, GetSliceAngle(slice, slices));
            Vector3 end = GetSpherePoint(firstPhi, GetSliceAngle(slice + 1, slices));
            matrices[matrixIndex++] = CreateTriangleMatrix(Vector3.UnitY, start, end, outer);
        }

        for (int ring = 1; ring < rings - 1; ring++)
        {
            float upperPhi = MathF.PI * ring / rings;
            float lowerPhi = MathF.PI * (ring + 1) / rings;
            for (int slice = 0; slice < slices; slice++)
            {
                float startAngle = GetSliceAngle(slice, slices);
                float endAngle = GetSliceAngle(slice + 1, slices);
                Vector3 upperStart = GetSpherePoint(upperPhi, startAngle);
                Vector3 upperEnd = GetSpherePoint(upperPhi, endAngle);
                Vector3 lowerStart = GetSpherePoint(lowerPhi, startAngle);
                Vector3 lowerEnd = GetSpherePoint(lowerPhi, endAngle);
                // The same winding as the engine sphere mesh: upper-left, lower-left, upper-right,
                // then upper-right, lower-left, lower-right.
                matrices[matrixIndex++] = CreateTriangleMatrix(
                    upperStart,
                    lowerStart,
                    upperEnd,
                    outer
                );
                matrices[matrixIndex++] = CreateTriangleMatrix(
                    upperEnd,
                    lowerStart,
                    lowerEnd,
                    outer
                );
            }
        }

        float lastPhi = MathF.PI * (rings - 1) / rings;
        for (int slice = 0; slice < slices; slice++)
        {
            Vector3 start = GetSpherePoint(lastPhi, GetSliceAngle(slice, slices));
            Vector3 end = GetSpherePoint(lastPhi, GetSliceAngle(slice + 1, slices));
            matrices[matrixIndex++] = CreateTriangleMatrix(start, -Vector3.UnitY, end, outer);
        }
    }

    /// <summary>Records a tessellated unit wire sphere as one instanced line draw.</summary>
    private void DrawTessellatedWireSphere(Matrix4x4 outer, int rings, int slices, Color color)
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            slices * ((2 * rings) - 1)
        );
        int matrixIndex = 0;
        for (int ring = 1; ring < rings; ring++)
        {
            float phi = MathF.PI * ring / rings;
            for (int slice = 0; slice < slices; slice++)
            {
                Vector3 start = GetSpherePoint(phi, GetSliceAngle(slice, slices));
                Vector3 end = GetSpherePoint(phi, GetSliceAngle(slice + 1, slices));
                matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
            }
        }

        for (int slice = 0; slice < slices; slice++)
        {
            float theta = GetSliceAngle(slice, slices);
            Vector3 start = Vector3.UnitY;
            for (int ring = 1; ring < rings; ring++)
            {
                Vector3 end = GetSpherePoint(MathF.PI * ring / rings, theta);
                matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                start = end;
            }

            matrices[matrixIndex++] = CreateLineMatrix(start, -Vector3.UnitY) * outer;
        }
    }

    /// <summary>Records a capped cylinder in local shape space through <paramref name="outer"/>.</summary>
    /// <remarks>
    /// Equal positive radii use quad sides, unequal radii use two triangles per side, and one zero
    /// radius makes a cone whose degenerate apex cap is skipped. The caller validates the values
    /// and rejects two zero radii or a zero height.
    /// </remarks>
    private void DrawCylinderCore(
        Matrix4x4 outer,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Vector3 topCenter = new(0f, height, 0f);
        bool hasTop = radiusTop > 0f;
        bool hasBottom = radiusBottom > 0f;
        if (hasTop && hasBottom && radiusTop == radiusBottom)
        {
            Span<Matrix4x4> sideMatrices = BeginPrimitiveDraw(
                primitives.Quad,
                material,
                material.DefaultRenderState,
                color,
                sides
            );
            Span<Matrix4x4> capMatrices = BeginPrimitiveDraw(
                primitives.Triangle,
                material,
                material.DefaultRenderState,
                color,
                sides * 2
            );
            for (int side = 0; side < sides; side++)
            {
                Vector3 bottomStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 bottomEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 topStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 topEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                sideMatrices[side] =
                    CreateCylinderSideMatrix(bottomStart, bottomEnd, height) * outer;
                capMatrices[side * 2] =
                    CreateTriangleMatrix(
                        Vector3.Zero,
                        bottomStart,
                        bottomEnd,
                        Vector3.Cross(bottomStart, bottomEnd),
                        -Vector3.UnitY
                    ) * outer;
                capMatrices[(side * 2) + 1] =
                    CreateTriangleMatrix(
                        topCenter,
                        topEnd,
                        topStart,
                        Vector3.Cross(topEnd - topCenter, topStart - topCenter),
                        Vector3.UnitY
                    ) * outer;
            }
        }
        else if (hasTop && hasBottom)
        {
            // A tapered side segment is a trapezoid, not an affine image of the unit quad, so the
            // unequal-radius cylinder records two triangles per side segment instead of one quad.
            int sideTriangleCount = sides * 2;
            Span<Matrix4x4> triangleMatrices = BeginPrimitiveDraw(
                primitives.Triangle,
                material,
                material.DefaultRenderState,
                color,
                sides * 4
            );
            for (int side = 0; side < sides; side++)
            {
                Vector3 bottomStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 bottomEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 topStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 topEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                triangleMatrices[side * 2] = CreateTriangleMatrix(
                    bottomEnd,
                    bottomStart,
                    topStart,
                    outer
                );
                triangleMatrices[(side * 2) + 1] = CreateTriangleMatrix(
                    bottomEnd,
                    topStart,
                    topEnd,
                    outer
                );
                triangleMatrices[sideTriangleCount + side] =
                    CreateTriangleMatrix(
                        Vector3.Zero,
                        bottomStart,
                        bottomEnd,
                        Vector3.Cross(bottomStart, bottomEnd),
                        -Vector3.UnitY
                    ) * outer;
                triangleMatrices[sideTriangleCount + sides + side] =
                    CreateTriangleMatrix(
                        topCenter,
                        topEnd,
                        topStart,
                        Vector3.Cross(topEnd - topCenter, topStart - topCenter),
                        Vector3.UnitY
                    ) * outer;
            }
        }
        else
        {
            Span<Matrix4x4> triangleMatrices = BeginPrimitiveDraw(
                primitives.Triangle,
                material,
                material.DefaultRenderState,
                color,
                sides * 2
            );
            for (int side = 0; side < sides; side++)
            {
                Vector3 ringStart = GetCylinderPoint(radiusBottom, 0f, side, sides);
                Vector3 ringEnd = GetCylinderPoint(radiusBottom, 0f, side + 1, sides);
                Vector3 capStart = GetCylinderPoint(radiusTop, height, side, sides);
                Vector3 capEnd = GetCylinderPoint(radiusTop, height, side + 1, sides);
                if (hasBottom)
                {
                    // Wound like the tapered side triangles: apex, then the ring points in the
                    // outward-triangle order, so the outside of the cone is the front face.
                    triangleMatrices[side] = CreateTriangleMatrix(
                        topCenter,
                        ringEnd,
                        ringStart,
                        outer
                    );
                    triangleMatrices[sides + side] =
                        CreateTriangleMatrix(
                            Vector3.Zero,
                            ringStart,
                            ringEnd,
                            Vector3.Cross(ringStart, ringEnd),
                            -Vector3.UnitY
                        ) * outer;
                }
                else
                {
                    // The inverted cone is the same side triangle with the apex at the bottom.
                    triangleMatrices[side] = CreateTriangleMatrix(
                        Vector3.Zero,
                        capStart,
                        capEnd,
                        outer
                    );
                    triangleMatrices[sides + side] =
                        CreateTriangleMatrix(
                            topCenter,
                            capEnd,
                            capStart,
                            Vector3.Cross(capEnd - topCenter, capStart - topCenter),
                            Vector3.UnitY
                        ) * outer;
                }
            }
        }
    }

    /// <summary>Records a wire cylinder in local shape space as one line draw.</summary>
    private void DrawWireCylinderCore(
        Matrix4x4 outer,
        float radiusTop,
        float radiusBottom,
        float height,
        int sides,
        Color color
    )
    {
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
            instanceCount
        );
        int matrixIndex = 0;
        for (int side = 0; side < sides; side++)
        {
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    GetCylinderPoint(radiusBottom, 0f, side, sides),
                    GetCylinderPoint(radiusTop, height, side, sides)
                ) * outer;
        }

        if (hasBottom)
        {
            for (int side = 0; side < sides; side++)
            {
                matrices[matrixIndex++] =
                    CreateLineMatrix(
                        GetCylinderPoint(radiusBottom, 0f, side, sides),
                        GetCylinderPoint(radiusBottom, 0f, side + 1, sides)
                    ) * outer;
            }
        }

        if (hasTop)
        {
            for (int side = 0; side < sides; side++)
            {
                matrices[matrixIndex++] =
                    CreateLineMatrix(
                        GetCylinderPoint(radiusTop, height, side, sides),
                        GetCylinderPoint(radiusTop, height, side + 1, sides)
                    ) * outer;
            }
        }
    }

    /// <summary>Records a solid capsule in local shape space through <paramref name="outer"/>.</summary>
    /// <remarks>
    /// Local space is centered at the origin with cap centers at <c>(0, ±height/2, 0)</c>; the
    /// caller validates the values and rejects a zero radius or height.
    /// </remarks>
    private void DrawCapsuleCore(
        Matrix4x4 outer,
        float radius,
        float height,
        int rings,
        int slices,
        Color color
    )
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> sideMatrices = BeginPrimitiveDraw(
            primitives.Quad,
            material,
            material.DefaultRenderState,
            color,
            slices
        );
        int capTriangleCount = 2 * slices * ((2 * rings) - 1);
        Span<Matrix4x4> capMatrices = BeginPrimitiveDraw(
            primitives.Triangle,
            material,
            material.DefaultRenderState,
            color,
            capTriangleCount
        );
        float halfHeight = height * 0.5f;
        for (int side = 0; side < slices; side++)
        {
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
            outer
        );
        AddHemisphereTriangles(
            capMatrices,
            matrixIndex,
            -halfHeight,
            radius,
            rings,
            slices,
            top: false,
            outer
        );
    }

    /// <summary>Records a wire capsule in local shape space as one line draw.</summary>
    /// <remarks>
    /// The draw has <c>slices * (4 * rings + 1)</c> line instances; the
    /// caller validates the values.
    /// </remarks>
    private void DrawWireCapsuleCore(
        Matrix4x4 outer,
        float radius,
        float height,
        int rings,
        int slices,
        Color color
    )
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            slices * ((4 * rings) + 1)
        );
        int matrixIndex = 0;
        float halfHeight = height * 0.5f;
        for (int slice = 0; slice < slices; slice++)
        {
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    GetCylinderPoint(radius, -halfHeight, slice, slices),
                    GetCylinderPoint(radius, halfHeight, slice, slices)
                ) * outer;
        }

        for (int slice = 0; slice < slices; slice++)
        {
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    GetCylinderPoint(radius, -halfHeight, slice, slices),
                    GetCylinderPoint(radius, -halfHeight, slice + 1, slices)
                ) * outer;
            matrices[matrixIndex++] =
                CreateLineMatrix(
                    GetCylinderPoint(radius, halfHeight, slice, slices),
                    GetCylinderPoint(radius, halfHeight, slice + 1, slices)
                ) * outer;
        }

        for (int cap = 0; cap < 2; cap++)
        {
            bool top = cap == 0;
            float centerY = top ? halfHeight : -halfHeight;
            for (int slice = 0; slice < slices; slice++)
            {
                float theta = GetSliceAngle(slice, slices);
                Vector3 start = top
                    ? new Vector3(0f, centerY + radius, 0f)
                    : new Vector3(0f, centerY - radius, 0f);
                for (int ring = 1; ring <= rings; ring++)
                {
                    float phi = (MathF.PI * 0.5f) * ring / rings;
                    Vector3 end = GetHemispherePoint(centerY, radius, phi, theta, top);
                    matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                    start = end;
                }
            }

            for (int ring = 1; ring < rings; ring++)
            {
                float phi = (MathF.PI * 0.5f) * ring / rings;
                for (int slice = 0; slice < slices; slice++)
                {
                    Vector3 start = GetHemispherePoint(
                        centerY,
                        radius,
                        phi,
                        GetSliceAngle(slice, slices),
                        top
                    );
                    Vector3 end = GetHemispherePoint(
                        centerY,
                        radius,
                        phi,
                        GetSliceAngle(slice + 1, slices),
                        top
                    );
                    matrices[matrixIndex++] = CreateLineMatrix(start, end) * outer;
                }
            }
        }
    }

    /// <summary>Fills one capsule hemisphere's cap triangles.</summary>
    /// <remarks>
    /// The top hemisphere uses the engine sphere winding; the bottom mirrors coordinates and
    /// reverses winding so both caps face outward.
    /// </remarks>
    private static int AddHemisphereTriangles(
        Span<Matrix4x4> matrices,
        int matrixIndex,
        float centerY,
        float radius,
        int rings,
        int slices,
        bool top,
        Matrix4x4 outer
    )
    {
        Vector3 pole = top
            ? new Vector3(0f, centerY + radius, 0f)
            : new Vector3(0f, centerY - radius, 0f);
        float firstPhi = (MathF.PI * 0.5f) / rings;
        for (int slice = 0; slice < slices; slice++)
        {
            Vector3 start = GetHemispherePoint(
                centerY,
                radius,
                firstPhi,
                GetSliceAngle(slice, slices),
                top
            );
            Vector3 end = GetHemispherePoint(
                centerY,
                radius,
                firstPhi,
                GetSliceAngle(slice + 1, slices),
                top
            );
            matrixIndex = AddCapsuleTriangle(matrices, matrixIndex, pole, start, end, top, outer);
        }

        for (int ring = 1; ring < rings; ring++)
        {
            float upperPhi = (MathF.PI * 0.5f) * ring / rings;
            float lowerPhi = (MathF.PI * 0.5f) * (ring + 1) / rings;
            for (int slice = 0; slice < slices; slice++)
            {
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
                    outer
                );
                matrixIndex = AddCapsuleTriangle(
                    matrices,
                    matrixIndex,
                    upperEnd,
                    lowerStart,
                    lowerEnd,
                    top,
                    outer
                );
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
        Matrix4x4 outer
    )
    {
        matrices[matrixIndex] = top
            ? CreateTriangleMatrix(a, b, c, outer)
            : CreateTriangleMatrix(a, c, b, outer);
        return matrixIndex + 1;
    }

    /// <summary>Records a closed unit circle in the local <c>XZ</c> plane.</summary>
    private void DrawCircleCore(Matrix4x4 outer, Color color)
    {
        PrimitiveAssets primitives = RequirePrimitives();
        Shader material = primitives.PrimitiveMaterial;
        Span<Matrix4x4> matrices = BeginPrimitiveDraw(
            primitives.Line,
            material,
            material.DefaultRenderState,
            color,
            CIRCLE_SEGMENTS
        );
        for (int segment = 0; segment < CIRCLE_SEGMENTS; segment++)
        {
            matrices[segment] =
                CreateLineMatrix(
                    GetCirclePoint(segment, CIRCLE_SEGMENTS),
                    GetCirclePoint(segment + 1, CIRCLE_SEGMENTS)
                ) * outer;
        }
    }

    private static Vector3 GetCirclePoint(int segment, int segments)
    {
        float angle = GetSliceAngle(segment, segments);
        return new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle));
    }

    private static Vector3 GetCylinderPoint(float radius, float y, int side, int sides)
    {
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
        bool top
    )
    {
        float ringRadius = radius * MathF.Sin(phi);
        float poleOffset = radius * MathF.Cos(phi);
        return new Vector3(
            ringRadius * MathF.Sin(theta),
            centerY + (top ? poleOffset : -poleOffset),
            ringRadius * MathF.Cos(theta)
        );
    }

    private static float GetSliceAngle(int slice, int slices) => MathF.Tau * slice / slices;

    private static Vector3 GetStripCorner(ReadOnlySpan<Vector3> points, int triangle, int corner)
    {
        if ((triangle & 1) == 0)
        {
            return points[triangle + corner];
        }

        return corner switch
        {
            0 => points[triangle + 1],
            1 => points[triangle],
            _ => points[triangle + 2],
        };
    }

    private static void ValidateTransform(Transform transform)
    {
        ValidateFinite(transform.Position, nameof(transform));
        Quaternion rotation = transform.Rotation;
        if (
            !float.IsFinite(rotation.X)
            || !float.IsFinite(rotation.Y)
            || !float.IsFinite(rotation.Z)
            || !float.IsFinite(rotation.W)
        )
        {
            throw new ArgumentException(
                "The transform rotation must be finite.",
                nameof(transform)
            );
        }

        ValidateFinite(transform.Scale, nameof(transform));
        ValidateNonNegative(transform.Scale, nameof(transform));
    }

    private static bool HasZeroScale(Transform transform) =>
        transform.Scale.X == 0f || transform.Scale.Y == 0f || transform.Scale.Z == 0f;

    /// <summary>Validates a normalized source rectangle.</summary>
    /// <remarks>
    /// <see cref="Rect.X"/> and <see cref="Rect.Y"/> are the top-left corner and
    /// <see cref="Rect.Width"/> and <see cref="Rect.Height"/> the extents; a rectangle ending
    /// exactly on <c>1</c> is legal.
    /// </remarks>
    private static void ValidateBillboardSource(Rect source, string parameterName)
    {
        ValidateFinite(source.X, parameterName);
        ValidateFinite(source.Y, parameterName);
        ValidateFinite(source.Width, parameterName);
        ValidateFinite(source.Height, parameterName);
        if (!(source.Width > 0f) || !(source.Height > 0f))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                source,
                "The source rectangle must have a positive width and height."
            );
        }

        if (source.X < 0f || source.Y < 0f || source.Right > 1f || source.Bottom > 1f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                source,
                "The source rectangle must lie within the normalized [0, 1] texture rectangle."
            );
        }
    }

    private static void ValidateTessellation(int rings, int slices)
    {
        if (rings < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rings),
                rings,
                "The shape needs at least two rings."
            );
        }

        if (slices < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slices),
                slices,
                "The shape needs at least three slices."
            );
        }
    }

    private static void ValidateSides(int sides)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sides),
                sides,
                "A cylinder needs at least three sides."
            );
        }
    }

    private static void ValidateParts(IReadOnlyList<ModelPart> parts)
    {
        int partCount = parts.Count;
        for (int index = 0; index < partCount; index++)
        {
            ModelPart part = parts[index];
            ValidateMesh(part.Mesh);
            ValidateShader(part.Shader);
            ValidateTextures(part.Shader.TextureValues);
        }
    }

    private static void ValidateMesh(Mesh mesh)
    {
        ObjectDisposedException.ThrowIf(mesh.IsDisposed, mesh);
    }

    private static void ValidateShader(Shader shader)
    {
        ObjectDisposedException.ThrowIf(shader.IsDisposed, shader);
        const ShaderStageMask requiredStages = ShaderStageMask.Vertex | ShaderStageMask.Fragment;
        if (shader.Stages != requiredStages)
        {
            throw new InvalidOperationException(
                $"The shader '{shader.Path}' must declare both the vertex and fragment stages to be "
                    + "used in a draw."
            );
        }
    }

    private static void ValidateTextures(ReadOnlySpan<Texture?> textures)
    {
        foreach (Texture? texture in textures)
        {
            if (texture is not null)
            {
                ObjectDisposedException.ThrowIf(texture.IsDisposed, texture);
            }
        }
    }

    private static void ThrowIfModelDisposed(Model model)
    {
        ObjectDisposedException.ThrowIf(model.IsDisposed, model);
    }

    private static void ValidateFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentException("The position components must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentException("The position components must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentException("The value must be finite.", parameterName);
        }
    }

    private static void ValidateFinite(Color color, string parameterName)
    {
        if (
            !float.IsFinite(color.R)
            || !float.IsFinite(color.G)
            || !float.IsFinite(color.B)
            || !float.IsFinite(color.A)
        )
        {
            throw new ArgumentException("The color components must be finite.", parameterName);
        }
    }

    private static void ValidateNonNegative(float value, string parameterName)
    {
        if (value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be zero or positive."
            );
        }
    }

    private static void ValidateNonNegative(Vector2 size, string parameterName)
    {
        if (size.X < 0f || size.Y < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                "The extent components must be zero or positive."
            );
        }
    }

    private static void ValidateNonNegative(Vector3 size, string parameterName)
    {
        if (size.X < 0f || size.Y < 0f || size.Z < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                "The extent components must be zero or positive."
            );
        }
    }

    private static void ValidatePositive(float value, string parameterName)
    {
        if (!(value > 0f))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "The value must be positive."
            );
        }
    }

    private void EnsureDrawable(string operation)
    {
        ThrowIfDisposed();
        TickThread.RequireCurrent(operation);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
