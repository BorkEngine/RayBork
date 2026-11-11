using System.ComponentModel;
using PolyType;

namespace HelloInterop;

/// <summary>
/// The custom TOML data asset baked for the M3 sample and loaded with <c>Load&lt;HelloSettings&gt;</c>.
/// </summary>
/// <remarks>
/// The type is shared by the sample host and the assets executable: the host registers it with
/// <c>WithAssetType&lt;HelloSettings&gt;()</c> and the bake registers it with
/// <c>RegisterAsset&lt;HelloSettings&gt;()</c>. The compiled <see cref="GenerateShapeAttribute"/> shape
/// is what keeps the runtime load free of reflection and Native AOT compatible.
/// </remarks>
[GenerateShape]
public sealed partial class HelloSettings {
    /// <summary>Gets the display name authored in <c>Assets/data/hello.toml</c>.</summary>
    [DefaultValue("")]
    public string Name { get; init; } = "";

    /// <summary>Gets the sample difficulty authored in <c>Assets/data/hello.toml</c>.</summary>
    public int Difficulty { get; init; }
}
