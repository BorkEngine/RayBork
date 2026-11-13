namespace RayBork.Internal;

/// <summary>
/// The built-in loader that turns one packed shader entry into a caller-owned <see cref="Shader"/>
/// material over the cached internal <see cref="ShaderProgram"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every successful load returns a distinct material. The program load uses the engine cache and
/// reference counting, so every material over the same path shares one GPU program while each keeps
/// its own uniform cursor and texture bindings.
/// </para>
/// <para>
/// The caller owns the returned material and must dispose it exactly once. The load's own program
/// reference is released as soon as the material acquired its own, whether the material was created or
/// the load was canceled first; an already-canceled caller never receives a material.
/// </para>
/// </remarks>
internal sealed class ShaderMaterialLoader : IAssetLoader<Shader> {
    /// <summary>
    /// The shared loader instance.
    /// </summary>
    internal static readonly ShaderMaterialLoader Instance = new();

    private ShaderMaterialLoader() {
    }

    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Shader;

    /// <inheritdoc/>
    public async Task<Shader> LoadAsync(AssetLoadContext context) {
        ArgumentNullException.ThrowIfNull(context);

        ShaderProgram program = await context.LoadAsync<ShaderProgram>(
            context.CanonicalPath,
            context.CancellationToken).ConfigureAwait(false);
        try {
            context.CancellationToken.ThrowIfCancellationRequested();
            Shader material = program.CreateMaterial();
            if (context.CancellationToken.IsCancellationRequested) {
                material.Dispose();
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            return material;
        } finally {
            try {
                program.Lifetime.ReleaseOwnerReference();
            } catch (ObjectDisposedException) {
                // Shutdown force-disposed the program after the material acquired its reference.
            }
        }
    }
}
