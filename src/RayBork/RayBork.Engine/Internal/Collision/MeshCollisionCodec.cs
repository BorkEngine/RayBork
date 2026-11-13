using System.Buffers.Binary;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Encodes and decodes the baked ray-mesh collision payload: a fixed header, the BVH nodes, and the
/// canonical triangle ids.
/// </summary>
/// <remarks>
/// <para>
/// The payload is self-contained and little-endian. The 24-byte header is the ASCII magic
/// <c>RBVC</c>, a 32-bit format version, the <see cref="PrimitiveTopology"/> as a 32-bit value, and
/// three 32-bit counts: the node count, the triangle-id count, and the canonical triangle count. The
/// header is followed by <see cref="NODE_LENGTH"/>-byte node entries and then the 32-bit triangle
/// ids; there is no alignment padding between fields.
/// </para>
/// <para>
/// A node entry is six little-endian floats (minimum XYZ, then maximum XYZ) followed by the 32-bit
/// integers <see cref="MeshCollisionNode.LeftOrFirst"/> and <see cref="MeshCollisionNode.Count"/>.
/// The canonical triangle count includes the degenerate triangles the builder omitted, and every
/// stored triangle id is below it.
/// </para>
/// <para>
/// The decoder validates the header, the exact payload length, the topology, and the canonical
/// triangle count with checked arithmetic before it allocates, then constructs a
/// <see cref="MeshCollisionData"/> whose constructor validates the tree shape, node ranges, bounds,
/// depth, and triangle ids. Every format or structure failure is an
/// <see cref="AssetLoadException"/> that names the mesh path, so a corrupt payload fails the mesh
/// load instead of reaching a query.
/// </para>
/// <para>
/// The format is internal to the engine and the bake, but the magic, version, and offsets are stable
/// for the pack-v1 compatibility contract and the codec fuzz tests. A format change increments
/// <see cref="CURRENT_VERSION"/>.
/// </para>
/// </remarks>
internal static class MeshCollisionCodec {
    /// <summary>The magic byte length.</summary>
    internal const int MAGIC_LENGTH = 4;

    /// <summary>The fixed header length in bytes.</summary>
    internal const int HEADER_LENGTH = 24;

    /// <summary>The fixed node entry length in bytes; it matches <see cref="MeshCollisionNode"/>.</summary>
    internal const int NODE_LENGTH = 32;

    /// <summary>The triangle-id field length in bytes.</summary>
    internal const int TRIANGLE_ID_LENGTH = sizeof(int);

    /// <summary>The only supported payload format version.</summary>
    internal const uint CURRENT_VERSION = 1;

    /// <summary>The header offset of the magic bytes.</summary>
    internal const int MAGIC_OFFSET = 0;

    /// <summary>The header offset of the format version.</summary>
    internal const int VERSION_OFFSET = 4;

    /// <summary>The header offset of the topology field.</summary>
    internal const int TOPOLOGY_OFFSET = 8;

    /// <summary>The header offset of the node count.</summary>
    internal const int NODE_COUNT_OFFSET = 12;

    /// <summary>The header offset of the triangle-id count.</summary>
    internal const int TRIANGLE_ID_COUNT_OFFSET = 16;

    /// <summary>The header offset of the canonical triangle count.</summary>
    internal const int TRIANGLE_COUNT_OFFSET = 20;

    /// <summary>The ASCII payload magic, <c>RBVC</c>.</summary>
    internal static ReadOnlySpan<byte> MagicBytes => "RBVC"u8;

    /// <summary>
    /// Encodes one collision structure as a self-contained payload.
    /// </summary>
    /// <param name="data">The frozen collision structure to encode.</param>
    /// <returns>The header, the node entries, and the triangle ids.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <c>null</c>.</exception>
    /// <exception cref="OverflowException">The structure is too large to address as one payload.</exception>
    internal static byte[] Encode(MeshCollisionData data) {
        ArgumentNullException.ThrowIfNull(data);

        ReadOnlySpan<MeshCollisionNode> nodes = data.Nodes;
        ReadOnlySpan<int> triangleIds = data.TriangleIds;
        int byteCount = checked(
            HEADER_LENGTH + (nodes.Length * NODE_LENGTH) + (triangleIds.Length * TRIANGLE_ID_LENGTH));
        byte[] payload = new byte[byteCount];
        Span<byte> destination = payload;
        MagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[VERSION_OFFSET..], CURRENT_VERSION);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[TOPOLOGY_OFFSET..], (uint)data.Topology);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[NODE_COUNT_OFFSET..], (uint)nodes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[TRIANGLE_ID_COUNT_OFFSET..],
            (uint)triangleIds.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[TRIANGLE_COUNT_OFFSET..],
            (uint)data.TriangleCount);

        int offset = HEADER_LENGTH;
        foreach (MeshCollisionNode node in nodes) {
            WriteVector3(destination, ref offset, node.Bounds.Min);
            WriteVector3(destination, ref offset, node.Bounds.Max);
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], node.LeftOrFirst);
            offset += sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], node.Count);
            offset += sizeof(int);
        }

        foreach (int triangleId in triangleIds) {
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], triangleId);
            offset += TRIANGLE_ID_LENGTH;
        }

        return payload;
    }

    /// <summary>
    /// Decodes one collision payload and validates it against the mesh that references it.
    /// </summary>
    /// <param name="payload">The complete collision payload bytes.</param>
    /// <param name="expectedTopology">The topology of the mesh that references the payload.</param>
    /// <param name="expectedIndexCount">The 32-bit index count of that mesh.</param>
    /// <param name="meshPath">
    /// The canonical mesh descriptor path, without a leading slash, used in the failure message.
    /// </param>
    /// <returns>The validated collision structure.</returns>
    /// <exception cref="ArgumentException"><paramref name="meshPath"/> is empty.</exception>
    /// <exception cref="AssetLoadException">
    /// The payload is truncated or corrupt, its topology does not match the mesh, its canonical
    /// triangle count does not match the mesh's index count, or the structure it describes is
    /// invalid.
    /// </exception>
    internal static MeshCollisionData Decode(
        ReadOnlySpan<byte> payload,
        PrimitiveTopology expectedTopology,
        int expectedIndexCount,
        string meshPath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshPath);

        if (payload.Length < HEADER_LENGTH) {
            throw CreateFailure(
                meshPath,
                $"the collision payload is {payload.Length} byte(s); the {HEADER_LENGTH}-byte header "
                    + "is truncated");
        }

        if (!payload[..MAGIC_LENGTH].SequenceEqual(MagicBytes)) {
            throw CreateFailure(
                meshPath,
                "the collision payload does not start with the RBVC magic");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload[VERSION_OFFSET..]);
        if (version != CURRENT_VERSION) {
            throw CreateFailure(
                meshPath,
                $"the collision payload format version {version} is not supported; the only version "
                    + $"is {CURRENT_VERSION}");
        }

        if (expectedTopology is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)) {
            throw CreateFailure(
                meshPath,
                $"the mesh topology {expectedTopology} is not a triangle topology");
        }

        uint topologyValue = BinaryPrimitives.ReadUInt32LittleEndian(payload[TOPOLOGY_OFFSET..]);
        PrimitiveTopology topology = (PrimitiveTopology)topologyValue;
        if (!Enum.IsDefined(topology)) {
            throw CreateFailure(
                meshPath,
                $"the collision payload topology {topologyValue} is not a defined primitive topology");
        }

        if (topology != expectedTopology) {
            throw CreateFailure(
                meshPath,
                $"the collision payload topology {topology} does not match the mesh topology "
                    + $"{expectedTopology}");
        }

        uint nodeCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[NODE_COUNT_OFFSET..]);
        uint triangleIdCount = BinaryPrimitives.ReadUInt32LittleEndian(
            payload[TRIANGLE_ID_COUNT_OFFSET..]);
        uint triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[TRIANGLE_COUNT_OFFSET..]);

        long expectedLength = HEADER_LENGTH
            + ((long)nodeCount * NODE_LENGTH)
            + ((long)triangleIdCount * TRIANGLE_ID_LENGTH);
        if (expectedLength != payload.Length) {
            throw CreateFailure(
                meshPath,
                $"the collision payload is {payload.Length} byte(s); the header requires exactly "
                    + $"{expectedLength} byte(s) for {nodeCount} nodes and {triangleIdCount} "
                    + "triangle ids");
        }

        int canonicalCount = MeshCollisionData.GetTriangleCount(expectedTopology, expectedIndexCount);
        if (triangleCount != (uint)canonicalCount) {
            throw CreateFailure(
                meshPath,
                $"the collision payload declares {triangleCount} canonical triangle(s); the mesh's "
                    + $"{expectedIndexCount} indices describe {canonicalCount}");
        }

        if (nodeCount == 0 || (nodeCount & 1u) == 0) {
            throw CreateFailure(
                meshPath,
                $"the collision payload declares {nodeCount} nodes; a binary tree has a positive odd "
                    + "node count");
        }

        if (triangleIdCount == 0) {
            throw CreateFailure(meshPath, "the collision payload declares no triangle ids");
        }

        if (triangleIdCount > triangleCount) {
            throw CreateFailure(
                meshPath,
                $"the collision payload stores {triangleIdCount} triangle id(s); the canonical "
                    + $"triangle count is {triangleCount}");
        }

        int nodeLength = checked((int)nodeCount);
        int triangleIdLength = checked((int)triangleIdCount);
        MeshCollisionNode[] nodes = new MeshCollisionNode[nodeLength];
        int offset = HEADER_LENGTH;
        for (int index = 0; index < nodes.Length; index++) {
            Vector3 minimum = ReadVector3(payload, ref offset);
            Vector3 maximum = ReadVector3(payload, ref offset);
            int leftOrFirst = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);
            nodes[index] = new MeshCollisionNode(
                new BoundingBox(minimum, maximum),
                leftOrFirst,
                count);
        }

        int[] triangleIds = new int[triangleIdLength];
        for (int index = 0; index < triangleIds.Length; index++) {
            triangleIds[index] = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += TRIANGLE_ID_LENGTH;
        }

        try {
            return new MeshCollisionData(nodes, triangleIds, topology, (int)triangleCount);
        } catch (ArgumentException exception) {
            throw CreateFailure(
                meshPath,
                $"the collision structure is invalid: {exception.Message}");
        }
    }

    private static AssetLoadException CreateFailure(string meshPath, string reason)
        => AssetLoadErrors.CreateLoadFailure(meshPath, reason);

    private static void WriteVector3(Span<byte> destination, ref int offset, Vector3 value) {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.X);
        offset += sizeof(float);
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.Y);
        offset += sizeof(float);
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.Z);
        offset += sizeof(float);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> source, ref int offset) {
        float x = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        float y = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        float z = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        return new Vector3(x, y, z);
    }
}
