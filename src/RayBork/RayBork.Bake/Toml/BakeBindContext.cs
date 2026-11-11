using RayBork.Bake.Diagnostics;
using Tomlyn.Model;
using Tomlyn.Syntax;

namespace RayBork.Bake.Toml;

/// <summary>
/// The per-source state used while binding one TOML document.
/// </summary>
internal sealed class BakeBindContext {
    /// <summary>Initializes a binding context.</summary>
    /// <param name="root">The parsed document root.</param>
    /// <param name="syntax">The syntax index for spans.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="filePath">The diagnostic source path.</param>
    /// <param name="assetPath">The canonical asset path, when known.</param>
    internal BakeBindContext(
        TomlTable root,
        TomlSyntaxIndex syntax,
        BakeDiagnostics diagnostics,
        string filePath,
        string? assetPath) {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Root = root;
        Syntax = syntax;
        Diagnostics = diagnostics;
        FilePath = filePath;
        AssetPath = assetPath;
    }

    /// <summary>Gets the parsed document root.</summary>
    internal TomlTable Root { get; }

    /// <summary>Gets the syntax index.</summary>
    internal TomlSyntaxIndex Syntax { get; }

    /// <summary>Gets the diagnostic bag.</summary>
    internal BakeDiagnostics Diagnostics { get; }

    /// <summary>Gets the diagnostic source path.</summary>
    internal string FilePath { get; }

    /// <summary>Gets the canonical asset path, when known.</summary>
    internal string? AssetPath { get; }

    /// <summary>Adds a binding error located at the value or key span of a path.</summary>
    /// <param name="message">The message.</param>
    /// <param name="path">The TOML path.</param>
    /// <param name="memberPath">The member path.</param>
    internal void AddError(string message, TomlPath path, string? memberPath) {
        int? line = null;
        int? column = null;
        if (TryGetSpan(path, out SourceSpan span)) {
            line = span.Start.Line + 1;
            column = span.Start.Column + 1;
        }

        Diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TOML_INVALID,
            message,
            FilePath,
            line,
            column,
            AssetPath,
            memberPath);
    }

    /// <summary>Adds a binding error located at the key span, falling back to the value span.</summary>
    /// <param name="message">The message.</param>
    /// <param name="path">The TOML path.</param>
    /// <param name="memberPath">The member path.</param>
    internal void AddKeyError(string message, TomlPath path, string? memberPath) {
        int? line = null;
        int? column = null;
        if (Syntax.TryGetKeySpan(path, out SourceSpan keySpan)
            || Syntax.TryGetValueSpan(path, out keySpan)) {
            line = keySpan.Start.Line + 1;
            column = keySpan.Start.Column + 1;
        }

        Diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TOML_INVALID,
            message,
            FilePath,
            line,
            column,
            AssetPath,
            memberPath);
    }

    /// <summary>Adds a binding error without a source position.</summary>
    /// <param name="message">The message.</param>
    /// <param name="memberPath">The member path, when known.</param>
    internal void AddError(string message, string? memberPath = null)
        => Diagnostics.Add(
            BakeSeverity.Error,
            BakeDiagnosticCodes.TOML_INVALID,
            message,
            FilePath,
            assetPath: AssetPath,
            memberPath: memberPath);

    private bool TryGetSpan(TomlPath path, out SourceSpan span)
        => Syntax.TryGetValueSpan(path, out span) || Syntax.TryGetKeySpan(path, out span);
}
