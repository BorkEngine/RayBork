using RayBork.Internal;

namespace RayBork.Tests;

public sealed class WindowCommandQueueTests
{
    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepeatedSettersCoalesceInPlaceAndKeepTheirQueuePosition()
    {
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
    public void DifferentSetterKindsDoNotCoalesceWithEachOther()
    {
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
    public void ActionsKeepTheirQueueOrderAndDoNotCoalesce()
    {
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
    public void ClipboardCommandsAreOrderedActions()
    {
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
    public void TryTakeDrainsInOrderAndReportsEmptiness()
    {
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
    public void SettersValidateArgumentsImmediately()
    {
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
    public void ZeroMaximumSizeMeansNoMaximum()
    {
        WindowCommandQueue queue = new();
        queue.EnqueueSetMaxSize(0, 0);

        SetMaxSizeCommand maximum = Assert.IsType<SetMaxSizeCommand>(
            Assert.Single(queue.PendingCommands)
        );
        Assert.Equal(0, maximum.Width);
        Assert.Equal(0, maximum.Height);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void RepeatedTextInputAreaCallsCoalesceIntoOneCommand()
    {
        WindowCommandQueue queue = new();
        queue.EnqueueSetTextInputArea(new Rect(1.2f, 2.6f, 100.4f, 40.6f), 3);
        queue.EnqueueMinimize();
        queue.EnqueueSetTextInputArea(new Rect(10.4f, 20.6f, 300.49f, 50.51f), 7);

        IReadOnlyList<WindowCommand> pending = queue.PendingCommands;

        Assert.Equal(2, pending.Count);
        SetTextInputAreaCommand area = Assert.IsType<SetTextInputAreaCommand>(pending[0]);
        Assert.Equal(10, area.X);
        Assert.Equal(21, area.Y);
        Assert.Equal(300, area.Width);
        Assert.Equal(51, area.Height);
        Assert.Equal(7, area.CursorOffset);
        Assert.IsType<MinimizeCommand>(pending[1]);
    }

    [Fact]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void TextInputAreaValidationRunsBeforeTheCommandEntersTheQueue()
    {
        WindowCommandQueue queue = new();

        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(float.NaN, 0f, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, float.PositiveInfinity, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, float.NaN, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, 10f, float.NegativeInfinity), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(3e9f, 0f, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, -3e9f, 10f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, 3e9f, 10f), 0)
        );
        Assert.Throws<ArgumentException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, 10f, 3e9f), 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, -1f, 10f), 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, 10f, -1f), 0)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.EnqueueSetTextInputArea(new Rect(0f, 0f, 10f, 10f), -1)
        );

        Assert.Empty(queue.PendingCommands);
    }

    [Theory]
    [InlineData(0f, 40f)]
    [InlineData(100f, 0f)]
    [InlineData(0.4f, 40f)]
    [Trait(TestCategories.TRAIT_NAME, TestCategories.UNIT)]
    public void ZeroTextInputAreaExtentIsAValidClear(float width, float height)
    {
        WindowCommandQueue queue = new();
        queue.EnqueueSetTextInputArea(new Rect(5f, 6f, width, height), 0);

        SetTextInputAreaCommand area = Assert.IsType<SetTextInputAreaCommand>(
            Assert.Single(queue.PendingCommands)
        );
        Assert.Equal(5, area.X);
        Assert.Equal(6, area.Y);
        Assert.True(area.Width == 0 || area.Height == 0);
    }
}
