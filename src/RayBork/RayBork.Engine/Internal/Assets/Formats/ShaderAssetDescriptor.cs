using PolyType;

namespace RayBork.Internal;

/// <summary>The packed shader descriptor; its stage modules are sibling raw entries.</summary>
/// <remarks>
/// The descriptor records the compiled modules, entry points, reflected metadata, default render state,
/// and shader ABI version.
/// </remarks>
[GenerateShape]
internal sealed partial class ShaderAssetDescriptor
{
    /// <summary>Gets the shader ABI version the bake wrote the descriptor with.</summary>
    /// <remarks>
    /// The runtime accepts only <see cref="ShaderAbiNames.ABI_VERSION"/>, rejecting older descriptors.
    /// </remarks>
    public int AbiVersion { get; init; }

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
