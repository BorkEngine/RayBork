using System.Numerics;
using RayBork;

namespace RayBork.Tests;

public sealed class QuaternionExtensionTests {
    private const float TOLERANCE = 1e-4f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EulerUsesTheRightHandedYUpConvention() {
        AssertRotationEqual(Quaternion.Identity, Quaternion.Euler(0f, 0f, 0f));
        AssertVectorEqual(
            new Vector3(-1f, 0f, 0f),
            Vector3.Transform(-Vector3.UnitZ, Quaternion.Euler(0f, 90f, 0f)));
        AssertVectorEqual(
            new Vector3(0f, 0f, 1f),
            Vector3.Transform(Vector3.UnitY, Quaternion.Euler(90f, 0f, 0f)));
        AssertVectorEqual(
            new Vector3(0f, 1f, 0f),
            Vector3.Transform(Vector3.UnitX, Quaternion.Euler(0f, 0f, 90f)));
    }

    [Theory]
    [InlineData(0f, 0f, 0f)]
    [InlineData(30f, 0f, 0f)]
    [InlineData(0f, 45f, 0f)]
    [InlineData(0f, 0f, 60f)]
    [InlineData(20f, 40f, 10f)]
    [InlineData(0f, 90f, 0f)]
    [InlineData(-30f, 120f, 45f)]
    [InlineData(90f, 0f, 0f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EulerAnglesRoundTripSimpleRotations(float x, float y, float z) {
        Quaternion original = Quaternion.Euler(x, y, z);

        Vector3 angles = original.EulerAngles;
        Quaternion roundTrip = Quaternion.Euler(angles.X, angles.Y, angles.Z);

        AssertRotationEqual(original, roundTrip);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EulerAnglesReturnDegreesInXyzOrder() {
        AssertVectorEqual(new Vector3(30f, 60f, 20f), Quaternion.Euler(30f, 60f, 20f).EulerAngles);
        AssertVectorEqual(new Vector3(0f, 90f, 0f), Quaternion.Euler(0f, 90f, 0f).EulerAngles);
        AssertVectorEqual(new Vector3(0f, 270f, 0f), Quaternion.Euler(0f, -90f, 0f).EulerAngles);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AngleAxisMatchesEulerAndNormalizesItsAxis() {
        Quaternion expected = Quaternion.Euler(0f, 90f, 0f);
        Quaternion rotation = Quaternion.AngleAxis(90f, Vector3.UnitY);
        Quaternion scaledAxis = Quaternion.AngleAxis(90f, new Vector3(0f, 5f, 0f));

        AssertRotationEqual(expected, rotation);
        AssertRotationEqual(expected, scaledAxis);
        AssertVectorEqual(new Vector3(0f, 0f, -1f), Vector3.Transform(Vector3.UnitX, rotation));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AngleAxisThrowsForAZeroAxis() {
        Assert.Throws<ArgumentException>(() => Quaternion.AngleAxis(90f, Vector3.Zero));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LookRotationAlignsForwardAndUp() {
        AssertRotationEqual(
            Quaternion.Identity,
            Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.UnitY));

        Quaternion lookRight = Quaternion.LookRotation(Vector3.UnitX, Vector3.UnitY);
        AssertVectorEqual(Vector3.UnitX, Vector3.Transform(-Vector3.UnitZ, lookRight));
        AssertVectorEqual(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, lookRight));
        AssertVectorEqual(Vector3.UnitZ, Vector3.Transform(Vector3.UnitX, lookRight));

        Quaternion lookUp = Quaternion.LookRotation(Vector3.UnitY, Vector3.UnitZ);
        AssertVectorEqual(Vector3.UnitY, Vector3.Transform(-Vector3.UnitZ, lookUp));
        AssertVectorEqual(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, lookUp));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LookRotationThrowsForDegenerateInputs() {
        Assert.Throws<ArgumentException>(() => Quaternion.LookRotation(Vector3.Zero, Vector3.UnitY));
        Assert.Throws<ArgumentException>(() => Quaternion.LookRotation(Vector3.UnitZ, Vector3.Zero));
        Assert.Throws<ArgumentException>(() => Quaternion.LookRotation(Vector3.UnitY, Vector3.UnitY));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromToRotationMapsFromOntoTo() {
        Quaternion xToY = Quaternion.FromToRotation(Vector3.UnitX, Vector3.UnitY);
        AssertVectorEqual(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, xToY));
        Assert.Equal(90f, xToY.Angle(Quaternion.Identity), TOLERANCE);

        Quaternion zToX = Quaternion.FromToRotation(new Vector3(0f, 0f, 2f), new Vector3(5f, 0f, 0f));
        AssertVectorEqual(Vector3.UnitX, Vector3.Transform(Vector3.UnitZ, zToX));

        AssertRotationEqual(Quaternion.Identity, Quaternion.FromToRotation(Vector3.UnitX, new Vector3(5f, 0f, 0f)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromToRotationHandlesOpposingDirections() {
        Quaternion opposite = Quaternion.FromToRotation(Vector3.UnitZ, -Vector3.UnitZ);

        Assert.Equal(180f, opposite.Angle(Quaternion.Identity), 1e-3f);
        AssertVectorEqual(-Vector3.UnitZ, Vector3.Transform(Vector3.UnitZ, opposite));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromToRotationThrowsForZeroVectors() {
        Assert.Throws<ArgumentException>(() => Quaternion.FromToRotation(Vector3.Zero, Vector3.UnitX));
        Assert.Throws<ArgumentException>(() => Quaternion.FromToRotation(Vector3.UnitX, Vector3.Zero));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NormalizedProducesUnitRotations() {
        Quaternion normalized = new Quaternion(1f, 2f, 3f, 4f).Normalized;

        Assert.Equal(1f, normalized.Length(), TOLERANCE);
        AssertRotationEqual(Quaternion.Normalize(new Quaternion(1f, 2f, 3f, 4f)), normalized);
        AssertRotationEqual(Quaternion.Identity, Quaternion.Multiply(normalized, Quaternion.Inverse(normalized)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AngleReturnsDegrees() {
        Assert.Equal(90f, Quaternion.Identity.Angle(Quaternion.Euler(0f, 90f, 0f)), TOLERANCE);
        Assert.Equal(180f, Quaternion.Identity.Angle(Quaternion.AngleAxis(180f, Vector3.UnitX)), TOLERANCE);
        Assert.Equal(0f, Quaternion.Identity.Angle(Quaternion.Negate(Quaternion.Identity)), TOLERANCE);

        Quaternion rotation = Quaternion.Euler(10f, 20f, 30f);
        Assert.Equal(0f, rotation.Angle(rotation), TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToAngleAxisReturnsDegreesAndTheAxis() {
        Quaternion.AngleAxis(90f, Vector3.UnitY).ToAngleAxis(out float angle, out Vector3 axis);
        Assert.Equal(90f, angle, TOLERANCE);
        AssertVectorEqual(Vector3.UnitY, axis);

        Quaternion.Identity.ToAngleAxis(out float identityAngle, out Vector3 identityAxis);
        Assert.Equal(0f, identityAngle);
        AssertVectorEqual(Vector3.UnitX, identityAxis);

        Quaternion.AngleAxis(180f, Vector3.UnitX).ToAngleAxis(out float halfTurnAngle, out Vector3 halfTurnAxis);
        Assert.Equal(180f, halfTurnAngle, TOLERANCE);
        AssertVectorEqual(Vector3.UnitX, halfTurnAxis);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RotateTowardsStepsByTheMaximumDelta() {
        Quaternion start = Quaternion.Identity;
        Quaternion target = Quaternion.Euler(0f, 90f, 0f);

        Quaternion stepped = start.RotateTowards(target, 30f);
        Assert.Equal(30f, stepped.Angle(start), TOLERANCE);
        AssertRotationEqual(target, start.RotateTowards(target, 1000f));
        AssertRotationEqual(start, start.RotateTowards(target, 0f));
        AssertRotationEqual(start, start.RotateTowards(target, -10f));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SlerpInterpolatesAndClampsTheFactor() {
        Quaternion start = Quaternion.Identity;
        Quaternion target = Quaternion.Euler(0f, 90f, 0f);

        Quaternion middle = start.Slerp(target, 0.5f);
        Assert.Equal(45f, middle.Angle(start), TOLERANCE);
        AssertRotationEqual(start, start.Slerp(target, -1f));
        AssertRotationEqual(target, start.Slerp(target, 2f));
        AssertRotationEqual(middle, start.Slerp(Quaternion.Negate(target), 0.5f));
    }

    private static void AssertVectorEqual(Vector3 expected, Vector3 actual, float tolerance = TOLERANCE) {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
        Assert.Equal(expected.Z, actual.Z, tolerance);
    }

    private static void AssertRotationEqual(Quaternion expected, Quaternion actual, float tolerance = TOLERANCE) {
        float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual)));
        Assert.Equal(1f, dot, tolerance);
    }
}
