using PolyType;

namespace RayBork.Bake;

/// <summary>
/// The output sink for one processor source.
/// </summary>
/// <remarks>
/// <see cref="AddAsset{T}"/> serializes a shaped descriptor or custom value with the engine's
/// MessagePack serializer. The sink maps engine descriptor types to their pack entry type and maps
/// every other shaped type to a <c>Data</c> entry. <see cref="AddData"/> stores an opaque payload as
/// a <c>Raw</c> entry.
/// </remarks>
public interface IAssetOutputSink {
    /// <summary>Emits one shaped asset at a canonical pack path.</summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="path">The canonical output path, without a leading slash.</param>
    /// <param name="value">The typed asset value.</param>
    void AddAsset<T>(string path, T value)
        where T : IShapeable<T>;

    /// <summary>Emits one opaque payload at a canonical pack path.</summary>
    /// <param name="path">The canonical output path, without a leading slash.</param>
    /// <param name="payload">The payload bytes; the sink copies them.</param>
    /// <param name="alignment">The required payload alignment: 1, 16, 64, or 256.</param>
    void AddData(string path, ReadOnlySpan<byte> payload, uint alignment = 1);
}
