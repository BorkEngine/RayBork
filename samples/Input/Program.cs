using RayBork;

namespace Input;

internal static class Program
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 960;
    private const int DEFAULT_HEIGHT = 600;
    private const string WINDOW_TITLE = "RayBork Input";

    private const string USAGE_TEXT = """
        RayBork Input - the M9 text field, clipboard, cursor, window, and gamepad playground

        A single-line text field accepts typed text and committed IME text, shows a blinking caret,
        and places the native text-input area over the field. Panels report window, monitor, DPI,
        focus, cursor, and gamepad state, and the event log records the last ten user-visible
        actions. The sample uses only the engine's built-in pack and default fonts.

        Usage:
          Input [options]

        Options:
          --frames N        Exit after N ticks; 0 runs until the window closes (default: 0).
          --help            Show this help and exit.

        Controls:
          Type / IME        Append committed text to the field.
          Ctrl+C / X / V    Copy, cut, or paste the field text.
          Backspace         Remove the last character (or surrogate pair).
          Delete            Clear the field.
          Alt+Enter         Toggle fullscreen.
          B                 Toggle borderless.
          M                 Move the window to the next monitor.
          C                 Cycle the cursor mode.
          R                 Request a rumble on every connected gamepad.
          Gamepad South     Request a short rumble on that pad.
          Escape            Exit.
        """;

    internal static int Main(string[] args)
    {
        if (!TryParseArguments(args, out int frames, out bool showHelp, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(USAGE_TEXT);
            return EXIT_INVALID_ARGUMENTS;
        }

        if (showHelp)
        {
            Console.Out.WriteLine(USAGE_TEXT);
            return EXIT_SUCCESS;
        }

        if (frames < 0)
        {
            Console.Error.WriteLine("--frames must be zero or positive.");
            return EXIT_INVALID_ARGUMENTS;
        }

        try
        {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new InputGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithPresentMode(PresentMode.Fifo)
                .WithMaxDeltaTime(0.25)
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RayBork Input failed: {exception}");
            return EXIT_FAILURE;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int frames,
        out bool showHelp,
        out string? error
    )
    {
        frames = 0;
        showHelp = false;
        error = null;

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                    showHelp = true;
                    break;
                case "--frames":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out frames))
                    {
                        error = "--frames requires a non-negative integer.";
                        return false;
                    }

                    break;
                default:
                    error = $"Unknown option '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }
}
