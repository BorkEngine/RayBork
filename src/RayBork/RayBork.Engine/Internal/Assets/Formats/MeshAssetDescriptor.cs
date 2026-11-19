using PolyType;

namespace RayBork.Internal;

/// <summary>The packed descriptor of a mesh asset.</summary>
/// <remarks>
/// The payload is a fixed 88-byte vertex stream (position, normal, uv, uv1, uv2, uv3, color,
/// tangent) followed by 32-bit indices in a sibling raw entry. <see cref="Bounds"/> carries minimum
/// XYZ then maximum XYZ.
/// </remarks>
[GenerateShape]
internal sealed partial class MeshAssetDescriptor
{
    /// <summary>The fixed vertex stride in bytes.</summary>
    internal const int VERTEX_STRIDE = 88;

    /// <summary>Gets the canonical path of the mesh payload entry.</summary>
    public required string Payload { get; init; }

    /// <summary>Gets the collision payload path, or null when the mesh has no baked collision.</summary>
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
