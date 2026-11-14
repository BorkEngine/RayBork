using RayBork;

namespace PrimitiveGallery;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 800;
    private const int DEFAULT_HEIGHT = 450;
    private const string WINDOW_TITLE = "RayBork Primitive Gallery";

    private const string USAGE_TEXT = """
        Primitive Gallery - every M5 primitive helper in one camera pass

        Draws the solid and wire shape families (cube, sphere, cylinder, cone, capsule, plane,
        circle, triangle, triangle strip, point, line, polyline, ray, and bounding box), a
        reference grid, and two textured billboards with the built-in primitive shader and an
        orbital perspective camera.

        Usage:
          PrimitiveGallery [options]

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

        try {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new GalleryGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithTargetFps(60)
                .WithMaxDeltaTime(0.25)
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Primitive Gallery failed: {exception}");
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
