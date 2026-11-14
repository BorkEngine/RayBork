using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>
/// The optional settings sidecar of the built-in font processor.
/// </summary>
/// <remarks>
/// <para>
/// The property names are the canonical snake_case keys <c>msdf_pixel_size</c>, <c>msdf_range</c>, and
/// <c>msdf_padding</c>. Every member is optional, so a missing sidecar uses
/// <c>msdf_pixel_size = 32</c>, <c>msdf_range = 4</c>, and <c>msdf_padding = 2</c>. The bake never
/// writes a sidecar.
/// </para>
/// <para>
/// The whole face is packed; there is no preload range and no <c>ranges</c> key. A <c>ranges</c> key in
/// an existing sidecar is an unknown-key error, so a source that carried one must drop it.
/// </para>
/// </remarks>
[GenerateShape]
internal sealed partial class FontSidecar {
    /// <summary>Gets or sets the MSDF pixel size. It must be positive; the default is <c>32</c>.</summary>
    public int? MsdfPixelSize { get; set; }

    /// <summary>Gets or sets the MSDF distance range. It must be positive; the default is <c>4</c>.</summary>
    public int? MsdfRange { get; set; }

    /// <summary>Gets or sets the MSDF padding. It must be non-negative; the default is <c>2</c>.</summary>
    public int? MsdfPadding { get; set; }
}
