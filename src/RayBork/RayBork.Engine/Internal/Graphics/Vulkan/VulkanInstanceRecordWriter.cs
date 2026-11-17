using System.Buffers.Binary;
using System.Numerics;

namespace RayBork.Internal;

/// <summary>
/// Writes the little-endian shader-ABI instance record and frame payloads the geometry and post-process
/// paths share.
/// </summary>
/// <remarks>
/// The methods are pure byte writers for the render thread's host-visible frame and instance storage
/// buffers. They allocate nothing and are safe from any single thread; the callers serialize the
/// buffers by frame slot.
/// </remarks>
internal static class VulkanInstanceRecordWriter {
    /// <summary>Rounds a byte offset up to <paramref name="alignment"/>.</summary>
    /// <param name="value">The byte offset to align.</param>
    /// <param name="alignment">The positive alignment in bytes.</param>
    /// <returns>The smallest multiple of <paramref name="alignment"/> not below <paramref name="value"/>.</returns>
    internal static ulong AlignUp(ulong value, ulong alignment)
        => checked(((value + alignment - 1) / alignment) * alignment);

    /// <summary>
    /// Writes one System.Numerics matrix in its storage order.
    /// </summary>
    /// <remarks>
    /// The words are written M11..M44 (row-major). GLSL reads a <c>mat4</c> from std140/std430 storage
    /// as column-major, so it sees the transpose of what this method writes, which is the engine's
    /// row-vector matrix expressed in the shader's column-vector convention.
    /// </remarks>
    /// <param name="destination">The record buffer.</param>
    /// <param name="offset">The byte offset of the matrix.</param>
    /// <param name="value">The matrix to write.</param>
    internal static void WriteMatrix(Span<byte> destination, int offset, Matrix4x4 value) {
        Span<byte> target = destination[offset..];
        WriteSingle(target, 0, value.M11);
        WriteSingle(target, 4, value.M12);
        WriteSingle(target, 8, value.M13);
        WriteSingle(target, 12, value.M14);
        WriteSingle(target, 16, value.M21);
        WriteSingle(target, 20, value.M22);
        WriteSingle(target, 24, value.M23);
        WriteSingle(target, 28, value.M24);
        WriteSingle(target, 32, value.M31);
        WriteSingle(target, 36, value.M32);
        WriteSingle(target, 40, value.M33);
        WriteSingle(target, 44, value.M34);
        WriteSingle(target, 48, value.M41);
        WriteSingle(target, 52, value.M42);
        WriteSingle(target, 56, value.M43);
        WriteSingle(target, 60, value.M44);
    }

    /// <summary>Writes one <c>vec4</c> at a byte offset.</summary>
    /// <param name="destination">The record buffer.</param>
    /// <param name="offset">The byte offset of the vector.</param>
    /// <param name="value">The vector to write.</param>
    internal static void WriteVector4(Span<byte> destination, int offset, Vector4 value) {
        Span<byte> target = destination[offset..];
        WriteSingle(target, 0, value.X);
        WriteSingle(target, 4, value.Y);
        WriteSingle(target, 8, value.Z);
        WriteSingle(target, 12, value.W);
    }

    /// <summary>Writes one little-endian <c>float</c> at a byte offset.</summary>
    /// <param name="destination">The record buffer.</param>
    /// <param name="offset">The byte offset of the value.</param>
    /// <param name="value">The value to write.</param>
    internal static void WriteSingle(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteSingleLittleEndian(destination[offset..], value);

    /// <summary>Writes one little-endian <c>uint</c> at a byte offset.</summary>
    /// <param name="destination">The record buffer.</param>
    /// <param name="offset">The byte offset of the value.</param>
    /// <param name="value">The value to write.</param>
    internal static void WriteUInt32(Span<byte> destination, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination[offset..], value);
}
