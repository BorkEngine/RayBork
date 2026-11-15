using System.ComponentModel;
using PolyType;

namespace Pong;

/// <summary>
/// The custom TOML data asset for the sample's design values, loaded from <c>/data/game</c>.
/// </summary>
/// <remarks>
/// The sample host and the assets executable compile this file. The host registers the type with
/// <c>WithAssetType&lt;PongSettings&gt;()</c>, the bake registers it with
/// <c>RegisterAsset&lt;PongSettings&gt;()</c>, and the compiled <see cref="GenerateShapeAttribute"/>
/// shape keeps the runtime load reflection-free and Native AOT compatible. Values are in design
/// pixels on the 1280x720 playfield.
/// </remarks>
[GenerateShape]
public sealed partial class PongSettings {
    /// <summary>Gets the texture path of the paddle quad.</summary>
    [DefaultValue("/textures/paddle")]
    public string PaddleTexture { get; init; } = "/textures/paddle";

    /// <summary>Gets the ball radius in design pixels.</summary>
    [DefaultValue(12f)]
    public float BallRadius { get; init; } = 12f;

    /// <summary>Gets the score a player must reach to win.</summary>
    [DefaultValue(11)]
    public int MaxScore { get; init; } = 11;

    /// <summary>Gets the paddle width in design pixels.</summary>
    [DefaultValue(18f)]
    public float PaddleWidth { get; init; } = 18f;

    /// <summary>Gets the paddle height in design pixels.</summary>
    [DefaultValue(110f)]
    public float PaddleHeight { get; init; } = 110f;

    /// <summary>Gets the paddle travel speed in design pixels per second.</summary>
    [DefaultValue(520f)]
    public float PaddleSpeed { get; init; } = 520f;

    /// <summary>Gets the ball speed in design pixels per second.</summary>
    [DefaultValue(420f)]
    public float BallSpeed { get; init; } = 420f;
}
