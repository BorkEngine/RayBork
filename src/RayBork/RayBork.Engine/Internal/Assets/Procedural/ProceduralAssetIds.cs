using System.Globalization;

namespace RayBork.Internal;

/// <summary>Reserves procedural asset paths and allocates their ids.</summary>
/// <remarks>
/// A procedural path is <c>_procedural/mesh/{id}</c> or <c>_procedural/texture/{id}</c>, allocated
/// from one process-wide counter per kind. The <c>_procedural</c> prefix is reserved: the catalog
/// reports such paths as missing and the bake must not write them.
/// </remarks>
internal static class ProceduralAssetIds
{
    /// <summary>The first segment of every reserved procedural path.</summary>
    internal const string RESERVED_PREFIX = "_procedural";

    private static int _nextMeshId;
    private static int _nextTextureId;

    /// <summary>Allocates a process-unique procedural mesh id.</summary>
    internal static int NextMeshId() => Interlocked.Increment(ref _nextMeshId);

    /// <summary>Allocates a process-unique procedural texture id.</summary>
    internal static int NextTextureId() => Interlocked.Increment(ref _nextTextureId);

    /// <summary>Builds the canonical path of a procedural mesh.</summary>
    internal static string MeshPath(int id) =>
        RESERVED_PREFIX + "/mesh/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds the canonical path of a procedural texture.</summary>
    internal static string TexturePath(int id) =>
        RESERVED_PREFIX + "/texture/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tests whether a canonical path is inside the reserved procedural prefix.</summary>
    internal static bool IsReserved(string canonicalPath)
    {
        if (!canonicalPath.StartsWith(RESERVED_PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        return canonicalPath.Length == RESERVED_PREFIX.Length
            || canonicalPath[RESERVED_PREFIX.Length] == '/';
    }
}
