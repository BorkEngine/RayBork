using System.Text;
using RayBork.Internal;

namespace RayBork.Bake.Models;

/// <summary>Sanitizes part names and allocates collision-free output paths.</summary>
/// <remarks>
/// Each part reserves <c>&lt;base&gt;/&lt;slug&gt;</c>, <c>&lt;base&gt;/&lt;slug&gt;-mesh</c>, and
/// <c>&lt;base&gt;/&lt;slug&gt;-collision</c> together, so all three are checked for collisions. A
/// slug is at most 64 characters.
/// </remarks>
internal sealed class PartSlugResolver
{
    private const int MAX_SLUG_LENGTH = 64;
    private const int MIN_SLUG_LENGTH = 8;
    private const string PAYLOAD_SUFFIX = "-mesh";
    private const string COLLISION_SUFFIX = "-collision";
    private const string FALLBACK_SLUG = "part";

    private readonly string _basePath;
    private readonly int _maxSlugLength;
    private readonly HashSet<string> _usedOutputPaths = new(StringComparer.Ordinal);

    private PartSlugResolver(string basePath, int maxSlugLength)
    {
        _basePath = basePath;
        _maxSlugLength = maxSlugLength;
    }

    /// <summary>Creates a resolver for one model base path.</summary>
    /// <returns><see langword="true"/> when a resolver was created.</returns>
    internal static bool TryCreate(
        string basePath,
        out PartSlugResolver? resolver,
        out string? reason
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basePath);

        resolver = null;
        int available = AssetPath.MAX_PATH_BYTES - basePath.Length - 1 - COLLISION_SUFFIX.Length;
        if (available < MIN_SLUG_LENGTH)
        {
            reason = $"The model path '{basePath}' leaves no room for a part path.";
            return false;
        }

        resolver = new PartSlugResolver(basePath, Math.Min(MAX_SLUG_LENGTH, available));
        reason = null;
        return true;
    }

    /// <summary>Allocates the next part, payload, and collision paths.</summary>
    /// <returns>The canonical part slug, used as the part name.</returns>
    internal string Allocate(
        string? name,
        out string meshPath,
        out string payloadPath,
        out string collisionPath
    )
    {
        string stem = Sanitize(name, _maxSlugLength);
        for (int suffix = 1; ; suffix++)
        {
            string candidate = suffix == 1 ? stem : $"{stem}-{suffix}";
            string slug = Fit(candidate, _maxSlugLength);
            string candidateMeshPath = _basePath + "/" + slug;
            string candidatePayloadPath = candidateMeshPath + PAYLOAD_SUFFIX;
            string candidateCollisionPath = candidateMeshPath + COLLISION_SUFFIX;
            if (
                _usedOutputPaths.Contains(candidateMeshPath)
                || _usedOutputPaths.Contains(candidatePayloadPath)
                || _usedOutputPaths.Contains(candidateCollisionPath)
            )
            {
                continue;
            }

            _usedOutputPaths.Add(candidateMeshPath);
            _usedOutputPaths.Add(candidatePayloadPath);
            _usedOutputPaths.Add(candidateCollisionPath);
            meshPath = candidateMeshPath;
            payloadPath = candidatePayloadPath;
            collisionPath = candidateCollisionPath;
            return slug;
        }
    }

    /// <summary>Maps a source name to a non-empty lowercase segment of <c>a-z0-9_-</c>.</summary>
    internal static string Sanitize(string? name, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 1);

        var builder = new StringBuilder();
        foreach (char character in name ?? string.Empty)
        {
            char lowered = character is >= 'A' and <= 'Z'
                ? (char)(character + ('a' - 'A'))
                : character;
            if (lowered is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                builder.Append(lowered);
            }
            else if (builder.Length == 0 || builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        string slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
        {
            return Fit(FALLBACK_SLUG, maxLength);
        }

        return Fit(slug, maxLength);
    }

    private static string Fit(string slug, int maxLength)
    {
        if (slug.Length <= maxLength)
        {
            return slug;
        }

        string truncated = slug[..maxLength].TrimEnd('-');
        return truncated.Length > 0 ? truncated : FALLBACK_SLUG[..maxLength];
    }
}
