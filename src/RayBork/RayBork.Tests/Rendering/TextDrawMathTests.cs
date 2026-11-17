using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the pure text draw matrix helpers the camera uses to place glyph quads.
/// </summary>
public sealed class TextDrawMathTests
{
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateGlyphQuadMapsTheUnitQuadToTheLayoutRect()
    {
        Vector2 position = new(2f, 3f);
        Vector2 size = new(4f, 5f);

        Matrix4x4 matrix = TextDrawMath.CreateGlyphQuad(position, size);

        AssertVector(position, Vector3.Transform(new Vector3(-0.5f, -0.5f, 0f), matrix));
        AssertVector(
            position + new Vector2(size.X, 0f),
            Vector3.Transform(new Vector3(0.5f, -0.5f, 0f), matrix)
        );
        AssertVector(position + size, Vector3.Transform(new Vector3(0.5f, 0.5f, 0f), matrix));
        AssertVector(
            position + new Vector2(0f, size.Y),
            Vector3.Transform(new Vector3(-0.5f, 0.5f, 0f), matrix)
        );
        AssertVector(Vector3.UnitZ, Vector3.TransformNormal(Vector3.UnitZ, matrix));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateCenteredBlockOffsetMovesTheBoundsCenterToTheOrigin()
    {
        Vector2 boundsMin = new(-2f, -5f);
        Vector2 boundsMax = new(4f, 1f);

        Matrix4x4 offset = TextDrawMath.CreateCenteredBlockOffset(boundsMin, boundsMax);

        AssertVector(
            Vector3.Zero,
            Vector3.Transform(new Vector3((boundsMin + boundsMax) * 0.5f, 0f), offset)
        );
        Vector2 halfSize = (boundsMax - boundsMin) * 0.5f;
        AssertVector(
            new Vector3(-halfSize.X, -halfSize.Y, 0f),
            Vector3.Transform(new Vector3(boundsMin, 0f), offset)
        );
        AssertVector(
            new Vector3(halfSize.X, halfSize.Y, 0f),
            Vector3.Transform(new Vector3(boundsMax, 0f), offset)
        );
    }

    private static void AssertVector(Vector2 expected, Vector3 actual)
    {
        AssertVector(new Vector3(expected, 0f), actual);
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, TOLERANCE);
        Assert.Equal(expected.Y, actual.Y, TOLERANCE);
        Assert.Equal(expected.Z, actual.Z, TOLERANCE);
    }
}
