using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The default custom data loader: it deserializes one MessagePack payload into a shaped type.
/// </summary>
/// <remarks>
/// The loader holds no state; every call creates a fresh caller-owned instance from the payload it is
/// given. It is thread-safe and may be shared by concurrent loads.
/// </remarks>
/// <typeparam name="T">The registered custom data type.</typeparam>
internal sealed class MessagePackAssetLoader<T> : IAssetLoader<T>
    where T : IShapeable<T> {
    /// <inheritdoc/>
    public AssetEntryType EntryType => AssetEntryType.Data;

    /// <inheritdoc/>
    public async Task<T> LoadAsync(AssetLoadContext context) {
        ArgumentNullException.ThrowIfNull(context);

        byte[] payload = await context.LoadPayloadAsync().ConfigureAwait(false);
        context.CancellationToken.ThrowIfCancellationRequested();
        return AssetSerialization.Deserialize<T>(payload);
    }
}
