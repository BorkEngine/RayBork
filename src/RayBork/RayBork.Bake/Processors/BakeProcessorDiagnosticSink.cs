using RayBork.Bake.Diagnostics;

namespace RayBork.Bake.Processors;

/// <summary>
/// Adapts a processor's or validator's diagnostic calls into the bake diagnostic bag.
/// </summary>
internal sealed class BakeProcessorDiagnosticSink : IAssetProcessorDiagnostics {
    private readonly string _ownerName;
    private readonly string _sourcePath;
    private readonly string? _assetPath;
    private readonly string _defaultCode;
    private readonly BakeDiagnostics _diagnostics;

    /// <summary>Initializes a source or validator diagnostic sink.</summary>
    /// <param name="ownerName">The processor or validator name.</param>
    /// <param name="sourcePath">The diagnostic source path.</param>
    /// <param name="assetPath">The canonical asset path, when known.</param>
    /// <param name="defaultCode">The code used when a caller supplies an empty code.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    internal BakeProcessorDiagnosticSink(
        string ownerName,
        string sourcePath,
        string? assetPath,
        string defaultCode,
        BakeDiagnostics diagnostics) {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCode);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _ownerName = ownerName;
        _sourcePath = sourcePath;
        _assetPath = assetPath;
        _defaultCode = defaultCode;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether an error was reported.</summary>
    internal bool HasError { get; private set; }

    /// <inheritdoc/>
    public void Error(string code, string message) {
        HasError = true;
        Add(BakeSeverity.Error, code, message);
    }

    /// <inheritdoc/>
    public void Warning(string code, string message) => Add(BakeSeverity.Warning, code, message);

    /// <inheritdoc/>
    public void Info(string code, string message) => Add(BakeSeverity.Info, code, message);

    private void Add(BakeSeverity severity, string code, string message)
        => _diagnostics.Add(
            severity,
            string.IsNullOrWhiteSpace(code) ? _defaultCode : code,
            $"{_ownerName}: {message}",
            _sourcePath,
            assetPath: _assetPath);
}
