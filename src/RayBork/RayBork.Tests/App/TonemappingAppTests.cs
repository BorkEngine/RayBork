using RayBork.Internal;

namespace RayBork.Tests;

/// <summary>
/// Tests the runtime tone-mapping settings from the app seams: the builder startup defaults reach
/// <see cref="EngineContext.Tonemapping"/>, and a write during <see cref="IGame.Tick"/> is carried by
/// the frame that tick records.
/// </summary>
public sealed class TonemappingAppTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderStartupValuesInitializeTheAppLifetimeSettings() {
        ScriptedGame game = new();
        using AppTestHost host = new(
            game,
            configure: builder => builder
                .WithExposure(1.5f)
                .WithAgxWhite(4f)
                .WithAgxContrast(2f));
        game.TickHandler = context => context.RequestExit();

        host.App.Run();

        EngineContext setupContext = Assert.IsType<EngineContext>(game.SetupContext);
        EngineContext tickContext = Assert.IsType<EngineContext>(game.LastTickContext);
        Assert.Same(setupContext.Tonemapping, tickContext.Tonemapping);
        Assert.Equal(1.5f, tickContext.Tonemapping.Exposure);
        Assert.Equal(4f, tickContext.Tonemapping.AgxWhite);
        Assert.Equal(2f, tickContext.Tonemapping.AgxContrast);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void BuilderDefaultsMatchTheDocumentedDefaults() {
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        game.TickHandler = context => context.RequestExit();

        host.App.Run();

        EngineContext tickContext = Assert.IsType<EngineContext>(game.LastTickContext);
        Assert.Equal(0f, tickContext.Tonemapping.Exposure);
        Assert.Equal(16.29f, tickContext.Tonemapping.AgxWhite);
        Assert.Equal(1.25f, tickContext.Tonemapping.AgxContrast);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ChangingTheSettingsDuringTickAppliesToThatFramesSnapshot() {
        Dictionary<long, TonemappingSnapshot> submitted = [];
        Lock gate = new();
        ScriptedGame game = new();
        using AppTestHost host = new(game);
        host.FakeRenderer.OnSubmit = packet => {
            lock (gate) {
                submitted[packet.FrameIndex] = packet.Tonemapping;
            }
        };
        game.TickHandler = context => {
            // Every tick uses values that identify its own frame; the seal must see the final writes.
            context.Tonemapping.Exposure = context.FrameCount;
            context.Tonemapping.AgxWhite = context.FrameCount + 2f;
            context.Tonemapping.AgxContrast = context.FrameCount + 1f;
            if (context.FrameCount >= 10) {
                context.RequestExit();
            }
        };

        host.App.Run();

        lock (gate) {
            Assert.NotEmpty(submitted);
            Assert.All(submitted, entry => {
                float frame = entry.Key;
                Assert.Equal(new TonemappingSnapshot(frame, frame + 2f, frame + 1f), entry.Value);
            });
        }
    }
}
