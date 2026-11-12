namespace RayBork.Internal;

/// <summary>
/// The kind of GPU resource an <see cref="AssetGpuUpload"/> creates.
/// </summary>
internal enum AssetGpuUploadKind {
    /// <summary>
    /// A texture image, view, and sampler decoded from a packed KTX2 payload.
    /// </summary>
    Texture,

    /// <summary>
    /// A texture image, view, and sampler created from the raw mip levels of a procedural texture.
    /// </summary>
    ProceduralTexture,

    /// <summary>
    /// A vertex and index buffer pair decoded from a packed mesh payload.
    /// </summary>
    Mesh,

    /// <summary>
    /// A vertex and fragment shader module pair compiled from packed SPIR-V payloads.
    /// </summary>
    Shader,
}
