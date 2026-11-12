using System.Buffers.Binary;
using System.Numerics;
using RayBork.Bake.Shaders;
using RayBork.Internal;
using RayBork.Tests.Graphics.Assets;
using Silk.NET.Shaderc;

namespace RayBork.Tests;

/// <summary>
/// Uploads real meshes, textures, and baked shaders through the GPU asset bridge and submits sealed
/// draw frames to a <see cref="BackendTestHost"/>, so the M4 GPU draw tests exercise the production
/// upload, bindless, pipeline, and recording path.
/// </summary>
/// <remarks>
/// <para>
/// The shaders are compiled at test time through the bake's own <see cref="ShaderPrologueGenerator"/>
/// and <see cref="GlslCompiler"/> plus the same entry-point rewrite the bake uses, so the generated
/// shader ABI is the one production shaders receive. The harness attaches the test thread as the
/// engine tick thread for its lifetime so the public <see cref="Shader"/> cursor can be used to build
/// per-draw uniform snapshots and the harness can detach cleanly.
/// </para>
/// <para>
/// The harness owns the asset upload bridge and the public asset wrappers it creates. Disposal waits
/// for frame idleness, closes the bridge (destroying the live GPU resources), and detaches the tick
/// thread. It never sleeps and never polls; every upload is completed by the worker on this thread.
/// </para>
/// </remarks>
internal sealed class VulkanDrawTestHarness : IDisposable {
    internal const string DEFAULT_VERTEX_SOURCE = """
        #version 450

        layout(location = 0) in vec3 aPosition;
        layout(location = 2) in vec2 aUv;

        layout(location = 0) out vec2 vUv;

        void main_vertex() {
            vec4 world = rbInstance.Transform * vec4(aPosition, 1.0);
            gl_Position = rbFrame.ViewProjection * world;
            vUv = aUv;
        }
        """;

    internal const string DEFAULT_FRAGMENT_SOURCE = """
        #version 450

        layout(location = 0) in vec2 vUv;

        layout(location = 0) out vec4 oColor;

        void main_fragment() {
            oColor = texture(mainTexture, vUv) * uTint * rbInstance.Color;
        }
        """;

    private readonly BackendTestHost _host;
    private readonly AssetGpuUploadHarness _uploads;
    private bool _disposed;

    /// <summary>
    /// Creates the harness for an initialized backend host and attaches the test thread as the tick
    /// thread.
    /// </summary>
    /// <param name="host">The initialized backend host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <c>null</c>.</exception>
    internal VulkanDrawTestHarness(BackendTestHost host) {
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _uploads = new AssetGpuUploadHarness(host.Backend);
        TickThread.Attach();
    }

    /// <summary>Gets the backend host the harness submits to.</summary>
    internal BackendTestHost Host => _host;

    /// <summary>Gets the bridge that owns the live GPU resources.</summary>
    internal GpuAssetBridge Bridge => _uploads.Bridge;

    /// <summary>Gets the Vulkan asset worker.</summary>
    internal VulkanAssetWorker Worker => _uploads.Worker;

    /// <summary>Gets the number of live bindless slots, including the white slot.</summary>
    internal int TextureCount => _host.Backend.BindlessTextures.LiveCount;

    /// <summary>Gets the number of cached geometry pipelines.</summary>
    internal int PipelineCount => _host.Backend.GeometryPipelines.Count;

    /// <summary>
    /// Uploads a solid-color texture through the real GPU asset worker.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="color">The linear RGBA color; the channels are clamped to <c>[0, 1]</c>.</param>
    /// <returns>The public texture wrapper over the uploaded image.</returns>
    internal Texture CreateTexture(string path, Color color) {
        AssertNotDisposed();
        byte[] payload = Ktx2TestWriter.WriteRgba8Solid(color);
        TextureAssetDescriptor descriptor = new() {
            Payload = path + "-ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Unorm,
            IsSrgb = false,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = 1f,
        };
        GpuAssetResource resource = _uploads.Upload(new TextureGpuUpload(path, descriptor, payload));
        return new Texture(CreateAsset(path, resource), descriptor);
    }

    /// <summary>
    /// Uploads an axis-aligned quad mesh in the fixed 88-byte vertex layout.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="halfWidth">The half width in mesh space.</param>
    /// <param name="halfHeight">The half height in mesh space.</param>
    /// <returns>The public mesh wrapper over the uploaded buffers.</returns>
    internal Mesh CreateQuad(string path = "meshes/quad", float halfWidth = 1f, float halfHeight = 1f) {
        AssertNotDisposed();
        Vertex[] vertices = [
            new(new Vector3(-halfWidth, -halfHeight, 0f), uv: new Vector2(0f, 1f)),
            new(new Vector3(halfWidth, -halfHeight, 0f), uv: new Vector2(1f, 1f)),
            new(new Vector3(halfWidth, halfHeight, 0f), uv: new Vector2(1f, 0f)),
            new(new Vector3(-halfWidth, halfHeight, 0f), uv: new Vector2(0f, 0f)),
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return CreateMesh(path, vertices, indices);
    }

    /// <summary>
    /// Uploads a cube mesh in the fixed 88-byte vertex layout.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="halfExtent">The half extent of the cube in mesh space.</param>
    /// <returns>The public mesh wrapper over the uploaded buffers.</returns>
    internal Mesh CreateCube(string path = "meshes/cube", float halfExtent = 0.5f) {
        AssertNotDisposed();
        float h = halfExtent;
        Vertex[] vertices = [
            new(new Vector3(-h, -h, -h), uv: new Vector2(0f, 1f)),
            new(new Vector3(h, -h, -h), uv: new Vector2(1f, 1f)),
            new(new Vector3(h, h, -h), uv: new Vector2(1f, 0f)),
            new(new Vector3(-h, h, -h), uv: new Vector2(0f, 0f)),
            new(new Vector3(-h, -h, h), uv: new Vector2(0f, 1f)),
            new(new Vector3(h, -h, h), uv: new Vector2(1f, 1f)),
            new(new Vector3(h, h, h), uv: new Vector2(1f, 0f)),
            new(new Vector3(-h, h, h), uv: new Vector2(0f, 0f)),
        ];
        uint[] indices = [
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            3, 6, 2, 3, 7, 6,
            0, 4, 7, 0, 7, 3,
            1, 2, 6, 1, 6, 5,
        ];
        return CreateMesh(path, vertices, indices);
    }

    /// <summary>
    /// Compiles a user vertex/fragment pair through the bake's prologue and shaderc path, uploads the
    /// two modules, and returns the public shader wrapper.
    /// </summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="vertexSource">The user vertex GLSL source.</param>
    /// <param name="fragmentSource">The user fragment GLSL source.</param>
    /// <param name="uniforms">The reflected uniforms in declaration order.</param>
    /// <param name="textures">The texture binding names in declaration order.</param>
    /// <param name="defaultRenderState">The shader's default render state.</param>
    /// <returns>The public shader wrapper over the uploaded modules.</returns>
    internal Shader CreateShader(
        string path,
        string vertexSource,
        string fragmentSource,
        ShaderUniformAsset[]? uniforms = null,
        string[]? textures = null,
        RenderState? defaultRenderState = null) {
        AssertNotDisposed();
        ShaderUniformAsset[] declaredUniforms = uniforms ?? [];
        string[] declaredTextures = textures ?? [];
        ShaderAbiLayout layout = new(declaredUniforms, declaredTextures);
        byte[] vertexSpirV = CompileStage(
            vertexSource,
            layout,
            ShaderStageMask.Vertex,
            ShaderKind.GlslVertexShader,
            SpirvExecutionModel.Vertex,
            path + ".vert");
        byte[] fragmentSpirV = CompileStage(
            fragmentSource,
            layout,
            ShaderStageMask.Fragment,
            ShaderKind.GlslFragmentShader,
            SpirvExecutionModel.Fragment,
            path + ".frag");
        ShaderAssetDescriptor descriptor = new() {
            VertexPayload = path + "-vert",
            FragmentPayload = path + "-frag",
            VertexEntry = ShaderAbiNames.VERTEX_ENTRY_POINT,
            FragmentEntry = ShaderAbiNames.FRAGMENT_ENTRY_POINT,
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = defaultRenderState ?? RenderState.Opaque,
            Uniforms = declaredUniforms,
            Textures = declaredTextures,
        };
        GpuAssetResource resource = _uploads.Upload(
            new ShaderGpuUpload(path, descriptor, vertexSpirV, fragmentSpirV));
        return new Shader(CreateAsset(path, resource), descriptor);
    }

    /// <summary>
    /// Builds an orthographic camera that looks down <c>-Z</c> from <c>(0, 0, 1)</c> and sees a world
    /// height of <paramref name="height"/>.
    /// </summary>
    /// <param name="host">The host that supplies the target extent.</param>
    /// <param name="height">The visible world-space height.</param>
    /// <returns>The camera view snapshot for <see cref="CreateFrame"/>.</returns>
    internal static CameraView OrthographicCamera(BackendTestHost host, float height = 2f) {
        ArgumentNullException.ThrowIfNull(host);
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(height, aspect, 0.1f, 10f);
        return new CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            checked((int)host.SceneTarget.Extent.Width),
            checked((int)host.SceneTarget.Extent.Height));
    }

    /// <summary>
    /// Builds a perspective camera at the origin that looks down <c>-Z</c>.
    /// </summary>
    /// <param name="host">The host that supplies the target extent.</param>
    /// <param name="fieldOfView">The vertical field of view in degrees.</param>
    /// <param name="near">The near clip distance.</param>
    /// <param name="far">The far clip distance; positive infinity is allowed.</param>
    /// <returns>The camera view snapshot for <see cref="CreateFrame"/>.</returns>
    internal static CameraView PerspectiveCamera(
        BackendTestHost host,
        float fieldOfView = 60f,
        float near = 0.01f,
        float far = float.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(host);
        Transform transform = new(Vector3.Zero);
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(fieldOfView, aspect, near, far);
        return new CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            checked((int)host.SceneTarget.Extent.Width),
            checked((int)host.SceneTarget.Extent.Height));
    }

    /// <summary>
    /// Builds one sealed frame with a clearing camera pass and the requested draw records.
    /// </summary>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="clearColor">The linear clear color.</param>
    /// <param name="camera">The camera view snapshot.</param>
    /// <param name="draws">The draw records in recording order.</param>
    /// <returns>The sealed packet.</returns>
    internal RenderFramePacket CreateFrame(
        long frameIndex,
        Color clearColor,
        CameraView camera,
        params DrawSpec[] draws) {
        AssertNotDisposed();
        RenderFramePacket packet = new(frameIndex);
        packet.BeginRecording(0.0, 0.0);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            index: 0,
            camera.View,
            camera.Projection,
            camera.ViewProjection,
            camera.Position,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: camera.Width,
            viewportHeight: camera.Height,
            ClearMode.ColorAndDepth,
            clearColor);
        foreach (DrawSpec draw in draws) {
            RenderDrawRecord record = packet.RentDrawRecord();
            record.Begin(
                draw.Mesh.Lifetime,
                draw.Mesh.Topology,
                draw.Mesh.Bounds,
                draw.Mesh.IndexCount,
                draw.Shader.Lifetime,
                draw.RenderState ?? draw.Shader.DefaultRenderState,
                draw.Tint ?? Color.White);
            record.SetShaderState(
                draw.InstanceTemplate ?? draw.Shader.InstanceData,
                draw.TextureBindings ?? draw.Shader.TextureValues);
            Span<Matrix4x4> worlds = record.BeginInstances(draw.WorldMatrices.Length);
            draw.WorldMatrices.AsSpan().CopyTo(worlds);
            pass.Add(record);
        }

        packet.Seal();
        return packet;
    }

    /// <summary>
    /// Submits and presents one sealed frame.
    /// </summary>
    /// <param name="packet">The sealed packet.</param>
    internal void Submit(RenderFramePacket packet) {
        AssertNotDisposed();
        _host.Backend.Submit(packet);
        _host.Backend.Present();
    }

    /// <summary>
    /// Builds, submits, and presents one frame.
    /// </summary>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="clearColor">The linear clear color.</param>
    /// <param name="camera">The camera view snapshot.</param>
    /// <param name="draws">The draw records in recording order.</param>
    internal void SubmitFrame(
        long frameIndex,
        Color clearColor,
        CameraView camera,
        params DrawSpec[] draws) {
        Submit(CreateFrame(frameIndex, clearColor, camera, draws));
    }

    /// <summary>
    /// Recreates every live GPU asset from its retained upload, as the render loop does after a device
    /// recovery.
    /// </summary>
    internal void RestoreAssets() {
        AssertNotDisposed();
        _uploads.Restore();
    }

    /// <summary>
    /// Reads one resolved linear scene color pixel.
    /// </summary>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadColor(uint x, uint y) {
        VulkanReadback.ReadSceneColorPixel(_host.Device, _host.SceneTarget, x, y, out float r, out float g, out float b);
        return (r, g, b);
    }

    /// <summary>
    /// Reads one reverse-Z scene depth pixel from a single-sample target.
    /// </summary>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The stored depth.</returns>
    internal float ReadDepth(uint x, uint y) {
        VulkanReadback.ReadSceneDepthPixel(_host.Device, _host.SceneTarget, x, y, out float depth);
        return depth;
    }

    /// <inheritdoc />
    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        try {
            _host.Backend.WaitForFrameResourcesIdle();
            _uploads.Dispose();
        } finally {
            TickThread.Detach();
        }
    }

    private Mesh CreateMesh(string path, Vertex[] vertices, uint[] indices) {
        int vertexBytes = checked(vertices.Length * MeshAssetDescriptor.VERTEX_STRIDE);
        byte[] payload = new byte[vertexBytes + (indices.Length * sizeof(uint))];
        Span<byte> vertexSpan = payload.AsSpan(0, vertexBytes);
        for (int index = 0; index < vertices.Length; index++) {
            WriteVertex(vertexSpan.Slice(index * MeshAssetDescriptor.VERTEX_STRIDE), vertices[index]);
        }

        Span<byte> indexSpan = payload.AsSpan(vertexBytes);
        for (int index = 0; index < indices.Length; index++) {
            BinaryPrimitives.WriteUInt32LittleEndian(indexSpan[(index * sizeof(uint))..], indices[index]);
        }

        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        foreach (Vertex vertex in vertices) {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }

        MeshAssetDescriptor descriptor = new() {
            Payload = path + "-mesh",
            VertexCount = (uint)vertices.Length,
            IndexCount = (uint)indices.Length,
            Bounds = [
                minimum.X,
                minimum.Y,
                minimum.Z,
                maximum.X,
                maximum.Y,
                maximum.Z,
            ],
            Topology = RayBork.PrimitiveTopology.TriangleList,
        };
        GpuAssetResource resource = _uploads.Upload(new MeshGpuUpload(path, descriptor, payload));
        return new Mesh(CreateAsset(path, resource), descriptor, vertices, indices, isProcedural: true);
    }

    private static void WriteVertex(Span<byte> destination, Vertex vertex) {
        int offset = 0;
        WriteVector3(destination, ref offset, vertex.Position);
        WriteVector3(destination, ref offset, vertex.Normal);
        WriteVector2(destination, ref offset, vertex.Uv);
        WriteVector2(destination, ref offset, vertex.Uv1);
        WriteVector2(destination, ref offset, vertex.Uv2);
        WriteVector2(destination, ref offset, vertex.Uv3);
        WriteSingle(destination, ref offset, vertex.Color.R);
        WriteSingle(destination, ref offset, vertex.Color.G);
        WriteSingle(destination, ref offset, vertex.Color.B);
        WriteSingle(destination, ref offset, vertex.Color.A);
        WriteSingle(destination, ref offset, vertex.Tangent.X);
        WriteSingle(destination, ref offset, vertex.Tangent.Y);
        WriteSingle(destination, ref offset, vertex.Tangent.Z);
        WriteSingle(destination, ref offset, vertex.Tangent.W);
        if (offset != MeshAssetDescriptor.VERTEX_STRIDE) {
            throw new InvalidOperationException(
                $"The vertex layout wrote {offset} byte(s) instead of {MeshAssetDescriptor.VERTEX_STRIDE}.");
        }
    }

    private static void WriteVector2(Span<byte> destination, ref int offset, Vector2 value) {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
    }

    private static void WriteVector3(Span<byte> destination, ref int offset, Vector3 value) {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
        WriteSingle(destination, ref offset, value.Z);
    }

    private static void WriteSingle(Span<byte> destination, ref int offset, float value) {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);
        offset += sizeof(float);
    }

    private static byte[] CompileStage(
        string source,
        ShaderAbiLayout layout,
        ShaderStageMask stage,
        ShaderKind kind,
        SpirvExecutionModel executionModel,
        string fileName) {
        if (!ShaderPrologueGenerator.TryBuild(source, layout, stage, out string wrapped, out string problem)) {
            throw new InvalidOperationException($"The {fileName} prologue failed: {problem}");
        }

        if (!GlslCompiler.TryCompile(
                wrapped,
                fileName,
                kind,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                out byte[] spirV,
                out string message)) {
            throw new InvalidOperationException($"The {fileName} stage did not compile: {message}");
        }

        string entryPoint = stage == ShaderStageMask.Vertex
            ? ShaderAbiNames.VERTEX_ENTRY_POINT
            : ShaderAbiNames.FRAGMENT_ENTRY_POINT;
        if (!SpirvEntryPointRewriter.TryRename(
                spirV,
                executionModel,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                entryPoint,
                out byte[] renamed,
                out string renameMessage)) {
            throw new InvalidOperationException($"The {fileName} entry point could not be renamed: {renameMessage}");
        }

        return renamed;
    }

    private EngineAsset CreateAsset(string path, GpuAssetResource resource) {
        // Wire the production destruction path so a public Dispose queues the GPU destruction on the
        // bridge exactly as the catalog does.
        EngineAsset asset = new(
            "/" + path,
            resource,
            _uploads.Bridge.Destroy,
            static () => { });
        asset.AddOwnerReference();
        return asset;
    }

    private void AssertNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>
    /// A camera value snapshot used by <see cref="CreateFrame"/>.
    /// </summary>
    /// <param name="View">The world-to-view matrix.</param>
    /// <param name="Projection">The reverse-Z view-to-clip matrix.</param>
    /// <param name="ViewProjection">The combined row-vector matrix.</param>
    /// <param name="Position">The camera position.</param>
    /// <param name="Width">The viewport width in pixels.</param>
    /// <param name="Height">The viewport height in pixels.</param>
    internal readonly record struct CameraView(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Matrix4x4 ViewProjection,
        Vector3 Position,
        int Width,
        int Height);

    /// <summary>
    /// One draw record for <see cref="CreateFrame"/>.
    /// </summary>
    /// <param name="Mesh">The mesh asset.</param>
    /// <param name="Shader">The shader asset.</param>
    /// <param name="WorldMatrices">The mesh-space-to-world matrices, one per instance.</param>
    /// <param name="RenderState">The render state override, or <c>null</c> for the shader default.</param>
    /// <param name="Tint">The per-instance tint, or <c>null</c> for opaque white.</param>
    /// <param name="InstanceTemplate">
    /// The captured per-instance uniform template, or <c>null</c> for the shader cursor's current
    /// template. The harness copies it, so a later shader cursor change cannot mutate the draw.
    /// </param>
    /// <param name="TextureBindings">
    /// The texture objects in sidecar declaration order, or <c>null</c> for the cursor's current
    /// bindings. A <c>null</c> entry resolves to the white fallback slot.
    /// </param>
    internal sealed record DrawSpec(
        Mesh Mesh,
        Shader Shader,
        Matrix4x4[] WorldMatrices,
        RenderState? RenderState = null,
        Color? Tint = null,
        byte[]? InstanceTemplate = null,
        Texture?[]? TextureBindings = null) {
        /// <summary>Creates a one-instance draw.</summary>
        /// <param name="mesh">The mesh asset.</param>
        /// <param name="shader">The shader asset.</param>
        /// <param name="world">The mesh-space-to-world matrix, or <c>null</c> for identity.</param>
        /// <returns>The draw specification.</returns>
        internal static DrawSpec Single(Mesh mesh, Shader shader, Matrix4x4? world = null) => new(
            mesh,
            shader,
            [world ?? Matrix4x4.Identity]);
    }

    /// <summary>
    /// Copies one shader cursor snapshot and texture binding list into a reusable draw specification
    /// value.
    /// </summary>
    /// <param name="shader">The shader whose cursor is captured.</param>
    /// <returns>The captured instance template and bindings.</returns>
    internal static (byte[] Template, Texture?[] Textures) SnapshotCursor(Shader shader) {
        ArgumentNullException.ThrowIfNull(shader);
        byte[] template = shader.InstanceData.ToArray();
        Texture?[] textures = shader.TextureValues.ToArray();
        return (template, textures);
    }
}
