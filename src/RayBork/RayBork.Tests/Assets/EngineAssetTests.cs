using RayBork.Internal;
using RayBork.Tests.Assets.Gpu;

namespace RayBork.Tests.Assets;

/// <summary>
/// Tests the shared <see cref="EngineAsset"/> lifetime: owner references, frame holds, the CPU-only
/// release used by <see cref="Font"/>, GPU destruction for an uploaded asset, and the exactly-once
/// teardown contract.
/// </summary>
public sealed class EngineAssetTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task ACpuOnlyReleaseWaitsForTheLastFrameHoldAndRunsExactlyOnce()
    {
        List<string> events = [];
        EngineAsset? asset = null;
        asset = new EngineAsset(
            "assets/cpu",
            resource: null,
            static _ =>
                throw new InvalidOperationException(
                    "A CPU-only asset must not queue GPU destruction."
                ),
            () => events.Add("owners-released"),
            () => events.Add(asset!.IsDisposed ? "release-disposed" : "release-not-disposed")
        );
        asset.AddOwnerReference();
        asset.AddFrameHold();

        // The release callback reads the asset state, which takes the lifetime lock; running the
        // transition on a pool thread with a bound fails loudly if the callback were invoked under a
        // lock that cannot be re-entered.
        await Task.Run(asset.ReleaseOwnerReference, TestContext.Current.CancellationToken)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.True(asset.IsDisposed);
        Assert.Equal(["owners-released"], events);

        asset.ReleaseFrameHold();

        Assert.Equal(["owners-released", "release-disposed"], events);
        Assert.Throws<InvalidOperationException>(() => asset.ReleaseFrameHold());
        Assert.Equal(2, events.Count);
        Assert.Throws<ObjectDisposedException>(() => asset.AddOwnerReference());
        Assert.Throws<ObjectDisposedException>(() => asset.AddFrameHold());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ForceDisposeRunsTheCpuOnlyReleaseExactlyOnce()
    {
        int releases = 0;
        EngineAsset asset = new(
            "assets/cpu-force",
            resource: null,
            static _ => { },
            static () => { },
            () => releases++
        );
        asset.AddOwnerReference();

        asset.ForceDispose();

        Assert.True(asset.IsDisposed);
        Assert.Equal(1, releases);

        asset.ForceDispose();
        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => asset.ReleaseOwnerReference());
        Assert.Equal(1, releases);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public async Task AGpuAssetQueuesItsDestructionAfterTheLastFrameHoldAndNeverCallsARelease()
    {
        List<string> events = [];
        FakeGpuAssetResource resource = new(CreateUpload(), handle: 1);
        EngineAsset? asset = null;
        asset = new EngineAsset(
            "assets/gpu",
            resource,
            destroyed =>
            {
                Assert.Same(resource, destroyed);
                events.Add(asset!.IsDisposed ? "destroy-disposed" : "destroy-not-disposed");
            },
            () => events.Add("owners-released")
        );
        asset.AddOwnerReference();
        asset.AddFrameHold();

        await Task.Run(asset.ReleaseOwnerReference, TestContext.Current.CancellationToken)
            .WaitAsync(_timeout, TestContext.Current.CancellationToken);

        Assert.Same(resource, asset.Resource);
        Assert.Equal(["owners-released"], events);

        asset.ReleaseFrameHold();

        Assert.Equal(["owners-released", "destroy-disposed"], events);
        Assert.Throws<InvalidOperationException>(() => asset.ReleaseFrameHold());
        Assert.Equal(2, events.Count);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ADoubleOwnerReleaseThrowsAndDoesNotDoubleRelease()
    {
        int releases = 0;
        EngineAsset asset = new(
            "assets/double",
            resource: null,
            static _ => { },
            static () => { },
            () => releases++
        );
        asset.AddOwnerReference();
        asset.AddOwnerReference();

        asset.ReleaseOwnerReference();
        Assert.False(asset.IsDisposed);
        Assert.Equal(0, releases);

        asset.ReleaseOwnerReference();
        Assert.True(asset.IsDisposed);
        Assert.Equal(1, releases);

        Assert.Throws<ObjectDisposedException>(() => asset.ReleaseOwnerReference());
        Assert.Equal(1, releases);
    }

    private static TextureGpuUpload CreateUpload()
    {
        TextureAssetDescriptor descriptor = new()
        {
            Payload = "textures/test-ktx2",
            Width = 1,
            Height = 1,
            MipLevels = 1,
            Format = TextureFormat.Rgba8Srgb,
            IsSrgb = true,
            Filter = TextureFilter.Linear,
            Wrap = TextureWrap.Clamp,
            MaxAnisotropy = 1f,
        };
        return new TextureGpuUpload("textures/test", descriptor, new byte[4]);
    }
}
