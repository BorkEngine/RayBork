using PolyType;

namespace RayBork.Internal;

/// <summary>
/// One reflected shader uniform with its baked default value.
/// </summary>
/// <remarks>
/// <see cref="Default"/> stores the default in component order; an empty array means a zero value.
/// Comparison uniforms do not use this type.
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderUniformAsset {
    /// <summary>Gets the GLSL uniform name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the reflected value type.</summary>
    public required ShaderUniformType Type { get; init; }

    /// <summary>Gets the baked default components.</summary>
    public required float[] Default { get; init; }
}
