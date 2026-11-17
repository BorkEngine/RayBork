using RayBork;

namespace Pong;

internal static class Program
{
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 1280;
    private const int DEFAULT_HEIGHT = 720;
    private const string WINDOW_TITLE = "RayBork Pong";
    private const string DEFAULT_PACK_FILE_NAME = "pong.pck";

    private const string USAGE_TEXT = """
        Pong - a player-vs-AI match on an orthographic screen plane

        A 1280x720 playfield is letterboxed into the window and drawn with screen-space quads, discs,
        and text. A scene pass draws the playfield, border, net, paddles, and ball with fast HDR bloom;
        a later HUD pass with ClearMode.Depth draws the scores, status line, and pause/winner overlay
        without bloom. Ball, paddle, and wall hits flash scene-linear HDR colors above the bloom
        threshold and decay over a few frames. The paddle texture and the design values are baked into
        pong.pck; the human moves the left paddle against a deterministic AI right paddle, every paddle
        hit speeds the ball up to a cap, and the first player to MaxScore wins.

        Usage:
          Pong [options]

        Options:
          --frames N        Exit after N ticks; 0 runs until the window closes (default: 0).
          --help            Show this help and exit.

        Controls:
          W / S             Move the left paddle.
          Up / Down         Move the left paddle.
          Gamepad 0         The left stick or d-pad moves the left paddle.
          Space / Start     Pause or resume.
          R / Back          Restart the match.
          B                 Toggle bloom.
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

        string packPath = Path.Combine(AppContext.BaseDirectory, DEFAULT_PACK_FILE_NAME);
        if (!File.Exists(packPath))
        {
            Console.Error.WriteLine(
                $"Pong failed: the asset pack '{packPath}' does not exist. Build the sample first; "
                    + "the sample build bakes pong.pck beside the executable."
            );
            return EXIT_FAILURE;
        }

        try
        {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new PongGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithPresentMode(PresentMode.Fifo)
                .WithMaxDeltaTime(0.25)
                .WithAssetsPack(packPath)
                .WithAssetType<PongSettings>()
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pong failed: {exception}");
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
