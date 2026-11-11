using PolyType;
using RayBork.Bake.Toml;

namespace RayBork.Bake.Processors;

/// <summary>
/// The shader source description: one <c>[shader]</c> table that names the two GLSL stages, the
/// optional entry-point restatements, the reflected uniforms and texture bindings, and the default
/// render state.
/// </summary>
/// <remarks>
/// <para>
/// The TOML file is both the processor source and its own sidecar, so its table is named after the
/// processor. <see cref="Vertex"/> and <see cref="Fragment"/> are required and are paths relative to
/// the TOML file; the processor requires them to resolve inside the source's directory subtree in
/// M3. The entry points default to <c>main_vertex</c> and <c>main_fragment</c> and may only restate
/// those exact values.
/// </para>
/// <para>
/// The property names are the canonical snake_case keys <c>vertex</c>, <c>fragment</c>,
/// <c>vertex_entry</c>, <c>fragment_entry</c>, <c>uniforms</c>, <c>textures</c>, and
/// <c>render_state</c>. Unknown keys and wrong types are <c>RB4002</c> binding errors; semantic
/// metadata errors are <c>RB4008</c>.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderSidecar {
    /// <summary>Gets or sets the vertex GLSL path relative to the TOML file.</summary>
    public required string Vertex { get; set; }

    /// <summary>Gets or sets the fragment GLSL path relative to the TOML file.</summary>
    public required string Fragment { get; set; }

    /// <summary>
    /// Gets or sets the vertex entry point. The only accepted value in M3 is <c>main_vertex</c>;
    /// <see langword="null"/> uses that default.
    /// </summary>
    public string? VertexEntry { get; set; }

    /// <summary>
    /// Gets or sets the fragment entry point. The only accepted value in M3 is <c>main_fragment</c>;
    /// <see langword="null"/> uses that default.
    /// </summary>
    public string? FragmentEntry { get; set; }

    /// <summary>Gets or sets the reflected uniform declarations, or <see langword="null"/> for none.</summary>
    public ShaderUniformSidecar[]? Uniforms { get; set; }

    /// <summary>Gets or sets the texture binding names in slot order, or <see langword="null"/> for none.</summary>
    public ShaderTextureSidecar[]? Textures { get; set; }

    /// <summary>Gets or sets the default render state, or <see langword="null"/> for opaque.</summary>
    public ShaderRenderStateSidecar? RenderState { get; set; }
}

/// <summary>One reflected uniform declaration in a shader source.</summary>
/// <remarks>
/// <c>type</c> is a string so an unsupported GLSL type name is a semantic <c>RB4008</c> diagnostic.
/// <c>default</c> is a scalar number for scalar types or an array of numbers whose length matches the
/// vector or matrix component count; an absent default means a zero value.
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderUniformSidecar {
    /// <summary>Gets or sets the GLSL uniform name.</summary>
    public required string Name { get; set; }

    /// <summary>Gets or sets the declared type token.</summary>
    public required string Type { get; set; }

    /// <summary>Gets or sets the default value, or <see langword="null"/> for a zero value.</summary>
    public TomlNumberOrArray? Default { get; set; }
}

/// <summary>One texture binding declaration in a shader source.</summary>
[GenerateShape]
internal sealed partial class ShaderTextureSidecar {
    /// <summary>Gets or sets the GLSL texture binding name.</summary>
    public required string Name { get; set; }
}

/// <summary>
/// The default render state of a shader source. Every member is optional; an absent member inherits
/// the <see cref="RenderState.Opaque"/> value.
/// </summary>
[GenerateShape]
internal sealed partial class ShaderRenderStateSidecar {
    /// <summary>Gets or sets the blend token: <c>opaque</c>, <c>alpha</c>, <c>additive</c>, or <c>multiply</c>.</summary>
    public string? Blend { get; set; }

    /// <summary>Gets or sets the cull token: <c>none</c>, <c>front</c>, or <c>back</c>.</summary>
    public string? Cull { get; set; }

    /// <summary>
    /// Gets or sets the depth-test token: <c>disabled</c>, <c>greater</c>, <c>greater_equal</c>,
    /// <c>less</c>, <c>less_or_equal</c>, or <c>always</c>.
    /// </summary>
    public string? DepthTest { get; set; }

    /// <summary>Gets or sets whether passing fragments write depth.</summary>
    public bool? DepthWrite { get; set; }
}
