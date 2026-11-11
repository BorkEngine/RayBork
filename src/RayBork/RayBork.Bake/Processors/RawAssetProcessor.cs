namespace RayBork.Bake.Processors;

/// <summary>
/// The raw fallback processor: it copies any unclaimed non-TOML source to a raw pack entry.
/// </summary>
internal sealed class RawAssetProcessor : IAssetProcessor {
    /// <inheritdoc/>
    public string Name => "raw";

    /// <inheritdoc/>
    /// <remarks><c>*</c> is the registry's fallback marker, not a source suffix.</remarks>
    public IReadOnlyList<string> Extensions => ["*"];

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public PolyType.ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);
        byte[] payload = File.ReadAllBytes(context.SourcePath);
        context.Outputs.AddData(context.BaseAssetPath, payload);
    }
}
