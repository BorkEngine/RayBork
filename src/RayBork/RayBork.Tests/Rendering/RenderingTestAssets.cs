using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Builds detached engine assets for camera, model, and batch-planner unit tests without a pack or a
/// GPU device.
/// </summary>
internal static class RenderingTestAssets {
    internal const string MESH_PATH = "meshes/test";
    internal const string SHADER_PATH = "shaders/test";
    internal const string TEXTURE_PATH = "textures/test";
    internal const string MODEL_PATH = "models/test";
    internal const string VALUE_UNIFORM = "uValue";
    internal const string TEXTURE_BINDING = "mainTexture";

    internal static Mesh CreateMesh(
        string path = MESH_PATH,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList,
        int indexCount = 6,
        BoundingBox? bounds = null) {
        MeshAssetDescriptor descriptor = new() {
            Payload = path + "-mesh",
            VertexCount = 3,
            IndexCount = (uint)indexCount,
            Bounds = BoundsToArray(bounds ?? new BoundingBox(new Vector3(-1f), new Vector3(1f))),
            Topology = topology,
        };
        var upload = new MeshGpuUpload(path, descriptor, ReadOnlyMemory<byte>.Empty);
        var asset = new EngineAsset(path, new FakeGpuAssetResource(upload, 1), static _ => { }, static () => { });
        asset.AddOwnerReference();
        Vertex[] vertices = [new Vertex(Vector3.Zero)];
        uint[] indices = new uint[indexCount];
        return new Mesh(asset, descriptor, vertices, indices, isProcedural: true);
    }

    internal static ShaderProgram CreateProgram(
        string path = SHADER_PATH,
        RenderState? defaultRenderState = null,
        float defaultValue = 0f,
        ShaderStageMask stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
        List<GpuAssetResource>? destroyed = null) {
        ShaderAssetDescriptor descriptor = new() {
            VertexPayload = path + "-vert",
            FragmentPayload = path + "-frag",
            VertexEntry = "main_vertex",
            FragmentEntry = "main_fragment",
            Stages = stages,
            DefaultRenderState = defaultRenderState ?? RenderState.Opaque,
            Uniforms = [
                new ShaderUniformAsset {
                    Name = VALUE_UNIFORM,
                    Type = ShaderUniformType.Float,
                    Default = [defaultValue],
                },
            ],
            Textures = [TEXTURE_BINDING],
        };
        var upload = new ShaderGpuUpload(path, descriptor, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);
        Action<GpuAssetResource> queueDestruction = destroyed is null ? static _ => { } : destroyed.Add;
        var asset = new EngineAsset(
            path,
            new FakeGpuAssetResource(upload, 1),
            queueDestruction,
            static () => { });
        asset.AddOwnerReference();
        return new ShaderProgram(asset, descriptor);
    }

    internal static Shader CreateShader(
        string path = SHADER_PATH,
        RenderState? defaultRenderState = null,
        float defaultValue = 0f,
        ShaderStageMask stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
        List<GpuAssetResource>? destroyed = null) {
        ShaderProgram program = CreateProgram(path, defaultRenderState, defaultValue, stages, destroyed);
        Shader material = program.CreateMaterial();
        // The helper's reference only stood in for the catalog's cache reference; the material owns
        // the program from here on.
        program.Lifetime.ReleaseOwnerReference();
        return material;
    }

    internal static void ReleaseProgram(ShaderProgram program) {
        ArgumentNullException.ThrowIfNull(program);
        try {
            program.Lifetime.ReleaseOwnerReference();
        } catch (ObjectDisposedException) {
            // Shutdown-style force disposal already removed the helper's reference.
        }
    }

    internal static Texture CreateTexture(string path = TEXTURE_PATH, List<GpuAssetResource>? destroyed = null) {
        TextureAssetDescriptor descriptor = new() {
            Payload = path + "-ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Srgb,
            IsSrgb = true,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = 1f,
        };
        var upload = new TextureGpuUpload(path, descriptor, ReadOnlyMemory<byte>.Empty);
        Action<GpuAssetResource> queueDestruction = destroyed is null ? static _ => { } : destroyed.Add;
        var asset = new EngineAsset(
            path,
            new FakeGpuAssetResource(upload, 1),
            queueDestruction,
            static () => { });
        asset.AddOwnerReference();
        return new Texture(asset, descriptor);
    }

    internal static Model CreateModel(
        string path = MODEL_PATH,
        string[]? partNames = null,
        Mesh[]? meshes = null,
        Shader? shader = null,
        Transform? localTransform = null,
        BoundingBox? bounds = null) {
        Mesh[] modelMeshes = meshes ?? [CreateMesh(path + "-mesh")];
        Shader modelShader = shader ?? CreateShader(path + "-shader");
        string[] names = partNames ?? [.. Enumerable.Range(0, modelMeshes.Length).Select(index => $"part-{index}")];
        Transform local = localTransform ?? Transform.Identity;
        BoundingBox localBounds = bounds ?? modelMeshes[0].Bounds;

        ModelPart[] parts = new ModelPart[modelMeshes.Length];
        for (int index = 0; index < modelMeshes.Length; index++) {
            parts[index] = new ModelPart(names[index], modelMeshes[index], modelShader, local, localBounds);
        }

        ModelAssetDescriptor descriptor = new() {
            Parts = [.. parts.Select(part => new ModelPartAsset {
                Name = part.Name,
                MeshPath = part.Mesh.Path.TrimStart('/'),
                ShaderPath = part.Shader.Path.TrimStart('/'),
                LocalTransform = TransformToArray(part.LocalTransform),
                Bounds = BoundsToArray(part.Bounds),
            })],
            Bounds = BoundsToArray(localBounds),
        };
        var upload = new ModelGpuUpload(path);
        var asset = new EngineAsset(
            path,
            new ModelAssetResource(upload),
            static _ => { },
            static () => { });
        asset.AddOwnerReference();
        return new Model(asset, descriptor, parts);
    }

    internal static float ReadInstanceFloat(Shader shader, ReadOnlySpan<byte> template, string uniformName) {
        ShaderAbiUniform uniform = shader.Abi.Uniforms.First(candidate => candidate.Name == uniformName);
        return BinaryPrimitives.ReadSingleLittleEndian(
            template.Slice(uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE, sizeof(float)));
    }

    internal static float[] BoundsToArray(BoundingBox bounds) => [
        bounds.Min.X,
        bounds.Min.Y,
        bounds.Min.Z,
        bounds.Max.X,
        bounds.Max.Y,
        bounds.Max.Z,
    ];

    internal static float[] TransformToArray(Transform transform) => [
        transform.Position.X,
        transform.Position.Y,
        transform.Position.Z,
        transform.Rotation.X,
        transform.Rotation.Y,
        transform.Rotation.Z,
        transform.Rotation.W,
        transform.Scale.X,
        transform.Scale.Y,
        transform.Scale.Z,
    ];
}
