using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using RayBork.Internal;

namespace RayBork.Tests.Bake.Models;

/// <summary>Source fixtures and pack readers shared by the model processor tests.</summary>
internal static class ModelTestData {
    internal const string TWO_GROUP_OBJ = """
        o first
        v 0 0 0
        v 1 0 0
        v 1 1 0
        v 0 1 0
        f 1 2 3
        g second
        v 2 0 0
        v 3 0 0
        v 3 1 0
        v 2 1 0
        f 5 6 7 8
        """;

    internal const string SINGLE_TRIANGLE_OBJ = """
        v 1 0 0
        v 0 1 0
        v 0 0 1
        f 1 2 3
        """;

    internal static ModelAssetDescriptor ReadModel(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"missing model '{path}'");
        Assert.NotNull(entry);
        Assert.Equal(AssetEntryType.Model, entry.Type);
        return AssetSerialization.Deserialize<ModelAssetDescriptor>(archive.ReadPayload(entry));
    }

    internal static MeshAssetDescriptor ReadMesh(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"missing mesh '{path}'");
        Assert.NotNull(entry);
        Assert.Equal(AssetEntryType.Mesh, entry.Type);
        return AssetSerialization.Deserialize<MeshAssetDescriptor>(archive.ReadPayload(entry));
    }

    internal static byte[] ReadPayload(PackArchive archive, string path) {
        Assert.True(archive.TryGetEntry(path, out PackEntry? entry), $"missing payload '{path}'");
        Assert.NotNull(entry);
        Assert.Equal(AssetEntryType.Raw, entry.Type);
        return archive.ReadPayload(entry);
    }

    internal static MeshCollisionData ReadCollision(
        PackArchive archive,
        string meshPath,
        MeshAssetDescriptor mesh) {
        Assert.NotNull(mesh.CollisionPayload);
        byte[] payload = ReadPayload(archive, mesh.CollisionPayload!);
        return MeshCollisionCodec.Decode(
            payload,
            mesh.Topology,
            checked((int)mesh.IndexCount),
            meshPath);
    }

    internal static Vector3 PositionAt(byte[] payload, int vertexIndex)
        => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 8)));

    internal static Vector3 NormalAt(byte[] payload, int vertexIndex)
        => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 12)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 16)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 20)));

    internal static Vector2 UvAt(byte[] payload, int vertexIndex)
        => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 24)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 28)));

    internal static Vector4 ColorAt(byte[] payload, int vertexIndex)
        => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 56)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 60)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 64)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 68)));

    internal static Vector4 TangentAt(byte[] payload, int vertexIndex)
        => new(
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 72)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 76)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 80)),
            BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(vertexIndex * 88 + 84)));

    internal static uint IndexAt(byte[] payload, int vertexCount, int index)
        => BinaryPrimitives.ReadUInt32LittleEndian(
            payload.AsSpan((vertexCount * 88) + (index * sizeof(uint))));

    internal static byte[] UvTriangleBufferData() {
        var buffer = new byte[66];
        WriteVector(buffer, 0, 0f, 0f, 0f);
        WriteVector(buffer, 12, 1f, 0f, 0f);
        WriteVector(buffer, 24, 0f, 1f, 0f);
        WriteVector2(buffer, 36, 0.25f, 0.75f);
        WriteVector2(buffer, 44, 0.5f, 0.5f);
        WriteVector2(buffer, 52, 0.75f, 0.25f);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(60), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(62), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(64), 2);
        return buffer;
    }

    internal static string UvTriangleGltf()
        => $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "name": "Tri", "mesh": 0 } ],
              "meshes": [
                {
                  "name": "TriMesh",
                  "primitives": [
                    {
                      "attributes": { "POSITION": 0, "TEXCOORD_0": 2 },
                      "indices": 1,
                      "mode": 4
                    }
                  ]
                }
              ],
              "buffers": [
                {
                  "byteLength": 66,
                  "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(UvTriangleBufferData())}}"
                }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 60, "byteLength": 6, "target": 34963 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 24, "target": 34962 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 1, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" },
                { "bufferView": 2, "componentType": 5126, "count": 3, "type": "VEC2" }
              ]
            }
            """;

    internal static byte[] TriangleBufferData() {
        var buffer = new byte[42];
        WriteVector(buffer, 0, 0f, 0f, 0f);
        WriteVector(buffer, 12, 1f, 0f, 0f);
        WriteVector(buffer, 24, 0f, 1f, 0f);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(36), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(38), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(40), 2);
        return buffer;
    }

    internal static byte[] LineBufferData() {
        var buffer = new byte[28];
        WriteVector(buffer, 0, 0f, 0f, 0f);
        WriteVector(buffer, 12, 1f, 0f, 0f);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(24), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(26), 1);
        return buffer;
    }

    internal static string LineGltf()
        => $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "name": "Edge", "mesh": 0 } ],
              "meshes": [
                {
                  "name": "EdgeMesh",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 1 }
                  ]
                }
              ],
              "buffers": [
                {
                  "byteLength": 28,
                  "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(LineBufferData())}}"
                }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 24, "target": 34962 },
                { "buffer": 0, "byteOffset": 24, "byteLength": 4, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 2,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 0, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 2, "type": "SCALAR" }
              ]
            }
            """;

    internal static string EmbeddedTriangleGltf(string translation = "1, 2, 3")
        => $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "name": "Tri", "mesh": 0, "translation": [ {{translation}} ] } ],
              "meshes": [
                {
                  "name": "TriMesh",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }
                  ]
                }
              ],
              "buffers": [
                {
                  "byteLength": 42,
                  "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(TriangleBufferData())}}"
                }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 1, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;

    internal static string ExternalBufferGltf(string bufferUri)
        => $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "name": "Tri", "mesh": 0 } ],
              "meshes": [
                {
                  "name": "TriMesh",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }
                  ]
                }
              ],
              "buffers": [ { "byteLength": 42, "uri": "{{bufferUri}}" } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 1, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;

    internal static string NodeChainGltf()
        => $$"""
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [
                { "name": "Root", "children": [ 1 ], "translation": [ 0, 2, 0 ] },
                { "name": "Child", "mesh": 0, "translation": [ 1, 0, 0 ] }
              ],
              "meshes": [
                {
                  "name": "TriMesh",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }
                  ]
                }
              ],
              "buffers": [
                {
                  "byteLength": 42,
                  "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(TriangleBufferData())}}"
                }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 1, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;

    internal static string GlbJson()
        => """
            {
              "asset": { "version": "2.0" },
              "scene": 0,
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "name": "Tri", "mesh": 0, "translation": [ 1, 2, 3 ] } ],
              "meshes": [
                {
                  "name": "TriMesh",
                  "primitives": [
                    { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }
                  ]
                }
              ],
              "buffers": [ { "byteLength": 42 } ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 6, "target": 34963 }
              ],
              "accessors": [
                {
                  "bufferView": 0,
                  "componentType": 5126,
                  "count": 3,
                  "type": "VEC3",
                  "min": [ 0, 0, 0 ],
                  "max": [ 1, 1, 0 ]
                },
                { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ]
            }
            """;

    internal static byte[] BuildGlb(string json, byte[] binary) {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonLength = Align4(jsonBytes.Length);
        int binaryLength = Align4(binary.Length);
        int totalLength = 12 + 8 + jsonLength + 8 + binaryLength;
        var glb = new byte[totalLength];
        Span<byte> span = glb;
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)totalLength);

        int offset = 12;
        BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], (uint)jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], 0x4E4F534A);
        jsonBytes.CopyTo(span[(offset + 8)..]);
        span.Slice(offset + 8 + jsonBytes.Length, jsonLength - jsonBytes.Length).Fill(0x20);
        offset += 8 + jsonLength;

        BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], (uint)binaryLength);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 4)..], 0x004E4942);
        binary.CopyTo(span[(offset + 8)..]);
        return glb;
    }

    internal static int Align4(int value) => (value + 3) & ~3;

    private static void WriteVector(byte[] buffer, int offset, float x, float y, float z) {
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), x);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 8), z);
    }

    private static void WriteVector2(byte[] buffer, int offset, float x, float y) {
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset), x);
        BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(offset + 4), y);
    }
}
