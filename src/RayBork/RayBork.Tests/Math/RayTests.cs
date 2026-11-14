using System.Numerics;

namespace RayBork.Tests;

/// <summary>
/// Tests the <see cref="Ray"/> value type's finite-origin and unit-direction contract.
/// </summary>
public sealed class RayTests {
    private const float TOLERANCE = 1e-6f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorNormalizesTheDirectionAndKeepsTheOrigin() {
        Vector3 origin = new(1f, 2f, 3f);
        Ray ray = new(origin, new Vector3(3f, 4f, 0f));

        Assert.Equal(origin, ray.Origin);
        AssertVector(new Vector3(0.6f, 0.8f, 0f), ray.Direction);
        Assert.Equal(1f, ray.Direction.Length(), TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorNormalizesADirectionWhoseLengthOverflowsInSinglePrecision() {
        Ray ray = new(Vector3.Zero, new Vector3(float.MaxValue, 0f, 0f));

        Assert.Equal(Vector3.UnitX, ray.Direction);
        Assert.Equal(1f, ray.Direction.Length(), TOLERANCE);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(float.NaN, 0f, 1f)]
    [InlineData(1f, float.PositiveInfinity, 1f)]
    [InlineData(1f, 1f, float.NegativeInfinity)]
    public void ConstructorRejectsANonFiniteOrZeroDirection(float x, float y, float z) {
        Assert.Throws<ArgumentException>(
            () => new Ray(Vector3.Zero, new Vector3(x, y, z)));
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void ConstructorRejectsANonFiniteOrigin(float x, float y, float z) {
        Assert.Throws<ArgumentException>(
            () => new Ray(new Vector3(x, y, z), Vector3.UnitZ));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithAndObjectInitializerValidateAndNormalizeTheComponents() {
        Ray ray = new(Vector3.Zero, Vector3.UnitX);

        Ray moved = ray with { Origin = new Vector3(1f, 2f, 3f) };
        Assert.Equal(new Vector3(1f, 2f, 3f), moved.Origin);
        Assert.Equal(Vector3.UnitX, moved.Direction);

        Ray turned = ray with { Direction = new Vector3(0f, -4f, 0f) };
        Assert.Equal(-Vector3.UnitY, turned.Direction);
        Assert.Equal(1f, turned.Direction.Length(), TOLERANCE);

        Ray initialized = new() {
            Origin = new Vector3(4f, 5f, 6f),
            Direction = new Vector3(0f, 0f, -8f),
        };
        Assert.Equal(new Vector3(4f, 5f, 6f), initialized.Origin);
        Assert.Equal(-Vector3.UnitZ, initialized.Direction);

        Assert.Throws<ArgumentException>(() => {
            Ray invalid = ray with { Direction = Vector3.Zero };
            _ = invalid;
        });
        Assert.Throws<ArgumentException>(() => {
            Ray invalid = ray with { Direction = new Vector3(float.NaN, 0f, 0f) };
            _ = invalid;
        });
        Assert.Throws<ArgumentException>(() => {
            Ray invalid = ray with { Origin = new Vector3(float.NaN, 0f, 0f) };
            _ = invalid;
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizedRaysCompareAndDeconstruct() {
        Ray first = new(new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, -5f));
        Ray second = new(new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, -2f));

        Assert.Equal(first, second);
        Assert.True(first == second);
        (Vector3 origin, Vector3 direction) = first;
        Assert.Equal(new Vector3(1f, 2f, 3f), origin);
        Assert.Equal(-Vector3.UnitZ, direction);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultRayHasAZeroDirection() {
        Ray ray = default;

        Assert.Equal(Vector3.Zero, ray.Origin);
        Assert.Equal(Vector3.Zero, ray.Direction);
        Assert.Equal(0f, ray.Direction.Length());
    }

    private static void AssertVector(Vector3 expected, Vector3 actual) {
        Assert.Equal(expected.X, actual.X, TOLERANCE);
        Assert.Equal(expected.Y, actual.Y, TOLERANCE);
        Assert.Equal(expected.Z, actual.Z, TOLERANCE);
    }
}
