using RayBork.Internal;

namespace RayBork.Tests.Rendering;

/// <summary>
/// Tests the tone-mapping snapshot a <see cref="RenderFramePacket"/> carries from the tick thread to
/// the render thread.
/// </summary>
public sealed class RenderFramePacketTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void NewPacketCarriesTheDefaultSnapshot() {
        RenderFramePacket packet = new(1);

        Assert.Equal(TonemappingSnapshot.Default, packet.Tonemapping);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettingTheSnapshotKeepsItOnThePacket() {
        RenderFramePacket packet = new(1);

        packet.Tonemapping = new TonemappingSnapshot(2f, 4f, 2f);

        Assert.Equal(new TonemappingSnapshot(2f, 4f, 2f), packet.Tonemapping);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ResetRestoresTheDefaultSnapshotAndTheNewFrameIndex() {
        RenderFramePacket packet = new(1);
        packet.Tonemapping = new TonemappingSnapshot(2f, 4f, 2f);

        packet.Reset(2);

        Assert.Equal(2, packet.FrameIndex);
        Assert.Equal(TonemappingSnapshot.Default, packet.Tonemapping);
    }
}
