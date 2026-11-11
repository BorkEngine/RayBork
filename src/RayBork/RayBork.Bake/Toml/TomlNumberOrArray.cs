using PolyType;

namespace RayBork.Bake.Toml;

/// <summary>
/// A TOML leaf that accepts either one number or an array of numbers.
/// </summary>
/// <remarks>
/// The shape binder has no union leaf for "scalar or array", so a shader uniform default uses this
/// explicit tooling leaf. A scalar becomes a one-component value; the consumer validates the
/// component count and finiteness.
/// </remarks>
[GenerateShape]
internal sealed partial class TomlNumberOrArray {
    /// <summary>Initializes a number list.</summary>
    /// <param name="values">The component values in source order.</param>
    internal TomlNumberOrArray(IReadOnlyList<double> values) {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    /// <summary>Gets the component values in source order.</summary>
    internal IReadOnlyList<double> Values { get; }
}
