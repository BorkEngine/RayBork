using System.Buffers.Binary;

namespace RenderTarget;

/// <summary>
/// Writes display-referred RGBA8 pixels as a 24-bit uncompressed Windows BMP.
/// </summary>
/// <remarks>
/// BMP stores BGR only, so the readback's opaque alpha is dropped. The header keeps a positive height,
/// so the rows are written bottom-up, and each row is padded to a 4-byte boundary. The sample uses a
/// BMP because it needs no image codec or package; common image viewers open it directly.
/// </remarks>
internal static class BmpScreenshot {
    private const int FILE_HEADER_SIZE = 14;
    private const int INFO_HEADER_SIZE = 40;
    private const int PIXEL_OFFSET = FILE_HEADER_SIZE + INFO_HEADER_SIZE;
    private const int BITS_PER_PIXEL = 24;
    private const int PIXELS_PER_METER = 2835;
    private const ushort PLANES = 1;
    private const int BI_RGB = 0;

    /// <summary>
    /// Writes one little-endian BITMAPFILEHEADER and BITMAPINFOHEADER followed by bottom-up BGR rows.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="rgbaPixels">Row-major RGBA8 pixels from the top-left, length at least width * height * 4.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is empty or <paramref name="rgbaPixels"/> is too short.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="width"/> or <paramref name="height"/> is not positive.
    /// </exception>
    internal static void Write(string path, int width, int height, ReadOnlySpan<byte> rgbaPixels) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        int requiredPixels = width * height * 4;
        if (rgbaPixels.Length < requiredPixels) {
            throw new ArgumentException(
                $"The RGBA image has {rgbaPixels.Length} byte(s); {width}x{height} needs at least "
                    + $"{requiredPixels}.",
                nameof(rgbaPixels));
        }

        int rowSize = (width * 3 + 3) & ~3;
        int imageSize = rowSize * height;
        int fileSize = PIXEL_OFFSET + imageSize;

        Span<byte> header = stackalloc byte[PIXEL_OFFSET];
        header.Clear();

        // BITMAPFILEHEADER: "BM", file size, two reserved zero words, and the pixel offset.
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], PIXEL_OFFSET);

        // BITMAPINFOHEADER: header size, width, positive height (bottom-up), planes, 24 bpp, BI_RGB,
        // image size, 2835 pixels per meter on both axes, and no palette entries.
        BinaryPrimitives.WriteInt32LittleEndian(header[14..], INFO_HEADER_SIZE);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], PLANES);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], BITS_PER_PIXEL);
        BinaryPrimitives.WriteInt32LittleEndian(header[30..], BI_RGB);
        BinaryPrimitives.WriteInt32LittleEndian(header[34..], imageSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[38..], PIXELS_PER_METER);
        BinaryPrimitives.WriteInt32LittleEndian(header[42..], PIXELS_PER_METER);

        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(header);

        // BMP rows are bottom-up, so walk the top-left input from its last row to its first and swap
        // the red and blue channels. The reused row stays zero-padded to the 4-byte boundary.
        byte[] row = new byte[rowSize];
        for (int y = height - 1; y >= 0; y--) {
            int sourceRow = y * width * 4;
            for (int x = 0; x < width; x++) {
                int source = sourceRow + (x * 4);
                int destination = x * 3;
                row[destination] = rgbaPixels[source + 2];
                row[destination + 1] = rgbaPixels[source + 1];
                row[destination + 2] = rgbaPixels[source];
            }

            stream.Write(row);
        }
    }
}
