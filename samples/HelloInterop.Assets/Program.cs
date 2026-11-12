using HelloInterop;
using PolyType;
using RayBork.Bake;

return await new BakeApplicationBuilder()
    .WithProjectRoot(".")
    .WithPack("hello.pck", "Assets")
    .UseDefaultAssets()
    .RegisterAsset<HelloSettings>()
    .UseDefaultProcessors()
    .AddProcessor(new CircleProcessor())
    .Build()
    .RunAsync(args);

/// <summary>
/// The sample's custom processor: turns the <c>.circle</c> marker into a generated RGBA circle
/// texture through <see cref="TextureBaker.AddTo"/>.
/// </summary>
/// <remarks>
/// The marker file has no pixels of its own. The processor generates a deterministic straight-alpha
/// RGBA8 image, asks <see cref="TextureBaker"/> for the complete KTX2 payload, and emits the texture
/// descriptor plus the <c>-ktx2</c> payload. It never writes MessagePack, KTX2 headers, or pack bytes
/// itself, so the sample stays at the documented <see cref="IAssetProcessor"/> surface.
/// </remarks>
internal sealed class CircleProcessor : IAssetProcessor {
    private const int TEXTURE_SIZE = 64;
    private const float CIRCLE_RADIUS_FRACTION = 0.8f;
    private const float EDGE_FEATHER_PIXELS = 1f;
    private const byte OPAQUE = 255;

    private static readonly string[] _extensions = ["circle"];

    /// <inheritdoc/>
    public string Name => "circle";

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _extensions;

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.None;

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => null;

    /// <inheritdoc/>
    public void Process(AssetProcessorContext context) {
        ArgumentNullException.ThrowIfNull(context);

        TextureBaker.AddTo(context, CreateCirclePixels(), TEXTURE_SIZE, TEXTURE_SIZE);
    }

    private static byte[] CreateCirclePixels() {
        var pixels = new byte[TEXTURE_SIZE * TEXTURE_SIZE * 4];
        float center = (TEXTURE_SIZE - 1) / 2f;
        float radius = center * CIRCLE_RADIUS_FRACTION;
        for (int y = 0; y < TEXTURE_SIZE; y++) {
            float dy = y - center;
            for (int x = 0; x < TEXTURE_SIZE; x++) {
                float dx = x - center;
                float distance = MathF.Sqrt((dx * dx) + (dy * dy));
                float coverage = Math.Clamp(
                    ((radius - distance) / EDGE_FEATHER_PIXELS) + 0.5f,
                    0f,
                    1f);
                int offset = ((y * TEXTURE_SIZE) + x) * 4;
                pixels[offset] = OPAQUE;
                pixels[offset + 1] = OPAQUE;
                pixels[offset + 2] = OPAQUE;
                pixels[offset + 3] = (byte)MathF.Round(coverage * OPAQUE);
            }
        }

        return pixels;
    }
}
