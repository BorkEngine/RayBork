using RayBork.Internal;

namespace RayBork.Tests;

public sealed class WindowCommandQueueTests {
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepeatedSettersCoalesceInPlaceAndKeepTheirQueuePosition() {
        WindowCommandQueue queue = new();
        queue.EnqueueSetSize(100, 100);
        queue.EnqueueMinimize();
        queue.EnqueueSetTitle("First");
        queue.EnqueueSetSize(200, 150);
        queue.EnqueueSetTitle("Second");

        IReadOnlyList<WindowCommand> pending = queue.PendingCommands;

        Assert.Equal(3, pending.Count);
        SetSizeCommand size = Assert.IsType<SetSizeCommand>(pending[0]);
        Assert.Equal(200, size.Width);
        Assert.Equal(150, size.Height);
        Assert.IsType<MinimizeCommand>(pending[1]);
        SetTitleCommand title = Assert.IsType<SetTitleCommand>(pending[2]);
        Assert.Equal("Second", title.Title);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void DifferentSetterKindsDoNotCoalesceWithEachOther() {
        WindowCommandQueue queue = new();
        queue.EnqueueSetMinSize(10, 20);
        queue.EnqueueSetMaxSize(30, 40);
        queue.EnqueueSetPosition(1, 2);
        queue.EnqueueSetMonitor(0);
        queue.EnqueueSetCursor(CursorMode.Hidden);
        queue.EnqueueSetFullscreen(true);
        queue.EnqueueSetBorderless(true);

        Assert.Equal(7, queue.PendingCommands.Count);

        queue.EnqueueSetMinSize(11, 21);
        Assert.Equal(7, queue.PendingCommands.Count);
        SetMinSizeCommand minimum = Assert.IsType<SetMinSizeCommand>(queue.PendingCommands[0]);
        Assert.Equal(11, minimum.Width);
        Assert.Equal(21, minimum.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ActionsKeepTheirQueueOrderAndDoNotCoalesce() {
        WindowCommandQueue queue = new();
        queue.EnqueueMinimize();
        queue.EnqueueMaximize();
        queue.EnqueueRestore();
        queue.EnqueueClose();
        queue.EnqueueMinimize();

        IReadOnlyList<WindowCommand> pending = queue.PendingCommands;

        Assert.Equal(5, pending.Count);
        Assert.IsType<MinimizeCommand>(pending[0]);
        Assert.IsType<MaximizeCommand>(pending[1]);
        Assert.IsType<RestoreCommand>(pending[2]);
        Assert.IsType<CloseCommand>(pending[3]);
        Assert.IsType<MinimizeCommand>(pending[4]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ClipboardCommandsAreOrderedActions() {
        WindowCommandQueue queue = new();
        queue.EnqueueClipboardWrite("one");
        queue.EnqueueClipboardRead();
        queue.EnqueueClipboardWrite("two");

        IReadOnlyList<WindowCommand> pending = queue.PendingCommands;

        Assert.Equal(3, pending.Count);
        Assert.Equal("one", Assert.IsType<WriteClipboardCommand>(pending[0]).Text);
        Assert.IsType<ReadClipboardCommand>(pending[1]);
        Assert.Equal("two", Assert.IsType<WriteClipboardCommand>(pending[2]).Text);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TryTakeDrainsInOrderAndReportsEmptiness() {
        WindowCommandQueue queue = new();
        queue.EnqueueSetTitle("A");
        queue.EnqueueClose();

        Assert.True(queue.TryTake(out WindowCommand first));
        Assert.IsType<SetTitleCommand>(first);
        Assert.True(queue.TryTake(out WindowCommand second));
        Assert.IsType<CloseCommand>(second);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void SettersValidateArgumentsImmediately() {
        WindowCommandQueue queue = new();

        Assert.Throws<ArgumentNullException>(() => queue.EnqueueSetTitle(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetSize(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetSize(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetSize(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetMinSize(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetMinSize(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetMaxSize(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetMaxSize(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetMonitor(-1));
        Assert.Throws<ArgumentNullException>(() => queue.EnqueueClipboardWrite(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.EnqueueSetCursor((CursorMode)3));

        Assert.Empty(queue.PendingCommands);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroMaximumSizeMeansNoMaximum() {
        WindowCommandQueue queue = new();
        queue.EnqueueSetMaxSize(0, 0);

        SetMaxSizeCommand maximum = Assert.IsType<SetMaxSizeCommand>(Assert.Single(queue.PendingCommands));
        Assert.Equal(0, maximum.Width);
        Assert.Equal(0, maximum.Height);
    }
}
