using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The packed descriptor of a mesh asset. Its payload is the fixed-layout vertex stream followed by
/// 32-bit indices in a sibling raw entry.
/// </summary>
/// <remarks>
/// The fixed vertex stride is 88 bytes: position, normal, uv, uv1, uv2, uv3, color, and tangent, in
/// that order. <see cref="Bounds"/> carries six floats: minimum X/Y/Z followed by maximum X/Y/Z.
/// </remarks>
[GenerateShape]
internal sealed partial class MeshAssetDescriptor {

    /// <summary>The fixed vertex stride in bytes.</summary>
    internal const int VERTEX_STRIDE = 88;


    /// <summary>Gets the canonical path of the mesh payload entry.</summary>
    public required string Payload { get; init; }

    /// <summary>
    /// Gets the canonical path of the baked ray-mesh collision payload entry, or <see langword="null"/>
    /// when the mesh has no baked collision structure.
    /// </summary>
    /// <remarks>
    /// The bake writes one sibling raw entry for every triangle mesh unless the source sidecar opts
    /// out. A descriptor baked before collision payloads existed has no member, so it deserializes
    /// with <see langword="null"/> and the mesh loads without an acceleration structure.
    /// </remarks>
    public string? CollisionPayload { get; init; }

    /// <summary>Gets the number of vertices.</summary>
    public required uint VertexCount { get; init; }

    /// <summary>Gets the number of 32-bit indices.</summary>
    public required uint IndexCount { get; init; }

    /// <summary>Gets the mesh-space bounds as minimum X/Y/Z and maximum X/Y/Z.</summary>
    public required float[] Bounds { get; init; }

    /// <summary>Gets the primitive topology.</summary>
    public required PrimitiveTopology Topology { get; init; }
}
