using RayBork.Internal;

namespace RayBork.Bake.Baking;

/// <summary>One asset entry produced by a source, before pack composition.</summary>
internal sealed class BakedEntry
{
    /// <summary>Initializes a baked entry.</summary>
    internal BakedEntry(
        string path,
        AssetEntryType type,
        byte[] payload,
        uint alignment,
        string sourceId
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(payload);

        Path = path;
        Type = type;
        Payload = payload;
        Alignment = alignment;
        SourceId = sourceId;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string Path { get; }

    /// <summary>Gets the pack entry type.</summary>
    internal AssetEntryType Type { get; }

    /// <summary>Gets the uncompressed payload bytes.</summary>
    internal byte[] Payload { get; }

    /// <summary>Gets the required payload alignment.</summary>
    internal uint Alignment { get; }

    /// <summary>Gets the producing source identifier.</summary>
    internal string SourceId { get; }
}

/// <summary>Maps shaped output types to pack entry types.</summary>
internal static class BakeEntryTypes
{
    private static readonly Dictionary<Type, AssetEntryType> _shapeTypes = new()
    {
        [typeof(TextureAssetDescriptor)] = AssetEntryType.Texture,
        [typeof(MeshAssetDescriptor)] = AssetEntryType.Mesh,
        [typeof(ShaderAssetDescriptor)] = AssetEntryType.Shader,
        [typeof(ModelAssetDescriptor)] = AssetEntryType.Model,
        [typeof(FontAssetDescriptor)] = AssetEntryType.Font,
    };

    /// <summary>Maps engine descriptors to pack types; all other shapeables are data.</summary>
    internal static AssetEntryType FromShapeType(Type shapeType)
    {
        ArgumentNullException.ThrowIfNull(shapeType);

        return _shapeTypes.TryGetValue(shapeType, out AssetEntryType entryType)
            ? entryType
            : AssetEntryType.Data;
    }

    /// <summary>Gets whether an alignment is 1, 16, 64, or 256.</summary>
    internal static bool IsValidAlignment(uint alignment) => alignment is 1 or 16 or 64 or 256;
}
