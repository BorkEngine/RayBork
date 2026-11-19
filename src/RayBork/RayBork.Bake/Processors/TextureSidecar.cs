using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>The optional <c>[texture]</c> sidecar of the built-in texture processor.</summary>
/// <remarks>
/// The optional keys <c>format</c>, <c>srgb</c>, <c>filter</c>, <c>wrap</c>, and <c>anisotropy</c>
/// inherit the builder default when omitted; a token key's unknown value is <c>RB4007</c>.
/// </remarks>
[GenerateShape]
internal sealed partial class TextureSidecar
{
    /// <summary>Gets or sets the storage format token: <c>rgba8</c> (default), <c>bc7</c>, <c>bc5</c>,
    /// or <c>r8</c>.</summary>
    public string? Format { get; set; }

    /// <summary>Gets or sets sRGB storage; <see langword="true"/> is the default for <c>rgba8</c> and
    /// <c>bc7</c> and is rejected for <c>bc5</c> and <c>r8</c>.</summary>
    public bool? Srgb { get; set; }

    /// <summary>Gets or sets the filter token: <c>nearest</c>, <c>linear</c>, or <c>anisotropic</c>;
    /// <see langword="null"/> uses the builder default.</summary>
    public string? Filter { get; set; }

    /// <summary>Gets or sets the wrap token: <c>repeat</c>, <c>clamp</c>, or <c>mirror</c>;
    /// <see langword="null"/> uses the builder default.</summary>
    public string? Wrap { get; set; }

    /// <summary>Gets or sets the maximum anisotropy; it must be positive and finite and defaults to
    /// <c>1</c>.</summary>
    public float? Anisotropy { get; set; }
}
