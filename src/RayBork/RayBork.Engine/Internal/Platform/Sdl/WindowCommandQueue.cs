using System.Collections.ObjectModel;

namespace RayBork.Internal;

/// <summary>
/// The kind of a queued window command. Commands of the same setter kind coalesce in place.
/// </summary>
internal enum WindowCommandKind {
    SetTitle,
    SetSize,
    SetPosition,
    SetMonitor,
    SetMinSize,
    SetMaxSize,
    SetFullscreen,
    SetBorderless,
    SetCursor,
    Minimize,
    Maximize,
    Restore,
    WriteClipboard,
    ReadClipboard,
    Close,
}

/// <summary>
/// A queued main-thread window command. Setters coalesce with the last pending command of the same
/// kind; actions keep their queue order.
/// </summary>
internal abstract record WindowCommand {
    internal WindowCommandKind Kind => this switch {
        SetTitleCommand => WindowCommandKind.SetTitle,
        SetSizeCommand => WindowCommandKind.SetSize,
        SetPositionCommand => WindowCommandKind.SetPosition,
        SetMonitorCommand => WindowCommandKind.SetMonitor,
        SetMinSizeCommand => WindowCommandKind.SetMinSize,
        SetMaxSizeCommand => WindowCommandKind.SetMaxSize,
        SetFullscreenCommand => WindowCommandKind.SetFullscreen,
        SetBorderlessCommand => WindowCommandKind.SetBorderless,
        SetCursorCommand => WindowCommandKind.SetCursor,
        MinimizeCommand => WindowCommandKind.Minimize,
        MaximizeCommand => WindowCommandKind.Maximize,
        RestoreCommand => WindowCommandKind.Restore,
        WriteClipboardCommand => WindowCommandKind.WriteClipboard,
        ReadClipboardCommand => WindowCommandKind.ReadClipboard,
        CloseCommand => WindowCommandKind.Close,
        _ => throw new InvalidOperationException($"Unknown window command type {GetType().Name}."),
    };

    internal bool IsSetter => Kind is WindowCommandKind.SetTitle
        or WindowCommandKind.SetSize
        or WindowCommandKind.SetPosition
        or WindowCommandKind.SetMonitor
        or WindowCommandKind.SetMinSize
        or WindowCommandKind.SetMaxSize
        or WindowCommandKind.SetFullscreen
        or WindowCommandKind.SetBorderless
        or WindowCommandKind.SetCursor;
}

internal sealed record SetTitleCommand(string Title) : WindowCommand;

internal sealed record SetSizeCommand(int Width, int Height) : WindowCommand;

internal sealed record SetPositionCommand(int X, int Y) : WindowCommand;

internal sealed record SetMonitorCommand(int MonitorIndex) : WindowCommand;

internal sealed record SetMinSizeCommand(int Width, int Height) : WindowCommand;

internal sealed record SetMaxSizeCommand(int Width, int Height) : WindowCommand;

internal sealed record SetFullscreenCommand(bool Fullscreen) : WindowCommand;

internal sealed record SetBorderlessCommand(bool Borderless) : WindowCommand;

internal sealed record SetCursorCommand(CursorMode Mode) : WindowCommand;

internal sealed record MinimizeCommand : WindowCommand;

internal sealed record MaximizeCommand : WindowCommand;

internal sealed record RestoreCommand : WindowCommand;

internal sealed record WriteClipboardCommand(string Text) : WindowCommand;

internal sealed record ReadClipboardCommand : WindowCommand;

internal sealed record CloseCommand : WindowCommand;

/// <summary>
/// Thread-safe queue for the Any-thread <see cref="IWindow"/> setters. Validation happens on the
/// calling thread before a command enters the queue; the main thread drains the queue in
/// <see cref="SdlWindow.ApplyPendingCommands"/>.
/// </summary>
internal sealed class WindowCommandQueue {
    private readonly Lock _gate = new();
    private readonly List<WindowCommand> _commands = [];

    internal bool HasPendingCommands {
        get {
            lock (_gate) {
                return _commands.Count > 0;
            }
        }
    }

    internal void EnqueueSetTitle(string title) {
        ArgumentNullException.ThrowIfNull(title);
        Enqueue(new SetTitleCommand(title));
    }

    internal void EnqueueSetSize(int width, int height) {
        if (width <= 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The window width must be greater than zero.");
        }

        if (height <= 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The window height must be greater than zero.");
        }

        Enqueue(new SetSizeCommand(width, height));
    }

    internal void EnqueueSetPosition(int x, int y) => Enqueue(new SetPositionCommand(x, y));

    internal void EnqueueSetMonitor(int monitorIndex) {
        if (monitorIndex < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(monitorIndex),
                monitorIndex,
                "The monitor index must be non-negative.");
        }

        Enqueue(new SetMonitorCommand(monitorIndex));
    }

    internal void EnqueueSetMinSize(int width, int height) {
        if (width < 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The minimum width must be non-negative.");
        }

        if (height < 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The minimum height must be non-negative.");
        }

        Enqueue(new SetMinSizeCommand(width, height));
    }

    internal void EnqueueSetMaxSize(int width, int height) {
        if (width < 0) {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The maximum width must be non-negative.");
        }

        if (height < 0) {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The maximum height must be non-negative.");
        }

        Enqueue(new SetMaxSizeCommand(width, height));
    }

    internal void EnqueueSetFullscreen(bool fullscreen) => Enqueue(new SetFullscreenCommand(fullscreen));

    internal void EnqueueSetBorderless(bool borderless) => Enqueue(new SetBorderlessCommand(borderless));

    internal void EnqueueSetCursor(CursorMode mode) {
        InputMappings.ValidateCursorMode(mode);
        Enqueue(new SetCursorCommand(mode));
    }

    internal void EnqueueMinimize() => Enqueue(new MinimizeCommand());

    internal void EnqueueMaximize() => Enqueue(new MaximizeCommand());

    internal void EnqueueRestore() => Enqueue(new RestoreCommand());

    internal void EnqueueClipboardWrite(string text) {
        ArgumentNullException.ThrowIfNull(text);
        Enqueue(new WriteClipboardCommand(text));
    }

    internal void EnqueueClipboardRead() => Enqueue(new ReadClipboardCommand());

    internal void EnqueueClose() => Enqueue(new CloseCommand());

    /// <summary>
    /// Takes the oldest pending command for application on the main thread.
    /// </summary>
    internal bool TryTake(out WindowCommand command) {
        lock (_gate) {
            if (_commands.Count == 0) {
                command = null!;
                return false;
            }

            command = _commands[0];
            _commands.RemoveAt(0);
            return true;
        }
    }

    /// <summary>
    /// Gets a read-only snapshot of the pending commands. Intended for diagnostics and tests.
    /// </summary>
    internal IReadOnlyList<WindowCommand> PendingCommands {
        get {
            lock (_gate) {
                return new ReadOnlyCollection<WindowCommand>(_commands.ToArray());
            }
        }
    }

    private void Enqueue(WindowCommand command) {
        lock (_gate) {
            if (command.IsSetter) {
                for (int i = _commands.Count - 1; i >= 0; i--) {
                    if (_commands[i].Kind == command.Kind) {
                        _commands[i] = command;
                        return;
                    }
                }
            }

            _commands.Add(command);
        }
    }
}
