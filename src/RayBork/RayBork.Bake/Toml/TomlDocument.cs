using RayBork.Bake.Diagnostics;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace RayBork.Bake.Toml;

/// <summary>The parsed root table, text, and syntax index of one TOML file.</summary>
internal sealed class TomlDocument {
    private TomlDocument(string text, TomlTable root, TomlSyntaxIndex syntax) {
        Text = text;
        Root = root;
        Syntax = syntax;
    }

    /// <summary>Gets the strict UTF-8 source text.</summary>
    internal string Text { get; }

    /// <summary>Gets the parsed root table.</summary>
    internal TomlTable Root { get; }

    /// <summary>Gets the syntax index.</summary>
    internal TomlSyntaxIndex Syntax { get; }

    /// <summary>Parses and validates one TOML document.</summary>
    /// <param name="text">The source text.</param>
    /// <param name="filePath">The diagnostic source path.</param>
    /// <param name="diagnostics">The diagnostic bag.</param>
    /// <param name="document">The document when parsing succeeds.</param>
    /// <returns><see langword="true"/> when the document parsed without errors.</returns>
    internal static bool TryParse(
        string text,
        string filePath,
        BakeDiagnostics diagnostics,
        out TomlDocument? document) {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        document = null;
        DocumentSyntax syntaxDocument;
        try {
            syntaxDocument = SyntaxParser.Parse(text, filePath, validate: true);
        } catch (TomlException exception) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                exception.Message,
                filePath,
                exception.Line,
                exception.Column);
            return false;
        }

        if (syntaxDocument.HasErrors) {
            foreach (DiagnosticMessage message in syntaxDocument.Diagnostics) {
                if (message.Kind != DiagnosticMessageKind.Error) {
                    continue;
                }

                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    message.Message,
                    filePath,
                    message.Span.Start.Line + 1,
                    message.Span.Start.Column + 1);
            }

            if (!diagnostics.HasErrors) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    "The TOML source is not valid.",
                    filePath);
            }

            return false;
        }

        TomlSyntaxIndex index = TomlSyntaxIndex.Create(syntaxDocument);
        if (index.Duplicates.Count > 0) {
            foreach ((string displayPath, SourceSpan span) in index.Duplicates) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.TOML_INVALID,
                    $"Duplicate TOML key '{displayPath}'.",
                    filePath,
                    span.Start.Line + 1,
                    span.Start.Column + 1);
            }

            return false;
        }

        TomlTable? root;
        try {
            root = TomlSerializer.Deserialize<TomlTable>(
                text,
                new TomlSerializerOptions { SourceName = filePath });
        } catch (TomlException exception) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                exception.Message,
                filePath,
                exception.Line,
                exception.Column);
            return false;
        }

        if (root is null) {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.TOML_INVALID,
                "The TOML source root must be a table.",
                filePath);
            return false;
        }

        document = new TomlDocument(text, root, index);
        return true;
    }
}
