using StbImageSharp;

namespace RayBork.Bake.Textures;

/// <summary>
/// The single texture decode implementation: probes the source header, decodes with StbImageSharp to
/// straight-alpha RGBA8, and validates dimensions and payload size.
/// </summary>
/// <remarks>
/// <para>
/// Decoding is StbImageSharp to straight alpha: no premultiplication, gamma, ICC, flipping, or
/// resampling. The codec normalizes palette, grayscale, and JPEG sources and supplies alpha
/// <c>255</c> where the source has none. The top-left origin is preserved.
/// </para>
/// <para>
/// Every failure is reported through the supplied error callback with <c>RB4007</c> and no partial
/// payload is returned. A PNG whose IHDR declares a 16-bit channel depth is rejected before the
/// decoder runs, as is a source whose declared dimensions exceed the bake's size policy. Malformed
/// and truncated sources are left to the decoder and become a decode diagnostic.
/// </para>
/// </remarks>
internal static class TextureSourceReader {
    private const int HEADER_BYTE_COUNT = 64;
    private const int PNG_HEADER_LENGTH = 25;
    private const int PNG_WIDTH_OFFSET = 16;
    private const int PNG_HEIGHT_OFFSET = 20;
    private const int PNG_BIT_DEPTH_OFFSET = 24;
    private const int PNG_BIT_DEPTH_16 = 16;
    private const int BMP_DIB_HEADER_SIZE_OFFSET = 14;
    private const int BMP_WIDTH_OFFSET = 18;
    private const int BMP_HEIGHT_OFFSET = 22;
    private const int BMP_CORE_HEIGHT_OFFSET = 20;
    private const int BMP_CORE_DIB_HEADER_SIZE = 12;
    private const int BMP_INFO_DIB_HEADER_SIZE = 40;
    private const int TGA_WIDTH_OFFSET = 12;
    private const int TGA_HEIGHT_OFFSET = 14;
    private const int TGA_BITS_PER_PIXEL_OFFSET = 16;

    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Decodes one texture source.</summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="displayPath">The path used in diagnostic messages.</param>
    /// <param name="error">The callback that receives an <c>RB4007</c> code and message.</param>
    /// <param name="payload">The RGBA8 payload when decoding succeeded, otherwise an empty array.</param>
    /// <param name="width">The decoded width when decoding succeeded, otherwise zero.</param>
    /// <param name="height">The decoded height when decoding succeeded, otherwise zero.</param>
    /// <returns><see langword="true"/> when the source decoded and validated successfully.</returns>
    internal static bool TryDecode(
        string sourcePath,
        string displayPath,
        Action<string, string> error,
        out byte[] payload,
        out int width,
        out int height) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayPath);
        ArgumentNullException.ThrowIfNull(error);

        payload = [];
        width = 0;
        height = 0;

        FileStream stream;
        try {
            stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        } catch (Exception exception) when (IsReadFailure(exception)) {
            error(
                TextureDiagnosticCodes.TEXTURE_INVALID,
                $"Failed to read texture source '{displayPath}': {exception.Message}");
            return false;
        }

        using (stream) {
            try {
                byte[] header = new byte[HEADER_BYTE_COUNT];
                int headerLength = stream.ReadAtLeast(
                    header,
                    header.Length,
                    throwOnEndOfStream: false);

                if (TryGetHeaderProblem(
                        header.AsSpan(0, headerLength),
                        out string problem)) {
                    error(
                        TextureDiagnosticCodes.TEXTURE_INVALID,
                        $"Texture source '{displayPath}' cannot be baked: {problem}");
                    return false;
                }

                stream.Position = 0;
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

                if (!TextureLimits.IsSupportedSize(image.Width, image.Height)) {
                    error(
                        TextureDiagnosticCodes.TEXTURE_INVALID,
                        $"Texture source '{displayPath}' decoded to unsupported dimensions "
                            + $"{image.Width}x{image.Height}; width and height must be positive and "
                            + $"at most {TextureLimits.MAX_DIMENSION} pixels.");
                    return false;
                }

                long expectedLength = (long)image.Width * image.Height * 4;
                if (image.Data.Length != expectedLength) {
                    error(
                        TextureDiagnosticCodes.TEXTURE_INVALID,
                        $"Texture source '{displayPath}' decoded to {image.Data.Length} RGBA8 "
                            + $"bytes; expected {expectedLength} (width × height × 4).");
                    return false;
                }

                width = image.Width;
                height = image.Height;
                payload = image.Data;
                return true;
            } catch (OutOfMemoryException) {
                error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Texture source '{displayPath}' could not be decoded: the process ran out of "
                        + "memory.");
                return false;
            } catch (Exception exception) when (IsReadFailure(exception)) {
                error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Failed to read texture source '{displayPath}': {exception.Message}");
                return false;
            } catch (Exception exception) {
                // The third-party decoder may surface malformed input as any exception type; the
                // bake must turn every such failure into a source diagnostic, never a crash.
                error(
                    TextureDiagnosticCodes.TEXTURE_INVALID,
                    $"Texture source '{displayPath}' could not be decoded: {exception.Message}");
                return false;
            }
        }
    }

    private static bool TryGetHeaderProblem(ReadOnlySpan<byte> header, out string problem) {
        problem = string.Empty;

        if (TryReadPng(header, out long pngWidth, out long pngHeight, out int bitDepth)) {
            if (bitDepth == PNG_BIT_DEPTH_16) {
                problem = "16-bit PNG sources are not supported; supply an 8-bit PNG.";
                return true;
            }

            if (!TextureLimits.IsSupportedSize(pngWidth, pngHeight)) {
                problem = $"the PNG header declares unsupported dimensions {pngWidth}x{pngHeight}.";
                return true;
            }

            return false;
        }

        if (TryReadBmp(header, out long bmpWidth, out long bmpHeight)
            && !TextureLimits.IsSupportedSize(bmpWidth, bmpHeight)) {
            problem = $"the BMP header declares unsupported dimensions {bmpWidth}x{bmpHeight}.";
            return true;
        }

        if (TryReadTga(header, out long tgaWidth, out long tgaHeight)
            && !TextureLimits.IsSupportedSize(tgaWidth, tgaHeight)) {
            problem = $"the TGA header declares unsupported dimensions {tgaWidth}x{tgaHeight}.";
            return true;
        }

        return false;
    }

    private static bool TryReadPng(
        ReadOnlySpan<byte> header,
        out long width,
        out long height,
        out int bitDepth) {
        width = 0;
        height = 0;
        bitDepth = 0;

        if (header.Length < PNG_HEADER_LENGTH
            || !header[.._pngSignature.Length].SequenceEqual(_pngSignature)
            || !header.Slice(12, 4).SequenceEqual("IHDR"u8)) {
            return false;
        }

        width = ReadBigEndianUInt32(header, PNG_WIDTH_OFFSET);
        height = ReadBigEndianUInt32(header, PNG_HEIGHT_OFFSET);
        bitDepth = header[PNG_BIT_DEPTH_OFFSET];
        return true;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> header, out long width, out long height) {
        width = 0;
        height = 0;

        if (header.Length < 26 || header[0] != (byte)'B' || header[1] != (byte)'M') {
            return false;
        }

        int dibHeaderSize = ReadInt32LittleEndian(header, BMP_DIB_HEADER_SIZE_OFFSET);

        if (dibHeaderSize == BMP_CORE_DIB_HEADER_SIZE) {
            // BITMAPCOREHEADER: width and height are 16-bit fields at 18 and 20.
            width = ReadUInt16LittleEndian(header, BMP_WIDTH_OFFSET);
            height = ReadUInt16LittleEndian(header, BMP_CORE_HEIGHT_OFFSET);
            return true;
        }

        if (dibHeaderSize < BMP_INFO_DIB_HEADER_SIZE) {
            return false;
        }

        int signedWidth = ReadInt32LittleEndian(header, BMP_WIDTH_OFFSET);
        int signedHeight = ReadInt32LittleEndian(header, BMP_HEIGHT_OFFSET);

        // A negative height is the top-down BMP convention; its magnitude is the row count.
        width = signedWidth;
        height = signedHeight < 0 ? -(long)signedHeight : signedHeight;
        return true;
    }

    private static bool TryReadTga(ReadOnlySpan<byte> header, out long width, out long height) {
        width = 0;
        height = 0;

        if (header.Length < 18) {
            return false;
        }

        byte imageType = header[2];
        byte colorMapType = header[1];
        byte bitsPerPixel = header[TGA_BITS_PER_PIXEL_OFFSET];
        bool isTrueColorOrGray = imageType is 1 or 2 or 3 or 9 or 10 or 11;
        bool hasPlausibleColorMap = colorMapType is 0 or 1;
        bool hasPlausibleDepth = bitsPerPixel is 8 or 15 or 16 or 24 or 32;

        if (!isTrueColorOrGray || !hasPlausibleColorMap || !hasPlausibleDepth) {
            return false;
        }

        width = ReadUInt16LittleEndian(header, TGA_WIDTH_OFFSET);
        height = ReadUInt16LittleEndian(header, TGA_HEIGHT_OFFSET);
        return true;
    }

    private static bool IsReadFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException;

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> buffer, int offset)
        => ((uint)buffer[offset] << 24)
            | ((uint)buffer[offset + 1] << 16)
            | ((uint)buffer[offset + 2] << 8)
            | buffer[offset + 3];

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> buffer, int offset)
        => buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24);

    private static int ReadUInt16LittleEndian(ReadOnlySpan<byte> buffer, int offset)
        => buffer[offset] | (buffer[offset + 1] << 8);
}
