using System.Diagnostics.CodeAnalysis;

namespace RayBork.Internal;

/// <summary>The immutable registry of asset type registrations built with the app.</summary>
/// <remarks>
/// The registry is built once in the catalog constructor and never changes, so every member is
/// thread-safe. Registrations are keyed by CLR type and each type may appear only once.
/// </remarks>
internal sealed class AssetTypeRegistry
{
    private readonly Dictionary<Type, AssetTypeRegistration> _registrations;

    /// <summary>Initializes a registry from the configured registrations.</summary>
    /// <exception cref="InvalidOperationException">A type is registered twice.</exception>
    internal AssetTypeRegistry(IReadOnlyList<AssetTypeRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _registrations = new Dictionary<Type, AssetTypeRegistration>(registrations.Count);
        foreach (AssetTypeRegistration registration in registrations)
        {
            if (!_registrations.TryAdd(registration.AssetType, registration))
            {
                throw new InvalidOperationException(
                    $"The asset type '{registration.AssetType.FullName}' is registered more than once."
                );
            }
        }
    }

    /// <summary>Gets the number of registered asset types.</summary>
    internal int Count => _registrations.Count;

    /// <summary>Tests whether an asset type is registered.</summary>
    internal bool Contains(Type assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return _registrations.ContainsKey(assetType);
    }

    /// <summary>Tries to get the registration for an asset type.</summary>
    internal bool TryGet(
        Type assetType,
        [NotNullWhen(true)] out AssetTypeRegistration? registration
    )
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return _registrations.TryGetValue(assetType, out registration);
    }

    /// <summary>Gets the registration for an asset type.</summary>
    /// <exception cref="NotSupportedException">The type is not registered.</exception>
    internal AssetTypeRegistration Get(Type assetType)
    {
        if (TryGet(assetType, out AssetTypeRegistration? registration))
        {
            return registration;
        }

        throw new NotSupportedException(
            $"The asset type '{assetType.FullName}' is not registered for loading. Built-in engine "
                + "assets are registered by the engine; register a data type with WithAssetType<T>() "
                + "or a custom loader with WithAssetLoader<T>() before Build."
        );
    }
}
