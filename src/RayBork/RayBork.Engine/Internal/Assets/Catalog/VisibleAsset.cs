namespace RayBork.Internal;

/// <summary>
/// One visible pack entry: the mounted archive that supplies it and the validated entry itself.
/// </summary>
/// <remarks>
/// The catalog's visible stack maps every canonical path to the entry from the last mounted pack that
/// defines it, so a lookup already encodes the shadowing order. The archive is mounted for the catalog
/// lifetime and outlives every visible-stack snapshot, so the pair is safe to hand to the loader
/// thread.
/// </remarks>
/// <param name="Archive">The mounted archive that owns the entry and its payload bytes.</param>
/// <param name="Entry">The validated index entry resolved for the canonical path.</param>
internal readonly record struct VisibleAsset(PackArchive Archive, PackEntry Entry);
