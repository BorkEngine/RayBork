using System.Text;
using RayBork.Bake.Diagnostics;

namespace RayBork.Bake.Sources;

/// <summary>Reads TOML sources as strict UTF-8 with an optional BOM.</summary>
internal static class BakeSourceText
{
    private static readonly UTF8Encoding _strictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    /// <summary>Reads one source file, reporting read and UTF-8 failures.</summary>
    internal static bool TryRead(
        string fullPath,
        string diagnosticPath,
        BakeDiagnostics diagnostics,
        out string text
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        text = string.Empty;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or ArgumentException
            )
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"Failed to read '{diagnosticPath}': {exception.Message}",
                diagnosticPath
            );
            return false;
        }

        ReadOnlySpan<byte> content = bytes;
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            content = content[3..];
        }

        try
        {
            text = _strictUtf8.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(
                BakeSeverity.Error,
                BakeDiagnosticCodes.SOURCE_INVALID,
                $"'{diagnosticPath}' is not valid UTF-8.",
                diagnosticPath
            );
            return false;
        }
    }
}
