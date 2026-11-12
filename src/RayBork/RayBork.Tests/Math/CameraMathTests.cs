using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

public sealed class CameraMathTests {
    private const float TOLERANCE = 1e-5f;
    private const float ASPECT = 16f / 9f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveMapsNearToOneAndInfinityToZero() {
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(60f, ASPECT, 0.1f, float.PositiveInfinity);

        Assert.Equal(1f, ProjectDepth(projection, -0.1f), TOLERANCE);
        Assert.Equal(0f, ProjectDepth(projection, -1_000_000f), TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveFiniteFarMapsTheFarPlaneToZero() {
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(60f, ASPECT, 0.1f, 500f);

        Assert.Equal(1f, ProjectDepth(projection, -0.1f), TOLERANCE);
        Assert.Equal(0f, ProjectDepth(projection, -500f), TOLERANCE);
        Assert.InRange(ProjectDepth(projection, -250f), 0f, 1f);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveYFlipMapsWorldUpToTheTopOfTheScreen() {
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(60f, ASPECT, 0.1f, float.PositiveInfinity);

        Vector3 below = ProjectNdc(projection, new Vector3(0f, -1f, -5f));
        Vector3 above = ProjectNdc(projection, new Vector3(0f, 1f, -5f));

        // Vulkan NDC has +Y pointing down, so a point above the camera axis projects to negative NDC Y.
        Assert.True(above.Y < 0f, $"Expected a negative NDC Y for world up, got {above.Y}.");
        Assert.True(below.Y > 0f, $"Expected a positive NDC Y for world down, got {below.Y}.");
        Assert.Equal(-above.Y, below.Y, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveUsesTheViewportAspectRatio() {
        Matrix4x4 wide = CameraMath.CreatePerspectiveProjection(60f, 2f, 0.1f, float.PositiveInfinity);
        Matrix4x4 square = CameraMath.CreatePerspectiveProjection(60f, 1f, 0.1f, float.PositiveInfinity);

        Assert.Equal(square.M11 / 2f, wide.M11, TOLERANCE);
        Assert.Equal(square.M22, wide.M22, TOLERANCE);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OrthographicMapsNearToOneFarToZeroAndUsesHeightAndAspect() {
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(4f, 2f, 0.5f, 20f);

        Assert.Equal(1f, ProjectDepth(projection, -0.5f), TOLERANCE);
        Assert.Equal(0f, ProjectDepth(projection, -20f), TOLERANCE);

        // Height 4 means y = +/-2 maps to NDC +/-1; the Y-flip inverts the sign.
        Vector3 top = ProjectNdc(projection, new Vector3(0f, 2f, -5f));
        Vector3 right = ProjectNdc(projection, new Vector3(4f, 0f, -5f));
        Assert.Equal(-1f, top.Y, TOLERANCE);
        Assert.Equal(1f, right.X, TOLERANCE);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f, 0.1f, 100f)]
    [InlineData(180f, 0.1f, 100f)]
    [InlineData(-1f, 0.1f, 100f)]
    [InlineData(float.NaN, 0.1f, 100f)]
    [InlineData(60f, 0f, 100f)]
    [InlineData(60f, -1f, 100f)]
    [InlineData(60f, float.PositiveInfinity, float.PositiveInfinity)]
    public void InvalidPerspectiveParametersThrow(float fieldOfView, float near, float far) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CameraMath.ValidatePerspective(fieldOfView, near, far));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveValidationAcceptsTheDocumentedRange() {
        CameraMath.ValidatePerspective(60f, 0.1f, 100f);
        CameraMath.ValidatePerspective(60f, 0.1f, float.PositiveInfinity);
        CameraMath.ValidatePerspective(179f, float.Epsilon, 1e30f);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f, 0.1f, 100f)]
    [InlineData(-2f, 0.1f, 100f)]
    [InlineData(float.NaN, 0.1f, 100f)]
    [InlineData(4f, 0.1f, float.PositiveInfinity)]
    [InlineData(4f, 0.1f, 0.05f)]
    public void InvalidOrthographicParametersThrow(float height, float near, float far) {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CameraMath.ValidateOrthographic(height, near, far));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingularCameraTransformThrows() {
        Transform singular = new(Vector3.Zero, Quaternion.Identity, new Vector3(1f, 0f, 1f));

        Assert.Throws<InvalidOperationException>(() => CameraMath.CreateViewMatrix(singular));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullViewportResolvesToTheFullTarget() {
        Rect viewport = CameraMath.ResolveViewport(null, 1280, 720, "viewport");

        Assert.Equal(new Rect(0f, 0f, 1280f, 720f), viewport);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ViewportIsClampedToTheTarget() {
        Rect viewport = CameraMath.ResolveViewport(new Rect(-10f, -20f, 200f, 100f), 1280, 720, "viewport");

        Assert.Equal(new Rect(0f, 0f, 190f, 80f), viewport);
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(float.NaN, 0f, 10f, 10f)]
    [InlineData(0f, 0f, 0f, 10f)]
    [InlineData(0f, 0f, 10f, -1f)]
    [InlineData(2000f, 0f, 10f, 10f)]
    [InlineData(0f, 2000f, 10f, 10f)]
    public void InvalidViewportThrows(float x, float y, float width, float height) {
        Assert.Equal(
            "viewport",
            Assert.Throws<ArgumentException>(
                () => CameraMath.ResolveViewport(new Rect(x, y, width, height), 1280, 720, "viewport")).ParamName);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyTargetThrows() {
        Assert.Throws<ArgumentException>(() => CameraMath.ResolveViewport(null, 0, 720, "viewport"));
        Assert.Throws<ArgumentException>(() => CameraMath.ResolveViewport(null, 1280, -1, "viewport"));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullClearColorIsOpaqueBlackAndHdrValuesAreAccepted() {
        Assert.Equal(Color.Black, CameraMath.ResolveClearColor(null, "clearColor"));

        Color hdr = new(4f, 2f, 0.5f, 1f);
        Assert.Equal(hdr, CameraMath.ResolveClearColor(hdr, "clearColor"));

        Assert.Throws<ArgumentException>(
            () => CameraMath.ResolveClearColor(new Color(float.NaN, 0f, 0f, 1f), "clearColor"));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ViewProjectionPlacesAPointInFrontOfTheCameraOnScreen() {
        CameraTestHost host = new();
        using (host) {
            host.BeginFrame();
            Camera camera = host.CreatePerspectiveCamera(transform: new Transform(new Vector3(0f, 0f, 5f)));
            Vector4 clip = Vector4.Transform(
                new Vector4(0f, 0f, 0f, 1f),
                camera.ViewProjectionMatrix);

            Assert.True(clip.W > 0f);
            Assert.Equal(0f, clip.X / clip.W, TOLERANCE);
            Assert.Equal(0f, clip.Y / clip.W, TOLERANCE);
            Assert.InRange(clip.Z / clip.W, 0f, 1f);
            camera.Dispose();

            host.EndFrame();
        }
    }

    private static float ProjectDepth(Matrix4x4 projection, float viewZ) {
        Vector3 ndc = ProjectNdc(projection, new Vector3(0f, 0f, viewZ));
        return ndc.Z;
    }

    private static Vector3 ProjectNdc(Matrix4x4 projection, Vector3 viewPosition) {
        Vector4 clip = Vector4.Transform(new Vector4(viewPosition, 1f), projection);
        return new Vector3(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
    }
}
