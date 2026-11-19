namespace RayBork.Bake.Models;

/// <summary>Dispatches a model source to its format reader.</summary>
internal static class ModelReader
{
    /// <summary>Reads a supported model source.</summary>
    /// <returns>The non-empty part list.</returns>
    internal static IReadOnlyList<ModelPartSource> Read(
        string sourcePath,
        string defaultPartName,
        Action<string> warning,
        Action<string> addInput
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPartName);
        ArgumentNullException.ThrowIfNull(warning);
        ArgumentNullException.ThrowIfNull(addInput);

        string extension = Path.GetExtension(sourcePath);
        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            return ObjModelReader.Read(sourcePath, defaultPartName, warning);
        }

        if (
            extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
        )
        {
            return GltfModelReader.Read(sourcePath, warning, addInput);
        }

        throw new ModelReadException(
            $"The model source '{Path.GetFileName(sourcePath)}' has the unsupported extension "
                + $"'{extension}'."
        );
    }
}
