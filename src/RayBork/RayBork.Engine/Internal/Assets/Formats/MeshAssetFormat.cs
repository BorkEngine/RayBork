using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// The built-in packed mesh format: it decodes the descriptor, validates the payload, and builds the
/// mesh upload and CPU geometry.
/// </summary>
/// <remarks>
/// The format is stateless, so one shared instance serves every mesh load on the loader thread. It
/// mirrors the bake layout exactly and never parses a source model format.
/// </remarks>
internal sealed class MeshAssetFormat : IEngineAssetFormat<Mesh> {
    /// <summary>
    /// The shared format instance.
    /// </summary>
    internal static readonly MeshAssetFormat Instance = new();

    private MeshAssetFormat() {
    }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Mesh;

    /// <inheritdoc/>
    public DecodedAsset<Mesh> Decode(AssetFormatContext context) {
        ArgumentNullException.ThrowIfNull(context);

        MeshAssetDescriptor descriptor = AssetSerialization.Deserialize<MeshAssetDescriptor>(
            context.DescriptorPayload);
        byte[] payload = context.ReadRawPayload(descriptor.Payload, "mesh");
        ValidateMesh(descriptor, payload, context.CanonicalPath);
        MeshCollisionData? collision = DecodeCollision(descriptor, context);
        Vertex[] vertices = MeshPayloadCodec.DecodeVertices(
            payload,
            checked((int)descriptor.VertexCount));
        uint[] indices = MeshPayloadCodec.DecodeIndices(
            payload,
            checked((int)descriptor.VertexCount),
            checked((int)descriptor.IndexCount));
        MeshAssetDescriptor captured = descriptor;
        return new DecodedAsset<Mesh>(
            new MeshGpuUpload(context.CanonicalPath, descriptor, payload),
            lifetime => new Mesh(
                lifetime,
                captured,
                vertices,
                indices,
                isProcedural: false,
                collision));
    }

    private static MeshCollisionData? DecodeCollision(
        MeshAssetDescriptor descriptor,
        AssetFormatContext context) {
        if (descriptor.CollisionPayload is null) {
            return null;
        }

        byte[] collisionPayload = context.ReadRawPayload(
            descriptor.CollisionPayload,
            "mesh collision");
        return MeshCollisionCodec.Decode(
            collisionPayload,
            descriptor.Topology,
            checked((int)descriptor.IndexCount),
            context.CanonicalPath);
    }

    private static void ValidateMesh(MeshAssetDescriptor descriptor, byte[] payload, string canonical) {
        if (descriptor.Bounds is null || descriptor.Bounds.Length != 6) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                "the mesh descriptor must carry six bounds floats (minimum XYZ, maximum XYZ)");
        }

        for (int i = 0; i < descriptor.Bounds.Length; i++) {
            if (!float.IsFinite(descriptor.Bounds[i])) {
                throw AssetLoadErrors.CreateLoadFailure(canonical, "the mesh descriptor bounds must be finite");
            }
        }

        if (descriptor.Bounds[0] > descriptor.Bounds[3]
            || descriptor.Bounds[1] > descriptor.Bounds[4]
            || descriptor.Bounds[2] > descriptor.Bounds[5]) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                "the mesh descriptor bounds minimum must not exceed the maximum");
        }

        if (descriptor.VertexCount == 0 || descriptor.IndexCount == 0) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                "the mesh descriptor must declare at least one vertex and one index");
        }

        if (!Enum.IsDefined(descriptor.Topology)) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the mesh descriptor topology {(int)descriptor.Topology} is not a defined value");
        }

        long expectedLength = (long)descriptor.VertexCount * MeshAssetDescriptor.VERTEX_STRIDE
            + (long)descriptor.IndexCount * sizeof(uint);
        if (payload.Length != expectedLength) {
            throw AssetLoadErrors.CreateLoadFailure(
                canonical,
                $"the mesh payload is {payload.Length} bytes; the descriptor requires exactly "
                    + $"{expectedLength} bytes for {descriptor.VertexCount} vertices and "
                    + $"{descriptor.IndexCount} indices");
        }

        // The CPU indices are exposed on the loaded mesh, so an out-of-range index is a corrupt payload
        // even though the GPU buffers would accept the raw bytes.
        ReadOnlySpan<byte> payloadSpan = payload;
        int indexCount = checked((int)descriptor.IndexCount);
        int indexOffset = checked((int)descriptor.VertexCount) * MeshAssetDescriptor.VERTEX_STRIDE;
        for (int index = 0; index < indexCount; index++) {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(
                payloadSpan[(indexOffset + (index * sizeof(uint)))..]);
            if (value >= descriptor.VertexCount) {
                throw AssetLoadErrors.CreateLoadFailure(
                    canonical,
                    $"the mesh index {value} at position {index} is outside the "
                        + $"{descriptor.VertexCount} vertices");
            }
        }
    }
}
