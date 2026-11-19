using PolyType;
using RayBork.Bake.Processors;

namespace RayBork.Bake;

/// <summary>The base for a processor whose settings bind from a sidecar table.</summary>
/// <remarks>
/// The default <see cref="Policy"/> is <see cref="SidecarPolicy.Required"/>; no sidecar is ever
/// written. The interface <see cref="IAssetProcessor.Process"/> throws without bound settings.
/// </remarks>
public abstract class AssetProcessor<TSidecar> : IAssetProcessor, ITypedAssetProcessor
    where TSidecar : IShapeable<TSidecar>
{
    /// <inheritdoc/>
    /// <remarks>Defaults to the processor CLR full name.</remarks>
    public virtual string Name => GetType().FullName ?? GetType().Name;

    /// <inheritdoc/>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc/>
    public virtual SidecarPolicy Policy => SidecarPolicy.Required;

    /// <inheritdoc/>
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();

    /// <inheritdoc/>
    void IAssetProcessor.Process(AssetProcessorContext context) =>
        throw new InvalidOperationException(
            $"'{GetType().FullName}' is a sidecar processor; the bake invokes it with its bound "
                + $"'{typeof(TSidecar).FullName}' sidecar."
        );

    /// <summary>Creates the default sidecar for a missing optional sidecar.</summary>
    protected abstract TSidecar CreateDefaultSidecar(AssetProcessorContext context);

    /// <summary>Processes one source with its bound sidecar.</summary>
    protected abstract void Process(AssetProcessorContext context, TSidecar sidecar);

    object ITypedAssetProcessor.CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateDefaultSidecar(context);
    }

    void ITypedAssetProcessor.Process(AssetProcessorContext context, object sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);
        Process(context, (TSidecar)sidecar);
    }
}
