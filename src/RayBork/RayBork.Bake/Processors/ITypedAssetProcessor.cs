namespace RayBork.Bake.Processors;

/// <summary>
/// The narrow bridge over the public required-sidecar base so the framework can create and invoke a
/// typed sidecar without knowing its generic argument.
/// </summary>
internal interface ITypedAssetProcessor {
    /// <summary>Creates the in-memory default sidecar for a missing optional sidecar.</summary>
    /// <param name="context">The per-source context.</param>
    /// <returns>The default sidecar instance.</returns>
    object CreateDefaultSidecar(AssetProcessorContext context);

    /// <summary>Processes one source with its bound sidecar.</summary>
    /// <param name="context">The per-source context.</param>
    /// <param name="sidecar">The bound sidecar instance.</param>
    void Process(AssetProcessorContext context, object sidecar);
}
