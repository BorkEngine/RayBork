namespace RayBork.Bake.Processors;

/// <summary>A bridge over the required-sidecar base for a runtime-typed sidecar.</summary>
internal interface ITypedAssetProcessor
{
    /// <summary>Creates the in-memory default sidecar for a missing optional sidecar.</summary>
    object CreateDefaultSidecar(AssetProcessorContext context);

    /// <summary>Processes one source with its bound sidecar instance.</summary>
    void Process(AssetProcessorContext context, object sidecar);
}
