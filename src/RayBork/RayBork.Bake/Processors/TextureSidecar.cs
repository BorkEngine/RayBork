using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>
/// The optional settings sidecar of the built-in texture processor.
/// </summary>
/// <remarks>
/// <para>
/// The property names are the canonical snake_case keys <c>format</c>, <c>srgb</c>, <c>filter</c>,
/// <c>wrap</c>, and <c>anisotropy</c>. Every member is optional so a sidecar can override one setting
/// and inherit the rest from the builder defaults. A missing sidecar is the all-default value and is
/// never written to disk.
/// </para>
/// <para>
/// <c>format</c>, <c>filter</c>, and <c>wrap</c> are bound as strings rather than enums so a
/// syntactically valid but unknown token is a semantic <c>RB4007</c> diagnostic instead of a TOML
/// binding error; a wrong type is still an <c>RB4002</c> binding error. The processor validates each
/// token.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class TextureSidecar {
    /// <summary>
    /// Gets or sets the storage format token: <c>rgba8</c> (default), <c>bc7</c>, <c>bc5</c>, or
    /// <c>r8</c>.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>
    /// Gets or sets whether the stored samples are sRGB-encoded. The default is <see langword="true"/>
    /// for <c>rgba8</c> and <c>bc7</c> and <see langword="false"/> for <c>bc5</c> and <c>r8</c>;
    /// <see langword="true"/> is rejected for the two linear data formats.
    /// </summary>
    public bool? Srgb { get; set; }

    /// <summary>
    /// Gets or sets the sampler filter token: <c>nearest</c>, <c>linear</c>, or <c>anisotropic</c>.
    /// <see langword="null"/> uses the builder default.
    /// </summary>
    public string? Filter { get; set; }

    /// <summary>
    /// Gets or sets the sampler address mode token: <c>repeat</c>, <c>clamp</c>, or <c>mirror</c>.
    /// <see langword="null"/> uses the builder default.
    /// </summary>
    public string? Wrap { get; set; }

    /// <summary>
    /// Gets or sets the requested maximum anisotropy. It must be positive and finite; the default is
    /// <c>1</c>. The runtime clamps it to the device limit.
    /// </summary>
    public float? Anisotropy { get; set; }
}
