using System.Numerics;
using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// GPU integration tests for the per-instance draw texture path: one draw record with two instances
/// that bind two different bindless textures is recorded directly, submitted through the real
/// <see cref="Vulkan12Backend"/>, and probed with linear scene-target readbacks.
/// </summary>
/// <remarks>
/// <para>
/// The tests use the engine's real quad mesh and either the built-in <c>/shaders/mesh</c> program or the
/// built-in <c>/shaders/msdf</c> program over the production GPU asset bridge and worker
/// (<see cref="PrimitiveDrawGpuHarness"/>), so the bindless slots, the geometry pipeline, the instance
/// writer, and the readback are the production ones. Two instances of one record sit side by side and
/// bind different textures, so a correct per-instance write samples each instance's own texture. A
/// shared-set draw would sample one texture twice.
/// </para>
/// <para>
/// The draws are recorded into a detached <see cref="RenderFramePacket"/> because the public
/// <see cref="Camera"/> does not expose the per-instance texture path yet; the record API is the same one
/// the text draw path uses. A frame retains every texture it references in its retention set, exactly as
/// a recorder must, and releases the set after the backend reaches frame idleness. The tests join the
/// serialized <c>SDL</c> collection because SDL and Vulkan initialization are process global, and they
/// report a skip with the reason when the host cannot open a GPU device.
/// </para>
/// </remarks>
[Collection("SDL")]
public sealed class PerInstanceTextureGpuTests {
    private static readonly Color _clearColor = new(0.05f, 0.1f, 0.2f, 1f);
    private static readonly Color _instanceAColor = new(0.9f, 0.1f, 0.1f, 1f);
    private static readonly Color _instanceBColor = new(0.1f, 0.2f, 0.9f, 1f);
    private const float COLOR_TOLERANCE = 2e-3f;
    private const float CAMERA_HEIGHT = 2f;
    private const float INSTANCE_OFFSET = 1f;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void EachInstanceSamplesItsOwnTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        Texture textureA = harness.CreateTexture(1, 1, _instanceAColor);
        Texture textureB = harness.CreateTexture(1, 1, _instanceBColor);
        Shader material = harness.Primitives.MeshMaterial;
        Mesh quad = harness.Primitives.Quad;

        (Matrix4x4 view, Matrix4x4 projection, Matrix4x4 viewProjection, Vector3 position) =
            CreateOrthographicView(harness, CAMERA_HEIGHT);

        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            index: 0,
            view,
            projection,
            viewProjection,
            position,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: checked((int)harness.Width),
            viewportHeight: checked((int)harness.Height),
            ClearMode.ColorAndDepth,
            _clearColor);
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            quad.Lifetime,
            quad.Topology,
            quad.Bounds,
            quad.IndexCount,
            material.Program.Lifetime,
            material.DefaultRenderState,
            Color.White);
        record.SetPerInstanceShaderState(material.InstanceData, material.Textures.Count);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        bindings[0] = textureA;
        bindings[1] = textureB;
        pass.Add(record);
        // Recording a per-instance texture does not retain it; the recorder owns the frame hold, exactly
        // as the camera draw recorder does for a shared binding.
        packet.Retention.Add(textureA.Lifetime);
        packet.Retention.Add(textureB.Lifetime);
        packet.Seal();

        harness.Host.Backend.Submit(packet);
        harness.Host.Backend.Present();
        harness.Host.Backend.WaitForFrameResourcesIdle();
        packet.Retention.Release();

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);
        Assert.Equal(0, stats.CulledInstances);
        Assert.Equal(1, stats.PipelineCount);

        AssertColorNear(_instanceAColor, ReadColorAt(harness, -INSTANCE_OFFSET, viewProjection));
        AssertColorNear(_instanceBColor, ReadColorAt(harness, INSTANCE_OFFSET, viewProjection));
        // The clear color still owns the gap between the two quads, so neither instance wrote across.
        AssertColorNear(_clearColor, harness.ReadColor(harness.Width / 2, harness.Height / 2));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.GPU)]
    public void TheBuiltInMsdfProgramSamplesAPerInstanceTexture() {
        using PrimitiveDrawGpuHarness harness = PrimitiveDrawGpuHarness.CreateOrSkip(sampleCount: 1);
        // A 1x1 field of 1 is fully covered and a field of 0 is fully transparent, so the two instances
        // prove both that the program samples its own texture and that the derivative band reconstructs
        // coverage without a range or pixel-size uniform.
        Texture covered = harness.CreateTexture(1, 1, Color.White);
        Texture empty = harness.CreateTexture(1, 1, Color.Black);
        Shader material = harness.Primitives.MsdfMaterial;
        Mesh quad = harness.Primitives.Quad;
        Color tint = new(0.25f, 0.5f, 0.75f, 1f);

        (Matrix4x4 view, Matrix4x4 projection, Matrix4x4 viewProjection, Vector3 position) =
            CreateOrthographicView(harness, CAMERA_HEIGHT);

        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            index: 0,
            view,
            projection,
            viewProjection,
            position,
            viewportX: 0,
            viewportY: 0,
            viewportWidth: checked((int)harness.Width),
            viewportHeight: checked((int)harness.Height),
            ClearMode.ColorAndDepth,
            _clearColor);
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            quad.Lifetime,
            quad.Topology,
            quad.Bounds,
            quad.IndexCount,
            material.Program.Lifetime,
            material.DefaultRenderState,
            tint);
        record.SetPerInstanceShaderState(material.InstanceData, material.Textures.Count);
        Span<Matrix4x4> worldMatrices = record.BeginInstances(2);
        worldMatrices[0] = Matrix4x4.CreateTranslation(-INSTANCE_OFFSET, 0f, 0f);
        worldMatrices[1] = Matrix4x4.CreateTranslation(INSTANCE_OFFSET, 0f, 0f);
        Span<Texture?> bindings = record.BeginInstanceTextureBindings();
        bindings[0] = covered;
        bindings[1] = empty;
        pass.Add(record);
        packet.Retention.Add(covered.Lifetime);
        packet.Retention.Add(empty.Lifetime);
        packet.Seal();

        harness.Host.Backend.Submit(packet);
        harness.Host.Backend.Present();
        harness.Host.Backend.WaitForFrameResourcesIdle();
        packet.Retention.Release();

        RenderStats stats = harness.Host.Backend.Stats;
        Assert.Equal(1, stats.DrawCalls);
        Assert.Equal(2, stats.Instances);
        Assert.Equal(4, stats.Triangles);

        AssertColorNear(tint, ReadColorAt(harness, -INSTANCE_OFFSET, viewProjection));
        AssertColorNear(_clearColor, ReadColorAt(harness, INSTANCE_OFFSET, viewProjection));
        Assert.Empty(harness.Host.Instance.ConsumeValidationErrors());
    }

    private static (Matrix4x4 View, Matrix4x4 Projection, Matrix4x4 ViewProjection, Vector3 Position)
        CreateOrthographicView(PrimitiveDrawGpuHarness harness, float height) {
        Transform transform = new(new Vector3(0f, 0f, 1f));
        Matrix4x4 view = CameraMath.CreateViewMatrix(transform);
        float aspect = harness.Width / (float)harness.Height;
        Matrix4x4 projection = CameraMath.CreateOrthographicProjection(height, aspect, 0.1f, 10f);
        return (view, projection, view * projection, transform.Position);
    }

    /// <summary>
    /// Reads the resolved scene color at the world-space instance center. The projection mirrors
    /// <see cref="Camera.WorldToScreen"/> for a full-target viewport.
    /// </summary>
    private static (float R, float G, float B) ReadColorAt(
        PrimitiveDrawGpuHarness harness,
        float worldX,
        Matrix4x4 viewProjection) {
        Vector4 clip = Vector4.Transform(new Vector4(worldX, 0f, 0f, 1f), viewProjection);
        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;
        uint x = checked((uint)MathF.Floor(((ndcX * 0.5f) + 0.5f) * harness.Width));
        uint y = checked((uint)MathF.Floor(((ndcY * 0.5f) + 0.5f) * harness.Height));
        return harness.ReadColor(x, y);
    }

    private static void AssertColorNear(Color expected, (float R, float G, float B) actual) {
        Assert.True(
            MathF.Abs(expected.R - actual.R) <= COLOR_TOLERANCE,
            $"Red {actual.R} is not within {COLOR_TOLERANCE} of {expected.R}.");
        Assert.True(
            MathF.Abs(expected.G - actual.G) <= COLOR_TOLERANCE,
            $"Green {actual.G} is not within {COLOR_TOLERANCE} of {expected.G}.");
        Assert.True(
            MathF.Abs(expected.B - actual.B) <= COLOR_TOLERANCE,
            $"Blue {actual.B} is not within {COLOR_TOLERANCE} of {expected.B}.");
    }
}
