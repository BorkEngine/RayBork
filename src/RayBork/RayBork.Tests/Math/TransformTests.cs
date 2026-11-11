using System.Numerics;

namespace RayBork.Tests;

public sealed class TransformTests {
    private const float TOLERANCE = 1e-4f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IdentityHasNoTranslationRotationOrScale() {
        Transform identity = Transform.Identity;

        Assert.Equal(Vector3.Zero, identity.Position);
        Assert.Equal(Quaternion.Identity, identity.Rotation);
        Assert.Equal(Vector3.One, identity.Scale);
        Assert.Equal(Matrix4x4.Identity, identity.ToMatrix());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorStoresComponentsAndNormalizesRotation() {
        Transform transform = new(
            new Vector3(1f, 2f, 3f),
            new Quaternion(0f, 0f, 0f, 4f),
            new Vector3(2f, 3f, 4f));

        Assert.Equal(new Vector3(1f, 2f, 3f), transform.Position);
        AssertRotationEqual(Quaternion.Identity, transform.Rotation);
        Assert.Equal(new Vector3(2f, 3f, 4f), transform.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorTreatsZeroLengthQuaternionAsIdentity() {
        Transform transform = new(new Vector3(1f, 2f, 3f), default, Vector3.One);

        Assert.Equal(Quaternion.Identity, transform.Rotation);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PositionConstructorUsesIdentityRotationAndUnitScale() {
        Transform transform = new(new Vector3(1f, 2f, 3f));

        Assert.Equal(new Vector3(1f, 2f, 3f), transform.Position);
        Assert.Equal(Quaternion.Identity, transform.Rotation);
        Assert.Equal(Vector3.One, transform.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ToMatrixMatchesScaleRotationTranslationOrder() {
        Transform transform = new(
            new Vector3(1f, 2f, 3f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(2f, 3f, 4f));

        Matrix4x4 expected =
            Matrix4x4.CreateScale(transform.Scale)
            * Matrix4x4.CreateFromQuaternion(transform.Rotation)
            * Matrix4x4.CreateTranslation(transform.Position);

        Assert.Equal(expected, transform.ToMatrix());

        Vector3 point = new(1f, 2f, 3f);
        AssertVectorEqual(Vector3.Transform(point, transform.ToMatrix()), transform.TransformPoint(point));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TransformPointAppliesScaleRotationAndTranslation() {
        Transform transform = new(
            new Vector3(1f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(2f, 2f, 2f));

        Vector3 transformed = transform.TransformPoint(new Vector3(0f, 0f, -1f));

        AssertVectorEqual(new Vector3(-1f, 0f, 0f), transformed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TransformDirectionAppliesRotationOnly() {
        Transform transform = new(
            new Vector3(5f, 5f, 5f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(10f, 10f, 10f));

        Vector3 transformed = transform.TransformDirection(new Vector3(0f, 0f, -1f));

        AssertVectorEqual(new Vector3(-1f, 0f, 0f), transformed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BasisVectorsFollowTheWorldConvention() {
        Transform identity = Transform.Identity;

        AssertVectorEqual(new Vector3(0f, 0f, -1f), identity.Forward);
        AssertVectorEqual(new Vector3(1f, 0f, 0f), identity.Right);
        AssertVectorEqual(new Vector3(0f, 1f, 0f), identity.Up);

        Transform rotated = identity.WithRotation(Quaternion.Euler(0f, 90f, 0f));

        AssertVectorEqual(new Vector3(-1f, 0f, 0f), rotated.Forward);
        AssertVectorEqual(new Vector3(0f, 0f, -1f), rotated.Right);
        AssertVectorEqual(new Vector3(0f, 1f, 0f), rotated.Up);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithParentComposesLocalTranslationIntoParentSpace() {
        Transform parent = new(new Vector3(0f, 1f, 0f));
        Transform local = new(new Vector3(1f, 0f, 0f));

        Transform world = local.WithParent(parent);

        AssertVectorEqual(new Vector3(1f, 1f, 0f), world.Position);
        AssertRotationEqual(Quaternion.Identity, world.Rotation);
        AssertVectorEqual(Vector3.One, world.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithParentAppliesParentRotationAndTranslation() {
        Transform parent = new(
            new Vector3(0f, 1f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            Vector3.One);
        Transform local = new(new Vector3(0f, 0f, -1f));

        Transform world = local.WithParent(parent);

        AssertVectorEqual(new Vector3(-1f, 1f, 0f), world.Position);
        AssertRotationEqual(parent.Rotation, world.Rotation);
        AssertVectorEqual(Vector3.One, world.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithParentSupportsNonUniformScaleForDecomposeFriendlyHierarchies() {
        Transform parent = new(
            new Vector3(5f, 0f, 0f),
            Quaternion.Euler(0f, 45f, 0f),
            new Vector3(2f, 3f, 4f));
        Transform local = new(new Vector3(1f, 2f, 3f));

        Transform world = local.WithParent(parent);

        Vector3 expectedPosition = Vector3.Transform(new Vector3(2f, 6f, 12f), parent.Rotation) + parent.Position;
        AssertVectorEqual(expectedPosition, world.Position);
        AssertVectorEqual(new Vector3(2f, 3f, 4f), world.Scale);
        AssertRotationEqual(parent.Rotation, world.Rotation);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithParentThrowsWhenTheCompositionContainsShear() {
        Transform parent = new(
            Vector3.Zero,
            Quaternion.Euler(0f, 45f, 0f),
            new Vector3(2f, 1f, 1f));
        Transform local = new(
            Vector3.Zero,
            Quaternion.Euler(0f, 0f, 45f),
            Vector3.One);

        Assert.Throws<InvalidOperationException>(() => local.WithParent(parent));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithParentThrowsWhenTheParentIsSingular() {
        Transform parent = Transform.Identity.WithScale(new Vector3(1f, 0f, 1f));
        Transform local = new(new Vector3(1f, 2f, 3f));

        Assert.Throws<InvalidOperationException>(() => local.WithParent(parent));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InverseRoundTripsPointsAndDirections() {
        Transform transform = new(
            new Vector3(3f, -2f, 5f),
            Quaternion.Euler(20f, 45f, -10f),
            new Vector3(2f, 2f, 2f));

        Transform inverse = transform.Inverse();
        Vector3 point = new(1f, 2f, 3f);
        Vector3 direction = Vector3.Normalize(new Vector3(1f, 2f, 3f));

        AssertVectorEqual(point, inverse.TransformPoint(transform.TransformPoint(point)));
        AssertVectorEqual(direction, inverse.TransformDirection(transform.TransformDirection(direction)));
        AssertVectorEqual(new Vector3(0.5f, 0.5f, 0.5f), inverse.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InverseThrowsForASingularScale() {
        Transform transform = new(
            new Vector3(1f, 1f, 1f),
            Quaternion.Euler(0f, 45f, 0f),
            new Vector3(1f, 0f, 1f));

        Assert.Throws<InvalidOperationException>(() => transform.Inverse());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WithMethodsReturnUpdatedCopiesAndPreserveTheSource() {
        Transform original = new(
            new Vector3(1f, 2f, 3f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(2f, 2f, 2f));

        Transform positioned = original.WithPosition(new Vector3(4f, 5f, 6f));
        Transform scaled = original.WithScale(new Vector3(1f, 2f, 3f));
        Transform rotated = original.WithRotation(new Quaternion(0f, 0f, 0f, 3f));

        AssertVectorEqual(new Vector3(4f, 5f, 6f), positioned.Position);
        Assert.Equal(original.Rotation, positioned.Rotation);
        Assert.Equal(original.Scale, positioned.Scale);

        AssertVectorEqual(new Vector3(1f, 2f, 3f), scaled.Position);
        Assert.Equal(original.Rotation, scaled.Rotation);
        AssertVectorEqual(new Vector3(1f, 2f, 3f), scaled.Scale);

        AssertRotationEqual(Quaternion.Identity, rotated.Rotation);
        AssertVectorEqual(original.Position, rotated.Position);
        Assert.Equal(original.Scale, rotated.Scale);

        AssertVectorEqual(new Vector3(1f, 2f, 3f), original.Position);
        AssertRotationEqual(Quaternion.Euler(0f, 90f, 0f), original.Rotation);
        AssertVectorEqual(new Vector3(2f, 2f, 2f), original.Scale);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LerpInterpolatesAndClampsTheFactor() {
        Transform a = new(Vector3.Zero, Quaternion.Identity, Vector3.One);
        Transform b = new(
            new Vector3(10f, 0f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            new Vector3(3f, 3f, 3f));

        Transform middle = Transform.Lerp(a, b, 0.5f);
        AssertVectorEqual(new Vector3(5f, 0f, 0f), middle.Position);
        AssertVectorEqual(new Vector3(2f, 2f, 2f), middle.Scale);
        AssertVectorEqual(new Vector3(-0.70710677f, 0f, -0.70710677f), middle.Forward);

        Transform before = Transform.Lerp(a, b, -2f);
        AssertVectorEqual(a.Position, before.Position);
        AssertRotationEqual(a.Rotation, before.Rotation);

        Transform after = Transform.Lerp(a, b, 2f);
        AssertVectorEqual(b.Position, after.Position);
        AssertRotationEqual(b.Rotation, after.Rotation);
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
