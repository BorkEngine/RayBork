using System.Numerics;

namespace RayBork.Tests.Assets.Catalog;

public sealed class BoundingBoxTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void MinAndMaxRoundTripAndEqualityComparesBothCorners() {
        BoundingBox box = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));
        BoundingBox same = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));
        BoundingBox differentMin = new(new Vector3(0f, 2f, 3f), new Vector3(4f, 5f, 6f));
        BoundingBox differentMax = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 7f));

        Assert.Equal(new Vector3(1f, 2f, 3f), box.Min);
        Assert.Equal(new Vector3(4f, 5f, 6f), box.Max);
        Assert.Equal(same, box);
        Assert.Equal(same.GetHashCode(), box.GetHashCode());
        Assert.True(box == same);
        Assert.False(box != same);
        Assert.NotEqual(differentMin, box);
        Assert.NotEqual(differentMax, box);
        Assert.False(box == differentMin);
        Assert.True(box != differentMax);
    }
}
