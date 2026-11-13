using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>
/// The immutable registry of asset type registrations captured when the app was built.
/// </summary>
/// <remarks>
/// The registry is built once in the catalog constructor and never changes afterwards, so every
/// member is thread-safe and may be called without a lock. Registrations are keyed by the registered
/// CLR type, and a type may appear only once.
/// </remarks>
internal sealed class AssetTypeRegistry {
    private readonly Dictionary<Type, AssetTypeRegistration> _registrations;

    /// <summary>
    /// Initializes a registry from the configured registrations.
    /// </summary>
    /// <param name="registrations">The registrations in registration order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">A registered asset type appears more than once.</exception>
    internal AssetTypeRegistry(IReadOnlyList<AssetTypeRegistration> registrations) {
        ArgumentNullException.ThrowIfNull(registrations);

        _registrations = new Dictionary<Type, AssetTypeRegistration>(registrations.Count);
        foreach (AssetTypeRegistration registration in registrations) {
            if (!_registrations.TryAdd(registration.AssetType, registration)) {
                throw new InvalidOperationException(
                    $"The asset type '{registration.AssetType.FullName}' is registered more than once.");
            }
        }
    }

    /// <summary>
    /// Gets the number of registered asset types.
    /// </summary>
    internal int Count => _registrations.Count;

    /// <summary>
    /// Tests whether an asset type is registered.
    /// </summary>
    /// <param name="assetType">The CLR asset type to test.</param>
    /// <returns><see langword="true"/> when the type is registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetType"/> is <c>null</c>.</exception>
    internal bool Contains(Type assetType) {
        ArgumentNullException.ThrowIfNull(assetType);
        return _registrations.ContainsKey(assetType);
    }

    /// <summary>
    /// Tries to get the registration for an asset type.
    /// </summary>
    /// <param name="assetType">The CLR asset type to look up.</param>
    /// <param name="registration">The matching registration, or <see langword="null"/> when absent.</param>
    /// <returns><see langword="true"/> when the type is registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetType"/> is <c>null</c>.</exception>
    internal bool TryGet(
        Type assetType,
        [NotNullWhen(true)] out AssetTypeRegistration? registration) {
        ArgumentNullException.ThrowIfNull(assetType);
        return _registrations.TryGetValue(assetType, out registration);
    }

    /// <summary>
    /// Gets the registration for an asset type.
    /// </summary>
    /// <param name="assetType">The CLR asset type to look up.</param>
    /// <returns>The matching registration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assetType"/> is <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">
    /// The type is not registered for loading.
    /// </exception>
    internal AssetTypeRegistration Get(Type assetType) {
        if (TryGet(assetType, out AssetTypeRegistration? registration)) {
            return registration;
        }

        throw new NotSupportedException(
            $"The asset type '{assetType.FullName}' is not registered for loading. Built-in engine "
                + "assets are registered by the engine; register a data type with WithAssetType<T>() "
                + "or a custom loader with WithAssetLoader<T>() before Build.");
    }
}
