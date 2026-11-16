using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the render target color readback conversion: the format decode to raw scene-linear
/// colors, the top-left <c>[y, x]</c> layout, and the CPU AgX/sRGB display encode.
/// </summary>
public sealed class RenderTextureReadbackConverterTests {
    private const float TOLERANCE = 1e-4f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void HalfFloatDecodePreservesTopLeftRowsHdrValuesAndAlpha() {
        byte[] pixels = new byte[2 * 2 * 8];
        WriteHalf(pixels, 0, 0.25f);
        WriteHalf(pixels, 2, 0.5f);
        WriteHalf(pixels, 4, 0.75f);
        WriteHalf(pixels, 6, 0.5f);

        WriteHalf(pixels, 8, 2f);
        WriteHalf(pixels, 10, 4f);
        WriteHalf(pixels, 12, 8f);
        WriteHalf(pixels, 14, 0.25f);

        WriteHalf(pixels, 16, 0.1f);
        WriteHalf(pixels, 18, 0.2f);
        WriteHalf(pixels, 20, 0.3f);
        WriteHalf(pixels, 22, 0f);

        WriteHalf(pixels, 24, 16.29f);
        WriteHalf(pixels, 26, 1f);
        WriteHalf(pixels, 28, 0f);
        WriteHalf(pixels, 30, 1f);

        Color[,] colors = RenderTextureReadbackConverter.DecodeColors(
            pixels,
            2,
            2,
            TextureFormat.Rgba16Float);

        Assert.Equal(2, colors.GetLength(0));
        Assert.Equal(2, colors.GetLength(1));
        AssertColor(colors[0, 0], 0.25f, 0.5f, 0.75f, 0.5f);
        AssertColor(colors[0, 1], 2f, 4f, 8f, 0.25f);
        AssertColor(colors[1, 0], 0.1f, 0.2f, 0.3f, 0f);
        AssertColor(colors[1, 1], 16.296875f, 1f, 0f, 1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnormDecodeIsTheRawByteOver255AndAlphaIsLinear() {
        byte[] pixels = [
            0, 128, 255, 64,
            255, 0, 32, 255,
        ];

        Color[,] colors = RenderTextureReadbackConverter.DecodeColors(
            pixels,
            2,
            1,
            TextureFormat.Rgba8Unorm);

        AssertColor(colors[0, 0], 0f, 128f / 255f, 1f, 64f / 255f);
        AssertColor(colors[0, 1], 1f, 0f, 32f / 255f, 1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SrgbDecodeConvertsRgbToLinearAndLeavesAlphaLinear() {
        byte[] pixels = [
            0, 128, 255, 128,
        ];

        Color[,] colors = RenderTextureReadbackConverter.DecodeColors(
            pixels,
            1,
            1,
            TextureFormat.Rgba8Srgb);

        Color color = colors[0, 0];
        Assert.Equal(0f, color.R, TOLERANCE);
        Assert.Equal(AgxToneMap.SrgbDecodeChannel(128), color.G, TOLERANCE);
        Assert.Equal(1f, color.B, TOLERANCE);
        Assert.Equal(128f / 255f, color.A, TOLERANCE);
        Assert.InRange(color.G, 0.2158f, 0.2159f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OpaqueBlackDefaultCoversEveryPixel() {
        Color[,] colors = RenderTextureReadbackConverter.CreateOpaqueBlackColors(3, 2);
        byte[] pixels = RenderTextureReadbackConverter.EncodePixels(
            colors,
            TonemappingSnapshot.Default,
            TonemappingMode.AgX);

        Assert.Equal(2, colors.GetLength(0));
        Assert.Equal(3, colors.GetLength(1));
        Assert.All(colors.Cast<Color>(), color => AssertColor(color, 0f, 0f, 0f, 1f));
        Assert.Equal(3 * 2 * 4, pixels.Length);
        for (int index = 0; index < pixels.Length; index += 4) {
            Assert.Equal(byte.MinValue, pixels[index]);
            Assert.Equal(byte.MinValue, pixels[index + 1]);
            Assert.Equal(byte.MinValue, pixels[index + 2]);
            Assert.Equal(byte.MaxValue, pixels[index + 3]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DecodeRejectsABufferThatIsNotExactlyOneImage() {
        Assert.Throws<ArgumentException>(() => RenderTextureReadbackConverter.DecodeColors(
            new byte[15],
            2,
            2,
            TextureFormat.Rgba8Unorm));
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderTextureReadbackConverter.DecodeColors(
            new byte[12],
            2,
            2,
            TextureFormat.Bc7Srgb));
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderTextureReadbackConverter.DecodeColors(
            [],
            0,
            2,
            TextureFormat.Rgba8Unorm));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EncodeMatchesTheCpuAgxReferenceAndWritesOpaqueAlpha() {
        Color[,] colors = new Color[1, 3];
        colors[0, 0] = new Color(1f, 1f, 1f, 1f);
        colors[0, 1] = new Color(4f, 4f, 4f, 1f);
        colors[0, 2] = new Color(2f, 0.5f, 0.1f, 0.25f);

        byte[] pixels = RenderTextureReadbackConverter.EncodePixels(
            colors,
            TonemappingSnapshot.Default,
            TonemappingMode.AgX);

        Assert.Equal(12, pixels.Length);
        for (int index = 0; index < 3; index++) {
            byte[] expected = ExpectedPixels(
                colors[0, index],
                TonemappingSnapshot.Default,
                TonemappingMode.AgX);
            Assert.Equal(expected[0], pixels[index * 4]);
            Assert.Equal(expected[1], pixels[(index * 4) + 1]);
            Assert.Equal(expected[2], pixels[(index * 4) + 2]);
            Assert.Equal(byte.MaxValue, pixels[(index * 4) + 3]);
        }

        // AgX maps neutral white to 0.5961 display-linear, which is 203 after the sRGB encode.
        Assert.Equal(203, pixels[0]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EncodeAppliesTheSnapshotExposureWhiteAndContrast() {
        TonemappingSnapshot snapshot = new(1f, 4f, 1.5f);
        Color[,] colors = new Color[1, 1];
        colors[0, 0] = new Color(0.25f, 0.5f, 1f, 1f);

        byte[] pixels = RenderTextureReadbackConverter.EncodePixels(
            colors,
            snapshot,
            TonemappingMode.AgX);

        byte[] expected = ExpectedPixels(colors[0, 0], snapshot, TonemappingMode.AgX);
        Assert.Equal(expected, pixels);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EncodeWithNoneModeSkipsTheCurveAndClampsTheExposedColor() {
        Color[,] colors = new Color[1, 3];
        colors[0, 0] = new Color(0.25f, 0.5f, 1f, 1f);
        colors[0, 1] = new Color(2f, -1f, 0.5f, 1f);
        colors[0, 2] = new Color(0.5f, 0.5f, 0.5f, 1f);

        byte[] pixels = RenderTextureReadbackConverter.EncodePixels(
            colors,
            TonemappingSnapshot.Default,
            TonemappingMode.None);

        byte[] expected = ExpectedPixels(colors[0, 0], TonemappingSnapshot.Default, TonemappingMode.None);
        Assert.Equal(expected, pixels[..4]);
        // Clamp happens after the exposure and the sRGB encode, matching the present shader's SDR path.
        Assert.Equal(byte.MaxValue, pixels[4]);
        Assert.Equal(byte.MinValue, pixels[5]);
        Assert.Equal(ExpectedSrgbByte(0.5f), pixels[6]);
        Assert.Equal(byte.MaxValue, pixels[7]);
        Assert.Equal(ExpectedSrgbByte(0.5f), pixels[8]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EncodeWritesRowsInTopLeftRowMajorOrder() {
        Color[,] colors = new Color[2, 2];
        colors[0, 0] = new Color(1f, 0f, 0f, 1f);
        colors[0, 1] = new Color(0f, 1f, 0f, 1f);
        colors[1, 0] = new Color(0f, 0f, 1f, 1f);
        colors[1, 1] = new Color(1f, 1f, 1f, 1f);

        byte[] pixels = RenderTextureReadbackConverter.EncodePixels(
            colors,
            TonemappingSnapshot.Default,
            TonemappingMode.None);

        Assert.Equal(16, pixels.Length);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels[..4]);
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, pixels[4..8]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[8..12]);
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels[12..16]);
    }

    private static byte[] ExpectedPixels(Color color, TonemappingSnapshot snapshot, TonemappingMode mode) {
        Vector3 present = AgxToneMap.Present(
            new Vector3(color.R, color.G, color.B),
            MathF.Pow(2f, snapshot.Exposure),
            mode,
            PresentEncoding.ManualSrgb,
            WindowHdrState.Sdr,
            snapshot.AgxWhite,
            snapshot.AgxContrast);
        return [
            Quantize(present.X),
            Quantize(present.Y),
            Quantize(present.Z),
            byte.MaxValue,
        ];
    }

    private static byte ExpectedSrgbByte(float displayLinear) => Quantize(AgxToneMap.SrgbEncodeChannel(displayLinear));

    private static byte Quantize(float channel) =>
        (byte)Math.Clamp((int)MathF.Round(channel * 255f, MidpointRounding.AwayFromZero), 0, 255);

    private static void WriteHalf(byte[] buffer, int offset, float value) {
        byte[] bytes = BitConverter.GetBytes((Half)value);
        Assert.Equal(2, bytes.Length);
        buffer[offset] = bytes[0];
        buffer[offset + 1] = bytes[1];
    }

    private static void AssertColor(Color color, float r, float g, float b, float a) {
        Assert.Equal(r, color.R, TOLERANCE);
        Assert.Equal(g, color.G, TOLERANCE);
        Assert.Equal(b, color.B, TOLERANCE);
        Assert.Equal(a, color.A, TOLERANCE);
    }
}
