namespace RayBork.Bake.Baking;

/// <summary>One asset entry produced by a source, before pack composition.</summary>
internal sealed class BakedEntry {
    /// <summary>Initializes a baked entry.</summary>
    /// <param name="path">The canonical asset path.</param>
    /// <param name="type">The pack entry type.</param>
    /// <param name="payload">The uncompressed payload bytes.</param>
    /// <param name="alignment">The required payload alignment.</param>
    /// <param name="sourceId">The producing source identifier.</param>
    internal BakedEntry(
        string path,
        RayBork.Internal.AssetEntryType type,
        byte[] payload,
        uint alignment,
        string sourceId) {
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
    internal RayBork.Internal.AssetEntryType Type { get; }

    /// <summary>Gets the uncompressed payload bytes.</summary>
    internal byte[] Payload { get; }

    /// <summary>Gets the required payload alignment.</summary>
    internal uint Alignment { get; }

    /// <summary>Gets the producing source identifier.</summary>
    internal string SourceId { get; }
}

/// <summary>Maps shaped output types to pack entry types.</summary>
internal static class BakeEntryTypes {
    /// <summary>Maps an engine descriptor type to its pack type; every other shapeable type is data.</summary>
    /// <param name="shapeType">The shaped output's CLR type.</param>
    /// <returns>The pack entry type.</returns>
    internal static RayBork.Internal.AssetEntryType FromShapeType(Type shapeType) {
        ArgumentNullException.ThrowIfNull(shapeType);

        if (shapeType == typeof(RayBork.Internal.TextureAssetDescriptor)) {
            return RayBork.Internal.AssetEntryType.Texture;
        }

        if (shapeType == typeof(RayBork.Internal.MeshAssetDescriptor)) {
            return RayBork.Internal.AssetEntryType.Mesh;
        }

        if (shapeType == typeof(RayBork.Internal.ShaderAssetDescriptor)) {
            return RayBork.Internal.AssetEntryType.Shader;
        }

        if (shapeType == typeof(RayBork.Internal.ModelAssetDescriptor)) {
            return RayBork.Internal.AssetEntryType.Model;
        }

        if (shapeType == typeof(RayBork.Internal.FontAssetDescriptor)) {
            return RayBork.Internal.AssetEntryType.Font;
        }

        return RayBork.Internal.AssetEntryType.Data;
    }

    /// <summary>Gets whether an alignment is one the RAYBORKP v1 writer accepts.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns><see langword="true"/> for 1, 16, 64, or 256.</returns>
    internal static bool IsValidAlignment(uint alignment)
        => alignment is 1 or 16 or 64 or 256;
}
