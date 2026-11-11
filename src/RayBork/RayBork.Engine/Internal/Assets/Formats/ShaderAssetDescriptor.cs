using PolyType;

namespace RayBork.Internal;

/// <summary>
/// The packed descriptor of a shader asset. Its stage modules are sibling raw entries.
/// </summary>
/// <remarks>
/// M3 records the compiled SPIR-V modules, entry points, reflected metadata declared by the bake
/// sidecar, and the default render state. The generated prologue and full SPIR-V reflection arrive
/// with the M4 shader ABI.
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderAssetDescriptor {


    /// <summary>Gets the canonical path of the vertex SPIR-V payload entry.</summary>
    public required string VertexPayload { get; init; }

    /// <summary>Gets the canonical path of the fragment SPIR-V payload entry.</summary>
    public required string FragmentPayload { get; init; }

    /// <summary>Gets the vertex entry point name.</summary>
    public required string VertexEntry { get; init; }

    /// <summary>Gets the fragment entry point name.</summary>
    public required string FragmentEntry { get; init; }

    /// <summary>Gets the stages present in the program.</summary>
    public required ShaderStageMask Stages { get; init; }

    /// <summary>Gets the default render state.</summary>
    public required RenderState DefaultRenderState { get; init; }

    /// <summary>Gets the reflected uniform declarations.</summary>
    public required ShaderUniformAsset[] Uniforms { get; init; }

    /// <summary>Gets the texture binding names in slot order.</summary>
    public required string[] Textures { get; init; }
}
