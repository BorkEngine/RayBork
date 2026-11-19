using PolyType;

namespace RayBork.Bake;

/// <summary>The output sink for one processor source.</summary>
/// <remarks>
/// Paths are canonical without a leading slash. Engine descriptors keep their pack entry type;
/// other shaped types become <c>Data</c>.
/// </remarks>
public interface IAssetOutputSink
{
    /// <summary>Emits one shaped asset at a canonical pack path.</summary>
    void AddAsset<T>(string path, T value)
        where T : IShapeable<T>;

    /// <summary>Emits one opaque payload with 1, 16, 64, or 256-byte alignment.</summary>
    void AddData(string path, ReadOnlySpan<byte> payload, uint alignment = 1);
}
