using System.Buffers.Binary;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>Encodes and decodes the baked ray-mesh collision payload.</summary>
/// <remarks>
/// The little-endian payload is a 24-byte header (magic <c>RBVC</c>, version, topology, node count,
/// triangle-id count, canonical triangle count), then <see cref="NODE_LENGTH"/>-byte nodes (six
/// floats, then two 32-bit integers), then the 32-bit triangle ids, with no alignment padding. A
/// malformed payload fails the load with an <see cref="AssetLoadException"/> naming the mesh path.
/// </remarks>
internal static class MeshCollisionCodec
{
    /// <summary>The magic byte length.</summary>
    internal const int MAGIC_LENGTH = 4;

    /// <summary>The fixed header length in bytes.</summary>
    internal const int HEADER_LENGTH = 24;

    /// <summary>
    /// The fixed node entry length in bytes; it matches <see cref="MeshCollisionNode"/>.
    /// </summary>
    internal const int NODE_LENGTH = 32;

    /// <summary>The triangle-id field length in bytes.</summary>
    internal const int TRIANGLE_ID_LENGTH = sizeof(int);

    /// <summary>The only supported payload format version; changes increment it.</summary>
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

    /// <summary>Encodes one collision structure as a self-contained payload.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <c>null</c>.</exception>
    /// <exception cref="OverflowException">The payload size overflows.</exception>
    internal static byte[] Encode(MeshCollisionData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        ReadOnlySpan<MeshCollisionNode> nodes = data.Nodes;
        ReadOnlySpan<int> triangleIds = data.TriangleIds;
        int byteCount = checked(
            HEADER_LENGTH + (nodes.Length * NODE_LENGTH) + (triangleIds.Length * TRIANGLE_ID_LENGTH)
        );
        byte[] payload = new byte[byteCount];
        Span<byte> destination = payload;
        MagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[VERSION_OFFSET..], CURRENT_VERSION);
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[TOPOLOGY_OFFSET..],
            (uint)data.Topology
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[NODE_COUNT_OFFSET..],
            (uint)nodes.Length
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[TRIANGLE_ID_COUNT_OFFSET..],
            (uint)triangleIds.Length
        );
        BinaryPrimitives.WriteUInt32LittleEndian(
            destination[TRIANGLE_COUNT_OFFSET..],
            (uint)data.TriangleCount
        );

        int offset = HEADER_LENGTH;
        foreach (MeshCollisionNode node in nodes)
        {
            WriteVector3(destination, ref offset, node.Bounds.Min);
            WriteVector3(destination, ref offset, node.Bounds.Max);
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], node.LeftOrFirst);
            offset += sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], node.Count);
            offset += sizeof(int);
        }

        foreach (int triangleId in triangleIds)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], triangleId);
            offset += TRIANGLE_ID_LENGTH;
        }

        return payload;
    }

    /// <summary>Decodes and validates one collision payload against the referencing mesh.</summary>
    /// <exception cref="ArgumentException"><paramref name="meshPath"/> is empty.</exception>
    /// <exception cref="AssetLoadException">
    /// The payload is truncated or corrupt, its topology or triangle count disagrees with the mesh,
    /// or the structure it describes is invalid.
    /// </exception>
    internal static MeshCollisionData Decode(
        ReadOnlySpan<byte> payload,
        PrimitiveTopology expectedTopology,
        int expectedIndexCount,
        string meshPath
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meshPath);

        if (payload.Length < HEADER_LENGTH)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload is {payload.Length} byte(s); the {HEADER_LENGTH}-byte header "
                    + "is truncated"
            );
        }

        if (!payload[..MAGIC_LENGTH].SequenceEqual(MagicBytes))
        {
            throw CreateFailure(
                meshPath,
                "the collision payload does not start with the RBVC magic"
            );
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload[VERSION_OFFSET..]);
        if (version != CURRENT_VERSION)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload format version {version} is not supported; the only version "
                    + $"is {CURRENT_VERSION}"
            );
        }

        if (
            expectedTopology
            is not (PrimitiveTopology.TriangleList or PrimitiveTopology.TriangleStrip)
        )
        {
            throw CreateFailure(
                meshPath,
                $"the mesh topology {expectedTopology} is not a triangle topology"
            );
        }

        uint topologyValue = BinaryPrimitives.ReadUInt32LittleEndian(payload[TOPOLOGY_OFFSET..]);
        PrimitiveTopology topology = (PrimitiveTopology)topologyValue;
        if (!Enum.IsDefined(topology))
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload topology {topologyValue} is not a defined primitive topology"
            );
        }

        if (topology != expectedTopology)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload topology {topology} does not match the mesh topology "
                    + $"{expectedTopology}"
            );
        }

        uint nodeCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[NODE_COUNT_OFFSET..]);
        uint triangleIdCount = BinaryPrimitives.ReadUInt32LittleEndian(
            payload[TRIANGLE_ID_COUNT_OFFSET..]
        );
        uint triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(
            payload[TRIANGLE_COUNT_OFFSET..]
        );

        long expectedLength =
            HEADER_LENGTH
            + ((long)nodeCount * NODE_LENGTH)
            + ((long)triangleIdCount * TRIANGLE_ID_LENGTH);
        if (expectedLength != payload.Length)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload is {payload.Length} byte(s); the header requires exactly "
                    + $"{expectedLength} byte(s) for {nodeCount} nodes and {triangleIdCount} "
                    + "triangle ids"
            );
        }

        int canonicalCount = MeshCollisionData.GetTriangleCount(
            expectedTopology,
            expectedIndexCount
        );
        if (triangleCount != (uint)canonicalCount)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload declares {triangleCount} canonical triangle(s); the mesh's "
                    + $"{expectedIndexCount} indices describe {canonicalCount}"
            );
        }

        if (nodeCount == 0 || (nodeCount & 1u) == 0)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload declares {nodeCount} nodes; a binary tree has a positive odd "
                    + "node count"
            );
        }

        if (triangleIdCount == 0)
        {
            throw CreateFailure(meshPath, "the collision payload declares no triangle ids");
        }

        if (triangleIdCount > triangleCount)
        {
            throw CreateFailure(
                meshPath,
                $"the collision payload stores {triangleIdCount} triangle id(s); the canonical "
                    + $"triangle count is {triangleCount}"
            );
        }

        int nodeLength = checked((int)nodeCount);
        int triangleIdLength = checked((int)triangleIdCount);
        MeshCollisionNode[] nodes = new MeshCollisionNode[nodeLength];
        int offset = HEADER_LENGTH;
        for (int index = 0; index < nodes.Length; index++)
        {
            Vector3 minimum = ReadVector3(payload, ref offset);
            Vector3 maximum = ReadVector3(payload, ref offset);
            int leftOrFirst = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);
            nodes[index] = new MeshCollisionNode(
                new BoundingBox(minimum, maximum),
                leftOrFirst,
                count
            );
        }

        int[] triangleIds = new int[triangleIdLength];
        for (int index = 0; index < triangleIds.Length; index++)
        {
            triangleIds[index] = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += TRIANGLE_ID_LENGTH;
        }

        try
        {
            return new MeshCollisionData(nodes, triangleIds, topology, (int)triangleCount);
        }
        catch (ArgumentException exception)
        {
            throw CreateFailure(
                meshPath,
                $"the collision structure is invalid: {exception.Message}"
            );
        }
    }

    private static AssetLoadException CreateFailure(string meshPath, string reason) =>
        AssetLoadErrors.CreateLoadFailure(meshPath, reason);

    private static void WriteVector3(Span<byte> destination, ref int offset, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.X);
        offset += sizeof(float);
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.Y);
        offset += sizeof(float);
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value.Z);
        offset += sizeof(float);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> source, ref int offset)
    {
        float x = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        float y = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        float z = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += sizeof(float);
        return new Vector3(x, y, z);
    }
}
