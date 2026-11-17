using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class BoundingFrustumTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ContainsAcceptsAPointInFrontAndRejectsBehindAndOffscreenPoints()
    {
        BoundingFrustum frustum = CreateFrustum();

        Assert.True(frustum.Contains(Vector3.Zero));
        Assert.True(frustum.Contains(new Vector3(0f, 1f, -5f)));
        Assert.False(frustum.Contains(new Vector3(0f, 0f, 10f)));
        Assert.False(frustum.Contains(new Vector3(40f, 0f, -5f)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PlaneNamesFollowTheYFlippedNdc()
    {
        BoundingFrustum frustum = CreateFrustum();

        // A point above the camera axis is inside both the Top and Bottom planes.
        Assert.True(Plane.DotCoordinate(frustum.Top, new Vector3(0f, 1f, -5f)) > 0f);
        Assert.True(Plane.DotCoordinate(frustum.Bottom, new Vector3(0f, 1f, -5f)) > 0f);
        Assert.True(Plane.DotCoordinate(frustum.Top, new Vector3(0f, -1f, -5f)) > 0f);
        Assert.False(Plane.DotCoordinate(frustum.Right, new Vector3(40f, 0f, -5f)) > 0f);
        Assert.False(Plane.DotCoordinate(frustum.Left, new Vector3(-40f, 0f, -5f)) > 0f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntersectsAcceptsOverlappingAndContainingBounds()
    {
        BoundingFrustum frustum = CreateFrustum();

        Assert.True(frustum.Intersects(new BoundingBox(new Vector3(-1f), new Vector3(1f))));
        Assert.True(
            frustum.Intersects(
                new BoundingBox(new Vector3(-100f, -100f, -20f), new Vector3(100f, 100f, -4f))
            )
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntersectsRejectsBoundsFullyOutsideOnePlane()
    {
        BoundingFrustum frustum = CreateFrustum();

        // Behind the camera (the camera sits at z = 5 and looks toward -Z).
        Assert.False(
            frustum.Intersects(new BoundingBox(new Vector3(-1f, -1f, 6f), new Vector3(1f, 1f, 7f)))
        );
        // Far to the right.
        Assert.False(
            frustum.Intersects(
                new BoundingBox(new Vector3(50f, -1f, -6f), new Vector3(60f, 1f, -4f))
            )
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntersectsAcceptsABoxExactlyOnAPlane()
    {
        BoundingFrustum frustum = CreateAxisAlignedFrustum();

        // The box minimum is exactly on the right plane at x = 2.
        Assert.True(
            frustum.Intersects(new BoundingBox(new Vector3(2f, -1f, -6f), new Vector3(3f, 1f, -4f)))
        );
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntersectsToleratesAFloatNudgeOutsideAPlaneButRejectsPastTheTolerance()
    {
        BoundingFrustum frustum = CreateAxisAlignedFrustum();

        // A 0.00005 nudge past the right plane is inside the tolerance and keeps the box visible.
        Assert.True(
            frustum.Intersects(
                new BoundingBox(new Vector3(2.00005f, -1f, -6f), new Vector3(3.00005f, 1f, -4f))
            )
        );
        // A 0.0002 nudge is outside the tolerance and is culled.
        Assert.False(
            frustum.Intersects(
                new BoundingBox(new Vector3(2.0002f, -1f, -6f), new Vector3(3.0002f, 1f, -4f))
            )
        );
        // Contains keeps its exact non-negative test and does not use the tolerance.
        Assert.False(frustum.Contains(new Vector3(2.00005f, 0f, -5f)));
    }

    private static BoundingFrustum CreateAxisAlignedFrustum()
    {
        Matrix4x4 view = CameraMath.CreateViewMatrix(Transform.Identity);
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(4f, 1f, 0.1f, 10f);
        return new BoundingFrustum(view * projection);
    }

    private static BoundingFrustum CreateFrustum()
    {
        Transform cameraTransform = new(new Vector3(0f, 0f, 5f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(cameraTransform);
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(
            60f,
            16f / 9f,
            0.1f,
            float.PositiveInfinity
        );
        return new BoundingFrustum(view * projection);
    }
}
