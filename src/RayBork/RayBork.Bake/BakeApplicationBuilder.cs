using PolyType;
using RayBork.Bake.Baking;

namespace RayBork.Bake;

/// <summary>
/// Configures one build-time assets executable: project and pack roots, data-type registrations,
/// validators, texture defaults, and processors.
/// </summary>
/// <remarks>
/// <para>
/// Configuration is immutable after <see cref="Build"/>. Semantic configuration problems are
/// diagnostics reported by <see cref="BakeApplication.RunAsync(string[])"/> with exit code 2; they do
/// not throw from <see cref="Build"/>. Null or empty method arguments throw immediately because they
/// are programmer errors.
/// </para>
/// <para>
/// The first <see cref="WithPack(string, string)"/> call declares the main pack. Later packs are
/// overlays whose entries shadow earlier packs at runtime.
/// </para>
/// </remarks>
public sealed class BakeApplicationBuilder {
    private readonly BakeProjectDefinition _definition = new();

    /// <summary>Sets the base for relative roots, pack paths, and diagnostic display paths.</summary>
    /// <param name="path">The project root directory.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    public BakeApplicationBuilder WithProjectRoot(string path) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _definition.SetProjectRoot(path);
        return this;
    }

    /// <summary>Adds one ordered pack; the first added pack is the main pack.</summary>
    /// <param name="packPath">The output-root-relative pack path, for example <c>assets.pck</c>.</param>
    /// <param name="root">The project-root-relative asset root, for example <c>Assets</c>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">An argument is empty or whitespace.</exception>
    public BakeApplicationBuilder WithPack(string packPath, string root) {
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _definition.AddPack(packPath, root);
        return this;
    }

    /// <summary>
    /// Registers the engine authoring descriptor types and their built-in validators; idempotent.
    /// </summary>
    /// <remarks>
    /// M3 registers the internal texture, mesh, model, model-part, shader, shader-uniform, and font
    /// descriptor types with no built-in validators. Runtime loading of custom data remains opt-in per
    /// type, so the application still calls <see cref="RegisterAsset{T}()"/> for its own data types.
    /// </remarks>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder UseDefaultAssets() {
        _definition.UseDefaultAssets();
        return this;
    }

    /// <summary>Registers one data type that a <c>[raybork]</c> TOML may select by full name.</summary>
    /// <typeparam name="T">The type, which must carry a compiled <c>[GenerateShape]</c>.</typeparam>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder RegisterAsset<T>()
        where T : IShapeable<T> {
        _definition.AddAsset<T>();
        return this;
    }

    /// <summary>Registers one data type if necessary and appends one semantic validator.</summary>
    /// <typeparam name="T">The type, which must carry a compiled <c>[GenerateShape]</c>.</typeparam>
    /// <param name="validator">The validator instance.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validator"/> is null.</exception>
    public BakeApplicationBuilder RegisterAsset<T>(IAssetValidator<T> validator)
        where T : IShapeable<T> {
        ArgumentNullException.ThrowIfNull(validator);
        _definition.AddAssetValidator(validator);
        return this;
    }

    /// <summary>
    /// Registers the built-in processors. In M3 this is the raw fallback processor; the texture,
    /// model, shader, and font processor task extends the same built-in registration point.
    /// </summary>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder UseDefaultProcessors() {
        _definition.RequestDefaultProcessors();
        return this;
    }

    /// <summary>Sets the sampler defaults used by textures that have no sidecar.</summary>
    /// <param name="filter">The default filter.</param>
    /// <param name="wrap">The default wrap mode.</param>
    /// <returns>This builder.</returns>
    public BakeApplicationBuilder WithTextureDefaults(TextureFilter filter, TextureWrap wrap) {
        _definition.SetTextureDefaults(filter, wrap);
        return this;
    }

    /// <summary>Registers a custom processor.</summary>
    /// <remarks>
    /// Processors run in registration order. A later registration overrides an earlier one for the
    /// same normalized extension.
    /// </remarks>
    /// <param name="processor">The processor instance.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="processor"/> is null.</exception>
    public BakeApplicationBuilder AddProcessor(IAssetProcessor processor) {
        ArgumentNullException.ThrowIfNull(processor);
        _definition.AddProcessor(processor);
        return this;
    }

    /// <summary>Builds the application. Configuration is validated when a command runs.</summary>
    /// <returns>The configured application.</returns>
    public BakeApplication Build() => new(_definition);

    /// <summary>Overrides the tool version for incremental invalidation tests.</summary>
    /// <param name="version">The version to record and compare.</param>
    /// <returns>This builder.</returns>
    internal BakeApplicationBuilder WithToolVersionOverride(string version) {
        _definition.SetToolVersionOverride(version);
        return this;
    }
}
