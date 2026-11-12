namespace RayBork.Bake.Diagnostics;

/// <summary>The severity of one bake diagnostic.</summary>
internal enum BakeSeverity {
    /// <summary>A failure that prevents publishing the pack.</summary>
    Error,

    /// <summary>A condition that does not fail the bake unless warnings are treated as errors.</summary>
    Warning,

    /// <summary>Informational output.</summary>
    Info,
}

/// <summary>
/// One bake diagnostic with an optional source location.
/// </summary>
/// <param name="Severity">The diagnostic severity.</param>
/// <param name="Code">The stable <c>RBxxxx</c> code.</param>
/// <param name="Message">The human-readable message.</param>
/// <param name="FilePath">The source or pack path, when known.</param>
/// <param name="Line">The optional 1-based line.</param>
/// <param name="Column">The optional 1-based column.</param>
/// <param name="AssetPath">The optional canonical asset path.</param>
/// <param name="MemberPath">The optional TOML member path.</param>
internal readonly record struct BakeDiagnostic(
    BakeSeverity Severity,
    string Code,
    string Message,
    string? FilePath,
    int? Line,
    int? Column,
    string? AssetPath,
    string? MemberPath) {
    /// <summary>Formats the diagnostic as <c>path(line,col): severity RBxxxx: message</c>.</summary>
    /// <returns>The canonical single-line rendering.</returns>
    internal string Format() {
        string severity = Severity switch {
            BakeSeverity.Warning => "warning",
            BakeSeverity.Info => "info",
            _ => "error",
        };

        string location = FilePath switch {
            null => string.Empty,
            _ when Line is int line && Column is int column => $"{FilePath}({line},{column}): ",
            _ when Line is int line => $"{FilePath}({line}): ",
            _ => $"{FilePath}: ",
        };

        return $"{location}{severity} {Code}: {Message}";
    }
}
