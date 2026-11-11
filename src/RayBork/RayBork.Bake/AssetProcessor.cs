using PolyType;
using RayBork.Bake.Processors;

namespace RayBork.Bake;

/// <summary>
/// The convenience base for a processor whose settings are bound from a sidecar table.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SidecarShape"/> is <c>TSidecar.GetTypeShape()</c>, so the sidecar is bound through the
/// shared shape-driven TOML binder without reflection. The default policy is
/// <see cref="SidecarPolicy.Required"/>; override <see cref="Policy"/> for an optional sidecar, in
/// which case <see cref="CreateDefaultSidecar"/> supplies the in-memory value for a missing file.
/// The bake never writes a sidecar.
/// </para>
/// <para>
/// The framework invokes the sidecar overload of <see cref="Process"/>; the interface member throws
/// because a sidecar processor cannot run without its bound settings.
/// </para>
/// </remarks>
/// <typeparam name="TSidecar">The sidecar type, which must carry a compiled shape.</typeparam>
public abstract class AssetProcessor<TSidecar> : IAssetProcessor, ITypedAssetProcessor
    where TSidecar : IShapeable<TSidecar> {
    /// <inheritdoc/>
    /// <remarks>The default is the processor CLR full name; override it for a stable short name.</remarks>
    public virtual string Name => GetType().FullName ?? GetType().Name;

    /// <inheritdoc/>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc/>
    public virtual SidecarPolicy Policy => SidecarPolicy.Required;

    /// <inheritdoc/>
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();

    /// <inheritdoc/>
    void IAssetProcessor.Process(AssetProcessorContext context)
        => throw new InvalidOperationException(
            $"'{GetType().FullName}' is a sidecar processor; the bake invokes it with its bound "
                + $"'{typeof(TSidecar).FullName}' sidecar.");

    /// <summary>Creates the in-memory default sidecar for a missing optional sidecar.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <returns>The default sidecar instance.</returns>
    protected abstract TSidecar CreateDefaultSidecar(AssetProcessorContext context);

    /// <summary>Processes one source with its bound sidecar.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <param name="sidecar">The bound or defaulted sidecar.</param>
    protected abstract void Process(AssetProcessorContext context, TSidecar sidecar);

    object ITypedAssetProcessor.CreateDefaultSidecar(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        return CreateDefaultSidecar(context);
    }

    void ITypedAssetProcessor.Process(AssetProcessorContext context, object sidecar) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);
        Process(context, (TSidecar)sidecar);
    }
}
