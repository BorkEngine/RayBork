using Microsoft.Extensions.Logging.Abstractions;
using RayBork.Internal;

namespace RayBork.Tests.Assets.Catalog;

/// <summary>
/// Tests the built-in engine asset formats and their registration in the catalog registry.
/// </summary>
public sealed class BuiltInAssetLoaderTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CreateDefaultRegistersTheEngineFormats() {
        AssetTypeRegistry registry = new(BuiltInAssetLoaders.CreateDefault());

        Assert.Equal(3, registry.Count);
        Assert.True(registry.Contains(typeof(Mesh)));
        Assert.True(registry.Contains(typeof(Texture)));
        Assert.True(registry.Contains(typeof(ShaderProgram)));

        EngineAssetTypeRegistration<Mesh> mesh = Assert.IsType<EngineAssetTypeRegistration<Mesh>>(
            registry.Get(typeof(Mesh)));
        Assert.Equal(AssetEntryType.Mesh, mesh.EntryType);
        Assert.Same(MeshAssetFormat.Instance, mesh.Format);

        EngineAssetTypeRegistration<Texture> texture = Assert.IsType<EngineAssetTypeRegistration<Texture>>(
            registry.Get(typeof(Texture)));
        Assert.Equal(AssetEntryType.Texture, texture.EntryType);
        Assert.Same(TextureAssetFormat.Instance, texture.Format);

        EngineAssetTypeRegistration<ShaderProgram> program =
            Assert.IsType<EngineAssetTypeRegistration<ShaderProgram>>(registry.Get(typeof(ShaderProgram)));
        Assert.Equal(AssetEntryType.Shader, program.EntryType);
        Assert.Same(ShaderProgramAssetFormat.Instance, program.Format);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ACatalogRejectsAUserRegistrationForABuiltInEngineFormat() {
        GpuAssetBridge bridge = new();
        try {
            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => new AssetCatalog(
                    NullLoggerFactory.Instance,
                    bridge,
                    ReadOnlyMemory<byte>.Empty,
                    [],
                    [new LoaderAssetTypeRegistration<Mesh>(new MeshLoader())]));

            Assert.Equal(
                $"The asset type '{typeof(Mesh).FullName}' is registered more than once.",
                thrown.Message);
        } finally {
            bridge.Close(null);
        }
    }

    private sealed class MeshLoader : IAssetLoader<Mesh> {
        public AssetEntryType EntryType => AssetEntryType.Mesh;

        public Task<Mesh> LoadAsync(AssetLoadContext context)
            => throw new NotSupportedException("The test loader never loads a mesh.");
    }
}
