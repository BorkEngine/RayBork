using PolyType;

namespace RayBork.Bake;

/// <summary>
/// The build-time processor contract: one stable name, an extension set, an optional settings
/// sidecar, and synchronous processing code.
/// </summary>
/// <remarks>
/// <para>
/// A processor with no sidecar implements this interface directly with
/// <see cref="SidecarPolicy.None"/>. A processor with a sidecar derives from
/// <see cref="AssetProcessor{TSidecar}"/>.
/// </para>
/// <para>
/// Processors run synchronously on the bake thread and may be reused for every source they own. They
/// must not retain per-source state. All output goes through <see cref="AssetProcessorContext"/>.
/// </para>
/// </remarks>
public interface IAssetProcessor {
    /// <summary>Gets the stable processor name used in diagnostics and configuration hashing.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the source extensions this processor owns. Extensions are normalized to lowercase without
    /// a leading dot; a compound extension such as <c>shader.toml</c> is matched as one suffix.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the sidecar policy.</summary>
    SidecarPolicy Policy { get; }

    /// <summary>
    /// Gets the compiled sidecar shape, or <see langword="null"/> when the policy is
    /// <see cref="SidecarPolicy.None"/>.
    /// </summary>
    ITypeShape? SidecarShape { get; }

    /// <summary>Processes one source.</summary>
    /// <param name="context">The per-source context.</param>
    void Process(AssetProcessorContext context);
}
