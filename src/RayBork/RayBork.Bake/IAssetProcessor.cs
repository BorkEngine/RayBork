using PolyType;

namespace RayBork.Bake;

/// <summary>Processor contract: name, extensions, sidecar policy, and processing.</summary>
/// <remarks>
/// Sidecar processors derive from <see cref="AssetProcessor{TSidecar}"/>; processors are reused
/// and must not retain per-source state.
/// </remarks>
public interface IAssetProcessor
{
    /// <summary>Gets the stable name used in diagnostics and configuration hashing.</summary>
    string Name { get; }

    /// <summary>Gets dotless lowercase extensions; compound extensions match as one suffix.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the sidecar policy.</summary>
    SidecarPolicy Policy { get; }

    /// <summary>
    /// Gets the compiled sidecar shape, or <see langword="null"/> when the policy is
    /// <see cref="SidecarPolicy.None"/>.
    /// </summary>
    ITypeShape? SidecarShape { get; }

    /// <summary>Processes one source.</summary>
    void Process(AssetProcessorContext context);
}
