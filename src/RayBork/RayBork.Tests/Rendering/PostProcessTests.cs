using RayBork.Internal;
using RayBork.Tests.Rendering;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the public post-process stack and effect API and for the camera snapshot contract:
/// ordering, duplicates, removal, disabled semantics, the custom shader capture and validation, the
/// frozen material state, and frame retention.
/// </summary>
public sealed class PostProcessTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheStackKeepsOrderAllowsDuplicatesAndRemovesByReference() {
        using CameraTestHost host = new();
        host.BeginFrame();
        PostProcessStack stack = new();
        ShaderPostProcessEffect first = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        ShaderPostProcessEffect second = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        try {
            Assert.True(stack.Enabled);
            Assert.Empty(stack.Effects);
            stack.Add(first);
            stack.Add(second);
            stack.Add(first);
            Assert.Equal(new PostProcessEffect[] { first, second, first }, stack.Effects);

            Assert.True(stack.Remove(first));
            Assert.Equal(new PostProcessEffect[] { second, first }, stack.Effects);
            Assert.True(stack.Remove(first));
            Assert.False(stack.Remove(first));
            Assert.Equal([second], stack.Effects);

            stack.Clear();
            Assert.Empty(stack.Effects);
            Assert.True(stack.Enabled);
        } finally {
            DisposeShader(first);
            DisposeShader(second);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullArgumentsAreRejected() {
        using CameraTestHost host = new();
        host.BeginFrame();
        PostProcessStack stack = new();
        Assert.Throws<ArgumentNullException>(() => stack.Add(null!));
        Assert.Throws<ArgumentNullException>(() => stack.Remove(null!));
        Assert.Throws<ArgumentNullException>(() => new ShaderPostProcessEffect(null!));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StackMutationsAreTickThreadOnly() {
        PostProcessStack stack = new();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        Exception? failure = null;
        Thread thread = new(() => {
            try {
                stack.Add(effect);
            } catch (Exception exception) {
                failure = exception;
            }

            try {
                stack.Enabled = false;
            } catch (Exception exception) {
                failure ??= exception;
            }
        }) {
            IsBackground = true,
            Name = "RayBork.Test.PostProcessThreadAffinity",
        };
        try {
            thread.Start();
            thread.Join();
            Assert.IsType<InvalidOperationException>(failure);
        } finally {
            effect.Shader.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACameraSnapshotsTheEnabledEffectsInStackOrder() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect first = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        ShaderPostProcessEffect second = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(first);
        stack.Add(second);
        try {
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.Equal(2, host.Packet.Passes[0].PostProcessEffectCount);
                Assert.True(host.Packet.Passes[0].HasEnabledPostProcessEffects);
            }

            // A stack change after the first camera affects later cameras only; the first pass keeps
            // the snapshots it captured.
            stack.Remove(first);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.Equal(1, host.Packet.Passes[1].PostProcessEffectCount);
            }

            Assert.Equal(2, host.Packet.Passes[0].PostProcessEffectCount);
            host.EndFrame();
        } finally {
            DisposeShader(first);
            DisposeShader(second);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADisabledStackAndADisabledEffectAreSkipped() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        try {
            effect.Enabled = false;
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.Equal(1, host.Packet.Passes[0].PostProcessEffectCount);
                Assert.False(host.Packet.Passes[0].HasEnabledPostProcessEffects);
                Assert.IsType<DisabledPostProcessEffectSnapshot>(host.Packet.Passes[0].PostProcessEffects[0]);
            }

            // Re-enabling the effect does not change the captured snapshot of a later camera only if the
            // stack itself is disabled: the disabled stack captures nothing at all.
            effect.Enabled = true;
            stack.Enabled = false;
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.Equal(0, host.Packet.Passes[1].PostProcessEffectCount);
                Assert.False(host.Packet.Passes[1].HasEnabledPostProcessEffects);
            }

            host.EndFrame();
        } finally {
            DisposeShader(effect);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheCustomEffectSnapshotFreezesTheMaterialCursorAndTextureBindings() {
        using CameraTestHost host = new(640, 360);
        host.BeginFrame();
        ShaderProgram program = RenderingTestAssets.CreateProgram(defaultValue: 1f, textures: ["sourceColor"]);
        Shader material = program.CreateMaterial();
        PostProcessStack stack = new();
        stack.Add(new ShaderPostProcessEffect(material));
        try {
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                ShaderPostProcessEffectSnapshot snapshot = Assert.IsType<ShaderPostProcessEffectSnapshot>(
                    host.Packet.Passes[0].PostProcessEffects[0]);
                Assert.Equal(
                    1f,
                    RenderingTestAssets.ReadInstanceFloat(material, snapshot.InstanceData, RenderingTestAssets.VALUE_UNIFORM));
                Assert.Equal(0, snapshot.SourceColorBinding);
                Assert.Equal(-1, snapshot.SourceDepthBinding);
                Assert.Equal(ShaderAbiLayout.INSTANCE_HEADER_SIZE + material.InstanceData.Length, snapshot.InstanceStride);
            }

            material.SetUniform(RenderingTestAssets.VALUE_UNIFORM, 7f);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                ShaderPostProcessEffectSnapshot snapshot = Assert.IsType<ShaderPostProcessEffectSnapshot>(
                    host.Packet.Passes[1].PostProcessEffects[0]);
                Assert.Equal(
                    7f,
                    RenderingTestAssets.ReadInstanceFloat(material, snapshot.InstanceData, RenderingTestAssets.VALUE_UNIFORM));
            }

            Assert.Equal(
                1f,
                RenderingTestAssets.ReadInstanceFloat(
                    material,
                    Assert.IsType<ShaderPostProcessEffectSnapshot>(host.Packet.Passes[0].PostProcessEffects[0]).InstanceData,
                    RenderingTestAssets.VALUE_UNIFORM));
            host.EndFrame();
        } finally {
            material.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ASnapshotRetainsTheProgramAndTexturesUntilTheFrameReleases() {
        using CameraTestHost host = new(640, 360);
        host.BeginFrame();
        var destroyedPrograms = new List<GpuAssetResource>();
        var destroyedTextures = new List<GpuAssetResource>();
        ShaderProgram program = RenderingTestAssets.CreateProgram(
            destroyed: destroyedPrograms,
            textures: ["sourceColor"]);
        Shader material = program.CreateMaterial();
        Texture texture = RenderingTestAssets.CreateTexture("textures/post-process-retained", destroyedTextures);
        PostProcessStack stack = new();
        stack.Add(new ShaderPostProcessEffect(material));
        try {
            material.SetUniform("sourceColor", texture);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.True(host.Packet.Retention.Count > 0);
            }

            // Drop the helper's catalog reference and the material's references. The frame holds keep
            // the program and the texture GPU resources alive, so no destruction is queued yet.
            RenderingTestAssets.ReleaseProgram(program);
            material.Dispose();
            texture.Dispose();
            Assert.Empty(destroyedPrograms);
            Assert.Empty(destroyedTextures);

            host.Packet.Retention.Release();
            Assert.NotEmpty(destroyedPrograms);
            Assert.NotEmpty(destroyedTextures);
            host.EndFrame();
        } finally {
            if (!material.IsDisposed) {
                material.Dispose();
            }

            if (!texture.IsDisposed) {
                texture.Dispose();
            }

            if (!program.IsDisposed) {
                RenderingTestAssets.ReleaseProgram(program);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AMissingSourceColorBindingIsRejectedWhenTheCameraIsCreated() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(
            RenderingTestAssets.CreateShader(textures: [RenderingTestAssets.TEXTURE_BINDING]));
        PostProcessStack stack = new();
        stack.Add(effect);
        try {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => host.CreatePerspectiveCamera(postProcess: stack));
            Assert.Contains(PostProcessNames.SOURCE_COLOR, exception.Message);
            Assert.Equal(0, host.Packet.PassCount);
        } finally {
            DisposeShader(effect);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AShaderWithoutBothStagesIsRejected() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(
            stages: ShaderStageMask.Fragment,
            textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        try {
            Assert.Throws<InvalidOperationException>(() => host.CreatePerspectiveCamera(postProcess: stack));
        } finally {
            DisposeShader(effect);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task SourceDepthOnADepthlessTargetIsRejectedWhenTheCameraIsCreated() {
        using RenderTextureService service = new();
        using FakeRendererBackend renderer = new();
        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(16, 16, Depth: false),
            CancellationToken.None);
        renderer.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;
        try {
            using CameraTestHost host = new();
            host.BeginFrame();
            ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(
                textures: ["sourceColor", "sourceDepth"]));
            PostProcessStack stack = new();
            stack.Add(effect);
            try {
                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                    () => host.CreatePerspectiveCamera(target: target, postProcess: stack));
                Assert.Contains(PostProcessNames.SOURCE_DEPTH, exception.Message);
            } finally {
                DisposeShader(effect);
            }
        } finally {
            target.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EffectEnabledMutationsAreTickThreadOnly() {
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        Exception? failure = null;
        Thread thread = new(() => {
            try {
                effect.Enabled = false;
            } catch (Exception exception) {
                failure = exception;
            }
        }) {
            IsBackground = true,
            Name = "RayBork.Test.PostProcessEffectThreadAffinity",
        };
        try {
            thread.Start();
            thread.Join();
            Assert.IsType<InvalidOperationException>(failure);
        } finally {
            effect.Shader.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisablingAnEffectAfterCameraCreationKeepsTheCapturedSnapshot() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        try {
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.True(host.Packet.Passes[0].HasEnabledPostProcessEffects);
            }

            effect.Enabled = false;
            Assert.True(host.Packet.Passes[0].HasEnabledPostProcessEffects);
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.False(host.Packet.Passes[1].HasEnabledPostProcessEffects);
            }

            host.EndFrame();
        } finally {
            DisposeShader(effect);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADisposedEffectShaderIsRejectedBeforeThePassIsRecorded() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        effect.Shader.Dispose();
        Assert.Throws<ObjectDisposedException>(() => host.CreatePerspectiveCamera(postProcess: stack));
        Assert.Equal(0, host.Packet.PassCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ADisposedRenderTargetViewBindingIsRejectedBeforeThePassIsRecorded() {
        using RenderTextureService service = new();
        using FakeRendererBackend renderer = new();
        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(16, 16),
            CancellationToken.None);
        renderer.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;
        ShaderProgram program = RenderingTestAssets.CreateProgram(textures: ["sourceColor", "uTint"]);
        Shader material = program.CreateMaterial();
        using CameraTestHost host = new();
        host.BeginFrame();
        PostProcessStack stack = new();
        stack.Add(new ShaderPostProcessEffect(material));
        try {
            material.SetUniform("uTint", target.ColorTexture);
            // Disposing the render texture retires its views even while the material still holds one;
            // the capture must reject the stale binding instead of recording it.
            target.Dispose();
            Assert.Throws<ObjectDisposedException>(() => host.CreatePerspectiveCamera(postProcess: stack));
            Assert.Equal(0, host.Packet.PassCount);
        } finally {
            material.Dispose();
            RenderingTestAssets.ReleaseProgram(program);
            if (!target.IsDisposed) {
                target.Dispose();
            }
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheSourceDepthBindingIsCapturedWhenTheTargetHasDepth() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(
            RenderingTestAssets.CreateShader(textures: ["sourceColor", "sourceDepth"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        try {
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                ShaderPostProcessEffectSnapshot snapshot = Assert.IsType<ShaderPostProcessEffectSnapshot>(
                    host.Packet.Passes[0].PostProcessEffects[0]);
                Assert.Equal(0, snapshot.SourceColorBinding);
                Assert.Equal(1, snapshot.SourceDepthBinding);
                Assert.Equal(1, snapshot.InstanceRecordCount);
            }

            host.EndFrame();
        } finally {
            DisposeShader(effect);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ASourceColorOnlyEffectIsAcceptedOnADepthlessTarget() {
        using RenderTextureService service = new();
        using FakeRendererBackend renderer = new();
        Task<RenderTexture> request = service.CreateAsync(
            new RenderTextureDescription(16, 16, Depth: false),
            CancellationToken.None);
        renderer.ProcessRenderTextureRequests(service);
        RenderTexture target = await request;
        try {
            using CameraTestHost host = new();
            host.BeginFrame();
            ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
            PostProcessStack stack = new();
            stack.Add(effect);
            try {
                using (Camera camera = host.CreatePerspectiveCamera(target: target, postProcess: stack)) {
                    Assert.True(host.Packet.Passes[0].HasEnabledPostProcessEffects);
                }

                host.EndFrame();
            } finally {
                DisposeShader(effect);
            }
        } finally {
            target.Dispose();
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADuplicateEffectRunsTwice() {
        using CameraTestHost host = new();
        host.BeginFrame();
        ShaderPostProcessEffect effect = new(RenderingTestAssets.CreateShader(textures: ["sourceColor"]));
        PostProcessStack stack = new();
        stack.Add(effect);
        stack.Add(effect);
        try {
            using (Camera camera = host.CreatePerspectiveCamera(postProcess: stack)) {
                Assert.Equal(2, host.Packet.Passes[0].PostProcessEffectCount);
            }

            host.EndFrame();
        } finally {
            DisposeShader(effect);
        }
    }

    private static void DisposeShader(PostProcessEffect effect) {
        if (effect is ShaderPostProcessEffect shaderEffect && !shaderEffect.Shader.IsDisposed) {
            shaderEffect.Shader.Dispose();
        }
    }
}
