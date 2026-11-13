namespace RayBork;

/// <summary>
/// Loads one registered custom asset type from a packed entry.
/// </summary>
/// <remarks>
/// <para>
/// Register a loader with <see cref="RayBorkAppBuilder.WithAssetLoader{T}"/>. The catalog invokes
/// <see cref="LoadAsync"/> for every <see cref="IAssetCatalog.Load{T}"/> call of the registered type;
/// it never caches the result, so every call returns a fresh caller-owned value. A loader that
/// requests another asset through the <see cref="AssetLoadContext"/> owns what it acquires: the
/// returned value must take ownership of it, and the loader must release what it acquired when the
/// load fails or is canceled.
/// </para>
/// <para>
/// The synchronous part of <see cref="LoadAsync"/> runs on the dedicated <c>RayBork.Asset</c> loader
/// thread; continuations after an <see langword="await"/> may run on the thread pool. A loader must
/// not block (no <c>.Result</c>, <c>.Wait()</c>, or synchronous wait), must not call SDL, Vulkan, or
/// tick-thread APIs, and must be stateless or thread-safe because one instance serves concurrent
/// loads. The catalog wraps a loader exception as <see cref="AssetLoadException"/> unless it is
/// already an <see cref="AssetException"/>, and treats a <see langword="null"/> result as a load
/// failure.
/// </para>
/// </remarks>
/// <typeparam name="T">The custom asset type produced by the loader.</typeparam>
public interface IAssetLoader<T> {
    /// <summary>
    /// Gets the pack entry type that this loader accepts.
    /// </summary>
    /// <remarks>
    /// The catalog rejects an entry with a different type with
    /// <see cref="AssetTypeMismatchException"/> before it invokes the loader.
    /// </remarks>
    AssetEntryType EntryType { get; }

    /// <summary>
    /// Loads one asset from the supplied context.
    /// </summary>
    /// <param name="context">The canonical path, cancellation token, and packed payload for the entry.</param>
    /// <returns>A new caller-owned instance, or a task that faults when the load fails.</returns>
    Task<T> LoadAsync(AssetLoadContext context);
}
