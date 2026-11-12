namespace RayBork.Internal;

/// <summary>
/// An asset catalog that owns the procedural asset service used by <see cref="EngineContext"/>.
/// </summary>
/// <remarks>
/// The catalog is the only internal object the app wires into <see cref="EngineContext"/> that holds
/// the GPU asset bridge, so it creates and stops the service and exposes it through this seam. The
/// engine context requires the provider; a substitute catalog without procedural support is an
/// engine programming error.
/// </remarks>
internal interface IProceduralAssetProvider {
    /// <summary>
    /// Gets the service behind the procedural mesh and texture builders.
    /// </summary>
    ProceduralAssetService ProceduralAssets { get; }
}
