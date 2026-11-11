using System.Text;
using RayBork.Internal;

namespace RayBork.Bake.Models;

/// <summary>
/// Sanitizes part names and allocates deterministic, collision-free part and payload paths.
/// </summary>
/// <remarks>
/// A part contributes two output paths: <c>&lt;base&gt;/&lt;slug&gt;</c> for its mesh descriptor and
/// <c>&lt;base&gt;/&lt;slug&gt;-mesh</c> for its payload. A second part whose slug is
/// <c>&lt;slug&gt;-mesh</c> would collide with the first part's payload, so both paths are reserved
/// and checked together.
/// </remarks>
internal sealed class PartSlugResolver {
    private const int MAX_SLUG_LENGTH = 64;
    private const int MIN_SLUG_LENGTH = 8;
    private const string PAYLOAD_SUFFIX = "-mesh";
    private const string FALLBACK_SLUG = "part";

    private readonly string _basePath;
    private readonly int _maxSlugLength;
    private readonly HashSet<string> _usedOutputPaths = new(StringComparer.Ordinal);

    private PartSlugResolver(string basePath, int maxSlugLength) {
        _basePath = basePath;
        _maxSlugLength = maxSlugLength;
    }

    /// <summary>Creates a resolver for one model base path.</summary>
    /// <param name="basePath">The canonical model base path.</param>
    /// <param name="resolver">The resolver when the base path leaves room for a part.</param>
    /// <param name="reason">The failure reason when the base path is too long.</param>
    /// <returns><see langword="true"/> when a resolver was created.</returns>
    internal static bool TryCreate(
        string basePath,
        out PartSlugResolver? resolver,
        out string? reason) {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        resolver = null;
        int available = AssetPath.MAX_PATH_BYTES - basePath.Length - 1 - PAYLOAD_SUFFIX.Length;
        if (available < MIN_SLUG_LENGTH) {
            reason = $"The model path '{basePath}' leaves no room for a part path.";
            return false;
        }

        resolver = new PartSlugResolver(basePath, Math.Min(MAX_SLUG_LENGTH, available));
        reason = null;
        return true;
    }

    /// <summary>Allocates the next part and payload paths.</summary>
    /// <param name="name">The source name, or <see langword="null"/> when unnamed.</param>
    /// <param name="meshPath">The canonical mesh descriptor path.</param>
    /// <param name="payloadPath">The canonical mesh payload path.</param>
    /// <returns>The canonical part slug, used as the part name.</returns>
    internal string Allocate(string? name, out string meshPath, out string payloadPath) {
        string stem = Sanitize(name, _maxSlugLength);
        for (int suffix = 1; ; suffix++) {
            string candidate = suffix == 1 ? stem : $"{stem}-{suffix}";
            string slug = Fit(candidate, _maxSlugLength);
            string candidateMeshPath = _basePath + "/" + slug;
            string candidatePayloadPath = candidateMeshPath + PAYLOAD_SUFFIX;
            if (_usedOutputPaths.Contains(candidateMeshPath)
                || _usedOutputPaths.Contains(candidatePayloadPath)) {
                continue;
            }

            _usedOutputPaths.Add(candidateMeshPath);
            _usedOutputPaths.Add(candidatePayloadPath);
            meshPath = candidateMeshPath;
            payloadPath = candidatePayloadPath;
            return slug;
        }
    }

    /// <summary>Maps a source name to a canonical lowercase asset path segment.</summary>
    /// <param name="name">The source name, or <see langword="null"/>.</param>
    /// <param name="maxLength">The maximum segment length.</param>
    /// <returns>A non-empty segment of <c>a-z0-9_-</c>.</returns>
    internal static string Sanitize(string? name, int maxLength) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var builder = new StringBuilder();
        foreach (char character in name ?? string.Empty) {
            char lowered = character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
            if (lowered is >= 'a' and <= 'z' or >= '0' and <= '9' or '_') {
                builder.Append(lowered);
            } else if (builder.Length == 0 || builder[^1] != '-') {
                builder.Append('-');
            }
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length == 0) {
            return Fit(FALLBACK_SLUG, maxLength);
        }

        return Fit(slug, maxLength);
    }

    private static string Fit(string slug, int maxLength) {
        if (slug.Length <= maxLength) {
            return slug;
        }

        string truncated = slug[..maxLength].TrimEnd('-');
        return truncated.Length > 0 ? truncated : FALLBACK_SLUG[..maxLength];
    }
}
