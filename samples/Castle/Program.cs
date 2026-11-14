using RayBork;

namespace Castle;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 800;
    private const int DEFAULT_HEIGHT = 450;
    private const string WINDOW_TITLE = "RayBork Castle";
    private const string DEFAULT_PACK_FILE_NAME = "castle.pck";

    private const string USAGE_TEXT = """
        Castle - an orbitable castle scene

        A castle model and its diffuse texture, drawn solid with the built-in mesh material and an
        orbital perspective camera. Hovering the castle outlines its bounds and marks the nearest
        surface point under the pointer.

        Usage:
          Castle [options]

        Options:
          --assets PATH     Mount a different baked asset pack (default: castle.pck beside the
                            executable).
          --frames N        Exit after N ticks; 0 runs until the window closes (default: 0).
          --help            Show this help and exit.

        Controls:
          Left mouse drag   Orbit the camera.
          Mouse wheel       Zoom the camera.
          Mouse hover       Outline the castle and mark the surface point under the pointer.
          Escape            Exit.
        """;

    internal static int Main(string[] args) {
        if (!TryParseArguments(args, out int frames, out string? assetsPath, out bool showHelp, out string? error)) {
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

        string packPath = assetsPath ?? Path.Combine(AppContext.BaseDirectory, DEFAULT_PACK_FILE_NAME);
        if (!File.Exists(packPath)) {
            Console.Error.WriteLine(
                $"Castle failed: the asset pack '{packPath}' does not exist. Build the sample first; "
                    + "the sample build bakes castle.pck beside the executable, or pass --assets PATH.");
            return EXIT_FAILURE;
        }

        try {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new CastleGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithTargetFps(60)
                .WithMaxDeltaTime(0.25)
                .WithAssetsPack(packPath)
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Castle failed: {exception}");
            return EXIT_FAILURE;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int frames,
        out string? assetsPath,
        out bool showHelp,
        out string? error) {
        frames = 0;
        assetsPath = null;
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
                case "--assets":
                    if (index + 1 >= args.Length) {
                        error = "--assets requires a path.";
                        return false;
                    }

                    assetsPath = args[++index];
                    break;
                default:
                    error = $"Unknown option '{args[index]}'.";
                    return false;
            }
        }

        return true;
    }
}
