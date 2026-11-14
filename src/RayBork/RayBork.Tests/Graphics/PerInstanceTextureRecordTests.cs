using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the per-instance texture-binding path of <see cref="RenderDrawRecord"/>: the shared
/// path is unchanged, the per-instance set is stored instance-major, shader/template mismatches are
/// rejected, per-instance bindings never split a batch, and a pooled record never exposes a texture
/// from an earlier frame.
/// </summary>
/// <remarks>
/// The record is a tick-thread value snapshot, so the tests record directly into a detached
/// <see cref="RenderFramePacket"/> without a GPU, a pack, or a camera. Texture objects come from
/// <see cref="RenderingTestAssets"/>; the record only stores the references and never acquires an
/// engine reference, so the tests dispose them explicitly.
/// </remarks>
public sealed class PerInstanceTextureRecordTests {
    /// <summary>
    /// The instance-template size a shader with one to four texture bindings and no uniforms gets from
    /// the shader ABI, which is enough for the two-slot cases below.
    /// </summary>
    private const int TEXTURE_TEMPLATE_SIZE = 16;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheSharedTexturePathIsUnchanged() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/shared");
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetShaderState(program.DefaultInstanceData, [texture]);
            record.BeginInstances(2);

            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.Equal(0, record.TextureBindingCount);
            Assert.True(record.GetInstanceTextureBindings().IsEmpty);
            Assert.Equal(1, record.GetTextureBindings().Length);
            Assert.Same(texture, record.GetTextureBindings()[0]);
            Assert.Equal(program.DefaultInstanceData.Length, record.InstanceTemplate.Length);
            Assert.Equal(2, record.InstanceCount);
        } finally {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerInstanceBindingsAreStoredInstanceMajor() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], textureBindingCount: 2);
            record.BeginInstances(2);

            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            Assert.Equal(4, bindings.Length);
            bindings[0] = first;
            bindings[1] = second;
            bindings[2] = second;
            bindings[3] = first;

            Assert.True(record.HasPerInstanceTextureBindings);
            Assert.Equal(2, record.TextureBindingCount);
            Assert.True(record.GetTextureBindings().IsEmpty);
            ReadOnlySpan<Texture?> stored = record.GetInstanceTextureBindings();
            Assert.Equal(4, stored.Length);
            Assert.Same(first, stored[0]);
            Assert.Same(second, stored[1]);
            Assert.Same(second, stored[2]);
            Assert.Same(first, stored[3]);
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OneBindingPerInstanceUsesOnlyThatInstancesTexture() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], textureBindingCount: 1);
            record.BeginInstances(3);

            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            Assert.Equal(3, bindings.Length);
            bindings[0] = first;
            bindings[1] = null;
            bindings[2] = second;

            ReadOnlySpan<Texture?> stored = record.GetInstanceTextureBindings();
            Assert.Same(first, stored[0]);
            Assert.Null(stored[1]);
            Assert.Same(second, stored[2]);
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ATemplateTooSmallForTheDeclaredBindingsIsRejected() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);

            // Five slots need 20 template bytes; the 16-byte template has room for four.
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], textureBindingCount: 5));

            Assert.Contains("5 declared texture index slot(s)", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("16 byte(s)", thrown.Message, StringComparison.Ordinal);
            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.Equal(0, record.TextureBindingCount);
            Assert.True(record.InstanceTemplate.IsEmpty);
        } finally {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ANonPositiveBindingCountIsRejected() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], textureBindingCount: 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], textureBindingCount: -1));
            Assert.False(record.HasPerInstanceTextureBindings);
        } finally {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASharedAndAPerInstanceTextureSetAreMutuallyExclusive() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/exclusive");
        try {
            RenderFramePacket packet = CreatePacket();

            RenderDrawRecord sharedFirst = RentRecord(packet, mesh, program);
            sharedFirst.SetShaderState(program.DefaultInstanceData, [texture]);
            Assert.Throws<InvalidOperationException>(
                () => sharedFirst.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 1));
            Assert.Equal(1, sharedFirst.GetTextureBindings().Length);
            Assert.Same(texture, sharedFirst.GetTextureBindings()[0]);
            Assert.False(sharedFirst.HasPerInstanceTextureBindings);

            RenderDrawRecord perInstanceFirst = RentRecord(packet, mesh, program);
            perInstanceFirst.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 1);
            perInstanceFirst.BeginInstances(1);
            perInstanceFirst.BeginInstanceTextureBindings()[0] = texture;
            Assert.Throws<InvalidOperationException>(
                () => perInstanceFirst.SetShaderState(program.DefaultInstanceData, [texture]));
            Assert.Throws<InvalidOperationException>(
                () => perInstanceFirst.SetShaderState(program.DefaultInstanceData, []));
            Assert.True(perInstanceFirst.GetTextureBindings().IsEmpty);
            Assert.True(perInstanceFirst.HasPerInstanceTextureBindings);
            Assert.Same(texture, perInstanceFirst.GetInstanceTextureBindings()[0]);
        } finally {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResetClearsTheSharedAndPerInstanceBindings() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/reset");
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 2);
            record.BeginInstances(2);
            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            bindings[0] = texture;
            bindings[1] = texture;
            bindings[2] = texture;
            bindings[3] = texture;

            packet.Reset(2);

            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.Equal(0, record.TextureBindingCount);
            Assert.True(record.GetInstanceTextureBindings().IsEmpty);
            Assert.True(record.GetTextureBindings().IsEmpty);
            Assert.Equal(0, record.InstanceCount);
            Assert.True(record.InstanceTemplate.IsEmpty);
        } finally {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AReusedRecordDoesNotExposeStaleTextureBindings() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/stale");
        try {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 2);
            record.BeginInstances(2);
            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            bindings[0] = texture;
            bindings[1] = texture;
            bindings[2] = texture;
            bindings[3] = texture;
            packet.Reset(2);

            RenderDrawRecord reused = packet.RentDrawRecord();
            Assert.Same(record, reused);
            reused.Begin(
                mesh.Lifetime,
                mesh.Topology,
                mesh.Bounds,
                mesh.IndexCount,
                program.Lifetime,
                RenderState.Opaque,
                Color.White);
            reused.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 2);
            reused.BeginInstances(1);

            Span<Texture?> refilled = reused.BeginInstanceTextureBindings();
            Assert.Equal(2, refilled.Length);
            Assert.True(refilled[0] is null);
            Assert.True(refilled[1] is null);
        } finally {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FillRequiresInstancesAndAPerInstanceSet() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try {
            RenderFramePacket packet = CreatePacket();

            RenderDrawRecord shared = RentRecord(packet, mesh, program);
            shared.SetShaderState(program.DefaultInstanceData, []);
            shared.BeginInstances(1);
            Assert.Throws<InvalidOperationException>(() => shared.BeginInstanceTextureBindings());

            RenderDrawRecord perInstance = RentRecord(packet, mesh, program);
            perInstance.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 1);
            Assert.Throws<InvalidOperationException>(() => perInstance.BeginInstanceTextureBindings());

            perInstance.BeginInstances(1);
            Assert.Equal(1, perInstance.BeginInstanceTextureBindings().Length);
        } finally {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerInstanceBindingsDoNotSplitOrJoinABatch() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try {
            RenderFramePacket packet = CreatePacket();
            CameraPass pass = BeginPass(packet);

            RenderDrawRecord perInstance = RentRecord(packet, mesh, program);
            perInstance.SetPerInstanceShaderState(program.DefaultInstanceData, 1);
            Span<Matrix4x4> worlds = perInstance.BeginInstances(2);
            Span<Texture?> bindings = perInstance.BeginInstanceTextureBindings();
            bindings[0] = first;
            bindings[1] = second;
            worlds[0] = Matrix4x4.CreateTranslation(0f, 0f, -5f);
            worlds[1] = Matrix4x4.CreateTranslation(1f, 0f, -5f);
            pass.Add(perInstance);

            RenderDrawRecord shared = RentRecord(packet, mesh, program);
            shared.SetShaderState(program.DefaultInstanceData, [first]);
            shared.BeginInstances(1)[0] = Matrix4x4.CreateTranslation(-1f, 0f, -5f);
            pass.Add(shared);

            FramePlan plan = new BatchPlanner().Plan(packet);

            PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
            Assert.Equal(3, batch.InstanceCount);
            Assert.Equal(1, plan.DrawCalls);
            Assert.Equal(3, plan.Instances);
            // The stride stays the header plus the uniform template; per-instance bindings are not
            // part of the record size.
            Assert.Equal(
                ShaderAbiLayout.INSTANCE_HEADER_SIZE + program.DefaultInstanceData.Length,
                ShaderAbiLayout.INSTANCE_HEADER_SIZE + batch.Instances[0].Record.InstanceTemplate.Length);
        } finally {
            first.Dispose();
            second.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheFrameRingReusesTheRecordWithEmptyBindings() {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/ring");
        FrameRing ring = new();
        try {
            Assert.True(ring.TryAcquireWritable(out FrameLease lease));
            RenderFramePacket packet = Assert.IsType<RenderFramePacket>(lease.Packet);
            RenderDrawRecord record = RentRecord(packet, mesh, program);
            record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], 1);
            record.BeginInstances(1);
            record.BeginInstanceTextureBindings()[0] = texture;
            ring.Publish(lease);

            Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
            ring.Release(rendering);

            Assert.True(ring.TryAcquireWritable(out FrameLease reused));
            Assert.Same(packet, reused.Packet);
            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.True(record.GetInstanceTextureBindings().IsEmpty);
            Assert.True(record.GetTextureBindings().IsEmpty);
            ring.Abandon(reused);
        } finally {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    private static RenderFramePacket CreatePacket() {
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        return packet;
    }

    private static CameraPass BeginPass(RenderFramePacket packet) {
        Matrix4x4 view = CameraMath.CreateViewMatrix(Transform.Identity);
        Matrix4x4 projection = CameraMath.CreatePerspectiveProjection(60f, 16f / 9f, 0.1f, 1000f);
        CameraPass pass = packet.BeginPass();
        pass.Begin(
            packet.PassCount - 1,
            view,
            projection,
            view * projection,
            Vector3.Zero,
            0,
            0,
            1280,
            720,
            ClearMode.ColorAndDepth,
            Color.Black);
        return pass;
    }

    private static RenderDrawRecord RentRecord(RenderFramePacket packet, Mesh mesh, ShaderProgram program) {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            program.Lifetime,
            RenderState.Opaque,
            Color.White);
        return record;
    }
}
