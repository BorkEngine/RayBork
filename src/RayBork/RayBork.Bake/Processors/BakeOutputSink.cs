using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;

namespace RayBork.Bake.Processors;

/// <summary>
/// Collects and validates the outputs of one processor source.
/// </summary>
internal sealed class BakeOutputSink : IAssetOutputSink {
    private readonly List<BakedEntry> _outputs = [];
    private readonly HashSet<string> _paths = new(StringComparer.Ordinal);
    private readonly string _sourceId;
    private readonly string _sourcePath;
    private readonly string _processorName;
    private readonly BakeDiagnostics _diagnostics;

    /// <summary>Initializes a source output sink.</summary>
    /// <param name="sourceId">The stable source identifier.</param>
    /// <param name="sourcePath">The diagnostic source path.</param>
    /// <param name="processorName">The owning processor name.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    internal BakeOutputSink(
        string sourceId,
        string sourcePath,
        string processorName,
        BakeDiagnostics diagnostics) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorName);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _sourceId = sourceId;
        _sourcePath = sourcePath;
        _processorName = processorName;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets the accepted outputs in emission order.</summary>
    internal IReadOnlyList<BakedEntry> Outputs => _outputs;

    /// <summary>Gets a value indicating whether an output was rejected.</summary>
    internal bool HasError { get; private set; }

    /// <inheritdoc/>
    public void AddAsset<T>(string path, T value)
        where T : PolyType.IShapeable<T> {
        if (!TryAcceptPath(path)) {
            return;
        }

        if (value is null) {
            Reject(path, "the shaped output value is null");
            return;
        }

        byte[] payload;
        try {
            payload = RayBork.Internal.AssetSerialization.Serialize(value);
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            Reject(path, $"the shaped value could not be serialized: {exception.Message}");
            return;
        }

        _outputs.Add(new BakedEntry(
            path,
            BakeEntryTypes.FromShapeType(typeof(T)),
            payload,
            alignment: 1,
            _sourceId));
    }

    /// <inheritdoc/>
    public void AddData(string path, ReadOnlySpan<byte> payload, uint alignment = 1) {
        if (!TryAcceptPath(path)) {
            return;
        }

        if (!BakeEntryTypes.IsValidAlignment(alignment)) {
            Reject(path, $"the alignment {alignment} is invalid; use 1, 16, 64, or 256");
            return;
        }

        _outputs.Add(new BakedEntry(
            path,
            AssetEntryType.Raw,
            payload.ToArray(),
            alignment,
            _sourceId));
    }

    private bool TryAcceptPath(string? path) {
        if (string.IsNullOrEmpty(path)) {
            Reject("<null>", "the output path is empty");
            return false;
        }

        if (!RayBork.Internal.AssetPath.TryValidateCanonical(path, out string? reason)) {
            Reject(path, $"the output path is not canonical: {reason}");
            return false;
        }

        if (!_paths.Add(path)) {
            Reject(path, "the output path is produced more than once for this source");
            return false;
        }

        return true;
    }

    private void Reject(string path, string reason) {
        HasError = true;
        _diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.PROCESSOR_FAILED,
            $"The processor '{_processorName}' produced an invalid output at '{path}': {reason}.",
            _sourcePath,
            assetPath: path);
    }
}
