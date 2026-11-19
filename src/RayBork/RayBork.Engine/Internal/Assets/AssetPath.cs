namespace RayBork.Internal;

/// <summary>Normalizes and validates canonical asset paths.</summary>
/// <remarks>
/// A canonical path is lowercase ASCII with <c>/</c> separators, no leading or trailing slash, no
/// empty, <c>.</c>, or <c>..</c> segments, and only <c>a-z</c>, <c>0-9</c>, <c>_</c>, and <c>-</c>
/// within a segment. Public input may carry one leading slash and is lowercased, backslashes are
/// rejected, and pack index entries are validated without normalization.
/// </remarks>
internal static class AssetPath
{
    /// <summary>The longest accepted asset path, in UTF-8 bytes.</summary>
    internal const int MAX_PATH_BYTES = 4096;

    /// <summary>Normalizes a public path input and validates the canonical result.</summary>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    internal static string Normalize(string? path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid asset path '{path}': backslashes are not supported; use forward slashes.",
                nameof(path)
            );
        }

        ReadOnlySpan<char> candidate = path;
        if (candidate.Length > 0 && candidate[0] == '/')
        {
            candidate = candidate[1..];
        }

        string normalized = candidate.ToString().ToLowerInvariant();
        if (!TryValidateCanonical(normalized, out string? reason))
        {
            throw new ArgumentException($"Invalid asset path '{path}': {reason}.", nameof(path));
        }

        return normalized;
    }

    /// <summary>Converts a canonical path to its public form, with a leading slash.</summary>
    /// <exception cref="ArgumentException"><paramref name="canonicalPath"/> is not canonical.</exception>
    internal static string ToPublic(string canonicalPath)
    {
        if (!TryValidateCanonical(canonicalPath, out string? reason))
        {
            throw new ArgumentException(
                $"Invalid canonical asset path '{canonicalPath}': {reason}.",
                nameof(canonicalPath)
            );
        }

        return "/" + canonicalPath;
    }

    /// <summary>Tests whether a path already has the canonical form.</summary>
    internal static bool IsCanonical(string? path) => TryValidateCanonical(path, out _);

    /// <summary>Validates a path without normalizing it.</summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="reason">The failure reason, or <see langword="null"/> on success.</param>
    internal static bool TryValidateCanonical(string? path, out string? reason)
    {
        if (path is null)
        {
            reason = "the path is null";
            return false;
        }

        if (path.Length == 0)
        {
            reason = "the path is empty";
            return false;
        }

        if (path.Length > MAX_PATH_BYTES)
        {
            reason = $"the path exceeds {MAX_PATH_BYTES} bytes";
            return false;
        }

        if (path[0] == '/' || path[^1] == '/')
        {
            reason = "the path has a leading or trailing '/'";
            return false;
        }

        int segmentStart = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i != path.Length && path[i] != '/')
            {
                continue;
            }

            int segmentLength = i - segmentStart;
            if (segmentLength == 0)
            {
                reason = "the path contains an empty segment";
                return false;
            }

            if (segmentLength == 1 && path[segmentStart] == '.')
            {
                reason = "the path contains a '.' segment";
                return false;
            }

            if (segmentLength == 2 && path[segmentStart] == '.' && path[segmentStart + 1] == '.')
            {
                reason = "the path contains a '..' segment";
                return false;
            }

            for (int j = segmentStart; j < i; j++)
            {
                char character = path[j];
                if (!IsCanonicalCharacter(character))
                {
                    reason = $"the path contains the invalid character '{character}'";
                    return false;
                }
            }

            segmentStart = i + 1;
        }

        reason = null;
        return true;
    }

    private static bool IsCanonicalCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-';
}
