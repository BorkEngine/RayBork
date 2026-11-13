using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>
/// The optional settings sidecar of the built-in model processor.
/// </summary>
/// <remarks>
/// <para>
/// The property names are the canonical snake_case keys <c>scale</c>, <c>front</c>, <c>shader</c>, and
/// <c>collisions</c>. Every member is optional, so a missing sidecar uses the defaults
/// <c>scale = 1.0</c>, <c>front = "+z"</c>, the built-in shader path <c>shaders/mesh</c>, and
/// <c>collisions = true</c>. The bake never writes a sidecar.
/// </para>
/// <para>
/// <c>front</c> is bound as a string so an unknown axis is a semantic <c>RB4009</c> error rather
/// than a TOML binding error; an unknown key or a wrong type remains an <c>RB4002</c> error.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class ModelSidecar {
    /// <summary>
    /// Gets or sets the positive uniform scale applied to the source geometry. The default is
    /// <c>1.0</c>.
    /// </summary>
    public float? Scale { get; set; }

    /// <summary>
    /// Gets or sets the source's forward axis: <c>+x</c>, <c>-x</c>, <c>+y</c>, <c>-y</c>,
    /// <c>+z</c>, or <c>-z</c>. The default is <c>+z</c>.
    /// </summary>
    public string? Front { get; set; }

    /// <summary>
    /// Gets or sets the optional canonical shader asset path, without a leading slash, stored on
    /// every part. When it is <see langword="null"/> the bake stores <c>shaders/mesh</c>. The bake
    /// does not load the shader.
    /// </summary>
    public string? Shader { get; set; }

    /// <summary>
    /// Gets or sets whether the bake writes a ray-mesh collision payload for each triangle part.
    /// <see langword="null"/> means enabled; <c>false</c> opts the whole source out.
    /// </summary>
    public bool? Collisions { get; set; }
}
