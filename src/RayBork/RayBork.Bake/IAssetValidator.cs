using PolyType;

namespace RayBork.Bake;

/// <summary>
/// A semantic validator for one data type registered with
/// <see cref="BakeApplicationBuilder.RegisterAsset{T}(IAssetValidator{T})"/>.
/// </summary>
/// <remarks>
/// Validators run after a <c>[raybork]</c> data source binds and before processor outputs are
/// composed. One instance is reused for every asset of its type, so a validator must not keep
/// per-asset state.
/// </remarks>
/// <typeparam name="T">The data type, which must carry a compiled shape.</typeparam>
public interface IAssetValidator<T>
    where T : IShapeable<T> {
    /// <summary>Validates one bound asset.</summary>
    /// <param name="assetPath">The asset's canonical path.</param>
    /// <param name="asset">The bound value.</param>
    /// <param name="diagnostics">The diagnostic sink; an error fails the pack.</param>
    void Validate(string assetPath, T asset, IAssetProcessorDiagnostics diagnostics);
}
