using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Fonts.Msdf;

/// <summary>
/// Tests the deterministic MSDF edge colouring: every flattened edge carries exactly two channels,
/// consecutive edges of a contour differ and share exactly one channel, the closing edge is fixed
/// when the cycle would repeat the first pair, and a degenerate single-edge contour still flattens.
/// </summary>
public sealed class MsdfEdgeColoringTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryEdgeCarriesExactlyTwoChannels() {
        MsdfEdge[] edges = Flatten(MsdfTestShapes.Polygon(7, 500f, 500f, 400f));

        Assert.Equal(7, edges.Length);
        foreach (MsdfEdge edge in edges) {
            Assert.Equal(2, BitOperations.PopCount((uint)edge.ColorMask));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConsecutiveAndClosingEdgesDifferAndShareExactlyOneChannel(int sides) {
        MsdfEdge[] edges = Flatten(MsdfTestShapes.Polygon(sides, 500f, 500f, 400f));

        Assert.Equal(sides, edges.Length);
        for (int index = 0; index < edges.Length; index++) {
            int current = edges[index].ColorMask;
            int next = edges[(index + 1) % edges.Length].ColorMask;
            Assert.NotEqual(current, next);
            Assert.Equal(1, BitOperations.PopCount((uint)(current & next)));
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AContourWhoseCycleWouldRepeatTheFirstPairGetsAFixedClosingEdge() {
        // A four-edge contour cycles yellow, cyan, magenta, and then yellow again; the closing edge
        // must take cyan instead so that it differs from both its predecessor and the first edge.
        MsdfEdge[] edges = Flatten(MsdfTestShapes.Polygon(4, 500f, 500f, 400f));

        Assert.Equal(MsdfOutlineFlattener.YELLOW, edges[0].ColorMask);
        Assert.Equal(MsdfOutlineFlattener.CYAN, edges[3].ColorMask);
        Assert.NotEqual(edges[3].ColorMask, edges[0].ColorMask);
        Assert.NotEqual(edges[3].ColorMask, edges[2].ColorMask);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACurvedContourFlattensToColouredLines() {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        OutlinePath outline = MsdfTestShapes.QuadraticDome(100f, 100f, 500f, 900f, 900f, 100f);

        int count = MsdfOutlineFlattener.CountEdges(outline, layout);
        MsdfEdge[] edges = new MsdfEdge[count];
        MsdfOutlineFlattener.Flatten(outline, layout, edges);

        // The quadratic is subdivided into at least two chords; its straight closing line stays one.
        Assert.True(count > 2);
        for (int index = 0; index < edges.Length; index++) {
            MsdfEdge current = edges[index];
            MsdfEdge next = edges[(index + 1) % edges.Length];
            Assert.Equal(2, BitOperations.PopCount((uint)current.ColorMask));
            Assert.Equal(1, BitOperations.PopCount((uint)(current.ColorMask & next.ColorMask)));
        }

        Assert.Equal(edges[0].Start, edges[^1].End);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASingleEdgeContourKeepsTheFirstColourPair() {
        OutlinePath outline = MsdfTestShapes.CreatePath(
            new OutlineContour(
                Vector2.Zero,
                new OutlineSegment[] { OutlineSegment.Line(Vector2.Zero) }));

        MsdfEdge[] edges = Flatten(outline);

        MsdfEdge edge = Assert.Single(edges);
        Assert.Equal(MsdfOutlineFlattener.YELLOW, edge.ColorMask);
    }

    private static MsdfEdge[] Flatten(OutlinePath outline) {
        MsdfGlyphLayout layout = MsdfGenerator.ComputeLayout(
            new FontBounds(0, 0, 1000, 1000),
            unitsPerEm: 1000);
        int count = MsdfOutlineFlattener.CountEdges(outline, layout);
        MsdfEdge[] edges = new MsdfEdge[count];
        MsdfOutlineFlattener.Flatten(outline, layout, edges);
        return edges;
    }
}
