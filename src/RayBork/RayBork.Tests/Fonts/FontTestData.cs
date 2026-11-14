using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>Shared assertions for the synthetic font parser tests.</summary>
internal static class FontTestData {
    /// <summary>Parses a face and asserts the documented failure with a reason fragment.</summary>
    internal static FontFormatException AssertFaceFailure(ReadOnlyMemory<byte> face, string reason) {
        FontFormatException exception = Assert.Throws<FontFormatException>(() => SfntFace.Parse(face));
        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        return exception;
    }

    /// <summary>Resolves a glyph and asserts the documented failure with a reason fragment.</summary>
    internal static FontFormatException AssertGlyphFailure(
        SfntFace face,
        int glyphId,
        string reason) {
        FontFormatException exception = Assert.Throws<FontFormatException>(() => face.GetGlyph(glyphId));
        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        return exception;
    }

    /// <summary>Resolves a glyph and asserts the documented unsupported-feature failure.</summary>
    internal static FontUnsupportedException AssertGlyphUnsupported(
        SfntFace face,
        int glyphId,
        string reason) {
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(
            () => face.GetGlyph(glyphId));
        Assert.Contains(reason, exception.Reason, StringComparison.Ordinal);
        return exception;
    }
}
