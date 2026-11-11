using System.Numerics;

namespace RayBork.Tests;

public sealed class RectTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ConstructorStoresOriginAndSize() {
        Rect rect = new(2f, 3f, 5f, 7f);

        Assert.Equal(2f, rect.X);
        Assert.Equal(3f, rect.Y);
        Assert.Equal(5f, rect.Width);
        Assert.Equal(7f, rect.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RightAndBottomAreDerivedFromOriginAndSize() {
        Rect rect = new(-2f, 1.5f, 4f, 3.5f);

        Assert.Equal(2f, rect.Right);
        Assert.Equal(5f, rect.Bottom);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PositionAndSizeReturnVectors() {
        Rect rect = new(1f, 2f, 3f, 4f);

        Assert.Equal(new Vector2(1f, 2f), rect.Position);
        Assert.Equal(new Vector2(3f, 4f), rect.Size);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FromSizeCreatesRectangleAtOrigin() {
        Rect rect = Rect.FromSize(3f, 4f);

        Assert.Equal(new Rect(0f, 0f, 3f, 4f), rect);
        Assert.Equal(new Vector2(0f, 0f), rect.Position);
        Assert.Equal(new Vector2(3f, 4f), rect.Size);
    }

    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(5f, 2.5f, true)]
    [InlineData(9.99f, 4.99f, true)]
    [InlineData(10f, 5f, false)]
    [InlineData(10.01f, 2.5f, false)]
    [InlineData(5f, 5.01f, false)]
    [InlineData(-0.01f, 2.5f, false)]
    [InlineData(5f, -0.01f, false)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ContainsUsesInclusiveMinimumAndExclusiveMaximum(float x, float y, bool expected) {
        Rect rect = new(0f, 0f, 10f, 5f);

        Assert.Equal(expected, rect.Contains(new Vector2(x, y)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ContainsAccountsForOffsetOrigin() {
        Rect rect = new(10f, 20f, 4f, 2f);

        Assert.True(rect.Contains(new Vector2(10f, 20f)));
        Assert.True(rect.Contains(new Vector2(13.5f, 21.5f)));
        Assert.False(rect.Contains(new Vector2(14f, 21f)));
        Assert.False(rect.Contains(new Vector2(12f, 22f)));
        Assert.False(rect.Contains(new Vector2(9f, 21f)));
        Assert.False(rect.Contains(new Vector2(12f, 19f)));
    }
}
