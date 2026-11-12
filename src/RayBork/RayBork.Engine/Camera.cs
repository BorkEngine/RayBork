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

    private void EnsureDrawable(string operation) {
        ThrowIfDisposed();
        TickThread.RequireCurrent(operation);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
