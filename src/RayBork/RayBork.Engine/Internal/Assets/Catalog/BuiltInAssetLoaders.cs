namespace RayBork.Internal;

/// <summary>
/// The engine asset formats every catalog has built in.
/// </summary>
/// <remarks>
/// The catalog combines these registrations with the user registrations when it builds its registry.
/// Built-ins come first, so a user registration for one of these types fails the registry's
/// duplicate-registration check in the catalog constructor.
/// </remarks>
internal static class BuiltInAssetLoaders {
    /// <summary>
    /// Creates the built-in engine asset registrations in registration order.
    /// </summary>
    /// <returns>The built-in registrations.</returns>
    internal static IReadOnlyList<AssetTypeRegistration> CreateDefault() => [
        new EngineAssetTypeRegistration<Mesh>(MeshAssetFormat.Instance),
        new EngineAssetTypeRegistration<Texture>(TextureAssetFormat.Instance),
        new EngineAssetTypeRegistration<ShaderProgram>(ShaderProgramAssetFormat.Instance),
    ];
}
