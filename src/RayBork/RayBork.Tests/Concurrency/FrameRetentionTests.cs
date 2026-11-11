using RayBork.Internal;

namespace RayBork.Tests;

public sealed class FrameRetentionTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AddIsIdempotentPerTargetAndReleaseRunsOnce() {
        FrameAssetRetention retention = new();
        RecordingRetentionTarget target = new();

        retention.Add(target);
        retention.Add(target);

        Assert.Equal(1, target.Holds);
        Assert.Equal(1, retention.Count);
        Assert.False(retention.IsReleased);

        retention.Release();
        retention.Release();

        Assert.Equal(1, target.Releases);
        Assert.True(retention.IsReleased);
        Assert.Equal(0, retention.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AddAfterReleaseIsIgnored() {
        FrameAssetRetention retention = new();
        retention.Release();
        RecordingRetentionTarget target = new();

        retention.Add(target);

        Assert.Equal(0, target.Holds);
        Assert.Equal(0, target.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ReleaseCallsEveryTargetExactlyOnce() {
        FrameAssetRetention retention = new();
        RecordingRetentionTarget first = new();
        RecordingRetentionTarget second = new();
        retention.Add(first);
        retention.Add(second);

        retention.Release();

        Assert.Equal(1, first.Holds);
        Assert.Equal(1, first.Releases);
        Assert.Equal(1, second.Holds);
        Assert.Equal(1, second.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TargetsAreHeldByReferenceNotByEquality() {
        FrameAssetRetention retention = new();
        EqualRetentionTarget first = new();
        EqualRetentionTarget second = new();

        retention.Add(first);
        retention.Add(second);

        Assert.Equal(2, retention.Count);
        Assert.Equal(1, first.Holds);
        Assert.Equal(1, second.Holds);

        retention.Release();

        Assert.Equal(1, first.Releases);
        Assert.Equal(1, second.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ATargetAddedToTwoFramesGetsOneHoldPerFrame() {
        FrameRing ring = new();
        FrameAssetRetention retention = new();
        RecordingRetentionTarget target = new();
        retention.Add(target);

        Assert.True(ring.TryAcquireWritable(out FrameLease first));
        ring.AttachRetention(first, retention);
        Assert.True(ring.TryAcquireWritable(out FrameLease second));
        FrameAssetRetention otherRetention = new();
        ring.AttachRetention(second, otherRetention);
        otherRetention.Add(target);
        ring.Publish(first);
        ring.Publish(second);

        Assert.Equal(2, target.Holds);
        Assert.Equal(1, target.Releases);

        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);
        retention.Release();

        Assert.Equal(2, target.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AReadyFrameKeepsItsHoldUntilItIsReleased() {
        FrameRing ring = new();
        FrameAssetRetention retention = new();
        RecordingRetentionTarget target = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.AttachRetention(lease, retention);
        retention.Add(target);
        ring.Publish(lease);

        Assert.Equal(0, target.Releases);

        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        Assert.Equal(0, target.Releases);

        ring.Release(rendering);
        Assert.Equal(1, target.Releases);
        Assert.Equal(1, target.Holds);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PublishingANewerFrameReleasesTheSupersededHold() {
        FrameRing ring = new();
        FrameAssetRetention firstRetention = new();
        RecordingRetentionTarget firstTarget = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease first));
        ring.AttachRetention(first, firstRetention);
        firstRetention.Add(firstTarget);
        ring.Publish(first);
        FrameAssetRetention secondRetention = new();
        RecordingRetentionTarget secondTarget = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease second));
        ring.AttachRetention(second, secondRetention);
        secondRetention.Add(secondTarget);

        ring.Publish(second);

        Assert.Equal(1, firstTarget.Releases);
        Assert.Equal(0, secondTarget.Releases);

        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);

        Assert.Equal(1, firstTarget.Releases);
        Assert.Equal(1, secondTarget.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AbandonReleasesTheHold() {
        FrameRing ring = new();
        FrameAssetRetention retention = new();
        RecordingRetentionTarget target = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.AttachRetention(lease, retention);
        retention.Add(target);

        ring.Abandon(lease);

        Assert.Equal(1, target.Releases);
        Assert.False(ring.TryAcquireNewest(out _));

        retention.Release();
        Assert.Equal(1, target.Releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void CompleteKeepsTheReadyFrameHoldUntilTheRingIsDrained() {
        FrameRing ring = new();
        FrameAssetRetention retention = new();
        RecordingRetentionTarget target = new();
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.AttachRetention(lease, retention);
        retention.Add(target);
        ring.Publish(lease);

        ring.Complete();

        Assert.Equal(0, target.Releases);
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);

        Assert.Equal(1, target.Releases);
        Assert.False(ring.TryAcquireNewest(out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void AttachRetentionRequiresARecordingLeaseAndAcceptsOneSet() {
        FrameRing ring = new();
        FrameAssetRetention retention = new();

        Assert.Throws<InvalidOperationException>(() => ring.AttachRetention(default, retention));
        Assert.True(ring.TryAcquireWritable(out FrameLease lease));
        ring.AttachRetention(lease, retention);
        Assert.Throws<InvalidOperationException>(() => ring.AttachRetention(lease, new FrameAssetRetention()));

        ring.Publish(lease);
        Assert.Throws<InvalidOperationException>(() => ring.AttachRetention(lease, new FrameAssetRetention()));
        Assert.True(ring.TryAcquireNewest(out FrameLease rendering));
        ring.Release(rendering);
    }

    private class RecordingRetentionTarget : IAssetRetentionTarget {
        private int _holds;
        private int _releases;

        internal int Holds => Volatile.Read(ref _holds);

        internal int Releases => Volatile.Read(ref _releases);

        public void AddFrameHold() => Interlocked.Increment(ref _holds);

        public void ReleaseFrameHold() => Interlocked.Increment(ref _releases);
    }

    private sealed class EqualRetentionTarget : RecordingRetentionTarget {
        public override bool Equals(object? obj) => obj is EqualRetentionTarget;

        public override int GetHashCode() => 0;
    }
}
