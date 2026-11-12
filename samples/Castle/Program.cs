using System.Globalization;
using Microsoft.Extensions.Logging;
using RayBork;

namespace Castle;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_FRAMES = 0;
    private const int DEFAULT_WIDTH = 800;
    private const int DEFAULT_HEIGHT = 450;
    private const string WINDOW_TITLE = "RayBork [models] example - loading";
    private const string DEFAULT_PACK_FILE_NAME = "castle.pck";
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Information;
    private const int DEFAULT_MSAA = 4;
    private const PresentMode DEFAULT_PRESENT_MODE = PresentMode.Mailbox;
    private const int TARGET_FPS = 60;
    private const double MAX_DELTA_TIME = 0.25;

    private const string USAGE_TEXT = """
        Castle - RayBork M4 core 3D renderer sample and Native AOT gate host

        A RayBork recreation of raylib's models_loading example: the castle OBJ and its diffuse
        texture are drawn unlit with the built-in mesh shader and an orbital camera. The raylib
        reference XZ grid moves to M5 with Camera.DrawGrid.

        Usage:
          Castle [options]

        Options:
          --assets PATH     Mount the baked Castle asset pack. A relative path resolves against the
                            current directory (default: castle.pck beside the executable, baked by
                            the sample build).
          --frames N        Run N ticks, then exit; 0 runs until close (default: 0).
          --size WxH        Window client size; both values positive (default: 800x450).
          --msaa N          Requested MSAA sample count; positive (default: 4).
          --present-mode NAME
                            Presentation mode: Mailbox, Fifo, or Immediate, case-insensitive
                            (default: Mailbox; falls back to Fifo when unsupported).
          --validation on|off
                            Request the Vulkan validation layer; defaults to the engine default
                            (on in Debug, off in Release).
          --log-level NAME  Minimum log level: Trace, Debug, Information, Warning, Error,
                            Critical, or None (default: Information).
          --help            Show this help and exit.

        The sample loads /models/obj/castle and /models/obj/castle_diffuse from the pack, binds the
        texture on the model's built-in /shaders/mesh material, and draws the model every tick with a
        perspective camera orbiting the castle. The camera target is (0, 12, 0), the initial position
        is (50, 50, 50), and the field of view is 45 degrees. The built-in mesh shader uses the M4
        ABI: rbFrame.ViewProjection, rbInstance.Transform, rbInstance.Color, uTint, and a bindless
        mainTexture.

        The run is the M4 gate: the sample requires at least one completed frame that reported both
        draw calls and instances. If no completed frame ever draws, shutdown fails with exit 1, so a
        silently invisible scene cannot pass the Native AOT gate while a slow host is judged on
        completed frames instead of a fixed tick number. The final statistics line reports the draw
        calls and instances that were submitted.

        Controls:
          Left mouse drag   Orbit the camera around the castle.
          Mouse wheel       Zoom the camera distance.
          Escape            Request exit.

        Exit codes:
          0  Success.
          1  The app failed to run, including a failed gate assertion.
          2  Invalid command line.

        Examples:
          Castle --frames 30
          Castle --frames 120 --size 1280x720 --msaa 8
          Castle --present-mode Fifo --validation off --log-level Debug
        """;

    internal static int Main(string[] args) {
        if (!TryParseCommandLine(args, out CommandLineOptions options, out string error)) {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(USAGE_TEXT);
            return EXIT_INVALID_ARGUMENTS;
        }

        if (options.ShowHelp) {
            Console.Out.WriteLine(USAGE_TEXT);
            return EXIT_SUCCESS;
        }

        string assetsPath = options.Assets ?? ResolveDefaultPackPath();
        if (!File.Exists(assetsPath)) {
            Console.Error.WriteLine(
                $"Castle failed: the asset pack '{assetsPath}' does not exist. Build the sample first; "
                    + "the sample build bakes castle.pck beside the executable. Pass --assets <path> to "
                    + "mount a different pack.");
            return EXIT_FAILURE;
        }

        try {
            CastleGame game = new(options.Frames);
            RayBorkAppBuilder builder = new RayBorkAppBuilder()
                .WithGame(game)
                .WithTitle(WINDOW_TITLE)
                .WithSize(options.Width, options.Height)
                .WithResizable(true)
                .WithTargetFps(TARGET_FPS)
                .WithMaxDeltaTime(MAX_DELTA_TIME)
                .WithAssetsPack(assetsPath)
                .WithMSAA(options.Msaa)
                .WithPresentMode(options.PresentMode)
                .WithLogLevel(options.LogLevel);

            if (options.Validation is { } validationEnabled) {
                builder.WithValidation(validationEnabled);
            }

            using RayBorkApp app = builder.Build();
            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"Castle failed: {exception}");
            return EXIT_FAILURE;
        }
    }

    private static string ResolveDefaultPackPath()
        => Path.Combine(AppContext.BaseDirectory, DEFAULT_PACK_FILE_NAME);

    private static bool TryParseCommandLine(
        string[] args,
        out CommandLineOptions options,
        out string error) {
        int frames = DEFAULT_FRAMES;
        string? assets = null;
        int width = DEFAULT_WIDTH;
        int height = DEFAULT_HEIGHT;
        LogLevel logLevel = DEFAULT_LOG_LEVEL;
        int msaa = DEFAULT_MSAA;
        PresentMode presentMode = DEFAULT_PRESENT_MODE;
        bool? validation = null;
        bool showHelp = false;
        error = string.Empty;

        for (int i = 0; i < args.Length; i++) {
            string argument = args[i];
            switch (argument) {
                case "--help":
                    showHelp = true;
                    break;
                case "--assets":
                    if (!TryTakeValue(args, ref i, argument, out string assetsValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(assetsValue)) {
                        error = "Invalid value for --assets: the pack path must not be empty or whitespace.";
                        options = default!;
                        return false;
                    }

                    try {
                        assets = Path.GetFullPath(assetsValue);
                    } catch (Exception exception) when (exception is ArgumentException
                        or NotSupportedException or PathTooLongException) {
                        error = $"Invalid value for --assets: '{assetsValue}' is not a valid path.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--frames":
                    if (!TryTakeValue(args, ref i, argument, out string framesValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!int.TryParse(framesValue, NumberStyles.None, CultureInfo.InvariantCulture, out frames)) {
                        error = $"Invalid value for --frames: '{framesValue}'. Expected a non-negative integer.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--size":
                    if (!TryTakeValue(args, ref i, argument, out string sizeValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!TryParseSize(sizeValue, out width, out height)) {
                        error = $"Invalid value for --size: '{sizeValue}'. Expected WxH with both values positive.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--msaa":
                    if (!TryTakeValue(args, ref i, argument, out string msaaValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!int.TryParse(msaaValue, NumberStyles.None, CultureInfo.InvariantCulture, out msaa)
                        || msaa <= 0) {
                        error = $"Invalid value for --msaa: '{msaaValue}'. Expected a positive integer.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--present-mode":
                    if (!TryTakeValue(args, ref i, argument, out string presentModeValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!TryParsePresentMode(presentModeValue, out presentMode)) {
                        error = $"Invalid value for --present-mode: '{presentModeValue}'. Expected Mailbox, Fifo, or Immediate.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--validation":
                    if (!TryTakeValue(args, ref i, argument, out string validationValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!TryParseValidation(validationValue, out bool validationEnabled)) {
                        error = $"Invalid value for --validation: '{validationValue}'. Expected on or off.";
                        options = default!;
                        return false;
                    }

                    validation = validationEnabled;
                    break;
                case "--log-level":
                    if (!TryTakeValue(args, ref i, argument, out string levelValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!TryParseLogLevel(levelValue, out logLevel)) {
                        error = $"Invalid value for --log-level: '{levelValue}'. Expected a defined LogLevel name.";
                        options = default!;
                        return false;
                    }

                    break;
                default:
                    error = $"Unknown argument: '{argument}'.";
                    options = default!;
                    return false;
            }
        }

        options = new CommandLineOptions(
            frames,
            assets,
            width,
            height,
            logLevel,
            msaa,
            presentMode,
            validation,
            showHelp);
        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        string option,
        out string value,
        out string error) {
        if (index + 1 >= args.Length) {
            value = string.Empty;
            error = $"Missing value for {option}.";
            return false;
        }

        index++;
        value = args[index];
        error = string.Empty;
        return true;
    }

    private static bool TryParseSize(string value, out int width, out int height) {
        width = 0;
        height = 0;

        int separator = value.IndexOfAny(['x', 'X']);
        if (separator <= 0
            || separator != value.LastIndexOfAny(['x', 'X'])
            || separator == value.Length - 1) {
            return false;
        }

        return int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && width > 0
            && height > 0;
    }

    private static bool TryParsePresentMode(string value, out PresentMode mode) {
        foreach (string name in Enum.GetNames<PresentMode>()) {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) {
                mode = Enum.Parse<PresentMode>(name);
                return true;
            }
        }

        mode = default;
        return false;
    }

    private static bool TryParseValidation(string value, out bool enabled) {
        if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)) {
            enabled = true;
            return true;
        }

        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private static bool TryParseLogLevel(string value, out LogLevel level) {
        foreach (string name in Enum.GetNames<LogLevel>()) {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) {
                level = Enum.Parse<LogLevel>(name);
                return true;
            }
        }

        level = default;
        return false;
    }

    private sealed record CommandLineOptions(
        int Frames,
        string? Assets,
        int Width,
        int Height,
        LogLevel LogLevel,
        int Msaa,
        PresentMode PresentMode,
        bool? Validation,
        bool ShowHelp);
}
