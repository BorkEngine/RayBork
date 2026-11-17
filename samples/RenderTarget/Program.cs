using RayBork;

namespace RenderTarget;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_WIDTH = 1280;
    private const int DEFAULT_HEIGHT = 720;
    private const string WINDOW_TITLE = "RayBork Render Target";
    private const string DEFAULT_PACK_FILE_NAME = "rendertarget.pck";

    private const string USAGE_TEXT = """
        Render Target - offscreen render target, split-screen, and a custom post effect

        Draws a procedural scene with a rotating cube ring and a monitor. An offscreen camera renders a
        second scene into a 640x360 HDR render texture, and a later scene camera samples that texture on
        the monitor. S splits the scene into two independent cameras with distinct clear colors; E
        toggles the warm vignette post-process on the single scene camera; F2 reads the offscreen target
        back, reports its center color, and saves rendertarget-readback.bmp.

        Usage:
          RenderTarget [options]

        Options:
          --frames N        Exit after N ticks; 0 runs until the window closes (default: 0).
          --help            Show this help and exit.

        Controls:
          S                 Toggle the split-screen scene cameras.
          E                 Toggle the vignette post-process on the single scene camera.
          F2                Read the offscreen target and save the screenshot.
          R                 Reset the procedural rotation and reverse its direction.
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
                $"Render Target failed: the asset pack '{packPath}' does not exist. Build the sample "
                    + "first; the sample build bakes rendertarget.pck beside the executable.");
            return EXIT_FAILURE;
        }

        try {
            using RayBorkApp app = new RayBorkAppBuilder()
                .WithGame(new RenderTargetGame(frames))
                .WithTitle(WINDOW_TITLE)
                .WithSize(DEFAULT_WIDTH, DEFAULT_HEIGHT)
                .WithResizable(true)
                .WithPresentMode(PresentMode.Fifo)
                .WithMaxDeltaTime(0.25)
                .WithAssetsPack(packPath)
                .Build();

            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Render Target failed: {exception}");
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
