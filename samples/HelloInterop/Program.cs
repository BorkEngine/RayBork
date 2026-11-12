using System.Globalization;
using Microsoft.Extensions.Logging;
using RayBork;

namespace HelloInterop;

internal static class Program {
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;
    private const int EXIT_INVALID_ARGUMENTS = 2;
    private const int DEFAULT_FRAMES = 0;
    private const int DEFAULT_WIDTH = 1280;
    private const int DEFAULT_HEIGHT = 720;
    private const string DEFAULT_TITLE = "HelloInterop";
    private const string DEFAULT_PACK_FILE_NAME = "hello.pck";
    private const LogLevel DEFAULT_LOG_LEVEL = LogLevel.Information;
    private const int DEFAULT_MSAA = 4;
    private const PresentMode DEFAULT_PRESENT_MODE = PresentMode.Mailbox;
    private const TonemappingMode DEFAULT_TONEMAPPING = TonemappingMode.AgX;
    private const float DEFAULT_EXPOSURE = 0f;

    private const string USAGE_TEXT = """
        HelloInterop - RayBork M1-M4 diagnostic host

        Usage:
          HelloInterop [options]

        Options:
          --assets PATH     Mount a baked asset pack and load the sample assets. Relative paths
                            resolve against the current directory. Default: hello.pck beside the
                            executable when it exists (baked by the sample build); otherwise no pack.
          --frames N        Run N ticks, then exit; 0 runs until close (default: 0).
          --title TEXT      Window title (default: HelloInterop).
          --size WxH        Window client size; both values positive (default: 1280x720).
          --msaa N          Requested MSAA sample count; positive (default: 4).
          --present-mode NAME
                            Presentation mode: Mailbox, Fifo, or Immediate, case-insensitive
                            (default: Mailbox; falls back to Fifo when unsupported).
          --tonemapping NAME
                            Tone mapping: AgX or None, case-insensitive (default: AgX).
          --exposure EV     Exposure in stops before tone mapping; finite (default: 0).
          --validation on|off
                            Request the Vulkan validation layer; defaults to the engine default
                            (on in Debug, off in Release).
          --log-level NAME  Minimum log level: Trace, Debug, Information, Warning, Error,
                            Critical, or None (default: Information).
          --help            Show this help and exit.

        HDR presentation is automatic: when the OS/window HDR state is enabled, the renderer selects
        an HDR-capable swapchain format and reports it through EngineContext.Info. There is no HDR
        override.

        With --assets, or with the auto-mounted hello.pck beside the executable, the host mounts the
        baked pack and loads a KTX2 texture, an OBJ mesh part, a compiled shader, and the registered
        HelloSettings TOML data, then logs their metadata. It does not draw; the Castle sample is the
        M4 draw-capable host. Without a pack the host keeps the renderer-only behavior.

        Controls:
          Escape            Request exit.
          Space, Enter, Tab, F1, F11, F12
                            Logged as key presses.
          Mouse             Button edges and the vertical wheel are logged.
          Gamepad           Connect/disconnect and South/East/West/North/shoulder/Start
                            button edges are logged.

        Exit codes:
          0  Success.
          1  The app failed to run.
          2  Invalid command line.

        Examples:
          HelloInterop --frames 120
          HelloInterop --assets samples/HelloInterop/bin/Debug/net11.0/hello.pck --frames 30
          HelloInterop --frames 120 --size 1920x1080 --msaa 8 --title "M4 diagnostic"
          HelloInterop --tonemapping None --exposure 1.5
          HelloInterop --msaa 8 --present-mode Fifo --validation off --log-level Debug
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

        string? assetsPackPath = options.Assets ?? FindDefaultPackPath();
        if (options.Assets is null && assetsPackPath is not null) {
            Console.Out.WriteLine(
                $"HelloInterop: no --assets supplied; mounting the baked pack '{assetsPackPath}' "
                    + "beside the executable.");
        }

        try {
            HelloGame game = new(
                options.Frames,
                options.Tonemapping,
                options.Exposure,
                assetsPackPath);
            RayBorkAppBuilder builder = new RayBorkAppBuilder()
                .WithGame(game)
                .WithTitle(options.Title)
                .WithSize(options.Width, options.Height)
                .WithResizable(true)
                .WithTargetFps(60)
                .WithMaxDeltaTime(0.25)
                .WithMSAA(options.Msaa)
                .WithPresentMode(options.PresentMode)
                .WithTonemapping(options.Tonemapping)
                .WithExposure(options.Exposure)
                .WithLogLevel(options.LogLevel);

            if (options.Validation is { } validationEnabled) {
                builder.WithValidation(validationEnabled);
            }

            if (assetsPackPath is not null) {
                builder
                    .WithAssetsPack(assetsPackPath)
                    .WithAssetType<HelloSettings>();
            }

            using RayBorkApp app = builder.Build();
            app.Run();
            return EXIT_SUCCESS;
        } catch (Exception exception) {
            Console.Error.WriteLine($"HelloInterop failed: {exception}");
            return EXIT_FAILURE;
        }
    }

    private static string? FindDefaultPackPath() {
        string path = Path.Combine(AppContext.BaseDirectory, DEFAULT_PACK_FILE_NAME);
        return File.Exists(path) ? path : null;
    }

    private static bool TryParseCommandLine(
        string[] args,
        out CommandLineOptions options,
        out string error) {
        int frames = DEFAULT_FRAMES;
        string? assets = null;
        string title = DEFAULT_TITLE;
        int width = DEFAULT_WIDTH;
        int height = DEFAULT_HEIGHT;
        LogLevel logLevel = DEFAULT_LOG_LEVEL;
        int msaa = DEFAULT_MSAA;
        PresentMode presentMode = DEFAULT_PRESENT_MODE;
        TonemappingMode tonemapping = DEFAULT_TONEMAPPING;
        float exposure = DEFAULT_EXPOSURE;
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
                case "--title":
                    if (!TryTakeValue(args, ref i, argument, out string titleValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(titleValue)) {
                        error = "Invalid value for --title: the title must not be empty or whitespace.";
                        options = default!;
                        return false;
                    }

                    title = titleValue;
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
                case "--tonemapping":
                    if (!TryTakeValue(args, ref i, argument, out string tonemappingValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!TryParseTonemapping(tonemappingValue, out tonemapping)) {
                        error = $"Invalid value for --tonemapping: '{tonemappingValue}'. Expected AgX or None.";
                        options = default!;
                        return false;
                    }

                    break;
                case "--exposure":
                    if (!TryTakeValue(args, ref i, argument, out string exposureValue, out error)) {
                        options = default!;
                        return false;
                    }

                    if (!float.TryParse(exposureValue, NumberStyles.Float, CultureInfo.InvariantCulture, out exposure)
                        || !float.IsFinite(exposure)) {
                        error = $"Invalid value for --exposure: '{exposureValue}'. Expected a finite number.";
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
            title,
            width,
            height,
            logLevel,
            msaa,
            presentMode,
            tonemapping,
            exposure,
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

    private static bool TryParseTonemapping(string value, out TonemappingMode mode) {
        foreach (string name in Enum.GetNames<TonemappingMode>()) {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) {
                mode = Enum.Parse<TonemappingMode>(name);
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
        string Title,
        int Width,
        int Height,
        LogLevel LogLevel,
        int Msaa,
        PresentMode PresentMode,
        TonemappingMode Tonemapping,
        float Exposure,
        bool? Validation,
        bool ShowHelp);
}
