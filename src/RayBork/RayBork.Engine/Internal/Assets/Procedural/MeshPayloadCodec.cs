using System.Buffers.Binary;

namespace RayBork.Internal;

/// <summary>
/// Encodes and decodes the fixed engine mesh payload: 88-byte little-endian vertices followed by
/// 32-bit little-endian indices.
/// </summary>
/// <remarks>
/// <para>
/// The bake writes this layout with a <see cref="BinaryWriter"/> and the runtime reads it here, so the
/// byte order is explicit and does not depend on the platform. Each vertex is written in the field
/// order of <see cref="Vertex"/>: position, normal, uv, uv1, uv2, uv3, color, and tangent. There is no
/// alignment padding inside or between vertices.
/// </para>
/// <para>
/// The loaded and procedural paths share the codec, so a procedural mesh payload and a baked mesh
/// payload are byte-identical for the same vertex data. The caller owns every returned array.
/// </para>
/// </remarks>
internal static class MeshPayloadCodec {
    private const int FLOAT_SIZE = sizeof(float);

    /// <summary>The fixed vertex stride in bytes.</summary>
    internal const int VERTEX_STRIDE = MeshAssetDescriptor.VERTEX_STRIDE;

    /// <summary>Encodes vertices and indices as one mesh payload.</summary>
    /// <param name="vertices">The vertices in the fixed layout.</param>
    /// <param name="indices">The 32-bit indices.</param>
    /// <returns>The vertex stream followed by the index stream.</returns>
    internal static byte[] Encode(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices) {
        int byteCount = checked((vertices.Length * VERTEX_STRIDE) + (indices.Length * sizeof(uint)));
        byte[] payload = new byte[byteCount];
        Span<byte> destination = payload;
        int offset = 0;
        foreach (Vertex vertex in vertices) {
            WriteVertex(destination, ref offset, vertex);
        }

        foreach (uint index in indices) {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], index);
            offset += sizeof(uint);
        }

        return payload;
    }

    /// <summary>Decodes the vertex stream of one mesh payload.</summary>
    /// <param name="payload">The complete payload, vertices first.</param>
    /// <param name="vertexCount">The number of vertices the descriptor declares.</param>
    /// <returns>The decoded vertices.</returns>
    /// <exception cref="ArgumentException">
    /// The payload is shorter than <paramref name="vertexCount"/> vertices.
    /// </exception>
    internal static Vertex[] DecodeVertices(ReadOnlySpan<byte> payload, int vertexCount) {
        if (payload.Length < (long)vertexCount * VERTEX_STRIDE) {
            throw new ArgumentException(
                $"The mesh payload is {payload.Length} bytes; {vertexCount} vertices require "
                    + $"{vertexCount * VERTEX_STRIDE}.",
                nameof(payload));
        }

        Vertex[] vertices = new Vertex[vertexCount];
        int offset = 0;
        for (int index = 0; index < vertexCount; index++) {
            vertices[index] = ReadVertex(payload, ref offset);
        }

        return vertices;
    }

    /// <summary>Decodes the index stream of one mesh payload.</summary>
    /// <param name="payload">The complete payload, vertices first.</param>
    /// <param name="vertexCount">The number of vertices that precede the indices.</param>
    /// <param name="indexCount">The number of indices the descriptor declares.</param>
    /// <returns>The decoded 32-bit indices.</returns>
    /// <exception cref="ArgumentException">
    /// The payload is shorter than the declared vertices and indices.
    /// </exception>
    internal static uint[] DecodeIndices(ReadOnlySpan<byte> payload, int vertexCount, int indexCount) {
        long vertexBytes = (long)vertexCount * VERTEX_STRIDE;
        if (payload.Length < vertexBytes + ((long)indexCount * sizeof(uint))) {
            throw new ArgumentException(
                $"The mesh payload is {payload.Length} bytes; {indexCount} indices after "
                    + $"{vertexCount} vertices require {vertexBytes + ((long)indexCount * sizeof(uint))}.",
                nameof(payload));
        }

        uint[] indices = new uint[indexCount];
        int offset = (int)vertexBytes;
        for (int index = 0; index < indexCount; index++) {
            indices[index] = BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);
            offset += sizeof(uint);
        }

        return indices;
    }

    private static void WriteVertex(Span<byte> destination, ref int offset, Vertex vertex) {
        WriteVector3(destination, ref offset, vertex.Position);
        WriteVector3(destination, ref offset, vertex.Normal);
        WriteVector2(destination, ref offset, vertex.Uv);
        WriteVector2(destination, ref offset, vertex.Uv1);
        WriteVector2(destination, ref offset, vertex.Uv2);
        WriteVector2(destination, ref offset, vertex.Uv3);
        WriteVector4(destination, ref offset, vertex.Color);
        WriteVector4(destination, ref offset, vertex.Tangent);
    }

    private static Vertex ReadVertex(ReadOnlySpan<byte> source, ref int offset) => new(
        ReadVector3(source, ref offset),
        ReadVector3(source, ref offset),
        ReadVector2(source, ref offset),
        ReadVector2(source, ref offset),
        ReadVector2(source, ref offset),
        ReadVector2(source, ref offset),
        ReadColor(source, ref offset),
        ReadVector4(source, ref offset));

    private static void WriteVector2(Span<byte> destination, ref int offset, System.Numerics.Vector2 value) {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
    }

    private static void WriteVector3(Span<byte> destination, ref int offset, System.Numerics.Vector3 value) {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
        WriteSingle(destination, ref offset, value.Z);
    }

    private static void WriteVector4(Span<byte> destination, ref int offset, System.Numerics.Vector4 value) {
        WriteSingle(destination, ref offset, value.X);
        WriteSingle(destination, ref offset, value.Y);
        WriteSingle(destination, ref offset, value.Z);
        WriteSingle(destination, ref offset, value.W);
    }

    private static void WriteSingle(Span<byte> destination, ref int offset, float value) {
        BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);
        offset += FLOAT_SIZE;
    }

    private static System.Numerics.Vector2 ReadVector2(ReadOnlySpan<byte> source, ref int offset) => new(
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset));

    private static System.Numerics.Vector3 ReadVector3(ReadOnlySpan<byte> source, ref int offset) => new(
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset));

    private static System.Numerics.Vector4 ReadVector4(ReadOnlySpan<byte> source, ref int offset) => new(
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset));

    private static Color ReadColor(ReadOnlySpan<byte> source, ref int offset) => new(
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset),
        ReadSingle(source, ref offset));

    private static float ReadSingle(ReadOnlySpan<byte> source, ref int offset) {
        float value = BinaryPrimitives.ReadSingleLittleEndian(source[offset..]);
        offset += FLOAT_SIZE;
        return value;
    }
}
