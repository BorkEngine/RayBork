namespace RayBork.Bake.Processors;

/// <summary>The raw fallback processor for unclaimed non-TOML sources.</summary>
internal sealed class RawAssetProcessor : IAssetProcessor
{
    /// <inheritdoc/>
    public string Name => "raw";

    /// <summary>Gets the fallback marker <c>*</c>, which is not a source suffix.</summary>
    public IReadOnlyList<string> Extensions => ["*"];

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public PolyType.ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        byte[] payload = File.ReadAllBytes(context.SourcePath);
        context.Outputs.AddData(context.BaseAssetPath, payload);
    }
}
