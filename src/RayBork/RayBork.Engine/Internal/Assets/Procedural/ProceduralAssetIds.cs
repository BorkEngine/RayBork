using System.Globalization;

namespace RayBork.Internal;

/// <summary>
/// Reserves the synthetic canonical paths of procedural assets and allocates their ids.
/// </summary>
/// <remarks>
/// <para>
/// A procedural asset has no pack entry, so its public path is a reserved synthetic path of the form
/// <c>/_procedural/mesh/{id}</c> or <c>/_procedural/texture/{id}</c>. Each kind has one process-wide
/// counter, so two procedural assets never share a path even after the earlier one is disposed.
/// </para>
/// <para>
/// The <c>_procedural</c> prefix is reserved. The pack catalog reports a path under it as missing
/// instead of resolving a pack entry that happens to use it, and the bake must not write such a path.
/// The public path carries the leading slash; the canonical form used by the catalog and the GPU
/// upload does not.
/// </para>
/// </remarks>
internal static class ProceduralAssetIds {
    /// <summary>The first segment of every reserved procedural path.</summary>
    internal const string RESERVED_PREFIX = "_procedural";

    private static int _nextMeshId;
    private static int _nextTextureId;

    /// <summary>Allocates the next procedural mesh id.</summary>
    /// <returns>A positive id that was never allocated before in this process.</returns>
    internal static int NextMeshId() => Interlocked.Increment(ref _nextMeshId);

    /// <summary>Allocates the next procedural texture id.</summary>
    /// <returns>A positive id that was never allocated before in this process.</returns>
    internal static int NextTextureId() => Interlocked.Increment(ref _nextTextureId);

    /// <summary>Builds the canonical path of a procedural mesh.</summary>
    /// <param name="id">The id from <see cref="NextMeshId"/>.</param>
    /// <returns>The canonical path <c>_procedural/mesh/{id}</c> without a leading slash.</returns>
    internal static string MeshPath(int id) => RESERVED_PREFIX + "/mesh/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds the canonical path of a procedural texture.</summary>
    /// <param name="id">The id from <see cref="NextTextureId"/>.</param>
    /// <returns>The canonical path <c>_procedural/texture/{id}</c> without a leading slash.</returns>
    internal static string TexturePath(int id) => RESERVED_PREFIX + "/texture/" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tests whether a canonical path is inside the reserved procedural prefix.</summary>
    /// <param name="canonicalPath">A canonical path without a leading slash.</param>
    /// <returns><see langword="true"/> when the catalog must never resolve the path.</returns>
    internal static bool IsReserved(string canonicalPath) {
        if (!canonicalPath.StartsWith(RESERVED_PREFIX, StringComparison.Ordinal)) {
            return false;
        }

        return canonicalPath.Length == RESERVED_PREFIX.Length
            || canonicalPath[RESERVED_PREFIX.Length] == '/';
    }
}
