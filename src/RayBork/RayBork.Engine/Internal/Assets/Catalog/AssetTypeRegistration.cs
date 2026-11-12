using PolyType;

namespace RayBork.Internal;

/// <summary>
/// One custom data type registered with <see cref="RayBorkAppBuilder.WithAssetType{T}"/>.
/// </summary>
/// <remarks>
/// The registration stores the compiled PolyType shape delegate, so the catalog never needs a
/// reflection resolver and stays Native AOT compatible. The bake must register the same type
/// independently; the runtime never scans assemblies.
/// </remarks>
internal sealed class AssetTypeRegistration {
    private readonly Func<ReadOnlyMemory<byte>, object> _deserialize;

    /// <summary>
    /// Initializes a registration from a compiled shape delegate.
    /// </summary>
    /// <param name="assetType">The registered CLR type.</param>
    /// <param name="deserialize">The delegate that creates a fresh instance from MessagePack bytes.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    internal AssetTypeRegistration(
        Type assetType,
        Func<ReadOnlyMemory<byte>, object> deserialize) {
        ArgumentNullException.ThrowIfNull(assetType);
        ArgumentNullException.ThrowIfNull(deserialize);

        AssetType = assetType;
        _deserialize = deserialize;
    }

    /// <summary>
    /// Gets the registered CLR type.
    /// </summary>
    internal Type AssetType { get; }

    /// <summary>
    /// Creates a fresh instance from one MessagePack payload.
    /// </summary>
    /// <param name="payload">The complete MessagePack payload.</param>
    /// <returns>A new caller-owned instance.</returns>
    internal object Deserialize(ReadOnlyMemory<byte> payload) => _deserialize(payload);

    /// <summary>
    /// Creates the registration for a type that carries a compiled <c>[GenerateShape]</c>.
    /// </summary>
    /// <typeparam name="T">The registered data type.</typeparam>
    /// <returns>The registration.</returns>
    internal static AssetTypeRegistration Create<T>()
        where T : IShapeable<T> => new(
            typeof(T),
            payload => AssetSerialization.Deserialize<T>(payload));
}
