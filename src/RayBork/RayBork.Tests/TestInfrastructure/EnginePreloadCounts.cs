namespace RayBork.Tests;

/// <summary>
/// The GPU upload and destroy counts the app's startup preload produces, so app-level tests assert one
/// shared expectation instead of repeating hard-coded numbers.
/// </summary>
/// <remarks>
/// <para>
/// The app first preloads <see cref="DEFAULTS"/> GPU-uploaded assets into <see cref="EngineDefaults"/>:
/// the <c>/shaders/mesh</c> program plus the white and error textures. The app also preloads the
/// default and monospaced fonts there, but a font load is CPU-only: it queues no upload and no
/// destroy, so they add nothing to these counts. The engine primitive assets then add
/// <see cref="ADDITIONAL_PRIMITIVE_PROGRAMS"/> programs (the <c>/shaders/primitive</c> and
/// <c>/shaders/msdf</c> programs; the mesh program is already cached) and
/// <see cref="PRIMITIVE_MESHES"/> procedural mesh uploads.
/// </para>
/// <para>
/// The three engine materials create no GPU resource of their own, so they add nothing to these counts.
/// Shutdown releases exactly the preloaded resources, once each.
/// </para>
/// </remarks>
internal static class EnginePreloadCounts {
    /// <summary>The GPU-uploaded assets preloaded into <see cref="EngineDefaults"/>.</summary>
    internal const int DEFAULTS = 3;

    /// <summary>The primitive-asset programs the defaults do not already hold.</summary>
    internal const int ADDITIONAL_PRIMITIVE_PROGRAMS = 2;

    /// <summary>The procedural primitive meshes.</summary>
    internal const int PRIMITIVE_MESHES = 8;

    /// <summary>The total GPU uploads the startup preload performs.</summary>
    internal const int TOTAL_UPLOADS = DEFAULTS + ADDITIONAL_PRIMITIVE_PROGRAMS + PRIMITIVE_MESHES;
}
