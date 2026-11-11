using System.Text;

namespace RayBork.Bake.Models;

/// <summary>
/// Writes the fixed engine mesh payload: 88-byte vertices followed by 32-bit indices.
/// </summary>
internal static class VertexLayoutWriter {
    /// <summary>The fixed vertex stride in bytes.</summary>
    internal const int VERTEX_STRIDE = 88;

    /// <summary>Serializes one mesh payload little-endian.</summary>
    /// <param name="mesh">The validated mesh.</param>
    /// <returns>The vertex bytes followed by the index bytes.</returns>
    /// <exception cref="ModelReadException">A count is too large or an index is out of range.</exception>
    internal static byte[] Write(MeshData mesh) {
        ArgumentNullException.ThrowIfNull(mesh);

        int vertexCount = mesh.Vertices.Count;
        int indexCount = mesh.Indices.Count;
        if (vertexCount == 0) {
            throw new ModelReadException("A mesh has no vertices.");
        }

        if ((long)vertexCount * VERTEX_STRIDE + (long)indexCount * sizeof(uint) > int.MaxValue) {
            throw new ModelReadException("A mesh payload is larger than the bake can address.");
        }

        for (int index = 0; index < indexCount; index++) {
            if (mesh.Indices[index] >= (uint)vertexCount) {
                throw new ModelReadException(
                    $"A mesh index {mesh.Indices[index]} is outside its {vertexCount} vertices.");
            }
        }

        int byteCount = vertexCount * VERTEX_STRIDE + indexCount * sizeof(uint);
        var payload = new byte[byteCount];
        var writer = new BinaryWriter(new MemoryStream(payload), Encoding.UTF8, leaveOpen: true);
        try {
            foreach (MeshVertex vertex in mesh.Vertices) {
                WriteVector3(writer, vertex.Position);
                WriteVector3(writer, vertex.Normal);
                WriteVector2(writer, vertex.Uv);
                WriteVector2(writer, vertex.Uv1);
                WriteVector2(writer, vertex.Uv2);
                WriteVector2(writer, vertex.Uv3);
                WriteVector4(writer, vertex.Color);
                WriteVector4(writer, vertex.Tangent);
            }

            foreach (uint index in mesh.Indices) {
                writer.Write(index);
            }
        } finally {
            writer.Dispose();
        }

        return payload;
    }

    private static void WriteVector2(BinaryWriter writer, System.Numerics.Vector2 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
    }

    private static void WriteVector3(BinaryWriter writer, System.Numerics.Vector3 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void WriteVector4(BinaryWriter writer, System.Numerics.Vector4 value) {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
        writer.Write(value.W);
    }
}
