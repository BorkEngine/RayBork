using RayBork.Internal;

namespace RayBork.Tests;

public sealed class TickSynchronizationContextTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TaskYieldContinuationRunsOnThePumpingThread() {
        using TickSynchronizationContext context = new();
        YieldProbe probe = new();
        int pumpingThreadId = Environment.CurrentManagedThreadId;
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try {
            Task task = probe.RunAsync();

            context.PumpUntilComplete(task);
        } finally {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.Equal(pumpingThreadId, probe.BeforeYieldThreadId);
        Assert.Equal(pumpingThreadId, probe.AfterYieldThreadId);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PostedCallbackDoesNotRunUntilPumped() {
        using TickSynchronizationContext context = new();
        int invocations = 0;

        context.Post(_ => invocations++, null);

        Assert.Equal(0, invocations);
        Assert.True(context.TryPumpOne());
        Assert.Equal(1, invocations);
        Assert.False(context.TryPumpOne());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PumpOneRunsAQueuedCallback() {
        using TickSynchronizationContext context = new();
        int invocations = 0;
        context.Post(_ => invocations++, null);

        context.PumpOne();

        Assert.Equal(1, invocations);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PumpUntilCompleteRunsQueuedCallbacksUntilTheTaskCompletes() {
        using TickSynchronizationContext context = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocations = 0;
        context.Post(
            _ => {
                invocations++;
                completion.SetResult();
            },
            null);

        context.PumpUntilComplete(completion.Task);

        Assert.Equal(1, invocations);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PumpUntilCompletePropagatesAFaultedTask() {
        using TickSynchronizationContext context = new();
        Task faulted = Task.FromException(new InvalidOperationException("setup failed"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => context.PumpUntilComplete(faulted));

        Assert.Equal("setup failed", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PostedCallbackExceptionPropagatesFromThePump() {
        using TickSynchronizationContext context = new();
        context.Post(static _ => throw new InvalidOperationException("callback failed"), null);

        Assert.Throws<InvalidOperationException>(() => context.PumpOne());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PostAfterDisposeThrows() {
        TickSynchronizationContext context = new();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.Post(static _ => { }, null));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void PumpAfterDisposeThrows() {
        TickSynchronizationContext context = new();
        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => context.PumpOne());
        Assert.Throws<ObjectDisposedException>(() => context.PumpUntilComplete(Task.CompletedTask));
        Assert.False(context.TryPumpOne());
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DisposeDiscardsQueuedCallbacksAndIsIdempotent() {
        TickSynchronizationContext context = new();
        int invocations = 0;
        context.Post(_ => invocations++, null);

        context.Dispose();
        context.Dispose();

        Assert.Equal(0, invocations);
        Assert.False(context.TryPumpOne());
    }

    private sealed class YieldProbe {
        public int BeforeYieldThreadId { get; private set; }

        public int AfterYieldThreadId { get; private set; }

        public async Task RunAsync() {
            BeforeYieldThreadId = Environment.CurrentManagedThreadId;
            await Task.Yield();
            AfterYieldThreadId = Environment.CurrentManagedThreadId;
        }
    }
}
