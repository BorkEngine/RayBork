using Nerdbank.MessagePack;
using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The single Nerdbank.MessagePack serializer used for pack descriptors and custom data.
/// </summary>
/// <remarks>
/// The serializer is immutable and thread-safe, so one instance is shared. It has no reflection
/// resolver: every packed type carries a source-generated shape, which is what keeps the loader
/// Native AOT compatible.
/// </remarks>
internal static class AssetSerialization {
    private static readonly MessagePackSerializer _serializer = new();

    /// <summary>
    /// Serializes a shaped value.
    /// </summary>
    /// <typeparam name="T">The shaped value type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The MessagePack bytes.</returns>
    internal static byte[] Serialize<T>(T value)
        where T : IShapeable<T>
        => _serializer.Serialize(value);

    /// <summary>
    /// Deserializes a shaped value from a payload.
    /// </summary>
    /// <typeparam name="T">The shaped value type.</typeparam>
    /// <param name="payload">The complete MessagePack payload.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="InvalidOperationException">The payload deserialized to <see langword="null"/>.</exception>
    internal static T Deserialize<T>(ReadOnlyMemory<byte> payload)
        where T : IShapeable<T> {
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        T? value = _serializer.Deserialize<T>(stream, T.GetTypeShape());
        return value ?? throw new InvalidOperationException(
            $"The payload did not contain a {typeof(T).FullName} value.");
    }
}
