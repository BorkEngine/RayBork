using System.Numerics;
using RayBork;

namespace RayBork.Tests;

public sealed class Vector3ExtensionTests {
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MagnitudeAndSqrMagnitudeMeasureLength() {
        Vector3 vector = new(3f, 4f, 0f);

        Assert.Equal(5f, vector.Magnitude);
        Assert.Equal(25f, vector.SqrMagnitude);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizedHasUnitLength() {
        Vector3 normalized = new Vector3(3f, 4f, 0f).Normalized;

        AssertVectorEqual(new Vector3(0.6f, 0.8f, 0f), normalized);
        Assert.Equal(1f, normalized.Magnitude, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithComponentsAndXzReplaceSingleAxes() {
        Vector3 vector = new(1f, 2f, 3f);

        Assert.Equal(new Vector3(9f, 2f, 3f), vector.WithX(9f));
        Assert.Equal(new Vector3(1f, 9f, 3f), vector.WithY(9f));
        Assert.Equal(new Vector3(1f, 2f, 9f), vector.WithZ(9f));
        Assert.Equal(new Vector3(1f, 0f, 3f), vector.Xz);
    }

    [Theory]
    [InlineData(1f, 0f, 0f, 1f, 0f, 0f, 0f)]
    [InlineData(1f, 0f, 0f, 0f, 1f, 0f, 90f)]
    [InlineData(-1f, 0f, 0f, 1f, 0f, 0f, 180f)]
    [InlineData(1f, 1f, 0f, 1f, 0f, 0f, 45f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AngleReturnsDegrees(
        float ax,
        float ay,
        float az,
        float bx,
        float by,
        float bz,
        float expectedDegrees) {
        float angle = new Vector3(ax, ay, az).Angle(new Vector3(bx, by, bz));

        Assert.Equal(expectedDegrees, angle, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AngleReturnsZeroForAZeroVector() {
        Assert.Equal(0f, Vector3.Zero.Angle(Vector3.UnitX));
        Assert.Equal(0f, Vector3.UnitX.Angle(Vector3.Zero));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SignedAngleUsesTheAxisDirection() {
        float positive = Vector3.UnitX.SignedAngle(Vector3.UnitY, Vector3.UnitZ);
        float negative = Vector3.UnitX.SignedAngle(Vector3.UnitY, -Vector3.UnitZ);

        Assert.Equal(90f, positive, TOLERANCE);
        Assert.Equal(-90f, negative, TOLERANCE);
        Assert.Equal(0f, Vector3.UnitX.SignedAngle(Vector3.UnitX, Vector3.UnitZ), TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProjectProjectsOntoTheNormal() {
        AssertVectorEqual(new Vector3(0f, 2f, 0f), new Vector3(1f, 2f, 3f).Project(Vector3.UnitY));
        AssertVectorEqual(new Vector3(1f, 0f, 0f), new Vector3(1f, 2f, 3f).Project(new Vector3(2f, 0f, 0f)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProjectOnPlaneRemovesTheNormalComponent() {
        AssertVectorEqual(new Vector3(1f, 0f, 3f), new Vector3(1f, 2f, 3f).ProjectOnPlane(Vector3.UnitY));
        AssertVectorEqual(Vector3.Zero, Vector3.UnitX.ProjectOnPlane(Vector3.UnitX));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ProjectAndReflectThrowForAZeroNormal() {
        Assert.Throws<ArgumentException>(() => Vector3.UnitX.Project(Vector3.Zero));
        Assert.Throws<ArgumentException>(() => Vector3.UnitX.ProjectOnPlane(Vector3.Zero));
        Assert.Throws<ArgumentException>(() => Vector3.UnitX.Reflect(Vector3.Zero));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReflectMirrorsAcrossThePlane() {
        AssertVectorEqual(new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f).Reflect(Vector3.UnitY));
        AssertVectorEqual(new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f).Reflect(Vector3.UnitX));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ClampMagnitudeLimitsLength() {
        Vector3 clamped = new Vector3(3f, 4f, 0f).ClampMagnitude(2f);

        AssertVectorEqual(new Vector3(1.2f, 1.6f, 0f), clamped);
        Assert.Equal(2f, clamped.Magnitude, TOLERANCE);
        Assert.Equal(new Vector3(3f, 4f, 0f), new Vector3(3f, 4f, 0f).ClampMagnitude(10f));
        Assert.Equal(Vector3.Zero, new Vector3(3f, 4f, 0f).ClampMagnitude(0f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RotatedUsesTheQuaternion() {
        Quaternion quarterTurn = Quaternion.Euler(0f, 90f, 0f);
        Vector3 vector = new(1f, 2f, 3f);

        AssertVectorEqual(new Vector3(0f, 0f, -1f), Vector3.UnitX.Rotated(quarterTurn));
        AssertVectorEqual(Vector3.Transform(vector, quarterTurn), vector.Rotated(quarterTurn));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StaticDirectionsFollowTheWorldConvention() {
        Assert.Equal(new Vector3(0f, 1f, 0f), Vector3.Up);
        Assert.Equal(new Vector3(0f, -1f, 0f), Vector3.Down);
        Assert.Equal(new Vector3(-1f, 0f, 0f), Vector3.Left);
        Assert.Equal(new Vector3(1f, 0f, 0f), Vector3.Right);
        Assert.Equal(new Vector3(0f, 0f, -1f), Vector3.Forward);
        Assert.Equal(new Vector3(0f, 0f, 1f), Vector3.Back);
    }

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }
}
