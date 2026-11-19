namespace RayBork.Bake.Diagnostics;

/// <summary>The severity of one bake diagnostic.</summary>
internal enum BakeSeverity
{
    /// <summary>A failure that prevents publishing the pack.</summary>
    Error,

    /// <summary>A condition that fails only when warnings are treated as errors.</summary>
    Warning,

    /// <summary>Informational output.</summary>
    Info,
}

/// <summary>One bake diagnostic with an optional source location.</summary>
/// <remarks>Line and column are 1-based when present.</remarks>
internal readonly record struct BakeDiagnostic(
    BakeSeverity Severity,
    string Code,
    string Message,
    string? FilePath,
    int? Line,
    int? Column,
    string? AssetPath,
    string? MemberPath
)
{
    /// <summary>Formats the diagnostic as <c>path(line,col): severity RBxxxx: message</c>.</summary>
    internal string Format()
    {
        string severity = Severity switch
        {
            BakeSeverity.Warning => "warning",
            BakeSeverity.Info => "info",
            _ => "error",
        };

        string location = FilePath switch
        {
            null => string.Empty,
            _ when Line is int line && Column is int column => $"{FilePath}({line},{column}): ",
            _ when Line is int line => $"{FilePath}({line}): ",
            _ => $"{FilePath}: ",
        };

        return $"{location}{severity} {Code}: {Message}";
    }
}
