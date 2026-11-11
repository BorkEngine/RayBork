namespace RayBork.Bake;

/// <summary>
/// The diagnostic sink for one processor source or one semantic validator.
/// </summary>
/// <remarks>
/// A processor error discards every output of its source and fails the pack. A validator error fails
/// the pack. Warnings and informational messages are reported without failing unless the invocation
/// sets <c>--warnings-as-errors</c>.
/// </remarks>
public interface IAssetProcessorDiagnostics {
    /// <summary>Reports an error.</summary>
    /// <param name="code">The stable <c>RBxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Error(string code, string message);

    /// <summary>Reports a warning.</summary>
    /// <param name="code">The stable <c>RBxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Warning(string code, string message);

    /// <summary>Reports informational output.</summary>
    /// <param name="code">The stable <c>RBxxxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Info(string code, string message);
}
