namespace RayBork;

/// <summary>The immutable depth, culling, and blending state of a shader pipeline.</summary>
/// <remarks>
/// Every preset culls back faces and uses reverse-Z greater-or-equal depth testing; only
/// <see cref="Opaque"/> writes depth.
/// </remarks>
public readonly record struct RenderState
{
    /// <summary>Gets the blend preset.</summary>
    public BlendMode Blend { get; init; }

    /// <summary>Gets the face-culling mode.</summary>
    public CullMode Cull { get; init; }

    /// <summary>Gets the depth comparison.</summary>
    public DepthTest DepthTest { get; init; }

    /// <summary>Gets whether passing fragments write depth.</summary>
    public bool DepthWrite { get; init; }

    /// <summary>Opaque blending with depth writes; the default.</summary>
    public static RenderState Opaque =>
        new()
        {
            Blend = BlendMode.Opaque,
            Cull = CullMode.Back,
            DepthTest = DepthTest.GreaterOrEqual,
            DepthWrite = true,
        };

    /// <summary>Source-over alpha blending without depth writes.</summary>
    public static RenderState AlphaBlend =>
        new()
        {
            Blend = BlendMode.Alpha,
            Cull = CullMode.Back,
            DepthTest = DepthTest.GreaterOrEqual,
            DepthWrite = false,
        };

    /// <summary>Additive blending without depth writes.</summary>
    public static RenderState Additive =>
        new()
        {
            Blend = BlendMode.Additive,
            Cull = CullMode.Back,
            DepthTest = DepthTest.GreaterOrEqual,
            DepthWrite = false,
        };

    /// <summary>Multiplicative blending without depth writes.</summary>
    public static RenderState Multiply =>
        new()
        {
            Blend = BlendMode.Multiply,
            Cull = CullMode.Back,
            DepthTest = DepthTest.GreaterOrEqual,
            DepthWrite = false,
        };
}
