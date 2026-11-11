using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Paths;
using RayBork.Bake.Processors;
using RayBork.Bake.Toml;
using Tomlyn.Model;

namespace RayBork.Bake.Sources;

/// <summary>Binds a processor's settings table from its sidecar or table-named source.</summary>
internal static class BakeSidecarBinder {
    /// <summary>Binds the sidecar for one processor source.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="source">The processor source.</param>
    /// <param name="context">The per-source processing context, used only for a default sidecar.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="sidecar">The bound sidecar, or <see langword="null"/> for a sidecar-free processor.</param>
    /// <returns><see langword="true"/> when the source may run.</returns>
    internal static bool TryBind(
        BakeProject project,
        BakeSource source,
        AssetProcessorContext context,
        BakeDiagnostics diagnostics,
        out object? sidecar) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(diagnostics);

        sidecar = null;
        BakeProcessorRegistration? processor = source.Processor;
        if (processor is null || processor.Policy == SidecarPolicy.None) {
            return true;
        }

        if (source.SidecarDocument is null) {
            return TryDefaultOrMissing(project, source, context, diagnostics, out sidecar);
        }

        TomlDocument sidecarDocument = source.SidecarDocument;
        string diagnosticPath = BakePath.ForDiagnostic(
            project.ProjectRoot,
            source.SidecarPath ?? source.FullPath);
        TomlTable root = sidecarDocument.Root;
        var bindContext = new BakeBindContext(
            root,
            sidecarDocument.Syntax,
            diagnostics,
            diagnosticPath,
            source.BaseAssetPath);

        bool structureValid = true;
        foreach (string key in root.Keys.OrderBy(key => key, StringComparer.Ordinal)) {
            if (string.Equals(key, processor.Name, StringComparison.Ordinal)) {
                continue;
            }

            structureValid = false;
            bindContext.AddKeyError(
                $"Unknown root key '{key}' in the sidecar of processor '{processor.Name}'; a "
                    + $"sidecar contains only the '[{processor.Name}]' table.",
                TomlPath.Root.Child(key),
                memberPath: key);
        }

        if (!root.TryGetValue(processor.Name, out object? tableValue)
            || tableValue is not TomlTable sidecarTable) {
            bindContext.AddError(
                $"The sidecar of processor '{processor.Name}' has no '[{processor.Name}]' table.",
                memberPath: processor.Name);
            return false;
        }

        if (!structureValid || processor.SidecarShape is null) {
            return false;
        }

        int before = diagnostics.Count;
        object? bound;
        try {
            bound = TomlShapeBinder.BindTable(
                processor.SidecarShape,
                bindContext,
                sidecarTable,
                processor.Name);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"Binding the sidecar of processor '{processor.Name}' failed unexpectedly: "
                    + exception.Message,
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        if (diagnostics.Count != before || bound is null) {
            return false;
        }

        sidecar = bound;
        return true;
    }

    private static bool TryDefaultOrMissing(
        BakeProject project,
        BakeSource source,
        AssetProcessorContext context,
        BakeDiagnostics diagnostics,
        out object? sidecar) {
        sidecar = null;
        BakeProcessorRegistration processor = source.Processor!;
        string diagnosticPath = BakePath.ForDiagnostic(project.ProjectRoot, source.FullPath);

        if (processor.Policy == SidecarPolicy.Required) {
            string expected = source.RelativePath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
                ? source.RelativePath
                : source.BaseAssetPath + ".toml";
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"The required sidecar for processor '{processor.Name}' is missing ('{expected}').",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }

        try {
            object? created = processor.CreateDefaultSidecar(context);
            if (created is null) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    $"The processor '{processor.Name}' returned a null default sidecar.",
                    diagnosticPath,
                    assetPath: source.BaseAssetPath);
                return false;
            }

            sidecar = created;
            return true;
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                $"Creating the default sidecar for processor '{processor.Name}' failed: "
                    + exception.Message,
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return false;
        }
    }
}
