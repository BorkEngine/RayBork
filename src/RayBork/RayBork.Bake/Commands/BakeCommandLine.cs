using RayBork.Bake.Baking;
using RayBork.Bake.Diagnostics;
using RayBork.Bake.Incremental;
using RayBork.Bake.Paths;
using RayBork.Bake.Sources;
using RayBork.Internal;

namespace RayBork.Bake.Commands;

/// <summary>
/// The command surface shared by every assets executable: argument parsing, configuration validation,
/// dispatch, exit codes, and stdout/stderr routing.
/// </summary>
internal static class BakeCommandLine {
    internal const int SUCCESS_EXIT_CODE = 0;
    internal const int DIAGNOSTICS_EXIT_CODE = 1;
    internal const int USAGE_EXIT_CODE = 2;
    internal const int INTERNAL_EXIT_CODE = 3;

    private static readonly string[] _commands = ["bake", "clean", "inspect"];

    /// <summary>Runs one command.</summary>
    /// <param name="definition">The builder configuration.</param>
    /// <param name="args">The arguments, excluding the executable name.</param>
    /// <param name="output">The summary stream.</param>
    /// <param name="error">The diagnostic stream.</param>
    /// <param name="programName">The CLI prefix.</param>
    /// <returns>The documented exit code.</returns>
    internal static int Run(
        BakeProjectDefinition definition,
        string[] args,
        TextWriter output,
        TextWriter error,
        string programName) {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(programName);

        bool verboseRequested = Array.IndexOf(args, "--verbose") >= 0;
        try {
            if (args.Length == 0) {
                error.WriteLine($"{programName}: a command is required.");
                WriteUsage(error, programName);
                return USAGE_EXIT_CODE;
            }

            switch (args[0]) {
                case "--help":
                case "-h":
                    WriteUsage(output, programName);
                    return SUCCESS_EXIT_CODE;

                case "--version":
                    output.WriteLine(
                        $"{programName} {definition.ToolVersionOverride ?? BakeToolInfo.Version}");
                    return SUCCESS_EXIT_CODE;

                case "bake":
                    return RunBake(definition, args.AsSpan(1), output, error, programName);

                case "clean":
                    return RunClean(definition, args.AsSpan(1), output, error, programName);

                case "inspect":
                    return RunInspect(definition, args.AsSpan(1), output, error, programName);

                default:
                    error.WriteLine($"{programName}: unknown command '{args[0]}'.");
                    WriteUsage(error, programName);
                    return USAGE_EXIT_CODE;
            }
        } catch (Exception exception) when (exception is not OutOfMemoryException) {
            error.WriteLine(verboseRequested
                ? $"{programName}: internal error: {exception}"
                : $"{programName}: internal error: {exception.Message}");
            return INTERNAL_EXIT_CODE;
        }
    }

    private static int RunBake(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName) {
        if (!TryParse(args, CommandKind.Bake, out CommandOptions options, out string? parseError)) {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return USAGE_EXIT_CODE;
        }

        if (options.Help) {
            WriteUsage(output, programName);
            return SUCCESS_EXIT_CODE;
        }

        if (!TryResolveInvocation(options, error, programName, out string? projectRoot, out string outputRoot)) {
            return USAGE_EXIT_CODE;
        }

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, projectRoot, diagnostics, out BakeProject? project)
            || project is null) {
            WriteDiagnostics(error, diagnostics, options.Verbose);
            if (!options.Quiet) {
                output.WriteLine(
                    $"{programName}: the bake configuration is invalid "
                        + $"({CountErrors(diagnostics)} error(s)).");
            }

            return USAGE_EXIT_CODE;
        }

        BakeSourceSet sources = BakeSourceDiscovery.Discover(project, diagnostics);
        var bakeOptions = new BakeOptions(
            outputRoot,
            options.Mode,
            options.Check,
            options.Force,
            options.WarningsAsErrors);
        BakeResult result = BakePipeline.Run(project, bakeOptions, sources, diagnostics);
        WriteDiagnostics(error, result.Diagnostics, options.Verbose);

        if (result.Status is BakeStatus.Success or BakeStatus.UpToDate) {
            if (!options.Quiet) {
                WriteSuccessSummary(output, programName, result, options);
            }

            if (options.Verbose) {
                WriteEntryList(output, result.Entries);
            }

            return SUCCESS_EXIT_CODE;
        }

        int failures = CountFailures(result.Diagnostics, options.WarningsAsErrors);
        if (!options.Quiet) {
            output.WriteLine(
                $"{programName}: the bake has {failures} error(s); no pack was written.");
        }

        return result.Status == BakeStatus.ConfigurationError
            ? USAGE_EXIT_CODE
            : DIAGNOSTICS_EXIT_CODE;
    }

    private static int RunClean(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName) {
        if (!TryParse(args, CommandKind.Clean, out CommandOptions options, out string? parseError)) {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return USAGE_EXIT_CODE;
        }

        if (options.Help) {
            WriteUsage(output, programName);
            return SUCCESS_EXIT_CODE;
        }

        if (!TryResolveInvocation(options, error, programName, out string? projectRoot, out string outputRoot)) {
            return USAGE_EXIT_CODE;
        }

        var diagnostics = new BakeDiagnostics();
        if (!BakeProjectFactory.TryCreate(definition, projectRoot, diagnostics, out BakeProject? project)
            || project is null) {
            WriteDiagnostics(error, diagnostics, options.Verbose);
            if (!options.Quiet) {
                output.WriteLine($"{programName}: the bake configuration is invalid.");
            }

            return USAGE_EXIT_CODE;
        }

        IReadOnlyList<BakePackOutput> outputs;
        try {
            outputs = BakeOutputLayout.Resolve(project, BakeOutputLayout.ResolveRoot(outputRoot));
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            error.WriteLine($"{programName}: the output root is invalid: {exception.Message}");
            return USAGE_EXIT_CODE;
        }

        int deleted = 0;
        foreach (BakePackOutput packOutput in outputs) {
            try {
                if (File.Exists(packOutput.Path)) {
                    File.Delete(packOutput.Path);
                    deleted++;
                }
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.PACK_WRITE_FAILED,
                    $"Failed to delete the pack '{packOutput.Path}': {exception.Message}",
                    packOutput.Path);
            }
        }

        string stateDirectory = BakeManifestStore.Directory(project.ProjectRoot);
        bool hadState = Directory.Exists(stateDirectory);
        if (hadState) {
            try {
                Directory.Delete(stateDirectory, recursive: true);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or NotSupportedException or ArgumentException) {
                diagnostics.Add(
                    BakeSeverity.Error,
                    BakeDiagnosticCodes.INCREMENTAL_FAILED,
                    $"Failed to delete the incremental manifest directory '{stateDirectory}': "
                        + exception.Message,
                    stateDirectory);
            }
        }

        WriteDiagnostics(error, diagnostics, options.Verbose);
        if (diagnostics.HasErrors) {
            if (!options.Quiet) {
                output.WriteLine($"{programName}: failed to clean every configured pack.");
            }

            return DIAGNOSTICS_EXIT_CODE;
        }

        if (!options.Quiet) {
            string state = hadState ? "and the incremental manifest" : "(no incremental manifest was present)";
            output.WriteLine(
                $"{programName}: cleaned {deleted} pack(s) under "
                    + $"'{Path.GetFullPath(outputRoot)}' {state}.");
        }

        return SUCCESS_EXIT_CODE;
    }

    private static int RunInspect(
        BakeProjectDefinition definition,
        ReadOnlySpan<string> args,
        TextWriter output,
        TextWriter error,
        string programName) {
        if (!TryParse(args, CommandKind.Inspect, out CommandOptions options, out string? parseError)) {
            error.WriteLine($"{programName}: {parseError}");
            WriteUsage(error, programName);
            return USAGE_EXIT_CODE;
        }

        if (options.Help) {
            WriteUsage(output, programName);
            return SUCCESS_EXIT_CODE;
        }

        if (!TryResolveInvocation(options, error, programName, out string? projectRoot, out string outputRoot)) {
            return USAGE_EXIT_CODE;
        }

        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Pack)) {
            if (!BakePath.TryNormalizeRelative(options.Pack, out string normalized, out string? reason)
                || !BakePath.TryResolveUnderRoot(outputRoot, normalized, out string fullPath)) {
                error.WriteLine(
                    $"{programName}: invalid --pack '{options.Pack}': {reason ?? "it escapes the output root"}.");
                return USAGE_EXIT_CODE;
            }

            paths.Add(fullPath);
        } else {
            var diagnostics = new BakeDiagnostics();
            if (!BakeProjectFactory.TryCreate(
                definition,
                projectRoot,
                diagnostics,
                out BakeProject? project)
                || project is null) {
                WriteDiagnostics(error, diagnostics, options.Verbose);
                if (!options.Quiet) {
                    output.WriteLine($"{programName}: the bake configuration is invalid.");
                }

                return USAGE_EXIT_CODE;
            }

            try {
                paths.AddRange(BakeOutputLayout
                    .Resolve(project, BakeOutputLayout.ResolveRoot(outputRoot))
                    .Select(packOutput => packOutput.Path));
            } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                or PathTooLongException) {
                error.WriteLine($"{programName}: the output root is invalid: {exception.Message}");
                return USAGE_EXIT_CODE;
            }
        }

        int packCount = 0;
        int entryCount = 0;
        foreach (string path in paths) {
            try {
                using PackArchive archive = PackArchive.Open(path);
                packCount++;
                entryCount += archive.Entries.Count;
                if (!options.Quiet) {
                    output.WriteLine($"{path}:");
                    foreach (PackEntry entry in archive.Entries) {
                        output.WriteLine(
                            $"  {entry.Path} -> {entry.Type} ({entry.UncompressedSize} bytes, "
                                + $"alignment {entry.Alignment})");
                    }
                }
            } catch (RayBorkException exception) {
                error.WriteLine(
                    $"{path}: error {exception.DiagnosticCode ?? DiagnosticCodes.ASSET_PACK_READ_FAILED}: "
                        + exception.Message);
                return DIAGNOSTICS_EXIT_CODE;
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException) {
                error.WriteLine($"{path}: error {DiagnosticCodes.ASSET_PACK_READ_FAILED}: {exception.Message}");
                return DIAGNOSTICS_EXIT_CODE;
            }
        }

        if (!options.Quiet) {
            output.WriteLine(
                $"{programName}: inspected {packCount} pack(s), {entryCount} asset(s).");
        }

        return SUCCESS_EXIT_CODE;
    }

    private static bool TryResolveInvocation(
        CommandOptions options,
        TextWriter error,
        string programName,
        out string? projectRoot,
        out string outputRoot) {
        projectRoot = null;
        outputRoot = string.Empty;

        if (!string.IsNullOrWhiteSpace(options.ProjectRoot)) {
            try {
                projectRoot = Path.GetFullPath(options.ProjectRoot);
            } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
                or PathTooLongException) {
                error.WriteLine(
                    $"{programName}: --project-root '{options.ProjectRoot}' is not a valid path: "
                        + exception.Message);
                return false;
            }
        }

        string requestedOutput = string.IsNullOrWhiteSpace(options.OutputRoot)
            ? AppContext.BaseDirectory
            : options.OutputRoot;
        try {
            outputRoot = Path.GetFullPath(requestedOutput);
            return true;
        } catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException) {
            error.WriteLine(
                $"{programName}: --output-root '{requestedOutput}' is not a valid path: "
                    + exception.Message);
            return false;
        }
    }

    private static bool TryParse(
        ReadOnlySpan<string> args,
        CommandKind kind,
        out CommandOptions options,
        out string? error) {
        options = new CommandOptions();
        error = null;

        string command = _commands[(int)kind];
        for (int index = 0; index < args.Length; index++) {
            string argument = args[index];
            switch (argument) {
                case "--help":
                case "-h":
                    options.Help = true;
                    break;

                case "--project-root":
                    if (!TryTakeValue(args, ref index, command, argument, out string? projectRoot, out error)) {
                        return false;
                    }

                    if (options.ProjectRoot is not null) {
                        error = $"{command} accepts --project-root only once.";
                        return false;
                    }

                    options.ProjectRoot = projectRoot;
                    break;

                case "--output-root":
                    if (!TryTakeValue(args, ref index, command, argument, out string? outputRoot, out error)) {
                        return false;
                    }

                    if (options.OutputRoot is not null) {
                        error = $"{command} accepts --output-root only once.";
                        return false;
                    }

                    options.OutputRoot = outputRoot;
                    break;

                case "--mode" when kind == CommandKind.Bake:
                    if (!TryTakeValue(args, ref index, command, argument, out string? mode, out error)) {
                        return false;
                    }

                    if (!BakeModeExtensions.TryParse(mode, out BakeMode parsed)) {
                        error = $"--mode must be 'debug' or 'release'; got '{mode}'.";
                        return false;
                    }

                    options.Mode = parsed;
                    break;

                case "--check" when kind == CommandKind.Bake:
                    options.Check = true;
                    break;

                case "--force" when kind == CommandKind.Bake:
                    options.Force = true;
                    break;

                case "--warnings-as-errors" when kind == CommandKind.Bake:
                    options.WarningsAsErrors = true;
                    break;

                case "--pack" when kind == CommandKind.Inspect:
                    if (!TryTakeValue(args, ref index, command, argument, out string? pack, out error)) {
                        return false;
                    }

                    if (options.Pack is not null) {
                        error = "inspect accepts --pack only once.";
                        return false;
                    }

                    options.Pack = pack;
                    break;

                case "--verbose":
                    options.Verbose = true;
                    break;

                case "--quiet":
                    options.Quiet = true;
                    break;

                default:
                    error = $"{command} does not accept '{argument}'.";
                    return false;
            }
        }

        if (options.Verbose && options.Quiet) {
            error = "--verbose and --quiet are mutually exclusive.";
            return false;
        }

        return true;
    }

    private static bool TryTakeValue(
        ReadOnlySpan<string> args,
        ref int index,
        string command,
        string optionName,
        out string? value,
        out string? error) {
        value = null;
        error = null;
        if (index + 1 >= args.Length) {
            error = $"{command} requires a value after {optionName}.";
            return false;
        }

        value = args[++index];
        if (string.IsNullOrWhiteSpace(value)) {
            error = $"{command} requires a non-empty value after {optionName}.";
            return false;
        }

        return true;
    }

    private static void WriteSuccessSummary(
        TextWriter output,
        string programName,
        BakeResult result,
        CommandOptions options) {
        string packs = string.Join(
            ", ",
            result.Packs.Select(pack => $"'{pack.Pack.PackPath}'"));
        if (result.Status == BakeStatus.UpToDate) {
            output.WriteLine($"{programName}: up to date: {packs}; no inputs changed.");
            return;
        }

        if (options.Check) {
            output.WriteLine(
                $"{programName}: valid (--check): {packs} ({result.Packs.Count} pack(s), "
                    + $"{result.AssetCount} asset(s), {result.RawCount} raw payload(s)); no pack written.");
            return;
        }

        output.WriteLine(
            $"{programName}: baked {packs} ({result.Packs.Count} pack(s), {result.AssetCount} "
                + $"asset(s), {result.RawCount} raw payload(s), {options.Mode.ToName()} mode).");
    }

    private static void WriteEntryList(TextWriter output, IReadOnlyList<BakedEntry> entries) {
        foreach (BakedEntry entry in entries) {
            output.WriteLine($"  {entry.Path} -> {entry.Type} ({entry.Payload.Length} bytes)");
        }
    }

    private static void WriteDiagnostics(
        TextWriter error,
        BakeDiagnostics diagnostics,
        bool includeInfo)
        => WriteDiagnostics(error, diagnostics.GetSorted(), includeInfo);

    private static void WriteDiagnostics(
        TextWriter error,
        IReadOnlyList<BakeDiagnostic> diagnostics,
        bool includeInfo) {
        foreach (BakeDiagnostic diagnostic in diagnostics) {
            if (diagnostic.Severity == BakeSeverity.Info && !includeInfo) {
                continue;
            }

            error.WriteLine(diagnostic.Format());
        }
    }

    private static int CountErrors(BakeDiagnostics diagnostics) {
        int count = 0;
        foreach (BakeDiagnostic diagnostic in diagnostics.GetSorted()) {
            if (diagnostic.Severity == BakeSeverity.Error) {
                count++;
            }
        }

        return count;
    }

    private static int CountFailures(
        IReadOnlyList<BakeDiagnostic> diagnostics,
        bool warningsAsErrors) {
        int count = 0;
        foreach (BakeDiagnostic diagnostic in diagnostics) {
            if (diagnostic.Severity == BakeSeverity.Error
                || (warningsAsErrors && diagnostic.Severity == BakeSeverity.Warning)) {
                count++;
            }
        }

        return count;
    }

    private static void WriteUsage(TextWriter writer, string programName) {
        writer.WriteLine($"usage: {programName} <command> [options]");
        writer.WriteLine("  --help       show this help");
        writer.WriteLine("  --version    show the tool version");
        writer.WriteLine("  bake      [--project-root <dir>] [--output-root <dir>] [--mode debug|release]");
        writer.WriteLine("            [--check] [--force] [--warnings-as-errors] [--verbose|--quiet]");
        writer.WriteLine("            discover, bind, process, validate, and atomically write every pack");
        writer.WriteLine("  clean     [--project-root <dir>] [--output-root <dir>] [--verbose|--quiet]");
        writer.WriteLine("            delete every configured pack and the incremental manifest");
        writer.WriteLine("  inspect   [--project-root <dir>] [--output-root <dir>] [--pack <path>]");
        writer.WriteLine("            [--verbose|--quiet]");
        writer.WriteLine("            list the entries of the configured packs or one --pack file");
        writer.WriteLine("exit codes: 0 success, 1 source diagnostics, 2 usage/configuration, 3 internal");
    }

    private enum CommandKind {
        Bake,
        Clean,
        Inspect,
    }

    private sealed class CommandOptions {
        internal string? ProjectRoot { get; set; }

        internal string? OutputRoot { get; set; }

        internal string? Pack { get; set; }

        internal BakeMode Mode { get; set; } = BakeMode.Debug;

        internal bool Check { get; set; }

        internal bool Force { get; set; }

        internal bool WarningsAsErrors { get; set; }

        internal bool Verbose { get; set; }

        internal bool Quiet { get; set; }

        internal bool Help { get; set; }
    }
}
