using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the per-instance texture-binding path of <see cref="RenderDrawRecord"/>: the shared
/// path is unchanged, the per-instance set is stored instance-major, the material overload derives the
/// template and the binding count, invalid combinations are rejected, bindings never split a batch, and
/// a pooled record never exposes a texture from an earlier frame.
/// </summary>
/// <remarks>
/// <para>
/// The record is a tick-thread value snapshot, so the tests record directly into a detached
/// <see cref="RenderFramePacket"/> without a GPU, a pack, or a camera. Texture objects and materials come
/// from <see cref="RenderingTestAssets"/>; the record only stores the references and never acquires an
/// engine reference, so the tests dispose them explicitly.
/// </para>
/// <para>
/// The raw <c>SetPerInstanceShaderState(template, count)</c> overload exists so a recorder that already
/// holds a template snapshot, and these tests, can exercise invalid templates. The material overload is
/// the engine-facing form: it derives both values from the material, so the count cannot disagree with
/// the template.
/// </para>
/// </remarks>
public sealed class PerInstanceTextureRecordTests
{
    /// <summary>
    /// The instance-template size a shader with one to four texture bindings and no uniforms gets from
    /// the shader ABI, which is enough for the two-slot cases below.
    /// </summary>
    private const int TEXTURE_TEMPLATE_SIZE = 16;

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheSharedTexturePathIsUnchanged()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        Texture texture = RenderingTestAssets.CreateTexture("textures/shared");
        try
        {
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
        }
        finally
        {
            texture.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheMaterialOverloadStoresPerInstanceBindingsInstanceMajor()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = CreateTwoTextureMaterial();
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
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
            Assert.Equal(material.InstanceData.Length, record.InstanceTemplate.Length);
            ReadOnlySpan<Texture?> stored = record.GetInstanceTextureBindings();
            Assert.Equal(4, stored.Length);
            Assert.Same(first, stored[0]);
            Assert.Same(second, stored[1]);
            Assert.Same(second, stored[2]);
            Assert.Same(first, stored[3]);
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OneBindingPerInstanceUsesOnlyThatInstancesTexture()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/one-texture");
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
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
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ATemplateTooSmallForTheDeclaredBindingsIsRejected()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);

            // Five slots need 20 template bytes; the 16-byte template has room for four.
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                record.SetPerInstanceShaderState(
                    new byte[TEXTURE_TEMPLATE_SIZE],
                    textureBindingCount: 5
                )
            );

            Assert.Contains(
                "5 declared texture index slot(s)",
                thrown.Message,
                StringComparison.Ordinal
            );
            Assert.Contains("16 byte(s)", thrown.Message, StringComparison.Ordinal);
            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.Equal(0, record.TextureBindingCount);
            Assert.True(record.InstanceTemplate.IsEmpty);
        }
        finally
        {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ANonPositiveBindingCountIsRejected()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                record.SetPerInstanceShaderState(
                    new byte[TEXTURE_TEMPLATE_SIZE],
                    textureBindingCount: 0
                )
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                record.SetPerInstanceShaderState(
                    new byte[TEXTURE_TEMPLATE_SIZE],
                    textureBindingCount: -1
                )
            );
            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.True(record.InstanceTemplate.IsEmpty);
        }
        finally
        {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ABindingCountThatWouldOverflowTheTemplateSizeIsRejected()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram();
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, program);

            // No checked multiplication can see this count: it is rejected before the byte count.
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                record.SetPerInstanceShaderState(new byte[TEXTURE_TEMPLATE_SIZE], int.MaxValue)
            );
            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.True(record.InstanceTemplate.IsEmpty);
        }
        finally
        {
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AMaterialWithoutTextureBindingsIsRejected()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        // The built-in /shaders/primitive program reflects no texture bindings.
        Shader material = RenderingTestAssets.CreateShader("shaders/no-textures", textures: []);
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                record.SetPerInstanceShaderState(material)
            );
            Assert.Contains(
                "declares no texture bindings",
                thrown.Message,
                StringComparison.Ordinal
            );
            Assert.False(record.HasPerInstanceTextureBindings);
        }
        finally
        {
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ANullOrDisposedMaterialIsRejected()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/disposed");
        RenderFramePacket packet = CreatePacket();
        RenderDrawRecord record = RentRecord(packet, mesh, material);

        Assert.Throws<ArgumentNullException>(() => record.SetPerInstanceShaderState((Shader)null!));
        // Disposing the material is the act under test, so it is released exactly here.
        material.Dispose();
        Assert.Throws<ObjectDisposedException>(() => record.SetPerInstanceShaderState(material));
        Assert.False(record.HasPerInstanceTextureBindings);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheMaterialMustBeOverTheRecordsProgram()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        ShaderProgram program = RenderingTestAssets.CreateProgram("shaders/guard");
        Shader recorded = program.CreateMaterial();
        Shader sameProgram = program.CreateMaterial();
        Shader otherProgram = RenderingTestAssets.CreateShader("shaders/guard-other");
        try
        {
            RenderFramePacket packet = CreatePacket();

            RenderDrawRecord accepted = RentRecord(packet, mesh, recorded);
            accepted.SetPerInstanceShaderState(sameProgram);
            Assert.True(accepted.HasPerInstanceTextureBindings);

            RenderDrawRecord rejected = RentRecord(packet, mesh, recorded);
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
                rejected.SetPerInstanceShaderState(otherProgram)
            );
            Assert.Contains(
                "is not over the program the draw record began with",
                thrown.Message,
                StringComparison.Ordinal
            );
            Assert.False(rejected.HasPerInstanceTextureBindings);
            Assert.True(rejected.InstanceTemplate.IsEmpty);

            // A record that has not begun has no program to match.
            RenderDrawRecord unbegun = packet.RentDrawRecord();
            Assert.Throws<InvalidOperationException>(() =>
                unbegun.SetPerInstanceShaderState(recorded)
            );
        }
        finally
        {
            recorded.Dispose();
            sameProgram.Dispose();
            otherProgram.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASharedAndAPerInstanceTextureSetAreMutuallyExclusive()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/exclusive");
        Texture texture = RenderingTestAssets.CreateTexture("textures/exclusive");
        try
        {
            RenderFramePacket packet = CreatePacket();

            RenderDrawRecord sharedFirst = RentRecord(packet, mesh, material);
            sharedFirst.SetShaderState(material.InstanceData, [texture]);
            Assert.Throws<InvalidOperationException>(() =>
                sharedFirst.SetPerInstanceShaderState(material)
            );
            Assert.Equal(1, sharedFirst.GetTextureBindings().Length);
            Assert.Same(texture, sharedFirst.GetTextureBindings()[0]);
            Assert.False(sharedFirst.HasPerInstanceTextureBindings);

            // An empty shared declaration still declares the shared form, because it writes the template.
            RenderDrawRecord emptySharedFirst = RentRecord(packet, mesh, material);
            emptySharedFirst.SetShaderState(material.InstanceData, []);
            Assert.Throws<InvalidOperationException>(() =>
                emptySharedFirst.SetPerInstanceShaderState(material)
            );
            Assert.False(emptySharedFirst.HasPerInstanceTextureBindings);

            RenderDrawRecord perInstanceFirst = RentRecord(packet, mesh, material);
            perInstanceFirst.SetPerInstanceShaderState(material);
            perInstanceFirst.BeginInstances(1);
            perInstanceFirst.BeginInstanceTextureBindings()[0] = texture;
            Assert.Throws<InvalidOperationException>(() =>
                perInstanceFirst.SetShaderState(material.InstanceData, [texture])
            );
            Assert.Throws<InvalidOperationException>(() =>
                perInstanceFirst.SetShaderState(material.InstanceData, [])
            );
            Assert.True(perInstanceFirst.GetTextureBindings().IsEmpty);
            Assert.True(perInstanceFirst.HasPerInstanceTextureBindings);
            Assert.Same(texture, perInstanceFirst.GetInstanceTextureBindings()[0]);
        }
        finally
        {
            texture.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResetClearsTheSharedAndPerInstanceBindings()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/reset");
        Texture texture = RenderingTestAssets.CreateTexture("textures/reset");
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
            record.BeginInstances(2);
            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            bindings[0] = texture;
            bindings[1] = texture;

            packet.Reset(2);

            Assert.False(record.HasPerInstanceTextureBindings);
            Assert.Equal(0, record.TextureBindingCount);
            Assert.True(record.GetInstanceTextureBindings().IsEmpty);
            Assert.True(record.GetTextureBindings().IsEmpty);
            Assert.Equal(0, record.InstanceCount);
            Assert.True(record.InstanceTemplate.IsEmpty);
        }
        finally
        {
            texture.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AReusedRecordDoesNotExposeStaleTextureBindings()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/stale");
        Texture texture = RenderingTestAssets.CreateTexture("textures/stale");
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
            record.BeginInstances(2);
            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            bindings[0] = texture;
            bindings[1] = texture;
            packet.Reset(2);

            RenderDrawRecord reused = packet.RentDrawRecord();
            Assert.Same(record, reused);
            reused.Begin(
                mesh.Lifetime,
                mesh.Topology,
                mesh.Bounds,
                mesh.IndexCount,
                material.Program.Lifetime,
                RenderState.Opaque,
                Color.White
            );
            reused.SetPerInstanceShaderState(material);
            reused.BeginInstances(1);

            Span<Texture?> refilled = reused.BeginInstanceTextureBindings();
            Assert.Equal(1, refilled.Length);
            Assert.True(refilled[0] is null);
        }
        finally
        {
            texture.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASecondBeginInstancesClearsThePerInstanceBindings()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/redeclare");
        Texture texture = RenderingTestAssets.CreateTexture("textures/redeclare");
        try
        {
            RenderFramePacket packet = CreatePacket();
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
            record.BeginInstances(2);
            Span<Texture?> bindings = record.BeginInstanceTextureBindings();
            bindings[0] = texture;
            bindings[1] = texture;

            // Redeclaring the instances discards the bindings filled for the previous declaration.
            Assert.Equal(3, record.BeginInstances(3).Length);
            Span<Texture?> refilled = record.BeginInstanceTextureBindings();
            Assert.Equal(3, refilled.Length);
            Assert.True(refilled[0] is null);
            Assert.True(refilled[1] is null);
            Assert.True(refilled[2] is null);
        }
        finally
        {
            texture.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FillRequiresAPerInstanceSetAndRecordedInstances()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/fill");
        try
        {
            RenderFramePacket packet = CreatePacket();

            RenderDrawRecord shared = RentRecord(packet, mesh, material);
            shared.SetShaderState(material.InstanceData, []);
            shared.BeginInstances(1);
            Assert.Throws<InvalidOperationException>(() => shared.BeginInstanceTextureBindings());

            RenderDrawRecord perInstance = RentRecord(packet, mesh, material);
            perInstance.SetPerInstanceShaderState(material);
            Assert.Throws<InvalidOperationException>(() =>
                perInstance.BeginInstanceTextureBindings()
            );

            perInstance.BeginInstances(1);
            Assert.Equal(1, perInstance.BeginInstanceTextureBindings().Length);
        }
        finally
        {
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PerInstanceBindingsDoNotSplitOrJoinABatch()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/batched");
        Texture first = RenderingTestAssets.CreateTexture("textures/first");
        Texture second = RenderingTestAssets.CreateTexture("textures/second");
        try
        {
            RenderFramePacket packet = CreatePacket();
            CameraPass pass = BeginPass(packet);

            RenderDrawRecord perInstance = RentRecord(packet, mesh, material);
            perInstance.SetPerInstanceShaderState(material);
            Span<Matrix4x4> worlds = perInstance.BeginInstances(2);
            Span<Texture?> bindings = perInstance.BeginInstanceTextureBindings();
            bindings[0] = first;
            bindings[1] = second;
            worlds[0] = Matrix4x4.CreateTranslation(0f, 0f, -5f);
            worlds[1] = Matrix4x4.CreateTranslation(1f, 0f, -5f);
            pass.Add(perInstance);

            RenderDrawRecord shared = RentRecord(packet, mesh, material);
            shared.SetShaderState(material.InstanceData, [first]);
            shared.BeginInstances(1)[0] = Matrix4x4.CreateTranslation(-1f, 0f, -5f);
            pass.Add(shared);

            FramePlan plan = new BatchPlanner().Plan(packet);

            PlannedBatch batch = Assert.Single(plan.Passes[0].Batches);
            Assert.Equal(3, batch.InstanceCount);
            Assert.Equal(1, plan.DrawCalls);
            Assert.Equal(3, plan.Instances);
            // The stride stays the header plus the uniform template; per-instance bindings are not part
            // of the record size, so mixing both forms keeps one batch and one stride.
            Assert.Equal(
                ShaderAbiLayout.INSTANCE_HEADER_SIZE + material.InstanceData.Length,
                ShaderAbiLayout.INSTANCE_HEADER_SIZE
                    + batch.Instances[0].Record.InstanceTemplate.Length
            );
        }
        finally
        {
            first.Dispose();
            second.Dispose();
            material.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheFrameRingReusesTheRecordWithEmptyBindings()
    {
        Mesh mesh = RenderingTestAssets.CreateMesh();
        Shader material = RenderingTestAssets.CreateShader("shaders/ring");
        Texture texture = RenderingTestAssets.CreateTexture("textures/ring");
        FrameRing ring = new();
        try
        {
            Assert.True(ring.TryAcquireWritable(out FrameLease lease));
            RenderFramePacket packet = Assert.IsType<RenderFramePacket>(lease.Packet);
            RenderDrawRecord record = RentRecord(packet, mesh, material);
            record.SetPerInstanceShaderState(material);
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
        }
        finally
        {
            texture.Dispose();
            material.Dispose();
        }
    }

    private static Shader CreateTwoTextureMaterial() =>
        RenderingTestAssets.CreateShader(
            "shaders/two-textures",
            textures: [RenderingTestAssets.TEXTURE_BINDING, "detailTexture"]
        );

    private static RenderFramePacket CreatePacket()
    {
        RenderFramePacket packet = new(1);
        packet.BeginRecording(0.0, 0.0);
        return packet;
    }

    private static CameraPass BeginPass(RenderFramePacket packet)
    {
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
            Color.Black
        );
        return pass;
    }

    private static RenderDrawRecord RentRecord(
        RenderFramePacket packet,
        Mesh mesh,
        ShaderProgram program
    )
    {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            program.Lifetime,
            RenderState.Opaque,
            Color.White
        );
        return record;
    }

    private static RenderDrawRecord RentRecord(RenderFramePacket packet, Mesh mesh, Shader material)
    {
        RenderDrawRecord record = packet.RentDrawRecord();
        record.Begin(
            mesh.Lifetime,
            mesh.Topology,
            mesh.Bounds,
            mesh.IndexCount,
            material.Program.Lifetime,
            RenderState.Opaque,
            Color.White
        );
        return record;
    }
}
