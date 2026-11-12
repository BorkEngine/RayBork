using System.Numerics;
using System.Runtime.InteropServices;

namespace RayBork.Tests.Assets.Procedural;

public sealed class VertexTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LayoutIsSequentialAndExactlyEightyEightBytes() {
        Assert.Equal(88, Marshal.SizeOf<Vertex>());
        Assert.Equal(0, Marshal.OffsetOf<Vertex>(nameof(Vertex.Position)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<Vertex>(nameof(Vertex.Normal)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<Vertex>(nameof(Vertex.Uv)).ToInt32());
        Assert.Equal(32, Marshal.OffsetOf<Vertex>(nameof(Vertex.Uv1)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<Vertex>(nameof(Vertex.Uv2)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<Vertex>(nameof(Vertex.Uv3)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<Vertex>(nameof(Vertex.Color)).ToInt32());
        Assert.Equal(72, Marshal.OffsetOf<Vertex>(nameof(Vertex.Tangent)).ToInt32());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorUsesTheDocumentedAttributeDefaults() {
        Vertex vertex = new(new Vector3(1f, 2f, 3f));

        Assert.Equal(new Vector3(1f, 2f, 3f), vertex.Position);
        Assert.Equal(new Vector3(0f, 1f, 0f), vertex.Normal);
        Assert.Equal(Vector2.Zero, vertex.Uv);
        Assert.Equal(Vector2.Zero, vertex.Uv1);
        Assert.Equal(Vector2.Zero, vertex.Uv2);
        Assert.Equal(Vector2.Zero, vertex.Uv3);
        Assert.Equal(Color.White, vertex.Color);
        Assert.Equal(Vector4.Zero, vertex.Tangent);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorStoresEveryOptionalAttribute() {
        Vertex vertex = new(
            new Vector3(1f, 2f, 3f),
            new Vector3(0f, 0f, 1f),
            new Vector2(0.1f, 0.2f),
            new Vector2(0.3f, 0.4f),
            new Vector2(0.5f, 0.6f),
            new Vector2(0.7f, 0.8f),
            new Color(0.25f, 0.5f, 0.75f, 0.5f),
            new Vector4(1f, 0f, 0f, -1f));

        Assert.Equal(new Vector3(1f, 2f, 3f), vertex.Position);
        Assert.Equal(new Vector3(0f, 0f, 1f), vertex.Normal);
        Assert.Equal(new Vector2(0.1f, 0.2f), vertex.Uv);
        Assert.Equal(new Vector2(0.3f, 0.4f), vertex.Uv1);
        Assert.Equal(new Vector2(0.5f, 0.6f), vertex.Uv2);
        Assert.Equal(new Vector2(0.7f, 0.8f), vertex.Uv3);
        Assert.Equal(new Color(0.25f, 0.5f, 0.75f, 0.5f), vertex.Color);
        Assert.Equal(new Vector4(1f, 0f, 0f, -1f), vertex.Tangent);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DefaultStructIsAllZero() {
        Vertex vertex = default;

        Assert.Equal(Vector3.Zero, vertex.Position);
        Assert.Equal(Vector3.Zero, vertex.Normal);
        Assert.Equal(Vector4.Zero, vertex.Tangent);
        Assert.Equal(new Color(0f, 0f, 0f, 0f), vertex.Color);
    }
}
