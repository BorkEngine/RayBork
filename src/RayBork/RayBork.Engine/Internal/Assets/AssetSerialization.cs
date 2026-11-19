using Nerdbank.MessagePack;
using PolyType;

namespace RayBork.Internal;

/// <summary>The MessagePack serializer shared by pack descriptors and custom data.</summary>
/// <remarks>
/// The serializer is immutable and thread-safe, and it resolves every packed type through a
/// source-generated shape, so the loader stays Native AOT compatible.
/// </remarks>
internal static class AssetSerialization
{
    private static readonly MessagePackSerializer _serializer = new();

    /// <summary>Serializes a shaped value to MessagePack bytes.</summary>
    internal static byte[] Serialize<T>(T value)
        where T : IShapeable<T> => _serializer.Serialize(value);

    /// <summary>Deserializes a shaped value from a complete MessagePack payload.</summary>
    /// <exception cref="InvalidOperationException">The payload contains no value.</exception>
    internal static T Deserialize<T>(ReadOnlyMemory<byte> payload)
        where T : IShapeable<T>
    {
        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        T? value = _serializer.Deserialize<T>(stream, T.GetTypeShape());
        return value
            ?? throw new InvalidOperationException(
                $"The payload did not contain a {typeof(T).FullName} value."
            );
    }
}
