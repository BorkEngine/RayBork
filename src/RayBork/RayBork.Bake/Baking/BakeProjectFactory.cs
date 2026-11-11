using System.Reflection;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Incremental;
using RayBork.Bake.Paths;
using RayBork.Bake.Processors;

namespace RayBork.Bake.Baking;

/// <summary>
/// Validates a builder-side <see cref="BakeProjectDefinition"/> into the immutable project the pipeline
/// consumes.
/// </summary>
internal static class BakeProjectFactory {
    /// <summary>Validates a definition and creates the project.</summary>
    /// <param name="definition">The builder-side configuration.</param>
    /// <param name="projectRootOverride">The command-line project root, or <see langword="null"/>.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="project">The created project, when successful.</param>
    /// <returns><see langword="true"/> when the configuration is valid.</returns>
    internal static bool TryCreate(
        BakeProjectDefinition definition,
        string? projectRootOverride,
        BakeDiagnostics diagnostics,
        out BakeProject? project) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(diagnostics);

        project = null;
        foreach (BakeDiagnostic issue in definition.Issues) {
            diagnostics.Add(issue);
        }

        if (!TryResolveProjectRoot(definition, projectRootOverride, diagnostics, out string projectRoot)
            || !BakeProcessorRegistry.TryCreate(
                definition.Processors,
                diagnostics,
                out BakeProcessorRegistry? processorRegistry)
            || processorRegistry is null) {
            return false;
        }

        if (definition.Packs.Count == 0) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.CONFIGURATION_INVALID,
                "The bake configuration declares no asset pack; call WithPack(packPath, root) at "
                    + "least once.");
            return false;
        }

        var packs = new List<BakePack>(definition.Packs.Count);
        var packPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool valid = true;

        for (int index = 0; index < definition.Packs.Count; index++) {
            BakePackDefinition configured = definition.Packs[index];
            if (!BakePath.TryNormalizeRelative(configured.PackPath, out string packPath, out string? packReason)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The pack path '{configured.PackPath}' is invalid: {packReason}. A pack path "
                        + "must be relative to the output root and must not contain an empty, '.' or "
                        + "'..' segment.");
                valid = false;
                continue;
            }

            if (!packPaths.Add(packPath)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The pack path '{packPath}' is configured more than once.");
                valid = false;
                continue;
            }

            if (!BakePath.TryNormalizeRelative(configured.Root, out string relativeRoot, out string? rootReason)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The asset root '{configured.Root}' of pack '{packPath}' is invalid: {rootReason}. "
                        + "A root must be relative to the project root and must not contain an empty, "
                        + "'.' or '..' segment.");
                valid = false;
                continue;
            }

            if (!BakePath.TryResolveUnderRoot(projectRoot, relativeRoot, out string root)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The asset root '{relativeRoot}' of pack '{packPath}' escapes the project root.");
                valid = false;
                continue;
            }

            if (!Directory.Exists(root)) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.CONFIGURATION_INVALID,
                    $"The asset root '{relativeRoot}' of pack '{packPath}' does not exist as a "
                        + $"directory ('{root}').");
                valid = false;
                continue;
            }

            packs.Add(new BakePack(index, root, relativeRoot, packPath));
        }

        if (!valid || packs.Count != definition.Packs.Count) {
            return false;
        }

        string toolVersion = definition.ToolVersionOverride ?? BakeToolInfo.Version;
        project = new BakeProject(
            projectRoot,
            packs,
            definition.TextureFilter,
            definition.TextureWrap,
            definition.AssetTypes,
            processorRegistry.Processors,
            processorRegistry,
            toolVersion);
        return !diagnostics.HasErrors;
    }

    private static bool TryResolveProjectRoot(
        BakeProjectDefinition definition,
        string? projectRootOverride,
        BakeDiagnostics diagnostics,
        out string projectRoot) {
        projectRoot = string.Empty;
        string requested = !string.IsNullOrWhiteSpace(projectRootOverride)
            ? projectRootOverride
            : !string.IsNullOrWhiteSpace(definition.ProjectRoot)
                ? definition.ProjectRoot
                : Directory.GetCurrentDirectory();

        try {
            projectRoot = Path.GetFullPath(requested);
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.CONFIGURATION_INVALID,
                $"The project root '{requested}' is not a valid path: {exception.Message}");
            return false;
        }

        if (!Directory.Exists(projectRoot)) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.CONFIGURATION_INVALID,
                $"The project root '{projectRoot}' does not exist as a directory. Pass "
                    + "--project-root or call WithProjectRoot().",
                projectRoot);
            return false;
        }

        return true;
    }
}
