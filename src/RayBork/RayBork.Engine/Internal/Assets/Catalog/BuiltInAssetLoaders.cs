namespace RayBork.Internal;

/// <summary>
/// The asset type registrations every catalog has built in: the engine asset formats and the shader
/// material and model loaders.
/// </summary>
/// <remarks>
/// The catalog combines these registrations with the user registrations when it builds its registry.
/// Built-ins come first, so a user registration for one of these types fails the registry's
/// duplicate-registration check in the catalog constructor.
/// </remarks>
internal static class BuiltInAssetLoaders {
    /// <summary>
    /// Creates the built-in asset registrations in registration order.
    /// </summary>
    /// <returns>The built-in registrations.</returns>
    internal static IReadOnlyList<AssetTypeRegistration> CreateDefault() => [
        new EngineAssetTypeRegistration<Mesh>(MeshAssetFormat.Instance),
        new EngineAssetTypeRegistration<Texture>(TextureAssetFormat.Instance),
        new EngineAssetTypeRegistration<ShaderProgram>(ShaderProgramAssetFormat.Instance),
        new LoaderAssetTypeRegistration<Shader>(ShaderMaterialLoader.Instance),
        new LoaderAssetTypeRegistration<Model>(ModelAssetLoader.Instance),
    ];
}
