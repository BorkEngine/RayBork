namespace RayBork;

/// <summary>
/// The immutable depth, culling, and blending state baked for a shader.
/// </summary>
/// <remarks>
/// The geometry pipelines consume this value as their immutable depth, culling, and blending state.
/// <see cref="Opaque"/> is the default.
/// </remarks>
public readonly record struct RenderState {
    /// <summary>Gets the blend preset.</summary>
    public BlendMode Blend { get; init; }

    /// <summary>Gets the face-culling mode.</summary>
    public CullMode Cull { get; init; }

    /// <summary>Gets the depth comparison.</summary>
    public DepthTest DepthTest { get; init; }

    /// <summary>Gets whether passing fragments write depth.</summary>
    public bool DepthWrite { get; init; }

    /// <summary>
    /// Gets the opaque preset: opaque blending, back-face culling, reverse-Z greater-or-equal depth
    /// testing, and depth writes.
    /// </summary>
    public static RenderState Opaque => new() {
        Blend = BlendMode.Opaque,
        Cull = CullMode.Back,
        DepthTest = DepthTest.GreaterOrEqual,
        DepthWrite = true,
    };

    /// <summary>
    /// Gets the alpha-blend preset: source-over blending, back-face culling, reverse-Z
    /// greater-or-equal depth testing, and no depth writes.
    /// </summary>
    public static RenderState AlphaBlend => new() {
        Blend = BlendMode.Alpha,
        Cull = CullMode.Back,
        DepthTest = DepthTest.GreaterOrEqual,
        DepthWrite = false,
    };

    /// <summary>
    /// Gets the additive preset: additive blending, back-face culling, reverse-Z greater-or-equal
    /// depth testing, and no depth writes.
    /// </summary>
    public static RenderState Additive => new() {
        Blend = BlendMode.Additive,
        Cull = CullMode.Back,
        DepthTest = DepthTest.GreaterOrEqual,
        DepthWrite = false,
    };

    /// <summary>
    /// Gets the multiply preset: multiplicative blending, back-face culling, reverse-Z
    /// greater-or-equal depth testing, and no depth writes.
    /// </summary>
    public static RenderState Multiply => new() {
        Blend = BlendMode.Multiply,
        Cull = CullMode.Back,
        DepthTest = DepthTest.GreaterOrEqual,
        DepthWrite = false,
    };
}
