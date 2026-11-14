using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests.Rendering;

public sealed class CameraTests {
    private const float TOLERANCE = 1e-5f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerspectiveCameraExposesDocumentedDefaults() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Camera camera = host.CreatePerspectiveCamera();

        Assert.Equal(CameraProjection.Perspective, camera.Projection);
        Assert.Equal(60f, camera.FieldOfView);
        Assert.Equal(0f, camera.OrthographicHeight);
        Assert.Equal(0.1f, camera.Near);
        Assert.Equal(float.PositiveInfinity, camera.Far);
        Assert.Equal(ClearMode.ColorAndDepth, camera.ClearMode);
        Assert.Equal(Color.Black, camera.ClearColor);
        Assert.Equal(new Rect(0f, 0f, 1280f, 720f), camera.Viewport);
        Assert.True(camera.Frustum.Contains(new Vector3(0f, 0f, -5f)));
        camera.Dispose();

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OrthographicCameraExposesItsHeightAndFiniteFarPlane() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Camera camera = host.CreateOrthographicCamera(height: 6f, near: 0.5f, far: 50f);

        Assert.Equal(CameraProjection.Orthographic, camera.Projection);
        Assert.Equal(6f, camera.OrthographicHeight);
        Assert.Equal(0f, camera.FieldOfView);
        Assert.Equal(0.5f, camera.Near);
        Assert.Equal(50f, camera.Far);
        Assert.True(camera.Frustum.Contains(new Vector3(0f, 2f, -5f)));
        Assert.False(camera.Frustum.Contains(new Vector3(0f, 4f, -5f)));
        camera.Dispose();

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullClearColorIsOpaqueBlackAndHdrValuesAreAccepted() {
        using CameraTestHost host = new();
        host.BeginFrame();

        using (Camera black = host.CreatePerspectiveCamera(clearColor: null)) {
            Assert.Equal(Color.Black, black.ClearColor);
        }

        Color hdr = new(4f, 2f, 0.5f, 1f);
        using (Camera camera = host.CreatePerspectiveCamera(
            clearColor: hdr,
            clearMode: ClearMode.All)) {
            Assert.Equal(hdr, camera.ClearColor);
            Assert.Equal(ClearMode.All, camera.ClearMode);
        }

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NonFiniteClearColorThrows() {
        using CameraTestHost host = new();
        host.BeginFrame();

        ArgumentException thrown = Assert.Throws<ArgumentException>(
            () => host.CreatePerspectiveCamera(clearColor: new Color(float.NaN, 0f, 0f, 1f)));
        Assert.Equal("clearColor", thrown.ParamName);

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ViewportIsClampedAndDrivesTheProjectionAspect() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Camera camera = host.CreatePerspectiveCamera(
            viewport: new Rect(-10f, -20f, 200f, 100f));

        Assert.Equal(new Rect(0f, 0f, 190f, 80f), camera.Viewport);
        float f = 1f / MathF.Tan(60f * MathF.PI / 180f * 0.5f);
        Assert.Equal(f / (190f / 80f), camera.ProjectionMatrix.M11, TOLERANCE);
        camera.Dispose();

        host.EndFrame();
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(float.NaN, 0f, 10f, 10f)]
    [InlineData(0f, 0f, 0f, 10f)]
    [InlineData(0f, 0f, -1f, 10f)]
    [InlineData(2000f, 0f, 10f, 10f)]
    [InlineData(0f, 2000f, 10f, 10f)]
    public void InvalidViewportThrows(float x, float y, float width, float height) {
        using CameraTestHost host = new();
        host.BeginFrame();

        Assert.Equal(
            "viewport",
            Assert.Throws<ArgumentException>(
                () => host.CreatePerspectiveCamera(viewport: new Rect(x, y, width, height))).ParamName);

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidClearModeBitsThrow() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => host.CreatePerspectiveCamera(clearMode: (ClearMode)8));

        host.EndFrame();
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f, 0.1f, 100f)]
    [InlineData(180f, 0.1f, 100f)]
    [InlineData(60f, 0f, 100f)]
    [InlineData(60f, 0.1f, 0.05f)]
    public void InvalidPerspectiveArgumentsThrow(float fieldOfView, float near, float far) {
        using CameraTestHost host = new();
        host.BeginFrame();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => host.CreatePerspectiveCamera(fieldOfView: fieldOfView, near: near, far: far));

        host.EndFrame();
    }

    [Theory]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    [InlineData(0f, 0.1f, 100f)]
    [InlineData(4f, 0f, 100f)]
    [InlineData(4f, 0.1f, float.PositiveInfinity)]
    [InlineData(4f, 0.1f, 0.05f)]
    public void InvalidOrthographicArgumentsThrow(float height, float near, float far) {
        using CameraTestHost host = new();
        host.BeginFrame();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => host.CreateOrthographicCamera(height: height, near: near, far: far));

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SingularCameraTransformThrowsInvalidOperation() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Transform singular = new(Vector3.Zero, Quaternion.Identity, new Vector3(1f, 0f, 1f));
        Assert.Throws<InvalidOperationException>(
            () => host.CreatePerspectiveCamera(transform: singular));

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreatingASecondCameraWhileOneIsActiveThrows() {
        using CameraTestHost host = new();
        host.BeginFrame();

        Camera first = host.CreatePerspectiveCamera();
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => host.CreatePerspectiveCamera());
        Assert.Contains("Camera 1", thrown.Message, StringComparison.Ordinal);

        first.Dispose();
        Camera second = host.CreatePerspectiveCamera();
        Assert.True(second.Number > 1);
        second.Dispose();

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CameraCreationOutsideAFrameThrows() {
        CameraService service = new(new FakePlatformWindow(static () => { }));
        TickThread.Attach();
        try {
            Assert.Throws<InvalidOperationException>(
                () => service.CreateCamera(
                    Transform.Identity,
                    60f,
                    ClearMode.ColorAndDepth,
                    null,
                    0.1f,
                    100f,
                    null));
        } finally {
            TickThread.Detach();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CameraCreationRequiresTheTickThread() {
        CameraService service = new(new FakePlatformWindow(static () => { }));

        Assert.False(TickThread.IsCurrent);
        Assert.Throws<InvalidOperationException>(
            () => service.CreateCamera(
                Transform.Identity,
                60f,
                ClearMode.ColorAndDepth,
                null,
                0.1f,
                100f,
                null));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeIsIdempotentAndSealsThePass() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();

        camera.Dispose();
        camera.Dispose();

        Assert.True(camera.IsDisposed);
        Assert.Single(packet.Passes.ToArray());
        Assert.True(packet.Passes[0].IsSealed);
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EveryPropertyAndDrawThrowsAfterDispose() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        using Model model = RenderingTestAssets.CreateModel(
            meshes: [RenderingTestAssets.CreateMesh("meshes/model")],
            materials: [RenderingTestAssets.CreateShader("shaders/model")]);
        camera.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = camera.Transform);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.Projection);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.FieldOfView);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.OrthographicHeight);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.Near);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.Far);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.ClearMode);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.ClearColor);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.Viewport);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.ViewMatrix);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.ProjectionMatrix);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.ViewProjectionMatrix);
        Assert.Throws<ObjectDisposedException>(() => _ = camera.Frustum);
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawMesh(mesh, Transform.Identity, shader));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawInstanced(mesh, shader, [Transform.Identity]));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawModel(model, Transform.Identity));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireModel(model, Transform.Identity));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawModelInstanced(model, [Transform.Identity]));
        Assert.Throws<ObjectDisposedException>(
            () => camera.ScreenToWorldRay(new Vector2(0f, 0f)));
        Assert.Throws<ObjectDisposedException>(
            () => camera.WorldToScreen(Vector3.Zero));
        Assert.True(camera.IsDisposed);

        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EndFrameCopiesTheAttachedRuntimeToneMappingIntoThePacket() {
        using CameraTestHost host = new();
        TonemappingSettings tonemapping = new(1f, 4f, 2f);
        host.AttachTonemapping(tonemapping);
        RenderFramePacket packet = host.BeginFrame();

        // The tick thread owns the settings during the frame; the seal sees the final values.
        tonemapping.Exposure = 2f;
        tonemapping.AgxContrast = 3f;
        host.EndFrame();

        Assert.Equal(new TonemappingSnapshot(2f, 4f, 3f), packet.Tonemapping);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EndFrameUsesTheDefaultSnapshotWithoutAttachedRuntimeSettings() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();

        host.EndFrame();

        Assert.Equal(TonemappingSnapshot.Default, packet.Tonemapping);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AttachTonemappingRejectsNull() {
        using CameraTestHost host = new();

        Assert.Throws<ArgumentNullException>(() => host.AttachTonemapping(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACameraLeftUndisposedMakesEndFrameThrowAndNamesIt() {
        using CameraTestHost host = new();
        _ = host.BeginFrame();
        host.CreatePerspectiveCamera();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(host.EndFrame);

        Assert.Contains("Camera 1", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("not disposed", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PassRecordsTheCameraValueSnapshots() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame(time: 2.5, deltaTime: 0.02);
        Color clearColor = new(0.25f, 0.5f, 0.75f, 1f);
        Transform transform = new(new Vector3(1f, 2f, 3f));
        Camera camera = host.CreatePerspectiveCamera(
            transform: transform,
            clearMode: ClearMode.All,
            clearColor: clearColor,
            viewport: new Rect(10f, 20f, 300f, 200f));

        Matrix4x4 view = camera.ViewMatrix;
        Matrix4x4 projection = camera.ProjectionMatrix;
        Matrix4x4 viewProjection = camera.ViewProjectionMatrix;
        camera.Dispose();
        host.EndFrame();

        CameraPass pass = packet.Passes[0];
        Assert.Equal(2.5, packet.Time);
        Assert.Equal(0.02, packet.DeltaTime);
        Assert.Equal(transform.Position, pass.CameraPosition);
        Assert.Equal(view, pass.View);
        Assert.Equal(projection, pass.Projection);
        Assert.Equal(viewProjection, pass.ViewProjection);
        Assert.Equal(10, pass.ViewportX);
        Assert.Equal(20, pass.ViewportY);
        Assert.Equal(300, pass.ViewportWidth);
        Assert.Equal(200, pass.ViewportHeight);
        Assert.Equal(ClearMode.All, pass.ClearMode);
        Assert.Equal(clearColor, pass.ClearColor);
        Assert.True(packet.IsSealed);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawMeshSnapshotsTheShaderCursorAtCallTime() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader(defaultValue: 0f);
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Camera camera = host.CreatePerspectiveCamera();

        shader.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 2f);
        camera.DrawMesh(mesh, Transform.Identity, shader);
        shader.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 3f);
        camera.Dispose();
        host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(2f, RenderingTestAssets.ReadInstanceFloat(shader, record.InstanceTemplate, RenderingTestAssets.VALUE_UNIFORM));
        Assert.Equal(1, record.InstanceCount);
        Assert.Single(record.GetTextureBindings().ToArray());
        Assert.Null(record.GetTextureBindings()[0]);
        Assert.Equal(new Vector4(0f, 0f, 1f, 1f), record.UvRect);
        Assert.Equal(Color.White, record.Tint);
        Assert.Equal(RenderState.Opaque, record.RenderState);
        Assert.Equal(Transform.Identity.ToMatrix(), record.GetWorldMatrices()[0]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawMeshUsesTheRenderStateAndTintOverrides() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Camera camera = host.CreatePerspectiveCamera();
        Color tint = new(0.1f, 0.2f, 0.3f, 0.4f);

        camera.DrawMesh(mesh, Transform.Identity, shader, RenderState.Additive, tint);
        camera.Dispose();
        host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(RenderState.Additive, record.RenderState);
        Assert.Equal(tint, record.Tint);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawMeshUsesTheShaderDefaultRenderStateWhenNoOverrideIsGiven() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader(defaultRenderState: RenderState.AlphaBlend);
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawMesh(mesh, Transform.Identity, shader);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(RenderState.AlphaBlend, packet.Passes[0].Draws[0].RenderState);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawMeshRetainsTheMeshShaderAndBoundTextures() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Texture texture = RenderingTestAssets.CreateTexture();
        shader.SetUniform(RenderingTestAssets.TEXTURE_BINDING, texture);
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawMesh(mesh, Transform.Identity, shader);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(3, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawingADisposedMeshOrShaderThrowsObjectDisposed() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();
        Mesh disposedMesh = RenderingTestAssets.CreateMesh();
        Shader shader = RenderingTestAssets.CreateShader();
        disposedMesh.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawMesh(disposedMesh, Transform.Identity, shader));

        Shader disposedShader = RenderingTestAssets.CreateShader("shaders/disposed");
        disposedShader.Dispose();
        Mesh mesh = RenderingTestAssets.CreateMesh("meshes/live");
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawMesh(mesh, Transform.Identity, disposedShader));

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AShaderWithoutBothStagesThrowsInvalidOperation() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();
        Shader vertexOnly = RenderingTestAssets.CreateShader(
            "shaders/vertex-only",
            stages: ShaderStageMask.Vertex);
        Mesh mesh = RenderingTestAssets.CreateMesh();

        Assert.Throws<InvalidOperationException>(
            () => camera.DrawMesh(mesh, Transform.Identity, vertexOnly));

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawInstancedCopiesEveryTransformAndOneCursorSnapshot() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh(indexCount: 3);
        Camera camera = host.CreatePerspectiveCamera();
        Transform[] transforms = [
            new(new Vector3(1f, 0f, 0f)),
            new(new Vector3(2f, 0f, 0f)),
            new(new Vector3(3f, 0f, 0f)),
        ];

        camera.DrawInstanced(mesh, shader, transforms);
        camera.Dispose();
        host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(3, record.InstanceCount);
        ReadOnlySpan<Matrix4x4> worldMatrices = record.GetWorldMatrices();
        for (int index = 0; index < transforms.Length; index++) {
            Assert.Equal(transforms[index].ToMatrix(), worldMatrices[index]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyInstancedSpanRecordsNothing() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader shader = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawInstanced(mesh, shader, ReadOnlySpan<Transform>.Empty);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelSnapshotsThePartMaterialCursorAtCallTime() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader material = RenderingTestAssets.CreateShader(defaultValue: 0.5f);
        Mesh mesh = RenderingTestAssets.CreateMesh();
        using Model model = RenderingTestAssets.CreateModel(meshes: [mesh], materials: [material]);
        Texture texture = RenderingTestAssets.CreateTexture();
        Camera camera = host.CreatePerspectiveCamera();
        material.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 9f);
        material.SetUniform(RenderingTestAssets.TEXTURE_BINDING, texture);

        camera.DrawModel(model, Transform.Identity);
        material.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 10f);
        camera.Dispose();
        host.EndFrame();

        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(
            9f,
            RenderingTestAssets.ReadInstanceFloat(
                material,
                record.InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
        Assert.Same(texture, record.GetTextureBindings()[0]);
        Assert.Equal(Color.White, record.Tint);
        // One distinct mesh, the part material's internal program, and the bound texture.
        Assert.Equal(3, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelUsesEachPartMaterialProgramStateAndTexture() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/first", defaultValue: 0f);
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/second", defaultValue: 0f);
        using Model model = new(
            new ModelPart("first", firstMesh, firstMaterial, RenderState.Additive),
            new ModelPart("second", secondMesh, secondMaterial, RenderState.Multiply));
        Texture firstTexture = RenderingTestAssets.CreateTexture("textures/first");
        Texture secondTexture = RenderingTestAssets.CreateTexture("textures/second");
        Camera camera = host.CreatePerspectiveCamera();
        Color tint = new(0.5f, 0.25f, 0.125f, 1f);
        firstMaterial.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 7f);
        firstMaterial.SetUniform(RenderingTestAssets.TEXTURE_BINDING, firstTexture);
        secondMaterial.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 8f);
        secondMaterial.SetUniform(RenderingTestAssets.TEXTURE_BINDING, secondTexture);

        camera.DrawModel(model, Transform.Identity, tint);
        firstMaterial.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 9f);
        secondMaterial.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 10f);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(2, packet.Passes[0].DrawCount);
        RenderDrawRecord firstRecord = packet.Passes[0].Draws[0];
        RenderDrawRecord secondRecord = packet.Passes[0].Draws[1];
        Assert.Same(firstMaterial.Program.Lifetime, firstRecord.Program);
        Assert.Same(secondMaterial.Program.Lifetime, secondRecord.Program);
        Assert.Equal(
            7f,
            RenderingTestAssets.ReadInstanceFloat(
                firstMaterial,
                firstRecord.InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
        Assert.Equal(
            8f,
            RenderingTestAssets.ReadInstanceFloat(
                secondMaterial,
                secondRecord.InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
        Assert.Same(firstTexture, firstRecord.GetTextureBindings()[0]);
        Assert.Same(secondTexture, secondRecord.GetTextureBindings()[0]);
        Assert.Equal(RenderState.Additive, firstRecord.RenderState);
        Assert.Equal(RenderState.Multiply, secondRecord.RenderState);
        Assert.Equal(tint, firstRecord.Tint);
        Assert.Equal(tint, secondRecord.Tint);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelDrawComposesThePartLocalTransformBeforeTheInstanceTransform() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        using Model model = RenderingTestAssets.CreateModel(
            meshes: [mesh],
            localTransform: new Transform(new Vector3(1f, 0f, 0f)));
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawModel(model, new Transform(new Vector3(0f, 2f, 0f)));
        camera.Dispose();
        host.EndFrame();

        Matrix4x4 world = packet.Passes[0].Draws[0].GetWorldMatrices()[0];
        Assert.Equal(new Vector3(1f, 2f, 0f), Vector3.Transform(Vector3.Zero, world));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawWireModelRecordsEveryPartWithTheWireframeState() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/wire-first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/wire-second");
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/wire-first", defaultValue: 0.25f);
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/wire-second", defaultValue: 0.5f);
        Transform local = new(new Vector3(1f, 0f, 0f));
        using Model model = RenderingTestAssets.CreateModel(
            meshes: [firstMesh, secondMesh],
            materials: [firstMaterial, secondMaterial],
            localTransform: local);
        Camera camera = host.CreatePerspectiveCamera();
        Color tint = new(0.5f, 0.25f, 0.125f, 1f);
        Transform instance = new(
            new Vector3(0f, 2f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            new Vector3(2f));

        camera.DrawWireModel(model, instance, tint);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(2, packet.Passes[0].DrawCount);
        RenderDrawRecord[] records = [.. packet.Passes[0].Draws];
        Assert.Same(firstMaterial.Program.Lifetime, records[0].Program);
        Assert.Same(secondMaterial.Program.Lifetime, records[1].Program);
        foreach (RenderDrawRecord record in records) {
            Assert.True(record.Wireframe);
            Assert.Equal(tint, record.Tint);
            Assert.Equal(local.ToMatrix() * instance.ToMatrix(), record.GetWorldMatrices()[0]);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelAndDrawWireModelUseSeparateBatches() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        using Model model = RenderingTestAssets.CreateModel(meshes: [mesh], materials: [material]);
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawModel(model, Transform.Identity);
        camera.DrawWireModel(model, Transform.Identity);
        camera.DrawWireModel(model, new Transform(new Vector3(2f, 0f, 0f)));
        camera.Dispose();
        host.EndFrame();

        // The polygon mode is part of the batch key: the filled draw and the two wireframe draws
        // share mesh, program, and render state but still need different pipelines.
        FramePlan plan = new BatchPlanner().Plan(packet);
        Assert.Equal(2, plan.Passes[0].Batches.Count);
        Assert.Equal(2, plan.DrawCalls);
        PlannedBatch filled = plan.Passes[0].Batches[0];
        PlannedBatch wireframe = plan.Passes[0].Batches[1];
        Assert.False(filled.Wireframe);
        Assert.Equal(1, filled.InstanceCount);
        Assert.True(wireframe.Wireframe);
        Assert.Equal(2, wireframe.InstanceCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawWireModelRejectsDisposedModelPartAssetsAndInvalidMaterials() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();

        Model disposedModel = RenderingTestAssets.CreateModel(
            meshes: [RenderingTestAssets.CreateMesh("meshes/wire-disposed-model")],
            materials: [RenderingTestAssets.CreateShader("shaders/wire-disposed-model")]);
        disposedModel.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawWireModel(disposedModel, Transform.Identity));

        Mesh disposedMesh = RenderingTestAssets.CreateMesh("meshes/wire-disposed-part");
        Shader liveMaterial = RenderingTestAssets.CreateShader("shaders/wire-live-part");
        using Model meshModel = new(new ModelPart("part", disposedMesh, liveMaterial));
        disposedMesh.Lifetime.ForceDispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawWireModel(meshModel, Transform.Identity));

        Mesh liveMesh = RenderingTestAssets.CreateMesh("meshes/wire-live-part");
        Shader disposedMaterial = RenderingTestAssets.CreateShader("shaders/wire-disposed-material");
        using Model materialModel = new(new ModelPart("part", liveMesh, disposedMaterial));
        disposedMaterial.Dispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawWireModel(materialModel, Transform.Identity));

        Mesh texturedMesh = RenderingTestAssets.CreateMesh("meshes/wire-textured-part");
        Shader texturedMaterial = RenderingTestAssets.CreateShader("shaders/wire-textured-part");
        using Model texturedModel = new(new ModelPart("part", texturedMesh, texturedMaterial));
        Texture disposedTexture = RenderingTestAssets.CreateTexture("textures/wire-disposed-part");
        texturedMaterial.SetUniform(RenderingTestAssets.TEXTURE_BINDING, disposedTexture);
        disposedTexture.Lifetime.ForceDispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawWireModel(texturedModel, Transform.Identity));

        Shader vertexOnly = RenderingTestAssets.CreateShader(
            "shaders/wire-vertex-only",
            stages: ShaderStageMask.Vertex);
        using Model invalidMaterialModel = new(
            new ModelPart("part", RenderingTestAssets.CreateMesh("meshes/wire-invalid-part"), vertexOnly));
        Assert.Throws<InvalidOperationException>(
            () => camera.DrawWireModel(invalidMaterialModel, Transform.Identity));

        // Validation runs before any part is recorded, so a bad part cannot leave frame holds behind.
        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelInstancedSnapshotsEachPartMaterialAndTransform() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh first = RenderingTestAssets.CreateMesh("meshes/first");
        Mesh second = RenderingTestAssets.CreateMesh("meshes/second");
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/first", defaultValue: 0.25f);
        Shader secondMaterial = RenderingTestAssets.CreateShader("shaders/second", defaultValue: 0.5f);
        using Model model = RenderingTestAssets.CreateModel(
            meshes: [first, second],
            materials: [firstMaterial, secondMaterial],
            localTransform: new Transform(new Vector3(0f, 1f, 0f)));
        Camera camera = host.CreatePerspectiveCamera();
        Transform[] transforms = [
            new(new Vector3(0f, 0f, 0f)),
            new(new Vector3(10f, 0f, 0f)),
        ];

        camera.DrawModelInstanced(model, transforms);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(2, packet.Passes[0].DrawCount);
        RenderDrawRecord[] records = [.. packet.Passes[0].Draws];
        Assert.Same(firstMaterial.Program.Lifetime, records[0].Program);
        Assert.Same(secondMaterial.Program.Lifetime, records[1].Program);
        foreach (RenderDrawRecord record in records) {
            Assert.Equal(2, record.InstanceCount);
            Assert.Equal(new Vector3(0f, 1f, 0f), Vector3.Transform(Vector3.Zero, record.GetWorldMatrices()[0]));
            Assert.Equal(new Vector3(10f, 1f, 0f), Vector3.Transform(Vector3.Zero, record.GetWorldMatrices()[1]));
        }

        Assert.Equal(
            0.25f,
            RenderingTestAssets.ReadInstanceFloat(
                firstMaterial,
                records[0].InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
        Assert.Equal(
            0.5f,
            RenderingTestAssets.ReadInstanceFloat(
                secondMaterial,
                records[1].InstanceTemplate,
                RenderingTestAssets.VALUE_UNIFORM));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelInstancedRecordsEveryTransformForLargeInstanceCounts() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        using Model model = RenderingTestAssets.CreateModel(meshes: [mesh], materials: [material]);
        Camera camera = host.CreatePerspectiveCamera();
        Transform[] transforms = new Transform[128];
        for (int index = 0; index < transforms.Length; index++) {
            transforms[index] = new Transform(new Vector3(index, 0f, 0f));
        }

        camera.DrawModelInstanced(model, transforms);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(1, packet.Passes[0].DrawCount);
        RenderDrawRecord record = packet.Passes[0].Draws[0];
        Assert.Equal(transforms.Length, record.InstanceCount);
        Assert.Equal(Vector3.Zero, Vector3.Transform(Vector3.Zero, record.GetWorldMatrices()[0]));
        Assert.Equal(
            new Vector3(transforms.Length - 1, 0f, 0f),
            Vector3.Transform(Vector3.Zero, record.GetWorldMatrices()[transforms.Length - 1]));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EmptyModelInstancedSpanRecordsNothing() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader();
        using Model model = RenderingTestAssets.CreateModel(meshes: [mesh], materials: [material]);
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawModelInstanced(model, ReadOnlySpan<Transform>.Empty);
        camera.Dispose();
        host.EndFrame();

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DrawModelRetainsPartMeshesProgramsAndTexturesOnly() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Shader material = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh();
        using Model model = RenderingTestAssets.CreateModel(meshes: [mesh, mesh], materials: [material, material]);
        Camera camera = host.CreatePerspectiveCamera();
        Texture texture = RenderingTestAssets.CreateTexture();
        material.SetUniform(RenderingTestAssets.TEXTURE_BINDING, texture);

        camera.DrawModel(model, Transform.Identity);
        camera.Dispose();
        host.EndFrame();

        // One distinct part mesh, the internal program, and the bound texture. The model and the
        // material are not engine assets and add no frame hold.
        Assert.Equal(3, packet.Retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ModelDrawsRejectDisposedModelAndPartAssets() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();

        Model disposedModel = RenderingTestAssets.CreateModel(
            meshes: [RenderingTestAssets.CreateMesh("meshes/disposed-model")],
            materials: [RenderingTestAssets.CreateShader("shaders/disposed-model")]);
        disposedModel.Dispose();
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawModel(disposedModel, Transform.Identity));
        Assert.Throws<ObjectDisposedException>(
            () => camera.DrawModelInstanced(disposedModel, [Transform.Identity]));

        Mesh disposedMesh = RenderingTestAssets.CreateMesh("meshes/disposed-part");
        Shader liveMaterial = RenderingTestAssets.CreateShader("shaders/live-part");
        using Model meshModel = new(new ModelPart("part", disposedMesh, liveMaterial));
        disposedMesh.Lifetime.ForceDispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawModel(meshModel, Transform.Identity));

        Mesh liveMesh = RenderingTestAssets.CreateMesh("meshes/live-part");
        Shader disposedMaterial = RenderingTestAssets.CreateShader("shaders/disposed-material");
        using Model materialModel = new(new ModelPart("part", liveMesh, disposedMaterial));
        disposedMaterial.Dispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawModel(materialModel, Transform.Identity));

        Mesh texturedMesh = RenderingTestAssets.CreateMesh("meshes/textured-part");
        Shader texturedMaterial = RenderingTestAssets.CreateShader("shaders/textured-part");
        using Model texturedModel = new(new ModelPart("part", texturedMesh, texturedMaterial));
        Texture disposedTexture = RenderingTestAssets.CreateTexture("textures/disposed-part");
        texturedMaterial.SetUniform(RenderingTestAssets.TEXTURE_BINDING, disposedTexture);
        disposedTexture.Lifetime.ForceDispose();
        Assert.Throws<ObjectDisposedException>(() => camera.DrawModel(texturedModel, Transform.Identity));

        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InvalidPartMaterialRecordsNothing() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        Mesh firstMesh = RenderingTestAssets.CreateMesh("meshes/first");
        Shader firstMaterial = RenderingTestAssets.CreateShader("shaders/first");
        Mesh secondMesh = RenderingTestAssets.CreateMesh("meshes/second");
        Shader vertexOnly = RenderingTestAssets.CreateShader(
            "shaders/vertex-only",
            stages: ShaderStageMask.Vertex);
        using Model model = new(
            new ModelPart("first", firstMesh, firstMaterial),
            new ModelPart("second", secondMesh, vertexOnly));
        Camera camera = host.CreatePerspectiveCamera();

        Assert.Throws<InvalidOperationException>(() => camera.DrawModel(model, Transform.Identity));
        Assert.Throws<InvalidOperationException>(
            () => camera.DrawModelInstanced(model, [Transform.Identity]));

        // Validation runs before any part is recorded, so a bad part cannot leave frame holds behind.
        Assert.Equal(0, packet.Passes[0].DrawCount);
        Assert.Equal(0, packet.Retention.Count);

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TwoPartMaterialsOverOneProgramShareOneModelBatch() {
        using CameraTestHost host = new();
        RenderFramePacket packet = host.BeginFrame();
        ShaderProgram program = RenderingTestAssets.CreateProgram("shaders/shared");
        Shader first = program.CreateMaterial();
        Shader second = program.CreateMaterial();
        Mesh sharedMesh = RenderingTestAssets.CreateMesh();
        using Model model = new(
            new ModelPart("first", sharedMesh, first),
            new ModelPart("second", sharedMesh, second));
        Camera camera = host.CreatePerspectiveCamera();

        camera.DrawModel(model, Transform.Identity);
        camera.Dispose();
        host.EndFrame();

        // The batch key is the internal program, so two distinct materials over it stay in one batch.
        FramePlan plan = new BatchPlanner().Plan(packet);
        PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
        Assert.Equal(2, batch.InstanceCount);
        Assert.Equal(1, plan.DrawCalls);
        Assert.Same(program.Lifetime, batch.Program);

        RenderingTestAssets.ReleaseProgram(program);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScreenToWorldRayRoundTripsForAPerspectiveCamera() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera(transform: new Transform(new Vector3(0f, 0f, 5f)));

        Ray ray = camera.ScreenToWorldRay(new Vector2(640f, 360f));
        Vector2 screen = camera.WorldToScreen(ray.Origin + (ray.Direction * 10f));

        Assert.Equal(new Vector3(0f, 0f, 5f), ray.Origin);
        Assert.Equal(-1f, ray.Direction.Z, TOLERANCE);
        Assert.Equal(640f, screen.X, 1e-3f);
        Assert.Equal(360f, screen.Y, 1e-3f);
        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScreenToWorldRayRoundTripsForAnOrthographicCamera() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreateOrthographicCamera(transform: new Transform(new Vector3(0f, 0f, 5f)), height: 4f);

        Ray ray = camera.ScreenToWorldRay(new Vector2(640f, 360f));
        Vector2 screen = camera.WorldToScreen(ray.Origin + (ray.Direction * 2f));

        Assert.Equal(4.9f, ray.Origin.Z, 1e-3f);
        Assert.Equal(-1f, ray.Direction.Z, TOLERANCE);
        Assert.Equal(640f, screen.X, 1e-3f);
        Assert.Equal(360f, screen.Y, 1e-3f);
        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScreenHelpersUseTheOffsetViewport() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera(
            transform: new Transform(new Vector3(0f, 0f, 5f)),
            viewport: new Rect(100f, 50f, 640f, 360f));

        Vector2 center = new(420f, 230f);
        Ray ray = camera.ScreenToWorldRay(center);
        Vector2 screen = camera.WorldToScreen(ray.Origin + (ray.Direction * 10f));

        Assert.Equal(center.X, screen.X, 1e-3f);
        Assert.Equal(center.Y, screen.Y, 1e-3f);
        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ScreenHelpersRejectNonFiniteInput() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();

        Assert.Throws<ArgumentException>(
            () => camera.ScreenToWorldRay(new Vector2(float.NaN, 0f)));
        Assert.Throws<ArgumentException>(
            () => camera.WorldToScreen(new Vector3(0f, float.PositiveInfinity, 0f)));

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullDrawArgumentsThrowArgumentNullException() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();
        Shader shader = RenderingTestAssets.CreateShader();
        Mesh mesh = RenderingTestAssets.CreateMesh();

        Assert.Throws<ArgumentNullException>(() => camera.DrawModel(null!, Transform.Identity));
        Assert.Throws<ArgumentNullException>(() => camera.DrawWireModel(null!, Transform.Identity));
        Assert.Throws<ArgumentNullException>(
            () => camera.DrawModelInstanced(null!, [Transform.Identity]));
        Assert.Throws<ArgumentNullException>(() => camera.DrawMesh(null!, Transform.Identity, shader));
        Assert.Throws<ArgumentNullException>(() => camera.DrawMesh(mesh, Transform.Identity, null!));
        Assert.Throws<ArgumentNullException>(() => camera.DrawInstanced(null!, shader, [Transform.Identity]));
        Assert.Throws<ArgumentNullException>(() => camera.DrawInstanced(mesh, null!, [Transform.Identity]));

        camera.Dispose();
        host.EndFrame();
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WorldToScreenReturnsNaNBehindTheCamera() {
        using CameraTestHost host = new();
        host.BeginFrame();
        Camera camera = host.CreatePerspectiveCamera();

        Vector2 behind = camera.WorldToScreen(new Vector3(0f, 0f, 1f));

        Assert.True(float.IsNaN(behind.X));
        Assert.True(float.IsNaN(behind.Y));
        camera.Dispose();
        host.EndFrame();
    }
}
