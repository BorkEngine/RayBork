using PolyType;

namespace RayBork.Bake;

/// <summary>A semantic validator for one registered data type.</summary>
/// <remarks>Runs after binding and before composition; must not keep per-asset state.</remarks>
public interface IAssetValidator<T>
    where T : IShapeable<T>
{
    /// <summary>Validates one bound asset.</summary>
    void Validate(string assetPath, T asset, IAssetProcessorDiagnostics diagnostics);
}
