using System.Globalization;
using RayBork.Bake.Baking;
using RayBork.Bake.Processors;

namespace RayBork.Bake.Incremental;

/// <summary>Computes the deterministic builder-configuration hash recorded in the manifest.</summary>
internal static class BakeConfigurationHash {
    private const string HASH_SHAPE = "raybork.bake.configuration.v1";

    /// <summary>Computes the configuration hash of a validated project.</summary>
    /// <param name="project">The validated project.</param>
    /// <returns>The 64-character digest.</returns>
    internal static string Compute(BakeProject project) {
        ArgumentNullException.ThrowIfNull(project);

        var fields = new List<string> {
            HASH_SHAPE,
            BuiltInProcessors.CONFIGURATION_MARKER,
            project.ProjectRoot,
            Format(project.Packs.Count),
        };

        foreach (BakePack pack in project.Packs) {
            fields.Add(Format(pack.Index));
            fields.Add(pack.PackPath);
            fields.Add(pack.RelativeRoot);
            fields.Add(pack.Root);
        }

        fields.Add(project.TextureFilter.ToString());
        fields.Add(project.TextureWrap.ToString());

        string[] typeNames = [.. project.AssetTypes.Keys.OrderBy(name => name, StringComparer.Ordinal)];
        fields.Add(Format(typeNames.Length));
        foreach (string typeName in typeNames) {
            BakeAssetTypeRegistration registration = project.AssetTypes[typeName];
            fields.Add(typeName);
            fields.Add(registration.Type.Assembly.GetName().Name ?? string.Empty);
        }

        fields.Add(Format(project.Processors.Count));
        foreach (BakeProcessorRegistration processor in project.Processors) {
            Type processorType = processor.ProcessorType;
            fields.Add(processor.Name);
            fields.Add(processorType.FullName ?? processorType.Name);
            fields.Add(processor.Policy.ToString());
            fields.Add(processor.SidecarShape?.Type.FullName ?? string.Empty);

            string[] extensions = [.. processor.Extensions
                .Select(NormalizeExtension)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(extension => extension, StringComparer.Ordinal)];
            fields.Add(Format(extensions.Length));
            fields.AddRange(extensions);
        }

        return BakeHash.HashTextFields([.. fields]);
    }

    private static string NormalizeExtension(string extension) {
        string normalized = extension.Trim().ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized[1..] : normalized;
    }

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
