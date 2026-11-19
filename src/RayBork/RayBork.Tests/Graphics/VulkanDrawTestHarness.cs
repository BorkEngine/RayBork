using System.Buffers.Binary;
using System.Numerics;
using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Bake.Shaders;
using RayBork.Internal;
using RayBork.Tests.Graphics.Assets;
using Silk.NET.Shaderc;

namespace RayBork.Tests;

/// <summary>
/// Holds the production GPU upload and draw submission path for one Vulkan draw test.
/// </summary>
/// <remarks>
/// Shaders are compiled through the bake's prologue, compiler, and entry-point rewrite, and the test
/// thread is attached as the tick thread for the harness lifetime. The harness owns the upload bridge
/// and every public asset wrapper; disposal waits for frame idleness, closes the bridge, and
/// detaches the tick thread.
/// </remarks>
internal sealed class VulkanDrawTestHarness : IDisposable
{
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
    private AssetCatalog? _catalog;
    private SsaoAssets? _ssaoAssets;
    private AssetCatalog? _bloomCatalog;
    private BloomAssets? _bloomAssets;
    private bool _disposed;

    /// <summary>Creates the harness for an initialized host and attaches the tick thread.</summary>
    /// <param name="host">The initialized backend host.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <c>null</c>.</exception>
    internal VulkanDrawTestHarness(BackendTestHost host)
    {
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

    /// <summary>Uploads a solid-color 1x1 texture through the real GPU asset worker.</summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="color">The linear RGBA color; channels are clamped to <c>[0, 1]</c>.</param>
    /// <returns>The public texture wrapper.</returns>
    internal Texture CreateTexture(string path, Color color)
    {
        AssertNotDisposed();
        byte[] payload = Ktx2TestWriter.WriteRgba8Solid(color);
        TextureAssetDescriptor descriptor = new()
        {
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
        GpuAssetResource resource = _uploads.Upload(
            new TextureGpuUpload(path, descriptor, payload)
        );
        return new Texture(CreateAsset(path, resource), descriptor);
    }

    /// <summary>Uploads an axis-aligned quad mesh in the fixed vertex layout.</summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="halfWidth">The half width in mesh space.</param>
    /// <param name="halfHeight">The half height in mesh space.</param>
    /// <returns>The public mesh wrapper.</returns>
    internal Mesh CreateQuad(
        string path = "meshes/quad",
        float halfWidth = 1f,
        float halfHeight = 1f
    )
    {
        AssertNotDisposed();
        Vertex[] vertices =
        [
            new(new Vector3(-halfWidth, -halfHeight, 0f), uv: new Vector2(0f, 1f)),
            new(new Vector3(halfWidth, -halfHeight, 0f), uv: new Vector2(1f, 1f)),
            new(new Vector3(halfWidth, halfHeight, 0f), uv: new Vector2(1f, 0f)),
            new(new Vector3(-halfWidth, halfHeight, 0f), uv: new Vector2(0f, 0f)),
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return CreateMesh(path, vertices, indices);
    }

    /// <summary>Uploads a cube mesh in the fixed vertex layout.</summary>
    /// <param name="path">The canonical asset path without a leading slash.</param>
    /// <param name="halfExtent">The half extent of the cube in mesh space.</param>
    /// <returns>The public mesh wrapper.</returns>
    internal Mesh CreateCube(string path = "meshes/cube", float halfExtent = 0.5f)
    {
        AssertNotDisposed();
        float h = halfExtent;
        Vertex[] vertices =
        [
            new(new Vector3(-h, -h, -h), uv: new Vector2(0f, 1f)),
            new(new Vector3(h, -h, -h), uv: new Vector2(1f, 1f)),
            new(new Vector3(h, h, -h), uv: new Vector2(1f, 0f)),
            new(new Vector3(-h, h, -h), uv: new Vector2(0f, 0f)),
            new(new Vector3(-h, -h, h), uv: new Vector2(0f, 1f)),
            new(new Vector3(h, -h, h), uv: new Vector2(1f, 1f)),
            new(new Vector3(h, h, h), uv: new Vector2(1f, 0f)),
            new(new Vector3(-h, h, h), uv: new Vector2(0f, 0f)),
        ];
        uint[] indices =
        [
            0,
            2,
            1,
            0,
            3,
            2,
            4,
            5,
            6,
            4,
            6,
            7,
            0,
            1,
            5,
            0,
            5,
            4,
            3,
            6,
            2,
            3,
            7,
            6,
            0,
            4,
            7,
            0,
            7,
            3,
            1,
            2,
            6,
            1,
            6,
            5,
        ];
        return CreateMesh(path, vertices, indices);
    }

    /// <summary>
    /// Compiles, uploads, and returns a material over a user vertex/fragment pair; the caller disposes it.
    /// </summary>
    internal Shader CreateShader(
        string path,
        string vertexSource,
        string fragmentSource,
        ShaderUniformAsset[]? uniforms = null,
        string[]? textures = null,
        RenderState? defaultRenderState = null
    )
    {
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
            path + ".vert"
        );
        byte[] fragmentSpirV = CompileStage(
            fragmentSource,
            layout,
            ShaderStageMask.Fragment,
            ShaderKind.GlslFragmentShader,
            SpirvExecutionModel.Fragment,
            path + ".frag"
        );
        ShaderAssetDescriptor descriptor = new()
        {
            AbiVersion = ShaderAbiNames.ABI_VERSION,
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
            new ShaderGpuUpload(path, descriptor, vertexSpirV, fragmentSpirV)
        );
        ShaderProgram program = new(CreateAsset(path, resource), descriptor);
        Shader material = program.CreateMaterial();
        // The harness keeps no long-lived program reference; the returned material owns the program
        // and the caller disposes the material.
        program.Lifetime.ReleaseOwnerReference();
        return material;
    }

    /// <summary>Creates a second material over the same program as an existing material.</summary>
    /// <param name="shader">A material whose program is shared.</param>
    /// <returns>The new material; the caller disposes it.</returns>
    internal Shader CreateMaterial(Shader shader)
    {
        AssertNotDisposed();
        ArgumentNullException.ThrowIfNull(shader);
        return shader.Program.CreateMaterial();
    }

    /// <summary>Creates an orthographic camera at <c>(0, 0, 1)</c> looking down <c>-Z</c>.</summary>
    /// <param name="host">The host that supplies the target extent.</param>
    /// <param name="height">The visible world-space height.</param>
    /// <returns>The camera view snapshot for <see cref="CreateFrame"/>.</returns>
    internal static CameraView OrthographicCamera(BackendTestHost host, float height = 2f)
    {
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
            checked((int)host.SceneTarget.Extent.Height)
        );
    }

    /// <summary>Creates a perspective camera at the origin looking down <c>-Z</c>.</summary>
    /// <returns>The camera view snapshot for <see cref="CreateFrame"/>.</returns>
    internal static CameraView PerspectiveCamera(
        BackendTestHost host,
        float fieldOfView = 60f,
        float near = 0.01f,
        float far = float.PositiveInfinity
    )
    {
        ArgumentNullException.ThrowIfNull(host);
        Transform transform = new(Vector3.Zero);
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = host.SceneTarget.Extent.Width / (float)host.SceneTarget.Extent.Height;
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(
            fieldOfView,
            aspect,
            near,
            far
        );
        return new CameraView(
            view,
            projection,
            view * projection,
            transform.Position,
            checked((int)host.SceneTarget.Extent.Width),
            checked((int)host.SceneTarget.Extent.Height)
        );
    }

    /// <summary>Builds one sealed frame with a clearing camera pass and the requested draws.</summary>
    /// <returns>The sealed packet.</returns>
    internal RenderFramePacket CreateFrame(
        long frameIndex,
        Color clearColor,
        CameraView camera,
        params DrawSpec[] draws
    )
    {
        AssertNotDisposed();
        return CreateFrameWithPasses(
            frameIndex,
            new PassSpec(
                camera,
                0,
                0,
                camera.Width,
                camera.Height,
                ClearMode.ColorAndDepth,
                clearColor,
                draws
            )
        );
    }

    /// <summary>Builds one sealed frame from explicit camera passes.</summary>
    /// <param name="frameIndex">The frame index.</param>
    /// <param name="passes">The camera passes in recording order.</param>
    /// <returns>The sealed packet.</returns>
    internal RenderFramePacket CreateFrameWithPasses(long frameIndex, params PassSpec[] passes)
    {
        AssertNotDisposed();
        ArgumentNullException.ThrowIfNull(passes);
        RenderFramePacket packet = new(frameIndex);
        packet.BeginRecording(0.0, 0.0);
        for (int index = 0; index < passes.Length; index++)
        {
            PassSpec spec = passes[index];
            CameraPass pass = packet.BeginPass();
            pass.Begin(
                index,
                spec.Camera.View,
                spec.Camera.Projection,
                spec.Camera.ViewProjection,
                spec.Camera.Position,
                spec.ViewportX,
                spec.ViewportY,
                spec.ViewportWidth,
                spec.ViewportHeight,
                spec.ClearMode,
                spec.ClearColor,
                spec.TargetId
            );
            AddDraws(packet, pass, spec.Draws);
            AddPostProcessEffects(packet, pass, spec.PostProcessEffects);
        }

        packet.Seal();
        return packet;
    }

    private static void AddPostProcessEffects(
        RenderFramePacket packet,
        CameraPass pass,
        PostProcessEffectSnapshot[]? effects
    )
    {
        if (effects is null)
        {
            return;
        }

        foreach (PostProcessEffectSnapshot effect in effects)
        {
            pass.AddPostProcessEffect(effect);
            effect.Retain(packet.Retention);
        }
    }

    private static void AddDraws(RenderFramePacket packet, CameraPass pass, DrawSpec[] draws)
    {
        foreach (DrawSpec draw in draws)
        {
            RenderDrawRecord record = packet.RentDrawRecord();
            record.Begin(
                draw.Mesh.Lifetime,
                draw.Mesh.Topology,
                draw.Mesh.Bounds,
                draw.Mesh.IndexCount,
                draw.Shader.Program.Lifetime,
                draw.RenderState ?? draw.Shader.DefaultRenderState,
                draw.Tint ?? Color.White
            );
            record.SetShaderState(
                draw.InstanceTemplate ?? draw.Shader.InstanceData,
                draw.TextureBindings ?? draw.Shader.TextureValues
            );
            Span<Matrix4x4> worlds = record.BeginInstances(draw.WorldMatrices.Length);
            draw.WorldMatrices.AsSpan().CopyTo(worlds);
            pass.Add(record);
        }
    }

    /// <summary>Submits and presents one sealed frame.</summary>
    internal void Submit(RenderFramePacket packet)
    {
        AssertNotDisposed();
        _host.Backend.Submit(packet);
        _host.Backend.Present();
    }

    /// <summary>Builds, submits, and presents one frame.</summary>
    internal void SubmitFrame(
        long frameIndex,
        Color clearColor,
        CameraView camera,
        params DrawSpec[] draws
    )
    {
        Submit(CreateFrame(frameIndex, clearColor, camera, draws));
    }

    /// <summary>Loads the built-in SSAO assets through this harness's bridge.</summary>
    /// <returns>The loaded assets, owned by the harness.</returns>
    internal SsaoAssets LoadSsaoAssetsFromBuiltInPack()
    {
        AssertNotDisposed();
        if (_ssaoAssets is not null)
        {
            return _ssaoAssets;
        }

        AssetCatalog catalog = new(
            NullLoggerFactory.Instance,
            _uploads.Bridge,
            BuiltInAssetPack.Bytes,
            [],
            []
        );
        catalog.Start();
        try
        {
            Task<SsaoAssets> load = SsaoAssets.LoadAsync(catalog);
            bool completed = SpinWait.SpinUntil(
                () =>
                {
                    _uploads.Pump();
                    return load.IsCompleted;
                },
                AssetGpuUploadHarness.Timeout
            );
            Assert.True(completed, "The built-in SSAO assets did not finish loading.");
            _ssaoAssets = load.GetAwaiter().GetResult();
            _catalog = catalog;
            return _ssaoAssets;
        }
        catch
        {
            catalog.Stop();
            throw;
        }
    }

    /// <summary>Loads the built-in bloom assets through this harness's bridge.</summary>
    /// <returns>The loaded assets, owned by the harness.</returns>
    internal BloomAssets LoadBloomAssetsFromBuiltInPack()
    {
        AssertNotDisposed();
        if (_bloomAssets is not null)
        {
            return _bloomAssets;
        }

        AssetCatalog catalog = new(
            NullLoggerFactory.Instance,
            _uploads.Bridge,
            BuiltInAssetPack.Bytes,
            [],
            []
        );
        catalog.Start();
        try
        {
            Task<BloomAssets> load = BloomAssets.LoadAsync(catalog);
            bool completed = SpinWait.SpinUntil(
                () =>
                {
                    _uploads.Pump();
                    return load.IsCompleted;
                },
                AssetGpuUploadHarness.Timeout
            );
            Assert.True(completed, "The built-in bloom assets did not finish loading.");
            _bloomAssets = load.GetAwaiter().GetResult();
            _bloomCatalog = catalog;
            return _bloomAssets;
        }
        catch
        {
            catalog.Stop();
            throw;
        }
    }

    /// <summary>Recreates every live GPU asset from its retained upload after device recovery.</summary>
    internal void RestoreAssets()
    {
        AssertNotDisposed();
        _uploads.Restore();
    }

    /// <summary>Reads one resolved linear color pixel from a runtime render target.</summary>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadRenderTargetColor(RenderTexture target, uint x, uint y)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (
            !_host.Backend.TryGetRenderTarget(target.TargetId, out VulkanRenderTarget? renderTarget)
            || renderTarget is null
        )
        {
            throw new InvalidOperationException(
                $"The render target {target.TargetId} is not live."
            );
        }

        VulkanReadback.ReadRenderTargetColorPixel(
            _host.Device,
            renderTarget,
            x,
            y,
            out float r,
            out float g,
            out float b
        );
        return (r, g, b);
    }

    /// <summary>Reads one resolved linear scene color pixel.</summary>
    /// <param name="x">The pixel column.</param>
    /// <param name="y">The pixel row.</param>
    /// <returns>The linear RGB values.</returns>
    internal (float R, float G, float B) ReadColor(uint x, uint y)
    {
        VulkanReadback.ReadSceneColorPixel(
            _host.Device,
            _host.SceneTarget,
            x,
            y,
            out float r,
            out float g,
            out float b
        );
        return (r, g, b);
    }

    /// <summary>Reads one reverse-Z scene depth pixel from the resolved depth image.</summary>
    /// <returns>The stored depth.</returns>
    internal float ReadDepth(uint x, uint y)
    {
        VulkanReadback.ReadSceneDepthPixel(_host.Device, _host.SceneTarget, x, y, out float depth);
        return depth;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _host.Backend.WaitForFrameResourcesIdle();
            _ssaoAssets?.Dispose();
            _catalog?.Stop();
            _bloomAssets?.Dispose();
            _bloomCatalog?.Stop();
            _uploads.Dispose();
        }
        finally
        {
            TickThread.Detach();
        }
    }

    private Mesh CreateMesh(string path, Vertex[] vertices, uint[] indices)
    {
        int vertexBytes = checked(vertices.Length * MeshAssetDescriptor.VERTEX_STRIDE);
        byte[] payload = new byte[vertexBytes + (indices.Length * sizeof(uint))];
        Span<byte> vertexSpan = payload.AsSpan(0, vertexBytes);
        for (int index = 0; index < vertices.Length; index++)
        {
            WriteVertex(
                vertexSpan.Slice(index * MeshAssetDescriptor.VERTEX_STRIDE),
                vertices[index]
            );
        }

        Span<byte> indexSpan = payload.AsSpan(vertexBytes);
        for (int index = 0; index < indices.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                indexSpan[(index * sizeof(uint))..],
                indices[index]
            );
        }

        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        foreach (Vertex vertex in vertices)
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }

        MeshAssetDescriptor descriptor = new()
        {
            Payload = path + "-mesh",
            VertexCount = (uint)vertices.Length,
            IndexCount = (uint)indices.Length,
            Bounds = [minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z],
            Topology = RayBork.PrimitiveTopology.TriangleList,
        };
        GpuAssetResource resource = _uploads.Upload(new MeshGpuUpload(path, descriptor, payload));
        return new Mesh(
            CreateAsset(path, resource),
            descriptor,
            vertices,
            indices,
            isProcedural: true
        );
    }

    private static void WriteVertex(Span<byte> destination, Vertex vertex)
    {
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
        if (offset != MeshAssetDescriptor.VERTEX_STRIDE)
        {
            throw new InvalidOperationException(
                $"The vertex layout wrote {offset} byte(s) instead of {MeshAssetDescriptor.VERTEX_STRIDE}."
            );
        }
    }

    private static void WriteVector2(Span<byte> destination, ref int offset, Vector2 value)
    {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
    }

    private static void WriteVector3(Span<byte> destination, ref int offset, Vector3 value)
    {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
        WriteSingle(destination, ref offset, value.Z);
    }

    private static void WriteSingle(Span<byte> destination, ref int offset, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);
        offset += sizeof(float);
    }

    private static byte[] CompileStage(
        string source,
        ShaderAbiLayout layout,
        ShaderStageMask stage,
        ShaderKind kind,
        SpirvExecutionModel executionModel,
        string fileName
    )
    {
        if (
            !ShaderPrologueGenerator.TryBuild(
                source,
                layout,
                stage,
                out string wrapped,
                out string problem
            )
        )
        {
            throw new InvalidOperationException($"The {fileName} prologue failed: {problem}");
        }

        if (
            !GlslCompiler.TryCompile(
                wrapped,
                fileName,
                kind,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                out byte[] spirV,
                out string message
            )
        )
        {
            throw new InvalidOperationException($"The {fileName} stage did not compile: {message}");
        }

        string entryPoint =
            stage == ShaderStageMask.Vertex
                ? ShaderAbiNames.VERTEX_ENTRY_POINT
                : ShaderAbiNames.FRAGMENT_ENTRY_POINT;
        if (
            !SpirvEntryPointRewriter.TryRename(
                spirV,
                executionModel,
                ShaderAbiNames.GLSL_ENTRY_POINT,
                entryPoint,
                out byte[] renamed,
                out string renameMessage
            )
        )
        {
            throw new InvalidOperationException(
                $"The {fileName} entry point could not be renamed: {renameMessage}"
            );
        }

        return renamed;
    }

    private EngineAsset CreateAsset(string path, GpuAssetResource resource)
    {
        // Wire the production destruction path so a public Dispose queues the GPU destruction on the
        // bridge exactly as the catalog does.
        EngineAsset asset = new("/" + path, resource, _uploads.Bridge.Destroy, static () => { });
        asset.AddOwnerReference();
        return asset;
    }

    private void AssertNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>A camera value snapshot used by <see cref="CreateFrame"/>.</summary>
    internal readonly record struct CameraView(
        Matrix4x4 View,
        Matrix4x4 Projection,
        Matrix4x4 ViewProjection,
        Vector3 Position,
        int Width,
        int Height
    );

    /// <summary>One camera pass for <see cref="CreateFrameWithPasses"/>.</summary>
    internal sealed record PassSpec(
        CameraView Camera,
        int ViewportX,
        int ViewportY,
        int ViewportWidth,
        int ViewportHeight,
        ClearMode ClearMode,
        Color ClearColor,
        DrawSpec[] Draws,
        long TargetId = 0,
        PostProcessEffectSnapshot[]? PostProcessEffects = null
    );

    /// <summary>One draw record for <see cref="CreateFrame"/>.</summary>
    internal sealed record DrawSpec(
        Mesh Mesh,
        Shader Shader,
        Matrix4x4[] WorldMatrices,
        RenderState? RenderState = null,
        Color? Tint = null,
        byte[]? InstanceTemplate = null,
        Texture?[]? TextureBindings = null
    )
    {
        /// <summary>Creates a one-instance draw.</summary>
        /// <returns>The draw specification.</returns>
        internal static DrawSpec Single(Mesh mesh, Shader shader, Matrix4x4? world = null) =>
            new(mesh, shader, [world ?? Matrix4x4.Identity]);
    }

    /// <summary>Copies a shader cursor's instance template and texture bindings.</summary>
    /// <returns>The captured instance template and bindings.</returns>
    internal static (byte[] Template, Texture?[] Textures) SnapshotCursor(Shader shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        byte[] template = shader.InstanceData.ToArray();
        Texture?[] textures = shader.TextureValues.ToArray();
        return (template, textures);
    }
}
