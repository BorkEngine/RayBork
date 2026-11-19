using PolyType;
using RayBork.Bake.Toml;

namespace RayBork.Bake.Processors;

/// <summary>The shader source description: one <c>[shader]</c> TOML table.</summary>
/// <remarks>
/// The TOML file is both source and sidecar; <see cref="Vertex"/> and <see cref="Fragment"/> are
/// required paths relative to it. Unknown keys are <c>RB4002</c>; metadata errors are <c>RB4008</c>.
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderSidecar
{
    /// <summary>Gets or sets the vertex GLSL path relative to the TOML file.</summary>
    public required string Vertex { get; set; }

    /// <summary>Gets or sets the fragment GLSL path relative to the TOML file.</summary>
    public required string Fragment { get; set; }

    /// <summary>Gets or sets the vertex entry point; <c>main_vertex</c> only.</summary>
    public string? VertexEntry { get; set; }

    /// <summary>Gets or sets the fragment entry point; <c>main_fragment</c> only.</summary>
    public string? FragmentEntry { get; set; }

    /// <summary>Gets or sets the reflected uniforms, or <see langword="null"/> for none.</summary>
    public ShaderUniformSidecar[]? Uniforms { get; set; }

    /// <summary>Gets or sets the texture binding names in slot order, if any.</summary>
    public ShaderTextureSidecar[]? Textures { get; set; }

    /// <summary>Gets or sets the render state overrides; <see langword="null"/> is opaque.</summary>
    public ShaderRenderStateSidecar? RenderState { get; set; }
}

/// <summary>One reflected uniform declaration.</summary>
/// <remarks>A bad <c>type</c> is <c>RB4008</c>; an absent default is zero.</remarks>
[GenerateShape]
internal sealed partial class ShaderUniformSidecar
{
    /// <summary>Gets or sets the GLSL uniform name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the declared type token.</summary>
    public required string Type { get; set; }

    /// <summary>Gets or sets the default value, or <see langword="null"/> for zero.</summary>
    public TomlNumberOrArray? Default { get; set; }
}

/// <summary>One texture binding declaration; at most 16 are supported.</summary>
[GenerateShape]
internal sealed partial class ShaderTextureSidecar
{
    /// <summary>Gets or sets the GLSL texture binding name.</summary>
    public required string Name { get; set; }
}

/// <summary>The render state overrides; an absent member is opaque.</summary>
[GenerateShape]
internal sealed partial class ShaderRenderStateSidecar
{
    /// <summary>Gets or sets the blend token: <c>opaque</c>, <c>alpha</c>, <c>additive</c>, or
    /// <c>multiply</c>.</summary>
    public string? Blend { get; set; }

    /// <summary>Gets or sets the cull token: <c>none</c>, <c>front</c>, or <c>back</c>.</summary>
    public string? Cull { get; set; }

    /// <summary>Gets or sets the depth-test token: <c>disabled</c>, <c>greater</c>,
    /// <c>greater_equal</c>, <c>less</c>, <c>less_or_equal</c>, or <c>always</c>.</summary>
    public string? DepthTest { get; set; }

    /// <summary>Gets or sets whether passing fragments write depth.</summary>
    public bool? DepthWrite { get; set; }
}
