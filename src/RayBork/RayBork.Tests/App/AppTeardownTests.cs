using Microsoft.Extensions.Logging;
using RayBork.Internal;

namespace RayBork.Tests;

public sealed class AppTeardownTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TeardownActionRunsOnTheMainThreadAfterTheRenderThreadStopped() {
        int mainThreadId = Environment.CurrentManagedThreadId;
        int teardownThreadId = 0;
        bool renderThreadWasAlive = true;
        bool surfaceWasDestroyed = false;
        bool platformWasDisposed = true;

        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game);
        host.FakeRenderer.TeardownAction = () => {
            teardownThreadId = Environment.CurrentManagedThreadId;
            renderThreadWasAlive = host.App.RenderThread is { IsAlive: true };
            surfaceWasDestroyed = host.FakeRenderer.SurfaceDestroyed;
            platformWasDisposed = host.Platform.IsDisposed;
        };

        host.App.Run();

        Assert.Equal(mainThreadId, teardownThreadId);
        Assert.False(renderThreadWasAlive);
        Assert.True(surfaceWasDestroyed);
        Assert.False(platformWasDisposed);
        Assert.True(host.Platform.IsDisposed);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TeardownActionRunsAfterAStartupFailure() {
        int mainThreadId = Environment.CurrentManagedThreadId;
        int teardownThreadId = 0;
        bool renderThreadWasAlive = true;
        bool platformWasDisposed = true;

        using AppTestHost host = new(new ScriptedGame());
        host.FakeRenderer.InitializeException = new GraphicsException("renderer initialization failed.");
        host.FakeRenderer.TeardownAction = () => {
            teardownThreadId = Environment.CurrentManagedThreadId;
            renderThreadWasAlive = host.App.RenderThread is { IsAlive: true };
            platformWasDisposed = host.Platform.IsDisposed;
        };

        Assert.Throws<GraphicsException>(() => host.App.Run());

        Assert.Equal(mainThreadId, teardownThreadId);
        Assert.False(renderThreadWasAlive);
        Assert.False(platformWasDisposed);
        Assert.True(host.Platform.IsDisposed);
        Assert.Equal(1, host.FakeRenderer.DisposeCallCount);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ThrowingTeardownActionIsLoggedAndDoesNotBlockPlatformDisposal() {
        using TrackingLoggerFactory loggerFactory = new();
        ScriptedGame game = new() {
            TickHandler = context => context.RequestExit(),
        };
        using AppTestHost host = new(game, loggerFactory: loggerFactory);
        InvalidOperationException teardownException = new("teardown failed");
        host.FakeRenderer.TeardownAction = () => throw teardownException;

        Exception? thrown = Record.Exception(host.App.Run);

        Assert.Null(thrown);
        Assert.True(host.Platform.IsDisposed);
        TrackedLogEntry entry = loggerFactory.Entries.Single(tracked => ReferenceEquals(tracked.Exception, teardownException));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("teardown", entry.Message, StringComparison.OrdinalIgnoreCase);
    }
}
