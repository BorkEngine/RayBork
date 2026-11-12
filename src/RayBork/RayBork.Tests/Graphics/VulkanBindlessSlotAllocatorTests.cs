using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Unit tests for the pure bindless slot allocator behind the M4 bindless texture table.
/// </summary>
public sealed class VulkanBindlessSlotAllocatorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void WhiteSlotIsReservedAndLiveCountIncludesIt() {
        VulkanBindlessSlotAllocator allocator = new(4);

        allocator.ReserveWhiteSlot();
        allocator.ReserveWhiteSlot();

        Assert.Equal(4u, allocator.Capacity);
        Assert.Equal(1, allocator.LiveCount);
        Assert.True(allocator.TryAllocate(out uint first));
        Assert.Equal(1u, first);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AllocationTakesTheLowestFreeSlotAndReleaseReusesIt() {
        VulkanBindlessSlotAllocator allocator = new(4);
        allocator.ReserveWhiteSlot();

        Assert.True(allocator.TryAllocate(out uint first));
        Assert.True(allocator.TryAllocate(out uint second));
        Assert.Equal(1u, first);
        Assert.Equal(2u, second);
        Assert.Equal(3, allocator.LiveCount);

        allocator.Release(first);
        Assert.Equal(2, allocator.LiveCount);
        Assert.True(allocator.TryAllocate(out uint reused));
        Assert.Equal(1u, reused);
        Assert.Equal(3, allocator.LiveCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void FullTableRejectsAllocationAndKeepsTheCapacity() {
        VulkanBindlessSlotAllocator allocator = new(2);
        allocator.ReserveWhiteSlot();
        Assert.True(allocator.TryAllocate(out uint slot));
        Assert.Equal(1u, slot);

        Assert.False(allocator.TryAllocate(out uint rejected));
        Assert.Equal(0u, rejected);
        Assert.Equal(2, allocator.LiveCount);

        allocator.Release(slot);
        Assert.True(allocator.TryAllocate(out uint reused));
        Assert.Equal(1u, reused);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleasingTheWhiteOrAnUnusedSlotIsANoOp() {
        VulkanBindlessSlotAllocator allocator = new(3);
        allocator.ReserveWhiteSlot();

        allocator.Release(0);
        allocator.Release(2);
        allocator.Release(99);

        Assert.Equal(1, allocator.LiveCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResetReleasesEverySlotIncludingWhite() {
        VulkanBindlessSlotAllocator allocator = new(3);
        allocator.ReserveWhiteSlot();
        Assert.True(allocator.TryAllocate(out _));
        Assert.True(allocator.TryAllocate(out _));
        Assert.Equal(3, allocator.LiveCount);

        allocator.Reset();

        Assert.Equal(0, allocator.LiveCount);
        Assert.True(allocator.TryAllocate(out uint slot));
        Assert.Equal(1u, slot);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroCapacityIsRejected() {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VulkanBindlessSlotAllocator(0));
    }
}
