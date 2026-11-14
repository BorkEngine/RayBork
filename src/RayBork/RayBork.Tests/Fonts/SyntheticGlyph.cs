using RayBork.Internal;

namespace RayBork.Tests.Fonts;

/// <summary>A synthetic glyph point in font units; on-curve unless marked otherwise.</summary>
internal readonly struct SyntheticPoint {
    internal SyntheticPoint(int x, int y, bool onCurve = true) {
        X = x;
        Y = y;
        OnCurve = onCurve;
    }

    internal int X { get; }

    internal int Y { get; }

    internal bool OnCurve { get; }
}

/// <summary>The component transform a synthetic composite glyph declares.</summary>
internal enum SyntheticComponentTransform {
    None,
    Scale,
    XAndYScale,
    TwoByTwo,
}

/// <summary>One synthetic composite component.</summary>
internal readonly struct SyntheticComponent {
    private SyntheticComponent(
        int glyphId,
        int offsetX,
        int offsetY,
        float a,
        float b,
        float c,
        float d,
        SyntheticComponentTransform transform,
        bool wordArguments,
        bool scaledOffset,
        bool pointMatched) {
        GlyphId = glyphId;
        OffsetX = offsetX;
        OffsetY = offsetY;
        A = a;
        B = b;
        C = c;
        D = d;
        Transform = transform;
        WordArguments = wordArguments;
        ScaledOffset = scaledOffset;
        PointMatched = pointMatched;
    }

    internal int GlyphId { get; }

    internal int OffsetX { get; }

    internal int OffsetY { get; }

    internal float A { get; }

    internal float B { get; }

    internal float C { get; }

    internal float D { get; }

    internal SyntheticComponentTransform Transform { get; }

    internal bool WordArguments { get; }

    internal bool ScaledOffset { get; }

    internal bool PointMatched { get; }

    /// <summary>Creates a translated component with 16-bit arguments.</summary>
    internal static SyntheticComponent Translate(int glyphId, int offsetX, int offsetY) => new(
        glyphId,
        offsetX,
        offsetY,
        1f,
        0f,
        0f,
        1f,
        SyntheticComponentTransform.None,
        wordArguments: true,
        scaledOffset: false,
        pointMatched: false);

    /// <summary>Creates a component with 8-bit arguments.</summary>
    internal static SyntheticComponent ByteArguments(int glyphId, sbyte offsetX, sbyte offsetY) => new(
        glyphId,
        offsetX,
        offsetY,
        1f,
        0f,
        0f,
        1f,
        SyntheticComponentTransform.None,
        wordArguments: false,
        scaledOffset: false,
        pointMatched: false);

    /// <summary>Creates a uniformly scaled component with 16-bit arguments.</summary>
    internal static SyntheticComponent Scale(int glyphId, int offsetX, int offsetY, float scale) => new(
        glyphId,
        offsetX,
        offsetY,
        scale,
        0f,
        0f,
        scale,
        SyntheticComponentTransform.Scale,
        wordArguments: true,
        scaledOffset: false,
        pointMatched: false);

    /// <summary>Creates a component with independent x and y scales.</summary>
    internal static SyntheticComponent ScaleXAndY(
        int glyphId,
        int offsetX,
        int offsetY,
        float scaleX,
        float scaleY) => new(
            glyphId,
            offsetX,
            offsetY,
            scaleX,
            0f,
            0f,
            scaleY,
            SyntheticComponentTransform.XAndYScale,
            wordArguments: true,
            scaledOffset: false,
            pointMatched: false);

    /// <summary>Creates a component with a full 2x2 transform and unscaled offsets.</summary>
    internal static SyntheticComponent TwoByTwo(
        int glyphId,
        int offsetX,
        int offsetY,
        float a,
        float b,
        float c,
        float d) => new(
            glyphId,
            offsetX,
            offsetY,
            a,
            b,
            c,
            d,
            SyntheticComponentTransform.TwoByTwo,
            wordArguments: true,
            scaledOffset: false,
            pointMatched: false);

    /// <summary>Creates a component with a full 2x2 transform and scaled offsets.</summary>
    internal static SyntheticComponent TwoByTwoScaledOffsets(
        int glyphId,
        int offsetX,
        int offsetY,
        float a,
        float b,
        float c,
        float d) => new(
            glyphId,
            offsetX,
            offsetY,
            a,
            b,
            c,
            d,
            SyntheticComponentTransform.TwoByTwo,
            wordArguments: true,
            scaledOffset: true,
            pointMatched: false);

    /// <summary>Creates a component that uses point-matched arguments.</summary>
    internal static SyntheticComponent PointMatch(int glyphId) => new(
        glyphId,
        0,
        0,
        1f,
        0f,
        0f,
        1f,
        SyntheticComponentTransform.None,
        wordArguments: true,
        scaledOffset: false,
        pointMatched: true);
}

/// <summary>The outline source of one synthetic glyph.</summary>
internal enum SyntheticGlyphKind {
    Simple,
    Empty,
    Composite,
}

/// <summary>One glyph of a synthetic face.</summary>
internal sealed class SyntheticGlyph {
    private SyntheticGlyph(
        SyntheticGlyphKind kind,
        int advanceWidth,
        int leftSideBearing,
        SyntheticPoint[][] contours,
        SyntheticComponent[] components,
        FontBounds? bounds) {
        Kind = kind;
        AdvanceWidth = advanceWidth;
        LeftSideBearing = leftSideBearing;
        Contours = contours;
        Components = components;
        Bounds = bounds;
    }

    internal SyntheticGlyphKind Kind { get; }

    internal int AdvanceWidth { get; }

    internal int LeftSideBearing { get; }

    internal SyntheticPoint[][] Contours { get; }

    internal SyntheticComponent[] Components { get; }

    internal FontBounds? Bounds { get; }

    internal static SyntheticGlyph Simple(
        int advanceWidth,
        int leftSideBearing,
        SyntheticPoint[][] contours) {
        foreach (SyntheticPoint[] contour in contours) {
            if (contour.Length == 0) {
                throw new ArgumentException("A synthetic contour needs at least one point.", nameof(contours));
            }
        }

        return new SyntheticGlyph(
            SyntheticGlyphKind.Simple,
            advanceWidth,
            leftSideBearing,
            contours,
            [],
            null);
    }

    internal static SyntheticGlyph Empty(int advanceWidth) => new(
        SyntheticGlyphKind.Empty,
        advanceWidth,
        0,
        [],
        [],
        null);

    internal static SyntheticGlyph Composite(
        int advanceWidth,
        int leftSideBearing,
        FontBounds? bounds,
        SyntheticComponent[] components) => new(
            SyntheticGlyphKind.Composite,
            advanceWidth,
            leftSideBearing,
            [],
            components,
            bounds);
}
