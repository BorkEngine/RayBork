namespace RayBork.Internal;

/// <summary>
/// The built-in loader that turns one packed shader entry into a caller-owned <see cref="Shader"/>
/// material over the cached internal <see cref="ShaderProgram"/>.
/// </summary>
/// <remarks>
/// Every load returns a distinct material; materials over the same path share one reference-counted
/// GPU program. The caller owns the material and must dispose it exactly once.
/// </remarks>
internal sealed class ShaderMaterialLoader : IAssetLoader<Shader>
{
    /// <summary>The shared loader instance.</summary>
    internal static readonly ShaderMaterialLoader Instance = new();

    private ShaderMaterialLoader() { }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Shader;

    /// <inheritdoc/>
    public async Task<Shader> LoadAsync(AssetLoadContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ShaderProgram program = await context
            .LoadAsync<ShaderProgram>(context.CanonicalPath, context.CancellationToken)
            .ConfigureAwait(false);
        try
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            Shader material = program.CreateMaterial();
            if (context.CancellationToken.IsCancellationRequested)
            {
                material.Dispose();
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            return material;
        }
        finally
        {
            try
            {
                program.Lifetime.ReleaseOwnerReference();
            }
            catch (ObjectDisposedException)
            {
                // Shutdown force-disposed the program after the material acquired its reference.
            }
        }
    }
}
