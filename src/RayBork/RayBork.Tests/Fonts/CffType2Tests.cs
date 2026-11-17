using System.Numerics;
using System.Text;
using RayBork.Internal;
using static RayBork.Tests.Fonts.SyntheticCffCharStrings;

namespace RayBork.Tests.Fonts;

/// <summary>
/// Tests the Type 2 charstring interpreter, glyph resolution, seac composition, CID private
/// dictionaries, FontMatrix application, bounds, and the interpreter limits.
/// </summary>
public sealed class CffType2Tests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesRMoveToAndRLineToIntoAClosedContour()
    {
        FontGlyph glyph = Resolve(Concat(RMoveTo(100, 200), RLineTo(50, 0), EndChar()));

        OutlineContour contour = SingleContour(glyph);
        AssertPoint(100, 200, contour.Start);
        Assert.Equal(2, contour.Segments.Length);
        AssertLine(150, 200, contour.Segments.Span[0]);
        AssertLine(100, 200, contour.Segments.Span[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesHMoveToAndVMoveToStarts()
    {
        FontGlyph horizontal = Resolve(Concat(HMoveTo(100), HLineTo(50), EndChar()));
        AssertPoint(100, 0, horizontal.Outline.Contours.Span[0].Start);

        FontGlyph vertical = Resolve(Concat(VMoveTo(50), VLineTo(20), EndChar()));
        AssertPoint(0, 50, vertical.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesAlternatingHLinesAndVLines()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), HLineTo(10, 20, 30), VLineTo(10, 20), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        Assert.Equal(6, segments.Length);
        AssertLine(10, 0, segments[0]);
        AssertLine(10, 20, segments[1]);
        AssertLine(40, 20, segments[2]);
        AssertLine(40, 30, segments[3]);
        AssertLine(60, 30, segments[4]);
        AssertLine(0, 0, segments[5]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesRRCurveTo()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), RRCurveTo(10, 20, 30, 40, 50, 60), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        OutlineSegment curve = segments[0];
        Assert.Equal(OutlineSegmentKind.Cubic, curve.Kind);
        AssertPoint(10, 20, curve.Control1);
        AssertPoint(40, 60, curve.Control2);
        AssertPoint(90, 120, curve.End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesHHCurveToWithAndWithoutLeadingDy(bool leadingDy)
    {
        byte[] curve = leadingDy ? HHCurveTo(5, 10, 20, 30, 40) : HHCurveTo(10, 20, 30, 40);
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), curve, EndChar()));

        OutlineSegment segment = glyph.Outline.Contours.Span[0].Segments.Span[0];
        AssertPoint(10, leadingDy ? 5 : 0, segment.Control1);
        AssertPoint(30, leadingDy ? 35 : 30, segment.Control2);
        AssertPoint(70, leadingDy ? 35 : 30, segment.End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesVVCurveToWithAndWithoutLeadingDx(bool leadingDx)
    {
        byte[] curve = leadingDx ? VVCurveTo(5, 10, 20, 30, 40) : VVCurveTo(10, 20, 30, 40);
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), curve, EndChar()));

        OutlineSegment segment = glyph.Outline.Contours.Span[0].Segments.Span[0];
        AssertPoint(leadingDx ? 5 : 0, 10, segment.Control1);
        AssertPoint(leadingDx ? 25 : 20, 40, segment.Control2);
        AssertPoint(leadingDx ? 25 : 20, 80, segment.End);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesHVCurveToWithOptionalLastArgument(int argumentCount)
    {
        byte[] arguments = argumentCount switch
        {
            4 => HVCurveTo(10, 20, 30, 40),
            5 => HVCurveTo(10, 20, 30, 40, 50),
            _ => HVCurveTo(10, 20, 30, 40, 50, 60, 70, 80, 90),
        };
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), arguments, EndChar()));

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        Assert.Equal(argumentCount == 9 ? 2 : 1, segments.Length - 1);
        AssertPoint(10, 0, segments[0].Control1);
        AssertPoint(30, 30, segments[0].Control2);
        AssertPoint(argumentCount == 5 ? 80 : 30, 70, segments[0].End);
        if (argumentCount == 9)
        {
            AssertPoint(30, 120, segments[1].Control1);
            AssertPoint(90, 190, segments[1].Control2);
            AssertPoint(170, 280, segments[1].End);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesVHCurveToWithOptionalLastArgument(int argumentCount)
    {
        byte[] arguments = argumentCount switch
        {
            4 => VHCurveTo(10, 20, 30, 40),
            5 => VHCurveTo(10, 20, 30, 40, 50),
            _ => VHCurveTo(10, 20, 30, 40, 50, 60, 70, 80, 90),
        };
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), arguments, EndChar()));

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        Assert.Equal(argumentCount == 9 ? 2 : 1, segments.Length - 1);
        AssertPoint(0, 10, segments[0].Control1);
        AssertPoint(20, 40, segments[0].Control2);
        AssertPoint(60, argumentCount == 5 ? 90 : 40, segments[0].End);
        if (argumentCount == 9)
        {
            AssertPoint(110, 40, segments[1].Control1);
            AssertPoint(170, 110, segments[1].Control2);
            AssertPoint(260, 190, segments[1].End);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesRCurveLine()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), RCurveLine(10, 20, 30, 40, 50, 60, 70, 80), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        Assert.Equal(OutlineSegmentKind.Cubic, segments[0].Kind);
        AssertPoint(10, 20, segments[0].Control1);
        AssertPoint(40, 60, segments[0].Control2);
        AssertPoint(90, 120, segments[0].End);
        AssertLine(160, 200, segments[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesRLineCurve()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), RLineCurve(10, 20, 30, 40, 50, 60, 70, 80, 90, 100), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        AssertLine(10, 20, segments[0]);
        AssertLine(40, 60, segments[1]);
        Assert.Equal(OutlineSegmentKind.Cubic, segments[2].Kind);
        AssertPoint(90, 120, segments[2].Control1);
        AssertPoint(160, 200, segments[2].Control2);
        AssertPoint(250, 300, segments[2].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesHFlex()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), HFlex(10, 20, 30, 40, 50, 60, 70), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        AssertPoint(10, 0, segments[0].Control1);
        AssertPoint(30, 30, segments[0].Control2);
        AssertPoint(70, 30, segments[0].End);
        AssertPoint(120, 30, segments[1].Control1);
        AssertPoint(180, 0, segments[1].Control2);
        AssertPoint(250, 0, segments[1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesFlex()
    {
        FontGlyph glyph = Resolve(
            Concat(
                RMoveTo(0, 0),
                Flex(10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 50),
                EndChar()
            )
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        AssertPoint(10, 20, segments[0].Control1);
        AssertPoint(40, 60, segments[0].Control2);
        AssertPoint(90, 120, segments[0].End);
        AssertPoint(160, 200, segments[1].Control1);
        AssertPoint(250, 300, segments[1].Control2);
        AssertPoint(360, 420, segments[1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesHFlex1()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(0, 0), HFlex1(10, 5, 20, 15, 30, 40, 25, 10, 35), EndChar())
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        AssertPoint(10, 5, segments[0].Control1);
        AssertPoint(30, 20, segments[0].Control2);
        AssertPoint(60, 20, segments[0].End);
        AssertPoint(100, 20, segments[1].Control1);
        AssertPoint(125, 30, segments[1].Control2);
        AssertPoint(160, 0, segments[1].End);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesFlex1InTheDominantDirection(bool horizontalDominant)
    {
        byte[] curve = horizontalDominant
            ? Flex1(10, 0, 20, 0, 30, 0, 40, 40, 0, 10, 60)
            : Flex1(10, 20, 0, 20, 10, 0, 0, 20, 10, 20, 40);
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), curve, EndChar()));

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        if (horizontalDominant)
        {
            AssertPoint(10, 0, segments[0].Control1);
            AssertPoint(30, 0, segments[0].Control2);
            AssertPoint(60, 0, segments[0].End);
            AssertPoint(100, 40, segments[1].Control1);
            AssertPoint(100, 50, segments[1].Control2);
            AssertPoint(160, 0, segments[1].End);
        }
        else
        {
            AssertPoint(10, 20, segments[0].Control1);
            AssertPoint(10, 40, segments[0].Control2);
            AssertPoint(20, 40, segments[0].End);
            AssertPoint(20, 60, segments[1].Control1);
            AssertPoint(30, 80, segments[1].Control2);
            AssertPoint(0, 120, segments[1].End);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesFixedPointAndShortIntOperands()
    {
        FontGlyph glyph = Resolve(
            Concat(
                RMoveTo(0, 0),
                Concat(Fixed(16.5), Number(10), Bytes(RLINETO)),
                Concat(Number(300), Number(0), Bytes(RLINETO)),
                Concat(Number(2000), Number(0), Bytes(RLINETO)),
                Concat(Number(-300), Number(-200), Bytes(RLINETO)),
                Concat(Fixed(-16.5), Number(0), Bytes(RLINETO)),
                EndChar()
            )
        );

        ReadOnlySpan<OutlineSegment> segments = glyph.Outline.Contours.Span[0].Segments.Span;
        AssertPoint(16.5f, 10, segments[0].End);
        AssertPoint(316.5f, 10, segments[1].End);
        AssertPoint(2316.5f, 10, segments[2].End);
        AssertPoint(2016.5f, -190, segments[3].End);
        AssertPoint(2000, -190, segments[4].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AddsAClosingLineOnlyWhenTheLastPointIsNotTheStart()
    {
        FontGlyph open = Resolve(Concat(RMoveTo(0, 0), RLineTo(10, 0), EndChar()));
        Assert.Equal(2, open.Outline.Contours.Span[0].Segments.Length);

        FontGlyph closed = Resolve(
            Concat(RMoveTo(0, 0), RLineTo(10, 0), RLineTo(-10, 0), EndChar())
        );
        Assert.Equal(2, closed.Outline.Contours.Span[0].Segments.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DropsAContourWithNoSegments()
    {
        FontGlyph glyph = Resolve(Concat(RMoveTo(10, 10), EndChar()));

        Assert.True(glyph.IsEmpty);
        Assert.Equal(0, glyph.Bounds.XMin);
        Assert.Equal(0, glyph.Bounds.YMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesAnImplicitlyEndedCharString()
    {
        FontGlyph glyph = Resolve(Concat(RMoveTo(0, 0), RLineTo(10, 0)));

        Assert.False(glyph.IsEmpty);
        Assert.Equal(2, glyph.Outline.Contours.Span[0].Segments.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesAnEmptyCharStringAsAnEmptyGlyph()
    {
        FontGlyph glyph = Resolve([]);

        Assert.True(glyph.IsEmpty);
        Assert.Equal(0, glyph.Outline.Contours.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesEndCharInsideASubroutine()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Concat(RMoveTo(10, 10), RLineTo(20, 0), EndChar()));
        int glyphId = cff.AddGlyph(Concat(CallSubr(-107), RLineTo(100, 0), EndChar()));

        FontGlyph glyph = SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);

        OutlineContour contour = SingleContour(glyph);
        AssertPoint(10, 10, contour.Start);
        Assert.Equal(2, contour.Segments.Length);
        AssertLine(30, 10, contour.Segments.Span[0]);
        AssertLine(10, 10, contour.Segments.Span[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesAGlyphThroughTheCmap()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()));
        SfntFace face = SfntFace.Parse(cff.BuildFace((0x41, 1)));

        Assert.True(face.TryGetGlyphId(new Rune('A'), out int glyphId));
        Assert.Equal(1, glyphId);
        FontGlyph glyph = face.GetGlyph(glyphId);
        AssertPoint(100, 200, glyph.Outline.Contours.Span[0].Start);
        Assert.False(face.TryGetGlyphId(new Rune('B'), out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReportsAdvancesFromHmtx()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(EndChar());
        SyntheticFontBuilder builder = SyntheticFontBuilder.Create();
        builder.AddEmpty(730);

        SfntFace face = SfntFace.Parse(builder.UseCff(cff.Build()).WithCmap(4, (0x41, 1)).Build());

        Assert.Equal(500, face.GetAdvanceWidth(0));
        Assert.Equal(730, face.GetAdvanceWidth(1));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsumesAWidthOnEveryFirstStemOperator()
    {
        byte[][] stems = [HStem(0, 10), VStem(0, 10), HStemHm(0, 10), VStemHm(0, 10)];
        foreach (byte[] stem in stems)
        {
            FontGlyph glyph = Resolve(
                Concat(WithWidth(300, stem), RMoveTo(10, 20), RLineTo(1, 0), EndChar())
            );

            AssertPoint(10, 20, SingleContour(glyph).Start);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsumesAWidthOnEveryMoveOperator()
    {
        (byte[] CharString, Vector2 Expected)[] moves =
        [
            (WithWidth(300, RMoveTo(10, 20)), new Vector2(10, 20)),
            (WithWidth(300, HMoveTo(30)), new Vector2(30, 0)),
            (WithWidth(300, VMoveTo(40)), new Vector2(0, 40)),
        ];
        foreach ((byte[] charString, Vector2 expected) in moves)
        {
            FontGlyph glyph = Resolve(Concat(charString, RLineTo(5, 5), EndChar()));

            Assert.Equal(expected, SingleContour(glyph).Start);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsumesAWidthOnHintMaskAndCntrMask()
    {
        foreach (byte[] mask in new[] { HintMask([]), CntrMask([]) })
        {
            FontGlyph glyph = Resolve(
                Concat(WithWidth(300, mask), RMoveTo(10, 20), RLineTo(1, 0), EndChar())
            );

            AssertPoint(10, 20, SingleContour(glyph).Start);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsumesAWidthOnEndCharOnly()
    {
        FontGlyph glyph = Resolve(WithWidth(300, EndChar()));

        Assert.True(glyph.IsEmpty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SkipsHintMasksForOneToNineHints(int hintCount)
    {
        int[] stem = new int[hintCount * 2];
        for (int index = 0; index < hintCount; index++)
        {
            stem[index * 2] = 0;
            stem[index * 2 + 1] = 10;
        }

        byte[] mask = new byte[(hintCount + 7) / 8];
        mask.AsSpan().Fill(0x0F);
        FontGlyph glyph = Resolve(
            Concat(HStem(stem), HintMask(mask), RMoveTo(10, 20), RLineTo(1, 0), EndChar())
        );

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SkipsCounterMasksForNineHints()
    {
        FontGlyph glyph = Resolve(
            Concat(
                HStem(0, 10, 0, 10, 0, 10, 0, 10, 0, 10, 0, 10, 0, 10, 0, 10, 0, 10),
                CntrMask([0x0F, 0x0F]),
                RMoveTo(10, 20),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CountsImplicitStemsBeforeAHintMask()
    {
        FontGlyph glyph = Resolve(
            Concat(
                HStemHm(0, 10),
                Numbers(20, 30),
                HintMask([0x0F]),
                RMoveTo(10, 20),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAHintMaskPastTheEndOfTheCharString()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            Resolve(Concat(HStem(0, 10), HintMask([])))
        );
        Assert.Contains("hint mask", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnOddStemOperandCountAfterTheWidth()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            Resolve(Concat(RMoveTo(0, 0), HStem(10)))
        );
        Assert.Contains("odd number", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SkipsAHintMaskWithNoHintsAndNoWidth()
    {
        FontGlyph glyph = Resolve(Concat(HintMask([]), RMoveTo(10, 20), RLineTo(1, 0), EndChar()));

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsumesAWidthBeforeANonEmptyHintMask()
    {
        FontGlyph glyph = Resolve(
            Concat(
                WithWidth(300, Concat(Number(0), Number(10), HintMask([0x0F]))),
                RMoveTo(10, 20),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CountsExplicitAndImplicitStemsBeforeAHintMask()
    {
        FontGlyph glyph = Resolve(
            Concat(
                HStemHm(0, 10),
                Numbers(20, 30),
                HintMask([0x0F]),
                RMoveTo(10, 20),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsExactlyTheHintLimit()
    {
        List<byte[]> parts = StemParts(65536);
        parts.Add(RMoveTo(10, 20));
        parts.Add(RLineTo(1, 0));
        parts.Add(EndChar());

        FontGlyph glyph = ResolveWith(Concat(parts.ToArray()));

        AssertPoint(10, 20, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsOneHintPastTheHintLimit()
    {
        List<byte[]> parts = StemParts(65537);
        parts.Add(EndChar());

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveWith(Concat(parts.ToArray()))
        );
        Assert.Contains("65536", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesAddSubMulAndDiv()
    {
        AssertPoint(
            100,
            0,
            ResolveWith(
                Concat(
                    Numbers(50, 50),
                    EscapeOperator(ADD),
                    Number(0),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            100,
            100,
            ResolveWith(
                Concat(
                    Numbers(200, 100),
                    EscapeOperator(SUB),
                    Numbers(10, 10),
                    EscapeOperator(MUL),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            100,
            50,
            ResolveWith(
                Concat(
                    Numbers(50, 2),
                    EscapeOperator(MUL),
                    Numbers(100, 2),
                    EscapeOperator(DIV),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesNegAbsAndSqrt()
    {
        AssertPoint(
            50,
            10,
            ResolveWith(
                Concat(
                    Number(-50),
                    EscapeOperator(NEG),
                    Number(100),
                    EscapeOperator(SQRT),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            50,
            0,
            ResolveWith(
                Concat(
                    Number(-50),
                    EscapeOperator(ABS),
                    Number(-4),
                    EscapeOperator(SQRT),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TreatsDivisionByZeroAsZero()
    {
        AssertPoint(
            0,
            50,
            ResolveWith(
                Concat(
                    Number(100),
                    Number(0),
                    EscapeOperator(DIV),
                    Number(50),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesAndOrNotAndEq()
    {
        AssertPoint(
            0,
            100,
            ResolveWith(
                Concat(
                    Numbers(1, 0),
                    EscapeOperator(AND),
                    Number(100),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            1,
            100,
            ResolveWith(
                Concat(
                    Numbers(0, 1),
                    EscapeOperator(OR),
                    Number(100),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            1,
            100,
            ResolveWith(
                Concat(
                    Number(0),
                    EscapeOperator(NOT),
                    Number(100),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            1,
            100,
            ResolveWith(
                Concat(
                    Numbers(50, 50),
                    EscapeOperator(EQ),
                    Number(100),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Theory]
    [InlineData(10, 50, 1, 2, 10, 77)]
    [InlineData(10, 50, 2, 1, 50, 77)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesIfElseInBothDirections(
        int first,
        int second,
        int firstValue,
        int secondValue,
        int expectedX,
        int expectedY
    )
    {
        FontGlyph glyph = ResolveWith(
            Concat(
                Numbers(first, second, firstValue, secondValue),
                EscapeOperator(IFELSE),
                Number(77),
                Bytes(RMOVETO),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(expectedX, expectedY, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesDupDropAndExch()
    {
        AssertPoint(
            100,
            0,
            ResolveWith(
                Concat(
                    Number(50),
                    EscapeOperator(DUP),
                    EscapeOperator(ADD),
                    Number(0),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            20,
            10,
            ResolveWith(
                Concat(
                    Numbers(10, 20),
                    EscapeOperator(EXCH),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            10,
            20,
            ResolveWith(
                Concat(
                    Numbers(10, 20, 30),
                    EscapeOperator(DROP),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesIndexIncludingANegativeIndex()
    {
        AssertPoint(
            10,
            50,
            ResolveWith(
                Concat(
                    Numbers(10, 50, 1),
                    EscapeOperator(INDEX),
                    EscapeOperator(DROP),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );

        AssertPoint(
            10,
            50,
            ResolveWith(
                Concat(
                    Numbers(10, 50, -1),
                    EscapeOperator(INDEX),
                    EscapeOperator(DROP),
                    Bytes(RMOVETO),
                    RLineTo(1, 0),
                    EndChar()
                )
            )
                .Outline.Contours.Span[0]
                .Start
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnIndexPastTheStackDepth()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(Numbers(10, 2), EscapeOperator(INDEX), EndChar()))
        );
        Assert.Contains("indexes", exception.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 10, 50)]
    [InlineData(1, 100, 10)]
    [InlineData(-1, 50, 100)]
    [InlineData(4, 100, 10)]
    [InlineData(-4, 50, 100)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesRollInBothDirections(int shift, int expectedX, int expectedY)
    {
        FontGlyph glyph = ResolveWith(
            Concat(
                Numbers(10, 50, 100, 3, shift),
                EscapeOperator(ROLL),
                EscapeOperator(DROP),
                Bytes(RMOVETO),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(expectedX, expectedY, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesARollHeavySubroutineWithinTheTokenBudget()
    {
        List<byte[]> sequences = [];
        for (int index = 0; index < 250; index++)
        {
            sequences.Add(
                Concat(
                    Numbers(10, 50, 100, 3, 1),
                    EscapeOperator(ROLL),
                    EscapeOperator(DROP),
                    EscapeOperator(DROP),
                    EscapeOperator(DROP)
                )
            );
        }

        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Concat([.. sequences, Return()]));
        List<byte[]> calls = new(444);
        for (int index = 0; index < 443; index++)
        {
            calls.Add(CallSubr(-107));
        }

        calls.Add(EndChar());
        int glyphId = cff.AddGlyph(Concat(calls.ToArray()));

        FontGlyph glyph = SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);

        Assert.True(glyph.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsARollLargerThanTheStack()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(Numbers(10, 50, 3, 1), EscapeOperator(ROLL), EndChar()))
        );
        Assert.Contains("rolls", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EvaluatesPutAndGet()
    {
        FontGlyph glyph = ResolveWith(
            Concat(
                Numbers(100, 0),
                EscapeOperator(PUT),
                Numbers(50, 0),
                EscapeOperator(GET),
                Bytes(RMOVETO),
                RLineTo(1, 0),
                EndChar()
            )
        );

        AssertPoint(50, 100, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReadsAnUnwrittenTransientEntryAsZero()
    {
        FontGlyph glyph = ResolveWith(
            Concat(Numbers(50, 5), EscapeOperator(GET), Bytes(RMOVETO), RLineTo(1, 0), EndChar())
        );

        AssertPoint(50, 0, glyph.Outline.Contours.Span[0].Start);
    }

    [Theory]
    [InlineData(32)]
    [InlineData(-1)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnOutOfRangeTransientIndex(int index)
    {
        byte[] charString =
            index >= 0
                ? Concat(Number(1), Number(index), EscapeOperator(GET))
                : Concat(Number(5), Number(index), EscapeOperator(PUT));

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(charString)
        );
        Assert.Contains("transient", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UsesADeterministicRandomValue()
    {
        byte[] charString = Concat(
            EscapeOperator(RANDOM),
            Number(100),
            EscapeOperator(ADD),
            Number(0),
            Bytes(RMOVETO),
            RLineTo(1, 0),
            EndChar()
        );

        FontGlyph first = ResolveWith(charString);
        FontGlyph second = ResolveWith(charString);

        AssertPoint(100.5f, 0, first.Outline.Contours.Span[0].Start);
        Assert.Equal(first.Outline.Contours.Span[0].Start, second.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsTheDeprecatedDotsectionOperator()
    {
        FontGlyph glyph = ResolveWith(
            Concat(EscapeOperator(DOTSECTION), RMoveTo(10, 20), RLineTo(1, 0), EndChar())
        );

        AssertPoint(10, 20, glyph.Outline.Contours.Span[0].Start);
    }

    [Theory]
    [InlineData(new byte[] { 15 })]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 2 })]
    [InlineData(new byte[] { 9 })]
    [InlineData(new byte[] { 13 })]
    [InlineData(new byte[] { 16 })]
    [InlineData(new byte[] { 17 })]
    [InlineData(new byte[] { 12, 99 })]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsUnknownOperators(byte[] charString)
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(charString)
        );
        Assert.Contains("unknown", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsStackUnderflow()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(EscapeOperator(ADD))
        );
        Assert.Contains("underflow", exception.Reason, StringComparison.Ordinal);

        Assert.Throws<FontFormatException>(() => ResolveWith(Concat(Number(10), Bytes(RMOVETO))));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnExceedinglyDeepOperandStack()
    {
        int[] values = new int[49];
        values.AsSpan().Fill(1);
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveWith(Concat(Numbers(values), Bytes(RMOVETO), RLineTo(1, 0), EndChar()))
        );
        Assert.Contains("48", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsADrawingOperatorBeforeTheFirstMoveTo()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(RLineTo(10, 0), EndChar()))
        );
        Assert.Contains("before its first moveto", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesLocalAndGlobalSubroutines()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Concat(RMoveTo(10, 10), RLineTo(20, 0), Return()));
        cff.WithGlobalSubrs(Concat(RLineTo(0, 30), Return()));
        int glyphId = cff.AddGlyph(Concat(CallSubr(-107), CallGSubr(-107), EndChar()));

        FontGlyph glyph = SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);

        OutlineContour contour = SingleContour(glyph);
        AssertPoint(10, 10, contour.Start);
        Assert.Equal(3, contour.Segments.Length);
        AssertLine(30, 10, contour.Segments.Span[0]);
        AssertLine(30, 40, contour.Segments.Span[1]);
        AssertLine(10, 10, contour.Segments.Span[2]);
    }

    [Theory]
    [InlineData(100, 99, -8, false)]
    [InlineData(1240, 1239, 108, true)]
    [InlineData(33900, 33899, 1131, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UsesTheDocumentedSubroutineBias(
        int subroutineCount,
        int subroutineIndex,
        int operand,
        bool useGlobalSubrs
    )
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        byte[][] subrs = new byte[subroutineCount][];
        for (int index = 0; index < subroutineCount; index++)
        {
            subrs[index] = Return();
        }

        subrs[subroutineIndex] = Concat(RMoveTo(5, 6), RLineTo(10, 0), Return());
        if (useGlobalSubrs)
        {
            cff.WithGlobalSubrs(subrs);
        }
        else
        {
            cff.WithLocalSubrs(subrs);
        }

        int glyphId = cff.AddGlyph(
            Concat(useGlobalSubrs ? CallGSubr(operand) : CallSubr(operand), EndChar())
        );

        FontGlyph glyph = SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);

        AssertPoint(5, 6, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsASubroutineIndexOutsideTheIndex()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Return());
        int glyphId = cff.AddGlyph(Concat(CallSubr(1000), EndChar()));

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId)
        );
        Assert.Contains("subroutine index", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AllowsTenNestedSubroutineLevels()
    {
        FontGlyph glyph = ResolveSubroutineChain(10);

        AssertPoint(5, 6, SingleContour(glyph).Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsElevenNestedSubroutineLevels()
    {
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveSubroutineChain(11)
        );
        Assert.Contains("nesting", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsExactlyTheSubroutineCallBudget()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Return());
        List<byte[]> calls = new(65537);
        for (int index = 0; index < 65536; index++)
        {
            calls.Add(CallSubr(-107));
        }

        calls.Add(EndChar());
        int glyphId = cff.AddGlyph(Concat(calls.ToArray()));

        FontGlyph glyph = SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);

        Assert.True(glyph.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsMoreThanTheSubroutineCallBudget()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.WithLocalSubrs(Return());
        List<byte[]> calls = new(65538);
        for (int index = 0; index < 65537; index++)
        {
            calls.Add(CallSubr(-107));
        }

        calls.Add(EndChar());
        int glyphId = cff.AddGlyph(Concat(calls.ToArray()));

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId)
        );
        Assert.Contains("65536", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsExactlyTheExecutedTokenBudget()
    {
        FontGlyph glyph = ResolveWith(BuildTokenCharString(999999));

        Assert.True(glyph.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsOneTokenPastTheExecutedTokenBudget()
    {
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveWith(BuildTokenCharString(1000000))
        );

        Assert.Contains("1000000", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnArithmeticOnlySubroutineThatExceedsTheTokenBudget()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        List<byte[]> arithmetic = [];
        for (int index = 0; index < 10; index++)
        {
            arithmetic.Add(Concat(Numbers(1, 1), EscapeOperator(AND), EscapeOperator(DROP)));
        }

        cff.WithLocalSubrs(Concat([.. arithmetic, Return()]));
        List<byte[]> calls = new(65538);
        for (int index = 0; index < 65537; index++)
        {
            calls.Add(CallSubr(-107));
        }

        calls.Add(EndChar());
        int glyphId = cff.AddGlyph(Concat(calls.ToArray()));

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId)
        );
        Assert.Contains("1000000", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsReturnOutsideASubroutine()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(RMoveTo(0, 0), Return()))
        );
        Assert.Contains("return", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComposesSeacBaseAndAccent()
    {
        SyntheticCff cff = CreateSeacTable(EndCharSeac(100, 50, 65, 66));

        FontGlyph glyph = Resolve(cff, 3);

        Assert.Equal(2, glyph.Outline.Contours.Length);
        AssertPoint(10, 20, glyph.Outline.Contours.Span[0].Start);
        AssertPoint(101, 52, glyph.Outline.Contours.Span[1].Start);
        Assert.Equal(2, glyph.Outline.Contours.Span[1].Segments.Length);
        AssertLine(106, 52, glyph.Outline.Contours.Span[1].Segments.Span[0]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComposesSeacWithAWidthPrefix()
    {
        SyntheticCff cff = CreateSeacTable(WithWidth(300, EndCharSeac(100, 50, 65, 66)));

        FontGlyph glyph = Resolve(cff, 3);

        Assert.Equal(2, glyph.Outline.Contours.Length);
        AssertPoint(101, 52, glyph.Outline.Contours.Span[1].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsASeacWithAnUnassignedStandardCode()
    {
        SyntheticCff cff = CreateSeacTable(EndCharSeac(0, 0, 127, 66));

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("cannot resolve", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsASeacWhoseSidIsNotInTheCharset()
    {
        SyntheticCff cff = CreateSeacTable(EndCharSeac(0, 0, 90, 66));

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("cannot resolve", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsANestedSeac()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(EndCharSeac(0, 0, 65, 66));
        cff.AddGlyph(Concat(RMoveTo(1, 2), RLineTo(5, 0), EndChar()));
        int glyphId = cff.AddGlyph(EndCharSeac(10, 10, 65, 66));
        cff.WithCharset(34, 35, 36);

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, glyphId)
        );
        Assert.Contains("nested seac", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsSeacInACidKeyedFace()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AsCid();
        cff.AddFontDict();
        cff.WithFdSelectFormat0(0, 0);
        cff.AddGlyph(EndCharSeac(10, 10, 65, 66));

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 1)
        );
        Assert.Contains("CID-keyed", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnEndCharWithAnUnsupportedOperandCount()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(RMoveTo(0, 0), Number(1), Number(2), Bytes(ENDCHAR)))
        );
        Assert.Contains("endchar", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesGlyphsThroughPerFdPrivateDicts()
    {
        SyntheticCff cff = CreateCidTable(withFdSelectFormat0: true, rangeFormat: false);

        SfntFace face = SfntFace.Parse(cff.BuildFace());

        Assert.True(face.GetGlyph(0).IsEmpty);
        AssertPoint(10, 10, face.GetGlyph(1).Outline.Contours.Span[0].Start);
        AssertPoint(300, 40, face.GetGlyph(2).Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesGlyphsThroughFdSelectFormatThree()
    {
        SyntheticCff cff = CreateCidTable(withFdSelectFormat0: false, rangeFormat: true);

        SfntFace face = SfntFace.Parse(cff.BuildFace());

        Assert.True(face.GetGlyph(0).IsEmpty);
        AssertPoint(10, 10, face.GetGlyph(1).Outline.Contours.Span[0].Start);
        AssertPoint(300, 40, face.GetGlyph(2).Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesTheDefaultFontMatrixAsIdentity()
    {
        FontGlyph glyph = Resolve(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()));

        AssertPoint(100, 200, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesANonDefaultFontMatrixScale()
    {
        SyntheticCff cff = CreateGlyphTable(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()))
            .WithFontMatrix(0.002, 0, 0, 0.002, 0, 0);

        FontGlyph glyph = Resolve(cff, 1);

        AssertPoint(200, 400, glyph.Outline.Contours.Span[0].Start);
        AssertPoint(220, 400, glyph.Outline.Contours.Span[0].Segments.Span[0].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesTheDefaultFontMatrixAtAnotherUnitsPerEm()
    {
        SyntheticCff cff = CreateGlyphTable(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()));

        FontGlyph glyph = Resolve(cff, 1, unitsPerEm: 2048);

        Assert.Equal(204.8f, glyph.Outline.Contours.Span[0].Start.X, 3);
        Assert.Equal(409.6f, glyph.Outline.Contours.Span[0].Start.Y, 3);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesTheFontMatrixTranslation()
    {
        SyntheticCff cff = CreateGlyphTable(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()))
            .WithFontMatrix(0.001, 0, 0, 0.001, 0.01, 0.02);

        FontGlyph glyph = Resolve(cff, 1);

        AssertPoint(110, 220, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesTheFontMatrixOffDiagonalTerms()
    {
        SyntheticCff cff = CreateGlyphTable(Concat(RMoveTo(100, 200), RLineTo(10, 0), EndChar()))
            .WithFontMatrix(0, 0.001, 0.001, 0, 0, 0);

        FontGlyph glyph = Resolve(cff, 1);

        AssertPoint(200, 100, glyph.Outline.Contours.Span[0].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AppliesTheFontMatrixToSeacComponents()
    {
        SyntheticCff cff = CreateSeacTable(EndCharSeac(100, 50, 65, 66))
            .WithFontMatrix(0.002, 0, 0, 0.002, 0, 0);

        FontGlyph glyph = Resolve(cff, 3);

        AssertPoint(20, 40, glyph.Outline.Contours.Span[0].Start);
        AssertPoint(202, 104, glyph.Outline.Contours.Span[1].Start);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SharesTheContourBudgetAcrossSeacComponents()
    {
        List<byte[]> baseParts = new(4097);
        for (int index = 0; index < 4096; index++)
        {
            baseParts.Add(RMoveTo(0, 0));
        }

        baseParts.Add(EndChar());
        SyntheticCff cff = CreateSeacTable(
            Concat(baseParts.ToArray()),
            Concat(RMoveTo(1, 2), RLineTo(1, 0), EndChar()),
            EndCharSeac(0, 0, 65, 66)
        );

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("4096", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SharesThePointBudgetAcrossSeacComponents()
    {
        List<byte[]> baseParts = new(6668) { RMoveTo(0, 0) };
        for (int index = 0; index < 6666; index++)
        {
            baseParts.Add(RRCurveTo(1, 1, 1, 1, 1, 1));
        }

        baseParts.Add(RLineTo(1, 0));
        baseParts.Add(EndChar());
        SyntheticCff cff = CreateSeacTable(
            Concat(baseParts.ToArray()),
            Concat(RMoveTo(1, 2), RLineTo(1, 0), EndChar()),
            EndCharSeac(0, 0, 65, 66)
        );

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("20000", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SharesTheSubroutineCallBudgetAcrossSeacComponents()
    {
        List<byte[]> baseParts = new(65537);
        for (int index = 0; index < 65536; index++)
        {
            baseParts.Add(CallSubr(-107));
        }

        baseParts.Add(EndChar());
        SyntheticCff cff = CreateSeacTable(
            Concat(baseParts.ToArray()),
            Concat(CallSubr(-107), EndChar()),
            EndCharSeac(0, 0, 65, 66)
        );
        cff.WithLocalSubrs(Return());

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("65536", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SharesTheTokenBudgetAcrossSeacComponents()
    {
        SyntheticCff cff = CreateSeacTable(
            BuildTokenCharString(600000),
            BuildTokenCharString(500000),
            EndCharSeac(0, 0, 65, 66)
        );

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            Resolve(cff, 3)
        );
        Assert.Contains("1000000", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ComputesBoundsFromTheControlPointHull()
    {
        FontGlyph glyph = Resolve(
            Concat(RMoveTo(10, 10), RRCurveTo(100, 200, 0, 0, 50, 0), EndChar())
        );

        Assert.Equal(10, glyph.Bounds.XMin);
        Assert.Equal(10, glyph.Bounds.YMin);
        Assert.Equal(160, glyph.Bounds.XMax);
        Assert.Equal(210, glyph.Bounds.YMax);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BoundsContainEveryFlattenedOutlinePoint()
    {
        FontGlyph glyph = Resolve(
            Concat(
                RMoveTo(10, 20),
                RRCurveTo(100, 200, -50, -100, 20, 300),
                HLineTo(-200, 40),
                VLineTo(30),
                EndChar()
            )
        );

        foreach (OutlineContour contour in glyph.Outline.Contours.Span)
        {
            Vector2 current = contour.Start;
            AssertContains(glyph.Bounds, current);
            foreach (OutlineSegment segment in contour.Segments.Span)
            {
                AssertContains(glyph.Bounds, segment.End);
                if (segment.Kind == OutlineSegmentKind.Cubic)
                {
                    for (int step = 1; step < 32; step++)
                    {
                        float t = step / 32f;
                        AssertContains(glyph.Bounds, CubicPoint(current, segment, t));
                    }
                }

                current = segment.End;
            }

            Assert.Equal(contour.Start, current);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsNonFiniteCoordinatesWithTheIdentityMatrix()
    {
        List<byte[]> parts = [Number(32767)];
        for (int index = 0; index < 70; index++)
        {
            parts.Add(Number(32767));
            parts.Add(EscapeOperator(MUL));
        }

        parts.Add(Number(0));
        parts.Add(Bytes(RMOVETO));
        parts.Add(RLineTo(1, 0));
        parts.Add(EndChar());

        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(parts.ToArray()))
        );
        Assert.Contains("not finite", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsExactlyThePointLimit()
    {
        List<byte[]> parts = new(6669) { RMoveTo(0, 0) };
        for (int index = 0; index < 6666; index++)
        {
            parts.Add(RRCurveTo(1, 1, 1, 1, 1, 1));
        }

        parts.Add(RLineTo(1, 0));
        parts.Add(EndChar());

        FontGlyph glyph = ResolveWith(Concat(parts.ToArray()));

        Assert.False(glyph.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsOnePointPastThePointLimit()
    {
        List<byte[]> parts = new(6670) { RMoveTo(0, 0) };
        for (int index = 0; index < 6666; index++)
        {
            parts.Add(RRCurveTo(1, 1, 1, 1, 1, 1));
        }

        parts.Add(RLineTo(1, 0));
        parts.Add(RLineTo(1, 0));
        parts.Add(EndChar());

        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveWith(Concat(parts.ToArray()))
        );
        Assert.Contains("20000", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcceptsExactlyTheContourLimit()
    {
        List<byte[]> moves = new(4097);
        for (int index = 0; index < 4096; index++)
        {
            moves.Add(RMoveTo(0, 0));
        }

        moves.Add(EndChar());
        FontGlyph glyph = ResolveWith(Concat(moves.ToArray()));

        Assert.True(glyph.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsOneContourPastTheContourLimit()
    {
        List<byte[]> moves = new(4098);
        for (int index = 0; index < 4097; index++)
        {
            moves.Add(RMoveTo(0, 0));
        }

        moves.Add(EndChar());
        FontUnsupportedException exception = Assert.Throws<FontUnsupportedException>(() =>
            ResolveWith(Concat(moves.ToArray()))
        );
        Assert.Contains("4096", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InterpretsTheSameFaceDeterministically()
    {
        byte[] faceBytes = CreateGlyphTable(
                Concat(
                    RMoveTo(10, 20),
                    RRCurveTo(100, 200, -50, -100, 20, 300),
                    HLineTo(-200, 40),
                    VLineTo(30),
                    EndChar()
                )
            )
            .BuildFace();

        SfntFace first = SfntFace.Parse(faceBytes);
        SfntFace second = SfntFace.Parse(faceBytes);

        Assert.Equal(first.GlyphCount, second.GlyphCount);
        for (int glyphId = 0; glyphId < first.GlyphCount; glyphId++)
        {
            FontGlyph expected = first.GetGlyph(glyphId);
            FontGlyph actual = second.GetGlyph(glyphId);
            Assert.Equal(expected.Bounds.XMin, actual.Bounds.XMin);
            Assert.Equal(expected.Bounds.YMin, actual.Bounds.YMin);
            Assert.Equal(expected.Bounds.XMax, actual.Bounds.XMax);
            Assert.Equal(expected.Bounds.YMax, actual.Bounds.YMax);
            Assert.Equal(expected.Outline.Contours.Length, actual.Outline.Contours.Length);
            for (
                int contourIndex = 0;
                contourIndex < expected.Outline.Contours.Length;
                contourIndex++
            )
            {
                OutlineContour expectedContour = expected.Outline.Contours.Span[contourIndex];
                OutlineContour actualContour = actual.Outline.Contours.Span[contourIndex];
                Assert.Equal(expectedContour.Start, actualContour.Start);
                Assert.Equal(expectedContour.Segments.Length, actualContour.Segments.Length);
                for (
                    int segmentIndex = 0;
                    segmentIndex < expectedContour.Segments.Length;
                    segmentIndex++
                )
                {
                    OutlineSegment expectedSegment = expectedContour.Segments.Span[segmentIndex];
                    OutlineSegment actualSegment = actualContour.Segments.Span[segmentIndex];
                    Assert.Equal(expectedSegment.Kind, actualSegment.Kind);
                    Assert.Equal(expectedSegment.Control1, actualSegment.Control1);
                    Assert.Equal(expectedSegment.Control2, actualSegment.Control2);
                    Assert.Equal(expectedSegment.End, actualSegment.End);
                }
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ParsesAFaceWithACorruptUnusedCharString()
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(Concat(RMoveTo(0, 0), RLineTo(10, 0), EndChar()));
        cff.AddGlyph(Bytes(15));

        SfntFace face = SfntFace.Parse(cff.BuildFace((0x41, 1), (0x42, 2)));

        Assert.False(face.GetGlyph(1).IsEmpty);
        Assert.Throws<FontFormatException>(() => face.GetGlyph(2));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResolvesGlyphsConcurrently()
    {
        SyntheticCff cff = CreateSeacTable(
            Concat(RMoveTo(10, 20), RRCurveTo(100, 200, -50, -100, 20, 300), EndChar())
        );
        cff.AddGlyph(
            Concat(HStemHm(0, 20), HintMask([0x01]), RMoveTo(0, 0), RLineTo(50, 0), EndChar())
        );
        cff.WithCharset(34, 35, 36, 37);
        SfntFace face = SfntFace.Parse(cff.BuildFace());
        FontGlyph[] expected = new FontGlyph[face.GlyphCount];
        for (int glyphId = 0; glyphId < face.GlyphCount; glyphId++)
        {
            expected[glyphId] = face.GetGlyph(glyphId);
        }

        Parallel.For(0, 8, worker =>
        {
            for (int iteration = 0; iteration < 64; iteration++)
            {
                for (int glyphId = 0; glyphId < expected.Length; glyphId++)
                {
                    AssertSameGlyph(expected[glyphId], face.GetGlyph(glyphId));
                }
            }
        });
    }

    [Theory]
    [InlineData(new byte[] { 247 })]
    [InlineData(new byte[] { 28, 1 })]
    [InlineData(new byte[] { 255, 0, 0, 1 })]
    [InlineData(new byte[] { 12 })]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsATruncatedOperand(byte[] charString)
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(charString)
        );
        Assert.Contains("truncated", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnOddRLineToOperandCount()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(RMoveTo(0, 0), Number(1), Bytes(RLINETO)))
        );
        Assert.Contains("odd", exception.Reason, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RejectsAnInvalidRRCurveToOperandCount()
    {
        FontFormatException exception = Assert.Throws<FontFormatException>(() =>
            ResolveWith(Concat(RMoveTo(0, 0), Numbers(1, 2, 3, 4, 5), Bytes(RRCURVETO)))
        );
        Assert.Contains("multiple of 6", exception.Reason, StringComparison.Ordinal);
    }

    private static FontGlyph Resolve(byte[] charString) => ResolveWith(charString);

    private static FontGlyph ResolveWith(byte[] charString, int unitsPerEm = 1000) =>
        Resolve(CreateGlyphTable(charString), 1, unitsPerEm);

    private static FontGlyph Resolve(SyntheticCff cff, int glyphId, int unitsPerEm = 1000) =>
        SfntFace.Parse(cff.BuildFace(unitsPerEm)).GetGlyph(glyphId);

    private static SyntheticCff CreateGlyphTable(byte[] charString)
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(charString);
        return cff;
    }

    private static SyntheticCff CreateSeacTable(byte[] compositeCharString) =>
        CreateSeacTable(
            Concat(RMoveTo(10, 20), RLineTo(30, 0), EndChar()),
            Concat(RMoveTo(1, 2), RLineTo(5, 0), EndChar()),
            compositeCharString
        );

    private static SyntheticCff CreateSeacTable(
        byte[] baseCharString,
        byte[] accentCharString,
        byte[] compositeCharString
    )
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AddGlyph(baseCharString);
        cff.AddGlyph(accentCharString);
        cff.AddGlyph(compositeCharString);
        cff.WithCharset(34, 35, 36);
        return cff;
    }

    private static byte[] BuildTokenCharString(int hintMaskCount)
    {
        byte[] charString = new byte[hintMaskCount + 1];
        charString.AsSpan(0, hintMaskCount).Fill(HINTMASK);
        charString[^1] = ENDCHAR;
        return charString;
    }

    private static List<byte[]> StemParts(int hintCount)
    {
        List<byte[]> parts = [];
        int remaining = hintCount;
        while (remaining > 0)
        {
            int take = Math.Min(remaining, 24);
            int[] arguments = new int[take * 2];
            for (int index = 0; index < take; index++)
            {
                arguments[index * 2] = 0;
                arguments[index * 2 + 1] = 10;
            }

            parts.Add(HStem(arguments));
            remaining -= take;
        }

        return parts;
    }

    private static void AssertSameGlyph(FontGlyph expected, FontGlyph actual)
    {
        Assert.Equal(expected.Bounds.XMin, actual.Bounds.XMin);
        Assert.Equal(expected.Bounds.YMin, actual.Bounds.YMin);
        Assert.Equal(expected.Bounds.XMax, actual.Bounds.XMax);
        Assert.Equal(expected.Bounds.YMax, actual.Bounds.YMax);
        Assert.Equal(expected.Outline.Contours.Length, actual.Outline.Contours.Length);
        for (int contourIndex = 0; contourIndex < expected.Outline.Contours.Length; contourIndex++)
        {
            OutlineContour expectedContour = expected.Outline.Contours.Span[contourIndex];
            OutlineContour actualContour = actual.Outline.Contours.Span[contourIndex];
            Assert.Equal(expectedContour.Start, actualContour.Start);
            Assert.Equal(expectedContour.Segments.Length, actualContour.Segments.Length);
            for (
                int segmentIndex = 0;
                segmentIndex < expectedContour.Segments.Length;
                segmentIndex++
            )
            {
                OutlineSegment expectedSegment = expectedContour.Segments.Span[segmentIndex];
                OutlineSegment actualSegment = actualContour.Segments.Span[segmentIndex];
                Assert.Equal(expectedSegment.Kind, actualSegment.Kind);
                Assert.Equal(expectedSegment.Control1, actualSegment.Control1);
                Assert.Equal(expectedSegment.Control2, actualSegment.Control2);
                Assert.Equal(expectedSegment.End, actualSegment.End);
            }
        }
    }

    private static SyntheticCff CreateCidTable(bool withFdSelectFormat0, bool rangeFormat)
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        cff.AsCid();
        cff.AddFontDict(Concat(RMoveTo(10, 10), RLineTo(20, 0), Return()));
        cff.AddFontDict(Concat(RMoveTo(300, 40), RLineTo(10, 0), Return()));
        if (withFdSelectFormat0)
        {
            cff.WithFdSelectFormat0(0, 0, 1);
        }
        else if (rangeFormat)
        {
            cff.WithFdSelectFormat3((0, 0), (2, 1));
        }

        cff.AddGlyph(Concat(CallSubr(-107), EndChar()));
        cff.AddGlyph(Concat(CallSubr(-107), EndChar()));
        return cff;
    }

    private static FontGlyph ResolveSubroutineChain(int subroutineCount)
    {
        SyntheticCff cff = SyntheticCff.Create();
        cff.AddGlyph(EndChar());
        byte[][] subrs = new byte[subroutineCount][];
        for (int index = 0; index < subroutineCount; index++)
        {
            subrs[index] =
                index == subroutineCount - 1
                    ? Concat(RMoveTo(5, 6), RLineTo(10, 0), Return())
                    : Concat(CallSubr((index + 1) - 107), Return());
        }

        cff.WithLocalSubrs(subrs);
        int glyphId = cff.AddGlyph(Concat(CallSubr(-107), EndChar()));
        return SfntFace.Parse(cff.BuildFace()).GetGlyph(glyphId);
    }

    private static OutlineContour SingleContour(FontGlyph glyph)
    {
        Assert.Equal(1, glyph.Outline.Contours.Length);
        return glyph.Outline.Contours.Span[0];
    }

    private static void AssertPoint(int x, int y, Vector2 point) =>
        Assert.Equal(new Vector2(x, y), point);

    private static void AssertPoint(float x, float y, Vector2 point) =>
        Assert.Equal(new Vector2(x, y), point);

    private static void AssertLine(int expectedX, int expectedY, OutlineSegment segment)
    {
        Assert.Equal(OutlineSegmentKind.Line, segment.Kind);
        Assert.Equal(new Vector2(expectedX, expectedY), segment.End);
    }

    private static void AssertContains(FontBounds bounds, Vector2 point)
    {
        Assert.InRange(point.X, (float)bounds.XMin, (float)bounds.XMax);
        Assert.InRange(point.Y, (float)bounds.YMin, (float)bounds.YMax);
    }

    private static Vector2 CubicPoint(Vector2 start, OutlineSegment segment, float t)
    {
        float inverse = 1 - t;
        float a = inverse * inverse * inverse;
        float b = 3 * inverse * inverse * t;
        float c = 3 * inverse * t * t;
        float d = t * t * t;
        return (a * start) + (b * segment.Control1) + (c * segment.Control2) + (d * segment.End);
    }
}
