using System.Text;

namespace RenderTarget;

/// <summary>
/// Writes display-referred RGBA8 pixels as a binary PPM (P6) image.
/// </summary>
/// <remarks>
/// PPM stores RGB only, so the readback's opaque alpha is dropped. The sample uses it because it needs
/// no image codec or package; a screenshot can be opened by common image viewers and converted offline.
/// </remarks>
internal static class PpmScreenshot {
    /// <summary>
    /// Writes one P6 file with a 255 maximum value.
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

        byte[] header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(header);

        // PPM is RGB, so build one packed row and write it instead of writing one byte at a time.
        byte[] row = new byte[width * 3];
        for (int y = 0; y < height; y++) {
            int sourceRow = y * width * 4;
            for (int x = 0; x < width; x++) {
                int source = sourceRow + (x * 4);
                int destination = x * 3;
                row[destination] = rgbaPixels[source];
                row[destination + 1] = rgbaPixels[source + 1];
                row[destination + 2] = rgbaPixels[source + 2];
            }

            stream.Write(row);
        }
    }
}
