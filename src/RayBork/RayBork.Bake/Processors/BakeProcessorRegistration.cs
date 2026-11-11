using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>
/// The framework registration over one public <see cref="IAssetProcessor"/>.
/// </summary>
internal sealed class BakeProcessorRegistration {
    private readonly IAssetProcessor _processor;

    /// <summary>Initializes a registration.</summary>
    /// <param name="processor">The public processor instance.</param>
    internal BakeProcessorRegistration(IAssetProcessor processor) {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>Gets the public processor.</summary>
    internal IAssetProcessor Processor => _processor;

    /// <summary>Gets the processor CLR type used in configuration hashing.</summary>
    internal Type ProcessorType => _processor.GetType();

    /// <summary>Gets the stable processor name.</summary>
    internal string Name => _processor.Name;

    /// <summary>Gets the declared extensions in declaration order.</summary>
    internal IReadOnlyList<string> Extensions => _processor.Extensions;

    /// <summary>Gets the sidecar policy.</summary>
    internal SidecarPolicy Policy => _processor.Policy;

    /// <summary>Gets the sidecar shape, or <see langword="null"/>.</summary>
    internal ITypeShape? SidecarShape => _processor.SidecarShape;

    /// <summary>Creates the in-memory default sidecar for a missing optional sidecar.</summary>
    /// <param name="context">The per-source context.</param>
    /// <returns>The default sidecar.</returns>
    internal object CreateDefaultSidecar(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        if (_processor is not ITypedAssetProcessor typed) {
            throw new InvalidOperationException(
                $"'{Name}' declares a sidecar policy but does not derive from AssetProcessor<TSidecar>.");
        }

        return typed.CreateDefaultSidecar(context);
    }

    /// <summary>Processes one source with its optional bound sidecar.</summary>
    /// <param name="context">The per-source context.</param>
    /// <param name="sidecar">The bound sidecar, or <see langword="null"/> for a sidecar-free processor.</param>
    internal void Process(AssetProcessorContext context, object? sidecar) {
        ArgumentNullException.ThrowIfNull(context);

        if (_processor.Policy == SidecarPolicy.None) {
            _processor.Process(context);
            return;
        }

        if (_processor is not ITypedAssetProcessor typed) {
            throw new InvalidOperationException(
                $"'{Name}' declares a sidecar policy but does not derive from AssetProcessor<TSidecar>.");
        }

        if (sidecar is null) {
            throw new InvalidOperationException(
                $"'{Name}' requires a sidecar, but the framework passed none.");
        }

        typed.Process(context, sidecar);
    }
}

/// <summary>The processor that owns one matched file and the extension it matched.</summary>
/// <param name="Processor">The owning processor.</param>
/// <param name="Extension">The normalized matched extension, without a leading dot.</param>
internal readonly record struct BakeProcessorMatch(
    BakeProcessorRegistration Processor,
    string Extension);
