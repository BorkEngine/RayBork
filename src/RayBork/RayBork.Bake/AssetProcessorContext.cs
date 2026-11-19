namespace RayBork.Bake;

/// <summary>The per-source state handed to a processor.</summary>
/// <remarks>A context belongs to one source and must not be retained after processing.</remarks>
public sealed class AssetProcessorContext
{
    private readonly List<string> _additionalInputs = [];

    /// <summary>Initializes a per-source context.</summary>
    internal AssetProcessorContext(
        string sourcePath,
        string relativePath,
        string baseAssetPath,
        string sourceFileName,
        string sourceFileNameWithoutExtension,
        IAssetOutputSink outputs,
        IAssetProcessorDiagnostics diagnostics
    )
    {
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
    /// Registers an existing input file, absolute or relative to the source directory, for hashing.
    /// </summary>
    public void AddInput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string candidate;
        try
        {
            candidate = Path.GetFullPath(
                Path.IsPathRooted(path)
                    ? path
                    : Path.Combine(
                        Path.GetDirectoryName(SourcePath) ?? Directory.GetCurrentDirectory(),
                        path
                    )
            );
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid additional input path '{path}'.",
                nameof(path),
                exception
            );
        }

        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"The additional input '{path}' does not exist.",
                candidate
            );
        }

        foreach (string existing in _additionalInputs)
        {
            if (string.Equals(existing, candidate, StringComparison.Ordinal))
            {
                return;
            }
        }

        _additionalInputs.Add(candidate);
    }
}
