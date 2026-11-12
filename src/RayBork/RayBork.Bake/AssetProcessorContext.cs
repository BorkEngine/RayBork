namespace RayBork.Bake;

/// <summary>
/// The per-source state handed to a processor.
/// </summary>
/// <remarks>
/// A context is created for exactly one source and must not be retained after <c>Process</c> returns.
/// Additional tracked dependencies, such as the GLSL files named by a shader TOML, are registered
/// with <see cref="AddInput"/>.
/// </remarks>
public sealed class AssetProcessorContext {
    private readonly List<string> _additionalInputs = [];

    /// <summary>Initializes a per-source context.</summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="relativePath">The source path relative to its pack root, with <c>/</c> separators.</param>
    /// <param name="baseAssetPath">The canonical base asset path.</param>
    /// <param name="sourceFileName">The source file name.</param>
    /// <param name="sourceFileNameWithoutExtension">The source file name without its final extension.</param>
    /// <param name="outputs">The output sink.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal AssetProcessorContext(
        string sourcePath,
        string relativePath,
        string baseAssetPath,
        string sourceFileName,
        string sourceFileNameWithoutExtension,
        IAssetOutputSink outputs,
        IAssetProcessorDiagnostics diagnostics) {
        SourcePath = sourcePath;
        RelativePath = relativePath;
        BaseAssetPath = baseAssetPath;
        SourceFileName = sourceFileName;
        SourceFileNameWithoutExtension = sourceFileNameWithoutExtension;
        Outputs = outputs;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the absolute source path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the source path relative to its pack root, with <c>/</c> separators.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the canonical base asset path, without a leading slash.</summary>
    public string BaseAssetPath { get; }

    /// <summary>Gets the source file name.</summary>
    public string SourceFileName { get; }

    /// <summary>Gets the source file name without its final extension.</summary>
    public string SourceFileNameWithoutExtension { get; }

    /// <summary>Gets the output sink for this source.</summary>
    public IAssetOutputSink Outputs { get; }

    /// <summary>Gets the diagnostic sink for this source.</summary>
    public IAssetProcessorDiagnostics Diagnostics { get; }

    /// <summary>Gets the additional tracked input files, as absolute paths.</summary>
    public IReadOnlyList<string> AdditionalInputs => _additionalInputs;

    /// <summary>
    /// Registers one additional file dependency for incremental hashing.
    /// </summary>
    /// <param name="path">
    /// An absolute path or a path relative to the source file's directory. The file must exist.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or invalid.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public void AddInput(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string candidate;
        try {
            candidate = Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(Path.GetDirectoryName(SourcePath) ?? Directory.GetCurrentDirectory(), path));
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            throw new ArgumentException($"Invalid additional input path '{path}'.", nameof(path), exception);
        }

        if (!File.Exists(candidate)) {
            throw new FileNotFoundException(
                $"The additional input '{path}' does not exist.",
                candidate);
        }

        foreach (string existing in _additionalInputs) {
            if (string.Equals(existing, candidate, StringComparison.Ordinal)) {
                return;
            }
        }

        _additionalInputs.Add(candidate);
    }
}
