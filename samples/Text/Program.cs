using RayBork;

namespace Text;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 1280;
    private const int DEFAULT_HEIGHT = 720;
    private const string WINDOW_TITLE = "RayBork Text";
    private const string DEFAULT_PACK_FILE_NAME = "text.pck";

    private const string USAGE_TEXT = """
        Text - world-space text with two fonts

        Draws MSDF text in the built-in Fira Sans face and a baked Lora face: a title in each face,
        one paragraph at three sizes, three alignments, two wrapping modes and two rotations, Latin
        and Cyrillic paragraphs in both faces, a camera-facing billboard, a lazily extended Greek
        glyph line, and a live status line.

        Usage:
          Text [options]

        Options:
          --frames N        Exit after N ticks; 0 runs until the window closes (default: 0).
          --help            Show this help and exit.

        Controls:
          Left mouse drag   Orbit the camera.
          Mouse wheel       Zoom the camera.
          Escape            Exit.
        """;

    internal static int Main(string[] args) {
        if (!TryParseArguments(args, out int frames, out bool showHelp, out string? error)) {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(USAGE_TEXT);
            return EXIT_INVALID_ARGUMENTS;
        }

        if (showHelp) {
            Console.Out.WriteLine(USAGE_TEXT);
            return EXIT_SUCCESS;
        }

        if (frames < 0) {
            Console.Error.WriteLine("--frames must be zero or positive.");
            return EXIT_INVALID_ARGUMENTS;
        }

        string packPath = Path.Combine(AppContext.BaseDirectory, DEFAULT_PACK_FILE_NAME);
        if (!File.Exists(packPath)) {
            Console.Error.WriteLine(
                $"Text failed: the asset pack '{packPath}' does not exist. Build the sample first; "
                    + "the sample build bakes text.pck beside the executable.");
            return EXIT_FAILURE;
        }

        try {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new TextGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithMaxDeltaTime(0.25)
                .WithAssetsPack(packPath)
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Text failed: {exception}");
            return EXIT_FAILURE;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int frames,
        out bool showHelp,
        out string? error) {
        frames = 0;
        showHelp = false;
        error = null;

        for (int index = 0; index < args.Length; index++) {
            switch (args[index]) {
                case "--help":
                    showHelp = true;
                    break;
                case "--frames":
                    if (index + 1 >= args.Length || !int.TryParse(args[++index], out frames)) {
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
