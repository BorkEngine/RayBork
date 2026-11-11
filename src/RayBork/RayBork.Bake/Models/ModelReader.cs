namespace RayBork.Bake.Models;

/// <summary>Dispatches a model source to its format reader.</summary>
internal static class ModelReader {
    /// <summary>Reads a supported model source.</summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="defaultPartName">The part name for OBJ faces before any group.</param>
    /// <param name="warning">The warning sink.</param>
    /// <param name="addInput">The tracked-input sink.</param>
    /// <returns>The non-empty part list.</returns>
    /// <exception cref="ModelReadException">The extension is unsupported or the source is invalid.</exception>
    internal static IReadOnlyList<ModelPartSource> Read(
        string sourcePath,
        string defaultPartName,
        Action<string> warning,
        Action<string> addInput) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPartName);
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(addInput);

        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)) {
            return ObjModelReader.Read(sourcePath, defaultPartName, warning);
        }

        if (extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)) {
            return GltfModelReader.Read(sourcePath, warning, addInput);
        }

        throw new ModelReadException(
            $"The model source '{Path.GetFileName(sourcePath)}' has the unsupported extension "
                + $"'{extension}'.");
    }
}
