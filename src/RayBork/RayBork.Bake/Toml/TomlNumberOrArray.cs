using PolyType;

namespace RayBork.Bake.Toml;

/// <summary>A TOML leaf that accepts one number or an array of numbers.</summary>
/// <remarks>
/// A scalar becomes a one-component value; consumers validate component count and finiteness.
/// </remarks>
[GenerateShape]
internal sealed partial class TomlNumberOrArray
{
    /// <summary>Initializes a number list.</summary>
    internal TomlNumberOrArray(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    /// <summary>Gets the component values in source order.</summary>
    internal IReadOnlyList<double> Values { get; }
}
