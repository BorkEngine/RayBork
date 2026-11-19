using PolyType;
using RayBork.Bake.Baking;

namespace RayBork.Bake;

/// <summary>Configures one build-time assets executable.</summary>
/// <remarks>
/// Configuration is immutable after <see cref="Build"/>; invalid configuration uses exit code 2.
/// The first <see cref="WithPack(string, string)"/> is the main pack; later packs shadow.
/// </remarks>
public sealed class BakeApplicationBuilder
{
    private readonly BakeProjectDefinition _definition = new();

    /// <summary>Sets the base for relative roots, pack paths, and diagnostic display paths.</summary>
    public BakeApplicationBuilder WithProjectRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _definition.SetProjectRoot(path);
        return this;
    }

    /// <summary>Adds one ordered pack; the first added pack is the main pack.</summary>
    /// <param name="packPath">The output-root-relative pack path.</param>
    /// <param name="root">The project-root-relative asset root.</param>
    public BakeApplicationBuilder WithPack(string packPath, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _definition.AddPack(packPath, root);
        return this;
    }

    /// <summary>Registers the built-in descriptor types; idempotent.</summary>
    public BakeApplicationBuilder UseDefaultAssets()
    {
        _definition.UseDefaultAssets();
        return this;
    }

    /// <summary>Registers one data type selectable by <c>[raybork].type</c>.</summary>
    public BakeApplicationBuilder RegisterAsset<T>()
        where T : IShapeable<T>
    {
        _definition.AddAsset<T>();
        return this;
    }

    /// <summary>Registers one data type if necessary and appends one semantic validator.</summary>
    public BakeApplicationBuilder RegisterAsset<T>(IAssetValidator<T> validator)
        where T : IShapeable<T>
    {
        ArgumentNullException.ThrowIfNull(validator);
        _definition.AddAssetValidator(validator);
        return this;
    }

    /// <summary>Registers the built-in processors; idempotent.</summary>
    public BakeApplicationBuilder UseDefaultProcessors()
    {
        _definition.RequestDefaultProcessors();
        return this;
    }

    /// <summary>Sets the sampler defaults used by textures that have no sidecar.</summary>
    public BakeApplicationBuilder WithTextureDefaults(TextureFilter filter, TextureWrap wrap)
    {
        _definition.SetTextureDefaults(filter, wrap);
        return this;
    }

    /// <summary>Registers a custom processor.</summary>
    /// <remarks>Processors run in registration order; a later extension registration wins.</remarks>
    public BakeApplicationBuilder AddProcessor(IAssetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _definition.AddProcessor(processor);
        return this;
    }

    /// <summary>Builds the application; configuration is validated when a command runs.</summary>
    public BakeApplication Build() => new(_definition);

    /// <summary>Overrides the tool version for incremental invalidation tests.</summary>
    internal BakeApplicationBuilder WithToolVersionOverride(string version)
    {
        _definition.SetToolVersionOverride(version);
        return this;
    }
}
