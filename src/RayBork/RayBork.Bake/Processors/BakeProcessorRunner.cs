using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Paths;
using RayBork.Bake.Sources;

namespace RayBork.Bake.Processors;

/// <summary>The accepted result of processing one source.</summary>
internal sealed class ProcessedSource {
    /// <summary>Initializes a processed source.</summary>
    /// <param name="source">The source.</param>
    /// <param name="entries">The emitted entries.</param>
    /// <param name="additionalInputs">Additional tracked input files.</param>
    internal ProcessedSource(
        BakeSource source,
        IReadOnlyList<BakedEntry> entries,
        IReadOnlyList<string> additionalInputs) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(additionalInputs);

        Source = source;
        Entries = [.. entries];
        AdditionalInputs = [.. additionalInputs];
    }

    /// <summary>Gets the processed source.</summary>
    internal BakeSource Source { get; }

    /// <summary>Gets the emitted entries.</summary>
    internal IReadOnlyList<BakedEntry> Entries { get; }

    /// <summary>Gets additional tracked input files.</summary>
    internal IReadOnlyList<string> AdditionalInputs { get; }
}

/// <summary>Runs one processor source and discards every output on failure.</summary>
internal static class BakeProcessorRunner {
    /// <summary>Processes one source.</summary>
    /// <param name="project">The validated project.</param>
    /// <param name="source">The processor source.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <returns>The accepted result, or <see langword="null"/> when the source failed.</returns>
    internal static ProcessedSource? Run(
        BakeProject project,
        BakeSource source,
        BakeDiagnostics diagnostics) {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostics);

        BakeProcessorRegistration? processor = source.Processor;
        if (processor is null) {
            throw new ArgumentException("The source is not a processor source.", nameof(source));
        }

        string diagnosticPath = BakePath.ForDiagnostic(project.ProjectRoot, source.FullPath);
        string fileName = Path.GetFileName(source.FullPath);
        var outputSink = new BakeOutputSink(
            source.SourceId,
            diagnosticPath,
            processor.Name,
            diagnostics);
        var diagnosticSink = new BakeProcessorDiagnosticSink(
            processor.Name,
            diagnosticPath,
            source.BaseAssetPath,
            BakeDiagnosticCodes.PROCESSOR_FAILED,
            diagnostics);
        var context = new AssetProcessorContext(
            source.FullPath,
            source.RelativePath,
            source.BaseAssetPath,
            fileName,
            Path.GetFileNameWithoutExtension(fileName),
            outputSink,
            diagnosticSink);

        if (!BakeSidecarBinder.TryBind(project, source, context, diagnostics, out object? sidecar)) {
            return null;
        }

        if (diagnosticSink.HasError) {
            return null;
        }

        try {
            processor.Process(context, sidecar);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.PROCESSOR_FAILED,
                $"The processor '{processor.Name}' failed while processing "
                    + $"'{source.RelativePath}': {exception.Message}",
                diagnosticPath,
                assetPath: source.BaseAssetPath);
            return null;
        }

        if (diagnosticSink.HasError || outputSink.HasError) {
            return null;
        }

        return new ProcessedSource(source, outputSink.Outputs, context.AdditionalInputs);
    }
}
