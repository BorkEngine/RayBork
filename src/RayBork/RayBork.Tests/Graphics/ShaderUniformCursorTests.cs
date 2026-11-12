using System.Buffers.Binary;
using System.Numerics;
using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests;

public sealed class ShaderUniformCursorTests {
    private const string FLOAT_UNIFORM = "uFloat";
    private const string INT_UNIFORM = "uInt";
    private const string UINT_UNIFORM = "uUint";
    private const string BOOL_UNIFORM = "uBool";
    private const string VEC2_UNIFORM = "uVec2";
    private const string VEC3_UNIFORM = "uVec3";
    private const string VEC4_UNIFORM = "uVec4";
    private const string MAT4_UNIFORM = "uMat4";
    private const string COLOR_UNIFORM = "uColor";
    private const string EMPTY_UNIFORM = "uEmpty";
    private const string MAIN_TEXTURE = "mainTexture";
    private const string DETAIL_TEXTURE = "detailTexture";

    private static readonly string[] _textureNames = [MAIN_TEXTURE, DETAIL_TEXTURE];

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MetadataAndRecordSizesMatchTheBakedDescriptor() {
        using Shader shader = CreateShader();

        Assert.Equal(10, shader.Uniforms.Count);
        Assert.Equal(
            [MAIN_TEXTURE, DETAIL_TEXTURE],
            shader.Textures.Select(texture => texture.Name).ToArray());
        Assert.Equal(ShaderStageMask.Vertex | ShaderStageMask.Fragment, shader.Stages);
        Assert.Equal(RenderState.Opaque, shader.DefaultRenderState);
        Assert.Equal(256, shader.InstanceDataSize);
        Assert.Equal(256 - 96, shader.InstanceData.Length);
        Assert.Equal(shader.InstanceData.Length, shader.DefaultInstanceData.Length);
        Assert.Equal(2, shader.TextureValues.Length);
        Assert.Equal(2, shader.DefaultTextureValues.Length);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TheCursorStartsFromTheBakedDefaults() {
        using Shader shader = CreateShader();

        Assert.Equal([1.5f], ReadFloats(ValueOf(shader, FLOAT_UNIFORM)));
        Assert.Equal(-2, ReadInt32(ValueOf(shader, INT_UNIFORM)));
        Assert.Equal(3u, ReadUInt32(ValueOf(shader, UINT_UNIFORM)));
        Assert.Equal(1u, ReadUInt32(ValueOf(shader, BOOL_UNIFORM)));
        Assert.Equal([1f, 2f], ReadFloats(ValueOf(shader, VEC2_UNIFORM)));
        Assert.Equal([3f, 4f, 5f], ReadFloats(ValueOf(shader, VEC3_UNIFORM)));
        Assert.Equal([6f, 7f, 8f, 9f], ReadFloats(ValueOf(shader, VEC4_UNIFORM)));
        Assert.Equal(
            [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f],
            ReadFloats(ValueOf(shader, MAT4_UNIFORM)));
        Assert.Equal([0.25f, 0.5f, 0.75f, 1f], ReadFloats(ValueOf(shader, COLOR_UNIFORM)));
        Assert.Equal([0f], ReadFloats(ValueOf(shader, EMPTY_UNIFORM)));
        Assert.All(shader.TextureValues.ToArray(), Assert.Null);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void EverySetUniformOverloadWritesLittleEndianComponents() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            shader.SetUniform(FLOAT_UNIFORM, 9.25f);
            shader.SetUniform(INT_UNIFORM, -7);
            shader.SetUniform(UINT_UNIFORM, 42u);
            shader.SetUniform(BOOL_UNIFORM, true);
            shader.SetUniform(VEC2_UNIFORM, new Vector2(1.5f, 2.5f));
            shader.SetUniform(VEC3_UNIFORM, new Vector3(3.5f, 4.5f, 5.5f));
            shader.SetUniform(VEC4_UNIFORM, new Vector4(6.5f, 7.5f, 8.5f, 9.5f));
            shader.SetUniform(
                MAT4_UNIFORM,
                new Matrix4x4(
                    17f, 18f, 19f, 20f,
                    21f, 22f, 23f, 24f,
                    25f, 26f, 27f, 28f,
                    29f, 30f, 31f, 32f));
            shader.SetUniform(COLOR_UNIFORM, new Color(0.1f, 0.2f, 0.3f, 0.4f));
        });

        Assert.Equal([9.25f], ReadFloats(ValueOf(shader, FLOAT_UNIFORM)));
        Assert.Equal(-7, ReadInt32(ValueOf(shader, INT_UNIFORM)));
        Assert.Equal(42u, ReadUInt32(ValueOf(shader, UINT_UNIFORM)));
        Assert.Equal(1u, ReadUInt32(ValueOf(shader, BOOL_UNIFORM)));
        Assert.Equal([1.5f, 2.5f], ReadFloats(ValueOf(shader, VEC2_UNIFORM)));
        Assert.Equal([3.5f, 4.5f, 5.5f], ReadFloats(ValueOf(shader, VEC3_UNIFORM)));
        Assert.Equal([6.5f, 7.5f, 8.5f, 9.5f], ReadFloats(ValueOf(shader, VEC4_UNIFORM)));
        Assert.Equal(
            [17f, 18f, 19f, 20f, 21f, 22f, 23f, 24f, 25f, 26f, 27f, 28f, 29f, 30f, 31f, 32f],
            ReadFloats(ValueOf(shader, MAT4_UNIFORM)));
        Assert.Equal(
            [0.1f, 0.2f, 0.3f, 0.4f],
            ReadFloats(ValueOf(shader, COLOR_UNIFORM)));

        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(shader.InstanceData));
        Assert.Equal(0f, BinaryPrimitives.ReadSingleLittleEndian(shader.InstanceData.Slice(4, 4)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void QuaternionMapsToVec4() {
        using Shader shader = CreateShader();
        OnTickThread(() => shader.SetUniform(COLOR_UNIFORM, new Quaternion(0.1f, 0.2f, 0.3f, 0.4f)));

        Assert.Equal([0.1f, 0.2f, 0.3f, 0.4f], ReadFloats(ValueOf(shader, COLOR_UNIFORM)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BooleanMapsToUintZeroOrOne() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            shader.SetUniform(BOOL_UNIFORM, true);
            Assert.Equal(1u, ReadUInt32(ValueOf(shader, BOOL_UNIFORM)));

            shader.SetUniform(BOOL_UNIFORM, false);
            Assert.Equal(0u, ReadUInt32(ValueOf(shader, BOOL_UNIFORM)));
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void IntegerComponentsRoundTripExactly() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            shader.SetUniform(INT_UNIFORM, int.MinValue);
            shader.SetUniform(UINT_UNIFORM, uint.MaxValue);
        });

        Assert.Equal(unchecked((uint)int.MinValue), ReadUInt32(ValueOf(shader, INT_UNIFORM)));
        Assert.Equal(uint.MaxValue, ReadUInt32(ValueOf(shader, UINT_UNIFORM)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void UnknownNameThrowsArgumentException() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            Assert.Throws<ArgumentException>(() => shader.SetUniform("uMissing", 1f));
            Assert.Throws<ArgumentException>(() => shader.SetUniform("uMissing", null));
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MismatchedValueTypeThrowsArgumentException() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            Assert.Throws<ArgumentException>(() => shader.SetUniform(FLOAT_UNIFORM, 1));
            Assert.Throws<ArgumentException>(() => shader.SetUniform(VEC2_UNIFORM, new Vector3(1f, 2f, 3f)));
            Assert.Throws<ArgumentException>(() => shader.SetUniform(MAT4_UNIFORM, new Vector4()));
            Assert.Throws<ArgumentException>(() => shader.SetUniform(MAIN_TEXTURE, 1f));
            Assert.Throws<ArgumentException>(() => shader.SetUniform(FLOAT_UNIFORM, (Texture?)null));
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResetRestoresDefaultsAndClearsTextures() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture texture = CreateTexture("textures/reset", destroyed);
        try {
            OnTickThread(() => {
                shader.SetUniform(FLOAT_UNIFORM, 9f);
                shader.SetUniform(MAIN_TEXTURE, texture);
                Assert.Same(texture, shader.TextureValues[0]);

                texture.Dispose();
                Assert.False(texture.IsDisposed);

                shader.ResetUniforms();

                Assert.Equal([1.5f], ReadFloats(ValueOf(shader, FLOAT_UNIFORM)));
                Assert.All(shader.TextureValues.ToArray(), Assert.Null);
            });

            Assert.True(texture.IsDisposed);
            Assert.Single(destroyed);
        } finally {
            DisposeShader(shader);
            DisposeTexture(texture);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void OverwritingATextureReleasesThePreviousOwnerReferenceExactlyOnce() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture first = CreateTexture("textures/first", destroyed);
        Texture second = CreateTexture("textures/second", destroyed);
        try {
            OnTickThread(() => {
                shader.SetUniform(MAIN_TEXTURE, first);
                first.Dispose();
                Assert.False(first.IsDisposed);

                shader.SetUniform(MAIN_TEXTURE, second);
                Assert.True(first.IsDisposed);
                Assert.Single(destroyed);
                Assert.Same(second, shader.TextureValues[0]);

                shader.SetUniform(MAIN_TEXTURE, second);
                Assert.False(second.IsDisposed);
            });

            DisposeShader(shader);
            second.Dispose();
            Assert.True(second.IsDisposed);
            Assert.Equal(2, destroyed.Count);
        } finally {
            DisposeShader(shader);
            DisposeTexture(first);
            DisposeTexture(second);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposingTheShaderReleasesTheCursorTextureOwnerReference() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture texture = CreateTexture("textures/dispose", destroyed);
        try {
            OnTickThread(() => shader.SetUniform(MAIN_TEXTURE, texture));
            texture.Dispose();
            Assert.False(texture.IsDisposed);
            Assert.Empty(destroyed);

            shader.Dispose();

            Assert.True(shader.IsDisposed);
            Assert.True(texture.IsDisposed);
            Assert.Single(destroyed);
        } finally {
            DisposeShader(shader);
            DisposeTexture(texture);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ATextureDisposedBeforeTheCallThrowsObjectDisposedException() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture texture = CreateTexture("textures/stale", destroyed);
        texture.Dispose();
        try {
            OnTickThread(() => {
                Assert.Throws<ObjectDisposedException>(
                    () => shader.SetUniform(MAIN_TEXTURE, texture));
            });
        } finally {
            DisposeShader(shader);
            DisposeTexture(texture);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NullTextureRecordsTheWhiteFallbackWithoutAReference() {
        using Shader shader = CreateShader();
        OnTickThread(() => {
            shader.SetUniform(MAIN_TEXTURE, null);
            Assert.Null(shader.TextureValues[0]);
        });
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultCaptureIsStableAndOwnsNoTextureReference() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture texture = CreateTexture("textures/default", destroyed);
        try {
            byte[] defaults = shader.DefaultInstanceData.ToArray();
            Assert.All(shader.DefaultTextureValues.ToArray(), Assert.Null);

            texture.Dispose();
            Assert.True(texture.IsDisposed);
            Assert.Single(destroyed);

            OnTickThread(() => {
                shader.SetUniform(FLOAT_UNIFORM, 99f);
                shader.SetUniform(VEC4_UNIFORM, new Vector4(4f, 3f, 2f, 1f));

                Assert.Equal(defaults, shader.DefaultInstanceData.ToArray());
                Assert.All(shader.DefaultTextureValues.ToArray(), Assert.Null);
            });
        } finally {
            DisposeShader(shader);
            DisposeTexture(texture);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetUniformRequiresTheTickThread() {
        using Shader shader = CreateShader();

        Assert.False(TickThread.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => shader.SetUniform(FLOAT_UNIFORM, 1f));
        Assert.Throws<InvalidOperationException>(() => shader.ResetUniforms());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SetUniformFromAnotherThreadThrowsInvalidOperationException() {
        using Shader shader = CreateShader();
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                shader.SetUniform(FLOAT_UNIFORM, 1f);
            } catch (Exception exception) {
                failure = exception;
            }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposedShaderThrowsObjectDisposedException() {
        Shader shader = CreateShader();
        shader.Dispose();

        OnTickThread(() => {
            Assert.Throws<ObjectDisposedException>(() => shader.SetUniform(FLOAT_UNIFORM, 1f));
            Assert.Throws<ObjectDisposedException>(() => shader.ResetUniforms());
            Assert.Throws<ObjectDisposedException>(() => shader.SetUniform(MAIN_TEXTURE, null));
        });
        Assert.Throws<ObjectDisposedException>(shader.Dispose);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeFromAnotherThreadReleasesTheCursorExactlyOnce() {
        var destroyed = new List<GpuAssetResource>();
        Shader shader = CreateShader();
        Texture texture = CreateTexture("textures/threaded", destroyed);
        try {
            OnTickThread(() => shader.SetUniform(MAIN_TEXTURE, texture));
            texture.Dispose();

            var thread = new Thread(shader.Dispose);
            thread.Start();
            thread.Join();

            Assert.True(shader.IsDisposed);
            Assert.True(texture.IsDisposed);
            Assert.Single(destroyed);
        } finally {
            DisposeTexture(texture);
        }
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TickThreadAttachAndDetachAreBalanced() {
        Assert.False(TickThread.IsCurrent);
        TickThread.Attach();
        try {
            Assert.True(TickThread.IsCurrent);
            Assert.Throws<InvalidOperationException>(TickThread.Attach);
        } finally {
            TickThread.Detach();
        }

        Assert.False(TickThread.IsCurrent);
        Assert.Throws<InvalidOperationException>(TickThread.Detach);
    }

    private static void OnTickThread(Action action) {
        TickThread.Attach();
        try {
            action();
        } finally {
            TickThread.Detach();
        }
    }

    private static ReadOnlySpan<byte> ValueOf(Shader shader, string name) {
        ShaderAbiUniform uniform = shader.Abi.Uniforms.First(candidate => candidate.Name == name);
        return shader.InstanceData.Slice(
            uniform.Offset - ShaderAbiLayout.INSTANCE_HEADER_SIZE,
            uniform.Size);
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> bytes) {
        var values = new float[bytes.Length / sizeof(float)];
        for (int index = 0; index < values.Length; index++) {
            values[index] = BinaryPrimitives.ReadSingleLittleEndian(
                bytes.Slice(index * sizeof(float), sizeof(float)));
        }

        return values;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadUInt32LittleEndian(bytes);

    private static int ReadInt32(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes);

    private static Shader CreateShader() {
        ShaderAssetDescriptor descriptor = new() {
            VertexPayload = "shaders/test-vert",
            FragmentPayload = "shaders/test-frag",
            VertexEntry = "main_vertex",
            FragmentEntry = "main_fragment",
            Stages = ShaderStageMask.Vertex | ShaderStageMask.Fragment,
            DefaultRenderState = RenderState.Opaque,
            Uniforms = [
                Uniform(FLOAT_UNIFORM, ShaderUniformType.Float, 1.5f),
                Uniform(INT_UNIFORM, ShaderUniformType.Int, -2f),
                Uniform(UINT_UNIFORM, ShaderUniformType.UInt, 3f),
                Uniform(BOOL_UNIFORM, ShaderUniformType.Bool, 1f),
                Uniform(VEC2_UNIFORM, ShaderUniformType.Vector2, 1f, 2f),
                Uniform(VEC3_UNIFORM, ShaderUniformType.Vector3, 3f, 4f, 5f),
                Uniform(VEC4_UNIFORM, ShaderUniformType.Vector4, 6f, 7f, 8f, 9f),
                Uniform(
                    MAT4_UNIFORM,
                    ShaderUniformType.Matrix4x4,
                    1f, 2f, 3f, 4f,
                    5f, 6f, 7f, 8f,
                    9f, 10f, 11f, 12f,
                    13f, 14f, 15f, 16f),
                Uniform(COLOR_UNIFORM, ShaderUniformType.Vector4, 0.25f, 0.5f, 0.75f, 1f),
                Uniform(EMPTY_UNIFORM, ShaderUniformType.Float),
            ],
            Textures = _textureNames,
        };
        var upload = new ShaderGpuUpload(
            "shaders/test",
            descriptor,
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty);
        var asset = new EngineAsset(
            "shaders/test",
            new FakeGpuAssetResource(upload, 1),
            static _ => { },
            static () => { });
        asset.AddOwnerReference();
        ShaderProgram program = new(asset, descriptor);
        Shader material = program.CreateMaterial();
        // Transfer the helper's catalog-style reference to the material.
        program.Lifetime.ReleaseOwnerReference();
        return material;
    }

    private static ShaderUniformAsset Uniform(
        string name,
        ShaderUniformType type,
        params float[] defaultValue) => new() {
            Name = name,
            Type = type,
            Default = defaultValue,
        };

    private static Texture CreateTexture(string path, List<GpuAssetResource> destroyed) {
        var descriptor = new TextureAssetDescriptor {
            Payload = path + "-ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Srgb,
            IsSrgb = true,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = 1f,
        };
        var upload = new TextureGpuUpload(path, descriptor, ReadOnlyMemory<byte>.Empty);
        var asset = new EngineAsset(
            path,
            new FakeGpuAssetResource(upload, 1),
            destroyed.Add,
            static () => { });
        asset.AddOwnerReference();
        return new Texture(asset, descriptor);
    }

    private static void DisposeShader(Shader shader) {
        if (!shader.IsDisposed) {
            shader.Dispose();
        }
    }

    private static void DisposeTexture(Texture texture) {
        if (!texture.IsDisposed) {
            texture.Dispose();
        }
    }
}
