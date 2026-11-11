using RayBork.Internal;

namespace RayBork.Tests.Graphics.Assets;

/// <summary>
/// Builds valid and deliberately invalid GPU asset uploads for the worker tests.
/// </summary>
/// <remarks>
/// The texture payloads come from <see cref="Ktx2TestWriter"/>; the shader modules are the committed
/// present shaders, so the tests never need a shader compiler or a golden image.
/// </remarks>
internal static class AssetUploadTestData {
    internal const string TEXTURE_PATH = "textures/test";
    internal const string MESH_PATH = "meshes/triangle";
    internal const string SHADER_PATH = "shaders/test";

    /// <summary>
    /// Creates a texture upload whose KTX2 payload matches its descriptor.
    /// </summary>
    internal static TextureGpuUpload Texture(
        TextureFormat format = TextureFormat.Rgba8Srgb,
        int width = 4,
        int height = 4,
        TextureFilter filter = TextureFilter.Linear,
        TextureWrap wrap = TextureWrap.Repeat,
        float maxAnisotropy = 1f,
        string path = TEXTURE_PATH) {
        byte[] payload = Ktx2TestWriter.Write(format, width, height);
        TextureAssetDescriptor descriptor = new() {
            Payload = path + "-ktx2",
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = (uint)Ktx2TestWriter.FullMipCount(width, height),
            Format = format,
            IsSrgb = format is TextureFormat.Rgba8Srgb or TextureFormat.Bc7Srgb,
            Filter = filter,
            Wrap = wrap,
            MaxAnisotropy = maxAnisotropy,
        };
        return new TextureGpuUpload(path, descriptor, payload);
    }

    /// <summary>
    /// Creates a mesh upload with the fixed 88-byte vertex stride and 32-bit indices.
    /// </summary>
    /// <param name="vertexCount">The number of vertices.</param>
    /// <param name="indexCount">The number of indices.</param>
    /// <param name="lengthDelta">
    /// Added to the payload length to create a mismatch the worker must reject.
    /// </param>
    /// <param name="path">The canonical asset path.</param>
    internal static MeshGpuUpload Mesh(
        int vertexCount = 3,
        int indexCount = 3,
        int lengthDelta = 0,
        string path = MESH_PATH) {
        int length = checked((vertexCount * MeshAssetDescriptor.VERTEX_STRIDE) + (indexCount * sizeof(uint)) + lengthDelta);
        byte[] payload = new byte[length];
        for (int index = 0; index < payload.Length; index++) {
            payload[index] = (byte)(index * 7);
        }

        MeshAssetDescriptor descriptor = new() {
            Payload = path + "-mesh",
            VertexCount = (uint)vertexCount,
            IndexCount = (uint)indexCount,
            Bounds = new float[6],
            Topology = PrimitiveTopology.TriangleList,
        };
        return new MeshGpuUpload(path, descriptor, payload);
    }

    /// <summary>
    /// Creates a shader upload from the committed present shader modules.
    /// </summary>
    internal static ShaderGpuUpload Shader(
        ReadOnlyMemory<byte>? vertexSpirv = null,
        ReadOnlyMemory<byte>? fragmentSpirv = null,
        string path = SHADER_PATH) {
        ShaderAssetDescriptor descriptor = new() {
            VertexPayload = path + "-vert",
            FragmentPayload = path + "-frag",
            VertexEntry = "main",
            FragmentEntry = "main",
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = [],
            Textures = [],
        };
        return new ShaderGpuUpload(
            path,
            descriptor,
            vertexSpirv ?? ShaderAssets.PresentVertex.ToArray(),
            fragmentSpirv ?? ShaderAssets.PresentFragment.ToArray());
    }
}
