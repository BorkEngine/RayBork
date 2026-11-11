using RayBork.Internal;

namespace RayBork.Tests;

public sealed class FrameRingTests {
    private const int SLOT_COUNT = 3;
    private static readonly TimeSpan _blockingTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AcquireWritableFillsThreeSlotsThenFails() {
        FrameRing ring = new();

        FrameLease[] leases = AcquireAllWritable(ring);

        Assert.False(ring.TryAcquireWritable(out FrameLease overflow));
        Assert.Equal(0, overflow.SlotIndex);
        Assert.Equal(0, overflow.FrameIndex);
        HashSet<int> slots = [.. leases.Select(lease => lease.SlotIndex)];
        Assert.Equal(SLOT_COUNT, slots.Count);
        Assert.True(leases[0].FrameIndex < leases[1].FrameIndex);
        Assert.True(leases[1].FrameIndex < leases[2].FrameIndex);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BackpressureAppliesOnlyWhenAllThreeSlotsAreOccupied() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease renderingLease));
        ring.Publish(renderingLease);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        Assert.True(ring.TryAcquireWritable(out FrameLease readyLease));
        ring.Publish(readyLease);
        Assert.True(ring.TryAcquireWritable(out FrameLease recordingLease));

        Assert.False(ring.TryAcquireWritable(out _));

        ring.Abandon(recordingLease);
        Assert.True(ring.TryAcquireWritable(out FrameLease reused));
        Assert.True(reused.FrameIndex > recordingLease.FrameIndex);

        ring.Release(rendering);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishSupersedesAnOlderReadyFrame() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease first));
        ring.Publish(first);
        Assert.True(ring.TryAcquireWritable(out FrameLease second));
        ring.Publish(second);

        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));

        Assert.Equal(second.SlotIndex, rendering.SlotIndex);
        Assert.Equal(second.FrameIndex, rendering.FrameIndex);
        Assert.True(ring.TryAcquireWritable(out FrameLease reused));
        Assert.Equal(first.SlotIndex, reused.SlotIndex);

        ring.Abandon(reused);
        ring.Release(rendering);
        Assert.False(ring.TryAcquireNewest(out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleaseReturnsTheRenderingSlotToTheFreePool() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));

        Assert.False(ring.TryAcquireNewest(out _));
        ring.Release(rendering);
        Assert.True(ring.TryAcquireWritable(out FrameLease next));

        Assert.Equal(lease.SlotIndex, next.SlotIndex);
        Assert.True(next.FrameIndex > lease.FrameIndex);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AbandonReturnsARecordingSlotWithoutPublishing() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));

        ring.Abandon(lease);

        Assert.False(ring.TryAcquireNewest(out _));
        Assert.True(ring.TryAcquireWritable(out FrameLease reused));
        Assert.Equal(lease.SlotIndex, reused.SlotIndex);
        Assert.True(reused.FrameIndex > lease.FrameIndex);
        Assert.Throws<InvalidOperationException>(() => ring.Publish(lease));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompleteDrainsReadyFramesBeforeBlockingAcquireNewestReturnsNull() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);

        ring.Complete();
        ring.Complete();

        Assert.False(ring.TryAcquireWritable(out _));
        Assert.Null(ring.AcquireWritable(CancellationToken.None));

        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        Assert.Equal(lease.SlotIndex, rendering.SlotIndex);
        ring.Release(rendering);

        Assert.False(ring.TryAcquireNewest(out _));
        Assert.Null(ring.AcquireNewest(CancellationToken.None));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompleteWithoutAFrameReturnsNullFromBothBlockingAcquires() {
        FrameRing ring = new();
        ring.Complete();

        Assert.False(ring.TryAcquireWritable(out _));
        Assert.False(ring.TryAcquireNewest(out _));
        Assert.Null(ring.AcquireWritable(CancellationToken.None));
        Assert.Null(ring.AcquireNewest(CancellationToken.None));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CancellationWinsOverCompletionForBlockingAcquires() {
        FrameRing ring = new();
        ring.Complete();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ring.AcquireWritable(cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => ring.AcquireNewest(cancellation.Token));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishRejectsADefaultLease() {
        FrameRing ring = new();

        Assert.Throws<InvalidOperationException>(() => ring.Publish(default));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishRejectsAPublishedLease() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);

        Assert.Throws<InvalidOperationException>(() => ring.Publish(lease));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishRejectsARenderingLease() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);
        Assert.True(ring.TryAcquireNewest(out _));

        Assert.Throws<InvalidOperationException>(() => ring.Publish(lease));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleaseRejectsADefaultLease() {
        FrameRing ring = new();

        Assert.Throws<InvalidOperationException>(() => ring.Release(default));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleaseRejectsAReadyLease() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);

        Assert.Throws<InvalidOperationException>(() => ring.Release(lease));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleaseRejectsAReleasedLease() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);

        Assert.Throws<InvalidOperationException>(() => ring.Release(rendering));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AbandonRejectsAPublishedLease() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);

        Assert.Throws<InvalidOperationException>(() => ring.Abandon(lease));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void LeaseWithAnOutOfRangeSlotThrows() {
        FrameRing ring = new();

        Assert.Throws<InvalidOperationException>(() => ring.Publish(new FrameLease(-1, 1)));
        Assert.Throws<InvalidOperationException>(() => ring.Release(new FrameLease(SLOT_COUNT, 1)));
        Assert.Throws<InvalidOperationException>(() => ring.Abandon(new FrameLease(SLOT_COUNT, 1)));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void StaleLeaseAfterSlotReuseThrows() {
        FrameRing ring = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease first));
        ring.Publish(first);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);
        Assert.True(ring.TryAcquireWritable(out FrameLease reused));

        Assert.Equal(first.SlotIndex, reused.SlotIndex);
        Assert.NotEqual(first.FrameIndex, reused.FrameIndex);
        Assert.Throws<InvalidOperationException>(() => ring.Publish(first));
        Assert.Throws<InvalidOperationException>(() => ring.Abandon(first));

        ring.Abandon(reused);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BlockingAcquireWritableWakesWhenASlotIsReleased() {
        FrameRing ring = new();
        FrameLease[] leases = AcquireAllWritable(ring);
        Task<FrameLease?> blocked = Task.Run(() => ring.AcquireWritable(CancellationToken.None));

        ring.Publish(leases[0]);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);

        FrameLease? acquired = await blocked.WaitAsync(_blockingTimeout, TestContext.Current.CancellationToken);

        Assert.True(acquired.HasValue);
        Assert.Equal(leases[0].SlotIndex, acquired.Value.SlotIndex);
        Assert.True(acquired.Value.FrameIndex > leases[0].FrameIndex);
        ring.Abandon(acquired.Value);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BlockingAcquireNewestWakesWhenAFrameIsPublished() {
        FrameRing ring = new();
        Task<FrameLease?> blocked = Task.Run(() => ring.AcquireNewest(CancellationToken.None));

        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.Publish(lease);

        FrameLease? acquired = await blocked.WaitAsync(_blockingTimeout, TestContext.Current.CancellationToken);

        Assert.True(acquired.HasValue);
        Assert.Equal(lease.SlotIndex, acquired.Value.SlotIndex);
        Assert.Equal(lease.FrameIndex, acquired.Value.FrameIndex);
        ring.Release(acquired.Value);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CompleteWakesABlockedWritableAcquisitionWithNull() {
        FrameRing ring = new();
        AcquireAllWritable(ring);
        Task<FrameLease?> blocked = Task.Run(() => ring.AcquireWritable(CancellationToken.None));

        ring.Complete();

        FrameLease? acquired = await blocked.WaitAsync(_blockingTimeout, TestContext.Current.CancellationToken);

        Assert.Null(acquired);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task CompleteWakesABlockedNewestAcquisitionWithNull() {
        FrameRing ring = new();
        Task<FrameLease?> blocked = Task.Run(() => ring.AcquireNewest(CancellationToken.None));

        ring.Complete();

        FrameLease? acquired = await blocked.WaitAsync(_blockingTimeout, TestContext.Current.CancellationToken);

        Assert.Null(acquired);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task BlockingAcquireWritableHonorsCancellation() {
        using CancellationTokenSource cancellation = new();
        FrameRing ring = new();
        AcquireAllWritable(ring);
        Task<FrameLease?> blocked = Task.Run(() => ring.AcquireWritable(cancellation.Token));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => blocked.WaitAsync(_blockingTimeout, TestContext.Current.CancellationToken));
    }

    private static FrameLease[] AcquireAllWritable(FrameRing ring) {
        FrameLease[] leases = new FrameLease[SLOT_COUNT];
        for (int i = 0; i < leases.Length; i++) {
            Assert.True(ring.TryAcquireWritable(out leases[i]));
        }
        return leases;
    }
}
