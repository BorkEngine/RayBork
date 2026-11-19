using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>The optional <c>[model]</c> sidecar of the built-in model processor.</summary>
/// <remarks>
/// The optional <c>scale</c>, <c>front</c>, <c>shader</c>, and <c>collisions</c> keys default to
/// <c>1.0</c>, <c>+z</c>, <c>shaders/mesh</c>, and enabled; a bad <c>front</c> is <c>RB4009</c>.
/// </remarks>
[GenerateShape]
internal sealed partial class ModelSidecar
{
    /// <summary>Gets or sets the positive uniform scale; the default is <c>1.0</c>.</summary>
    public float? Scale { get; set; }

    /// <summary>Gets or sets the forward axis: <c>+x</c>, <c>-x</c>, <c>+y</c>, <c>-y</c>, <c>+z</c>,
    /// or <c>-z</c>; the default is <c>+z</c>.</summary>
    public string? Front { get; set; }

    /// <summary>Gets or sets the canonical shader path (default <c>shaders/mesh</c>).</summary>
    public string? Shader { get; set; }

    /// <summary>Gets or sets collision baking; <see langword="null"/> means enabled.</summary>
    public bool? Collisions { get; set; }
}
