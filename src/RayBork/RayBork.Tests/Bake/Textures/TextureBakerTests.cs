using RayBork.Bake;
using RayBork.Internal;

namespace RayBork.Tests.Bake.Textures;

public sealed class TextureBakerTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Rgba8BakeWritesTheCompleteSrgbKtx2Profile() {
        const int width = 4;
        const int height = 4;
        byte[] pixels = TextureTestData.CreatePixels(width, height);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.Rgba,
            TextureCompression.None);

        Assert.Equal(TextureFormat.Rgba8Srgb, baked.Format);
        Assert.Equal(3u, baked.MipLevels);

        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(43u, document.VkFormat);
        Assert.Equal(1u, document.TypeSize);
        Assert.Equal((uint)width, document.Width);
        Assert.Equal((uint)height, document.Height);
        Assert.Equal(0u, document.PixelDepth);
        Assert.Equal(0u, document.LayerCount);
        Assert.Equal(1u, document.FaceCount);
        Assert.Equal(3u, document.LevelCount);
        Assert.Equal(0u, document.Supercompression);
        Assert.Equal(0u, document.KvdLength);
        Assert.Equal(0ul, document.SgdLength);
        Assert.Equal(92u, document.DfdLength);
        Assert.Equal(92, document.Dfd.TotalSize);
        Assert.Equal(1, document.Dfd.ColorModel);
        Assert.Equal(2, document.Dfd.TransferFunction);
        Assert.Equal(1, document.Dfd.BlockWidth);
        Assert.Equal(1, document.Dfd.BlockHeight);
        Assert.Equal(4, document.Dfd.BytesPerBlock);
        Assert.Collection(
            document.Dfd.Samples,
            sample => AssertSample(sample, 0, 8, 0, 255),
            sample => AssertSample(sample, 8, 8, 1, 255),
            sample => AssertSample(sample, 16, 8, 2, 255),
            sample => AssertSample(sample, 24, 8, 15, 255));

        AssertLevels(document, baked.Format, width, height);
        Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
            baked.Payload.Span,
            baked.Format,
            width,
            height,
            baked.MipLevels);
        Assert.Equal(TextureFormat.Rgba8Srgb, parsed.Format);
        Assert.True(parsed.IsSrgb);
        Assert.Equal((uint)width, parsed.Width);
        Assert.Equal((uint)height, parsed.Height);
        Assert.Equal(3, parsed.MipLevels);
        for (int level = 0; level < parsed.MipLevels; level++) {
            Assert.Equal(document.Levels[level].ByteLength, parsed.Levels[level].ByteLength);
            Assert.Equal(document.Levels[level].ByteOffset, parsed.Levels[level].ByteOffset);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Bc7BakeWritesBlockGeometryAndTheBc7Dfd() {
        const int width = 5;
        const int height = 3;
        byte[] pixels = TextureTestData.CreatePixels(width, height);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.Rgba,
            TextureCompression.Auto);

        Assert.Equal(TextureFormat.Bc7Srgb, baked.Format);
        Assert.Equal(3u, baked.MipLevels);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(146u, document.VkFormat);
        Assert.Equal(1u, document.TypeSize);
        Assert.Equal(44u, document.DfdLength);
        Assert.Equal(134, document.Dfd.ColorModel);
        Assert.Equal(2, document.Dfd.TransferFunction);
        Assert.Equal(4, document.Dfd.BlockWidth);
        Assert.Equal(4, document.Dfd.BlockHeight);
        Assert.Equal(16, document.Dfd.BytesPerBlock);
        Ktx2TestSample sample = Assert.Single(document.Dfd.Samples);
        AssertSample(sample, 0, 128, 0, uint.MaxValue);
        AssertLevels(document, baked.Format, width, height);

        Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
            baked.Payload.Span,
            baked.Format,
            width,
            height,
            baked.MipLevels);
        Assert.Equal(TextureFormat.Bc7Srgb, parsed.Format);
        Assert.True(parsed.IsSrgb);
        Assert.Equal(3, parsed.MipLevels);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Bc5BakeWritesTwoLinearChannels() {
        const int width = 4;
        const int height = 4;
        byte[] pixels = TextureTestData.CreatePixels(width, height);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.Rg,
            TextureCompression.Auto);

        Assert.Equal(TextureFormat.Bc5Unorm, baked.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(141u, document.VkFormat);
        Assert.Equal(60u, document.DfdLength);
        Assert.Equal(132, document.Dfd.ColorModel);
        Assert.Equal(1, document.Dfd.TransferFunction);
        Assert.Equal(4, document.Dfd.BlockWidth);
        Assert.Equal(4, document.Dfd.BlockHeight);
        Assert.Equal(16, document.Dfd.BytesPerBlock);
        Assert.Collection(
            document.Dfd.Samples,
            sample => AssertSample(sample, 0, 64, 0, uint.MaxValue),
            sample => AssertSample(sample, 64, 64, 1, uint.MaxValue));
        AssertLevels(document, baked.Format, width, height);

        Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
            baked.Payload.Span,
            baked.Format,
            width,
            height,
            baked.MipLevels);
        Assert.Equal(TextureFormat.Bc5Unorm, parsed.Format);
        Assert.False(parsed.IsSrgb);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void R8BakeWritesOneLinearBytePerPixel() {
        const int width = 3;
        const int height = 2;
        byte[] pixels = TextureTestData.CreatePixels(width, height);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.R,
            TextureCompression.None);

        Assert.Equal(TextureFormat.R8Unorm, baked.Format);
        Assert.Equal(2u, baked.MipLevels);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(9u, document.VkFormat);
        Assert.Equal(44u, document.DfdLength);
        Assert.Equal(1, document.Dfd.ColorModel);
        Assert.Equal(1, document.Dfd.TransferFunction);
        Assert.Equal(1, document.Dfd.BlockWidth);
        Assert.Equal(1, document.Dfd.BlockHeight);
        Assert.Equal(1, document.Dfd.BytesPerBlock);
        Ktx2TestSample sample = Assert.Single(document.Dfd.Samples);
        AssertSample(sample, 0, 8, 0, 255);
        AssertLevels(document, baked.Format, width, height);

        Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
            baked.Payload.Span,
            baked.Format,
            width,
            height,
            baked.MipLevels);
        Assert.Equal(TextureFormat.R8Unorm, parsed.Format);
        Assert.False(parsed.IsSrgb);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RgbForcesOpaqueAlphaAndStoresLinearRgba8() {
        const int width = 2;
        const int height = 2;
        byte[] pixels = TextureTestData.CreatePixels(width, height);
        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.Rgb,
            TextureCompression.None);

        Assert.Equal(TextureFormat.Rgba8Unorm, baked.Format);
        Assert.Equal(2u, baked.MipLevels);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(37u, document.VkFormat);
        Assert.Equal(1, document.Dfd.TransferFunction);

        ReadOnlySpan<byte> level = baked.Payload.Span[
            (int)document.Levels[0].ByteOffset..(int)(document.Levels[0].ByteOffset + 16)];
        for (int pixel = 0; pixel < width * height; pixel++) {
            Assert.Equal((byte)255, level[(pixel * 4) + 3]);
            Assert.Equal(pixels[pixel * 4], level[pixel * 4]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ExplicitLinearTransferStoresLinearRgba8() {
        byte[] pixels = TextureTestData.CreatePixels(2, 2);

        BakedTexture baked = TextureBaker.Bake(
            pixels,
            2,
            2,
            TextureChannelType.Rgba,
            TextureCompression.Auto,
            isSrgb: false);

        Assert.Equal(TextureFormat.Bc7Unorm, baked.Format);
        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        Assert.Equal(145u, document.VkFormat);
        Assert.Equal(1, document.Dfd.TransferFunction);
        Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
            baked.Payload.Span,
            baked.Format,
            2,
            2,
            baked.MipLevels);
        Assert.False(parsed.IsSrgb);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MipChainCoversNonSquareAndSinglePixelTextures() {
        (int Width, int Height, int Levels)[] cases = [
            (1, 1, 1),
            (5, 3, 3),
            (1, 7, 3),
            (8, 2, 4),
            (16, 16, 5),
        ];

        foreach ((int width, int height, int expectedLevels) in cases) {
            byte[] pixels = TextureTestData.CreatePixels(width, height);
            BakedTexture baked = TextureBaker.Bake(
                pixels,
                width,
                height,
                TextureChannelType.Rgba,
                TextureCompression.None);

            Assert.Equal((uint)expectedLevels, baked.MipLevels);
            Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
            Assert.Equal((uint)expectedLevels, document.LevelCount);
            AssertLevels(document, baked.Format, width, height);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LevelDataIsSmallestFirstAndPaddedToTheFormatAlignment() {
        const int width = 5;
        const int height = 3;
        byte[] pixels = TextureTestData.CreatePixels(width, height);
        BakedTexture baked = TextureBaker.Bake(
            pixels,
            width,
            height,
            TextureChannelType.Rgba,
            TextureCompression.Auto);

        Ktx2TestDocument document = Ktx2TestDocument.Parse(baked.Payload.Span);
        int alignment = 16;
        for (int level = 1; level < document.Levels.Count; level++) {
            Assert.True(
                document.Levels[level - 1].ByteOffset > document.Levels[level].ByteOffset,
                "The level index must be base-level first while the level data is smallest first.");
        }

        foreach (Ktx2TestLevel level in document.Levels) {
            Assert.Equal(0ul, level.ByteOffset % (ulong)alignment);
            Assert.Equal(level.ByteLength, level.UncompressedByteLength);
        }

        Assert.Equal(
            checked((ulong)baked.Payload.Length),
            document.Levels[0].ByteOffset + document.Levels[0].ByteLength);
        Assert.True(document.Levels[^1].ByteOffset >= document.DfdOffset + document.DfdLength);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryFormatRoundTripsThroughTheRuntimeReader() {
        (TextureChannelType Channel, TextureCompression Compression, TextureFormat Format)[] cases = [
            (TextureChannelType.Rgba, TextureCompression.None, TextureFormat.Rgba8Srgb),
            (TextureChannelType.Rgba, TextureCompression.Auto, TextureFormat.Bc7Srgb),
            (TextureChannelType.Rgb, TextureCompression.None, TextureFormat.Rgba8Unorm),
            (TextureChannelType.Rgb, TextureCompression.Auto, TextureFormat.Bc7Unorm),
            (TextureChannelType.Rg, TextureCompression.None, TextureFormat.Bc5Unorm),
            (TextureChannelType.Rg, TextureCompression.Auto, TextureFormat.Bc5Unorm),
            (TextureChannelType.R, TextureCompression.None, TextureFormat.R8Unorm),
            (TextureChannelType.R, TextureCompression.Auto, TextureFormat.R8Unorm),
        ];

        const int width = 6;
        const int height = 4;
        byte[] pixels = TextureTestData.CreatePixels(width, height);
        foreach ((TextureChannelType channel, TextureCompression compression, TextureFormat expected) in cases) {
            BakedTexture baked = TextureBaker.Bake(pixels, width, height, channel, compression);
            Assert.Equal(expected, baked.Format);

            Ktx2Texture parsed = Ktx2TestDocument.ReadWithEngine(
                baked.Payload.Span,
                baked.Format,
                width,
                height,
                baked.MipLevels);
            Assert.Equal(expected, parsed.Format);
            Assert.Equal((uint)width, parsed.Width);
            Assert.Equal((uint)height, parsed.Height);
            Assert.Equal(baked.MipLevels, (uint)parsed.MipLevels);
            for (int level = 0; level < parsed.MipLevels; level++) {
                int expectedLength = TextureTestData.LevelByteLength(
                    expected,
                    TextureTestData.LevelWidth(width, level),
                    TextureTestData.LevelHeight(height, level));
                Assert.Equal((ulong)expectedLength, parsed.Levels[level].ByteLength);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BakeIsDeterministic() {
        byte[] pixels = TextureTestData.CreatePixels(7, 5);

        BakedTexture first = TextureBaker.Bake(
            pixels,
            7,
            5,
            TextureChannelType.Rgba,
            TextureCompression.Auto);
        BakedTexture second = TextureBaker.Bake(
            pixels,
            7,
            5,
            TextureChannelType.Rgba,
            TextureCompression.Auto);

        Assert.Equal(first.Format, second.Format);
        Assert.Equal(first.MipLevels, second.MipLevels);
        Assert.True(first.Payload.Span.SequenceEqual(second.Payload.Span));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidArgumentsAreRejectedBeforeAnyOutput() {
        byte[] pixels = TextureTestData.CreatePixels(2, 2);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 0, 2, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 0, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 16385, 2, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 16385, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentException>(
            () => TextureBaker.Bake(pixels.AsSpan(0, 15), 2, 2, TextureChannelType.Rgba, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 2, (TextureChannelType)99, TextureCompression.None));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TextureBaker.Bake(pixels, 2, 2, TextureChannelType.Rgba, (TextureCompression)99));
    }

    private static void AssertLevels(
        Ktx2TestDocument document,
        TextureFormat format,
        int width,
        int height) {
        Assert.Equal((uint)TextureTestData.MipLevelCount(width, height), document.LevelCount);
        for (int level = 0; level < document.Levels.Count; level++) {
            int levelWidth = TextureTestData.LevelWidth(width, level);
            int levelHeight = TextureTestData.LevelHeight(height, level);
            int expectedLength = TextureTestData.LevelByteLength(format, levelWidth, levelHeight);
            Assert.Equal((ulong)expectedLength, document.Levels[level].ByteLength);
            Assert.Equal((ulong)expectedLength, document.Levels[level].UncompressedByteLength);
        }
    }

    private static void AssertSample(
        Ktx2TestSample sample,
        ushort bitOffset,
        int bitLength,
        byte channelType,
        uint upper) {
        Assert.Equal(bitOffset, sample.BitOffset);
        Assert.Equal(bitLength, sample.BitLength);
        Assert.Equal(channelType, sample.ChannelType);
        Assert.Equal(0u, sample.Lower);
        Assert.Equal(upper, sample.Upper);
    }
}
