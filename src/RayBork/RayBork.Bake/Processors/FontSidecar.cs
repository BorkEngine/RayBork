using PolyType;

namespace RayBork.Bake.Processors;

/// <summary>The optional <c>[font]</c> sidecar of the built-in font processor.</summary>
/// <remarks>
/// The optional keys <c>msdf_pixel_size</c>, <c>msdf_range</c>, and <c>msdf_padding</c> default to
/// <c>32</c>, <c>4</c>, and <c>2</c>; the whole face is packed, so a <c>ranges</c> key is invalid.
/// </remarks>
[GenerateShape]
internal sealed partial class FontSidecar
{
    /// <summary>Gets or sets the positive MSDF pixel size; the default is <c>32</c>.</summary>
    public int? MsdfPixelSize { get; set; }

    /// <summary>Gets or sets the positive MSDF distance range; the default is <c>4</c>.</summary>
    public int? MsdfRange { get; set; }

    /// <summary>Gets or sets the non-negative MSDF padding; the default is <c>2</c>.</summary>
    public int? MsdfPadding { get; set; }
}
