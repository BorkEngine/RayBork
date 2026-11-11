namespace RayBork.Tests;

public sealed class PublicSurfaceInspectorTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void InternalBackendPropertyIsFlagged() {
        IReadOnlyList<string> violations = PublicSurfaceInspector.FindViolations(typeof(InternalBackendFixture));

        Assert.Contains(
            violations,
            violation => violation.Contains("RayBork.Internal.ProbeHandle", StringComparison.Ordinal));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void VersionPropertyIsAccepted() {
        IReadOnlyList<string> violations = PublicSurfaceInspector.FindViolations(typeof(VersionFixture));

        Assert.Empty(violations);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SilkNetBackendPropertyIsFlagged() {
        IReadOnlyList<string> violations = PublicSurfaceInspector.FindViolations(typeof(SilkNetBackendFixture));

        Assert.Contains(
            violations,
            violation => violation.Contains("Silk.NET.Vulkan", StringComparison.Ordinal));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void Sdl3BackendPropertyIsFlagged() {
        IReadOnlyList<string> violations = PublicSurfaceInspector.FindViolations(typeof(Sdl3BackendFixture));

        Assert.Contains(
            violations,
            violation => violation.Contains("SDL3", StringComparison.Ordinal));
    }
}
